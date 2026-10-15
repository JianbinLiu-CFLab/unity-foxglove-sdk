// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: External OpenH264 helper process wrapper with bounded queues.

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
    /// <summary>Explicit ownership/allocation contract for one OpenH264 session.</summary>
    internal readonly struct OpenH264AllocationContract
    {
        public OpenH264AllocationContract(
            int inputOwnershipCopies,
            int pooledConversionBuffers,
            int protocolHeaderAllocationsPerSession,
            int encodedOutputOwnershipAllocationsPerAccessUnit)
        {
            InputOwnershipCopies = inputOwnershipCopies;
            PooledConversionBuffers = pooledConversionBuffers;
            ProtocolHeaderAllocationsPerSession = protocolHeaderAllocationsPerSession;
            EncodedOutputOwnershipAllocationsPerAccessUnit = encodedOutputOwnershipAllocationsPerAccessUnit;
        }

        public int InputOwnershipCopies { get; }
        public int PooledConversionBuffers { get; }
        public int ProtocolHeaderAllocationsPerSession { get; }
        public int EncodedOutputOwnershipAllocationsPerAccessUnit { get; }
    }

    /// <summary>
    /// Describes the intentional OpenH264 ownership boundary: pooled input and
    /// conversion scratch, one reusable protocol header, and one owned output
    /// byte array per encoded access unit.
    /// </summary>
    internal static class OpenH264AllocationPolicy
    {
        public const int ProtocolHeaderBytes = 12;

        public static OpenH264AllocationContract Contract(bool rgbInput)
            => new OpenH264AllocationContract(
                inputOwnershipCopies: 1,
                pooledConversionBuffers: rgbInput ? 1 : 0,
                protocolHeaderAllocationsPerSession: 1,
                encodedOutputOwnershipAllocationsPerAccessUnit: 1);
    }

    /// <summary>
    /// Encodes I420 frames through an external OpenH264 helper process and
    /// exposes completed H.264 Annex B access units.
    /// </summary>
    /// <remarks>Phase192 source sections: WorkerLifecycle.cs, ProtocolAndOutput.cs</remarks>
    public sealed partial class OpenH264EncoderSidecar : ICameraVideoEncoderSidecar, ITimestampedCameraVideoEncoderSidecar, ICameraVideoRgbFrameSourceSidecar
    {
        private const int MaxAccessUnitBytes = 16 * 1024 * 1024;
        private const int ShutdownTimeoutMs = 500;
        private const string ProtocolMarker = "OPENH264_PROBE_PROTOCOL 2";
        // Only a legacy helper waits the full window; v2 helpers advertise before loading OpenH264.
        private const int DefaultProtocolNegotiationTimeoutMs = 2000;
        private const string ProtocolMismatchError =
            "OpenH264 helper advertised protocol 2 after legacy framing was selected; stopping the encoder session.";
        private int _protocolNegotiationTimeoutMs = DefaultProtocolNegotiationTimeoutMs;
        private int _helperProtocolVersion = 1;
        private int _protocolNegotiationState;
        private readonly object _protocolNegotiationLock = new object();
        private TaskCompletionSource<bool> _helperProtocolReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
        private long _writtenFrameCount;
        private long _processingFrameCount;
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
        public int InputQueueDepth => Volatile.Read(ref _inputCount);
        public int MaxInputQueue => Volatile.Read(ref _maxInputQueue);
        internal int PendingTimestampCountForTests
            => _encodedFrameTimestamps.Count
                + (int)Math.Min(int.MaxValue, Interlocked.Read(ref _writtenFrameCount))
                + (int)Math.Min(int.MaxValue, Interlocked.Read(ref _processingFrameCount));
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
                    Volatile.Write(ref _helperProtocolVersion, 1);
                    Volatile.Write(ref _protocolNegotiationState, 0);
                    _helperProtocolReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Interlocked.Exchange(ref _writtenFrameCount, 0);
                    Interlocked.Exchange(ref _processingFrameCount, 0);
                    var process = _process;
                    var token = _stop.Token;
                    var sessionId = Interlocked.Increment(ref _sessionId);
                    _stdinTask = Task.Run(() => RunStdinWriter(process, token));
                    _stdoutTask = Task.Run(() => RunStdoutReaderForSession(process, token, sessionId));
                    _stderrTask = Task.Run(() => RunStderrReaderForSession(process, token, sessionId));
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

            var copy = ArrayPool<byte>.Shared.Rent(frame.Length);
            Buffer.BlockCopy(frame, 0, copy, 0, frame.Length);

            lock (_inputLock)
            {
                if (!ReferenceEquals(submittingProcess, Volatile.Read(ref _process))
                    || !IsProcessRunning(submittingProcess))
                {
                    ArrayPool<byte>.Shared.Return(copy);
                    return false;
                }

                while (_inputCount >= _maxInputQueue && _inputFrames.TryDequeue(out var dropped))
                {
                    ReturnInputFrameBuffer(dropped);
                    _inputCount--;
                    Interlocked.Increment(ref _droppedInputFrames);
                }

                // Pending raw frames and written-but-unpaired frames share one finite budget.
                if ((long)_inputCount + PendingTimestampCountForTests >= (long)_maxInputQueue + _maxOutputQueue)
                {
                    ArrayPool<byte>.Shared.Return(copy);
                    return false;
                }

                _inputFrames.Enqueue(new QueuedVideoFrame(copy, timestampNs, pooled: true, length: frame.Length));
                _inputCount++;
            }

            Interlocked.Increment(ref _framesSubmitted);
            return true;
        }

        bool ICameraVideoRgbFrameSourceSidecar.TrySubmitRgbFrame<TFrameBytes>(TFrameBytes frame, ulong timestampNs)
        {
            var submittingProcess = Volatile.Read(ref _process);
            if (frame.Length <= 0 || !IsProcessRunning(submittingProcess))
                return false;

            var expectedBytes = _options != null ? _options.Rgb24FrameByteCount : 0;
            if (expectedBytes <= 0)
            {
                LastError = "OpenH264 encoder dimensions produce an invalid RGB24 frame size.";
                return false;
            }

            if (frame.Length != expectedBytes)
            {
                LastError = "RGB24 frame byte count does not match OpenH264 encoder dimensions.";
                return false;
            }

            var copy = ArrayPool<byte>.Shared.Rent(expectedBytes);
            try
            {
                frame.CopyTo(copy);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(copy);
                throw;
            }

            lock (_inputLock)
            {
                if (!ReferenceEquals(submittingProcess, Volatile.Read(ref _process))
                    || !IsProcessRunning(submittingProcess))
                {
                    ArrayPool<byte>.Shared.Return(copy);
                    return false;
                }

                while (_inputCount >= _maxInputQueue && _inputFrames.TryDequeue(out var dropped))
                {
                    ReturnInputFrameBuffer(dropped);
                    _inputCount--;
                    Interlocked.Increment(ref _droppedInputFrames);
                }

                if ((long)_inputCount + PendingTimestampCountForTests >= (long)_maxInputQueue + _maxOutputQueue)
                {
                    ArrayPool<byte>.Shared.Return(copy);
                    return false;
                }

                _inputFrames.Enqueue(new QueuedVideoFrame(copy, timestampNs, pooled: true, length: expectedBytes, isRgb24: true));
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

    }
}
