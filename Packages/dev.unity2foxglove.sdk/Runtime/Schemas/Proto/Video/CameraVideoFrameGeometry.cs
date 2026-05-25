// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Shared checked video frame geometry helpers for camera encoder sidecars.

namespace Foxglove.Schemas.Video
{
    internal static class CameraVideoFrameGeometry
    {
        public const int MaxDimension = 8192;
        public const int MaxRgb24FrameBytes = 256 * 1024 * 1024;
        public const int MaxYuv420FrameBytes = 128 * 1024 * 1024;

        public static bool TryGetRgb24FrameByteCount(int width, int height, out int byteCount)
            => TryGetFrameByteCount(width, height, 3, 1, MaxRgb24FrameBytes, requireEvenDimensions: false, out byteCount);

        public static bool TryGetYuv420FrameByteCount(int width, int height, out int byteCount)
            => TryGetFrameByteCount(width, height, 3, 2, MaxYuv420FrameBytes, requireEvenDimensions: true, out byteCount);

        public static int GetRgb24FrameByteCountOrZero(int width, int height)
            => TryGetRgb24FrameByteCount(width, height, out var byteCount) ? byteCount : 0;

        public static int GetYuv420FrameByteCountOrZero(int width, int height)
            => TryGetYuv420FrameByteCount(width, height, out var byteCount) ? byteCount : 0;

        public static bool ValidateRgb24Dimensions(int width, int height, string label, out string error)
            => ValidateDimensions(width, height, requireEvenDimensions: false, label, out error);

        public static bool ValidateYuv420Dimensions(int width, int height, string label, out string error)
            => ValidateDimensions(width, height, requireEvenDimensions: true, label, out error);

        private static bool ValidateDimensions(
            int width,
            int height,
            bool requireEvenDimensions,
            string label,
            out string error)
        {
            if (width <= 0 || height <= 0)
            {
                error = label + " width and height must be positive.";
                return false;
            }

            if (width > MaxDimension || height > MaxDimension)
            {
                error = label + " width and height must be <= " + MaxDimension + ".";
                return false;
            }

            if (requireEvenDimensions && (((width | height) & 1) != 0))
            {
                error = label + " width and height must be even.";
                return false;
            }

            var hasValidByteCount = requireEvenDimensions
                ? TryGetYuv420FrameByteCount(width, height, out _)
                : TryGetRgb24FrameByteCount(width, height, out _);
            if (!hasValidByteCount)
            {
                error = label + " frame byte count exceeds the supported budget.";
                return false;
            }

            error = "";
            return true;
        }

        private static bool TryGetFrameByteCount(
            int width,
            int height,
            int numerator,
            int denominator,
            int maxFrameBytes,
            bool requireEvenDimensions,
            out int byteCount)
        {
            byteCount = 0;
            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
                return false;
            if (requireEvenDimensions && (((width | height) & 1) != 0))
                return false;

            var pixels = (long)width * height;
            var bytes = pixels * numerator / denominator;
            if (bytes <= 0 || bytes > maxFrameBytes || bytes > int.MaxValue)
                return false;

            byteCount = (int)bytes;
            return true;
        }
    }
}
