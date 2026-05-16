// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Unity-free RGB24 to I420 conversion for OpenH264 camera video.

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Converts packed RGB24 frames to planar I420/YUV420p.
    /// </summary>
    public static class Rgb24ToI420Converter
    {
        public static bool TryConvertRgb24ToI420(
            byte[] rgb24,
            int width,
            int height,
            byte[] i420,
            bool flipVertical,
            out string error)
        {
            error = "";
            if (width <= 0 || height <= 0 || (width % 2) != 0 || (height % 2) != 0)
            {
                error = "RGB24-to-I420 conversion requires positive even dimensions.";
                return false;
            }

            var rgbBytes = width * height * 3;
            var i420Bytes = width * height * 3 / 2;
            if (rgb24 == null || rgb24.Length != rgbBytes)
            {
                error = "RGB24 input buffer length does not match width * height * 3.";
                return false;
            }

            if (i420 == null || i420.Length != i420Bytes)
            {
                error = "I420 output buffer length does not match width * height * 3 / 2.";
                return false;
            }

            var yOffset = 0;
            var uOffset = width * height;
            var vOffset = uOffset + (width * height / 4);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var rgbIndex = GetRgbIndex(x, y, width, height, flipVertical);
                    var r = rgb24[rgbIndex];
                    var g = rgb24[rgbIndex + 1];
                    var b = rgb24[rgbIndex + 2];
                    i420[yOffset + y * width + x] = ComputeY(r, g, b);
                }
            }

            // I420 stores one U and V sample for each 2x2 RGB block.
            for (var y = 0; y < height; y += 2)
            {
                for (var x = 0; x < width; x += 2)
                {
                    var rSum = 0;
                    var gSum = 0;
                    var bSum = 0;
                    for (var dy = 0; dy < 2; dy++)
                    {
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var rgbIndex = GetRgbIndex(x + dx, y + dy, width, height, flipVertical);
                            rSum += rgb24[rgbIndex];
                            gSum += rgb24[rgbIndex + 1];
                            bSum += rgb24[rgbIndex + 2];
                        }
                    }

                    var rAvg = rSum / 4;
                    var gAvg = gSum / 4;
                    var bAvg = bSum / 4;
                    var chromaIndex = (y / 2) * (width / 2) + (x / 2);
                    i420[uOffset + chromaIndex] = ComputeU(rAvg, gAvg, bAvg);
                    i420[vOffset + chromaIndex] = ComputeV(rAvg, gAvg, bAvg);
                }
            }

            return true;
        }

        private static int GetRgbIndex(int x, int y, int width, int height, bool flipVertical)
        {
            var sourceY = flipVertical ? height - 1 - y : y;
            return ((sourceY * width) + x) * 3;
        }

        private static byte ComputeY(int r, int g, int b)
            => ClampToByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

        private static byte ComputeU(int r, int g, int b)
            => ClampToByte(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);

        private static byte ComputeV(int r, int g, int b)
            => ClampToByte(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);

        private static byte ClampToByte(int value)
        {
            if (value < 0)
                return 0;
            if (value > 255)
                return 255;
            return (byte)value;
        }
    }
}
