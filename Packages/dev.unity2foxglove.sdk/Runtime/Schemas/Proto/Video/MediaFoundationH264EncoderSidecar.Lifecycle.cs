// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Experimental Windows Media Foundation H.264 encoder sidecar.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Experimental Media Foundation H.264 encoder sidecar. RGB frames enter a
    /// bounded worker queue so TrySubmitFrame remains non-blocking like the
    /// other video sidecars.
    /// </summary>
    public sealed partial class MediaFoundationH264EncoderSidecar
    {
        /// <summary>Starts the Windows native H.264 encoder if available.</summary>
        public bool Start(MediaFoundationH264EncoderOptions options)
        {
            Stop(clearOutputQueue: true);
            if (_encoderWorker != null && _encoderWorker.IsAlive)
            {
                LastError = "Media Foundation H.264 worker is still stopping.";
                return false;
            }
            if (_transform != null || _mfStarted || _comInitialized)
            {
                LastError = "Media Foundation H.264 native cleanup is still pending.";
                return false;
            }
            _options = options ?? new MediaFoundationH264EncoderOptions();
            _maxOutputQueue = Math.Max(1, _options.MaxOutputQueue);
            Interlocked.Exchange(ref _evictedTimestampCount, 0);
            Interlocked.Exchange(ref _droppedInputFrames, 0);
            LastError = null;
            LastDiagnosticLine = null;

            if (!IsWindows)
            {
                LastError = "Windows Media Foundation H.264 is only available on Windows.";
                return false;
            }

            if (!_options.Validate(out var error))
            {
                LastError = error;
                return false;
            }

            _workerExited.Reset();
            _workerInitialized.Reset();
            Volatile.Write(ref _stopRequested, 0);

            try
            {
                _encoderWorker = new Thread(EncoderWorkerMain)
                {
                    IsBackground = true,
                    Name = "Foxglove-MediaFoundation-H264"
                };
                _encoderWorker.Start();
                if (!_workerInitialized.Wait(StartupTimeoutMs))
                {
                    LastError = "Media Foundation H.264 worker initialization timed out.";
                    LastDiagnosticLine = LastError;
                    Stop(clearOutputQueue: true);
                    return false;
                }

                if (!IsRunning)
                {
                    if (string.IsNullOrWhiteSpace(LastError))
                        LastError = "Media Foundation H.264 worker failed to initialize.";
                    Stop(clearOutputQueue: true);
                    return false;
                }

                LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, "Windows Media Foundation H.264 encoder started.");
                return true;
            }
            catch (Exception ex)
            {
                LastError = DescribeException(ex);
                LastDiagnosticLine = LastError;
                Stop(clearOutputQueue: true);
                return false;
            }
        }

        /// <summary>Submits an RGB24 frame without blocking the caller.</summary>
        public bool TrySubmitFrame(byte[] rgb24Frame)
            => TrySubmitFrame(rgb24Frame, 0UL);

        public bool TrySubmitFrame(byte[] rgb24Frame, ulong timestampNs)
            => TrySubmitFrameCore(new CameraVideoArrayFrameBytesSource(rgb24Frame), timestampNs);

        bool ICameraVideoFrameSourceSidecar.TrySubmitFrame<TFrameBytes>(TFrameBytes frame, ulong timestampNs)
        {
            return TrySubmitFrameCore(frame, timestampNs);
        }

        private bool TrySubmitFrameCore<TFrameBytes>(TFrameBytes frame, ulong timestampNs)
            where TFrameBytes : struct, ICameraVideoFrameBytesSource
        {
            if (!IsRunning)
            {
                LastError = "Media Foundation H.264 encoder is not running.";
                return false;
            }

            var expectedBytes = _options != null ? _options.Rgb24FrameByteCount : 0;
            if (expectedBytes <= 0)
            {
                LastError = "Media Foundation encoder dimensions produce an invalid RGB24 frame size.";
                return false;
            }

            if (frame.Length != expectedBytes)
            {
                LastError = "RGB24 frame byte count does not match Media Foundation encoder dimensions.";
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

            return EnqueueInputFrame(copy, expectedBytes, timestampNs);
        }

        private bool EnqueueInputFrame(byte[] copy, int frameLength, ulong timestampNs)
        {
            lock (_inputLock)
            {
                if (!IsRunning)
                {
                    ArrayPool<byte>.Shared.Return(copy);
                    return false;
                }

                while (_inputCount >= MaxInputQueueCapacity
                    && _inputFrames.TryDequeue(out var dropped))
                {
                    _inputCount--;
                    ReturnInputFrameBuffer(dropped);
                    Interlocked.Increment(ref _droppedInputFrames);
                }

                _inputFrames.Enqueue(new QueuedInputFrame(copy, frameLength, timestampNs));
                _inputCount++;
            }

            _inputSignal.Set();
            return true;
        }

        private void EncoderWorkerMain()
        {
            try
            {
                InitializeMediaFoundation();
                ConfigureEncoder(_options);
                if (Volatile.Read(ref _stopRequested) != 0)
                    return;

                IsRunning = true;
                _workerInitialized.Set();
                EncoderWorkerLoop();
            }
            catch (Exception ex)
            {
                LastError = DescribeException(ex);
                LastDiagnosticLine = LastError;
                IsRunning = false;
                _workerInitialized.Set();
            }
            finally
            {
                IsRunning = false;
                DrainInputQueue();
                ReleaseEncoderResources();
                _workerInitialized.Set();
                _workerExited.Set();
            }
        }

        private void EncoderWorkerLoop()
        {
            while (IsRunning || Volatile.Read(ref _inputCount) > 0)
                {
                    QueuedInputFrame frame;
                    lock (_inputLock)
                    {
                        if (!_inputFrames.TryDequeue(out frame))
                        {
                            frame = default;
                        }
                        else
                        {
                            _inputCount--;
                        }
                    }

                    if (frame.Data == null)
                    {
                        _inputSignal.WaitOne(50);
                        continue;
                    }

                    try
                    {
                        var nv12Frame = EnsureNv12Scratch();
                        if (!Rgb24ToNv12Converter.TryConvertRgb24ToNv12(
                            frame.Data,
                            frame.Length,
                            _options.Width,
                            _options.Height,
                            nv12Frame,
                            flipVertical: true,
                            out var conversionError))
                            throw new InvalidOperationException(conversionError);
                        ProcessInputFrame(nv12Frame, frame.TimestampNs);
                        DrainEncoderOutput();
                    }
                    catch (Exception ex)
                    {
                        LastError = DescribeException(ex);
                        LastDiagnosticLine = LastError;
                        IsRunning = false;
                        DrainInputQueue();
                        break;
                    }
                    finally
                    {
                        ReturnInputFrameBuffer(frame);
                    }
                }
        }

        private readonly struct QueuedInputFrame
        {
            internal QueuedInputFrame(byte[] data, int length, ulong timestampNs)
            {
                Data = data;
                Length = length;
                TimestampNs = timestampNs;
            }

            internal byte[] Data { get; }
            internal int Length { get; }
            internal ulong TimestampNs { get; }
        }

        /// <summary>Dequeues a completed H.264 access unit, if available.</summary>
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

    }
}
