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

        private static void ContainsAll(string path, string label, params string[] tokens)
        {
            var source = File.ReadAllText(path);
            foreach (var token in tokens)
                if (!source.Contains(token))
                    throw new Exception("[FAIL] " + label + " missing token: " + token);

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
