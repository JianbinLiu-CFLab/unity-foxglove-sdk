// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: External OpenH264 helper process wrapper with bounded queues.

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Encodes I420 frames through an external OpenH264 helper process and
    /// exposes completed H.264 Annex B access units.
    /// </summary>
    public sealed class OpenH264EncoderSidecar : ICameraVideoEncoderSidecar, ITimestampedCameraVideoEncoderSidecar
    {
        private const int MaxAccessUnitBytes = 16 * 1024 * 1024;
        private const int ShutdownTimeoutMs = 500;

        private readonly ConcurrentQueue<QueuedVideoFrame> _inputFrames = new ConcurrentQueue<QueuedVideoFrame>();
        private readonly ConcurrentQueue<ulong> _encodedFrameTimestamps = new ConcurrentQueue<ulong>();
        private readonly ConcurrentQueue<EncodedVideoAccessUnit> _outputAccessUnits = new ConcurrentQueue<EncodedVideoAccessUnit>();
        private readonly object _startStopLock = new object();
        private readonly object _inputLock = new object();
        private readonly object _outputLock = new object();
        private Process _process;
        private CancellationTokenSource _stop;
        private Task _stdinTask;
        private Task _stdoutTask;
        private Task _stderrTask;
        private long _sessionId;
        private OpenH264EncoderOptions _options;
        private int _maxInputQueue = 2;
        private int _maxOutputQueue = 4;
        private int _inputCount;
        private int _outputCount;
        private long _framesSubmitted;
        private long _accessUnitsReceived;
        private long _skippedAccessUnits;
        private long _droppedInputFrames;
        private long _droppedOutputFrames;
        private string _lastDiagnosticLine;
        private string _lastError;

        public bool IsRunning
        {
            get
            {
                lock (_startStopLock)
                {
                    return IsRunningNoLock();
                }
            }
        }

        public long FramesSubmitted => Interlocked.Read(ref _framesSubmitted);
        public long AccessUnitsReceived => Interlocked.Read(ref _accessUnitsReceived);
        public long SkippedAccessUnits => Interlocked.Read(ref _skippedAccessUnits);
        public long DroppedInputFrames => Interlocked.Read(ref _droppedInputFrames);
        public long DroppedOutputFrames => Interlocked.Read(ref _droppedOutputFrames);
        public int OutputQueueDepth => Volatile.Read(ref _outputCount);
        public int MaxOutputQueue => Volatile.Read(ref _maxOutputQueue);
        internal int PendingTimestampCountForTests => _encodedFrameTimestamps.Count;
        public string LastDiagnosticLine
        {
            get => Volatile.Read(ref _lastDiagnosticLine);
            private set => Volatile.Write(ref _lastDiagnosticLine, value);
        }
        public string LastError
        {
            get => Volatile.Read(ref _lastError);
            private set => Volatile.Write(ref _lastError, value);
        }

        public bool Start(OpenH264EncoderOptions options)
        {
            lock (_startStopLock)
            {
                if (IsRunningNoLock())
                    return true;

                StopNoLock(clearOutputQueue: true);

                _options = options ?? new OpenH264EncoderOptions();
                LastError = null;
                LastDiagnosticLine = null;

                if (!_options.Validate(out var error))
                {
                    LastError = error;
                    return false;
                }

                _maxInputQueue = Math.Max(1, _options.MaxInputQueue);
                _maxOutputQueue = Math.Max(1, _options.MaxOutputQueue);

                try
                {
                    _process = new Process
                    {
                        StartInfo = _options.CreateStartInfo(),
                        EnableRaisingEvents = true
                    };

                    if (!_process.Start())
                    {
                        LastError = "OpenH264 helper process failed to start.";
                        StopNoLock(clearOutputQueue: true);
                        return false;
                    }

                    _stop = new CancellationTokenSource();
                    var process = _process;
                    var token = _stop.Token;
                    var sessionId = Interlocked.Increment(ref _sessionId);
                    _stdinTask = Task.Run(() => RunStdinWriter(process, token));
                    _stdoutTask = Task.Run(() => RunStdoutReaderForSession(process, token, sessionId));
                    _stderrTask = Task.Run(() => RunStderrReader(process, token));
                    return true;
                }
                catch (Win32Exception ex)
                {
                    LastError = "OpenH264 helper executable could not be started: " + ex.Message;
                    StopNoLock(clearOutputQueue: true);
                    return false;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    StopNoLock(clearOutputQueue: true);
                    return false;
                }
            }
        }

        public bool TrySubmitFrame(byte[] frame)
            => TrySubmitFrame(frame, 0UL);

        public bool TrySubmitFrame(byte[] frame, ulong timestampNs)
        {
            var submittingProcess = Volatile.Read(ref _process);
            if (frame == null || frame.Length == 0 || !IsProcessRunning(submittingProcess))
                return false;

            var expectedBytes = _options != null ? _options.FrameByteCount : 0;
            if (expectedBytes <= 0)
            {
                LastError = "OpenH264 encoder dimensions produce an invalid I420 frame size.";
                return false;
            }

            if (frame.Length != expectedBytes)
            {
                LastError = "I420 frame byte count does not match OpenH264 encoder dimensions.";
                return false;
            }

            var copy = new byte[frame.Length];
            Buffer.BlockCopy(frame, 0, copy, 0, frame.Length);

            lock (_inputLock)
            {
                if (!ReferenceEquals(submittingProcess, Volatile.Read(ref _process))
                    || !IsProcessRunning(submittingProcess))
                    return false;

                while (_inputCount >= _maxInputQueue && _inputFrames.TryDequeue(out _))
                {
                    _inputCount--;
                    Interlocked.Increment(ref _droppedInputFrames);
                }

                // Pending raw frames and written-but-unpaired frames share one finite budget.
                if ((long)_inputCount + _encodedFrameTimestamps.Count >= (long)_maxInputQueue + _maxOutputQueue)
                {
                    return false;
                }

                _inputFrames.Enqueue(new QueuedVideoFrame(copy, timestampNs));
                _inputCount++;
            }

            Interlocked.Increment(ref _framesSubmitted);
            return true;
        }

        public bool TryDequeueAccessUnit(out byte[] accessUnit)
        {
            if (TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit timestamped))
            {
                accessUnit = timestamped.Data;
                return true;
            }

            accessUnit = null;
            return false;
        }

        public bool TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit)
        {
            lock (_outputLock)
            {
                if (!_outputAccessUnits.TryDequeue(out accessUnit))
                    return false;

                _outputCount--;
                return true;
            }
        }

        public void Stop()
        {
            Stop(clearOutputQueue: true);
        }

        private void Stop(bool clearOutputQueue)
        {
            lock (_startStopLock)
            {
                StopNoLock(clearOutputQueue);
            }
        }

        private void StopNoLock(bool clearOutputQueue)
        {
            Interlocked.Increment(ref _sessionId);
            var stop = _stop;
            if (stop != null && !stop.IsCancellationRequested)
                stop.Cancel();

            Process process;
            lock (_inputLock)
                process = Interlocked.Exchange(ref _process, null);
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.StandardInput.BaseStream.Close();
                }
                catch
                {
                }

                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }

                var deadlineUtc = DateTime.UtcNow.AddMilliseconds(ShutdownTimeoutMs);
                try
                {
                    process.WaitForExit(RemainingMilliseconds(deadlineUtc));
                }
                catch
                {
                }

                WaitForTask(_stdinTask, "stdin", deadlineUtc);
                WaitForTask(_stdoutTask, "stdout", deadlineUtc);
                WaitForTask(_stderrTask, "stderr", deadlineUtc);
                process.Dispose();
            }

            _stdinTask = null;
            _stdoutTask = null;
            _stderrTask = null;
            _stop?.Dispose();
            _stop = null;
            DrainInputQueue();
            if (clearOutputQueue)
                DrainOutputQueue();
        }

        public void Dispose()
        {
            Stop(clearOutputQueue: false);
        }

        private async Task RunStdinWriter(Process process, CancellationToken token)
        {
            try
            {
                var stream = process.StandardInput.BaseStream;
                while (!token.IsCancellationRequested && IsProcessRunning(process))
                {
                    if (TryDequeueInputFrame(process, token, out var frame))
                    {
                        await stream.WriteAsync(frame.Data, 0, frame.Data.Length, token).ConfigureAwait(false);
                        await stream.FlushAsync(token).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(2, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RetireFailedProcess(process, token, ex.Message);
            }
        }

        private Task RunStdoutReader(Process process, CancellationToken token)
            => RunStdoutReaderForSession(process, token, Volatile.Read(ref _sessionId));

        private async Task RunStdoutReaderForSession(Process process, CancellationToken token, long sessionId)
        {
            var header = new byte[4];
            try
            {
                var stream = process.StandardOutput.BaseStream;
                while (!token.IsCancellationRequested)
                {
                    var readLength = await ReadLittleEndianLength(stream, header, token).ConfigureAwait(false);
                    if (!readLength.Success)
                    {
                        RetireFailedProcess(process, token, "Encoder stdout ended unexpectedly.");
                        break;
                    }

                    var length = readLength.Length;
                    lock (_outputLock)
                    {
                        if (!IsCurrentSessionForTests(process, sessionId))
                            return;

                        if (length == 0)
                        {
                            AcceptHelperSkippedAccessUnit();
                            continue;
                        }

                        if (length < 0 || length > MaxAccessUnitBytes)
                        {
                            LastError = "OpenH264 helper emitted an invalid access-unit length: " + length;
                            RetireFailedProcess(process, token, LastError);
                            return;
                        }
                    }

                    var payload = new byte[length];
                    if (!await ReadExact(stream, payload, token).ConfigureAwait(false))
                    {
                        LastError = "OpenH264 helper stdout ended mid access unit.";
                        RetireFailedProcess(process, token, LastError);
                        return;
                    }

                    lock (_outputLock)
                    {
                        if (!IsCurrentSessionForTests(process, sessionId))
                            return;
                        AcceptHelperAccessUnit(payload);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                RetireFailedProcess(process, token, ex.Message);
            }
        }

        private async Task RunStderrReader(Process process, CancellationToken token)
        {
            try
            {
                await ReadBoundedDiagnosticStream(
                    process.StandardError.BaseStream,
                    line => LastDiagnosticLine = line,
                    token,
                    Math.Max(1, _options?.MaxStderrLineBytes ?? 8192),
                    Math.Max(1, _options?.MaxStderrRetainedBytes ?? 8192)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    RetireFailedProcess(process, token, ex.Message);
            }
        }

        private static async Task ReadBoundedDiagnosticStream(
            Stream stream,
            Action<string> publishLine,
            CancellationToken token,
            int maxLineBytes,
            int maxRetainedBytes)
        {
            var buffer = new byte[Math.Min(4096, Math.Max(256, maxLineBytes))];
            var lineLimit = Math.Max(1, Math.Min(maxLineBytes, maxRetainedBytes));
            var retained = new byte[lineLimit];
            var retainedCount = 0;
            var truncated = false;

            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (read <= 0)
                    break;

                for (var i = 0; i < read; i++)
                {
                    var value = buffer[i];
                    if (value == (byte)'\n')
                    {
                        PublishDiagnosticLine(retained, retainedCount, truncated, publishLine);
                        retainedCount = 0;
                        truncated = false;
                        continue;
                    }

                    if (value == (byte)'\r')
                        continue;

                    if (retainedCount < lineLimit)
                        retained[retainedCount++] = value;
                    else
                        truncated = true;
                }
            }

            if (retainedCount > 0 || truncated)
                PublishDiagnosticLine(retained, retainedCount, truncated, publishLine);
        }

        private static void PublishDiagnosticLine(byte[] retained, int retainedCount, bool truncated, Action<string> publishLine)
        {
            var text = retainedCount == 0
                ? string.Empty
                : Encoding.UTF8.GetString(retained, 0, retainedCount);
            publishLine(truncated ? text + " [truncated]" : text);
        }

        private void EnqueueAccessUnit(byte[] accessUnit)
        {
            lock (_outputLock)
            {
                if (_outputCount >= _maxOutputQueue)
                {
                    _encodedFrameTimestamps.TryDequeue(out _);
                    Interlocked.Increment(ref _droppedOutputFrames);
                    LastDiagnosticLine = "OpenH264 output queue full; capture admission is holding new frames.";
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

        internal void AcceptHelperAccessUnit(byte[] accessUnit)
        {
            if (accessUnit == null)
                throw new ArgumentNullException(nameof(accessUnit));

            if (accessUnit.Length == 0)
                throw new ArgumentException("OpenH264 access unit must not be empty.", nameof(accessUnit));

            EnqueueAccessUnit(accessUnit);
        }

        internal void AcceptHelperSkippedAccessUnit()
        {
            _encodedFrameTimestamps.TryDequeue(out _);
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
                while (_inputFrames.TryDequeue(out _)) { }
                _inputCount = 0;
            }

            while (_encodedFrameTimestamps.TryDequeue(out _)) { }
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
                _encodedFrameTimestamps.Enqueue(frame.TimestampNs);
                return true;
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
