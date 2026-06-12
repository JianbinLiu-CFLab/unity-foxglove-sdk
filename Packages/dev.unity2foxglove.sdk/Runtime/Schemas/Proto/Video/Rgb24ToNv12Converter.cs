// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Unity-free RGB24 to NV12 conversion for Media Foundation camera video.

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Converts packed RGB24 frames to NV12/YUV420sp.
    /// </summary>
    public static class Rgb24ToNv12Converter
    {
        public static bool TryConvertRgb24ToNv12(
            byte[] rgb24,
            int width,
            int height,
            byte[] nv12,
            bool flipVertical,
            out string error)
        {
            error = "";
            if (width <= 0 || height <= 0 || (width % 2) != 0 || (height % 2) != 0)
            {
                error = "RGB24-to-NV12 conversion requires positive even dimensions.";
                return false;
            }

            var rgbBytes = checked(width * height * 3);
            var nv12Bytes = checked(width * height * 3 / 2);
            if (rgb24 == null || rgb24.Length != rgbBytes)
            {
                error = "RGB24 input buffer length does not match width * height * 3.";
                return false;
            }

            if (nv12 == null || nv12.Length < nv12Bytes)
            {
                error = "NV12 output buffer length is smaller than width * height * 3 / 2.";
                return false;
            }

            var yPlaneLength = checked(width * height);
            var uvOffset = yPlaneLength;

            for (var y = 0; y < height; y += 2)
            {
                var rowBase0 = GetRgbRowBase(y, width, height, flipVertical);
                var rowBase1 = GetRgbRowBase(y + 1, width, height, flipVertical);
                var yRow0 = y * width;
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

                    nv12[yRow0 + x] = ComputeY(r00, g00, b00);
                    nv12[yRow0 + x + 1] = ComputeY(r01, g01, b01);
                    nv12[yRow1 + x] = ComputeY(r10, g10, b10);
                    nv12[yRow1 + x + 1] = ComputeY(r11, g11, b11);

                    var u = ComputeU(r00, g00, b00)
                        + ComputeU(r01, g01, b01)
                        + ComputeU(r10, g10, b10)
                        + ComputeU(r11, g11, b11);
                    var v = ComputeV(r00, g00, b00)
                        + ComputeV(r01, g01, b01)
                        + ComputeV(r10, g10, b10)
                        + ComputeV(r11, g11, b11);
                    var uv = uvOffset + (y / 2) * width + x;
                    nv12[uv] = ClampByte(u / 4);
                    nv12[uv + 1] = ClampByte(v / 4);
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
            => ClampByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

        private static int ComputeU(int r, int g, int b)
            => ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;

        private static int ComputeV(int r, int g, int b)
            => ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;

        private static byte ClampByte(int value)
            => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
    }
}
