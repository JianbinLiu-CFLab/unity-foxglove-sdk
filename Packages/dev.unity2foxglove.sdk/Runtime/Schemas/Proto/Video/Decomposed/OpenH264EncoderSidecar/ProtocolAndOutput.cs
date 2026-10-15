// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Focused OpenH264 encoder sidecar implementation section.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;

namespace Foxglove.Schemas.Video
{
    public sealed partial class OpenH264EncoderSidecar
    {
        private void EnqueueAccessUnit(byte[] accessUnit)
        {
            lock (_outputLock)
            {
                if (_outputCount >= _maxOutputQueue)
                {
                    _encodedFrameTimestamps.TryDequeue(out _);
                    Interlocked.Increment(ref _droppedOutputFrames);
                    LastDiagnosticLine = "OpenH264 output queue full; encoded frame was dropped.";
                    return;
                }

                if (!_encodedFrameTimestamps.TryDequeue(out var timestampNs))
                {
                    LastDiagnosticLine = "OpenH264 access unit had no queued capture timestamp.";
                    return;
                }
                _outputAccessUnits.Enqueue(new EncodedVideoAccessUnit(accessUnit, timestampNs));
                _outputCount++;
                Interlocked.Increment(ref _accessUnitsReceived);
            }
        }

        private void EnqueueAccessUnit(byte[] accessUnit, ulong timestampNs)
        {
            lock (_outputLock)
            {
                if (_outputCount >= _maxOutputQueue)
                {
                    Interlocked.Increment(ref _droppedOutputFrames);
                    LastDiagnosticLine = "OpenH264 output queue full; encoded frame was dropped.";
                    return;
                }

                _outputAccessUnits.Enqueue(new EncodedVideoAccessUnit(accessUnit, timestampNs));
                _outputCount++;
                Interlocked.Increment(ref _accessUnitsReceived);
            }
        }

        internal void AcceptHelperAccessUnit(byte[] accessUnit)
        {
            if (accessUnit == null)
                throw new ArgumentNullException(nameof(accessUnit));

            if (accessUnit.Length == 0)
                throw new ArgumentException("OpenH264 access unit must not be empty.", nameof(accessUnit));

            EnqueueAccessUnit(accessUnit);
        }

        private void AcceptHelperAccessUnit(byte[] accessUnit, ulong timestampNs)
        {
            ConsumeWrittenFrame();
            if (accessUnit == null || accessUnit.Length == 0)
            {
                Interlocked.Increment(ref _skippedAccessUnits);
                return;
            }

            EnqueueAccessUnit(accessUnit, timestampNs);
        }

        internal void AcceptHelperSkippedAccessUnit()
        {
            _encodedFrameTimestamps.TryDequeue(out _);
            Interlocked.Increment(ref _skippedAccessUnits);
            LastDiagnosticLine = "OpenH264 helper skipped an access unit.";
        }

        private void AcceptHelperSkippedAccessUnit(ulong timestampNs)
        {
            ConsumeWrittenFrame();
            Interlocked.Increment(ref _skippedAccessUnits);
            LastDiagnosticLine = "OpenH264 helper skipped an access unit.";
        }

        internal void EnqueueTimestampForTests(ulong timestampNs)
        {
            _encodedFrameTimestamps.Enqueue(timestampNs);
        }

        private bool IsCurrentSessionForTests(Process process, long sessionId)
            => process != null
                && ReferenceEquals(process, Volatile.Read(ref _process))
                && Volatile.Read(ref _sessionId) == sessionId;

        private static async Task<LengthReadResult> ReadLittleEndianLength(Stream stream, byte[] header, CancellationToken token)
        {
            if (!await ReadExact(stream, header, token).ConfigureAwait(false))
                return new LengthReadResult(false, 0);

            var length = header[0]
                | (header[1] << 8)
                | (header[2] << 16)
                | (header[3] << 24);
            return new LengthReadResult(true, length);
        }

        private static async Task<bool> ReadExact(Stream stream, byte[] buffer, CancellationToken token)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, token).ConfigureAwait(false);
                if (read == 0)
                    return false;

                offset += read;
            }

            return true;
        }

        private void DrainInputQueue()
        {
            lock (_inputLock)
            {
                while (_inputFrames.TryDequeue(out var frame)) ReturnInputFrameBuffer(frame);
                _inputCount = 0;
            }

            while (_encodedFrameTimestamps.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _writtenFrameCount, 0);
            Interlocked.Exchange(ref _processingFrameCount, 0);
        }

        private static void ReturnInputFrameBuffer(QueuedVideoFrame frame)
        {
            if (frame.Pooled && frame.Data != null)
                ArrayPool<byte>.Shared.Return(frame.Data);
        }

        private bool TryDequeueInputFrame(Process process, CancellationToken token, out QueuedVideoFrame frame)
        {
            lock (_inputLock)
            {
                frame = default;
                if (token.IsCancellationRequested || !ReferenceEquals(process, Volatile.Read(ref _process)))
                    return false;
                if (!_inputFrames.TryDequeue(out frame))
                    return false;

                if (_inputCount > 0)
                    _inputCount--;
                Interlocked.Increment(ref _processingFrameCount);
                return true;
            }
        }

        private void ConsumeWrittenFrame()
        {
            while (true)
            {
                var current = Volatile.Read(ref _writtenFrameCount);
                if (current <= 0)
                    return;
                if (Interlocked.CompareExchange(ref _writtenFrameCount, current - 1, current) == current)
                    return;
            }
        }

        private void DrainOutputQueue()
        {
            lock (_outputLock)
            {
                while (_outputAccessUnits.TryDequeue(out _)) { }
                _outputCount = 0;
            }
        }

        private static bool IsProcessRunning(Process process)
        {
            if (process == null)
                return false;

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private bool IsRunningNoLock()
        {
            var process = _process;
            if (process == null)
                return false;

            try
            {
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();
            }
            catch
            {
                // Best-effort failure shutdown.
            }
        }

        private void RetireFailedProcess(Process process, CancellationToken token, string error)
        {
            if (token.IsCancellationRequested || !ReferenceEquals(process, Volatile.Read(ref _process)))
                return;

            LastError = error;
            // Kill only the failed session's process; never wait on the calling worker itself.
            TryKillProcess(process);
            Task.Run(() =>
            {
                lock (_startStopLock)
                {
                    if (ReferenceEquals(process, Volatile.Read(ref _process)))
                        StopNoLock(clearOutputQueue: false);
                }
            });
        }

        private void WaitForTask(Task task, string taskName, DateTime deadlineUtc)
        {
            if (task == null || task.IsCompleted)
                return;

            try
            {
                var timeoutMs = RemainingMilliseconds(deadlineUtc);
                if (timeoutMs > 0)
                    task.Wait(timeoutMs);
            }
            catch
            {
                // Best-effort task shutdown.
            }

            if (!task.IsCompleted)
                LastError = "OpenH264 shutdown timed out waiting for the " + taskName + " task.";
        }

        private static int RemainingMilliseconds(DateTime deadlineUtc)
            => Math.Max(0, (int)(deadlineUtc - DateTime.UtcNow).TotalMilliseconds);

        private readonly struct LengthReadResult
        {
            public LengthReadResult(bool success, int length)
            {
                Success = success;
                Length = length;
            }

            public bool Success { get; }
            public int Length { get; }
        }
    }
}
