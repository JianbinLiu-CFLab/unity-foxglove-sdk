// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/PointCloud.

using System;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg
{
    /// <summary>Builds minimal CDR payloads for foxglove_msgs/msg/PointCloud.</summary>
    public static class Ros2CdrPointCloudBuilder
    {
        public const string SchemaName = "foxglove_msgs/msg/PointCloud";

        /// <summary>Serialize a point-cloud frame to ROS 2 CDR.</summary>
        public static byte[] Serialize(PointCloudFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var layout = PointCloudPackedDataBuilder.BuildLayout(frame);
            var packed = PointCloudPackedDataBuilder.BuildPooled(frame, layout);
            try
            {
                return Serialize(frame, packed);
            }
            finally
            {
                packed.RecycleData();
            }
        }

        internal static byte[] Serialize(
            PointCloudFrame frame,
            PointCloudPackedDataBuilder.PointCloudLayout layout)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var packed = PointCloudPackedDataBuilder.BuildPooled(frame, layout);
            try
            {
                return Serialize(frame, packed);
            }
            finally
            {
                packed.RecycleData();
            }
        }

        private static byte[] Serialize(PointCloudFrame frame, PointCloudPackedData packed)
        {
            FoxgloveProfiler.Global.BeginSample("CdrBuild.PointCloud");
            try
            {
                var writer = new Ros2CdrWriter(160 + packed.Data.Length + (packed.Fields.Count * 32));
                Ros2CdrGeometryWriter.WriteTime(writer, frame.UnixNs);
                writer.WriteString(frame.FrameId);
                Ros2CdrGeometryWriter.WriteIdentityPose(writer);
                writer.WriteUInt32(packed.PointStride);
                writer.WriteSequenceLength(packed.Fields.Count);
                for (var i = 0; i < packed.Fields.Count; i++)
                {
                    var field = packed.Fields[i];
                    writer.WriteString(field.Name);
                    writer.WriteUInt32(field.Offset);
                    writer.WriteUInt8(MapDatatype(field.Type));
                }
                writer.WriteByteArray(packed.Data);
                return writer.ToArray();
            }
            finally
            {
                FoxgloveProfiler.Global.EndSample();
            }
        }

        private static byte MapDatatype(PointCloudPackedNumericType type)
        {
            switch (type)
            {
                case PointCloudPackedNumericType.Int8:
                case PointCloudPackedNumericType.Uint8:
                case PointCloudPackedNumericType.Int16:
                case PointCloudPackedNumericType.Uint16:
                case PointCloudPackedNumericType.Int32:
                case PointCloudPackedNumericType.Uint32:
                case PointCloudPackedNumericType.Float32:
                case PointCloudPackedNumericType.Float64:
                    return checked((byte)type);
                default:
                    throw new NotSupportedException("Unsupported PointCloud packed numeric type: " + type);
            }
        }
    }
}
