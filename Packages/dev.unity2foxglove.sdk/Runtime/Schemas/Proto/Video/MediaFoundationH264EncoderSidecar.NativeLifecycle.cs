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

        private void InitializeMediaFoundation()
        {
            var hr = NativeMethods.CoInitializeEx(IntPtr.Zero, CoinitMultithreaded);
            if (hr == SOk || hr == SFalse)
                _comInitialized = true;
            else if (hr != RpcEChangedMode)
                ThrowForHr(hr, "CoInitializeEx failed.");

            hr = NativeMethods.MFStartup(MfVersion, 0);
            ThrowForHr(hr, "MFStartup failed.");
            _mfStarted = true;
        }

        private void ConfigureEncoder(MediaFoundationH264EncoderOptions options)
        {
            var transformId = MfGuids.CLSID_CMSH264EncoderMFT;
            var transformInterface = MfGuids.IID_IMFTransform;
            var hr = NativeMethods.CoCreateInstance(
                ref transformId,
                IntPtr.Zero,
                ClsctxInprocServer,
                ref transformInterface,
                out _transform);
            ThrowForHr(hr, "Could not create the Windows Media Foundation H.264 encoder MFT.");

            var outputType = CreateH264OutputType(options);
            var inputType = CreateNv12InputType(options);
            try
            {
                ConfigureLowLatencyEncoderOptions(options);

                // Media Foundation H.264 requires output type before input type.
                hr = _transform.SetOutputType(0, outputType, 0);
                ThrowForHr(hr, "Media Foundation H.264 SetOutputType failed.");
                CacheOutputSequenceHeader();

                hr = _transform.SetInputType(0, inputType, 0);
                ThrowForHr(hr, "Media Foundation H.264 SetInputType failed.");
            }
            finally
            {
                ReleaseComObject(outputType);
                ReleaseComObject(inputType);
            }

            _sampleDuration = 10_000_000L / Math.Max(1, options.FrameRate);
            _nextSampleTime = 0;
            RefreshOutputStreamInfo();
            _transform.ProcessMessage(MftMessageNotifyBeginStreaming, IntPtr.Zero);
            _transform.ProcessMessage(MftMessageNotifyStartOfStream, IntPtr.Zero);
        }

        private void ConfigureLowLatencyEncoderOptions(MediaFoundationH264EncoderOptions options)
        {
            ApplyMftLowLatencyAttribute();

            var codecApi = _transform as ICodecAPI;
            if (codecApi == null)
            {
                LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, "ICodecAPI unavailable.");
                return;
            }

            var bitrate = (uint)options.BitrateBitsPerSecond;
            SetCodecBool(codecApi, MfGuids.CODECAPI_AVLowLatencyMode, true, "AVLowLatencyMode");
            SetCodecBool(codecApi, MfGuids.CODECAPI_AVEncCommonLowLatency, true, "AVEncCommonLowLatency");
            SetCodecBool(codecApi, MfGuids.CODECAPI_AVEncCommonRealTime, true, "AVEncCommonRealTime");
            SetCodecUInt32(codecApi, MfGuids.CODECAPI_AVEncCommonRateControlMode, RateControlModeCbr, "AVEncCommonRateControlMode");
            SetCodecUInt32(codecApi, MfGuids.CODECAPI_AVEncCommonMeanBitRate, bitrate, "AVEncCommonMeanBitRate");
            SetCodecUInt32(codecApi, MfGuids.CODECAPI_AVEncMPVGOPSize, (uint)Math.Max(1, options.KeyframeInterval), "AVEncMPVGOPSize");
            SetCodecUInt32(codecApi, MfGuids.CODECAPI_AVEncMPVDefaultBPictureCount, 0, "AVEncMPVDefaultBPictureCount");
        }

        private void ApplyMftLowLatencyAttribute()
        {
            IMFAttributes attributes = null;
            try
            {
                var hr = _transform.GetAttributes(out attributes);
                if (hr < 0 || attributes == null)
                {
                    LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, "MF_LOW_LATENCY attributes unavailable.");
                    return;
                }

                var key = MfGuids.CODECAPI_AVLowLatencyMode;
                hr = attributes.SetUINT32(ref key, 1);
                if (hr < 0)
                    LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, "MF_LOW_LATENCY rejected HRESULT=0x" + hr.ToString("X8"));
            }
            finally
            {
                ReleaseComObject(attributes);
            }
        }

        private void SetCodecBool(ICodecAPI codecApi, Guid key, bool value, string name)
        {
            var v = Variant.FromBool(value);
            SetCodecValue(codecApi, key, ref v, name);
        }

        private void SetCodecUInt32(ICodecAPI codecApi, Guid key, uint value, string name)
        {
            var v = Variant.FromUInt32(value);
            SetCodecValue(codecApi, key, ref v, name);
        }

        private void SetCodecValue(ICodecAPI codecApi, Guid key, ref Variant value, string name)
        {
            var k = key;
            var hr = codecApi.IsSupported(ref k);
            if (hr != SOk)
            {
                LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, name + " unsupported HRESULT=0x" + hr.ToString("X8"));
                return;
            }

            hr = codecApi.IsModifiable(ref k);
            if (hr != SOk)
                LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, name + " modifiability unknown HRESULT=0x" + hr.ToString("X8"));

            hr = codecApi.SetValue(ref k, ref value);
            if (hr < 0)
                LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, name + " rejected HRESULT=0x" + hr.ToString("X8"));
        }

        private static IMFMediaType CreateH264OutputType(MediaFoundationH264EncoderOptions options)
        {
            var hr = NativeMethods.MFCreateMediaType(out var mediaType);
            ThrowForHr(hr, "MFCreateMediaType output failed.");

            SetGuid(mediaType, MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
            SetGuid(mediaType, MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_H264);
            SetUInt32(mediaType, MfGuids.MF_MT_AVG_BITRATE, options.BitrateBitsPerSecond);
            SetUInt32(mediaType, MfGuids.MF_MT_INTERLACE_MODE, MfVideoInterlaceProgressive);
            SetUInt32(mediaType, MfGuids.MF_MT_MPEG2_PROFILE, H264BaselineProfile);
            SetFrameSize(mediaType, options.Width, options.Height);
            SetFrameRate(mediaType, options.FrameRate, 1);
            SetPixelAspectRatio(mediaType, 1, 1);
            return mediaType;
        }

        private static IMFMediaType CreateNv12InputType(MediaFoundationH264EncoderOptions options)
        {
            var hr = NativeMethods.MFCreateMediaType(out var mediaType);
            ThrowForHr(hr, "MFCreateMediaType input failed.");

            SetGuid(mediaType, MfGuids.MF_MT_MAJOR_TYPE, MfGuids.MFMediaType_Video);
            SetGuid(mediaType, MfGuids.MF_MT_SUBTYPE, MfGuids.MFVideoFormat_NV12);
            SetUInt32(mediaType, MfGuids.MF_MT_INTERLACE_MODE, MfVideoInterlaceProgressive);
            SetFrameSize(mediaType, options.Width, options.Height);
            SetFrameRate(mediaType, options.FrameRate, 1);
            SetPixelAspectRatio(mediaType, 1, 1);
            return mediaType;
        }

        private void ProcessInputFrame(byte[] nv12Frame, ulong timestampNs)
        {
            var sampleTime = _nextSampleTime;
            var sample = CreateSample(nv12Frame, sampleTime, _sampleDuration);
            try
            {
                var hr = _transform.ProcessInput(0, sample, 0);
                if (hr == MfENotAccepting)
                {
                    DrainEncoderOutput();
                    hr = _transform.ProcessInput(0, sample, 0);
                }

                ThrowForHr(hr, "Media Foundation H.264 ProcessInput failed.");
                RegisterSampleTimestamp(sampleTime, timestampNs);
                _nextSampleTime += _sampleDuration;
            }
            finally
            {
                ReleaseComObject(sample);
            }
        }

        private IMFSample CreateSample(byte[] data, long sampleTime, long duration)
        {
            IMFMediaBuffer buffer = null;
            IMFSample sample = null;
            var sampleReturned = false;
            try
            {
                var hr = NativeMethods.MFCreateMemoryBuffer(data.Length, out buffer);
                ThrowForHr(hr, "MFCreateMemoryBuffer failed.");
                WriteBuffer(buffer, data);

                hr = NativeMethods.MFCreateSample(out sample);
                ThrowForHr(hr, "MFCreateSample failed.");
                hr = sample.AddBuffer(buffer);
                ThrowForHr(hr, "IMFSample.AddBuffer failed.");
                hr = sample.SetSampleTime(sampleTime);
                ThrowForHr(hr, "IMFSample.SetSampleTime failed.");
                hr = sample.SetSampleDuration(duration);
                ThrowForHr(hr, "IMFSample.SetSampleDuration failed.");
                sampleReturned = true;
                return sample;
            }
            finally
            {
                ReleaseComObject(buffer);
                if (!sampleReturned)
                    ReleaseComObject(sample);
            }
        }

    }
}
