using System;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Abstraction over time. Provides the nanosecond timestamp used
    /// in Foxglove binary MessageData frames (logTime field).
    /// </summary>
    public interface IFoxgloveClock
    {
        /// <summary>
        /// Current time in nanoseconds since UNIX epoch.
        /// This is what gets written into the MessageData logTime field.
        /// </summary>
        ulong NowNs { get; }
    }

    /// <summary>Default clock using system time.</summary>
    public class SystemClock : IFoxgloveClock
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public ulong NowNs
        {
            get
            {
                var elapsed = DateTime.UtcNow - UnixEpoch;
                return (ulong)(elapsed.Ticks * 100); // 1 tick = 100 ns
            }
        }
    }
}
