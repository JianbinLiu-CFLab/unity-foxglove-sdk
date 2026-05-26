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
        public const int MaxBitrateKbps = 1_000_000;

        public int Width = 640;
        public int Height = 480;
        public int FrameRate = 30;
        public int BitrateKbps = 4000;
        public int KeyframeInterval = 30;
        public int MaxInputQueue = 1;
        public int MaxOutputQueue = 4;

        /// <summary>Returns the expected RGB24 byte count for one raw sidecar input frame.</summary>
        public int Rgb24FrameByteCount
            => CameraVideoFrameGeometry.GetRgb24FrameByteCountOrZero(Positive(Width, 640), Positive(Height, 480));
        /// <summary>Returns the expected NV12 byte count after the managed RGB24 conversion step.</summary>
        public int Nv12FrameByteCount
            => CameraVideoFrameGeometry.GetYuv420FrameByteCountOrZero(Positive(Width, 640), Positive(Height, 480));
        public int BitrateBitsPerSecond => (int)((long)Positive(BitrateKbps, 1) * 1000L);
        public bool HasManagedConversionCostWarning
            => (long)Positive(Width, 640) * Positive(Height, 480) > 1280L * 720L;

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

            if (!CameraVideoFrameGeometry.ValidateRgb24Dimensions(Width, Height, "Media Foundation H.264 RGB24", out error))
                return false;

            if (!CameraVideoFrameGeometry.ValidateYuv420Dimensions(Width, Height, "Media Foundation H.264 NV12", out error))
                return false;

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

            if (BitrateKbps > MaxBitrateKbps)
            {
                error = "Media Foundation H.264 bitrate must be <= " + MaxBitrateKbps + " kbps.";
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
