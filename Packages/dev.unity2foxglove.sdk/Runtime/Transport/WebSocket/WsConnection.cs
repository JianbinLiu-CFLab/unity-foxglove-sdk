// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: Per-client WebSocket connection state, send loop, lifecycle, and
// transport statistics.

using System;
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
        private const int CloseDrainTimeoutMs = 250;
        private const int SendLoopCloseTimeoutMs = 1000;

        /// <summary>Underlying TCP client owned by this connection after handshake.</summary>
        private readonly TcpClient _tcpClient;
        /// <summary>Underlying plain or TLS stream.</summary>
        private readonly Stream _stream;
        private readonly WsSendQueue _sendQueue;
        private CancellationTokenSource _sendCts;
        private Task _sendTask;
        private int _disposed;

        /// <summary>RFC 6455 opcode for text frames.</summary>
        private const byte OpText = 0x1;
        /// <summary>RFC 6455 opcode for binary frames.</summary>
        private const byte OpBinary = 0x2;
        /// <summary>RFC 6455 opcode for close frames.</summary>
        private const byte OpClose = 0x8;
        /// <summary>RFC 6455 opcode for pong frames.</summary>
        private const byte OpPong = 0xA;

        // Health counters
        private readonly DateTime _connectedAtUtc = DateTime.UtcNow;
        private long _lastActivityMs;
        private long _sentFrames;
        private long _sentBytes;

        /// <summary>Create a connection on the given network stream.</summary>
        public WsConnection(TcpClient tcpClient, Stream stream, int maxQueuedFrames, int maxQueuedBytes)
        {
            _tcpClient = tcpClient;
            _stream = stream;
            _sendQueue = new WsSendQueue(maxQueuedFrames, maxQueuedBytes);
            _lastActivityMs = MonotonicMilliseconds();
        }

        public long DroppedDataFrames => _sendQueue.DroppedDataFramesSnapshot;

        public TransportClientStats GetClientStats(uint clientId)
        {
            var snap = _sendQueue.GetSnapshot();
            var nowTicks = DateTime.UtcNow.Ticks;
            var nowMs = MonotonicMilliseconds();
            return new TransportClientStats
            {
                ClientId = clientId,
                ConnectedAtUtc = _connectedAtUtc,
                ConnectedDurationMs = (long)(new DateTime(nowTicks) - _connectedAtUtc).TotalMilliseconds,
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

        private static long MonotonicMilliseconds()
        {
            var ticks = Stopwatch.GetTimestamp();
            var frequency = Stopwatch.Frequency;
            return (ticks / frequency) * 1000 + (ticks % frequency) * 1000 / frequency;
        }

        public void StartSendLoop(Action onSendFailed, CancellationToken parentToken)
        {
            if (_sendTask != null)
                return;

            _sendCts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            var token = _sendCts.Token;
            _sendTask = Task.Run(() => SendLoop(onSendFailed, token), token);
        }

        /// <summary>Encode the string as UTF-8 and send it in a text frame.</summary>
        public EnqueueResult SendText(string json, FramePriority priority)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            return _sendQueue.Enqueue(new QueuedFrame(OpText, payload, priority));
        }

        /// <summary>Send raw bytes in a binary frame.</summary>
        public EnqueueResult SendBinary(byte[] data, FramePriority priority)
        {
            return _sendQueue.Enqueue(new QueuedFrame(OpBinary, data, priority));
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

        /// <summary>Echo back a pong frame with the given payload in response to a ping.</summary>
        public EnqueueResult SendPong(byte[] data)
        {
            return _sendQueue.Enqueue(new QueuedFrame(OpPong, data, FramePriority.Control));
        }

        public bool WaitForPendingSends(TimeSpan timeout)
        {
            return _sendQueue.WaitUntilEmpty(timeout);
        }

        public bool WaitForSendLoop(TimeSpan timeout)
        {
            var task = _sendTask;
            if (task == null)
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

        private void SendLoop(Action onSendFailed, CancellationToken ct)
        {
            try
            {
                while (_sendQueue.WaitToDequeue(ct, out var frame))
                    WriteFrame(frame.Opcode, frame.Payload);
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
        }

        /// <summary>Build and write a complete WebSocket frame (FIN + opcode + length-prefixed payload).</summary>
        private void WriteFrame(byte opcode, byte[] payload)
        {
            WsFrameCodec.WriteFrame(_stream, opcode, payload);
            Interlocked.Increment(ref _sentFrames);
            Interlocked.Add(ref _sentBytes, payload.Length);
            TouchActivity();
        }

        /// <summary>
        /// Read and unmask a complete WebSocket frame from the stream.
        /// Returns <c>null</c> on stream closure, oversized payload, or protocol error.
        /// </summary>
        public WsFrame ReadFrame()
        {
            return WsFrameCodec.TryReadFrame(_stream, out var frame) ? frame : null;
        }

        /// <summary>Close and dispose the underlying network stream.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _sendQueue.Complete();
            if (!WaitForSendLoop(TimeSpan.FromMilliseconds(SendLoopCloseTimeoutMs)))
            {
                try { _sendCts?.Cancel(); } catch { }
                WaitForSendLoop(TimeSpan.FromMilliseconds(CloseDrainTimeoutMs));
            }
            try { _stream.Close(); } catch { }
            try { _stream.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            try { _tcpClient?.Dispose(); } catch { }
            try { _sendCts?.Dispose(); } catch { }
            _sendCts = null;
        }
    }
}
