// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Resolves camera video sidecar dimensions, frame rate, and config values.

using System;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Creates normalized video sidecar configs from camera publisher settings.
    /// </summary>
    internal static class CameraVideoSidecarConfigFactory
    {
        private const int DefaultFrameRate = 30;
        private const float MaxResolvedFrameRateExclusive = 1000f;

        public static CameraVideoSidecarConfig Create(
            string ffmpegPath,
            string openH264HelperPath,
            string openH264DllPath,
            int width,
            int height,
            float effectivePublishRateHz,
            int bitrateKbps,
            int keyframeInterval,
            int maxPendingReadbacks,
            int openH264MaxInputQueue,
            int maxOutputQueue)
            => new CameraVideoSidecarConfig(
                ffmpegPath,
                openH264HelperPath,
                openH264DllPath,
                ResolveDimension(width),
                ResolveDimension(height),
                ResolveFrameRate(effectivePublishRateHz),
                bitrateKbps,
                keyframeInterval,
                maxPendingReadbacks,
                openH264MaxInputQueue,
                maxOutputQueue);

        public static int ResolveDimension(int requestedPixels)
            => Math.Max(1, requestedPixels);

        public static int ResolveFrameRate(float effectivePublishRateHz)
        {
            if (effectivePublishRateHz > 0f && effectivePublishRateHz < MaxResolvedFrameRateExclusive)
                return Math.Max(1, (int)Math.Round(effectivePublishRateHz, MidpointRounding.AwayFromZero));

            return DefaultFrameRate;
        }
    }
}
