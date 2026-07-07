// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video

using System;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Creates video sidecar option objects from publisher configuration values.
    /// </summary>
    internal static class CameraVideoSidecarOptionsFactory
    {
        public static FfmpegH264EncoderOptions CreateH264Options(
            string ffmpegPath,
            int width,
            int height,
            int frameRate,
            int bitrateKbps,
            int keyframeInterval,
            int maxInputQueue,
            int maxOutputQueue)
            => new FfmpegH264EncoderOptions
            {
                FfmpegPath = ffmpegPath ?? "",
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                FrameRate = Math.Max(1, frameRate),
                BitrateKbps = Math.Max(1, bitrateKbps),
                KeyframeInterval = Math.Max(1, keyframeInterval),
                MaxInputQueue = Math.Max(1, maxInputQueue),
                MaxOutputQueue = Math.Max(1, maxOutputQueue)
            };

        public static FfmpegH265EncoderOptions CreateH265Options(
            string ffmpegPath,
            int width,
            int height,
            int frameRate,
            int bitrateKbps,
            int keyframeInterval,
            int maxInputQueue,
            int maxOutputQueue)
            => new FfmpegH265EncoderOptions
            {
                FfmpegPath = ffmpegPath ?? "",
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                FrameRate = Math.Max(1, frameRate),
                BitrateKbps = Math.Max(1, bitrateKbps),
                KeyframeInterval = Math.Max(1, keyframeInterval),
                MaxInputQueue = Math.Max(1, maxInputQueue),
                MaxOutputQueue = Math.Max(1, maxOutputQueue)
            };

        public static OpenH264EncoderOptions CreateOpenH264Options(
            string helperExecutablePath,
            string openH264DllPath,
            int width,
            int height,
            int frameRate,
            int bitrateKbps,
            int keyframeInterval,
            int maxInputQueue,
            int maxOutputQueue)
            => new OpenH264EncoderOptions
            {
                HelperExecutablePath = helperExecutablePath,
                OpenH264DllPath = openH264DllPath,
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                FrameRate = Math.Max(1, frameRate),
                BitrateKbps = Math.Max(1, bitrateKbps),
                KeyframeInterval = Math.Max(1, keyframeInterval),
                MaxInputQueue = Math.Max(1, maxInputQueue),
                MaxOutputQueue = Math.Max(1, maxOutputQueue)
            };

        public static MediaFoundationH264EncoderOptions CreateMediaFoundationH264Options(
            int width,
            int height,
            int frameRate,
            int bitrateKbps,
            int keyframeInterval,
            int maxInputQueue,
            int maxOutputQueue)
            => new MediaFoundationH264EncoderOptions
            {
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                FrameRate = Math.Max(1, frameRate),
                BitrateKbps = Math.Max(1, bitrateKbps),
                KeyframeInterval = Math.Max(1, keyframeInterval),
                MaxInputQueue = Math.Max(1, maxInputQueue),
                MaxOutputQueue = Math.Max(1, maxOutputQueue)
            };
    }
}
