// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR builder for standard sensor_msgs/msg/PointCloud2 payloads.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds CDR payloads for standard ROS 2 sensor_msgs/msg/PointCloud2.</summary>
    public static class Ros2CdrSensorPointCloud2Builder
    {
        /// <summary>ROS2 schema name serialized by this PointCloud2 CDR builder.</summary>
        public const string SchemaName = "sensor_msgs/msg/PointCloud2";

        private const byte PointFieldInt8 = 1;
        private const byte PointFieldUint8 = 2;
        private const byte PointFieldInt16 = 3;
        private const byte PointFieldUint16 = 4;
        private const byte PointFieldInt32 = 5;
        private const byte PointFieldUint32 = 6;
        private const byte PointFieldFloat32 = 7;
        private const byte PointFieldFloat64 = 8;

        /// <summary>Serialize a managed point-cloud frame as an unorganized PointCloud2 payload.</summary>
        public static byte[] Serialize(PointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var packed = PointCloudPackedDataBuilder.Build(frame);
            return Serialize(frame, packed);
        }

        internal static byte[] Serialize(
            PointCloudFrame frame,
            PointCloudPackedDataBuilder.PointCloudLayout layout)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var packed = PointCloudPackedDataBuilder.Build(frame, layout);
            return Serialize(frame, packed);
        }

        private static byte[] Serialize(PointCloudFrame frame, PointCloudPackedData packed)
        {
            var width = checked((uint)frame.GetPointCount());
            return Serialize(
                frame.UnixNs,
                frame.FrameId,
                height: 1U,
                width: width,
                fields: packed.Fields,
                pointStep: packed.PointStride,
                data: packed.Data,
                isDense: true);
        }

        /// <summary>Serialize a pre-packed PointCloud2 body with explicit layout metadata.</summary>
        public static byte[] Serialize(
            ulong unixNs,
            string frameId,
            uint height,
            uint width,
            IReadOnlyList<PointCloudPackedField> fields,
            uint pointStep,
            byte[] data,
            bool isDense)
        {
            fields ??= Array.Empty<PointCloudPackedField>();
            data ??= Array.Empty<byte>();
            ValidateLayout(height, width, pointStep, data);

            FoxgloveProfiler.Global.BeginSample("CdrBuild.PointCloud2");
            try
            {
                var writer = new Ros2CdrWriter(128 + (fields.Count * 32) + data.Length);
                Ros2CdrGeometryWriter.WriteTime(writer, unixNs);
                writer.WriteString(frameId);
                writer.WriteUInt32(height);
                writer.WriteUInt32(width);
                writer.WriteSequenceLength(fields.Count);
                for (var i = 0; i < fields.Count; i++)
                    WritePointField(writer, fields[i]);
                writer.WriteBool(false);
                writer.WriteUInt32(pointStep);
                writer.WriteUInt32(checked(pointStep * width));
                writer.WriteByteArray(data);
                writer.WriteBool(isDense);
                return writer.ToArray();
            }
            finally
            {
                FoxgloveProfiler.Global.EndSample();
            }
        }

        private static void WritePointField(Ros2CdrWriter writer, PointCloudPackedField field)
        {
            if (field == null)
                throw new ArgumentException("PointCloud2 fields must not contain null entries.", nameof(field));

            writer.WriteString(field.Name);
            writer.WriteUInt32(field.Offset);
            writer.WriteUInt8(MapDatatype(field.Type));
            writer.WriteUInt32(1U);
        }

        private static byte MapDatatype(PointCloudPackedNumericType type)
        {
            switch (type)
            {
                case PointCloudPackedNumericType.Int8:
                    return PointFieldInt8;
                case PointCloudPackedNumericType.Uint8:
                    return PointFieldUint8;
                case PointCloudPackedNumericType.Int16:
                    return PointFieldInt16;
                case PointCloudPackedNumericType.Uint16:
                    return PointFieldUint16;
                case PointCloudPackedNumericType.Int32:
                    return PointFieldInt32;
                case PointCloudPackedNumericType.Uint32:
                    return PointFieldUint32;
                case PointCloudPackedNumericType.Float32:
                    return PointFieldFloat32;
                case PointCloudPackedNumericType.Float64:
                    return PointFieldFloat64;
                default:
                    throw new NotSupportedException("Unsupported PointCloud2 packed numeric type: " + type);
            }
        }

        private static void ValidateLayout(uint height, uint width, uint pointStep, byte[] data)
        {
            if (height == 0U)
                throw new ArgumentOutOfRangeException(nameof(height), "PointCloud2 height must be greater than zero.");
            if (pointStep == 0U)
                throw new ArgumentOutOfRangeException(nameof(pointStep), "PointCloud2 point_step must be greater than zero.");

            var rowStep = checked(pointStep * width);
            var expectedBytes = checked((long)rowStep * height);
            if (data.LongLength != expectedBytes)
            {
                throw new ArgumentException(
                    $"PointCloud2 data length must equal height * width * point_step ({expectedBytes} bytes expected, {data.LongLength} provided).",
                    nameof(data));
            }
        }
    }
}
