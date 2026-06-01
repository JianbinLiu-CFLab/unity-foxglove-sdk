// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138K validation for video camera dimension and diagnostics rules.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Phase 138K checks for camera H.264/H.265 video path hardening.
    /// </summary>
    public static class Phase138KValidation
    {
        private static int _passed;

        /// <summary>
        /// Runs all validation checks for phase 138K.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138K: Camera Video Encoder Pipeline ===");
            _passed = 0;

            CameraPublisherCarriesCaptureDimensionsIntoVideoSubmission();
            CameraPublisherUsesCapturedDimensionsForOpenH264Conversion();
            CameraPublisherKeepsVideoFailureOutOfJpegFallback();
            CameraPublisherSurfacesVideoDiagnostics();
            VideoSidecarsCarryCaptureTimestamps();

            Console.WriteLine($"Phase 138K: {_passed} checks passed.");
        }

        private static void CameraPublisherCarriesCaptureDimensionsIntoVideoSubmission()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Check(source.Contains("SubmitVideoFrame(req, renderUnixNs, captureWidth, captureHeight)", StringComparison.Ordinal),
                "138K-1A: video readback path carries captured dimensions into submission");
            Check(source.Contains("private void SubmitVideoFrame(AsyncGPUReadbackRequest req, ulong renderUnixNs, int captureWidth, int captureHeight)", StringComparison.Ordinal),
                "138K-1B: video submission accepts captured dimensions");
        }

        private static void CameraPublisherUsesCapturedDimensionsForOpenH264Conversion()
        {
            var method = ExtractMethod("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs", "private void SubmitVideoFrame");

            Check(method.Contains("Math.Max(1, captureWidth)", StringComparison.Ordinal)
                  && method.Contains("Math.Max(1, captureHeight)", StringComparison.Ordinal),
                "138K-2A: video conversion dimensions come from captured readback size");
            Check(!method.Contains("Math.Max(1, _width)", StringComparison.Ordinal)
                  && !method.Contains("Math.Max(1, _height)", StringComparison.Ordinal),
                "138K-2B: video submission does not use mutable Inspector dimensions after readback");
        }

        private static void CameraPublisherKeepsVideoFailureOutOfJpegFallback()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var videoBranchStart = source.IndexOf("if (profile.IsVideo)", StringComparison.Ordinal);
            var videoBranchEnd = source.IndexOf("var publishWebSocket = ShouldPreparePublishPayload", videoBranchStart, StringComparison.Ordinal);
            var videoBranch = videoBranchStart >= 0 && videoBranchEnd > videoBranchStart
                ? source.Substring(videoBranchStart, videoBranchEnd - videoBranchStart)
                : "";

            Check(videoBranch.Contains("return;", StringComparison.Ordinal),
                "138K-3A: video branch returns before JPEG publish preparation");
            Check(!videoBranch.Contains("QueueJpegFrame", StringComparison.Ordinal)
                  && !videoBranch.Contains("PublishJpegFrame", StringComparison.Ordinal),
                "138K-3B: video failure does not invoke JPEG fallback in the same tick");
        }

        private static void CameraPublisherSurfacesVideoDiagnostics()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Check(source.Contains("[Foxglove][VideoDiag]", StringComparison.Ordinal),
                "138K-4A: video path exposes VideoDiag logs");
            Check(source.Contains("dimensionMismatch", StringComparison.Ordinal)
                  && source.Contains("videoSubmitMs", StringComparison.Ordinal)
                  && source.Contains("videoDrainMs", StringComparison.Ordinal),
                "138K-4B: VideoDiag reports dimension, submit, and drain evidence");
        }

        private static void VideoSidecarsCarryCaptureTimestamps()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs"
            })
            {
                var source = Read(path);
                Check(source.Contains("ITimestampedCameraVideoEncoderSidecar", StringComparison.Ordinal),
                    "138K-5A: sidecar carries capture timestamps: " + Path.GetFileName(path));
            }
        }

        private static string ExtractMethod(string path, string signatureStart)
        {
            var source = Read(path);
            var start = source.IndexOf(signatureStart, StringComparison.Ordinal);
            if (start < 0)
                return "";

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return "";

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return source.Substring(start);
        }

        private static string Read(string path) => File.ReadAllText(path);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
