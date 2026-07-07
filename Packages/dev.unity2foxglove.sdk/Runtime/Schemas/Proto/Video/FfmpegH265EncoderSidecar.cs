// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: External FFmpeg H.265/HEVC encoder process wrapper with bounded queues.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Encodes RGB24 frames through an external FFmpeg process and exposes completed
    /// HEVC Annex B access units through a thread-safe bounded output queue.
    /// </summary>
    public sealed class FfmpegH265EncoderSidecar : IFfmpegVideoEncoderSidecar, ITimestampedCameraVideoEncoderSidecar
    {
        private const int ShutdownTimeoutMs = 500;

        private readonly ConcurrentQueue<QueuedVideoFrame> _inputFrames = new ConcurrentQueue<QueuedVideoFrame>();
        private readonly ConcurrentQueue<ulong> _encodedFrameTimestamps = new ConcurrentQueue<ulong>();
        private readonly ConcurrentQueue<EncodedVideoAccessUnit> _outputAccessUnits = new ConcurrentQueue<EncodedVideoAccessUnit>();
        private readonly object _inputLock = new object();
        private readonly object _outputLock = new object();
        private readonly SemaphoreSlim _inputSignal = new SemaphoreSlim(0);
        private Process _process;
        private CancellationTokenSource _stop;
        private Task _stdinTask;
        private Task _stdoutTask;
        private Task _stderrTask;
        private FfmpegH265EncoderOptions _options;
        private H265AnnexBAccessUnitPacketizer _packetizer;
        private int _maxInputQueue = 2;
        private int _maxOutputQueue = 4;
        private int _inputCount;
        private int _outputCount;
        private long _framesSubmitted;
        private long _accessUnitsProduced;
        private long _accessUnitsDropped;
        private long _timestampQueueUnderflows;
        private string _lastStderrLine;
        private string _lastError;

        public bool IsRunning
        {
            get
            {
                var process = Volatile.Read(ref _process);
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
        public long AccessUnitsProduced => Interlocked.Read(ref _accessUnitsProduced);
        public long AccessUnitsDropped => Interlocked.Read(ref _accessUnitsDropped);
        public long TimestampQueueUnderflows => Interlocked.Read(ref _timestampQueueUnderflows);
        public int OutputQueueDepth => Volatile.Read(ref _outputCount);
        public int MaxOutputQueue => Volatile.Read(ref _maxOutputQueue);
        public string LastStderrLine
        {
            get => Volatile.Read(ref _lastStderrLine);
            private set => Volatile.Write(ref _lastStderrLine, value);
        }
        public string LastDiagnosticLine => LastStderrLine ?? LastError;
        public string LastError
        {
            get => Volatile.Read(ref _lastError);
            private set => Volatile.Write(ref _lastError, value);
        }

        /// <summary>Starts FFmpeg if it is not already running.</summary>
        public bool Start(FfmpegH265EncoderOptions options)
        {
            if (IsRunning)
                return true;

            Stop(clearOutputQueue: true);

            _options = options ?? new FfmpegH265EncoderOptions();
            _packetizer = new H265AnnexBAccessUnitPacketizer();
            LastError = null;

            if (!_options.Validate(out var validationError))
            {
                LastError = validationError;
                return false;
            }

            _maxInputQueue = Math.Max(1, _options.MaxInputQueue);
            _maxOutputQueue = Math.Max(1, _options.MaxOutputQueue);

            try
            {
                var process = new Process
                {
                    StartInfo = _options.CreateStartInfo(),
                    EnableRaisingEvents = true
                };
                Volatile.Write(ref _process, process);

                if (!process.Start())
                {
                    LastError = "FFmpeg process failed to start.";
                    Stop();
                    return false;
                }

                var stop = new CancellationTokenSource();
                Volatile.Write(ref _stop, stop);
                var token = stop.Token;
                var frameBytes = _options.FrameByteCount;
                _stdinTask = Task.Run(() => RunStdinWriter(process, token, frameBytes));
                _stdoutTask = Task.Run(() => RunStdoutReader(process, token));
                _stderrTask = Task.Run(() => RunStderrReader(process, token));
                return true;
            }
            catch (Win32Exception ex)
            {
                LastError = BuildStartFailureMessage(_options?.FfmpegPath, ex.Message);
                Stop();
                return false;
            }
            catch (FileNotFoundException ex)
            {
                LastError = BuildStartFailureMessage(_options?.FfmpegPath, ex.Message);
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

        private static string BuildStartFailureMessage(string ffmpegPath, string detail)
        {
            var configured = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath.Trim();
            var suffix = string.IsNullOrEmpty(detail) ? "" : " Detail: " + detail;
            if (string.Equals(configured, "ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                return "FFmpeg executable was not found in the Unity process PATH. "
                    + "Leave FFmpeg Path empty only when Unity can resolve ffmpeg; otherwise use the FFmpeg Path ... button to select ffmpeg.exe. "
                    + "Restart Unity after changing PATH."
                    + suffix;
            }

            return "FFmpeg executable was not found at the configured FFmpeg Path: "
                + configured
                + ". Use the FFmpeg Path ... button to select a valid executable."
                + suffix;
        }

        /// <summary>
        /// Submits a raw RGB24 frame without blocking the caller on FFmpeg I/O.
        /// Old input frames are dropped if the queue is already full.
        /// </summary>
        public bool TrySubmitFrame(byte[] rgb24Frame)
            => TrySubmitFrame(rgb24Frame, 0UL);

        public bool TrySubmitFrame(byte[] rgb24Frame, ulong timestampNs)
        {
            if (rgb24Frame == null || rgb24Frame.Length == 0 || !IsRunning)
                return false;

            var expectedBytes = _options != null ? _options.FrameByteCount : 0;
            if (expectedBytes <= 0)
            {
                LastError = "FFmpeg H.265 encoder dimensions produce an invalid RGB24 frame size.";
                return false;
            }

            if (rgb24Frame.Length != expectedBytes)
            {
                LastError = "RGB24 frame byte count does not match encoder dimensions.";
                return false;
            }

            var copy = ArrayPool<byte>.Shared.Rent(rgb24Frame.Length);
            Buffer.BlockCopy(rgb24Frame, 0, copy, 0, rgb24Frame.Length);

            lock (_inputLock)
            {
                while (_inputCount >= _maxInputQueue && _inputFrames.TryDequeue(out var dropped))
                {
                    _inputCount--;
                    ReturnInputFrameBuffer(dropped);
                }

                _inputFrames.Enqueue(new QueuedVideoFrame(copy, timestampNs));
                _inputCount++;
            }

            _inputSignal.Release();
            Interlocked.Increment(ref _framesSubmitted);
            return true;
        }

        /// <summary>Dequeues a completed HEVC access unit, if available.</summary>
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

        /// <summary>Stops FFmpeg and clears pending live queues.</summary>
        public void Stop()
        {
            Stop(clearOutputQueue: true);
        }

        private void Stop(bool clearOutputQueue)
        {
            var stop = Interlocked.Exchange(ref _stop, null);
            if (stop != null && !stop.IsCancellationRequested)
                stop.Cancel();

            var process = Interlocked.Exchange(ref _process, null);
            var stdinTask = Interlocked.Exchange(ref _stdinTask, null);
            var stdoutTask = Interlocked.Exchange(ref _stdoutTask, null);
            var stderrTask = Interlocked.Exchange(ref _stderrTask, null);
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.StandardInput.BaseStream.Close();
                }
                catch
                {
                    // Best-effort shutdown.
                }

                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                    // Process may already have exited.
                }

                try
                {
                    process.WaitForExit(ShutdownTimeoutMs);
                }
                catch
                {
                    // Ignore wait failures during best-effort shutdown.
                }

                WaitForTask(stdinTask, "stdin");
                WaitForTask(stdoutTask, "stdout");
                WaitForTask(stderrTask, "stderr");
                process.Dispose();
            }

            stop?.Dispose();
            DrainInputQueue();
            if (clearOutputQueue)
                DrainOutputQueue();
        }

        public void Dispose()
        {
            Stop(clearOutputQueue: false);
        }

        private async Task RunStdinWriter(Process process, CancellationToken token, int frameBytes)
        {
            try
            {
                var stream = process.StandardInput.BaseStream;
                while (!token.IsCancellationRequested && IsProcessRunning(process))
                {
                    await _inputSignal.WaitAsync(token).ConfigureAwait(false);
                    while (TryDequeueInputFrame(out var frame))
                    {
                        try
                        {
                            _encodedFrameTimestamps.Enqueue(frame.TimestampNs);
                            await stream.WriteAsync(frame.Data, 0, frameBytes, token).ConfigureAwait(false);
                            await stream.FlushAsync(token).ConfigureAwait(false);
                        }
                        finally
                        {
                            ReturnInputFrameBuffer(frame);
                        }
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
            var buffer = new byte[16 * 1024];
            try
            {
                var stream = process.StandardOutput.BaseStream;
                while (!token.IsCancellationRequested && IsProcessRunning(process))
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    _packetizer.Append(buffer, 0, read);
                    DrainPacketizer();
                }

                if (_packetizer != null && _packetizer.Flush(out var finalUnit))
                {
                    EnqueueAccessUnit(finalUnit);
                    DrainPacketizer();
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
                await ReadBoundedDiagnosticStream(
                    process.StandardError.BaseStream,
                    line => LastStderrLine = line,
                    token,
                    Math.Max(1, _options?.MaxStderrLineBytes ?? 8192),
                    Math.Max(1, _options?.MaxStderrRetainedBytes ?? 8192)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    LastError = ex.Message;
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

        private void DrainPacketizer()
        {
            while (_packetizer.TryDequeueAccessUnit(out var accessUnit))
                EnqueueAccessUnit(accessUnit);
        }

        private void EnqueueAccessUnit(byte[] accessUnit)
        {
            if (accessUnit == null || accessUnit.Length == 0)
                return;

            lock (_outputLock)
            {
                if (_outputCount >= _maxOutputQueue)
                {
                    LastStderrLine = "FFmpeg H.265 output queue full; capture admission is holding new frames.";
                    Interlocked.Increment(ref _accessUnitsDropped);
                    return;
                }

                // FFmpeg's rawvideo pipe carries no per-frame PTS. With zerolatency
                // and B-frames disabled, output order is expected to match input order;
                // this queue remains an accepted approximation until a PTS-bearing
                // sidecar protocol replaces the rawvideo stdin/stdout contract.
                var timestampNs = 0UL;
                if (_encodedFrameTimestamps.TryDequeue(out var capturedNs))
                {
                    timestampNs = capturedNs;
                }
                else
                {
                    Interlocked.Increment(ref _timestampQueueUnderflows);
                    LastStderrLine = "FFmpeg H.265 access unit had no queued timestamp.";
                }
                _outputAccessUnits.Enqueue(new EncodedVideoAccessUnit(accessUnit, timestampNs));
                _outputCount++;
                Interlocked.Increment(ref _accessUnitsProduced);
            }
        }

        private void DrainInputQueue()
        {
            lock (_inputLock)
            {
                while (_inputFrames.TryDequeue(out var frame))
                {
                    ReturnInputFrameBuffer(frame);
                }

                _inputCount = 0;
            }

            while (_inputSignal.Wait(0))
            {
            }

            while (_encodedFrameTimestamps.TryDequeue(out _))
            {
            }
        }

        private bool TryDequeueInputFrame(out QueuedVideoFrame frame)
        {
            lock (_inputLock)
            {
                if (!_inputFrames.TryDequeue(out frame))
                    return false;

                if (_inputCount > 0)
                    _inputCount--;
                return true;
            }
        }

        private static void ReturnInputFrameBuffer(QueuedVideoFrame frame)
        {
            if (frame.Data != null && frame.Data.Length > 0)
                ArrayPool<byte>.Shared.Return(frame.Data);
        }

        private void DrainOutputQueue()
        {
            lock (_outputLock)
            {
                while (_outputAccessUnits.TryDequeue(out _))
                {
                }

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

        private void WaitForTask(Task task, string taskName)
        {
            if (task == null || task.IsCompleted)
                return;

            try
            {
                task.Wait(ShutdownTimeoutMs);
            }
            catch
            {
                // Best-effort task shutdown.
            }

            if (!task.IsCompleted)
                LastError = "FFmpeg H.265 shutdown timed out waiting for the " + taskName + " task.";
        }
    }
}
