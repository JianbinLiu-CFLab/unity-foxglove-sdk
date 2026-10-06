// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Exact checked conversion of FoxRun Unix nanoseconds to ROS2 time.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Pure managed representation of the ROS2 time fields. Keeping the range
    /// calculation independent from a ros2cs message constructor makes the
    /// overflow rule testable without loading an RMW native library.
    /// </summary>
    public readonly struct FoxRunRos2CustomUnixTimestamp
    {
        public FoxRunRos2CustomUnixTimestamp(int seconds, uint nanoseconds)
        {
            Seconds = seconds;
            Nanoseconds = nanoseconds;
        }

        public int Seconds { get; }
        public uint Nanoseconds { get; }
    }

    /// <summary>Converts unsigned Unix nanoseconds without saturation or wraparound.</summary>
    public static class FoxRunRos2CustomEnvelopeTimestamp
    {
        private const ulong NanosecondsPerSecond = 1_000_000_000UL;

        public static bool TryFromUnixNanoseconds(
            ulong unixNanoseconds,
            out FoxRunRos2CustomUnixTimestamp timestamp)
        {
            var seconds = unixNanoseconds / NanosecondsPerSecond;
            if (seconds > int.MaxValue)
            {
                timestamp = default;
                return false;
            }

            timestamp = new FoxRunRos2CustomUnixTimestamp(
                (int)seconds,
                (uint)(unixNanoseconds % NanosecondsPerSecond));
            return true;
        }
    }
}
#endif
