// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Shared Unity-free helpers for building official Foxglove protobuf messages.

using Google.Protobuf.WellKnownTypes;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    internal static class FoxgloveProtoBuilderUtil
    {
        public static FoxgloveTime ToJsonTime(ulong unixNs)
        {
            return new FoxgloveTime
            {
                Sec = unixNs / 1_000_000_000UL,
                Nsec = (uint)(unixNs % 1_000_000_000UL)
            };
        }

        public static Timestamp ToTimestamp(ulong unixNs)
        {
            return new Timestamp
            {
                Seconds = (long)(unixNs / 1_000_000_000UL),
                Nanos = (int)(unixNs % 1_000_000_000UL)
            };
        }

        public static FoxglovePose JsonIdentityPose()
        {
            return new FoxglovePose
            {
                Position = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                Orientation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
            };
        }

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
