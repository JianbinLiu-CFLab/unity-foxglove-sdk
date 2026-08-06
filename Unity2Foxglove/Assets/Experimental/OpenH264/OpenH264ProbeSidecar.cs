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
    private bool _stopping;
    private readonly object _outputLock = new object();
    private Process _process;
    private CancellationTokenSource _stop;
    private Task _stdinTask;
    private Task _stdoutTask;
    private Task _stderrTask;
    private OpenH264ProbeSidecarOptions _options;
    private int _outputCount;
    private int _framesSubmitted;
    private int _accessUnitsReceived;
    private int _droppedInputFrames;
    private string _lastStderrLine;
    private string _lastError;

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
    public string LastStderrLine => Volatile.Read(ref _lastStderrLine);
    public string LastError => Volatile.Read(ref _lastError);

    public bool Start(OpenH264ProbeSidecarOptions options)
    {
        if (IsRunning)
            return true;

        Stop();

        _options = options ?? new OpenH264ProbeSidecarOptions();
        SetLastError(null);
        SetLastStderrLine(null);

        if (!_options.Validate(out var error))
        {
            SetLastError(error);
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
                SetLastError("OpenH264 helper process failed to start.");
                Stop();
                return false;
            }

            _stop = new CancellationTokenSource();
            var process = _process;
            _stdinTask = Task.Run(() => RunStdinWriter(process, _stop.Token));
            _stdoutTask = Task.Run(() => RunStdoutReader(process, _stop.Token));
            _stderrTask = Task.Run(() => RunStderrReader(process, _stop.Token));
            return true;
        }
        catch (Win32Exception ex)
        {
            SetLastError("OpenH264 helper executable was not found or could not be started: " + ex.Message);
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

    public bool TrySubmitFrame(byte[] i420Frame)
    {
        if (i420Frame == null || i420Frame.Length == 0 || !IsRunning)
            return false;

        var expectedBytes = _options != null ? _options.FrameByteCount : 0;
        if (expectedBytes > 0 && i420Frame.Length != expectedBytes)
        {
            SetLastError("I420 frame byte count does not match encoder dimensions.");
            return false;
        }

        var capacity = Math.Max(1, _options?.MaxInputQueue ?? 2);
        while (_inputFrames.Count >= capacity && _inputFrames.TryDequeue(out _))
        {
            Interlocked.Increment(ref _droppedInputFrames);
        }

        var copy = new byte[i420Frame.Length];
        Buffer.BlockCopy(i420Frame, 0, copy, 0, i420Frame.Length);
        _inputFrames.Enqueue(copy);
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
        if (!TryCaptureStopState(
                out var stop,
                out var process,
                out var stdinTask,
                out var stdoutTask,
                out var stderrTask))
            return;

        if (stop != null && !stop.IsCancellationRequested)
            stop.Cancel();

        if (process != null)
        {
            CloseProcessStreams(process);

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
            }
        }

        try
        {
            CleanupWorkers(process, stop, stdinTask, stdoutTask, stderrTask);
            DrainQueues();
        }
        finally
        {
            ClearStoppingFlag();
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static ProcessStartInfo CreateStartInfo(OpenH264ProbeSidecarOptions options)
    {
        var openH264DllPath = string.IsNullOrWhiteSpace(options.OpenH264DllPath)
            ? options.OpenH264DllPath
            : Path.GetFullPath(options.OpenH264DllPath);
        var args = string.Join(" ", new[]
        {
            "--width " + options.Width.ToString(CultureInfo.InvariantCulture),
            "--height " + options.Height.ToString(CultureInfo.InvariantCulture),
            "--fps " + options.FrameRate.ToString(CultureInfo.InvariantCulture),
            "--bitrate-kbps " + options.BitrateKbps.ToString(CultureInfo.InvariantCulture),
            "--keyint " + options.KeyframeInterval.ToString(CultureInfo.InvariantCulture),
            "--openh264-dll " + QuoteArgument(openH264DllPath)
        });

        return new ProcessStartInfo
        {
            FileName = Path.GetFullPath(options.HelperExecutablePath),
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
    }

    private bool TryCaptureStopState(
        out CancellationTokenSource stop,
        out Process process,
        out Task stdinTask,
        out Task stdoutTask,
        out Task stderrTask)
    {
        lock (_lifecycleLock)
        {
            while (_stopping)
                Monitor.Wait(_lifecycleLock);

            _stopping = true;
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
            return true;
        }
    }

    private void ClearStoppingFlag()
    {
        lock (_lifecycleLock)
        {
            _stopping = false;
            Monitor.PulseAll(_lifecycleLock);
        }
    }

    private async Task RunStdinWriter(Process process, CancellationToken token)
    {
        try
        {
            var stream = process.StandardInput.BaseStream;
            while (!token.IsCancellationRequested && IsRunning)
            {
                if (_inputFrames.TryDequeue(out var frame))
                {
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
            SetLastError(ex.Message);
        }
    }

    private async Task RunStdoutReader(Process process, CancellationToken token)
    {
        try
        {
            var stream = process.StandardOutput.BaseStream;
            while (!token.IsCancellationRequested && IsRunning)
            {
                var readLength = await ReadLittleEndianLength(stream, token).ConfigureAwait(false);
                if (!readLength.Success)
                    break;

                var length = readLength.Length;
                if (length <= 0 || length > MaxAccessUnitBytes)
                {
                    SetLastError("OpenH264 helper emitted an invalid access-unit length: " + length);
                    StopFromWorker();
                    return;
                }

                var payload = new byte[length];
                if (!await ReadExact(stream, payload, token).ConfigureAwait(false))
                {
                    SetLastError("OpenH264 helper stdout ended mid access unit.");
                    StopFromWorker();
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
            SetLastError(ex.Message);
        }
    }

    private async Task RunStderrReader(Process process, CancellationToken token)
    {
        try
        {
            var reader = process.StandardError;
            while (!token.IsCancellationRequested && IsRunning)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                    break;

                SetLastStderrLine(line);
            }
        }
        catch (Exception ex)
        {
            if (!(ex is ObjectDisposedException))
                SetLastError(ex.Message);
        }
    }

    private void StopFromWorker()
    {
        Task.Run(() => Stop());
    }

    private static void CloseProcessStreams(Process process)
    {
        if (process == null)
            return;

        try
        {
            process.StandardInput.BaseStream.Close();
        }
        catch
        {
        }

        try
        {
            process.StandardOutput.BaseStream.Close();
        }
        catch
        {
        }

        try
        {
            // StreamReader.ReadLineAsync cannot take a CancellationToken on
            // Unity's .NET Standard profile; closing the stream unblocks it.
            process.StandardError.BaseStream.Close();
        }
        catch
        {
        }
    }

    private static void CleanupWorkers(Process process, CancellationTokenSource stop, params Task[] tasks)
    {
        try
        {
            if (process != null)
            {
                try
                {
                    process.WaitForExit(200);
                }
                catch
                {
                }
            }

            WaitForWorkerTasks(tasks);
        }
        finally
        {
            try
            {
                process?.Dispose();
            }
            catch
            {
            }

            stop?.Dispose();
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

        foreach (var task in tasks)
        {
            if (task == null || task.IsCompleted)
                continue;

            try
            {
                Task.WaitAll(new[] { task }, 500);
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

    private static string QuoteArgument(string value)
        => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

    private void DrainQueues()
    {
        while (_inputFrames.TryDequeue(out _)) { }
        while (_outputAccessUnits.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _outputCount, 0);
    }

    private void SetLastError(string value)
    {
        Volatile.Write(ref _lastError, value);
    }

    private void SetLastStderrLine(string value)
    {
        Volatile.Write(ref _lastStderrLine, value);
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
    public string OpenH264DllPath { get; set; } = "";
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

        if (RequiresExplicitOpenH264Dll)
        {
            if (string.IsNullOrWhiteSpace(OpenH264DllPath))
            {
                error = "OpenH264 DLL path is empty.";
                return false;
            }

            if (!File.Exists(OpenH264DllPath))
            {
                error = "OpenH264 DLL does not exist: " + OpenH264DllPath;
                return false;
            }
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

        if (MaxInputQueue <= 0 || MaxOutputQueue <= 0)
        {
            error = "OpenH264 helper queue sizes must be positive.";
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

    private static bool RequiresExplicitOpenH264Dll
    {
        get
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return true;
#else
            return Path.DirectorySeparatorChar == '\\';
#endif
        }
    }
}
