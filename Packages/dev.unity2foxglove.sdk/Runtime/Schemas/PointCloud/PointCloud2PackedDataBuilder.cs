// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/PointCloud
// Purpose: Native LiDAR PointCloud2 packed data construction without managed point objects.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Schemas.PointCloud
{
    /// <summary>Builds PointCloud2 packed bytes from native LiDAR point snapshots.</summary>
    internal static class PointCloud2PackedDataBuilder
    {
        private const uint BaseStride = 26U;
        private const uint AbsoluteTimeStride = 30U;
        private static readonly IReadOnlyList<PointCloudPackedField> BaseFields = Array.AsReadOnly(new[]
        {
            Field("x", 0, PointCloudPackedNumericType.Float32),
            Field("y", 4, PointCloudPackedNumericType.Float32),
            Field("z", 8, PointCloudPackedNumericType.Float32),
            Field("intensity", 12, PointCloudPackedNumericType.Float32),
            Field("reflectivity", 16, PointCloudPackedNumericType.Float32),
            Field("ring", 20, PointCloudPackedNumericType.Uint16),
            Field("time_offset", 22, PointCloudPackedNumericType.Float32)
        });
        private static readonly IReadOnlyList<PointCloudPackedField> AbsoluteTimeFields = Array.AsReadOnly(new[]
        {
            Field("x", 0, PointCloudPackedNumericType.Float32),
            Field("y", 4, PointCloudPackedNumericType.Float32),
            Field("z", 8, PointCloudPackedNumericType.Float32),
            Field("intensity", 12, PointCloudPackedNumericType.Float32),
            Field("reflectivity", 16, PointCloudPackedNumericType.Float32),
            Field("ring", 20, PointCloudPackedNumericType.Uint16),
            Field("time_offset", 22, PointCloudPackedNumericType.Float32),
            Field("t", 26, PointCloudPackedNumericType.Uint32)
        });

        /// <summary>
        /// Builds the full SLAM PointCloud2 field layout from compacted valid
        /// VirtualLidar rays without allocating PointCloudFrame.Points.
        /// </summary>
        internal static PointCloudPackedData BuildVirtualLidarFullStride(VirtualLidarPointData[] points, bool emitAbsoluteTimeNs, bool useAcquisitionFrameCoordinates = false)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));

            return BuildVirtualLidarFullStride(points, points.Length, emitAbsoluteTimeNs, useAcquisitionFrameCoordinates);
        }

        /// <summary>
        /// Builds the full SLAM PointCloud2 field layout from the first
        /// <paramref name="pointCount"/> native VirtualLidar source slots using
        /// direct array indexing for worker hot paths.
        /// </summary>
        internal static PointCloudPackedData BuildVirtualLidarFullStride(
            VirtualLidarPointData[] points,
            int pointCount,
            bool emitAbsoluteTimeNs,
            bool useAcquisitionFrameCoordinates = false)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (pointCount < 0 || pointCount > points.Length)
                throw new ArgumentOutOfRangeException(nameof(pointCount));

            var validCount = CountValid(points, pointCount);
            var stride = emitAbsoluteTimeNs ? AbsoluteTimeStride : BaseStride;
            var capacity = ValidatePackedDataBudget(validCount, stride);
            var fields = BuildFields(emitAbsoluteTimeNs);

            var data = new byte[capacity];
            var offset = 0;
            for (var i = 0; i < pointCount; i++)
            {
                var point = points[i];
                if (point.IsValid == 0)
                    continue;

                var useAcquisition = useAcquisitionFrameCoordinates && point.HasAcquisitionFrame != 0;
                WriteSingleLittleEndian(data, ref offset, useAcquisition ? point.AcquisitionX : point.X);
                WriteSingleLittleEndian(data, ref offset, useAcquisition ? point.AcquisitionY : point.Y);
                WriteSingleLittleEndian(data, ref offset, useAcquisition ? point.AcquisitionZ : point.Z);
                WriteSingleLittleEndian(data, ref offset, point.Intensity);
                WriteSingleLittleEndian(data, ref offset, point.Reflectivity);
                WriteUInt16LittleEndian(data, ref offset, point.Ring);
                WriteSingleLittleEndian(data, ref offset, point.TimeOffsetSeconds);
                if (emitAbsoluteTimeNs)
                    WriteUInt32LittleEndian(data, ref offset, PointCloudPackedDataBuilder.TimeOffsetSecondsToNanoseconds(point.TimeOffsetSeconds));
            }

            return new PointCloudPackedData(stride, fields, data);
        }

        /// <summary>
        /// Builds the full SLAM PointCloud2 field layout from compacted valid
        /// VirtualLidar rays without allocating PointCloudFrame.Points.
        /// </summary>
        internal static PointCloudPackedData BuildVirtualLidarFullStride(
            IReadOnlyList<VirtualLidarPointData> points,
            bool emitAbsoluteTimeNs,
            bool useAcquisitionFrameCoordinates = false)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));

            return BuildVirtualLidarFullStride(points, points.Count, emitAbsoluteTimeNs, useAcquisitionFrameCoordinates);
        }

        /// <summary>
        /// Builds the full SLAM PointCloud2 field layout from the first
        /// <paramref name="pointCount"/> native VirtualLidar source slots.
        /// </summary>
        internal static PointCloudPackedData BuildVirtualLidarFullStride(
            IReadOnlyList<VirtualLidarPointData> points,
            int pointCount,
            bool emitAbsoluteTimeNs,
            bool useAcquisitionFrameCoordinates = false)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (pointCount < 0 || pointCount > points.Count)
                throw new ArgumentOutOfRangeException(nameof(pointCount));

            var validCount = CountValid(points, pointCount);
            var stride = emitAbsoluteTimeNs ? AbsoluteTimeStride : BaseStride;
            var capacity = ValidatePackedDataBudget(validCount, stride);
            var fields = BuildFields(emitAbsoluteTimeNs);

            var data = new byte[capacity];
            var offset = 0;
            for (var i = 0; i < pointCount; i++)
            {
                var point = points[i];
                if (point.IsValid == 0)
                    continue;

                var useAcquisition = useAcquisitionFrameCoordinates && point.HasAcquisitionFrame != 0;
                WriteSingleLittleEndian(data, ref offset, useAcquisition ? point.AcquisitionX : point.X);
                WriteSingleLittleEndian(data, ref offset, useAcquisition ? point.AcquisitionY : point.Y);
                WriteSingleLittleEndian(data, ref offset, useAcquisition ? point.AcquisitionZ : point.Z);
                WriteSingleLittleEndian(data, ref offset, point.Intensity);
                WriteSingleLittleEndian(data, ref offset, point.Reflectivity);
                WriteUInt16LittleEndian(data, ref offset, point.Ring);
                WriteSingleLittleEndian(data, ref offset, point.TimeOffsetSeconds);
                if (emitAbsoluteTimeNs)
                    WriteUInt32LittleEndian(data, ref offset, PointCloudPackedDataBuilder.TimeOffsetSecondsToNanoseconds(point.TimeOffsetSeconds));
            }

            return new PointCloudPackedData(stride, fields, data);
        }

        private static int CountValid(VirtualLidarPointData[] points, int pointCount)
        {
            var validCount = 0;
            for (var i = 0; i < pointCount; i++)
            {
                if (points[i].IsValid != 0)
                    validCount++;
            }

            return validCount;
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
            return emitAbsoluteTimeNs ? AbsoluteTimeFields : BaseFields;
        }

        private static void WriteSingleLittleEndian(byte[] data, ref int offset, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(float)), BitConverter.SingleToInt32Bits(value));
            offset += sizeof(float);
        }

        private static void WriteUInt16LittleEndian(byte[] data, ref int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);
            offset += sizeof(ushort);
        }

        private static void WriteUInt32LittleEndian(byte[] data, ref int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
            offset += sizeof(uint);
        }

        private static PointCloudPackedField Field(string name, uint offset, PointCloudPackedNumericType type)
        {
            return new PointCloudPackedField(name, offset, type);
        }
    }
}
