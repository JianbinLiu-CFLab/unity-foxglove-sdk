// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/PointCloud
// Purpose: Native LiDAR PointCloud2 packed data construction without managed point objects.

using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.Schemas.PointCloud
{
    /// <summary>Builds PointCloud2 packed bytes from native LiDAR point snapshots.</summary>
    internal static class PointCloud2PackedDataBuilder
    {
        private const uint BaseStride = 26U;
        private const uint AbsoluteTimeStride = 30U;

        /// <summary>
        /// Builds the full SLAM PointCloud2 field layout from compacted valid
        /// VirtualLidar rays without allocating PointCloudFrame.Points.
        /// </summary>
        internal static PointCloudPackedData BuildVirtualLidarFullStride(
            IReadOnlyList<VirtualLidarPointData> points,
            bool emitAbsoluteTimeNs)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));

            return BuildVirtualLidarFullStride(points, points.Count, emitAbsoluteTimeNs);
        }

        /// <summary>
        /// Builds the full SLAM PointCloud2 field layout from the first
        /// <paramref name="pointCount"/> native VirtualLidar source slots.
        /// </summary>
        internal static PointCloudPackedData BuildVirtualLidarFullStride(
            IReadOnlyList<VirtualLidarPointData> points,
            int pointCount,
            bool emitAbsoluteTimeNs)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (pointCount < 0 || pointCount > points.Count)
                throw new ArgumentOutOfRangeException(nameof(pointCount));

            var validCount = CountValid(points, pointCount);
            var stride = emitAbsoluteTimeNs ? AbsoluteTimeStride : BaseStride;
            var capacity = ValidatePackedDataBudget(validCount, stride);
            var fields = BuildFields(emitAbsoluteTimeNs);

            using (var stream = new MemoryStream(capacity))
            using (var writer = new BinaryWriter(stream))
            {
                for (var i = 0; i < pointCount; i++)
                {
                    var point = points[i];
                    if (point.IsValid == 0)
                        continue;

                    writer.Write(point.X);
                    writer.Write(point.Y);
                    writer.Write(point.Z);
                    writer.Write(point.Intensity);
                    writer.Write(point.Reflectivity);
                    writer.Write(point.Ring);
                    writer.Write(point.TimeOffsetSeconds);
                    if (emitAbsoluteTimeNs)
                        writer.Write((uint)Math.Round(Math.Max(0f, point.TimeOffsetSeconds) * 1e9));
                }

                return new PointCloudPackedData(stride, fields, stream.ToArray());
            }
        }

        private static int CountValid(IReadOnlyList<VirtualLidarPointData> points, int pointCount)
        {
            var validCount = 0;
            for (var i = 0; i < pointCount; i++)
            {
                if (points[i].IsValid != 0)
                    validCount++;
            }

            return validCount;
        }

        private static int ValidatePackedDataBudget(int pointCount, uint stride)
        {
            var packedBytes = checked((long)pointCount * stride);
            if (packedBytes > PointCloudPackedDataBuilder.MaxPackedDataBytes)
            {
                throw new InvalidOperationException(
                    $"PointCloud2 packed data exceeds {PointCloudPackedDataBuilder.MaxPackedDataBytes} bytes ({packedBytes} requested).");
            }

            return (int)packedBytes;
        }

        private static IReadOnlyList<PointCloudPackedField> BuildFields(bool emitAbsoluteTimeNs)
        {
            var fields = new List<PointCloudPackedField>
            {
                Field("x", 0, PointCloudPackedNumericType.Float32),
                Field("y", 4, PointCloudPackedNumericType.Float32),
                Field("z", 8, PointCloudPackedNumericType.Float32),
                Field("intensity", 12, PointCloudPackedNumericType.Float32),
                Field("reflectivity", 16, PointCloudPackedNumericType.Float32),
                Field("ring", 20, PointCloudPackedNumericType.Uint16),
                Field("time_offset", 22, PointCloudPackedNumericType.Float32)
            };

            if (emitAbsoluteTimeNs)
                fields.Add(Field("t", 26, PointCloudPackedNumericType.Uint32));

            return fields.ToArray();
        }

        private static PointCloudPackedField Field(string name, uint offset, PointCloudPackedNumericType type)
        {
            return new PointCloudPackedField(name, offset, type);
        }
    }
}
