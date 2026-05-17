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
using System.Threading;
using System.Threading.Tasks;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Encodes I420 frames through an external OpenH264 helper process and
    /// exposes completed H.264 Annex B access units.
    /// </summary>
    public sealed class OpenH264EncoderSidecar : ICameraVideoEncoderSidecar
    {
        private const int MaxAccessUnitBytes = 16 * 1024 * 1024;

        private readonly ConcurrentQueue<byte[]> _inputFrames = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _outputAccessUnits = new ConcurrentQueue<byte[]>();
        private readonly object _outputLock = new object();
        private Process _process;
        private CancellationTokenSource _stop;
        private Task _stdinTask;
        private Task _stdoutTask;
        private Task _stderrTask;
        private OpenH264EncoderOptions _options;
        private int _inputCount;
        private int _outputCount;
        private long _framesSubmitted;
        private long _accessUnitsReceived;
        private long _droppedInputFrames;

        public bool IsRunning
        {
            get
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
        }

        public long FramesSubmitted => Interlocked.Read(ref _framesSubmitted);
        public long AccessUnitsReceived => Interlocked.Read(ref _accessUnitsReceived);
        public long DroppedInputFrames => Interlocked.Read(ref _droppedInputFrames);
        public string LastDiagnosticLine { get; private set; }
        public string LastError { get; private set; }

        public bool Start(OpenH264EncoderOptions options)
        {
            if (IsRunning)
                return true;

            Stop();

            _options = options ?? new OpenH264EncoderOptions();
            LastError = null;
            LastDiagnosticLine = null;

            if (!_options.Validate(out var error))
            {
                LastError = error;
                return false;
            }

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
                    Stop();
                    return false;
                }

                _stop = new CancellationTokenSource();
                var process = _process;
                var token = _stop.Token;
                _stdinTask = Task.Run(() => RunStdinWriter(process, token));
                _stdoutTask = Task.Run(() => RunStdoutReader(process, token));
                _stderrTask = Task.Run(() => RunStderrReader(process, token));
                return true;
            }
            catch (Win32Exception ex)
            {
                LastError = "OpenH264 helper executable could not be started: " + ex.Message;
                Stop();
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Stop();
                return false;
            }
        }

        public bool TrySubmitFrame(byte[] frame)
        {
            if (frame == null || frame.Length == 0 || !IsRunning)
                return false;

            var expectedBytes = _options != null ? _options.FrameByteCount : 0;
            if (expectedBytes > 0 && frame.Length != expectedBytes)
            {
                LastError = "I420 frame byte count does not match OpenH264 encoder dimensions.";
                return false;
            }

            var capacity = Math.Max(1, _options?.MaxInputQueue ?? 2);
            while (Volatile.Read(ref _inputCount) >= capacity && _inputFrames.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _inputCount);
                Interlocked.Increment(ref _droppedInputFrames);
            }

            var copy = new byte[frame.Length];
            Buffer.BlockCopy(frame, 0, copy, 0, frame.Length);
            _inputFrames.Enqueue(copy);
            Interlocked.Increment(ref _inputCount);
            Interlocked.Increment(ref _framesSubmitted);
            return true;
        }

        public bool TryDequeueAccessUnit(out byte[] accessUnit)
        {
            lock (_outputLock)
            {
                if (!_outputAccessUnits.TryDequeue(out accessUnit))
                    return false;

                Interlocked.Decrement(ref _outputCount);
                return true;
            }
        }

        public void Stop()
        {
            var stop = _stop;
            if (stop != null && !stop.IsCancellationRequested)
                stop.Cancel();

            var process = _process;
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

                try
                {
                    process.WaitForExit(200);
                }
                catch
                {
                }

                WaitForTask(_stdinTask, 200);
                WaitForTask(_stdoutTask, 200);
                WaitForTask(_stderrTask, 200);
                process.Dispose();
            }

            _process = null;
            _stdinTask = null;
            _stdoutTask = null;
            _stderrTask = null;
            _stop?.Dispose();
            _stop = null;
            DrainInputQueue();
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task RunStdinWriter(Process process, CancellationToken token)
        {
            try
            {
                var stream = process.StandardInput.BaseStream;
                while (!token.IsCancellationRequested && IsProcessRunning(process))
                {
                    if (_inputFrames.TryDequeue(out var frame))
                    {
                        Interlocked.Decrement(ref _inputCount);
                        await stream.WriteAsync(frame, 0, frame.Length, token).ConfigureAwait(false);
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
                LastError = ex.Message;
            }
        }

        private async Task RunStdoutReader(Process process, CancellationToken token)
        {
            try
            {
                var stream = process.StandardOutput.BaseStream;
                while (!token.IsCancellationRequested)
                {
                    var readLength = await ReadLittleEndianLength(stream, token).ConfigureAwait(false);
                    if (!readLength.Success)
                        break;

                    var length = readLength.Length;
                    if (length <= 0 || length > MaxAccessUnitBytes)
                    {
                        LastError = "OpenH264 helper emitted an invalid access-unit length: " + length;
                        TryKillProcess(process);
                        return;
                    }

                    var payload = new byte[length];
                    if (!await ReadExact(stream, payload, token).ConfigureAwait(false))
                    {
                        LastError = "OpenH264 helper stdout ended mid access unit.";
                        TryKillProcess(process);
                        return;
                    }

                    EnqueueAccessUnit(payload);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private async Task RunStderrReader(Process process, CancellationToken token)
        {
            try
            {
                var reader = process.StandardError;
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                        break;

                    LastDiagnosticLine = line;
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    LastError = ex.Message;
            }
        }

        private void EnqueueAccessUnit(byte[] accessUnit)
        {
            var capacity = Math.Max(1, _options?.MaxOutputQueue ?? 4);
            lock (_outputLock)
            {
                while (Volatile.Read(ref _outputCount) >= capacity && _outputAccessUnits.TryDequeue(out _))
                    Interlocked.Decrement(ref _outputCount);

                _outputAccessUnits.Enqueue(accessUnit);
                Interlocked.Increment(ref _outputCount);
                Interlocked.Increment(ref _accessUnitsReceived);
            }
        }

        private static async Task<LengthReadResult> ReadLittleEndianLength(Stream stream, CancellationToken token)
        {
            var header = new byte[4];
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
            while (_inputFrames.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _inputCount, 0);
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

        private static void WaitForTask(Task task, int timeoutMs)
        {
            if (task == null || task.IsCompleted)
                return;

            try
            {
                task.Wait(timeoutMs);
            }
            catch
            {
                // Best-effort task shutdown.
            }
        }

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
