// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 82 validation for the experimental Windows native H.264 camera backend.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Windows Media Foundation H.264 backend without requiring
    /// Unity Editor. The optional native smoke path runs only on Windows.
    /// </summary>
    public static class Phase82Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 82: Windows Native H.264 Experimental Backend ===");
            _passed = 0;

            VerifyModeProfile();
            VerifyH264AccessUnitNormalizer();
            VerifyMediaFoundationOptionsAndSidecar();
            VerifyCameraIntegrationSource();
            VerifyInspectorUxSource();

            Console.WriteLine($"Phase 82: {_passed} checks passed.");
        }

        public static void RunNativeSmoke()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 82 Native Media Foundation Smoke ===");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("Skipped: Windows Media Foundation is only available on Windows.");
                return;
            }

            var optionsType = Type.GetType("Foxglove.Schemas.Video.MediaFoundationH264EncoderOptions, FoxgloveSdk.Tests");
            var sidecarType = Type.GetType("Foxglove.Schemas.Video.MediaFoundationH264EncoderSidecar, FoxgloveSdk.Tests");
            if (optionsType == null || sidecarType == null)
                throw new InvalidOperationException("Native H.264 types are not available.");

            var options = Activator.CreateInstance(optionsType);
            SetField(optionsType, options, "Width", 640);
            SetField(optionsType, options, "Height", 480);
            SetField(optionsType, options, "FrameRate", 30);
            SetField(optionsType, options, "BitrateKbps", 4000);
            SetField(optionsType, options, "KeyframeInterval", 30);
            SetField(optionsType, options, "MaxInputQueue", 1);
            SetField(optionsType, options, "MaxOutputQueue", 8);

            using (var sidecar = (IDisposable)Activator.CreateInstance(sidecarType))
            {
                var start = sidecarType.GetMethod("Start", new[] { optionsType });
                if (start == null)
                    throw new InvalidOperationException("Native H.264 sidecar does not expose Start(options).");

                if (!(bool)start.Invoke(sidecar, new[] { options }))
                    throw new InvalidOperationException("Native H.264 start failed: " + GetStringProperty(sidecarType, sidecar, "LastError"));

                var frame = new byte[GetIntProperty(optionsType, options, "Rgb24FrameByteCount")];
                for (var i = 0; i < frame.Length; i += 3)
                {
                    frame[i] = 32;
                    frame[i + 1] = 128;
                    frame[i + 2] = 32;
                }

                var trySubmitFrame = sidecarType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]) });
                var tryDequeueAccessUnit = sidecarType.GetMethod("TryDequeueAccessUnit");
                if (trySubmitFrame == null || tryDequeueAccessUnit == null)
                    throw new InvalidOperationException("Native H.264 sidecar publish methods are not available.");

                var accessUnits = 0;
                var bytes = 0;
                var firstOutputAfterInput = -1;
                for (var i = 0; i < 60; i++)
                {
                    if (!(bool)trySubmitFrame.Invoke(sidecar, new object[] { frame }))
                        throw new InvalidOperationException("Native H.264 frame submit failed: " + GetStringProperty(sidecarType, sidecar, "LastError"));

                    while (InvokeTryDequeueAccessUnit(tryDequeueAccessUnit, sidecar, out var accessUnit))
                    {
                        if (firstOutputAfterInput < 0)
                            firstOutputAfterInput = i + 1;

                        accessUnits++;
                        bytes += accessUnit?.Length ?? 0;
                    }
                }

                var expectedMinimum = 57;
                if (firstOutputAfterInput < 0 || firstOutputAfterInput > 2)
                    throw new InvalidOperationException(
                        "Native H.264 first output was delayed until input "
                        + firstOutputAfterInput + "; low-latency mode did not engage.");
                if (accessUnits < expectedMinimum)
                    throw new InvalidOperationException(
                        "Native H.264 produced only " + accessUnits
                        + " access units for 60 input frames; expected at least " + expectedMinimum + ".");

                Console.WriteLine(
                    "Native H.264 smoke completed. AccessUnits=" + accessUnits
                    + ", Bytes=" + bytes
                    + ", FirstOutputAfterInput=" + firstOutputAfterInput);
                Console.WriteLine("LastDiagnosticLine=" + GetStringProperty(sidecarType, sidecar, "LastDiagnosticLine"));
            }
        }

        private static void VerifyModeProfile()
        {
            var modeType = Type.GetType("Unity.FoxgloveSDK.Components.CameraOutputMode, FoxgloveSdk.Tests");
            Check(modeType != null && modeType.IsEnum, "82A-1: CameraOutputMode enum exists");
            Check(Enum.GetNames(modeType).Contains("H264OpenH264"),
                "82A-2: OpenH264 mode remains present");
            Check(Convert.ToInt32(Enum.Parse(modeType, "H264OpenH264")) == 3,
                "82A-3: H264OpenH264 enum value stays stable at 3");
            Check(Enum.GetNames(modeType).Contains("H264MediaFoundationExperimental"),
                "82A-4: CameraOutputMode exposes H264MediaFoundationExperimental");
            Check(Convert.ToInt32(Enum.Parse(modeType, "H264MediaFoundationExperimental")) == 4,
                "82A-5: H264MediaFoundationExperimental enum value is stable at 4");

            var profileType = Type.GetType("Unity.FoxgloveSDK.Components.CameraVideoOutputProfile, FoxgloveSdk.Tests");
            Check(profileType != null, "82A-6: CameraVideoOutputProfile exists");
            var forMode = profileType.GetMethod("ForMode", BindingFlags.Public | BindingFlags.Static);
            Check(forMode != null, "82A-7: CameraVideoOutputProfile.ForMode exists");

            var profile = forMode.Invoke(null, new[] { Enum.Parse(modeType, "H264MediaFoundationExperimental") });
            Check(GetStringProperty(profileType, profile, "DefaultTopic") == "/unity/camera",
                "82A-8: native H.264 default topic is /unity/camera");
            Check(GetStringProperty(profileType, profile, "SchemaName") == "foxglove.CompressedVideo",
                "82A-9: native H.264 schema is foxglove.CompressedVideo");
            Check(GetStringProperty(profileType, profile, "VideoFormat") == "h264",
                "82A-10: native H.264 video format is h264");
            var displayName = GetStringProperty(profileType, profile, "DisplayName");
            Check(displayName.Contains("Windows Native") && displayName.Contains("Experimental"),
                "82A-11: native H.264 display name calls out Windows Native and Experimental");
            Check(!GetBoolProperty(profileType, profile, "SupportsJson")
                    && GetBoolProperty(profileType, profile, "SupportsProtobuf"),
                "82A-12: native H.264 supports protobuf only");
        }

        private static void VerifyH264AccessUnitNormalizer()
        {
            var normalizerType = Type.GetType("Foxglove.Schemas.Video.H264AccessUnitNormalizer, FoxgloveSdk.Tests");
            Check(normalizerType != null, "82B-1: H264AccessUnitNormalizer type exists");

            var normalizer = Activator.CreateInstance(normalizerType);
            var tryNormalize = normalizerType.GetMethod("TryNormalizeSample", new[] { typeof(byte[]), typeof(byte[]).MakeByRefType() });
            var cacheParameterSets = normalizerType.GetMethod("CacheParameterSets", new[] { typeof(byte[]) });
            Check(tryNormalize != null && tryNormalize.ReturnType == typeof(bool),
                "82B-2: normalizer exposes TryNormalizeSample");
            Check(cacheParameterSets != null,
                "82B-3: normalizer exposes CacheParameterSets");

            var annexBKey = Concat(
                H264AnnexBNal(7, 0x67, 0x64),
                H264AnnexBNal(8, 0x68, 0xee),
                H264AnnexBNal(5, 0x65, 0x88));
            Check(InvokeTryNormalize(normalizerType, normalizer, annexBKey, out var annexBOut),
                "82B-4: Annex B keyframe normalizes successfully");
            Check(annexBOut.SequenceEqual(annexBKey),
                "82B-5: Annex B keyframe is preserved");

            var avccKey = AvccSample(
                H264RawNal(7, 0x11),
                H264RawNal(8, 0x22),
                H264RawNal(5, 0x33));
            Check(InvokeTryNormalize(normalizerType, normalizer, avccKey, out var avccOut),
                "82B-6: length-prefixed keyframe normalizes successfully");
            Check(Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.HasAnnexBStartCode(avccOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsSpsNal(avccOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsPpsNal(avccOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsIdrNal(avccOut),
                "82B-7: length-prefixed keyframe becomes Annex B with SPS/PPS/IDR");

            var cachedNormalizer = Activator.CreateInstance(normalizerType);
            InvokeCacheParameterSets(normalizerType, cachedNormalizer, AvccSample(H264RawNal(7, 0x44), H264RawNal(8, 0x55)));
            Check(!InvokeTryNormalize(normalizerType, cachedNormalizer, AvccSample(H264RawNal(6, 0x06)), out _),
                "82B-8: non-VCL parameter/SEI sample is not published");
            Check(InvokeTryNormalize(normalizerType, cachedNormalizer, AvccSample(H264RawNal(5, 0x66)), out var cachedOut),
                "82B-9: cached SPS/PPS are prepended to later IDR sample");
            Check(Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsSpsNal(cachedOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsPpsNal(cachedOut)
                    && Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsIdrNal(cachedOut),
                "82B-10: cached IDR output is decodable H.264");

            var deltaNormalizer = Activator.CreateInstance(normalizerType);
            Check(InvokeTryNormalize(normalizerType, deltaNormalizer, AvccSample(H264RawNal(1, 0x77)), out var deltaOut),
                "82B-11: non-IDR VCL sample normalizes successfully");
            Check(Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsVclNal(deltaOut)
                    && !Foxglove.Schemas.Video.H264AnnexBAccessUnitPacketizer.ContainsIdrNal(deltaOut),
                "82B-12: non-IDR output remains a delta access unit");
        }

        private static void VerifyMediaFoundationOptionsAndSidecar()
        {
            var optionsType = Type.GetType("Foxglove.Schemas.Video.MediaFoundationH264EncoderOptions, FoxgloveSdk.Tests");
            Check(optionsType != null, "82C-1: MediaFoundationH264EncoderOptions type exists");
            var options = Activator.CreateInstance(optionsType);
            SetField(optionsType, options, "Width", 640);
            SetField(optionsType, options, "Height", 480);
            SetField(optionsType, options, "FrameRate", 30);
            SetField(optionsType, options, "BitrateKbps", 4000);
            SetField(optionsType, options, "KeyframeInterval", 30);
            SetField(optionsType, options, "MaxInputQueue", 1);
            SetField(optionsType, options, "MaxOutputQueue", 4);

            Check(GetIntProperty(optionsType, options, "Rgb24FrameByteCount") == 640 * 480 * 3,
                "82C-2: options compute RGB24 byte count");
            Check(GetIntProperty(optionsType, options, "Nv12FrameByteCount") == 640 * 480 * 3 / 2,
                "82C-3: options compute NV12 byte count");

            var validate = optionsType.GetMethod("Validate", new[] { typeof(string).MakeByRefType() });
            Check(validate != null && validate.ReturnType == typeof(bool),
                "82C-4: options expose Validate(out error)");
            Check(InvokeValidate(optionsType, options, out _),
                "82C-5: standard 640x480 options validate");

            var oddOptions = Activator.CreateInstance(optionsType);
            SetField(optionsType, oddOptions, "Width", 641);
            SetField(optionsType, oddOptions, "Height", 480);
            Check(!InvokeValidate(optionsType, oddOptions, out var oddError) && oddError.Contains("even"),
                "82C-6: odd dimensions fail validation for NV12");

            var highOptions = Activator.CreateInstance(optionsType);
            SetField(optionsType, highOptions, "Width", 1920);
            SetField(optionsType, highOptions, "Height", 1080);
            Check(GetBoolProperty(optionsType, highOptions, "HasManagedConversionCostWarning"),
                "82C-7: options warn for high-resolution managed NV12 conversion");

            var sidecarType = Type.GetType("Foxglove.Schemas.Video.MediaFoundationH264EncoderSidecar, FoxgloveSdk.Tests");
            var interfaceType = Type.GetType("Foxglove.Schemas.Video.ICameraVideoEncoderSidecar, FoxgloveSdk.Tests");
            Check(sidecarType != null, "82C-8: MediaFoundationH264EncoderSidecar type exists");
            Check(interfaceType != null && interfaceType.IsAssignableFrom(sidecarType),
                "82C-9: MediaFoundation sidecar implements codec-neutral interface");
            Check(sidecarType.GetMethod("Start", new[] { optionsType })?.ReturnType == typeof(bool),
                "82C-10: MediaFoundation sidecar exposes Start(options)");
            Check(sidecarType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]) })?.ReturnType == typeof(bool),
                "82C-11: MediaFoundation sidecar exposes non-blocking frame submission");
            Check(sidecarType.GetMethod("TryDequeueAccessUnit")?.ReturnType == typeof(bool),
                "82C-12: MediaFoundation sidecar exposes access-unit dequeue");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            Check(source.Contains("H264AccessUnitNormalizer"),
                "82C-13: MediaFoundation sidecar normalizes encoder output");
            Check(source.Contains("IsWindows"),
                "82C-14: MediaFoundation sidecar has a Windows platform guard");
            Check(source.Contains("MFStartup") && source.Contains("CoCreateInstance"),
                "82C-15: MediaFoundation sidecar initializes native MF encoder");
            VerifyMediaFoundationGuidTable(sidecarType);
            CheckOrdered(source, "SetOutputType", "SetInputType",
                "82C-16: MediaFoundation sidecar configures output type before input type");
            Check(source.Contains("ConvertRgb24ToNv12"),
                "82C-17: MediaFoundation sidecar converts RGB24 input to NV12");
            Check(source.Contains("ProcessInput") && source.Contains("ProcessOutput"),
                "82C-18: MediaFoundation sidecar drives IMFTransform input/output");
            Check(!source.Contains("extern int MFSetAttributeSize") && !source.Contains("extern int MFSetAttributeRatio"),
                "82C-19: Media Foundation inline attribute helpers are not P/Invoked");
            Check(source.Contains("PackUInt32PairAsUInt64") && source.Contains("SetUINT64"),
                "82C-20: Media Foundation size/ratio attributes use packed UINT64 values");
            Check(source.Contains("DescribeException") && source.Contains("HResult=0x"),
                "82C-21: Media Foundation runtime failures include diagnostic exception details");
            Check(source.Contains("UnmanagedType.LPArray") && source.Contains("GetBlob("),
                "82C-22: Media Foundation blob attributes marshal byte buffers as LPArray");
            Check(!source.Contains("private interface IMFSample : IMFAttributes")
                  && source.Contains("IMFSample extends IMFAttributes"),
                "82C-23: IMFSample COM interface is flattened to match the native vtable");
            Check(source.Contains("[PreserveSig] int ProcessOutput")
                  && source.Contains("IntPtr pOutputSamples"),
                "82C-24: IMFTransform.ProcessOutput uses PreserveSig and unmanaged output buffers");
            Check(source.Contains("Marshal.StructureToPtr(output, outputPtr")
                  && source.Contains("Marshal.PtrToStructure<MftOutputDataBuffer>"),
                "82C-25: Media Foundation output buffer marshaling avoids typelib-dependent struct arrays");
            Check(source.Contains("IMFSample.SetSampleTime failed.")
                  && source.Contains("IMFSample.SetSampleDuration failed."),
                "82C-26: Media Foundation sample timestamp HRESULTs are checked");
            Check(source.Contains("ApplyMftLowLatencyAttribute")
                  && source.Contains("CODECAPI_AVLowLatencyMode"),
                "82C-27: Media Foundation encoder enables low-latency mode");
            Check(source.Contains("ICodecAPI")
                  && source.Contains("CODECAPI_AVEncMPVGOPSize")
                  && source.Contains("CODECAPI_AVEncMPVDefaultBPictureCount"),
                "82C-28: Media Foundation encoder applies real-time codec settings when available");
            CheckOrdered(source, "ConfigureLowLatencyEncoderOptions(options)", "SetOutputType",
                "82C-29: Media Foundation low-latency codec settings are attempted before SetOutputType");
        }

        private static void VerifyCameraIntegrationSource()
        {
            var publisherSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(publisherSource.Contains("ICameraVideoEncoderSidecar _videoSidecar"),
                "82D-1: camera publisher stores codec-neutral sidecar");
            Check(publisherSource.Contains("CameraOutputMode.H264MediaFoundationExperimental"),
                "82D-2: camera publisher routes native H.264 mode");
            Check(publisherSource.Contains("MediaFoundationH264EncoderSidecar") && publisherSource.Contains("MediaFoundationH264EncoderOptions"),
                "82D-3: camera publisher starts the Media Foundation sidecar");
            Check(publisherSource.Contains("CreateMediaFoundationH264Options"),
                "82D-4: camera publisher creates native H.264 options");
            CheckOrdered(publisherSource, "ShouldPreparePublishPayload()", "EnsureVideoSidecarStarted(profile)",
                "82D-5: demand preflight remains before native sidecar startup");
            CheckOrdered(publisherSource, "ShouldPreparePublishPayload()", "_captureCam.Render()",
                "82D-6: demand preflight remains before camera render");
            var submitIndex = publisherSource.IndexOf("private void SubmitVideoFrame", StringComparison.Ordinal);
            var trySubmitIndex = publisherSource.IndexOf("sidecar.TrySubmitFrame(frameBytes)", submitIndex, StringComparison.Ordinal);
            var drainIndex = publisherSource.IndexOf("DrainEncodedAccessUnits();", trySubmitIndex, StringComparison.Ordinal);
            Check(submitIndex >= 0 && trySubmitIndex > submitIndex && drainIndex > trySubmitIndex,
                "82D-7: camera publisher drains encoded video immediately after sidecar submit");
        }

        private static void VerifyInspectorUxSource()
        {
            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(editorSource.Contains("\"H.264 (Windows Native, Experimental)\""),
                "82E-1: camera editor exposes native Windows H.264 label");
            Check(editorSource.Contains("DrawNativeH264Section"),
                "82E-2: camera editor has a dedicated native H.264 section");
            Check(editorSource.Contains("does not use FFmpeg") || editorSource.Contains("no external FFmpeg"),
                "82E-3: camera editor explains native mode does not need FFmpeg");
            Check(editorSource.Contains("Media Foundation"),
                "82E-4: camera editor names Media Foundation as the backend");
        }

        private static void VerifyMediaFoundationGuidTable(Type sidecarType)
        {
            var guidTable = sidecarType.GetNestedType("MfGuids", BindingFlags.NonPublic);
            Check(guidTable != null, "82C-15a: Media Foundation GUID table exists");

            try
            {
                var fields = guidTable.GetFields(BindingFlags.Public | BindingFlags.Static);
                var count = 0;
                foreach (var field in fields)
                {
                    if (field.FieldType != typeof(Guid))
                        continue;

                    var value = (Guid)field.GetValue(null);
                    Check(value != Guid.Empty, "82C-15b: Media Foundation GUID " + field.Name + " is valid");
                    count++;
                }

                Check(count >= 10, "82C-15c: Media Foundation GUID table covers required attributes");
                CheckGuidField(guidTable, "MF_MT_MPEG2_PROFILE", "AD76A80B-2D5C-4E0B-B375-64E520137036");
                CheckGuidField(guidTable, "MF_MT_MPEG_SEQUENCE_HEADER", "3C036DE7-3AD0-4C9E-9216-EE6D6AC21CB3");
                CheckGuidField(guidTable, "CODECAPI_AVLowLatencyMode", "9C27891A-ED7A-40E1-88E8-B22727A024EE");
                CheckGuidField(guidTable, "CODECAPI_AVEncMPVGOPSize", "95F31B26-95A4-41AA-9303-246A7FC6EEF1");
                CheckGuidField(guidTable, "CODECAPI_AVEncMPVDefaultBPictureCount", "8D390AAC-DC5C-4200-B57F-814D04BABAB2");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "82C-15b: Media Foundation GUID table initializes without TypeInitializationException: "
                    + ex.GetType().Name + " " + ex.Message,
                    ex);
            }
        }

        private static void CheckGuidField(Type guidTable, string fieldName, string expected)
        {
            var field = guidTable.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Check(field != null, "82C-15d: Media Foundation GUID " + fieldName + " exists");
            var value = (Guid)field.GetValue(null);
            Check(value == new Guid(expected), "82C-15e: Media Foundation GUID " + fieldName + " matches Windows SDK");
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

        private static bool InvokeTryNormalize(Type type, object normalizer, byte[] sample, out byte[] accessUnit)
        {
            var args = new object[] { sample, null };
            var result = (bool)type.GetMethod("TryNormalizeSample").Invoke(normalizer, args);
            accessUnit = (byte[])args[1];
            return result;
        }

        private static bool InvokeTryDequeueAccessUnit(MethodInfo method, object sidecar, out byte[] accessUnit)
        {
            var args = new object[] { null };
            var result = (bool)method.Invoke(sidecar, args);
            accessUnit = (byte[])args[0];
            return result;
        }

        private static void InvokeCacheParameterSets(Type type, object normalizer, byte[] data)
            => type.GetMethod("CacheParameterSets").Invoke(normalizer, new object[] { data });

        private static void CheckOrdered(string text, string first, string second, string name)
        {
            var firstIdx = text.IndexOf(first, StringComparison.Ordinal);
            var secondIdx = text.IndexOf(second, StringComparison.Ordinal);
            Check(firstIdx >= 0 && secondIdx >= 0 && firstIdx < secondIdx, name);
        }

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
            Console.WriteLine("[PASS] " + name);
        }
    }
}
