// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Options for the experimental Windows Media Foundation H.264 encoder.

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Options for the experimental Windows native H.264 encoder path.
    /// </summary>
    public sealed class MediaFoundationH264EncoderOptions
    {
        public int Width = 640;
        public int Height = 480;
        public int FrameRate = 30;
        public int BitrateKbps = 4000;
        public int KeyframeInterval = 30;
        public int MaxInputQueue = 1;
        public int MaxOutputQueue = 4;

        public int Rgb24FrameByteCount => Positive(Width, 640) * Positive(Height, 480) * 3;
        public int Nv12FrameByteCount => Positive(Width, 640) * Positive(Height, 480) * 3 / 2;
        public bool HasManagedConversionCostWarning => Positive(Width, 640) * Positive(Height, 480) > 1280 * 720;

        /// <summary>Validates settings required by NV12 and Media Foundation.</summary>
        public bool Validate(out string error)
        {
            error = null;
            if (Width <= 0 || Height <= 0)
            {
                error = "Media Foundation H.264 width and height must be positive.";
                return false;
            }

            if ((Width & 1) != 0 || (Height & 1) != 0)
            {
                error = "Media Foundation H.264 width and height must be even for NV12 input.";
                return false;
            }

            if (FrameRate <= 0)
            {
                error = "Media Foundation H.264 frame rate must be positive.";
                return false;
            }

            if (BitrateKbps <= 0)
            {
                error = "Media Foundation H.264 bitrate must be positive.";
                return false;
            }

            if (KeyframeInterval <= 0)
            {
                error = "Media Foundation H.264 keyframe interval must be positive.";
                return false;
            }

            if (MaxInputQueue <= 0 || MaxOutputQueue <= 0)
            {
                error = "Media Foundation H.264 queue sizes must be positive.";
                return false;
            }

            return true;
        }

        private static int Positive(int value, int fallback)
            => value > 0 ? value : fallback;
    }
}
