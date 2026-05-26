// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-13 regression coverage for video encoder sidecar frame geometry.

using System;
using System.IO;
using Foxglove.Schemas.Video;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_13Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-13: Video Encoding Sidecars ===");
            _passed = 0;

            NormalFrameByteCountsRemainStable();
            HugeDimensionsFailClosed();
            SidecarStartRejectsInvalidFfmpegDimensionsBeforeProcessLaunch();
            SidecarSourcesRejectInvalidExpectedByteCounts();
            FfmpegPresetValidationRejectsControlCharacters();
            SidecarSourcesGuardQueueCountersAndDiagnostics();
            FfmpegExecutableCheckDrainsPipesBeforeWaitingForExit();
            MediaFoundationOutputQueueUsesAtomicLockAndNv12Fallback();
            PacketizersAvoidPerNalListGetRangeCopies();
            H264NormalizerDoesNotPreallocateSkippedEmptyNals();
            VideoSidecarContractsDocumentRawInputFormats();

            Console.WriteLine($"Phase 134-13: {_passed} checks passed.");
        }

        private static void NormalFrameByteCountsRemainStable()
        {
            Check(new FfmpegH264EncoderOptions { Width = 640, Height = 480 }.FrameByteCount == 921600,
                "134-13A-1: FFmpeg H.264 RGB24 normal frame byte count is unchanged");
            Check(new FfmpegH265EncoderOptions { Width = 640, Height = 480 }.FrameByteCount == 921600,
                "134-13A-2: FFmpeg H.265 RGB24 normal frame byte count is unchanged");
            Check(new OpenH264EncoderOptions { Width = 640, Height = 480 }.FrameByteCount == 460800,
                "134-13A-3: OpenH264 I420 normal frame byte count is unchanged");

            var mf = new MediaFoundationH264EncoderOptions { Width = 640, Height = 480 };
            Check(mf.Rgb24FrameByteCount == 921600,
                "134-13A-4: Media Foundation RGB24 normal frame byte count is unchanged");
            Check(mf.Nv12FrameByteCount == 460800,
                "134-13A-5: Media Foundation NV12 normal frame byte count is unchanged");
        }

        private static void HugeDimensionsFailClosed()
        {
            var h264 = new FfmpegH264EncoderOptions { Width = int.MaxValue, Height = int.MaxValue };
            Check(h264.FrameByteCount == 0,
                "134-13B-1: FFmpeg H.264 huge dimensions do not overflow into a usable byte count");
            Check(!h264.Validate(out var h264Error) && h264Error.Contains(CameraVideoFrameGeometry.MaxDimension.ToString()),
                "134-13B-2: FFmpeg H.264 huge dimensions fail validation");

            var h265 = new FfmpegH265EncoderOptions { Width = int.MaxValue, Height = int.MaxValue };
            Check(h265.FrameByteCount == 0,
                "134-13B-3: FFmpeg H.265 huge dimensions do not overflow into a usable byte count");
            Check(!h265.Validate(out var h265Error) && h265Error.Contains(CameraVideoFrameGeometry.MaxDimension.ToString()),
                "134-13B-4: FFmpeg H.265 huge dimensions fail validation");

            var mf = new MediaFoundationH264EncoderOptions { Width = 50000, Height = 50000 };
            Check(mf.Rgb24FrameByteCount == 0 && mf.Nv12FrameByteCount == 0,
                "134-13B-5: Media Foundation huge dimensions do not overflow into usable byte counts");
            Check(!mf.Validate(out var mfError) && mfError.Contains(CameraVideoFrameGeometry.MaxDimension.ToString()),
                "134-13B-6: Media Foundation huge dimensions fail validation");

            ValidateOpenH264HugeDimensions();
        }

        private static void ValidateOpenH264HugeDimensions()
        {
            var helperPath = Path.Combine(Path.GetTempPath(), "u2f_phase134_13_" + Guid.NewGuid().ToString("N") + ".exe");
            var dllPath = Path.Combine(Path.GetTempPath(), "u2f_phase134_13_" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                File.WriteAllBytes(helperPath, Array.Empty<byte>());
                File.WriteAllBytes(dllPath, Array.Empty<byte>());
                var openH264 = new OpenH264EncoderOptions
                {
                    HelperExecutablePath = helperPath,
                    OpenH264DllPath = dllPath,
                    Width = 50000,
                    Height = 50000
                };

                Check(openH264.FrameByteCount == 0,
                    "134-13B-7: OpenH264 huge dimensions do not overflow into a usable byte count");
                Check(!openH264.Validate(out var error) && error.Contains(CameraVideoFrameGeometry.MaxDimension.ToString()),
                    "134-13B-8: OpenH264 huge dimensions fail validation after path checks");
            }
            finally
            {
                TryDelete(helperPath);
                TryDelete(dllPath);
            }
        }

        private static void SidecarStartRejectsInvalidFfmpegDimensionsBeforeProcessLaunch()
        {
            using (var h264 = new FfmpegH264EncoderSidecar())
            {
                Check(!h264.Start(new FfmpegH264EncoderOptions
                    {
                        FfmpegPath = "definitely-missing-ffmpeg.exe",
                        Width = 50000,
                        Height = 50000
                    }) && h264.LastError.Contains(CameraVideoFrameGeometry.MaxDimension.ToString()),
                    "134-13C-1: FFmpeg H.264 sidecar rejects impossible dimensions before process launch");
            }

            using (var h265 = new FfmpegH265EncoderSidecar())
            {
                Check(!h265.Start(new FfmpegH265EncoderOptions
                    {
                        FfmpegPath = "definitely-missing-ffmpeg.exe",
                        Width = 50000,
                        Height = 50000
                    }) && h265.LastError.Contains(CameraVideoFrameGeometry.MaxDimension.ToString()),
                    "134-13C-2: FFmpeg H.265 sidecar rejects impossible dimensions before process launch");
            }
        }

        private static void SidecarSourcesRejectInvalidExpectedByteCounts()
        {
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "134-13D-1: FFmpeg H.264 sidecar fail-closes invalid expected RGB24 byte counts",
                "expectedBytes <= 0",
                "FFmpeg H.264 encoder dimensions produce an invalid RGB24 frame size");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "134-13D-2: FFmpeg H.265 sidecar fail-closes invalid expected RGB24 byte counts",
                "expectedBytes <= 0",
                "FFmpeg H.265 encoder dimensions produce an invalid RGB24 frame size");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                "134-13D-3: OpenH264 sidecar fail-closes invalid expected I420 byte counts",
                "expectedBytes <= 0",
                "OpenH264 encoder dimensions produce an invalid I420 frame size");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs",
                "134-13D-4: Media Foundation sidecar fail-closes invalid expected RGB24 byte counts",
                "expectedBytes <= 0",
                "Media Foundation encoder dimensions produce an invalid RGB24 frame size");
        }

        private static void FfmpegPresetValidationRejectsControlCharacters()
        {
            var h264 = new FfmpegH264EncoderOptions { Preset = "ultra\nfast" };
            Check(!h264.Validate(out var h264Error) && h264Error.Contains("control characters"),
                "134-13E-1: FFmpeg H.264 preset validation rejects control characters");

            var h265 = new FfmpegH265EncoderOptions { Preset = "ultra\u007ffast" };
            Check(!h265.Validate(out var h265Error) && h265Error.Contains("control characters"),
                "134-13E-2: FFmpeg H.265 preset validation rejects DEL/control characters");

            Check(new FfmpegH264EncoderOptions { Preset = "ultrafast" }.Validate(out _),
                "134-13E-3: FFmpeg H.264 preset validation accepts normal presets");
            Check(new FfmpegH265EncoderOptions { Preset = "veryfast" }.Validate(out _),
                "134-13E-4: FFmpeg H.265 preset validation accepts normal presets");
        }

        private static void SidecarSourcesGuardQueueCountersAndDiagnostics()
        {
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "134-13F-1: FFmpeg H.264 input queue count is guarded by one lock",
                "private readonly object _inputLock",
                "lock (_inputLock)",
                "TryDequeueInputFrame",
                "_inputCount--",
                "_inputCount++");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "134-13F-2: FFmpeg H.265 input queue count is guarded by one lock",
                "private readonly object _inputLock",
                "lock (_inputLock)",
                "TryDequeueInputFrame",
                "_inputCount--",
                "_inputCount++");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                "134-13F-3: OpenH264 input queue count is guarded by one lock",
                "private readonly object _inputLock",
                "lock (_inputLock)",
                "TryDequeueInputFrame",
                "_inputCount--",
                "_inputCount++");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "134-13F-4: FFmpeg H.264 cross-thread diagnostics use volatile backing fields",
                "Volatile.Read(ref _lastStderrLine)",
                "Volatile.Write(ref _lastStderrLine",
                "Volatile.Read(ref _lastError)",
                "Volatile.Write(ref _lastError");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "134-13F-5: FFmpeg H.265 cross-thread diagnostics use volatile backing fields",
                "Volatile.Read(ref _lastStderrLine)",
                "Volatile.Write(ref _lastStderrLine",
                "Volatile.Read(ref _lastError)",
                "Volatile.Write(ref _lastError");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                "134-13F-6: OpenH264 cross-thread diagnostics use volatile backing fields",
                "Volatile.Read(ref _lastDiagnosticLine)",
                "Volatile.Write(ref _lastDiagnosticLine",
                "Volatile.Read(ref _lastError)",
                "Volatile.Write(ref _lastError");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "134-13F-7: FFmpeg H.264 shutdown waits use a single deadline and task diagnostics",
                "ShutdownTimeoutMs",
                "RemainingMilliseconds",
                "shutdown timed out waiting for the");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "134-13F-8: FFmpeg H.265 shutdown waits use a single deadline and task diagnostics",
                "ShutdownTimeoutMs",
                "RemainingMilliseconds",
                "shutdown timed out waiting for the");
        }

        private static void FfmpegExecutableCheckDrainsPipesBeforeWaitingForExit()
        {
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegExecutableCheck.cs",
                "134-13G-1: FFmpeg executable check starts async pipe reads before WaitForExit",
                "ReadToEndAsync",
                "WaitForReaderTasks",
                "CompletedText");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegExecutableCheck.cs",
                "134-13G-2: FFmpeg PATH resolver reads one canonical PATH value per Windows target",
                "FirstNonEmpty(",
                "EnvironmentVariableTarget.User",
                "EnvironmentVariableTarget.Machine",
                "yield break");
        }

        private static void MediaFoundationOutputQueueUsesAtomicLockAndNv12Fallback()
        {
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs",
                "134-13H-1: Media Foundation output queue count is guarded by one lock",
                "private readonly object _outputLock",
                "lock (_outputLock)",
                "_outputCount--",
                "_outputCount++");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs",
                "134-13H-2: Media Foundation fallback output buffer uses NV12 frame size",
                "Math.Max(info.cbSize, Math.Max(1, _options.Nv12FrameByteCount))");
        }

        private static void PacketizersAvoidPerNalListGetRangeCopies()
        {
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H264AnnexBAccessUnitPacketizer.cs",
                "134-13I-1: H.264 packetizer uses a read offset instead of per-NAL GetRange copies",
                "private int _bufferStart",
                "CopyBufferRange",
                "CompactBufferIfNeeded");
            DoesNotContain(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H264AnnexBAccessUnitPacketizer.cs",
                "134-13I-2: H.264 packetizer no longer allocates List.GetRange per NAL",
                ".GetRange(");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H265AnnexBAccessUnitPacketizer.cs",
                "134-13I-3: H.265 packetizer uses a read offset instead of per-NAL GetRange copies",
                "private int _bufferStart",
                "CopyBufferRange",
                "CompactBufferIfNeeded");
            DoesNotContain(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H265AnnexBAccessUnitPacketizer.cs",
                "134-13I-4: H.265 packetizer no longer allocates List.GetRange per NAL",
                ".GetRange(");
        }

        private static void H264NormalizerDoesNotPreallocateSkippedEmptyNals()
        {
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H264AccessUnitNormalizer.cs",
                "134-13J-1: H.264 normalizer only counts non-empty NALs when preallocating Annex B output",
                "nal != null && nal.Length > 0 ? 4 + nal.Length : 0");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/H264AccessUnitNormalizer.cs",
                "134-13J-2: H.264 normalizer documents single SPS/PPS cache boundary",
                "caches the latest SPS and PPS only");
        }

        private static void VideoSidecarContractsDocumentRawInputFormats()
        {
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/ICameraVideoEncoderSidecar.cs",
                "134-13K-1: sidecar interface documents implementation-specific input pixel format",
                "input pixel format is implementation-specific",
                "RGB24 for FFmpeg",
                "I420 for the OpenH264 helper");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderOptions.cs",
                "134-13K-2: OpenH264 frame byte count is documented as I420",
                "I420/YUV420 byte count");
            ContainsAll(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderOptions.cs",
                "134-13K-3: Media Foundation frame byte counts distinguish RGB24 input and NV12 conversion",
                "RGB24 byte count",
                "NV12 byte count");
        }

        private static void ContainsAll(string path, string label, params string[] tokens)
        {
            var source = File.ReadAllText(path);
            foreach (var token in tokens)
                if (!source.Contains(token))
                    throw new Exception("[FAIL] " + label + " missing token: " + token);

            Check(true, label);
        }

        private static void DoesNotContain(string path, string label, string token)
        {
            var source = File.ReadAllText(path);
            if (source.Contains(token))
                throw new Exception("[FAIL] " + label + " unexpected token: " + token);

            Check(true, label);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
