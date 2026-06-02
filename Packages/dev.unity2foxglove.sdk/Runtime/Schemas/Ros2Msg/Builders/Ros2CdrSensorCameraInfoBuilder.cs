// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR builder for standard sensor_msgs/msg/CameraInfo payloads.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds CDR payloads for standard ROS 2 sensor_msgs/msg/CameraInfo.</summary>
    public static class Ros2CdrSensorCameraInfoBuilder
    {
        /// <summary>ROS2 schema name serialized by this CameraInfo CDR builder.</summary>
        public const string SchemaName = "sensor_msgs/msg/CameraInfo";

        /// <summary>Serialize a standard camera info message with default zero ROI.</summary>
        public static byte[] Serialize(
            ulong unixNs,
            string frameId,
            uint width,
            uint height,
            string distortionModel,
            IEnumerable<double> d,
            IEnumerable<double> k,
            IEnumerable<double> r,
            IEnumerable<double> p)
        {
            var dList = ToList(d, nameof(d));
            var kList = ToFixedList(k, 9, nameof(k));
            var rList = ToFixedList(r, 9, nameof(r));
            var pList = ToFixedList(p, 12, nameof(p));

            var writer = new Ros2CdrWriter(320);
            Ros2CdrGeometryWriter.WriteTime(writer, unixNs);
            writer.WriteString(frameId);
            writer.WriteUInt32(height);
            writer.WriteUInt32(width);
            writer.WriteString(string.IsNullOrWhiteSpace(distortionModel) ? "plumb_bob" : distortionModel);
            writer.WriteFloat64Sequence(dList);
            writer.WriteFloat64Fixed(kList, 9, nameof(k));
            writer.WriteFloat64Fixed(rList, 9, nameof(r));
            writer.WriteFloat64Fixed(pList, 12, nameof(p));
            writer.WriteUInt32(0U);
            writer.WriteUInt32(0U);
            writer.WriteUInt32(0U);
            writer.WriteUInt32(0U);
            writer.WriteUInt32(0U);
            writer.WriteUInt32(0U);
            writer.WriteBool(false);
            return writer.ToArray();
        }

        private static IReadOnlyList<double> ToList(IEnumerable<double> values, string name)
        {
            if (values == null)
                throw new ArgumentNullException(name);
            if (values is IReadOnlyList<double> list)
                return list;
            return new List<double>(values);
        }

        private static IReadOnlyList<double> ToFixedList(IEnumerable<double> values, int expected, string name)
        {
            var list = ToList(values, name);
            if (list.Count != expected)
                throw new ArgumentException($"{name} must contain exactly {expected} values.", name);
            return list;
        }
    }
}
