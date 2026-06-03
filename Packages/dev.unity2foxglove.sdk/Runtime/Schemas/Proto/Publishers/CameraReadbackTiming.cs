// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Tracks AsyncGPUReadback latency timing for camera diagnostics.

using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Stores camera readback start ticks until a request is consumed by the main thread.
    /// </summary>
    internal sealed class CameraReadbackTiming
    {
        private readonly object _gate = new object();
        private readonly Dictionary<ulong, long> _requestTicks = new Dictionary<ulong, long>();

        public void Remember(ulong unixNs, long ticks)
        {
            lock (_gate)
                _requestTicks[unixNs] = ticks;
        }

        public double TakeLatencyMs(ulong unixNs)
        {
            lock (_gate)
            {
                if (_requestTicks.TryGetValue(unixNs, out var ticks))
                {
                    _requestTicks.Remove(unixNs);
                    return ElapsedMs(ticks);
                }
            }

            return 0;
        }

        public void Clear()
        {
            lock (_gate)
                _requestTicks.Clear();
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
    }
}
