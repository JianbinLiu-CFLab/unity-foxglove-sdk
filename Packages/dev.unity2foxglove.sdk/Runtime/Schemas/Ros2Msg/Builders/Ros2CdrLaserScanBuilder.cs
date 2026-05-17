// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/LaserScan.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds minimal CDR payloads for foxglove_msgs/msg/LaserScan.</summary>
    public static class Ros2CdrLaserScanBuilder
    {
        public const string SchemaName = "foxglove_msgs/msg/LaserScan";

        /// <summary>Serialize LaserScan data to ROS 2 CDR.</summary>
        public static byte[] Serialize(
            ulong unixNs,
            string frameId,
            double startAngle,
            double endAngle,
            IEnumerable<double> ranges,
            IEnumerable<double> intensities = null)
        {
            var rangeList = ToListOrEmpty(ranges);
            var intensityList = ToListOrEmpty(intensities);
            ValidateIntensities(rangeList, intensityList);

            var writer = new Ros2CdrWriter();
            Ros2CdrGeometryWriter.WriteTime(writer, unixNs);
            writer.WriteString(frameId);
            Ros2CdrGeometryWriter.WriteIdentityPose(writer);
            writer.WriteFloat64(startAngle);
            writer.WriteFloat64(endAngle);
            writer.WriteFloat64Sequence(rangeList);
            writer.WriteFloat64Sequence(intensityList);
            return writer.ToArray();
        }

        private static List<double> ToListOrEmpty(IEnumerable<double> values)
        {
            return values == null ? new List<double>() : values.ToList();
        }

        private static void ValidateIntensities(List<double> ranges, List<double> intensities)
        {
            if (intensities.Count != 0 && intensities.Count != ranges.Count)
                throw new ArgumentException("LaserScan intensities must be empty or have the same length as ranges.", nameof(intensities));
        }
    }
}
