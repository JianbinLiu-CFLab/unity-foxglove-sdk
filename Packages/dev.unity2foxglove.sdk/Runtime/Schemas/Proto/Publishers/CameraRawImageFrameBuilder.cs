// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Builds owned-row raw RGB image DTOs for ROS2 DDS publication.

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

            var data = new byte[Math.Max(1, width) * Math.Max(1, height) * 3];
            CopyRgb24Rows(rgb24Readback, data, Math.Max(1, width), Math.Max(1, height), flipVertical);
            return new SensorRawImageFrame(unixNs, frameId, width, height, data, "rgb8");
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

            var expectedLength = width * height * 3;
            if (source.Length != expectedLength || destination.Length != expectedLength)
                throw new ArgumentException("RGB24 row buffers must match width * height * 3 bytes.");

            for (var y = 0; y < height; y++)
            {
                var sourceY = flipVertical ? (height - 1 - y) : y;
                var sourceOffset = sourceY * width * 3;
                var destinationOffset = y * width * 3;
                Array.Copy(source, sourceOffset, destination, destinationOffset, width * 3);
            }
        }
    }
}
