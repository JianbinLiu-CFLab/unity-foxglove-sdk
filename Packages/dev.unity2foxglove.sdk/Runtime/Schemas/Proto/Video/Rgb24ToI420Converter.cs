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

            // I420 stores one U and V sample for each 2x2 RGB block.
            for (var y = 0; y < height; y += 2)
            {
                var rowBase0 = GetRgbRowBase(y, width, height, flipVertical);
                var rowBase1 = GetRgbRowBase(y + 1, width, height, flipVertical);
                var yRow0 = yOffset + y * width;
                var yRow1 = yRow0 + width;
                for (var x = 0; x < width; x += 2)
                {
                    var rgbIndex00 = rowBase0 + x * 3;
                    var r00 = rgb24[rgbIndex00];
                    var g00 = rgb24[rgbIndex00 + 1];
                    var b00 = rgb24[rgbIndex00 + 2];

                    var rgbIndex01 = rgbIndex00 + 3;
                    var r01 = rgb24[rgbIndex01];
                    var g01 = rgb24[rgbIndex01 + 1];
                    var b01 = rgb24[rgbIndex01 + 2];

                    var rgbIndex10 = rowBase1 + x * 3;
                    var r10 = rgb24[rgbIndex10];
                    var g10 = rgb24[rgbIndex10 + 1];
                    var b10 = rgb24[rgbIndex10 + 2];

                    var rgbIndex11 = rgbIndex10 + 3;
                    var r11 = rgb24[rgbIndex11];
                    var g11 = rgb24[rgbIndex11 + 1];
                    var b11 = rgb24[rgbIndex11 + 2];

                    i420[yRow0 + x] = ComputeY(r00, g00, b00);
                    i420[yRow0 + x + 1] = ComputeY(r01, g01, b01);
                    i420[yRow1 + x] = ComputeY(r10, g10, b10);
                    i420[yRow1 + x + 1] = ComputeY(r11, g11, b11);

                    var rAvg = (r00 + r01 + r10 + r11) / 4;
                    var gAvg = (g00 + g01 + g10 + g11) / 4;
                    var bAvg = (b00 + b01 + b10 + b11) / 4;
                    var chromaIndex = (y / 2) * (width / 2) + (x / 2);
                    i420[uOffset + chromaIndex] = ComputeU(rAvg, gAvg, bAvg);
                    i420[vOffset + chromaIndex] = ComputeV(rAvg, gAvg, bAvg);
                }
            }

            return true;
        }

        private static int GetRgbRowBase(int y, int width, int height, bool flipVertical)
        {
            var sourceY = flipVertical ? height - 1 - y : y;
            return sourceY * width * 3;
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
