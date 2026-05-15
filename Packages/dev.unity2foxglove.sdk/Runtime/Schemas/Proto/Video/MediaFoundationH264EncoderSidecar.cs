// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Experimental Windows Media Foundation H.264 encoder sidecar.

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Experimental Media Foundation H.264 encoder sidecar. Phase 78 keeps the
    /// boundary explicit so unsupported Windows encoder states fail clearly.
    /// </summary>
    public sealed class MediaFoundationH264EncoderSidecar : ICameraVideoEncoderSidecar
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

        private readonly ConcurrentQueue<byte[]> _outputAccessUnits = new ConcurrentQueue<byte[]>();
        private readonly H264AccessUnitNormalizer _normalizer = new H264AccessUnitNormalizer();
        private MediaFoundationH264EncoderOptions _options;
        private IMFTransform _transform;
        private long _nextSampleTime;
        private long _sampleDuration;
        private int _outputCount;
        private bool _mfStarted;
        private bool _comInitialized;

        public bool IsRunning { get; private set; }
        public string LastDiagnosticLine { get; private set; }
        public string LastError { get; private set; }

        internal static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Starts the Windows native H.264 encoder if available.</summary>
        public bool Start(MediaFoundationH264EncoderOptions options)
        {
            Stop();
            _options = options ?? new MediaFoundationH264EncoderOptions();
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
                Stop();
                return false;
            }
        }

        /// <summary>Submits an RGB24 frame without blocking the caller.</summary>
        public bool TrySubmitFrame(byte[] rgb24Frame)
        {
            if (!IsRunning)
            {
                LastError = "Media Foundation H.264 encoder is not running.";
                return false;
            }

            if (rgb24Frame == null || rgb24Frame.Length != _options.Rgb24FrameByteCount)
            {
                LastError = "RGB24 frame byte count does not match Media Foundation encoder dimensions.";
                return false;
            }

            try
            {
                var nv12Frame = ConvertRgb24ToNv12(rgb24Frame, _options.Width, _options.Height);
                ProcessInputFrame(nv12Frame);
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
            if (!_outputAccessUnits.TryDequeue(out accessUnit))
                return false;

            Interlocked.Decrement(ref _outputCount);
            return true;
        }

        public void Dispose()
        {
            Stop();
        }

        private void Stop()
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
            while (_outputAccessUnits.TryDequeue(out _))
            {
            }

            Volatile.Write(ref _outputCount, 0);
            _nextSampleTime = 0;
            _sampleDuration = 0;

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

            var bitrate = checked((uint)Math.Max(1, options.BitrateKbps) * 1000u);
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
            SetUInt32(mediaType, MfGuids.MF_MT_AVG_BITRATE, checked((int)Math.Max(1, options.BitrateKbps) * 1000));
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

        private void ProcessInputFrame(byte[] nv12Frame)
        {
            var sample = CreateSample(nv12Frame, _nextSampleTime, _sampleDuration);
            try
            {
                var hr = _transform.ProcessInput(0, sample, 0);
                if (hr == MfENotAccepting)
                {
                    DrainEncoderOutput();
                    hr = _transform.ProcessInput(0, sample, 0);
                }

                ThrowForHr(hr, "Media Foundation H.264 ProcessInput failed.");
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
                return sample;
            }
            finally
            {
                ReleaseComObject(buffer);
            }
        }

        private void DrainEncoderOutput()
        {
            while (true)
            {
                var hr = _transform.GetOutputStreamInfo(0, out var info);
                ThrowForHr(hr, "Media Foundation H.264 GetOutputStreamInfo failed.");

                IMFSample sample = null;
                IMFMediaBuffer buffer = null;
                var output = new MftOutputDataBuffer();
                var outputPtr = IntPtr.Zero;
                var samplePtr = IntPtr.Zero;
                IMFSample outputSample = null;
                try
                {
                    if ((info.dwFlags & MftOutputStreamProvidesSamples) == 0)
                    {
                        var size = Math.Max(info.cbSize, Math.Max(1, _options.Width * _options.Height));
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

                    outputPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MftOutputDataBuffer)));
                    Marshal.StructureToPtr(output, outputPtr, false);
                    hr = _transform.ProcessOutput(0, 1, outputPtr, out _);
                    output = Marshal.PtrToStructure<MftOutputDataBuffer>(outputPtr);
                    if (hr == MfETransformNeedMoreInput)
                        return;

                    if (hr == MfETransformStreamChange)
                    {
                        CacheOutputSequenceHeader();
                        continue;
                    }

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
                if (_normalizer.TryNormalizeSample(bytes, out var accessUnit))
                    EnqueueAccessUnit(accessUnit);
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

        private void EnqueueAccessUnit(byte[] accessUnit)
        {
            if (accessUnit == null || accessUnit.Length == 0)
                return;

            var capacity = Math.Max(1, _options?.MaxOutputQueue ?? 4);
            while (Volatile.Read(ref _outputCount) >= capacity && _outputAccessUnits.TryDequeue(out _))
                Interlocked.Decrement(ref _outputCount);

            _outputAccessUnits.Enqueue(accessUnit);
            Interlocked.Increment(ref _outputCount);
        }

        private static byte[] ConvertRgb24ToNv12(byte[] rgb24Frame, int width, int height)
        {
            var yPlaneLength = width * height;
            var nv12 = new byte[yPlaneLength + yPlaneLength / 2];

            for (var y = 0; y < height; y++)
            {
                var sourceY = height - 1 - y;
                for (var x = 0; x < width; x++)
                {
                    var rgb = (sourceY * width + x) * 3;
                    var r = rgb24Frame[rgb];
                    var g = rgb24Frame[rgb + 1];
                    var b = rgb24Frame[rgb + 2];
                    nv12[y * width + x] = ToLuma(r, g, b);
                }
            }

            var uvOffset = yPlaneLength;
            for (var y = 0; y < height; y += 2)
            {
                var sourceY0 = height - 1 - y;
                var sourceY1 = Math.Max(0, sourceY0 - 1);
                for (var x = 0; x < width; x += 2)
                {
                    var u = 0;
                    var v = 0;
                    AccumulateChroma(rgb24Frame, width, sourceY0, x, ref u, ref v);
                    AccumulateChroma(rgb24Frame, width, sourceY0, Math.Min(x + 1, width - 1), ref u, ref v);
                    AccumulateChroma(rgb24Frame, width, sourceY1, x, ref u, ref v);
                    AccumulateChroma(rgb24Frame, width, sourceY1, Math.Min(x + 1, width - 1), ref u, ref v);
                    var uv = uvOffset + (y / 2) * width + x;
                    nv12[uv] = ClampByte(u / 4);
                    nv12[uv + 1] = ClampByte(v / 4);
                }
            }

            return nv12;
        }

        private static void AccumulateChroma(byte[] rgb24Frame, int width, int y, int x, ref int u, ref int v)
        {
            var rgb = (y * width + x) * 3;
            var r = rgb24Frame[rgb];
            var g = rgb24Frame[rgb + 1];
            var b = rgb24Frame[rgb + 2];
            u += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
            v += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
        }

        private static byte ToLuma(byte r, byte g, byte b)
            => ClampByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

        private static byte ClampByte(int value)
            => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
        
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

        private static class MfGuids
        {
            public static readonly Guid CLSID_CMSH264EncoderMFT = new Guid("6CA50344-051A-4DED-9779-A43305165E35");
            public static readonly Guid IID_IMFTransform = new Guid("BF94C121-5B05-4E6F-8000-BA598961414D");
            public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
            public static readonly Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
            public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
            public static readonly Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
            public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
            public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
            public static readonly Guid MF_MT_MPEG2_PROFILE = new Guid("AD76A80B-2D5C-4E0B-B375-64E520137036");
            public static readonly Guid MF_MT_MPEG_SEQUENCE_HEADER = new Guid("3C036DE7-3AD0-4C9E-9216-EE6D6AC21CB3");
            public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
            public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
            public static readonly Guid CODECAPI_AVLowLatencyMode = new Guid("9C27891A-ED7A-40E1-88E8-B22727A024EE");
            public static readonly Guid CODECAPI_AVEncCommonLowLatency = new Guid("9D3ECD55-89E8-490A-970A-0C9548D5A56E");
            public static readonly Guid CODECAPI_AVEncCommonRealTime = new Guid("143A0FF6-A131-43DA-B81E-98FBB8EC378E");
            public static readonly Guid CODECAPI_AVEncCommonRateControlMode = new Guid("1C0608E9-370C-4710-8A58-CB6181C42423");
            public static readonly Guid CODECAPI_AVEncCommonMeanBitRate = new Guid("F7222374-2144-4815-B550-A37F8E12EE52");
            public static readonly Guid CODECAPI_AVEncMPVGOPSize = new Guid("95F31B26-95A4-41AA-9303-246A7FC6EEF1");
            public static readonly Guid CODECAPI_AVEncMPVDefaultBPictureCount = new Guid("8D390AAC-DC5C-4200-B57F-814D04BABAB2");
        }

        private static class NativeMethods
        {
            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern void CoUninitialize();

            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern int CoCreateInstance(
                ref Guid rclsid,
                IntPtr pUnkOuter,
                int dwClsContext,
                ref Guid riid,
                [MarshalAs(UnmanagedType.Interface)] out IMFTransform ppv);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFStartup(int version, int dwFlags);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFShutdown();

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFCreateMediaType([MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppMFType);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFCreateMemoryBuffer(int cbMaxLength, [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);

            [DllImport("mfplat.dll", ExactSpelling = true)]
            public static extern int MFCreateSample([MarshalAs(UnmanagedType.Interface)] out IMFSample ppIMFSample);

        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            private readonly long _a;
            private readonly long _b;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Variant
        {
            private readonly ushort _variantType;
            private readonly ushort _reserved1;
            private readonly ushort _reserved2;
            private readonly ushort _reserved3;
            private readonly long _value;

            private Variant(ushort variantType, long value)
            {
                _variantType = variantType;
                _reserved1 = 0;
                _reserved2 = 0;
                _reserved3 = 0;
                _value = value;
            }

            public static Variant FromBool(bool value)
                => new Variant(VtBool, value ? VariantTrue : 0);

            public static Variant FromUInt32(uint value)
                => new Variant(VtUI4, value);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftInputStreamInfo
        {
            public long hnsMaxLatency;
            public int dwFlags;
            public int cbSize;
            public int cbMaxLookahead;
            public int cbAlignment;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftOutputStreamInfo
        {
            public int dwFlags;
            public int cbSize;
            public int cbAlignment;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MftOutputDataBuffer
        {
            public int dwStreamID;
            public IntPtr pSample;
            public int dwStatus;
            public IntPtr pEvents;
        }

        [ComImport]
        [Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, out bool pbResult);
            [PreserveSig] int Compare(IMFAttributes pTheirs, int matchType, out bool pbResult);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob(
                ref Guid guidKey,
                [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize,
                out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int SetItem(ref Guid guidKey, ref PropVariant value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);
            [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob(
                ref Guid guidKey,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int pcItems);
            [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
            [PreserveSig] int CopyAllItems(IMFAttributes pDest);
        }

        [ComImport]
        [Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType : IMFAttributes
        {
        }

        [ComImport]
        [Guid("045FA593-8799-42B8-BC8D-8968C6453507")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
            [PreserveSig] int SetCurrentLength(int cbCurrentLength);
            [PreserveSig] int GetMaxLength(out int pcbMaxLength);
        }

        [ComImport]
        [Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample
        {
            // IMFAttributes vtable prefix. IMFSample extends IMFAttributes in native code,
            // so the derived COM interface must be flattened for reliable C# interop.
            [PreserveSig] int GetItem(ref Guid guidKey, IntPtr pValue);
            [PreserveSig] int GetItemType(ref Guid guidKey, out int pType);
            [PreserveSig] int CompareItem(ref Guid guidKey, IntPtr value, out bool pbResult);
            [PreserveSig] int Compare(IMFAttributes pTheirs, int matchType, out bool pbResult);
            [PreserveSig] int GetUINT32(ref Guid guidKey, out int punValue);
            [PreserveSig] int GetUINT64(ref Guid guidKey, out long punValue);
            [PreserveSig] int GetDouble(ref Guid guidKey, out double pfValue);
            [PreserveSig] int GetGUID(ref Guid guidKey, out Guid pguidValue);
            [PreserveSig] int GetStringLength(ref Guid guidKey, out int pcchLength);
            [PreserveSig] int GetString(ref Guid guidKey, IntPtr pwszValue, int cchBufSize, out int pcchLength);
            [PreserveSig] int GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
            [PreserveSig] int GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
            [PreserveSig] int GetBlob(
                ref Guid guidKey,
                [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize,
                out int pcbBlobSize);
            [PreserveSig] int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
            [PreserveSig] int GetUnknown(ref Guid guidKey, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int SetItem(ref Guid guidKey, ref PropVariant value);
            [PreserveSig] int DeleteItem(ref Guid guidKey);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid guidKey, int unValue);
            [PreserveSig] int SetUINT64(ref Guid guidKey, long unValue);
            [PreserveSig] int SetDouble(ref Guid guidKey, double fValue);
            [PreserveSig] int SetGUID(ref Guid guidKey, ref Guid guidValue);
            [PreserveSig] int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
            [PreserveSig] int SetBlob(
                ref Guid guidKey,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] byte[] pBuf,
                int cbBufSize);
            [PreserveSig] int SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int pcItems);
            [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
            [PreserveSig] int CopyAllItems(IMFAttributes pDest);

            [PreserveSig] int GetSampleFlags(out int pdwSampleFlags);
            [PreserveSig] int SetSampleFlags(int dwSampleFlags);
            [PreserveSig] int GetSampleTime(out long phnsSampleTime);
            [PreserveSig] int SetSampleTime(long hnsSampleTime);
            [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
            [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
            [PreserveSig] int GetBufferCount(out int pdwBufferCount);
            [PreserveSig] int GetBufferByIndex(int dwIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);
            [PreserveSig] int ConvertToContiguousBuffer([MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);
            [PreserveSig] int AddBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
            [PreserveSig] int RemoveBufferByIndex(int dwIndex);
            [PreserveSig] int RemoveAllBuffers();
            [PreserveSig] int GetTotalLength(out int pcbTotalLength);
            [PreserveSig] int CopyToBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);
        }

        [ComImport]
        [Guid("BF94C121-5B05-4E6F-8000-BA598961414D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFTransform
        {
            [PreserveSig] int GetStreamLimits(out int pdwInputMinimum, out int pdwInputMaximum, out int pdwOutputMinimum, out int pdwOutputMaximum);
            [PreserveSig] int GetStreamCount(out int pcInputStreams, out int pcOutputStreams);
            [PreserveSig] int GetStreamIDs(int dwInputIDArraySize, [Out] int[] pdwInputIDs, int dwOutputIDArraySize, [Out] int[] pdwOutputIDs);
            [PreserveSig] int GetInputStreamInfo(int dwInputStreamID, out MftInputStreamInfo pStreamInfo);
            [PreserveSig] int GetOutputStreamInfo(int dwOutputStreamID, out MftOutputStreamInfo pStreamInfo);
            [PreserveSig] int GetAttributes([MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);
            [PreserveSig] int GetInputStreamAttributes(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);
            [PreserveSig] int GetOutputStreamAttributes(int dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFAttributes pAttributes);
            [PreserveSig] int DeleteInputStream(int dwStreamID);
            [PreserveSig] int AddInputStreams(int cStreams, [In] int[] adwStreamIDs);
            [PreserveSig] int GetInputAvailableType(int dwInputStreamID, int dwTypeIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int GetOutputAvailableType(int dwOutputStreamID, int dwTypeIndex, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int SetInputType(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pType, int dwFlags);
            [PreserveSig] int SetOutputType(int dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pType, int dwFlags);
            [PreserveSig] int GetInputCurrentType(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int GetOutputCurrentType(int dwOutputStreamID, [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppType);
            [PreserveSig] int GetInputStatus(int dwInputStreamID, out int pdwFlags);
            [PreserveSig] int GetOutputStatus(out int pdwFlags);
            [PreserveSig] int SetOutputBounds(long hnsLowerBound, long hnsUpperBound);
            [PreserveSig] int ProcessEvent(int dwInputStreamID, IntPtr pEvent);
            [PreserveSig] int ProcessMessage(int eMessage, IntPtr ulParam);
            [PreserveSig] int ProcessInput(int dwInputStreamID, [MarshalAs(UnmanagedType.Interface)] IMFSample pSample, int dwFlags);
            [PreserveSig] int ProcessOutput(int dwFlags, int cOutputBufferCount, IntPtr pOutputSamples, out int pdwStatus);
        }

        [ComImport]
        [Guid("901DB4C7-31CE-41A2-85DC-8FA0BF41B8DA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICodecAPI
        {
            [PreserveSig] int IsSupported(ref Guid api);
            [PreserveSig] int IsModifiable(ref Guid api);
            [PreserveSig] int GetParameterRange(ref Guid api, out Variant valueMin, out Variant valueMax, out Variant steppingDelta);
            [PreserveSig] int GetParameterValues(ref Guid api, out IntPtr values, out int valuesCount);
            [PreserveSig] int GetDefaultValue(ref Guid api, out Variant value);
            [PreserveSig] int GetValue(ref Guid api, out Variant value);
            [PreserveSig] int SetValue(ref Guid api, ref Variant value);
            [PreserveSig] int RegisterForEvent(ref Guid api, IntPtr userData);
            [PreserveSig] int UnregisterForEvent(ref Guid api);
            [PreserveSig] int SetAllDefaults();
            [PreserveSig] int SetValueWithNotify(ref Guid api, ref Variant value, out IntPtr changedParam, out int changedParamCount);
            [PreserveSig] int SetAllDefaultsWithNotify(out IntPtr changedParam, out int changedParamCount);
            [PreserveSig] int GetAllSettings(IntPtr stream);
            [PreserveSig] int SetAllSettings(IntPtr stream);
            [PreserveSig] int SetAllSettingsWithNotify(IntPtr stream, out IntPtr changedParam, out int changedParamCount);
        }
    }
}
