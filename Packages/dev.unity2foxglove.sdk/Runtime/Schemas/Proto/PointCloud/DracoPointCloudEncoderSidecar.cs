// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/PointCloud
// Purpose: External Draco helper process wrapper for compressed point-cloud spike.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas.PointCloud
{
    /// <summary>Serializes point-cloud frames for the Phase 87 Draco helper protocol.</summary>
    public static class DracoPointCloudHelperProtocol
    {
        /// <summary>
        /// Build helper stdin bytes: uint32 point_count followed by
        /// point_count records of float32 x, float32 y, float32 z.
        /// </summary>
        public static byte[] BuildXyzFramePayload(PointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var capacity = checked(4 + frame.Points.Count * 12);
            using (var stream = new MemoryStream(capacity))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((uint)frame.Points.Count);
                foreach (var point in frame.Points)
                {
                    writer.Write(point.X);
                    writer.Write(point.Y);
                    writer.Write(point.Z);
                }

                return stream.ToArray();
            }
        }
    }

    /// <summary>
    /// Synchronous, length-prefixed wrapper around a spike-only native Draco
    /// helper executable. The helper remains external and is never bundled.
    /// </summary>
    public sealed class DracoPointCloudEncoderSidecar : IDisposable
    {
        private const int MaxPayloadBytes = 64 * 1024 * 1024;

        private Process _process;
        private CancellationTokenSource _stop;
        private Task _stderrTask;
        private string _lastDiagnosticLine;
        private string _lastError;

        /// <summary>Last diagnostic line observed on helper stderr.</summary>
        public string LastDiagnosticLine => Volatile.Read(ref _lastDiagnosticLine);
        /// <summary>Last startup or encode error.</summary>
        public string LastError => Volatile.Read(ref _lastError);

        /// <summary>True when the helper process is still running.</summary>
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

        /// <summary>Start the helper executable if it is not already running.</summary>
        public bool Start(string helperExecutablePath)
        {
            if (IsRunning)
                return true;

            Stop();
            SetLastError(null);
            SetLastDiagnosticLine(null);

            if (string.IsNullOrWhiteSpace(helperExecutablePath))
            {
                SetLastError("Draco helper executable path is empty; publishes nothing on Draco topic.");
                return false;
            }

            if (!File.Exists(helperExecutablePath))
            {
                SetLastError("Draco helper executable does not exist: " + helperExecutablePath);
                return false;
            }

            try
            {
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = helperExecutablePath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                if (!_process.Start())
                {
                    SetLastError("Draco helper process failed to start.");
                    Stop();
                    return false;
                }

                _stop = new CancellationTokenSource();
                var process = _process;
                var token = _stop.Token;
                _stderrTask = Task.Run(() => RunStderrReader(process, token));
                return true;
            }
            catch (Win32Exception ex)
            {
                SetLastError("Draco helper executable could not be started: " + ex.Message);
                Stop();
                return false;
            }
            catch (Exception ex)
            {
                SetLastError(ex.Message);
                Stop();
                return false;
            }
        }

        /// <summary>Encode one frame and read exactly one length-prefixed Draco payload.</summary>
        public bool TryEncode(PointCloudFrame frame, int timeoutMs, out byte[] dracoPayload)
        {
            dracoPayload = null;
            SetLastError(null);

            if (frame == null || frame.Points.Count == 0)
            {
                SetLastError("Draco point-cloud frame is empty.");
                return false;
            }

            if (!IsRunning)
            {
                SetLastError("Draco helper is not running.");
                return false;
            }

            var boundedTimeoutMs = Math.Max(1, timeoutMs);
            var deadlineUtc = DateTime.UtcNow.AddMilliseconds(boundedTimeoutMs);
            var framePayload = DracoPointCloudHelperProtocol.BuildXyzFramePayload(frame);
            var process = _process;

            try
            {
                var stdin = process.StandardInput.BaseStream;
                if (!WaitForTask(stdin.WriteAsync(framePayload, 0, framePayload.Length), deadlineUtc))
                    return FailAndStop("Timed out writing point-cloud frame to Draco helper.");
                if (!WaitForTask(stdin.FlushAsync(), deadlineUtc))
                    return FailAndStop("Timed out flushing point-cloud frame to Draco helper.");

                var readLength = ReadLittleEndianLength(process.StandardOutput.BaseStream, deadlineUtc);
                if (!readLength.Success)
                    return FailAndStop("Draco helper stdout ended before payload length.");

                var payloadLength = readLength.Length;
                if (payloadLength <= 0 || payloadLength > MaxPayloadBytes)
                    return FailAndStop("Draco helper emitted an invalid payload length: " + payloadLength);

                var payload = new byte[payloadLength];
                if (!ReadExact(process.StandardOutput.BaseStream, payload, deadlineUtc))
                    return FailAndStop("Draco helper stdout ended mid payload.");

                dracoPayload = payload;
                return true;
            }
            catch (Exception ex)
            {
                return FailAndStop(ex.Message);
            }
        }

        /// <summary>Stop the helper process and release streams.</summary>
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

                TryKillProcess(process);
                WaitForProcessExit(process, 200);
                WaitForTask(_stderrTask, 200);
                process.Dispose();
            }

            _process = null;
            _stderrTask = null;
            _stop?.Dispose();
            _stop = null;
        }

        /// <summary>Stop and dispose the helper wrapper.</summary>
        public void Dispose()
        {
            Stop();
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

                    SetLastDiagnosticLine(line);
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    SetLastError(ex.Message);
            }
        }

        private bool FailAndStop(string error)
        {
            SetLastError(error);
            Stop();
            return false;
        }

        private void SetLastDiagnosticLine(string value)
        {
            Volatile.Write(ref _lastDiagnosticLine, value);
        }

        private void SetLastError(string value)
        {
            Volatile.Write(ref _lastError, value);
        }

        private static LengthReadResult ReadLittleEndianLength(Stream stream, DateTime deadlineUtc)
        {
            var header = new byte[4];
            if (!ReadExact(stream, header, deadlineUtc))
                return new LengthReadResult(false, 0);

            var length = header[0]
                | (header[1] << 8)
                | (header[2] << 16)
                | (header[3] << 24);
            return new LengthReadResult(true, length);
        }

        private static bool ReadExact(Stream stream, byte[] buffer, DateTime deadlineUtc)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var readTask = stream.ReadAsync(buffer, offset, buffer.Length - offset);
                if (!WaitForTask(readTask, deadlineUtc))
                    return false;

                var read = readTask.Result;
                if (read == 0)
                    return false;

                offset += read;
            }

            return true;
        }

        private static bool WaitForTask(Task task, int timeoutMs)
        {
            return WaitForTask(task, DateTime.UtcNow.AddMilliseconds(Math.Max(1, timeoutMs)));
        }

        private static bool WaitForTask(Task task, DateTime deadlineUtc)
        {
            try
            {
                if (!task.Wait(RemainingMilliseconds(deadlineUtc)))
                {
                    ObserveFaultedTask(task);
                    return false;
                }
                return task.Exception == null;
            }
            catch
            {
                return false;
            }
        }

        private static void ObserveFaultedTask(Task task)
        {
            task.ContinueWith(
                completed => { _ = completed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static int RemainingMilliseconds(DateTime deadlineUtc)
        {
            var remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return 0;

            return Math.Max(1, (int)Math.Min(int.MaxValue, remaining.TotalMilliseconds));
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

        private static void WaitForProcessExit(Process process, int timeoutMs)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.WaitForExit(Math.Max(1, timeoutMs));
            }
            catch
            {
                // Best-effort failure shutdown.
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
