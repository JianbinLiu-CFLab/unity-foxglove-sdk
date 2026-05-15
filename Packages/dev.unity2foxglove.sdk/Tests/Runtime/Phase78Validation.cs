// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 78 validation for experimental native Windows H.264 camera encoding.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Phase 78 H.264 encoder backend spike surface without
    /// requiring Unity Editor or Media Foundation at test runtime.
    /// </summary>
    public static class Phase78Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 78 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 78: H.264 Encoder Backend Spike ===");
            _passed = 0;

            VerifyModeProfile();
            VerifyCodecNeutralSidecarBoundary();
            VerifyH264AccessUnitNormalizer();
            VerifyMediaFoundationOptionsAndSidecar();
            VerifyCameraIntegrationSource();

            Console.WriteLine($"Phase 78: {_passed} checks passed.");
        }

        private static void VerifyModeProfile()
        {
            var modeType = Type.GetType("Unity.FoxgloveSDK.Components.CameraOutputMode, FoxgloveSdk.Tests");
            Check(modeType != null && modeType.IsEnum, "78A-1: CameraOutputMode enum exists");
            Check(Enum.GetNames(modeType).Contains("H264MediaFoundationExperimental"),
                "78A-2: CameraOutputMode exposes H264MediaFoundationExperimental");
            Check(Convert.ToInt32(Enum.Parse(modeType, "H264MediaFoundationExperimental")) == 3,
                "78A-3: H264MediaFoundationExperimental enum value is stable at 3");

            var profileType = Type.GetType("Unity.FoxgloveSDK.Components.CameraVideoOutputProfile, FoxgloveSdk.Tests");
            Check(profileType != null, "78A-4: CameraVideoOutputProfile exists");
            var forMode = profileType.GetMethod("ForMode", BindingFlags.Public | BindingFlags.Static);
            Check(forMode != null, "78A-5: CameraVideoOutputProfile.ForMode exists");

            var profile = forMode.Invoke(null, new[] { Enum.Parse(modeType, "H264MediaFoundationExperimental") });
            Check(GetStringProperty(profileType, profile, "DefaultTopic") == "/unity/camera",
                "78A-6: native H.264 default topic is /unity/camera");
            Check(GetStringProperty(profileType, profile, "SchemaName") == "foxglove.CompressedVideo",
                "78A-7: native H.264 schema is foxglove.CompressedVideo");
            Check(GetStringProperty(profileType, profile, "VideoFormat") == "h264",
                "78A-8: native H.264 video format is h264");
            var displayName = GetStringProperty(profileType, profile, "DisplayName");
            Check(displayName.Contains("Windows Native") && displayName.Contains("Experimental"),
                "78A-9: native H.264 display name calls out Windows Native and Experimental");
            Check(!GetBoolProperty(profileType, profile, "SupportsJson")
                    && GetBoolProperty(profileType, profile, "SupportsProtobuf"),
                "78A-10: native H.264 supports protobuf only");
        }

        private static void VerifyCodecNeutralSidecarBoundary()
        {
            var interfaceType = Type.GetType("Foxglove.Schemas.Video.ICameraVideoEncoderSidecar, FoxgloveSdk.Tests");
            Check(interfaceType != null && interfaceType.IsInterface,
                "78B-1: ICameraVideoEncoderSidecar interface exists");
            Check(typeof(IDisposable).IsAssignableFrom(interfaceType),
                "78B-2: ICameraVideoEncoderSidecar is disposable");
            Check(interfaceType.GetProperty("IsRunning")?.PropertyType == typeof(bool),
                "78B-3: sidecar interface exposes IsRunning");
            Check(interfaceType.GetProperty("LastDiagnosticLine")?.PropertyType == typeof(string),
                "78B-4: sidecar interface exposes LastDiagnosticLine");
            Check(interfaceType.GetProperty("LastError")?.PropertyType == typeof(string),
                "78B-5: sidecar interface exposes LastError");
            Check(interfaceType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]) })?.ReturnType == typeof(bool),
                "78B-6: sidecar interface exposes TrySubmitFrame");
            Check(interfaceType.GetMethod("TryDequeueAccessUnit")?.ReturnType == typeof(bool),
                "78B-7: sidecar interface exposes TryDequeueAccessUnit");

            var ffmpegInterface = Type.GetType("Foxglove.Schemas.Video.IFfmpegVideoEncoderSidecar, FoxgloveSdk.Tests");
            Check(ffmpegInterface != null && interfaceType.IsAssignableFrom(ffmpegInterface),
                "78B-8: IFfmpegVideoEncoderSidecar extends codec-neutral interface");

            var h264Sidecar = Type.GetType("Foxglove.Schemas.Video.FfmpegH264EncoderSidecar, FoxgloveSdk.Tests");
            var h265Sidecar = Type.GetType("Foxglove.Schemas.Video.FfmpegH265EncoderSidecar, FoxgloveSdk.Tests");
            Check(h264Sidecar != null && interfaceType.IsAssignableFrom(h264Sidecar),
                "78B-9: FFmpeg H.264 sidecar implements codec-neutral interface");
            Check(h265Sidecar != null && interfaceType.IsAssignableFrom(h265Sidecar),
                "78B-10: FFmpeg H.265 sidecar implements codec-neutral interface");

            var h264Source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var h265Source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs");
            Check(h264Source.Contains("LastDiagnosticLine") && h264Source.Contains("LastStderrLine"),
                "78B-11: FFmpeg H.264 diagnostic line maps from stderr");
            Check(h265Source.Contains("LastDiagnosticLine") && h265Source.Contains("LastStderrLine"),
                "78B-12: FFmpeg H.265 diagnostic line maps from stderr");
        }

        private static void VerifyH264AccessUnitNormalizer()
        {
            var normalizerType = Type.GetType("Foxglove.Schemas.Video.H264AccessUnitNormalizer, FoxgloveSdk.Tests");
            Check(normalizerType != null, "78C-1: H264AccessUnitNormalizer type exists");

            var normalizer = Activator.CreateInstance(normalizerType);
            var tryNormalize = normalizerType.GetMethod("TryNormalizeSample", new[] { typeof(byte[]), typeof(byte[]).MakeByRefType() });
            var cacheParameterSets = normalizerType.GetMethod("CacheParameterSets", new[] { typeof(byte[]) });
            Check(tryNormalize != null && tryNormalize.ReturnType == typeof(bool),
                "78C-2: normalizer exposes TryNormalizeSample");
            Check(cacheParameterSets != null,
                "78C-3: normalizer exposes CacheParameterSets");

            var annexBKey = Concat(
                H264AnnexBNal(7, 0x67, 0x64),
                H264AnnexBNal(8, 0x68, 0xee),
                H264AnnexBNal(5, 0x65, 0x88));
            Check(InvokeTryNormalize(normalizerType, normalizer, annexBKey, out var annexBOut),
                "78C-4: Annex B keyframe normalizes successfully");
            Check(annexBOut.SequenceEqual(annexBKey),
                "78C-5: Annex B keyframe is preserved");

            var avccKey = AvccSample(
                H264RawNal(7, 0x11),
                H264RawNal(8, 0x22),
                H264RawNal(5, 0x33));
            Check(InvokeTryNormalize(normalizerType, normalizer, avccKey, out var avccOut),
                "78C-6: length-prefixed keyframe normalizes successfully");
            Check(Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.HasAnnexBStartCode(avccOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsSpsNal(avccOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsPpsNal(avccOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsIdrNal(avccOut),
                "78C-7: length-prefixed keyframe becomes Annex B with SPS/PPS/IDR");

            var cachedNormalizer = Activator.CreateInstance(normalizerType);
            InvokeCacheParameterSets(normalizerType, cachedNormalizer, AvccSample(H264RawNal(7, 0x44), H264RawNal(8, 0x55)));
            Check(!InvokeTryNormalize(normalizerType, cachedNormalizer, AvccSample(H264RawNal(6, 0x06)), out _),
                "78C-8: non-VCL parameter/SEI sample is not published");
            Check(InvokeTryNormalize(normalizerType, cachedNormalizer, AvccSample(H264RawNal(5, 0x66)), out var cachedOut),
                "78C-9: cached SPS/PPS are prepended to later IDR sample");
            Check(Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsSpsNal(cachedOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsPpsNal(cachedOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsIdrNal(cachedOut),
                "78C-10: cached IDR output is decodable H.264");

            var deltaNormalizer = Activator.CreateInstance(normalizerType);
            Check(InvokeTryNormalize(normalizerType, deltaNormalizer, AvccSample(H264RawNal(1, 0x77)), out var deltaOut),
                "78C-11: non-IDR VCL sample normalizes successfully");
            Check(Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsVclNal(deltaOut)
                    && !Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsIdrNal(deltaOut),
                "78C-12: non-IDR output remains a delta access unit");
        }

        private static void VerifyMediaFoundationOptionsAndSidecar()
        {
            var optionsType = Type.GetType("Foxglove.Schemas.Video.MediaFoundationH264EncoderOptions, FoxgloveSdk.Tests");
            Check(optionsType != null, "78D-1: MediaFoundationH264EncoderOptions type exists");
            var options = Activator.CreateInstance(optionsType);
            SetField(optionsType, options, "Width", 640);
            SetField(optionsType, options, "Height", 480);
            SetField(optionsType, options, "FrameRate", 30);
            SetField(optionsType, options, "BitrateKbps", 4000);
            SetField(optionsType, options, "KeyframeInterval", 30);
            SetField(optionsType, options, "MaxInputQueue", 1);
            SetField(optionsType, options, "MaxOutputQueue", 4);

            Check(GetIntProperty(optionsType, options, "Rgb24FrameByteCount") == 640 * 480 * 3,
                "78D-2: options compute RGB24 byte count");
            Check(GetIntProperty(optionsType, options, "Nv12FrameByteCount") == 640 * 480 * 3 / 2,
                "78D-3: options compute NV12 byte count");

            var validate = optionsType.GetMethod("Validate", new[] { typeof(string).MakeByRefType() });
            Check(validate != null && validate.ReturnType == typeof(bool),
                "78D-4: options expose Validate(out error)");
            Check(InvokeValidate(optionsType, options, out _),
                "78D-5: standard 640x480 options validate");

            var oddOptions = Activator.CreateInstance(optionsType);
            SetField(optionsType, oddOptions, "Width", 641);
            SetField(optionsType, oddOptions, "Height", 480);
            Check(!InvokeValidate(optionsType, oddOptions, out var oddError) && oddError.Contains("even"),
                "78D-6: odd dimensions fail validation for NV12");

            var highOptions = Activator.CreateInstance(optionsType);
            SetField(optionsType, highOptions, "Width", 1920);
            SetField(optionsType, highOptions, "Height", 1080);
            Check(GetBoolProperty(optionsType, highOptions, "HasManagedConversionCostWarning"),
                "78D-7: options warn for high-resolution managed NV12 conversion");

            var sidecarType = Type.GetType("Foxglove.Schemas.Video.MediaFoundationH264EncoderSidecar, FoxgloveSdk.Tests");
            var interfaceType = Type.GetType("Foxglove.Schemas.Video.ICameraVideoEncoderSidecar, FoxgloveSdk.Tests");
            Check(sidecarType != null, "78D-8: MediaFoundationH264EncoderSidecar type exists");
            Check(interfaceType.IsAssignableFrom(sidecarType),
                "78D-9: MediaFoundation sidecar implements codec-neutral interface");
            Check(sidecarType.GetMethod("Start", new[] { optionsType })?.ReturnType == typeof(bool),
                "78D-10: MediaFoundation sidecar exposes Start(options)");
            Check(sidecarType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]) })?.ReturnType == typeof(bool),
                "78D-11: MediaFoundation sidecar exposes non-blocking frame submission");
            Check(sidecarType.GetMethod("TryDequeueAccessUnit")?.ReturnType == typeof(bool),
                "78D-12: MediaFoundation sidecar exposes access-unit dequeue");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            Check(source.Contains("H264AccessUnitNormalizer"),
                "78D-13: MediaFoundation sidecar normalizes encoder output");
            Check(source.Contains("IsWindows"),
                "78D-14: MediaFoundation sidecar has a Windows platform guard");
            Check(source.Contains("MFStartup") && source.Contains("CoCreateInstance"),
                "78D-15: MediaFoundation sidecar initializes native MF encoder");
            VerifyMediaFoundationGuidTable(sidecarType);
            CheckOrdered(source, "SetOutputType", "SetInputType",
                "78D-16: MediaFoundation sidecar configures output type before input type");
            Check(source.Contains("ConvertRgb24ToNv12"),
                "78D-17: MediaFoundation sidecar converts RGB24 input to NV12");
            Check(source.Contains("ProcessInput") && source.Contains("ProcessOutput"),
                "78D-18: MediaFoundation sidecar drives IMFTransform input/output");
            Check(!source.Contains("extern int MFSetAttributeSize") && !source.Contains("extern int MFSetAttributeRatio"),
                "78D-19: MediaFoundation inline attribute helpers are not P/Invoked");
            Check(source.Contains("PackUInt32PairAsUInt64") && source.Contains("SetUINT64"),
                "78D-20: MediaFoundation size/ratio attributes use packed UINT64 values");
            Check(source.Contains("DescribeException") && source.Contains("HResult=0x"),
                "78D-21: MediaFoundation runtime failures include diagnostic exception details");
            Check(source.Contains("UnmanagedType.LPArray") && source.Contains("GetBlob("),
                "78D-22: MediaFoundation blob attributes marshal byte buffers as LPArray");
            Check(!source.Contains("private interface IMFSample : IMFAttributes")
                  && source.Contains("IMFSample extends IMFAttributes"),
                "78D-23: IMFSample COM interface is flattened to match the native vtable");
            Check(source.Contains("[PreserveSig] int ProcessOutput")
                  && source.Contains("IntPtr pOutputSamples"),
                "78D-24: IMFTransform.ProcessOutput uses PreserveSig and unmanaged output buffers");
            Check(source.Contains("Marshal.StructureToPtr(output, outputPtr")
                  && source.Contains("Marshal.PtrToStructure<MftOutputDataBuffer>"),
                "78D-25: MediaFoundation output buffer marshaling avoids typelib-dependent struct arrays");
            Check(source.Contains("IMFSample.SetSampleTime failed.")
                  && source.Contains("IMFSample.SetSampleDuration failed."),
                "78D-26: MediaFoundation sample timestamp HRESULTs are checked");
            Check(source.Contains("ApplyMftLowLatencyAttribute")
                  && source.Contains("CODECAPI_AVLowLatencyMode"),
                "78D-27: MediaFoundation encoder enables low-latency mode");
            Check(source.Contains("ICodecAPI")
                  && source.Contains("CODECAPI_AVEncMPVGOPSize")
                  && source.Contains("CODECAPI_AVEncMPVDefaultBPictureCount"),
                "78D-28: MediaFoundation encoder applies real-time codec settings when available");
            CheckOrdered(source, "ConfigureLowLatencyEncoderOptions(options)", "SetOutputType",
                "78D-29: MediaFoundation low-latency codec settings are attempted before SetOutputType");
        }

        private static void VerifyMediaFoundationGuidTable(Type sidecarType)
        {
            var guidTable = sidecarType.GetNestedType("MfGuids", BindingFlags.NonPublic);
            Check(guidTable != null, "78D-15a: MediaFoundation GUID table exists");

            try
            {
                var fields = guidTable.GetFields(BindingFlags.Public | BindingFlags.Static);
                var count = 0;
                foreach (var field in fields)
                {
                    if (field.FieldType != typeof(Guid))
                        continue;

                    var value = (Guid)field.GetValue(null);
                    Check(value != Guid.Empty, "78D-15b: MediaFoundation GUID " + field.Name + " is valid");
                    count++;
                }

                Check(count >= 10, "78D-15c: MediaFoundation GUID table covers required attributes");
                CheckGuidField(guidTable, "MF_MT_MPEG2_PROFILE", "AD76A80B-2D5C-4E0B-B375-64E520137036");
                CheckGuidField(guidTable, "MF_MT_MPEG_SEQUENCE_HEADER", "3C036DE7-3AD0-4C9E-9216-EE6D6AC21CB3");
                CheckGuidField(guidTable, "CODECAPI_AVLowLatencyMode", "9C27891A-ED7A-40E1-88E8-B22727A024EE");
                CheckGuidField(guidTable, "CODECAPI_AVEncMPVGOPSize", "95F31B26-95A4-41AA-9303-246A7FC6EEF1");
                CheckGuidField(guidTable, "CODECAPI_AVEncMPVDefaultBPictureCount", "8D390AAC-DC5C-4200-B57F-814D04BABAB2");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "78D-15b: MediaFoundation GUID table initializes without TypeInitializationException: "
                    + ex.GetType().Name + " " + ex.Message,
                    ex);
            }
        }

        private static void CheckGuidField(Type guidTable, string fieldName, string expected)
        {
            var field = guidTable.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Check(field != null, "78D-15d: MediaFoundation GUID " + fieldName + " exists");
            var value = (Guid)field.GetValue(null);
            Check(value == new Guid(expected), "78D-15e: MediaFoundation GUID " + fieldName + " matches Windows SDK");
        }

        private static void VerifyCameraIntegrationSource()
        {
            var profileType = Type.GetType("Unity.FoxgloveSDK.Components.CameraVideoOutputProfile, FoxgloveSdk.Tests");
            Check(profileType.GetMethod("CreateSidecar", BindingFlags.NonPublic | BindingFlags.Instance) != null,
                "78E-1: CameraVideoOutputProfile owns sidecar factory");

            var cameraModeSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraOutputMode.cs");
            Check(cameraModeSource.Contains("MediaFoundationH264EncoderSidecar"),
                "78E-2: native H.264 profile creates MediaFoundation sidecar");
            Check(cameraModeSource.Contains("FfmpegH264EncoderSidecar") && cameraModeSource.Contains("FfmpegH265EncoderSidecar"),
                "78E-3: FFmpeg profiles remain factory-routed");

            var publisherSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(publisherSource.Contains("ICameraVideoEncoderSidecar _videoSidecar"),
                "78E-4: camera publisher stores codec-neutral sidecar");
            Check(publisherSource.Contains("profile.CreateSidecar()"),
                "78E-5: camera publisher routes sidecar creation through profile");
            Check(publisherSource.Contains("CreateMediaFoundationH264Options"),
                "78E-6: camera publisher creates native H.264 options");
            CheckOrdered(publisherSource, "ShouldPreparePublishPayload()", "EnsureVideoSidecarStarted(profile)",
                "78E-7: demand preflight remains before native sidecar startup");
            CheckOrdered(publisherSource, "ShouldPreparePublishPayload()", "_captureCam.Render()",
                "78E-8: demand preflight remains before camera render");
            Check(!publisherSource.Contains("FFmpeg encoder is not running."),
                "78E-9: generic video diagnostics no longer say FFmpeg for native mode");
            var submitIndex = publisherSource.IndexOf("private void SubmitVideoFrame", StringComparison.Ordinal);
            var trySubmitIndex = publisherSource.IndexOf("sidecar.TrySubmitFrame(frameBytes)", submitIndex, StringComparison.Ordinal);
            var drainIndex = publisherSource.IndexOf("DrainEncodedAccessUnits();", trySubmitIndex, StringComparison.Ordinal);
            Check(submitIndex >= 0 && trySubmitIndex > submitIndex && drainIndex > trySubmitIndex,
                "78E-10: camera publisher drains encoded video immediately after sidecar submit");

            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(editorSource.Contains("\"H.264 (Windows Native, Experimental)\""),
                "78E-11: camera editor exposes native Windows H.264 label");
            Check(editorSource.Contains("DrawNativeH264Section"),
                "78E-12: camera editor has a dedicated native H.264 section");
            Check(editorSource.Contains("no external FFmpeg") || editorSource.Contains("does not use FFmpeg"),
                "78E-13: camera editor explains native mode does not need FFmpeg");
        }

        public static void RunNativeSmoke()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 78 Native Media Foundation Smoke ===");
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                Console.WriteLine("Skipped: Windows Media Foundation is only available on Windows.");
                return;
            }

            var options = new Foxglove.Schemas.Video.MediaFoundationH264EncoderOptions
            {
                Width = 640,
                Height = 480,
                FrameRate = 10,
                BitrateKbps = 1000,
                KeyframeInterval = 10,
                MaxInputQueue = 1,
                MaxOutputQueue = 8
            };

            using (var sidecar = new Foxglove.Schemas.Video.MediaFoundationH264EncoderSidecar())
            {
                if (!sidecar.Start(options))
                    throw new InvalidOperationException("Native H.264 start failed: " + sidecar.LastError);

                var frame = new byte[options.Rgb24FrameByteCount];
                for (var i = 0; i < frame.Length; i += 3)
                {
                    frame[i] = 32;
                    frame[i + 1] = 128;
                    frame[i + 2] = 32;
                }

                var accessUnits = 0;
                var bytes = 0;
                var firstOutputAfterInput = -1;
                for (var i = 0; i < 30; i++)
                {
                    if (!sidecar.TrySubmitFrame(frame))
                        throw new InvalidOperationException("Native H.264 frame submit failed: " + sidecar.LastError);

                    while (sidecar.TryDequeueAccessUnit(out var accessUnit))
                    {
                        if (firstOutputAfterInput < 0)
                            firstOutputAfterInput = i + 1;
                        accessUnits++;
                        bytes += accessUnit?.Length ?? 0;
                    }
                }

                if (firstOutputAfterInput < 0 || firstOutputAfterInput > 2)
                    throw new InvalidOperationException(
                        "Native H.264 first output was delayed until input "
                        + firstOutputAfterInput + "; low-latency mode did not engage.");
                if (accessUnits < 28)
                    throw new InvalidOperationException(
                        "Native H.264 produced only " + accessUnits + " access units for 30 input frames.");

                Console.WriteLine(
                    "Native H.264 smoke completed. AccessUnits=" + accessUnits
                    + ", Bytes=" + bytes
                    + ", FirstOutputAfterInput=" + firstOutputAfterInput);
                Console.WriteLine("LastDiagnosticLine=" + (sidecar.LastDiagnosticLine ?? ""));
            }
        }

        private static string GetStringProperty(Type type, object target, string name)
            => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as string ?? "";

        private static bool GetBoolProperty(Type type, object target, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return value is bool b && b;
        }

        private static int GetIntProperty(Type type, object target, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return value is int i ? i : 0;
        }

        private static void SetField(Type type, object target, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);

            field.SetValue(target, value);
        }

        private static bool InvokeValidate(Type type, object target, out string error)
        {
            var args = new object[] { null };
            var result = (bool)type.GetMethod("Validate").Invoke(target, args);
            error = args[0] as string ?? "";
            return result;
        }

        private static void CheckOrdered(string text, string first, string second, string name)
        {
            var firstIdx = text.IndexOf(first, StringComparison.Ordinal);
            var secondIdx = text.IndexOf(second, StringComparison.Ordinal);
            Check(firstIdx >= 0 && secondIdx >= 0 && firstIdx < secondIdx, name);
        }

        private static bool InvokeTryNormalize(Type type, object normalizer, byte[] sample, out byte[] accessUnit)
        {
            var args = new object[] { sample, null };
            var result = (bool)type.GetMethod("TryNormalizeSample").Invoke(normalizer, args);
            accessUnit = (byte[])args[1];
            return result;
        }

        private static void InvokeCacheParameterSets(Type type, object normalizer, byte[] data)
            => type.GetMethod("CacheParameterSets").Invoke(normalizer, new object[] { data });

        private static byte[] H264AnnexBNal(byte type, params byte[] payload)
        {
            var nal = new byte[5 + payload.Length];
            nal[0] = 0;
            nal[1] = 0;
            nal[2] = 0;
            nal[3] = 1;
            nal[4] = type;
            Buffer.BlockCopy(payload, 0, nal, 5, payload.Length);
            return nal;
        }

        private static byte[] H264RawNal(byte type, params byte[] payload)
        {
            var nal = new byte[1 + payload.Length];
            nal[0] = type;
            Buffer.BlockCopy(payload, 0, nal, 1, payload.Length);
            return nal;
        }

        private static byte[] AvccSample(params byte[][] nals)
        {
            var length = nals.Sum(nal => 4 + (nal?.Length ?? 0));
            var result = new byte[length];
            var offset = 0;
            foreach (var nal in nals)
            {
                var nalLength = nal?.Length ?? 0;
                result[offset] = (byte)((nalLength >> 24) & 0xff);
                result[offset + 1] = (byte)((nalLength >> 16) & 0xff);
                result[offset + 2] = (byte)((nalLength >> 8) & 0xff);
                result[offset + 3] = (byte)(nalLength & 0xff);
                offset += 4;
                if (nalLength > 0)
                {
                    Buffer.BlockCopy(nal, 0, result, offset, nalLength);
                    offset += nalLength;
                }
            }

            return result;
        }

        private static byte[] Concat(params byte[][] chunks)
        {
            var length = chunks.Sum(chunk => chunk?.Length ?? 0);
            var result = new byte[length];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                if (chunk == null)
                    continue;

                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            return result;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : "";
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;

                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[OK] " + name);
        }
    }
}
