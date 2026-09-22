// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Builds owned-row raw RGB image DTOs for optional Providers.

using System;
using Unity.FoxgloveSDK.Schemas.Camera;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Utility for producing <see cref="SensorRawImageFrame"/> payloads from
    /// Unity readback buffers.
    /// </summary>
    internal static class CameraRawImageFrameBuilder
    {
        public static SensorRawImageFrame BuildRgb8(
            ulong unixNs,
            string frameId,
            int width,
            int height,
            byte[] rgb24Readback,
            bool flipVertical)
        {
            if (rgb24Readback == null)
                throw new ArgumentNullException(nameof(rgb24Readback));

            var safeWidth = Math.Max(1, width);
            var safeHeight = Math.Max(1, height);
            var data = new byte[CheckedRgb24ByteLength(safeWidth, safeHeight)];
            CopyRgb24Rows(rgb24Readback, data, safeWidth, safeHeight, flipVertical);
            return new SensorRawImageFrame(unixNs, frameId, width, height, data, "rgb8");
        }

        public static SensorRawImageFrame BuildRgb8Owned(
            ulong unixNs,
            string frameId,
            int width,
            int height,
            byte[] ownedRgb24,
            bool flipVertical)
        {
            if (ownedRgb24 == null)
                throw new ArgumentNullException(nameof(ownedRgb24));
            var safeWidth = Math.Max(1, width);
            var safeHeight = Math.Max(1, height);
            var expectedLength = CheckedRgb24ByteLength(safeWidth, safeHeight);
            if (ownedRgb24.Length != expectedLength)
                throw new ArgumentException("RGB24 row buffer must match width * height * 3 bytes.", nameof(ownedRgb24));
            if (flipVertical)
                FlipRgb24RowsInPlace(ownedRgb24, safeWidth, safeHeight);
            return new SensorRawImageFrame(unixNs, frameId, width, height, ownedRgb24, "rgb8");
        }

        public static void CopyRgb24Rows(
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

            // AsyncGPUReadback RGB24 buffers are expected to be tightly packed;
            // use a platform-specific stride path if Unity exposes padded rows.
            var expectedLength = CheckedRgb24ByteLength(width, height);
            if (source.Length != expectedLength || destination.Length != expectedLength)
                throw new ArgumentException("RGB24 row buffers must match width * height * 3 bytes.");
            var rowStride = checked(width * 3);

            for (var y = 0; y < height; y++)
            {
                var sourceY = flipVertical ? (height - 1 - y) : y;
                var sourceOffset = sourceY * rowStride;
                var destinationOffset = y * rowStride;
                Array.Copy(source, sourceOffset, destination, destinationOffset, rowStride);
            }
        }

        private static void FlipRgb24RowsInPlace(byte[] data, int width, int height)
        {
            var rowStride = checked(width * 3);
            var row = new byte[rowStride];
            for (var y = 0; y < height / 2; y++)
            {
                var opposite = height - 1 - y;
                Buffer.BlockCopy(data, y * rowStride, row, 0, rowStride);
                Buffer.BlockCopy(data, opposite * rowStride, data, y * rowStride, rowStride);
                Buffer.BlockCopy(row, 0, data, opposite * rowStride, rowStride);
            }
        }

        private static int CheckedRgb24ByteLength(int width, int height)
        {
            var byteLength = (long)width * height * 3L;
            if (byteLength > int.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(width) + " / " + nameof(height),
                    "RGB24 frame dimensions exceed the maximum managed byte-array length.");

            return (int)byteLength;
        }
    }
}
