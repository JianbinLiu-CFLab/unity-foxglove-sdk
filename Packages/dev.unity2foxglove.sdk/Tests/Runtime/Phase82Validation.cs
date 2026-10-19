// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 82 validation for the experimental Windows native H.264 camera backend.

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Windows Media Foundation H.264 backend without requiring
    /// Unity Editor. The optional native smoke path runs only on Windows.
    /// </summary>
    public static partial class Phase82Validation
    {
        private const int NativeSmokeSubmittedFrames = 60;
        private const int NativeSmokeMaxFirstOutputInput = 3;
        private const int NativeSmokeExpectedMinimumAccessUnits = 57;

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
                Console.WriteLine("NATIVE_SMOKE_SKIPPED: Windows Media Foundation is only available on Windows.");
                return;
            }

            var optionsType = FindType("Foxglove.Schemas.Video.MediaFoundationH264EncoderOptions");
            var sidecarType = FindType("Foxglove.Schemas.Video.MediaFoundationH264EncoderSidecar");
            if (optionsType == null || sidecarType == null)
                throw new InvalidOperationException("Native H.264 types are not available.");
            if (!typeof(IDisposable).IsAssignableFrom(sidecarType))
                throw new InvalidOperationException("Native H.264 sidecar does not implement IDisposable.");

            var options = Activator.CreateInstance(optionsType);
            SetField(optionsType, options, "Width", 640);
            SetField(optionsType, options, "Height", 480);
            SetField(optionsType, options, "FrameRate", 30);
            SetField(optionsType, options, "BitrateKbps", 4000);
            SetField(optionsType, options, "KeyframeInterval", 30);
            SetField(optionsType, options, "MaxOutputQueue", 64);

            var retirementRegistryType = FindType("Foxglove.Schemas.Video.CameraVideoSidecarRetirementRegistry");
            var pendingBefore = retirementRegistryType == null
                ? 0
                : GetStaticIntProperty(retirementRegistryType, "PendingCountForTests");
            var nativeSmokeSkipped = false;

            using (var sidecar = (IDisposable)Activator.CreateInstance(sidecarType))
            {
                var start = sidecarType.GetMethod("Start", new[] { optionsType });
                if (start == null)
                    throw new InvalidOperationException("Native H.264 sidecar does not expose Start(options).");

                if (!(bool)start.Invoke(sidecar, new[] { options }))
                {
                    var error = GetStringProperty(sidecarType, sidecar, "LastError");
                    if (IsMissingH264Transform(error))
                    {
                        Console.WriteLine("NATIVE_SMOKE_SKIPPED: " + error);
                        nativeSmokeSkipped = true;
                    }
                    else
                    {
                        throw new InvalidOperationException("Native H.264 start failed: " + error);
                    }
                }

                if (!nativeSmokeSkipped)
                {
                    var frame = new byte[GetIntProperty(optionsType, options, "Rgb24FrameByteCount")];
                    for (var i = 0; i < frame.Length; i += 3)
                    {
                        frame[i] = 32;
                        frame[i + 1] = 128;
                        frame[i + 2] = 32;
                    }

                    var trySubmitFrame = sidecarType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]), typeof(ulong) });
                    var tryDequeueAccessUnit = sidecarType.GetMethod("TryDequeueAccessUnit");
                    if (trySubmitFrame == null)
                        trySubmitFrame = sidecarType.GetMethod("TrySubmitFrame", new[] { typeof(byte[]) });
                    if (trySubmitFrame == null || tryDequeueAccessUnit == null)
                        throw new InvalidOperationException("Native H.264 sidecar publish methods are not available.");

                    var inputCapacity = Math.Max(1, GetIntProperty(sidecarType, sidecar, "MaxInputQueue"));
                    var accessUnits = 0;
                    var bytes = 0;
                    var firstOutputAfterInput = -1;
                    for (var i = 0; i < NativeSmokeSubmittedFrames; i++)
                    {
                        var deadline = Stopwatch.GetTimestamp() + MillisecondsToTicks(200);
                        var submitted = false;
                        while (!submitted && Stopwatch.GetTimestamp() < deadline)
                        {
                            if (GetIntProperty(sidecarType, sidecar, "InputQueueDepth") < inputCapacity)
                            {
                                submitted = trySubmitFrame.GetParameters().Length == 2
                                    ? (bool)trySubmitFrame.Invoke(sidecar, new object[] { frame, (ulong)(i + 1) })
                                    : (bool)trySubmitFrame.Invoke(sidecar, new object[] { frame });
                            }

                            if (!submitted)
                                System.Threading.Thread.Sleep(1);
                        }

                        if (!submitted)
                            throw new InvalidOperationException("Native H.264 frame submit failed: " + GetStringProperty(sidecarType, sidecar, "LastError"));

                        deadline = Stopwatch.GetTimestamp() + MillisecondsToTicks(200);
                        while (Stopwatch.GetTimestamp() < deadline)
                        {
                            var drained = false;
                            while (InvokeTryDequeueAccessUnit(tryDequeueAccessUnit, sidecar, out var accessUnit))
                            {
                                drained = true;
                                if (firstOutputAfterInput < 0)
                                    firstOutputAfterInput = i + 1;
                                accessUnits++;
                                bytes += accessUnit?.Length ?? 0;
                            }

                            if (drained || firstOutputAfterInput >= 0)
                                break;
                            System.Threading.Thread.Sleep(1);
                        }
                    }

                    var finalDeadline = Stopwatch.GetTimestamp() + MillisecondsToTicks(2000);
                    while (Stopwatch.GetTimestamp() < finalDeadline)
                    {
                        while (InvokeTryDequeueAccessUnit(tryDequeueAccessUnit, sidecar, out var accessUnit))
                        {
                            if (firstOutputAfterInput < 0)
                                firstOutputAfterInput = NativeSmokeSubmittedFrames;
                            accessUnits++;
                            bytes += accessUnit?.Length ?? 0;
                        }

                        if (accessUnits >= NativeSmokeSubmittedFrames)
                            break;
                        System.Threading.Thread.Sleep(1);
                    }

                    if (accessUnits < NativeSmokeExpectedMinimumAccessUnits)
                        throw new InvalidOperationException(
                            "Native H.264 produced only " + accessUnits
                            + " access units for " + NativeSmokeSubmittedFrames + " input frames; expected at least " + NativeSmokeExpectedMinimumAccessUnits + ".");
                    if (firstOutputAfterInput < 0 || firstOutputAfterInput > NativeSmokeMaxFirstOutputInput)
                        throw new InvalidOperationException(
                            "Native H.264 first output was delayed until input "
                            + firstOutputAfterInput + "; low-latency mode did not engage within the startup buffer.");
                    var droppedInputFrames = GetLongProperty(sidecarType, sidecar, "DroppedInputFrames");
                    if (droppedInputFrames != 0)
                        throw new InvalidOperationException("Native H.264 dropped " + droppedInputFrames + " input frames during smoke.");

                    Console.WriteLine(
                        "Native H.264 smoke completed. AccessUnits=" + accessUnits
                        + ", Bytes=" + bytes
                        + ", FirstOutputAfterInput=" + firstOutputAfterInput);
                    Console.WriteLine("LastDiagnosticLine=" + GetStringProperty(sidecarType, sidecar, "LastDiagnosticLine"));
                }
            }

            if (retirementRegistryType != null)
            {
                var pendingAfter = GetStaticIntProperty(retirementRegistryType, "PendingCountForTests");
                if (pendingAfter != pendingBefore)
                    throw new InvalidOperationException("Native H.264 retirement registry retained " + pendingAfter + " sidecars.");
            }
        }

        private static long MillisecondsToTicks(int milliseconds)
            => (long)(milliseconds * (double)Stopwatch.Frequency / 1000.0);

        private static bool IsMissingH264Transform(string error)
        {
            return error != null
                && error.IndexOf("Could not create the Windows Media Foundation H.264 encoder MFT.", StringComparison.OrdinalIgnoreCase) >= 0
                && error.IndexOf("HRESULT=0x80040154", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void VerifyModeProfile()
        {
            var modeType = FindType("Unity.FoxgloveSDK.Components.CameraOutputMode");
            Check(modeType != null && modeType.IsEnum, "82A-1: CameraOutputMode enum exists");
            Check(Enum.GetNames(modeType).Contains("H264OpenH264"),
                "82A-2: OpenH264 mode remains present");
            Check(Convert.ToInt32(Enum.Parse(modeType, "H264OpenH264")) == 3,
                "82A-3: H264OpenH264 enum value stays stable at 3");
            Check(Enum.GetNames(modeType).Contains("H264MediaFoundationExperimental"),
                "82A-4: CameraOutputMode exposes H264MediaFoundationExperimental");
            Check(Convert.ToInt32(Enum.Parse(modeType, "H264MediaFoundationExperimental")) == 4,
                "82A-5: H264MediaFoundationExperimental enum value is stable at 4");

            var profileType = FindType("Unity.FoxgloveSDK.Components.CameraVideoOutputProfile");
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
            var normalizerType = FindType("Foxglove.Schemas.Video.H264AccessUnitNormalizer");
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
            var optionsType = FindType("Foxglove.Schemas.Video.MediaFoundationH264EncoderOptions");
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

            var sidecarType = FindType("Foxglove.Schemas.Video.MediaFoundationH264EncoderSidecar");
            var interfaceType = FindType("Foxglove.Schemas.Video.ICameraVideoEncoderSidecar");
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
            var interopSource = ReadMediaFoundationSidecarSources();
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
            Check(interopSource.Contains("UnmanagedType.LPArray") && interopSource.Contains("GetBlob("),
                "82C-22: Media Foundation blob attributes marshal byte buffers as LPArray");
            Check(!interopSource.Contains("private interface IMFSample : IMFAttributes")
                  && interopSource.Contains("IMFSample extends IMFAttributes"),
                "82C-23: IMFSample COM interface is flattened to match the native vtable");
            Check(interopSource.Contains("[PreserveSig] int ProcessOutput")
                  && interopSource.Contains("IntPtr pOutputSamples"),
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
    }
}
