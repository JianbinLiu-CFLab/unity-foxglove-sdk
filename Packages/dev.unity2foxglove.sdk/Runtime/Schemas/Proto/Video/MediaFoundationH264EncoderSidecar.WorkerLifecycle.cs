// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Worker lifecycle for the Windows Media Foundation H.264 encoder sidecar.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Foxglove.Schemas.Video
{
    public sealed partial class MediaFoundationH264EncoderSidecar : ICameraVideoEncoderSidecar, ITimestampedCameraVideoEncoderSidecar, ICameraVideoFrameSourceSidecar, ICameraVideoSidecarDeferredCleanup, ICameraVideoSidecarDeferredCleanupScheduler
    {
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

        bool ICameraVideoSidecarDeferredCleanup.TryFinalizeDeferredCleanup()
        {
            if (IsRunning)
                return false;

            var worker = Volatile.Read(ref _encoderWorker);
            if (worker != null && worker.IsAlive)
                return false;

            if (worker != null)
                Interlocked.CompareExchange(ref _encoderWorker, null, worker);

            if (_transform != null || _mfStarted || _comInitialized)
                return false;

            Interlocked.Exchange(ref _deferredCleanupScheduled, 0);
            var registration = Interlocked.Exchange(ref _deferredCleanupRegistration, null);
            registration?.Unregister(null);
            CameraVideoSidecarRetirementRegistry.Complete(this);
            return true;
        }

        void ICameraVideoSidecarDeferredCleanupScheduler.ScheduleDeferredCleanup(Action callback)
        {
            if (callback == null || Interlocked.Exchange(ref _deferredCleanupScheduled, 1) != 0)
                return;

            var worker = Volatile.Read(ref _encoderWorker);
            if (worker == null || !worker.IsAlive)
            {
                callback();
                return;
            }

            _deferredCleanupRegistration = ThreadPool.RegisterWaitForSingleObject(
                _workerExited,
                (_, __) =>
                {
                    worker.Join();
                    callback();
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: true);
        }

        public void Dispose()
        {
            Stop(clearOutputQueue: false);
        }

        private void Stop(bool clearOutputQueue)
        {
            Volatile.Write(ref _stopRequested, 1);
            IsRunning = false;
            _inputSignal.Set();

            var worker = Volatile.Read(ref _encoderWorker);
            if (worker != null && ReferenceEquals(worker, Thread.CurrentThread))
            {
                DrainInputQueue();
                return;
            }

            if (worker != null)
            {
                if (!worker.Join(ShutdownTimeoutMs))
                {
                    LastDiagnosticLine = AppendDiagnostic(
                        LastDiagnosticLine,
                        "Media Foundation H.264 worker shutdown timed out; native cleanup remains worker-owned.");
                    DrainInputQueue();
                    CameraVideoSidecarRetirementRegistry.Retire(this);
                    return;
                }

                Interlocked.CompareExchange(ref _encoderWorker, null, worker);
            }

            DrainInputQueue();
            ClearManagedEncoderState();

            if (clearOutputQueue)
            {
                _maxOutputQueue = 4;
                DrainOutputQueue();
            }

            var registration = Interlocked.Exchange(ref _deferredCleanupRegistration, null);
            registration?.Unregister(null);
            Interlocked.Exchange(ref _deferredCleanupScheduled, 0);
            CameraVideoSidecarRetirementRegistry.Complete(this);
        }

        private void ClearManagedEncoderState()
        {
            _options = null;
            _nv12Scratch = null;
            _hasOutputStreamInfo = false;
            _nextSampleTime = 0;
            _sampleDuration = 0;
            ClearSampleTimestampMap();
        }

        private void ReleaseEncoderResources()
        {
            if (_transform != null)
            {
                try
                {
                    _transform.ProcessMessage(MftMessageNotifyEndOfStream, IntPtr.Zero);
                    _transform.ProcessMessage(MftMessageNotifyEndStreaming, IntPtr.Zero);
                    _transform.ProcessMessage(MftMessageCommandFlush, IntPtr.Zero);
                }
                catch
                {
                    // Best-effort shutdown on the owning worker thread.
                }

                ReleaseComObject(_transform);
                _transform = null;
            }

            ClearManagedEncoderState();

            if (_mfStarted)
            {
                NativeMethods.MFShutdown();
                _mfStarted = false;
            }

            if (_comInitialized)
            {
                NativeMethods.CoUninitialize();
                _comInitialized = false;
            }
        }

        private static void ReturnInputFrameBuffer(QueuedInputFrame frame)
        {
            if (frame.Data != null)
                ArrayPool<byte>.Shared.Return(frame.Data);
        }

        private void DrainInputQueue()
        {
            lock (_inputLock)
            {
                while (_inputFrames.TryDequeue(out var frame))
                {
                    ReturnInputFrameBuffer(frame);
                    if (_inputCount > 0)
                        _inputCount--;
                }

                _inputCount = 0;
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
    }
}
