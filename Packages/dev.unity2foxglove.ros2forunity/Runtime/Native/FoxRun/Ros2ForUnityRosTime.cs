// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal readonly struct Ros2ForUnityTimestamp
    {
        internal Ros2ForUnityTimestamp(int seconds, uint nanoseconds)
        {
            Seconds = seconds;
            Nanoseconds = nanoseconds;
        }

        internal int Seconds { get; }
        internal uint Nanoseconds { get; }
    }

    /// <summary>Converts unsigned Unix nanoseconds without int32 wraparound.</summary>
    internal static class Ros2ForUnityRosTime
    {
        private const ulong NanosecondsPerSecond = 1_000_000_000UL;

        internal static Ros2ForUnityTimestamp SplitUnixNanoseconds(ulong unixNanoseconds)
        {
            var seconds = unixNanoseconds / NanosecondsPerSecond;
            return new Ros2ForUnityTimestamp(
                seconds > int.MaxValue ? int.MaxValue : (int)seconds,
                (uint)(unixNanoseconds % NanosecondsPerSecond));
        }

        internal static builtin_interfaces.msg.Time ToBuiltinTime(ulong unixNanoseconds)
        {
            var timestamp = SplitUnixNanoseconds(unixNanoseconds);
            return new builtin_interfaces.msg.Time
            {
                Sec = timestamp.Seconds,
                Nanosec = timestamp.Nanoseconds
            };
        }
    }
}
#endif
