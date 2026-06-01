// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free JPEG encoder wrapper for async camera publishing.

using System;
using System.IO;
using StbImageWriteSharp;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Unity-free JPEG encoding helper used by the async camera JPEG pipeline.
    /// </summary>
    public static class ManagedJpegEncoder
    {
        /// <summary>
        /// Encodes raw RGB24 bytes into JPEG without depending on Unity runtime APIs.
        /// </summary>
        /// <param name="rgb24">Packed RGB24 pixel data.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="quality">JPEG quality in [1,100].</param>
        /// <param name="flipVertical">Whether to vertically flip rows before encoding.</param>
        /// <returns>Encoded JPEG byte payload.</returns>
        public static byte[] EncodeRgb24(byte[] rgb24, int width, int height, int quality, bool flipVertical)
        {
            if (rgb24 == null)
                throw new ArgumentNullException(nameof(rgb24));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            var expectedBytes = checked(width * height * 3);
            if (rgb24.Length < expectedBytes)
                throw new ArgumentException("RGB24 buffer is smaller than width * height * 3.", nameof(rgb24));

            var source = flipVertical ? FlipRgb24Rows(rgb24, width, height) : rgb24;
            using var stream = new MemoryStream(Math.Max(1024, expectedBytes / 8));
            var writer = new ImageWriter();
            writer.WriteJpg(
                source,
                width,
                height,
                ColorComponents.RedGreenBlue,
                stream,
                ClampQuality(quality));
            return stream.ToArray();
        }

        /// <summary>
        /// Clamps JPEG quality to a valid 1..100 range.
        /// </summary>
        private static int ClampQuality(int quality)
            => quality < 1 ? 1 : quality > 100 ? 100 : quality;

        /// <summary>
        /// Returns a copy of the RGB24 buffer with row order reversed.
        /// </summary>
        private static byte[] FlipRgb24Rows(byte[] source, int width, int height)
        {
            var stride = checked(width * 3);
            var expectedBytes = checked(stride * height);
            var flipped = new byte[expectedBytes];
            for (var y = 0; y < height; y++)
            {
                Buffer.BlockCopy(
                    source,
                    y * stride,
                    flipped,
                    (height - 1 - y) * stride,
                    stride);
            }

            return flipped;
        }
    }
}
