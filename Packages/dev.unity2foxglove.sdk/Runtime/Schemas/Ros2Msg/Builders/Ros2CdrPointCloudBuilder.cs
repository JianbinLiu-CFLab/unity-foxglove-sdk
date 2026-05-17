// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/PointCloud.

using System;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
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

            var packed = PointCloudPackedDataBuilder.Build(frame);
            var writer = new Ros2CdrWriter();
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
                writer.WriteUInt8((byte)field.Type);
            }
            writer.WriteByteArray(packed.Data);
            return writer.ToArray();
        }
    }
}
