// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-15 regression coverage for video encoding sidecar review fixes.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Foxglove.Schemas.Video;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_15Validation.
    /// </summary>
    /// <remarks>
    /// Source-text checks in Module 7 validators are labeled architecture guards for boundaries
    /// the standalone harness cannot observe (Unity, COM, native jobs, allocation shape).
    /// Module7Coverage maps every finding to its xUnit behavior tests; retired source checks
    /// are listed with the behavior test that replaced them.
    /// </remarks>
    public static class Phase140_15Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-15: Video Encoding Sidecars ===");
            _passed = 0;

            MediaFoundationCreateSampleReleasesPartialSamples();
            FfmpegStdoutFlushDrainsPacketizerTail();
            MediaFoundationStreamChangeIsBoundedAndRenegotiated();
            FfmpegPresetValidationRejectsUnexpectedPresetValues();
            FfmpegOutputCountersUseLockOnlyArithmetic();
            VideoTimestampPairingUsesPtsBearingMpegTs();
            CameraVideoSubmitUsesFrameByteSourceContract();
            OpenH264I420ScratchIsReusedBeforeSidecarCopy();
            FfmpegStdoutReadersAppendReadBufferRanges();
            PacketizersCopyRangesWithoutManualByteLoop();
            H264NormalizerAvoidsLinqHotPathPasses();
            OpenH264StdoutReaderReusesLengthHeader();
            SidecarsCacheQueueCapacitiesOnStart();
            FrameSourceSubmissionUsesGenericStructPath();
            MediaFoundationWorkerOwnsNativeCleanup();
            Module7CoverageIsClassified();

            Console.WriteLine($"Phase 140-15: {_passed} checks passed.");
        }

        private static readonly string[] Module7ValidatorFiles =
        {
            "Phase138QValidation.cs",
            "Phase138SValidation.cs",
            "Phase140_14Validation.cs",
            "Phase140_15Validation.cs",
            "Phase140_17Validation.cs",
            "Phase163_18Validation.cs",
            "Phase164_15Validation.cs",
            "Phase82Validation.cs"
        };

        // finding|title|xUnit behavior tests|architecture guards
        private static readonly string[] Module7Coverage =
        {
            "M7-01|Raw and async JPEG ownership copy|RawSnapshotOwnsPixelsBeforeSubscriberMutation|164-15D-1;164-15D-2",
            "M7-02|Video caller-thread copy and conversion|FrameSourceCopiesOnlyLogicalBytesIntoOversizedPoolBuffer;PublishPipelineHandsEachFrameToEncoderWithExactlyOneCopy|140-15N-4;164-15C-3",
            "M7-03|Media Foundation submit and lifecycle, plus --phase82-native-smoke|MediaFoundationSubmissionRejectsFrameWhenStopped;MediaFoundationInputQueueReportsDroppedInputFrames;MediaFoundationWorkerReleaseClearsManagedStateOnWorkerThread;MediaFoundationTimestampTrackingEvictsOnlyTheOldestSample|140-15A-1;140-15D-1;140-15Q-1",
            "M7-04|VirtualImu global physics ownership|ExtremeTargetRateIsNormalizedAndTickWorkIsBounded|140-17B-1;140-17B-3;140-17C-2",
            "M7-05|IMU cumulative drop counter|ResizePreservesLifetimeDroppedCount|",
            "M7-06|LiDAR Complete() stall|PendingScanCompletionRequiresACompletedScheduledBatch|140-17L-1",
            "M7-07|Draco MsgPack fail-closed|DracoMsgPackFailureNeverPublishesProtobufFallback|",
            "M7-08|JPEG queue resize race|CameraPipelineResizeMigratesNewestRequestsUnderOneQueueGate|138Q-5D4",
            "M7-09|JPEG admission semantics|JpegQueueAdmitsNewestFrameAndReportsReplacedFrame|",
            "M7-10|Source-shape-heavy verification||140-15P-4",
            "M7-11|Two full-frame video copies|OpenH264RgbFrameSourceQueuesRgbBytesForWorkerConversion;PublishPipelineHandsEachFrameToEncoderWithExactlyOneCopy|140-15H-1;140-15I-1",
            "M7-12|FFmpeg timestamp approximation|MatchesMpegTsPtsToCaptureTimestampsWithoutFifoAssumption;RealFfmpegSidecarPairsEveryAccessUnitWithItsCaptureTimestamp;OpenH264StartInfoUsesTimestampProtocolV2;ProtocolMarkerAfterLegacyFallbackIsReportedAsMismatch|140-15G-1;140-15G-2"
        };

        // retired source check|xUnit behavior test that replaced it
        private static readonly string[] Module7RetiredSourceChecks =
        {
            "140-15O-1|PublishPipelineHandsEachFrameToEncoderWithExactlyOneCopy",
            "140-15C-1|MediaFoundationTimestampTrackingEvictsOnlyTheOldestSample",
            "164-15B-1|CameraReadbackTimingKeepsTheNewestRequestsAndClearForgetsThem",
            "164-15B-3|CameraReadbackTimingKeepsTheNewestRequestsAndClearForgetsThem",
            "138Q-5D3|JpegQueueAdmitsNewestFrameAndReportsReplacedFrame"
        };

        private static readonly Regex MethodHeader = new Regex(
            @"^        (?:private|public|internal) static [^\r\n]*?\w+\([^)]*\)\r?$",
            RegexOptions.Multiline);

        private static readonly Regex SourceRead = new Regex(
            @"\bRead(Source)?\(|File\.ReadAllText|Slice\(|SourceMethod\(");

        private static readonly Regex UnlabeledCheck = new Regex(
            "\"\\d{3}[A-Z]?-[0-9A-Za-z]+(?:-[0-9A-Za-z]+)?: ");

        private static void Module7CoverageIsClassified()
        {
            var unitTestBuilder = new StringBuilder();
            foreach (var path in Directory.EnumerateFiles("Packages/dev.unity2foxglove.sdk/Tests/Unit", "*.cs", SearchOption.AllDirectories))
                unitTestBuilder.Append(File.ReadAllText(path));
            var unitTests = unitTestBuilder.ToString();
            var validators = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Module7ValidatorFiles)
                validators[file] = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + file);
            var validatorText = string.Concat(validators.Values);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var classified = Module7Coverage.Length == 12;
            var behaviorBacked = true;
            var guardsPresent = true;
            foreach (var entry in Module7Coverage)
            {
                var parts = entry.Split('|');
                classified &= parts.Length == 4 && parts[0].StartsWith("M7-", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(parts[1]) && seen.Add(parts[0]);
                if (parts.Length != 4)
                    continue;

                var tests = SplitList(parts[2]);
                behaviorBacked &= (tests.Length > 0 || parts[0] == "M7-10")
                    && tests.All(test => unitTests.Contains("void " + test + "(", StringComparison.Ordinal));
                guardsPresent &= SplitList(parts[3]).All(
                    guard => validatorText.Contains("\"architecture guard: " + guard + ":", StringComparison.Ordinal));
            }

            Check(classified && seen.Count == 12,
                "architecture guard: 140-15P-1: Module 7 findings M7-01..M7-12 are each listed once with a title");
            Check(behaviorBacked,
                "architecture guard: 140-15P-2: every Module 7 finding except the verification meta-finding names declared xUnit behavior tests");
            Check(guardsPresent,
                "architecture guard: 140-15P-3: every architecture guard named by the Module 7 ledger exists in a Module 7 validator");

            var unlabeled = new List<string>();
            foreach (var pair in validators)
            {
                var headers = MethodHeader.Matches(pair.Value).Cast<Match>().Select(match => match.Index).ToList();
                headers.Add(pair.Value.Length);
                for (var index = 0; index + 1 < headers.Count; index++)
                {
                    var body = pair.Value.Substring(headers[index], headers[index + 1] - headers[index]);
                    if (SourceRead.IsMatch(body))
                        unlabeled.AddRange(UnlabeledCheck.Matches(body).Cast<Match>().Select(match => pair.Key + " " + match.Value));
                }
            }

            Check(unlabeled.Count == 0,
                "architecture guard: 140-15P-4: every Module 7 source-shape check is labeled as an architecture guard"
                + (unlabeled.Count == 0 ? "" : " (unlabeled: " + string.Join(", ", unlabeled) + ")"));

            var retired = Module7RetiredSourceChecks.All(entry =>
            {
                var parts = entry.Split('|');
                return !validatorText.Contains(parts[0] + ":", StringComparison.Ordinal)
                    && unitTests.Contains("void " + parts[1] + "(", StringComparison.Ordinal);
            });
            Check(retired,
                "architecture guard: 140-15P-5: retired Module 7 source checks are gone and their replacement xUnit behavior tests exist");
        }

        private static string[] SplitList(string value)
            => value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        private static void MediaFoundationCreateSampleReleasesPartialSamples()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            var method = Slice(source, "private IMFSample CreateSample", "private void DrainEncoderOutput");
            Check(method.Contains("var sampleReturned = false", StringComparison.Ordinal)
                  && method.Contains("sampleReturned = true", StringComparison.Ordinal)
                  && method.Contains("if (!sampleReturned)", StringComparison.Ordinal)
                  && method.Contains("ReleaseComObject(sample)", StringComparison.Ordinal),
                "architecture guard: 140-15A-1: Media Foundation CreateSample releases partial IMFSample objects on setup failure");
        }

        private static void FfmpegStdoutFlushDrainsPacketizerTail()
        {
            var h264 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var h265 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs");
            Check(FlushBlockDrainsTail(h264),
                "architecture guard: 140-15B-1: FFmpeg H.264 stdout reader drains every packetizer access unit after EOF flush");
            Check(FlushBlockDrainsTail(h265),
                "architecture guard: 140-15B-2: FFmpeg H.265 stdout reader drains every packetizer access unit after EOF flush");
        }

        private static bool FlushBlockDrainsTail(string source)
        {
            var flushIndex = source.IndexOf("_packetizer.FlushPendingEvents()", StringComparison.Ordinal);
            if (flushIndex < 0)
                flushIndex = source.IndexOf("_packetizer.Flush(out var finalUnit)", StringComparison.Ordinal);

            if (flushIndex < 0)
                return false;
            var drainIndex = source.IndexOf("DrainPacketizer()", flushIndex, StringComparison.Ordinal);
            return drainIndex > flushIndex;
        }

        private static void MediaFoundationStreamChangeIsBoundedAndRenegotiated()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            Check(source.Contains("MaxConsecutiveOutputStreamChanges", StringComparison.Ordinal)
                  && source.Contains("HandleOutputStreamChange", StringComparison.Ordinal)
                  && source.Contains("GetOutputAvailableType", StringComparison.Ordinal)
                  && source.Contains("SetOutputType", StringComparison.Ordinal),
                "architecture guard: 140-15D-1: Media Foundation output stream changes are capped and renegotiate output type");
        }

        private static void FfmpegPresetValidationRejectsUnexpectedPresetValues()
        {
            Check(!new FfmpegH264EncoderOptions { Preset = "ultrafast -vf scale=1:1" }.Validate(out var h264Error)
                  && h264Error.Contains("preset", StringComparison.Ordinal),
                "140-15E-1: FFmpeg H.264 preset validation rejects values outside the known preset set");
            Check(!new FfmpegH265EncoderOptions { Preset = "veryfast -vf scale=1:1" }.Validate(out var h265Error)
                  && h265Error.Contains("preset", StringComparison.Ordinal),
                "140-15E-2: FFmpeg H.265 preset validation rejects values outside the known preset set");
            Check(new FfmpegH264EncoderOptions { Preset = "slower" }.Validate(out _),
                "140-15E-3: FFmpeg H.264 preset validation accepts known presets");
            Check(new FfmpegH265EncoderOptions { Preset = "medium" }.Validate(out _),
                "140-15E-4: FFmpeg H.265 preset validation accepts known presets");
        }

        private static void FfmpegOutputCountersUseLockOnlyArithmetic()
        {
            Check(OutputQueueBlockUsesPlainCounter(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs")),
                "architecture guard: 140-15F-1: FFmpeg H.264 output queue counter uses plain arithmetic under _outputLock");
            Check(OutputQueueBlockUsesPlainCounter(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs")),
                "architecture guard: 140-15F-2: FFmpeg H.265 output queue counter uses plain arithmetic under _outputLock");
        }

        private static bool OutputQueueBlockUsesPlainCounter(string source)
        {
            var method = Slice(source, "private void EnqueueAccessUnit", "private int RemainingMilliseconds");
            var dropsOldOutput = method.Contains("while (_outputCount >= capacity", StringComparison.Ordinal)
                                 || method.Contains("while (_outputCount >= _maxOutputQueue", StringComparison.Ordinal);
            var rejectsNewOutput = method.Contains("if (_outputCount >= _maxOutputQueue)", StringComparison.Ordinal);
            return (dropsOldOutput || rejectsNewOutput)
                   && (rejectsNewOutput || method.Contains("_outputCount--", StringComparison.Ordinal))
                   && method.Contains("_outputCount++", StringComparison.Ordinal)
                   && !method.Contains("Interlocked.Decrement(ref _outputCount)", StringComparison.Ordinal)
                   && !method.Contains("Interlocked.Increment(ref _outputCount)", StringComparison.Ordinal)
                   && !method.Contains("Volatile.Read(ref _outputCount)", StringComparison.Ordinal)
                   && !method.Contains("Volatile.Write(ref _outputCount", StringComparison.Ordinal);
        }

        private static void VideoTimestampPairingUsesPtsBearingMpegTs()
        {
            var h264 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var h265 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs");
            var h264Options = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderOptions.cs");
            var h265Options = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderOptions.cs");
            Check(PtsBearingMpegTsPath(h264, h264Options),
                "architecture guard: 140-15G-1: FFmpeg H.264 uses PTS-bearing MPEG-TS and capture timestamp matching");
            Check(PtsBearingMpegTsPath(h265, h265Options),
                "architecture guard: 140-15G-2: FFmpeg H.265 uses PTS-bearing MPEG-TS and capture timestamp matching");
        }

        private static bool PtsBearingMpegTsPath(string sidecar, string options)
        {
            return sidecar.Contains("MpegTsVideoDemuxer", StringComparison.Ordinal)
                   && sidecar.Contains("FfmpegTimestampMatcher", StringComparison.Ordinal)
                   && sidecar.Contains("accessUnit.Pts90k", StringComparison.Ordinal)
                   && options.Contains("-fps_mode passthrough", StringComparison.Ordinal)
                   && options.Contains("-f mpegts", StringComparison.Ordinal)
                   && options.Contains("-mpegts_copyts 1", StringComparison.Ordinal);
        }

        private static void CameraVideoSubmitUsesFrameByteSourceContract()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var method = Slice(source, "public CameraVideoSubmitResult SubmitVideoFrame", "private static double ElapsedMs");
            Check(method.Contains("where TFrameBytes : struct, ICameraVideoFrameBytesSource", StringComparison.Ordinal)
                  && method.Contains("if (frameBytes.Length <= 0)", StringComparison.Ordinal)
                  && method.Contains("if (_rgbScratch == null || _rgbScratch.Length != frameBytes.Length)", StringComparison.Ordinal)
                  && method.Contains("frameBytes.CopyTo(_rgbScratch)", StringComparison.Ordinal)
                  && method.Contains("var ownedFrameBytes = _rgbScratch", StringComparison.Ordinal)
                  && !method.Contains("Func<byte[]>", StringComparison.Ordinal),
                "architecture guard: 140-15H-1: Camera video submit keeps the frame byte source contract and reuses the pipeline RGB scratch buffer");
        }

        private static void OpenH264I420ScratchIsReusedBeforeSidecarCopy()
        {
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var openH264 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs");
            Check(pipeline.Contains("private byte[] _i420Scratch", StringComparison.Ordinal)
                  && pipeline.Contains("EnsureI420Scratch(captureWidth, captureHeight)", StringComparison.Ordinal)
                  && pipeline.Contains("SupportsRgbFrameSource", StringComparison.Ordinal)
                  && pipeline.Contains("TrySubmitRgbFrame", StringComparison.Ordinal)
                  && !pipeline.Contains("new byte[captureWidth * captureHeight * 3 / 2]", StringComparison.Ordinal)
                  && openH264.Contains("frame.IsRgb24", StringComparison.Ordinal)
                  && openH264.Contains("Rgb24ToI420Converter.TryConvertRgb24ToI420", StringComparison.Ordinal)
                  && openH264.Contains("Buffer.BlockCopy(frame, 0, copy, 0, frame.Length)", StringComparison.Ordinal),
                "architecture guard: 140-15I-1: OpenH264 uses worker-side RGB conversion while retaining the bounded I420 fallback and defensive queue copy");
        }

        private static void FfmpegStdoutReadersAppendReadBufferRanges()
        {
            Check(FfmpegReaderAppendsRanges(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs")),
                "architecture guard: 140-15J-1: FFmpeg H.264 stdout reader appends read buffer ranges without chunk arrays");
            Check(FfmpegReaderAppendsRanges(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs")),
                "architecture guard: 140-15J-2: FFmpeg H.265 stdout reader appends read buffer ranges without chunk arrays");
        }

        private static bool FfmpegReaderAppendsRanges(string source)
        {
            var method = Slice(source, "private async Task RunStdoutReader", "private async Task RunStderrReader");
            return method.Contains("_packetizer.Append(buffer, 0, read)", StringComparison.Ordinal)
                   && !method.Contains("new byte[read]", StringComparison.Ordinal)
                   && !method.Contains("Buffer.BlockCopy(buffer, 0, chunk, 0, read)", StringComparison.Ordinal);
        }

        private static void PacketizersCopyRangesWithoutManualByteLoop()
        {
            Check(PacketizerUsesRangeAppendAndCopyTo(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H264AnnexBAccessUnitPacketizer.cs")),
                "architecture guard: 140-15K-1: H.264 packetizer supports range append and uses List.CopyTo for buffer ranges");
            Check(PacketizerUsesRangeAppendAndCopyTo(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H265AnnexBAccessUnitPacketizer.cs")),
                "architecture guard: 140-15K-2: H.265 packetizer supports range append and uses List.CopyTo for buffer ranges");
        }

        private static bool PacketizerUsesRangeAppendAndCopyTo(string source)
        {
            var copyMethod = Slice(source, "private byte[] CopyBufferRange", "private void CompactBufferIfNeeded");
            return source.Contains("public void Append(byte[] data, int offset, int count)", StringComparison.Ordinal)
                   && copyMethod.Contains("_buffer.CopyTo(offset, copy, 0, copy.Length)", StringComparison.Ordinal)
                   && !copyMethod.Contains("copy[i] = _buffer[offset + i]", StringComparison.Ordinal);
        }

        private static void H264NormalizerAvoidsLinqHotPathPasses()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H264AccessUnitNormalizer.cs");
            var normalize = Slice(source, "public bool TryNormalizeSample", "private void CacheParameterSets");
            var build = Slice(source, "private static byte[] BuildAnnexB", "private static byte NalType");
            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal)
                  && !normalize.Contains(".Any(", StringComparison.Ordinal)
                  && !build.Contains(".Sum(", StringComparison.Ordinal)
                  && normalize.Contains("foreach (var nal in nals)", StringComparison.Ordinal)
                  && build.Contains("foreach (var nal in nals)", StringComparison.Ordinal),
                "architecture guard: 140-15L-1: H.264 normalizer computes hot-path flags and output length without LINQ passes");
        }

        private static void OpenH264StdoutReaderReusesLengthHeader()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs");
            var reader = Slice(source, "private async Task RunStdoutReader", "private async Task RunStderrReader");
            var lengthReader = Slice(source, "private static async Task<LengthReadResult> ReadLittleEndianLength", "private static async Task<bool> ReadExact");
            Check(reader.Contains("var header = new byte[4]", StringComparison.Ordinal)
                  && reader.Contains("ReadLittleEndianLength(stream, header, token)", StringComparison.Ordinal)
                  && !lengthReader.Contains("new byte[4]", StringComparison.Ordinal),
                "architecture guard: 140-15M-1: OpenH264 stdout reader reuses one length header buffer");
        }

        private static void SidecarsCacheQueueCapacitiesOnStart()
        {
            Check(SidecarCachesQueueCapacities(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs")),
                "architecture guard: 140-15N-1: FFmpeg H.264 sidecar caches queue capacities after option validation");
            Check(SidecarCachesQueueCapacities(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs")),
                "architecture guard: 140-15N-2: FFmpeg H.265 sidecar caches queue capacities after option validation");
            Check(SidecarCachesQueueCapacities(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs")),
                "architecture guard: 140-15N-3: OpenH264 sidecar caches queue capacities after option validation");
        }

        private static void FrameSourceSubmissionUsesGenericStructPath()
        {
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var h264 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var h265 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs");
            var mediaFoundation = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            var openH264 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs");
            Check(pipeline.Contains("TrySubmitFrame<TFrameBytes>", StringComparison.Ordinal)
                  && pipeline.Contains("TrySubmitRgbFrame<TFrameBytes>", StringComparison.Ordinal)
                  && h264.Contains("private bool TryEnqueueFrame<TFrameBytes>", StringComparison.Ordinal)
                  && h265.Contains("private bool TryEnqueueFrame<TFrameBytes>", StringComparison.Ordinal)
                  && mediaFoundation.Contains("private bool TrySubmitFrameCore<TFrameBytes>", StringComparison.Ordinal)
                  && openH264.Contains("ICameraVideoRgbFrameSourceSidecar", StringComparison.Ordinal)
                  && openH264.Contains("TrySubmitRgbFrame<TFrameBytes>", StringComparison.Ordinal)
                  && !h264.Contains("Action<byte[]> copyFrame", StringComparison.Ordinal)
                  && !h265.Contains("Action<byte[]> copyFrame", StringComparison.Ordinal)
                  && !mediaFoundation.Contains("Action<byte[]> copyFrame", StringComparison.Ordinal),
                "architecture guard: 140-15N-4: direct frame-source submission uses generic structs without copy delegates");
        }

        private static void MediaFoundationWorkerOwnsNativeCleanup()
        {
            var session = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");
            var sidecar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            Check(session.Contains("ICameraVideoSidecarDeferredCleanup", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarRetirementRegistry.Retire(deferred)", StringComparison.Ordinal)
                  && session.Contains("CameraVideoSidecarRetirementRegistry.Poll()", StringComparison.Ordinal)
                  && sidecar.Contains("EncoderWorkerMain", StringComparison.Ordinal)
                  && sidecar.Contains("ReleaseEncoderResources", StringComparison.Ordinal)
                  && sidecar.Contains("_workerInitialized.Wait(StartupTimeoutMs)", StringComparison.Ordinal)
                  && sidecar.Contains("worker != null && worker.IsAlive", StringComparison.Ordinal),
                "architecture guard: 140-15Q-1: Media Foundation worker owns native initialization and cleanup after bounded stop");
        }

        private static bool SidecarCachesQueueCapacities(string source)
        {
            return source.Contains("private int _maxInputQueue = 2", StringComparison.Ordinal)
                   && source.Contains("private int _maxOutputQueue = 4", StringComparison.Ordinal)
                   && source.Contains("_maxInputQueue = Math.Max(1, _options.MaxInputQueue)", StringComparison.Ordinal)
                   && source.Contains("_maxOutputQueue = Math.Max(1, _options.MaxOutputQueue)", StringComparison.Ordinal)
                   && source.Contains("while (_inputCount >= _maxInputQueue", StringComparison.Ordinal)
                   && source.Contains("_maxOutputQueue", StringComparison.Ordinal);
        }

        private static string Read(string path)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(path);

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] Missing start token: " + startToken);

            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;

            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
        }
    }
}
