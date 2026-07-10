// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Experimental Windows Media Foundation H.264 encoder sidecar.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Experimental Media Foundation H.264 encoder sidecar. Phase 78 keeps the
    /// boundary explicit so unsupported Windows encoder states fail clearly. The
    /// path still converts and submits samples synchronously and should remain
    /// marked experimental until that work is moved off the caller thread.
    /// </summary>
    public sealed partial class MediaFoundationH264EncoderSidecar : ICameraVideoEncoderSidecar, ITimestampedCameraVideoEncoderSidecar
    {
        private const int SOk = 0;
        private const int SFalse = 1;
        private const int MfVersion = 0x00020070;
        private const int ClsctxInprocServer = 0x1;
        private const int CoinitMultithreaded = 0x0;
        private const int RpcEChangedMode = unchecked((int)0x80010106);
        private const int MfENotAccepting = unchecked((int)0xC00D36B5);
        private const int MfETransformNeedMoreInput = unchecked((int)0xC00D6D72);
        private const int MfETransformStreamChange = unchecked((int)0xC00D6D61);
        private const int MftOutputStreamProvidesSamples = 0x00000100;
        private const int VtBool = 11;
        private const int VtUI4 = 19;
        private const int VariantTrue = -1;
        private const int RateControlModeCbr = 0;
        private const int MftMessageCommandFlush = 0x00000000;
        private const int MftMessageNotifyBeginStreaming = 0x10000000;
        private const int MftMessageNotifyEndStreaming = 0x10000001;
        private const int MftMessageNotifyStartOfStream = 0x10000002;
        private const int MftMessageNotifyEndOfStream = 0x10000003;
        private const int MfVideoInterlaceProgressive = 2;
        private const int H264BaselineProfile = 66;
        private const int MaxTrackedSampleTimestamps = 256;
        private const int MaxConsecutiveOutputStreamChanges = 3;
        private static readonly int s_mftOutputDataBufferSize = Marshal.SizeOf(typeof(MftOutputDataBuffer));

        private readonly ConcurrentQueue<EncodedVideoAccessUnit> _outputAccessUnits = new ConcurrentQueue<EncodedVideoAccessUnit>();
        private readonly Dictionary<long, ulong> _sampleTimestampNsByTime = new Dictionary<long, ulong>();
        private readonly Dictionary<long, LinkedListNode<long>> _sampleTimestampNodesByTime = new Dictionary<long, LinkedListNode<long>>();
        private readonly LinkedList<long> _sampleTimestampOrder = new LinkedList<long>();
        private readonly object _outputLock = new object();
        private readonly H264AccessUnitNormalizer _normalizer = new H264AccessUnitNormalizer();
        private MediaFoundationH264EncoderOptions _options;
        private IMFTransform _transform;
        private byte[] _nv12Scratch;
        private MftOutputStreamInfo _outputStreamInfo;
        private long _nextSampleTime;
        private long _sampleDuration;
        private long _evictedTimestampCount;
        private int _outputCount;
        private int _maxOutputQueue = 4;
        private bool _mfStarted;
        private bool _comInitialized;
        private bool _hasOutputStreamInfo;
        private bool _isRunning;
        private string _lastDiagnosticLine;
        private string _lastError;

        public bool IsRunning
        {
            get => Volatile.Read(ref _isRunning);
            private set => Volatile.Write(ref _isRunning, value);
        }
        public int OutputQueueDepth => Volatile.Read(ref _outputCount);
        public int MaxOutputQueue => Volatile.Read(ref _maxOutputQueue);
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
        public long EvictedTimestampCount => Interlocked.Read(ref _evictedTimestampCount);

        internal static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Starts the Windows native H.264 encoder if available.</summary>
        public bool Start(MediaFoundationH264EncoderOptions options)
        {
            Stop(clearOutputQueue: true);
            _options = options ?? new MediaFoundationH264EncoderOptions();
            _maxOutputQueue = Math.Max(1, _options.MaxOutputQueue);
            Interlocked.Exchange(ref _evictedTimestampCount, 0);
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

            try
            {
                InitializeMediaFoundation();
                ConfigureEncoder(_options);
                IsRunning = true;
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

            if (rgb24Frame == null || rgb24Frame.Length != expectedBytes)
            {
                LastError = "RGB24 frame byte count does not match Media Foundation encoder dimensions.";
                return false;
            }

            try
            {
                var nv12Frame = EnsureNv12Scratch();
                if (!Rgb24ToNv12Converter.TryConvertRgb24ToNv12(
                    rgb24Frame,
                    _options.Width,
                    _options.Height,
                    nv12Frame,
                    flipVertical: true,
                    out var conversionError))
                    throw new InvalidOperationException(conversionError);

                ProcessInputFrame(nv12Frame, timestampNs);
                DrainEncoderOutput();
                return true;
            }
            catch (Exception ex)
            {
                LastError = DescribeException(ex);
                LastDiagnosticLine = LastError;
                return false;
            }
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

        public void Dispose()
        {
            Stop(clearOutputQueue: false);
        }

        private void Stop(bool clearOutputQueue)
        {
            IsRunning = false;
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
                    // Best-effort shutdown.
                }

                ReleaseComObject(_transform);
                _transform = null;
            }

            _options = null;
            _nv12Scratch = null;
            _hasOutputStreamInfo = false;
            _nextSampleTime = 0;
            _sampleDuration = 0;
            ClearSampleTimestampMap();

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

            if (clearOutputQueue)
            {
                _maxOutputQueue = 4;
                DrainOutputQueue();
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

        private void DrainEncoderOutput()
        {
            var consecutiveStreamChanges = 0;
            while (true)
            {
                var info = GetCachedOutputStreamInfo();

                IMFSample sample = null;
                IMFMediaBuffer buffer = null;
                var output = new MftOutputDataBuffer();
                var outputPtr = IntPtr.Zero;
                var samplePtr = IntPtr.Zero;
                IMFSample outputSample = null;
                int hr;
                try
                {
                    if ((info.dwFlags & MftOutputStreamProvidesSamples) == 0)
                    {
                        var size = Math.Max(info.cbSize, Math.Max(1, _options.Nv12FrameByteCount));
                        hr = NativeMethods.MFCreateMemoryBuffer(size, out buffer);
                        ThrowForHr(hr, "MFCreateMemoryBuffer output failed.");
                        hr = NativeMethods.MFCreateSample(out sample);
                        ThrowForHr(hr, "MFCreateSample output failed.");
                        hr = sample.AddBuffer(buffer);
                        ThrowForHr(hr, "Output IMFSample.AddBuffer failed.");
#pragma warning disable CA1416 // Guarded by Start()'s Windows-only path.
                        samplePtr = Marshal.GetIUnknownForObject(sample);
#pragma warning restore CA1416
                        output.pSample = samplePtr;
                    }

                    outputPtr = Marshal.AllocHGlobal(s_mftOutputDataBufferSize);
                    Marshal.StructureToPtr(output, outputPtr, false);
                    hr = _transform.ProcessOutput(0, 1, outputPtr, out _);
                    output = Marshal.PtrToStructure<MftOutputDataBuffer>(outputPtr);
                    if (hr == MfETransformNeedMoreInput)
                        return;

                    if (hr == MfETransformStreamChange)
                    {
                        consecutiveStreamChanges++;
                        HandleOutputStreamChange(consecutiveStreamChanges);
                        continue;
                    }

                    consecutiveStreamChanges = 0;
                    ThrowForHr(hr, "Media Foundation H.264 ProcessOutput failed.");
                    if (output.pSample != IntPtr.Zero)
                    {
                        if (output.pSample == samplePtr)
                        {
                            outputSample = sample;
                        }
                        else
                        {
#pragma warning disable CA1416 // Guarded by Start()'s Windows-only path.
                            outputSample = (IMFSample)Marshal.GetObjectForIUnknown(output.pSample);
#pragma warning restore CA1416
                        }
                    }

                    ExtractOutputSample(outputSample ?? sample);
                }
                finally
                {
                    if (outputPtr != IntPtr.Zero)
                        Marshal.FreeHGlobal(outputPtr);
                    if (output.pEvents != IntPtr.Zero)
                        Marshal.Release(output.pEvents);
                    if (output.pSample != IntPtr.Zero && output.pSample != samplePtr)
                        Marshal.Release(output.pSample);
                    if (samplePtr != IntPtr.Zero)
                        Marshal.Release(samplePtr);
                    if (outputSample != null && !ReferenceEquals(outputSample, sample))
                        ReleaseComObject(outputSample);
                    ReleaseComObject(sample);
                    ReleaseComObject(buffer);
                }
            }
        }

        private void HandleOutputStreamChange(int consecutiveStreamChanges)
        {
            if (consecutiveStreamChanges > MaxConsecutiveOutputStreamChanges)
                throw new InvalidOperationException("Media Foundation H.264 output stream change did not settle.");

            IMFMediaType outputType = null;
            try
            {
                var hr = _transform.GetOutputAvailableType(0, 0, out outputType);
                if (hr < 0 || outputType == null)
                {
                    ReleaseComObject(outputType);
                    outputType = CreateH264OutputType(_options);
                }

                hr = _transform.SetOutputType(0, outputType, 0);
                ThrowForHr(hr, "Media Foundation H.264 SetOutputType after stream change failed.");
                CacheOutputSequenceHeader();
                RefreshOutputStreamInfo();
            }
            finally
            {
                ReleaseComObject(outputType);
            }
        }

        private void ExtractOutputSample(IMFSample sample)
        {
            if (sample == null)
                return;

            IMFMediaBuffer buffer = null;
            try
            {
                var hr = sample.ConvertToContiguousBuffer(out buffer);
                ThrowForHr(hr, "ConvertToContiguousBuffer failed.");
                var bytes = ReadBuffer(buffer);
                var timestampNs = ResolveOutputTimestamp(sample);
                if (_normalizer.TryNormalizeSample(bytes, out var accessUnit))
                    EnqueueAccessUnit(accessUnit, timestampNs);
            }
            finally
            {
                ReleaseComObject(buffer);
            }
        }

        private void CacheOutputSequenceHeader()
        {
            IMFMediaType currentType = null;
            try
            {
                var hr = _transform.GetOutputCurrentType(0, out currentType);
                if (hr < 0 || currentType == null)
                    return;

                var key = MfGuids.MF_MT_MPEG_SEQUENCE_HEADER;
                hr = currentType.GetBlobSize(ref key, out var size);
                if (hr < 0 || size <= 0)
                    return;

                var blob = new byte[size];
                hr = currentType.GetBlob(ref key, blob, blob.Length, out _);
                if (hr >= 0)
                    _normalizer.CacheParameterSets(blob);
            }
            finally
            {
                ReleaseComObject(currentType);
            }
        }

        private void EnqueueAccessUnit(byte[] accessUnit, ulong timestampNs)
        {
            if (accessUnit == null || accessUnit.Length == 0)
                return;

            lock (_outputLock)
            {
                if (_outputCount >= _maxOutputQueue)
                {
                    LastDiagnosticLine = "Media Foundation H.264 output queue full; capture admission is holding new frames.";
                    return;
                }

                _outputAccessUnits.Enqueue(new EncodedVideoAccessUnit(accessUnit, timestampNs));
                _outputCount++;
            }
        }

        private byte[] EnsureNv12Scratch()
        {
            var expectedBytes = _options != null ? _options.Nv12FrameByteCount : 0;
            if (expectedBytes <= 0)
                throw new InvalidOperationException("Media Foundation H.264 NV12 frame byte count is invalid.");

            if (_nv12Scratch == null || _nv12Scratch.Length != expectedBytes)
                _nv12Scratch = new byte[expectedBytes];

            return _nv12Scratch;
        }

        private void RegisterSampleTimestamp(long sampleTime, ulong timestampNs)
        {
            lock (_outputLock)
            {
                if (_sampleTimestampNsByTime.Count >= MaxTrackedSampleTimestamps)
                    EvictOldestSampleTimestamp();

                if (_sampleTimestampNodesByTime.TryGetValue(sampleTime, out var existingNode))
                    _sampleTimestampOrder.Remove(existingNode);

                _sampleTimestampNsByTime[sampleTime] = timestampNs;
                _sampleTimestampNodesByTime[sampleTime] = _sampleTimestampOrder.AddLast(sampleTime);
            }
        }

        private ulong ResolveOutputTimestamp(IMFSample sample)
        {
            var hr = sample.GetSampleTime(out var sampleTime);
            if (hr < 0)
                return 0UL;

            lock (_outputLock)
            {
                if (!_sampleTimestampNsByTime.TryGetValue(sampleTime, out var timestampNs))
                    return 0UL;

                _sampleTimestampNsByTime.Remove(sampleTime);
                if (_sampleTimestampNodesByTime.TryGetValue(sampleTime, out var node))
                {
                    _sampleTimestampOrder.Remove(node);
                    _sampleTimestampNodesByTime.Remove(sampleTime);
                }

                return timestampNs;
            }
        }

        private void EvictOldestSampleTimestamp()
        {
            var node = _sampleTimestampOrder.First;
            if (node == null)
                return;

            var oldestSampleTime = node.Value;
            _sampleTimestampOrder.RemoveFirst();
            _sampleTimestampNodesByTime.Remove(oldestSampleTime);
            _sampleTimestampNsByTime.Remove(oldestSampleTime);
            if (Interlocked.Increment(ref _evictedTimestampCount) == 1)
                LastDiagnosticLine = AppendDiagnostic(LastDiagnosticLine, "Media Foundation H.264 evicted old sample timestamps; output timestamps may fall back to zero under sustained backlog.");
        }

        private MftOutputStreamInfo GetCachedOutputStreamInfo()
        {
            if (!_hasOutputStreamInfo)
                RefreshOutputStreamInfo();
            return _outputStreamInfo;
        }

        private void RefreshOutputStreamInfo()
        {
            var hr = _transform.GetOutputStreamInfo(0, out _outputStreamInfo);
            ThrowForHr(hr, "Media Foundation H.264 GetOutputStreamInfo failed.");
            _hasOutputStreamInfo = true;
        }

        private void ClearSampleTimestampMap()
        {
            lock (_outputLock)
            {
                _sampleTimestampNsByTime.Clear();
                _sampleTimestampNodesByTime.Clear();
                _sampleTimestampOrder.Clear();
            }
        }

        private static void WriteBuffer(IMFMediaBuffer buffer, byte[] data)
        {
            IntPtr ptr = IntPtr.Zero;
            var locked = false;
            try
            {
                var hr = buffer.Lock(out ptr, out _, out _);
                ThrowForHr(hr, "IMFMediaBuffer.Lock failed.");
                locked = true;
                Marshal.Copy(data, 0, ptr, data.Length);
                hr = buffer.SetCurrentLength(data.Length);
                ThrowForHr(hr, "IMFMediaBuffer.SetCurrentLength failed.");
            }
            finally
            {
                if (locked)
                    buffer.Unlock();
            }
        }

        private static byte[] ReadBuffer(IMFMediaBuffer buffer)
        {
            IntPtr ptr = IntPtr.Zero;
            var locked = false;
            try
            {
                var hr = buffer.Lock(out ptr, out _, out var currentLength);
                ThrowForHr(hr, "IMFMediaBuffer.Lock output failed.");
                locked = true;
                var bytes = new byte[Math.Max(0, currentLength)];
                if (bytes.Length > 0)
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                if (locked)
                    buffer.Unlock();
            }
        }

        private static void SetGuid(IMFAttributes attributes, Guid key, Guid value)
        {
            var k = key;
            var v = value;
            ThrowForHr(attributes.SetGUID(ref k, ref v), "IMFAttributes.SetGUID failed.");
        }

        private static void SetUInt32(IMFAttributes attributes, Guid key, int value)
        {
            var k = key;
            ThrowForHr(attributes.SetUINT32(ref k, value), "IMFAttributes.SetUINT32 failed.");
        }

        private static void SetFrameSize(IMFAttributes attributes, int width, int height)
        {
            var k = MfGuids.MF_MT_FRAME_SIZE;
            ThrowForHr(attributes.SetUINT64(ref k, PackUInt32PairAsUInt64(width, height)), "IMFAttributes.SetUINT64 frame size failed.");
        }

        private static void SetFrameRate(IMFAttributes attributes, int numerator, int denominator)
        {
            var k = MfGuids.MF_MT_FRAME_RATE;
            ThrowForHr(attributes.SetUINT64(ref k, PackUInt32PairAsUInt64(numerator, denominator)), "IMFAttributes.SetUINT64 frame rate failed.");
        }

        private static void SetPixelAspectRatio(IMFAttributes attributes, int numerator, int denominator)
        {
            var k = MfGuids.MF_MT_PIXEL_ASPECT_RATIO;
            ThrowForHr(attributes.SetUINT64(ref k, PackUInt32PairAsUInt64(numerator, denominator)), "IMFAttributes.SetUINT64 pixel aspect failed.");
        }

        private static long PackUInt32PairAsUInt64(int high, int low)
        {
            return ((long)(uint)high << 32) | (uint)low;
        }

        private static void ThrowForHr(int hr, string message)
        {
            if (hr >= 0)
                return;

            throw new InvalidOperationException(message + " HRESULT=0x" + hr.ToString("X8"));
        }

        private static string DescribeException(Exception ex)
        {
            if (ex == null)
                return "Unknown Media Foundation H.264 encoder failure.";

            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().FullName
                : ex.GetType().FullName + ": " + ex.Message;
            if (ex.HResult != 0)
                message += " HResult=0x" + ex.HResult.ToString("X8");
            if (ex.TargetSite != null)
                message += " Target=" + ex.TargetSite.Name;
            if (ex.InnerException != null)
                message += " Inner=" + DescribeException(ex.InnerException);
            return message;
        }

        private static string AppendDiagnostic(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(next))
                return current;
            return string.IsNullOrWhiteSpace(current) ? next : current + " " + next;
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject))
                return;

            try
            {
#pragma warning disable CA1416 // Guarded by Start()'s Windows-only path; release is best-effort cleanup.
                Marshal.ReleaseComObject(comObject);
#pragma warning restore CA1416
            }
            catch
            {
                // Ignore release failures during cleanup.
            }
        }
    }
}
