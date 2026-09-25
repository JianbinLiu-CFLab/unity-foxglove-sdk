// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: Per-client WebSocket connection state, send loop, lifecycle, and
// transport statistics.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Per-connection framing layer: send/receive WebSocket frames over a single TCP stream.
    /// Outbound writes are serialized by the per-connection send loop.
    /// </summary>
    internal sealed class WsConnection : IDisposable
    {
        private const int MaxSendBatchFrames = 32;
        private const int DisposeWaitTimeoutMs = 5000;
        private static readonly long StopwatchTicksPerMillisecond = Math.Max(1L, Stopwatch.Frequency / 1000L);

        /// <summary>Underlying TCP client owned by this connection after handshake.</summary>
        private readonly TcpClient _tcpClient;
        /// <summary>Underlying plain or TLS stream.</summary>
        private readonly Stream _stream;
        private readonly WsSendQueue _sendQueue;
        private readonly List<QueuedFrame> _sendBatch = new(MaxSendBatchFrames);
        private CancellationTokenSource _sendCts;
        private Task _sendTask;
        private CancellationTokenSource _livenessCts;
        private Task _livenessTask;
        private int _disposed;
        private readonly int _maxInboundFrameBytes;
        private int _sendLoopThreadId;
        private int _livenessThreadId;

        /// <summary>RFC 6455 opcode for text frames.</summary>
        private const byte OpText = 0x1;
        /// <summary>RFC 6455 opcode for binary frames.</summary>
        private const byte OpBinary = 0x2;
        /// <summary>RFC 6455 opcode for ping frames.</summary>
        private const byte OpPing = 0x9;
        /// <summary>RFC 6455 opcode for close frames.</summary>
        private const byte OpClose = 0x8;
        /// <summary>RFC 6455 opcode for pong frames.</summary>
        private const byte OpPong = 0xA;

        // Health counters
        private readonly DateTime _connectedAtUtc = DateTime.UtcNow;
        private readonly long _connectedAtMs;
        private long _lastActivityMs;
        private long _lastInboundActivityMs;
        private long _pingSentAtMs;
        private int _pingOutstanding;
        private long _sentFrames;
        private long _sentBytes;

        /// <summary>Create a connection on the given network stream.</summary>
        public WsConnection(TcpClient tcpClient, Stream stream, int maxQueuedFrames, int maxQueuedBytes, int maxInboundFrameBytes = WsFrameCodec.MaxPayloadBytes)
        {
            _tcpClient = tcpClient;
            _stream = stream;
            _sendQueue = new WsSendQueue(maxQueuedFrames, maxQueuedBytes);
            _maxInboundFrameBytes = ManagedWebSocketOptions.NormalizeMaxInboundFrameBytes(maxInboundFrameBytes);
            _connectedAtMs = MonotonicMilliseconds();
            _lastActivityMs = _connectedAtMs;
            _lastInboundActivityMs = _connectedAtMs;
        }

        public long DroppedDataFrames => _sendQueue.DroppedDataFramesSnapshot;

        public TransportClientStats GetClientStats(uint clientId)
        {
            var snap = _sendQueue.GetSnapshot();
            var nowMs = MonotonicMilliseconds();
            return new TransportClientStats
            {
                ClientId = clientId,
                ConnectedAtUtc = _connectedAtUtc,
                ConnectedDurationMs = Math.Max(0L, nowMs - _connectedAtMs),
                LastActivityAgeMs = nowMs - Interlocked.Read(ref _lastActivityMs),
                QueuedFrames = snap.QueuedFrames,
                QueuedControlFrames = snap.QueuedControlFrames,
                QueuedDataFrames = snap.QueuedDataFrames,
                QueuedBytes = snap.QueuedBytes,
                DroppedDataFrames = snap.DroppedDataFrames,
                SentFrames = Interlocked.Read(ref _sentFrames),
                SentBytes = Interlocked.Read(ref _sentBytes)
            };
        }

        internal void TouchActivity() => Interlocked.Exchange(ref _lastActivityMs, MonotonicMilliseconds());

        internal void TouchInboundActivity()
        {
            var nowMs = MonotonicMilliseconds();
            Interlocked.Exchange(ref _lastActivityMs, nowMs);
            Interlocked.Exchange(ref _lastInboundActivityMs, nowMs);
            Interlocked.Exchange(ref _pingOutstanding, 0);
        }

        private static long MonotonicMilliseconds()
        {
            return Stopwatch.GetTimestamp() / StopwatchTicksPerMillisecond;
        }

        public void StartSendLoop(Action onSendFailed, CancellationToken parentToken)
        {
            if (_sendTask != null)
                return;

            _sendCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            var token = _sendCts.Token;
            _sendTask = Task.Run(() => SendLoop(onSendFailed, token), token);
        }

        internal void StartLivenessMonitor(int timeoutMs, Action onTimeout, CancellationToken parentToken)
        {
            if (_livenessTask != null || timeoutMs <= 0 || onTimeout == null)
                return;

            _livenessCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            var token = _livenessCts.Token;
            _livenessTask = Task.Run(
                () => MonitorLiveness(timeoutMs, onTimeout, token),
                token);
        }

        /// <summary>Encode the string as UTF-8 and send it in a text frame.</summary>
        public EnqueueResult SendText(string json, FramePriority priority)
        {
            var payload = Encoding.UTF8.GetBytes(json ?? string.Empty);
            return _sendQueue.Enqueue(new QueuedFrame(OpText, payload, priority));
        }

        internal EnqueueResult SendTextEncoded(byte[] utf8Json, FramePriority priority)
        {
            return _sendQueue.Enqueue(new QueuedFrame(OpText, utf8Json ?? Array.Empty<byte>(), priority));
        }

        /// <summary>Send raw bytes in a binary frame.</summary>
        public EnqueueResult SendBinary(byte[] data, FramePriority priority)
        {
            return _sendQueue.Enqueue(new QueuedFrame(OpBinary, data == null ? Array.Empty<byte>() : (byte[])data.Clone(), priority));
        }

        public int ClearDataFrames()
        {
            return _sendQueue.ClearDataFrames();
        }

        /// <summary>Send a close frame with an empty payload to initiate graceful shutdown.</summary>
        public EnqueueResult SendClose()
        {
            return _sendQueue.Enqueue(new QueuedFrame(OpClose, Array.Empty<byte>(), FramePriority.Control));
        }

        public EnqueueResult SendClose(ushort statusCode)
        {
            var payload = new[]
            {
                (byte)(statusCode >> 8),
                (byte)statusCode
            };
            return _sendQueue.Enqueue(new QueuedFrame(OpClose, payload, FramePriority.Control));
        }

        /// <summary>Echo back a pong frame with the given payload in response to a ping.</summary>
        public EnqueueResult SendPong(byte[] data)
        {
            return _sendQueue.Enqueue(new QueuedFrame(OpPong, data, FramePriority.Control));
        }

        /// <summary>Send a server ping used to keep an established peer alive.</summary>
        internal EnqueueResult SendPing()
        {
            return _sendQueue.Enqueue(new QueuedFrame(OpPing, Array.Empty<byte>(), FramePriority.Control));
        }

        public bool WaitForPendingSends(TimeSpan timeout)
        {
            return _sendQueue.WaitUntilEmpty(timeout);
        }

        public bool WaitForSendLoop(TimeSpan timeout)
        {
            var task = _sendTask;
            if (task == null || IsCurrentSendLoop)
                return true;

            try
            {
                return task.Wait(timeout);
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                return true;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        private bool IsCurrentSendLoop => Environment.CurrentManagedThreadId == Volatile.Read(ref _sendLoopThreadId);

        private bool IsCurrentLivenessMonitor =>
            Environment.CurrentManagedThreadId == Volatile.Read(ref _livenessThreadId);

        private async Task MonitorLiveness(int timeoutMs, Action onTimeout, CancellationToken ct)
        {
            Interlocked.Exchange(ref _livenessThreadId, Environment.CurrentManagedThreadId);
            var pingIntervalMs = Math.Max(10, timeoutMs / 3);
            var pollMs = Math.Max(10, Math.Min(1000, pingIntervalMs / 4));
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(pollMs, ct).ConfigureAwait(false);
                    // Async continuations may resume on a different worker;
                    // keep reentrant Dispose detection bound to the callback's
                    // actual executing thread.
                    Interlocked.Exchange(ref _livenessThreadId, Environment.CurrentManagedThreadId);
                    var nowMs = MonotonicMilliseconds();
                    var ageMs = nowMs - Interlocked.Read(ref _lastInboundActivityMs);
                    if (ageMs >= pingIntervalMs
                        && Interlocked.CompareExchange(ref _pingOutstanding, 1, 0) == 0)
                    {
                        Interlocked.Exchange(ref _pingSentAtMs, nowMs);
                        if (!SendPing().Accepted)
                        {
                            onTimeout();
                            return;
                        }
                    }
                    else if (Volatile.Read(ref _pingOutstanding) != 0
                             && nowMs - Interlocked.Read(ref _pingSentAtMs) >= timeoutMs)
                    {
                        onTimeout();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                Interlocked.Exchange(ref _livenessThreadId, 0);
            }
        }

        private void SendLoop(Action onSendFailed, CancellationToken ct)
        {
            Interlocked.Exchange(ref _sendLoopThreadId, Environment.CurrentManagedThreadId);
            try
            {
                while (_sendQueue.WaitToDequeue(ct, out var frame))
                {
                    _sendBatch.Clear();
                    _sendBatch.Add(frame);
                    while (_sendBatch.Count < MaxSendBatchFrames && _sendQueue.TryDequeue(out var nextFrame))
                        _sendBatch.Add(nextFrame);
                    WriteFrameBatch(_sendBatch);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) when (ct.IsCancellationRequested) { }
            catch (IOException)
            {
                onSendFailed?.Invoke();
            }
            catch
            {
                onSendFailed?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref _sendLoopThreadId, 0);
            }
        }

        /// <summary>Write a bounded burst of queued frames and flush once at the end.</summary>
        private void WriteFrameBatch(List<QueuedFrame> frames)
        {
            if (frames == null || frames.Count == 0)
                return;

            foreach (var frame in frames)
            {
                WsFrameCodec.WriteFrame(_stream, frame.Opcode, frame.Payload, flush: false);
                Interlocked.Increment(ref _sentFrames);
                Interlocked.Add(ref _sentBytes, frame.Payload.Length);
            }

            _stream.Flush();
            TouchActivity();
        }

        /// <summary>
        /// Read and unmask a complete WebSocket frame from the stream.
        /// Returns <c>null</c> on stream closure, oversized payload, or protocol error.
        /// </summary>
        public WsFrame ReadFrame()
        {
            return ReadFrame(out _);
        }

        public WsFrame ReadFrame(out WsFrameReadResult result)
        {
            result = WsFrameCodec.ReadFrame(_stream, out var frame, _maxInboundFrameBytes);
            return result == WsFrameReadResult.Success ? frame : null;
        }

        /// <summary>
        /// Read a frame while applying a deadline only after the first header
        /// byte arrives. A completely idle peer therefore remains connected,
        /// while a partially sent frame cannot hold the receive loop forever.
        /// </summary>
        internal WsFrame ReadFrame(out WsFrameReadResult result, int frameProgressTimeoutMs)
        {
            if (!_stream.CanTimeout || frameProgressTimeoutMs <= 0)
                return ReadFrame(out result);

            var previousReadTimeout = _stream.ReadTimeout;
            try
            {
                _stream.ReadTimeout = Timeout.Infinite;
                int firstHeaderByte;
                try
                {
                    firstHeaderByte = _stream.ReadByte();
                }
                catch (IOException)
                {
                    result = WsFrameReadResult.EndOfStream;
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    result = WsFrameReadResult.EndOfStream;
                    return null;
                }

                if (firstHeaderByte < 0)
                {
                    result = WsFrameReadResult.EndOfStream;
                    return null;
                }

                var frameDeadlineMs = MonotonicMilliseconds() + frameProgressTimeoutMs;
                using var frameStream = new FrameProgressStream(_stream, frameDeadlineMs);
                result = WsFrameCodec.ReadFrameAfterFirstHeaderByte(
                    frameStream,
                    (byte)firstHeaderByte,
                    out var frame,
                    _maxInboundFrameBytes);
                return result == WsFrameReadResult.Success ? frame : null;
            }
            finally
            {
                try { _stream.ReadTimeout = previousReadTimeout; } catch { }
            }
        }

        /// <summary>Close and dispose the underlying network stream.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _sendQueue.Complete();
            try { _livenessCts?.Cancel(); } catch { }
            try { _sendCts?.Cancel(); } catch { }
            try { _stream.Close(); } catch { }
            try { _stream.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            try { _tcpClient?.Dispose(); } catch { }
            WaitForLivenessMonitor(TimeSpan.FromMilliseconds(DisposeWaitTimeoutMs));
            WaitForSendLoop(TimeSpan.FromMilliseconds(DisposeWaitTimeoutMs));
            try { _livenessCts?.Dispose(); } catch { }
            _livenessCts = null;
            try { _sendCts?.Dispose(); } catch { }
            _sendCts = null;
        }

        private void WaitForLivenessMonitor(TimeSpan timeout)
        {
            var task = _livenessTask;
            if (task == null || IsCurrentLivenessMonitor)
                return;

            try { task.Wait(timeout); }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException)) { }
            catch (ObjectDisposedException) { }
        }

        private sealed class FrameProgressStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _deadlineMs;

            internal FrameProgressStream(Stream inner, long deadlineMs)
            {
                _inner = inner;
                _deadlineMs = deadlineMs;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override bool CanTimeout => _inner.CanTimeout;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count)
            {
                ApplyRemainingTimeout();
                return _inner.Read(buffer, offset, count);
            }

            public override int Read(Span<byte> buffer)
            {
                ApplyRemainingTimeout();
                return _inner.Read(buffer);
            }

            public override int ReadByte()
            {
                ApplyRemainingTimeout();
                return _inner.ReadByte();
            }

            public override void Write(byte[] buffer, int offset, int count)
                => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin)
                => throw new NotSupportedException();

            public override void SetLength(long value)
                => throw new NotSupportedException();

            protected override void Dispose(bool disposing) { }

            private void ApplyRemainingTimeout()
            {
                var remainingMs = _deadlineMs - MonotonicMilliseconds();
                if (remainingMs <= 0)
                    throw new IOException("WebSocket frame progress deadline exceeded.");
                if (!_inner.CanTimeout)
                    return;

                _inner.ReadTimeout = (int)Math.Max(1L, Math.Min(int.MaxValue, remainingMs));
            }
        }
    }
}
