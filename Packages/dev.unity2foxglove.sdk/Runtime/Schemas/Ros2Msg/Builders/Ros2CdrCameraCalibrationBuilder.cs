// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/CameraCalibration.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds minimal CDR payloads for foxglove_msgs/msg/CameraCalibration.</summary>
    public static class Ros2CdrCameraCalibrationBuilder
    {
        public const string SchemaName = "foxglove_msgs/msg/CameraCalibration";

        /// <summary>Serialize camera calibration data to ROS 2 CDR.</summary>
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
            var dList = ToListOrEmpty(d);
            var kList = ToListOrEmpty(k);
            var rList = ToListOrEmpty(r);
            var pList = ToListOrEmpty(p);
            ValidateMatrices(kList, rList, pList);

            var writer = new Ros2CdrWriter();
            Ros2CdrGeometryWriter.WriteTime(writer, unixNs);
            writer.WriteString(frameId);
            writer.WriteUInt32(width);
            writer.WriteUInt32(height);
            writer.WriteString(distortionModel);
            writer.WriteFloat64Sequence(dList);
            writer.WriteFloat64Fixed(kList, 9, nameof(k));
            writer.WriteFloat64Fixed(rList, 9, nameof(r));
            writer.WriteFloat64Fixed(pList, 12, nameof(p));
            return writer.ToArray();
        }

        private static List<double> ToListOrEmpty(IEnumerable<double> values)
        {
            return values == null ? new List<double>() : values.ToList();
        }

        private static void ValidateMatrices(ICollection<double> k, ICollection<double> r, ICollection<double> p)
        {
            if (k.Count != 9)
                throw new ArgumentException("CameraCalibration K must contain exactly 9 values.", nameof(k));
            if (r.Count != 9)
                throw new ArgumentException("CameraCalibration R must contain exactly 9 values.", nameof(r));
            if (p.Count != 12)
                throw new ArgumentException("CameraCalibration P must contain exactly 12 values.", nameof(p));
        }
    }
}
