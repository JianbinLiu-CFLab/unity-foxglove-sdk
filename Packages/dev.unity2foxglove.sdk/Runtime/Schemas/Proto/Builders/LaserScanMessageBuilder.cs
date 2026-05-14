// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Builders
// Purpose: Unity-free builders for foxglove.LaserScan JSON and protobuf payloads.

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>Builds <c>foxglove.LaserScan</c> JSON/protobuf payloads.</summary>
    public static class LaserScanMessageBuilder
    {
        /// <summary>Create a JSON LaserScan DTO.</summary>
        public static LaserScanMessage CreateJson(
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

            return new LaserScanMessage
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToJsonTime(unixNs),
                FrameId = frameId ?? "",
                Pose = FoxgloveProtoBuilderUtil.JsonIdentityPose(),
                StartAngle = startAngle,
                EndAngle = endAngle,
                Ranges = rangeList,
                Intensities = intensityList
            };
        }

        /// <summary>Create an official protobuf LaserScan message.</summary>
        public static Foxglove.LaserScan CreateProtobuf(
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

            var message = new Foxglove.LaserScan
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs),
                FrameId = frameId ?? "",
                Pose = FoxgloveProtoBuilderUtil.ProtoIdentityPose(),
                StartAngle = startAngle,
                EndAngle = endAngle
            };
            message.Ranges.AddRange(rangeList);
            message.Intensities.AddRange(intensityList);
            return message;
        }

        /// <summary>Create and serialize an official protobuf LaserScan payload.</summary>
        public static byte[] SerializeProtobuf(
            ulong unixNs,
            string frameId,
            double startAngle,
            double endAngle,
            IEnumerable<double> ranges,
            IEnumerable<double> intensities = null)
        {
            return CreateProtobuf(unixNs, frameId, startAngle, endAngle, ranges, intensities).ToByteArray();
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
