// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR builder for standard sensor_msgs/msg/CompressedImage payloads.

using System;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds CDR payloads for standard ROS 2 sensor_msgs/msg/CompressedImage.</summary>
    public static class Ros2CdrSensorCompressedImageBuilder
    {
        /// <summary>ROS2 schema name serialized by this CompressedImage CDR builder.</summary>
        public const string SchemaName = "sensor_msgs/msg/CompressedImage";

        /// <summary>Serialize a compressed image with a ROS 2 Header, format, and byte data.</summary>
        public static byte[] Serialize(ulong unixNs, string frameId, byte[] data, string format)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var writer = new Ros2CdrWriter(64 + data.Length);
            Ros2CdrGeometryWriter.WriteTime(writer, unixNs);
            writer.WriteString(frameId);
            writer.WriteString(string.IsNullOrWhiteSpace(format) ? "jpeg" : format);
            writer.WriteByteArray(data);
            return writer.ToArray();
        }
    }
}
