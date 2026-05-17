// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/CompressedPointCloud.

using System;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds minimal CDR payloads for foxglove_msgs/msg/CompressedPointCloud.</summary>
    public static class Ros2CdrCompressedPointCloudBuilder
    {
        public const string SchemaName = "foxglove_msgs/msg/CompressedPointCloud";
        public const string DracoFormat = "draco";

        /// <summary>Serialize compressed point-cloud bytes to ROS 2 CDR.</summary>
        public static byte[] Serialize(PointCloudFrame frame, byte[] compressedPayload, string format = DracoFormat)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (compressedPayload == null || compressedPayload.Length == 0)
                throw new ArgumentException("Compressed point-cloud payload must be non-empty.", nameof(compressedPayload));

            var writer = new Ros2CdrWriter();
            Ros2CdrGeometryWriter.WriteTime(writer, frame.UnixNs);
            writer.WriteString(frame.FrameId);
            Ros2CdrGeometryWriter.WriteIdentityPose(writer);
            writer.WriteByteArray(compressedPayload);
            writer.WriteString(format);
            return writer.ToArray();
        }
    }
}
