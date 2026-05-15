// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: External FFmpeg H.265/HEVC encoder process wrapper with bounded queues.

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
    /// Encodes RGB24 frames through an external FFmpeg process and exposes completed
    /// HEVC Annex B access units through a thread-safe bounded output queue.
    /// </summary>
    public sealed class FfmpegH265EncoderSidecar : IFfmpegVideoEncoderSidecar
    {
        private readonly ConcurrentQueue<byte[]> _inputFrames = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _outputAccessUnits = new ConcurrentQueue<byte[]>();
        private readonly object _outputLock = new object();
        private Process _process;
        private CancellationTokenSource _stop;
        private Task _stdinTask;
        private Task _stdoutTask;
        private Task _stderrTask;
        private FfmpegH265EncoderOptions _options;
        private H265AnnexBAccessUnitPacketizer _packetizer;
        private int _inputCount;
        private int _outputCount;
        private long _framesSubmitted;
        private long _accessUnitsProduced;
        private long _accessUnitsDropped;

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
        public long AccessUnitsProduced => Interlocked.Read(ref _accessUnitsProduced);
        public long AccessUnitsDropped => Interlocked.Read(ref _accessUnitsDropped);
        public string LastStderrLine { get; private set; }
        public string LastError { get; private set; }

        /// <summary>Starts FFmpeg if it is not already running.</summary>
        public bool Start(FfmpegH265EncoderOptions options)
        {
            if (IsRunning)
                return true;

            Stop();

            _options = options ?? new FfmpegH265EncoderOptions();
            _packetizer = new H265AnnexBAccessUnitPacketizer();
            LastError = null;

            try
            {
                _process = new Process
                {
                    StartInfo = _options.CreateStartInfo(),
                    EnableRaisingEvents = true
                };

                if (!_process.Start())
                {
                    LastError = "FFmpeg process failed to start.";
                    Stop();
                    return false;
                }

                _stop = new CancellationTokenSource();
                _stdinTask = Task.Run(() => RunStdinWriter(_stop.Token));
                _stdoutTask = Task.Run(() => RunStdoutReader(_stop.Token));
                _stderrTask = Task.Run(() => RunStderrReader(_stop.Token));
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
        {
            if (rgb24Frame == null || rgb24Frame.Length == 0 || !IsRunning)
                return false;

            var expectedBytes = _options != null ? _options.FrameByteCount : 0;
            if (expectedBytes > 0 && rgb24Frame.Length != expectedBytes)
            {
                LastError = "RGB24 frame byte count does not match encoder dimensions.";
                return false;
            }

            var capacity = Math.Max(1, _options?.MaxInputQueue ?? 2);
            while (Volatile.Read(ref _inputCount) >= capacity && _inputFrames.TryDequeue(out _))
                Interlocked.Decrement(ref _inputCount);

            var copy = new byte[rgb24Frame.Length];
            Buffer.BlockCopy(rgb24Frame, 0, copy, 0, rgb24Frame.Length);
            _inputFrames.Enqueue(copy);
            Interlocked.Increment(ref _inputCount);
            Interlocked.Increment(ref _framesSubmitted);
            return true;
        }

        /// <summary>Dequeues a completed HEVC access unit, if available.</summary>
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

        /// <summary>Stops FFmpeg and clears pending live queues.</summary>
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
                    process.WaitForExit(200);
                }
                catch
                {
                    // Ignore wait failures during best-effort shutdown.
                }

                process.Dispose();
            }

            _process = null;
            _stdinTask = null;
            _stdoutTask = null;
            _stderrTask = null;
            _stop?.Dispose();
            _stop = null;
            DrainQueues();
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task RunStdinWriter(CancellationToken token)
        {
            try
            {
                var stream = _process.StandardInput.BaseStream;
                while (!token.IsCancellationRequested && IsRunning)
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

        private async Task RunStdoutReader(CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                var stream = _process.StandardOutput.BaseStream;
                while (!token.IsCancellationRequested && IsRunning)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    var chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                    _packetizer.Append(chunk);
                    DrainPacketizer();
                }

                if (_packetizer != null && _packetizer.Flush(out var finalUnit))
                    EnqueueAccessUnit(finalUnit);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private async Task RunStderrReader(CancellationToken token)
        {
            try
            {
                var reader = _process.StandardError;
                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                        break;

                    LastStderrLine = line;
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    LastError = ex.Message;
            }
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
                var capacity = Math.Max(1, _options?.MaxOutputQueue ?? 4);
                while (Volatile.Read(ref _outputCount) >= capacity && _outputAccessUnits.TryDequeue(out _))
                {
                    Interlocked.Decrement(ref _outputCount);
                    Interlocked.Increment(ref _accessUnitsDropped);
                }

                _outputAccessUnits.Enqueue(accessUnit);
                Interlocked.Increment(ref _outputCount);
                Interlocked.Increment(ref _accessUnitsProduced);
            }
        }

        private void DrainQueues()
        {
            while (_inputFrames.TryDequeue(out _))
            {
            }

            lock (_outputLock)
            {
                while (_outputAccessUnits.TryDequeue(out _))
                {
                }
            }

            Volatile.Write(ref _inputCount, 0);
            Volatile.Write(ref _outputCount, 0);
        }
    }
}
