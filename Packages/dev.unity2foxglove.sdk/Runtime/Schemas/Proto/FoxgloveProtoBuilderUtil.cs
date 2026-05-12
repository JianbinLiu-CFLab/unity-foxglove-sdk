// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Shared Unity-free helpers for building official Foxglove protobuf messages.

using Google.Protobuf.WellKnownTypes;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Shared conversion helpers used by generated protobuf publishers and JSON
    /// schema publishers so timestamp and identity-pose defaults stay aligned.
    /// </summary>
    internal static class FoxgloveProtoBuilderUtil
    {
        /// <summary>
        /// Converts Unix nanoseconds to the JSON schema time DTO used by
        /// Unity2Foxglove schema builders.
        /// </summary>
        public static FoxgloveTime ToJsonTime(ulong unixNs)
        {
            return new FoxgloveTime
            {
                Sec = unixNs / 1_000_000_000UL,
                Nsec = (uint)(unixNs % 1_000_000_000UL)
            };
        }

        /// <summary>
        /// Converts Unix nanoseconds to a Google.Protobuf timestamp.
        /// </summary>
        public static Timestamp ToTimestamp(ulong unixNs)
        {
            return new Timestamp
            {
                Seconds = (long)(unixNs / 1_000_000_000UL),
                Nanos = (int)(unixNs % 1_000_000_000UL)
            };
        }

        /// <summary>
        /// Creates a JSON schema identity pose with zero position and identity
        /// orientation.
        /// </summary>
        public static FoxglovePose JsonIdentityPose()
        {
            return new FoxglovePose
            {
                Position = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                Orientation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
            };
        }

        /// <summary>
        /// Creates a protobuf identity pose with zero position and identity
        /// orientation.
        /// </summary>
        public static Foxglove.Pose ProtoIdentityPose()
        {
            return new Foxglove.Pose
            {
                Position = new Foxglove.Vector3 { X = 0, Y = 0, Z = 0 },
                Orientation = new Foxglove.Quaternion { X = 0, Y = 0, Z = 0, W = 1 }
            };
        }
    }
}
