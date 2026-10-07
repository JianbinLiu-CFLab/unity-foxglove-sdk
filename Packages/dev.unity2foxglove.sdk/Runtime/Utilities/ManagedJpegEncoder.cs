// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free JPEG encoder wrapper for async camera publishing.

using System;
using System.Buffers;
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

            byte[] rentedFlipBuffer = null;
            var source = rgb24;
            if (flipVertical)
            {
                rentedFlipBuffer = ArrayPool<byte>.Shared.Rent(expectedBytes);
                Rgb24Orientation.CopyRows(rgb24, rentedFlipBuffer, width, height, flipVertical: true);
                source = rentedFlipBuffer;
            }

            using var stream = new MemoryStream(Math.Max(1024, expectedBytes / 8));
            try
            {
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
            finally
            {
                if (rentedFlipBuffer != null)
                    ArrayPool<byte>.Shared.Return(rentedFlipBuffer);
            }
        }

        /// <summary>
        /// Clamps JPEG quality to a valid 1..100 range.
        /// </summary>
        private static int ClampQuality(int quality)
            => quality < 1 ? 1 : quality > 100 ? 100 : quality;

    }

    /// <summary>Shared RGB24 row-orientation authority for camera encoders and publishers.</summary>
    public static class Rgb24Orientation
    {
        /// <summary>Copies tightly packed RGB24 rows, optionally reversing their order.</summary>
        public static void CopyRows(
            byte[] source,
            byte[] destination,
            int width,
            int height,
            bool flipVertical)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width) + " / " + nameof(height));

            var expectedBytes = checked(width * height * 3);
            if (source.Length < expectedBytes || destination.Length < expectedBytes)
                throw new ArgumentException("RGB24 buffers are smaller than width * height * 3 bytes.");

            if (ReferenceEquals(source, destination))
            {
                if (flipVertical)
                {
                    byte[] rowScratch = null;
                    FlipRowsInPlace(destination, width, height, ref rowScratch);
                }
                return;
            }

            var stride = checked(width * 3);
            for (var y = 0; y < height; y++)
            {
                var sourceY = flipVertical ? height - 1 - y : y;
                Buffer.BlockCopy(
                    source,
                    sourceY * stride,
                    destination,
                    y * stride,
                    stride);
            }
        }

        /// <summary>Reverses RGB24 rows in place using caller-owned scratch storage.</summary>
        public static void FlipRowsInPlace(byte[] data, int width, int height, ref byte[] rowScratch)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width) + " / " + nameof(height));

            var rowBytes = checked(width * 3);
            var expectedBytes = checked(rowBytes * height);
            if (data.Length < expectedBytes)
                throw new ArgumentException("RGB24 buffer is smaller than width * height * 3 bytes.", nameof(data));
            if (rowScratch == null || rowScratch.Length < rowBytes)
                rowScratch = new byte[rowBytes];

            for (var top = 0; top < height / 2; top++)
            {
                var bottom = height - 1 - top;
                Buffer.BlockCopy(data, top * rowBytes, rowScratch, 0, rowBytes);
                Buffer.BlockCopy(data, bottom * rowBytes, data, top * rowBytes, rowBytes);
                Buffer.BlockCopy(rowScratch, 0, data, bottom * rowBytes, rowBytes);
            }
        }
    }
}
