// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/CompressedImage.

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds minimal CDR payloads for foxglove_msgs/msg/CompressedImage.</summary>
    public static class Ros2CdrCompressedImageBuilder
    {
        public const string SchemaName = "foxglove_msgs/msg/CompressedImage";

        /// <summary>Serialize compressed image bytes to ROS 2 CDR.</summary>
        public static byte[] Serialize(ulong unixNs, string frameId, byte[] encodedBytes, string format)
        {
            var writer = new Ros2CdrWriter();
            Ros2CdrGeometryWriter.WriteTime(writer, unixNs);
            writer.WriteString(frameId);
            writer.WriteByteArray(encodedBytes);
            writer.WriteString(format);
            return writer.ToArray();
        }
    }
}
