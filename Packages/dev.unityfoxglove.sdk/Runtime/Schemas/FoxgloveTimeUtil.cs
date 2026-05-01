using System;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>
    /// Unix epoch nanosecond time utilities for foxglove protocol timestamps.
    /// Current precision: milliseconds (derived from ToUnixTimeMilliseconds).
    /// True nanosecond precision (e.g. Stopwatch.GetTimestamp) deferred to Phase 5.
    /// </summary>
    public static class FoxgloveTimeUtil
    {
        /// <summary>Current UTC time as Unix epoch nanoseconds (millisecond precision).</summary>
        public static ulong NowUnixTimeNs()
        {
            return (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL;
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
