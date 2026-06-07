// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-15 regression coverage for video encoding sidecar review fixes.

using System;
using System.IO;
using Foxglove.Schemas.Video;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_15Validation.
    /// </summary>
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
            MediaFoundationTimestampMapUsesOldestEntryEviction();
            MediaFoundationStreamChangeIsBoundedAndRenegotiated();
            FfmpegPresetValidationRejectsUnexpectedPresetValues();
            FfmpegOutputCountersUseLockOnlyArithmetic();
            VideoTimestampPairingRiskIsDocumentedAtTheFfmpegBoundary();
            CameraVideoSubmitFrameFactoryNullCheckRemainsAtEntry();

            Console.WriteLine($"Phase 140-15: {_passed} checks passed.");
        }

        private static void MediaFoundationCreateSampleReleasesPartialSamples()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            var method = Slice(source, "private IMFSample CreateSample", "private void DrainEncoderOutput");
            Check(method.Contains("var sampleReturned = false", StringComparison.Ordinal)
                  && method.Contains("sampleReturned = true", StringComparison.Ordinal)
                  && method.Contains("if (!sampleReturned)", StringComparison.Ordinal)
                  && method.Contains("ReleaseComObject(sample)", StringComparison.Ordinal),
                "140-15A-1: Media Foundation CreateSample releases partial IMFSample objects on setup failure");
        }

        private static void FfmpegStdoutFlushDrainsPacketizerTail()
        {
            var h264 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var h265 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs");
            Check(FlushBlockDrainsTail(h264),
                "140-15B-1: FFmpeg H.264 stdout reader drains every packetizer access unit after EOF flush");
            Check(FlushBlockDrainsTail(h265),
                "140-15B-2: FFmpeg H.265 stdout reader drains every packetizer access unit after EOF flush");
        }

        private static bool FlushBlockDrainsTail(string source)
        {
            var flushIndex = source.IndexOf("_packetizer.Flush(out var finalUnit)", StringComparison.Ordinal);
            if (flushIndex < 0)
                return false;

            var drainIndex = source.IndexOf("DrainPacketizer()", flushIndex, StringComparison.Ordinal);
            return drainIndex > flushIndex;
        }

        private static void MediaFoundationTimestampMapUsesOldestEntryEviction()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            var method = Slice(source, "private void RegisterSampleTimestamp", "private ulong ResolveOutputTimestamp");
            Check(method.Contains("EvictOldestSampleTimestamp()", StringComparison.Ordinal)
                  && method.Contains("_sampleTimestampOrder.Enqueue(sampleTime)", StringComparison.Ordinal)
                  && !method.Contains("_sampleTimestampNsByTime.Clear()", StringComparison.Ordinal),
                "140-15C-1: Media Foundation timestamp tracking evicts one oldest entry instead of clearing all in-flight samples");
        }

        private static void MediaFoundationStreamChangeIsBoundedAndRenegotiated()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            Check(source.Contains("MaxConsecutiveOutputStreamChanges", StringComparison.Ordinal)
                  && source.Contains("HandleOutputStreamChange", StringComparison.Ordinal)
                  && source.Contains("GetOutputAvailableType", StringComparison.Ordinal)
                  && source.Contains("SetOutputType", StringComparison.Ordinal),
                "140-15D-1: Media Foundation output stream changes are capped and renegotiate output type");
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
                "140-15F-1: FFmpeg H.264 output queue counter uses plain arithmetic under _outputLock");
            Check(OutputQueueBlockUsesPlainCounter(
                    Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs")),
                "140-15F-2: FFmpeg H.265 output queue counter uses plain arithmetic under _outputLock");
        }

        private static bool OutputQueueBlockUsesPlainCounter(string source)
        {
            var method = Slice(source, "private void EnqueueAccessUnit", "private int RemainingMilliseconds");
            return method.Contains("while (_outputCount >= capacity", StringComparison.Ordinal)
                   && method.Contains("_outputCount--", StringComparison.Ordinal)
                   && method.Contains("_outputCount++", StringComparison.Ordinal)
                   && !method.Contains("Interlocked.Decrement(ref _outputCount)", StringComparison.Ordinal)
                   && !method.Contains("Interlocked.Increment(ref _outputCount)", StringComparison.Ordinal)
                   && !method.Contains("Volatile.Read(ref _outputCount)", StringComparison.Ordinal)
                   && !method.Contains("Volatile.Write(ref _outputCount", StringComparison.Ordinal);
        }

        private static void VideoTimestampPairingRiskIsDocumentedAtTheFfmpegBoundary()
        {
            var h264 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs");
            var h265 = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs");
            Check(h264.Contains("rawvideo pipe carries no per-frame PTS", StringComparison.Ordinal),
                "140-15G-1: FFmpeg H.264 timestamp pairing documents the rawvideo PTS limitation");
            Check(h265.Contains("rawvideo pipe carries no per-frame PTS", StringComparison.Ordinal),
                "140-15G-2: FFmpeg H.265 timestamp pairing documents the rawvideo PTS limitation");
        }

        private static void CameraVideoSubmitFrameFactoryNullCheckRemainsAtEntry()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var method = Slice(source, "public CameraVideoSubmitResult SubmitVideoFrame", "private static double ElapsedMs");
            var nullCheck = method.IndexOf("if (frameBytesFactory == null)", StringComparison.Ordinal);
            var frameLengthCheck = method.IndexOf("if (frameByteLength <= 0)", StringComparison.Ordinal);
            Check(nullCheck >= 0 && nullCheck < frameLengthCheck,
                "140-15H-1: Camera video frame factory null check remains at method entry");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

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
