// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove/Assets/Experimental/OpenH264
// Purpose: Demo-only managed sidecar for Phase 80 OpenH264 source spike.

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Launches the locally built OpenH264 probe helper process and exposes
/// completed H.264 access units through non-blocking queues.
/// </summary>
public sealed class OpenH264ProbeSidecar : IDisposable
{
    private const int MaxAccessUnitBytes = 16 * 1024 * 1024;

    private readonly ConcurrentQueue<byte[]> _inputFrames = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<byte[]> _outputAccessUnits = new ConcurrentQueue<byte[]>();
    private readonly object _lifecycleLock = new object();
    private readonly object _outputLock = new object();
    private Process _process;
    private CancellationTokenSource _stop;
    private Task _stdinTask;
    private Task _stdoutTask;
    private Task _stderrTask;
    private OpenH264ProbeSidecarOptions _options;
    private int _inputCount;
    private int _outputCount;
    private int _framesSubmitted;
    private int _accessUnitsReceived;
    private int _droppedInputFrames;

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

    public int FramesSubmitted => Volatile.Read(ref _framesSubmitted);
    public int AccessUnitsReceived => Volatile.Read(ref _accessUnitsReceived);
    public int DroppedInputFrames => Volatile.Read(ref _droppedInputFrames);
    public string LastStderrLine { get; private set; }
    public string LastError { get; private set; }

    public bool Start(OpenH264ProbeSidecarOptions options)
    {
        if (IsRunning)
            return true;

        Stop();

        _options = options ?? new OpenH264ProbeSidecarOptions();
        LastError = null;
        LastStderrLine = null;

        if (!_options.Validate(out var error))
        {
            LastError = error;
            return false;
        }

        try
        {
            _process = new Process
            {
                StartInfo = CreateStartInfo(_options),
                EnableRaisingEvents = true
            };

            if (!_process.Start())
            {
                LastError = "OpenH264 helper process failed to start.";
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
            LastError = "OpenH264 helper executable was not found or could not be started: " + ex.Message;
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

    public bool TrySubmitFrame(byte[] i420Frame)
    {
        if (i420Frame == null || i420Frame.Length == 0 || !IsRunning)
            return false;

        var expectedBytes = _options != null ? _options.FrameByteCount : 0;
        if (expectedBytes > 0 && i420Frame.Length != expectedBytes)
        {
            LastError = "I420 frame byte count does not match encoder dimensions.";
            return false;
        }

        var capacity = Math.Max(1, _options?.MaxInputQueue ?? 2);
        while (Volatile.Read(ref _inputCount) >= capacity && _inputFrames.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _inputCount);
            Interlocked.Increment(ref _droppedInputFrames);
        }

        var copy = new byte[i420Frame.Length];
        Buffer.BlockCopy(i420Frame, 0, copy, 0, i420Frame.Length);
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
        CancellationTokenSource stop;
        Process process;
        Task stdinTask;
        Task stdoutTask;
        Task stderrTask;
        lock (_lifecycleLock)
        {
            stop = _stop;
            process = _process;
            stdinTask = _stdinTask;
            stdoutTask = _stdoutTask;
            stderrTask = _stderrTask;

            _process = null;
            _stdinTask = null;
            _stdoutTask = null;
            _stderrTask = null;
            _stop = null;
        }

        if (stop != null && !stop.IsCancellationRequested)
            stop.Cancel();

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

            WaitForWorkerTasks(stdinTask, stdoutTask, stderrTask);

            try
            {
                process.Dispose();
            }
            catch
            {
            }
        }
        else
        {
            WaitForWorkerTasks(stdinTask, stdoutTask, stderrTask);
        }

        stop?.Dispose();
        DrainQueues();
    }

    public void Dispose()
    {
        Stop();
    }

    private static ProcessStartInfo CreateStartInfo(OpenH264ProbeSidecarOptions options)
    {
        var args = string.Join(" ", new[]
        {
            "--width " + options.Width.ToString(CultureInfo.InvariantCulture),
            "--height " + options.Height.ToString(CultureInfo.InvariantCulture),
            "--fps " + options.FrameRate.ToString(CultureInfo.InvariantCulture),
            "--bitrate-kbps " + options.BitrateKbps.ToString(CultureInfo.InvariantCulture),
            "--keyint " + options.KeyframeInterval.ToString(CultureInfo.InvariantCulture)
        });

        return new ProcessStartInfo
        {
            FileName = options.HelperExecutablePath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
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
        try
        {
            var stream = _process.StandardOutput.BaseStream;
            while (!token.IsCancellationRequested && IsRunning)
            {
                var readLength = await ReadLittleEndianLength(stream, token).ConfigureAwait(false);
                if (!readLength.Success)
                    break;

                var length = readLength.Length;
                if (length <= 0 || length > MaxAccessUnitBytes)
                {
                    LastError = "OpenH264 helper emitted an invalid access-unit length: " + length;
                    Stop();
                    return;
                }

                var payload = new byte[length];
                if (!await ReadExact(stream, payload, token).ConfigureAwait(false))
                {
                    LastError = "OpenH264 helper stdout ended mid access unit.";
                    Stop();
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

    private async Task RunStderrReader(CancellationToken token)
    {
        try
        {
            var reader = _process.StandardError;
            while (!token.IsCancellationRequested && IsRunning)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                    break;

                LastStderrLine = line;
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

    private static void WaitForWorkerTasks(params Task[] tasks)
    {
        if (tasks == null || tasks.Length == 0)
            return;

        var currentTaskId = Task.CurrentId;
        foreach (var task in tasks)
        {
            if (task == null || task.IsCompleted)
                continue;

            if (currentTaskId.HasValue && task.Id == currentTaskId.Value)
                continue;

            try
            {
                task.Wait(500);
            }
            catch
            {
            }
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

    private void DrainQueues()
    {
        while (_inputFrames.TryDequeue(out _)) { }
        while (_outputAccessUnits.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _inputCount, 0);
        Interlocked.Exchange(ref _outputCount, 0);
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

public sealed class OpenH264ProbeSidecarOptions
{
    public const int MaxDimension = 4096;
    public const int MaxFrameBytes = 32 * 1024 * 1024;

    public string HelperExecutablePath { get; set; } = "";
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 480;
    public int FrameRate { get; set; } = 30;
    public int BitrateKbps { get; set; } = 4000;
    public int KeyframeInterval { get; set; } = 30;
    public int MaxInputQueue { get; set; } = 2;
    public int MaxOutputQueue { get; set; } = 4;

    public int FrameByteCount
    {
        get
        {
            return TryComputeFrameByteCount(Width, Height, out var frameByteCount, out _)
                ? frameByteCount
                : 0;
        }
    }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(HelperExecutablePath))
        {
            error = "OpenH264 helper executable path is empty.";
            return false;
        }

        if (!File.Exists(HelperExecutablePath))
        {
            error = "OpenH264 helper executable does not exist: " + HelperExecutablePath;
            return false;
        }

        if (Width <= 0 || Height <= 0 || (Width % 2) != 0 || (Height % 2) != 0)
        {
            error = "OpenH264 helper requires positive even width and height.";
            return false;
        }

        if (!TryComputeFrameByteCount(Width, Height, out _, out error))
            return false;

        if (FrameRate <= 0 || BitrateKbps <= 0 || KeyframeInterval <= 0)
        {
            error = "OpenH264 helper requires positive frame rate, bitrate, and keyframe interval.";
            return false;
        }

        error = "";
        return true;
    }

    public static bool TryComputeFrameByteCount(int width, int height, out int frameByteCount, out string error)
    {
        frameByteCount = 0;
        if (width <= 0 || height <= 0 || (width % 2) != 0 || (height % 2) != 0)
        {
            error = "OpenH264 helper requires positive even width and height.";
            return false;
        }

        if (width > MaxDimension || height > MaxDimension)
        {
            error = $"OpenH264 probe dimensions must be <= {MaxDimension}x{MaxDimension}.";
            return false;
        }

        var pixels = (long)width * height;
        var bytes = pixels * 3 / 2;
        if (bytes > MaxFrameBytes)
        {
            error = $"OpenH264 probe I420 frame budget exceeded ({bytes} bytes > {MaxFrameBytes} bytes).";
            return false;
        }

        frameByteCount = (int)bytes;
        error = "";
        return true;
    }
}
