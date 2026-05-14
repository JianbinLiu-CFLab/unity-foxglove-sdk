// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/Clock
// Purpose: IFoxgloveClock interface and SystemClock implementation providing nanosecond timestamps for Foxglove message framing.

using Unity.FoxgloveSDK.Schemas;

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

    /// <summary>Default clock using FoxgloveTimeUtil for unified time source.</summary>
    public class SystemClock : IFoxgloveClock
    {
        public ulong NowNs => FoxgloveTimeUtil.NowUnixTimeNs();
    }
}
