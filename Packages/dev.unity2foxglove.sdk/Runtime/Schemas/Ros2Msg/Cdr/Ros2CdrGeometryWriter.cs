// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Cdr
// Purpose: Shared geometry/time writers for ROS 2 CDR Foxglove payloads.

using System;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Shared CDR writers for common ROS 2 Foxglove geometry structs.</summary>
    public static class Ros2CdrGeometryWriter
    {
        /// <summary>Write builtin_interfaces/Time from Unix nanoseconds.</summary>
        public static void WriteTime(Ros2CdrWriter writer, ulong unixNs)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var sec = unixNs / 1_000_000_000UL;
            var nsec = (uint)(unixNs % 1_000_000_000UL);
            if (sec > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(unixNs), "ROS 2 Time seconds must fit in int32.");

            writer.WriteInt32((int)sec);
            writer.WriteUInt32(nsec);
        }

        /// <summary>Write builtin_interfaces/Time.</summary>
        public static void WriteTime(Ros2CdrWriter writer, FoxgloveTime value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var sec = value?.Sec ?? 0UL;
            var nsec = value?.Nsec ?? 0U;
            if (sec > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Time seconds must fit in int32.");
            if (nsec > 999_999_999U)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Time nanoseconds must be less than 1e9.");

            writer.WriteInt32((int)sec);
            writer.WriteUInt32(nsec);
        }

        /// <summary>Write builtin_interfaces/Time from a generated protobuf timestamp.</summary>
        public static void WriteTime(Ros2CdrWriter writer, global::Google.Protobuf.WellKnownTypes.Timestamp value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var sec = value?.Seconds ?? 0L;
            var nsec = value?.Nanos ?? 0;
            if (sec < int.MinValue || sec > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Time seconds must fit in int32.");
            if (nsec < 0 || nsec > 999_999_999)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Time nanoseconds must be less than 1e9.");

            writer.WriteInt32((int)sec);
            writer.WriteUInt32((uint)nsec);
        }

        /// <summary>Write builtin_interfaces/Duration.</summary>
        public static void WriteDuration(Ros2CdrWriter writer, FoxgloveDuration value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var sec = value?.Sec ?? 0L;
            var nsec = value?.Nsec ?? 0U;
            if (sec < int.MinValue || sec > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Duration seconds must fit in int32.");
            if (nsec > 999_999_999U)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Duration nanoseconds must be less than 1e9.");

            writer.WriteInt32((int)sec);
            writer.WriteUInt32(nsec);
        }

        /// <summary>Write builtin_interfaces/Duration from a generated protobuf duration.</summary>
        public static void WriteDuration(Ros2CdrWriter writer, global::Google.Protobuf.WellKnownTypes.Duration value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var sec = value?.Seconds ?? 0L;
            var nsec = value?.Nanos ?? 0;
            if (sec < int.MinValue || sec > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Duration seconds must fit in int32.");
            if (nsec < 0 || nsec > 999_999_999)
                throw new ArgumentOutOfRangeException(nameof(value), "ROS 2 Duration nanoseconds must be less than 1e9.");

            writer.WriteInt32((int)sec);
            writer.WriteUInt32((uint)nsec);
        }

        /// <summary>Write geometry_msgs/Point.</summary>
        public static void WritePoint(Ros2CdrWriter writer, FoxgloveVector3 value)
        {
            WriteVector3(writer, value);
        }

        /// <summary>Write geometry_msgs/Vector3.</summary>
        public static void WriteVector3(Ros2CdrWriter writer, FoxgloveVector3 value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteFloat64(value?.X ?? 0.0);
            writer.WriteFloat64(value?.Y ?? 0.0);
            writer.WriteFloat64(value?.Z ?? 0.0);
        }

        /// <summary>Write geometry_msgs/Quaternion, defaulting to identity orientation.</summary>
        public static void WriteQuaternion(Ros2CdrWriter writer, FoxgloveQuaternion value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteFloat64(value?.X ?? 0.0);
            writer.WriteFloat64(value?.Y ?? 0.0);
            writer.WriteFloat64(value?.Z ?? 0.0);
            writer.WriteFloat64(value?.W ?? 1.0);
        }

        /// <summary>Write geometry_msgs/Pose, defaulting to identity pose.</summary>
        public static void WritePose(Ros2CdrWriter writer, FoxglovePose value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            WritePoint(writer, value?.Position);
            WriteQuaternion(writer, value?.Orientation);
        }

        /// <summary>Write an identity geometry_msgs/Pose.</summary>
        public static void WriteIdentityPose(Ros2CdrWriter writer)
        {
            WritePose(writer, (FoxglovePose)null);
        }

        /// <summary>Write foxglove_msgs/Color.</summary>
        public static void WriteColor(Ros2CdrWriter writer, FoxgloveColor value)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.WriteFloat64(value?.R ?? 0.0);
            writer.WriteFloat64(value?.G ?? 0.0);
            writer.WriteFloat64(value?.B ?? 0.0);
            writer.WriteFloat64(value?.A ?? 1.0);
        }
    }
}
