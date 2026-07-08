// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Tracks AsyncGPUReadback latency timing for camera diagnostics.

using System;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Stores camera readback start ticks until a request is consumed by the main thread.
    /// </summary>
    internal sealed class CameraReadbackTiming
    {
        private const int MaxTrackedRequests = 8;
        private readonly ulong[] _requestKeys = new ulong[MaxTrackedRequests];
        private readonly long[] _requestTicks = new long[MaxTrackedRequests];
        private int _nextSlot;

        public void Remember(ulong unixNs, long ticks)
        {
            for (var i = 0; i < _requestKeys.Length; i++)
            {
                if (_requestKeys[i] != unixNs)
                    continue;

                _requestTicks[i] = ticks;
                return;
            }

            var slot = _nextSlot;
            _nextSlot = (_nextSlot + 1) % _requestKeys.Length;
            _requestKeys[slot] = unixNs;
            _requestTicks[slot] = ticks;
        }

        public double TakeLatencyMs(ulong unixNs)
        {
            for (var i = 0; i < _requestKeys.Length; i++)
            {
                if (_requestKeys[i] != unixNs)
                    continue;

                var ticks = _requestTicks[i];
                _requestKeys[i] = 0UL;
                _requestTicks[i] = 0L;
                return ElapsedMs(ticks);
            }

            return 0;
        }

        public void Clear()
        {
            Array.Clear(_requestKeys, 0, _requestKeys.Length);
            Array.Clear(_requestTicks, 0, _requestTicks.Length);
            _nextSlot = 0;
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
    }
}
