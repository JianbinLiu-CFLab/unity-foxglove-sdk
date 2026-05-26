// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MessageDefinitions
// Purpose: Unix epoch nanosecond timestamp utilities for foxglove protocol.

using System;
using System.Diagnostics;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>
    /// Unix epoch nanosecond time utilities for foxglove protocol timestamps.
    /// Uses Stopwatch.GetTimestamp for nanosecond precision with UTC epoch anchor.
    /// </summary>
    public static class FoxgloveTimeUtil
    {
        private const long UnixEpochTicks = 621355968000000000L;

        private static readonly long AnchorTicks;  // Stopwatch.GetTimestamp() at init
        private static readonly long AnchorUnixNs;  // Unix epoch ns at init
        private static readonly double TicksToNs;   // Stopwatch tick → ns conversion

        static FoxgloveTimeUtil()
        {
            AnchorTicks = Stopwatch.GetTimestamp();
            AnchorUnixNs = checked((DateTimeOffset.UtcNow.Ticks - UnixEpochTicks) * 100L);
            TicksToNs = 1_000_000_000.0 / Stopwatch.Frequency;
        }

        /// <summary>Current UTC time as Unix epoch nanoseconds (Stopwatch precision).</summary>
        public static ulong NowUnixTimeNs()
        {
            var elapsedTicks = Stopwatch.GetTimestamp() - AnchorTicks;
            var elapsedNs = (long)(elapsedTicks * TicksToNs);
            // Guard against system time adjustment making this go backwards within the process
            var result = AnchorUnixNs + elapsedNs;
            return result > 0 ? (ulong)result : 0UL;
        }

        /// <summary>Split a Unix nanosecond timestamp into FoxgloveTime.</summary>
        public static FoxgloveTime ToFoxgloveTime(ulong unixNs)
        {
            return new FoxgloveTime
            {
                Sec = unixNs / 1_000_000_000UL,
                Nsec = (uint)(unixNs % 1_000_000_000UL)
            };
        }
    }
}
