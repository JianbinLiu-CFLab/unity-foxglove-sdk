// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/FrameTransform.

using System;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds minimal CDR payloads for foxglove_msgs/msg/FrameTransform.</summary>
    public static class Ros2CdrFrameTransformBuilder
    {
        public const string SchemaName = "foxglove_msgs/msg/FrameTransform";

        /// <summary>Serialize a FrameTransform DTO to ROS 2 CDR.</summary>
        public static byte[] Serialize(FrameTransformMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var writer = new Ros2CdrWriter();
            Ros2CdrGeometryWriter.WriteTime(writer, message.Timestamp);
            writer.WriteString(message.ParentFrameId);
            writer.WriteString(message.ChildFrameId);
            Ros2CdrGeometryWriter.WriteVector3(writer, message.Translation);
            Ros2CdrGeometryWriter.WriteQuaternion(writer, message.Rotation);
            return writer.ToArray();
        }
    }
}
