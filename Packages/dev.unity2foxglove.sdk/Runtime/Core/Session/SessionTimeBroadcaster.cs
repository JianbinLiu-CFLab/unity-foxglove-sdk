// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Thread-safe rate gate for Foxglove time broadcasts.
    /// </summary>
    internal sealed class SessionTimeBroadcaster
    {
        private const float DefaultRateHz = 10f;
        private long _lastBroadcastTicks;

        internal void Reset()
            => Interlocked.Exchange(ref _lastBroadcastTicks, 0);

        internal bool TryReserveBroadcast(long nowTicks, float rateHz)
        {
            var effectiveRate = NormalizeRate(rateHz);
            var interval = Math.Max(1L, (long)(TimeSpan.TicksPerSecond / (double)effectiveRate));
            var last = Interlocked.Read(ref _lastBroadcastTicks);
            if (nowTicks - last < interval)
                return false;

            return Interlocked.CompareExchange(ref _lastBroadcastTicks, nowTicks, last) == last;
        }

        private static float NormalizeRate(float rateHz)
            => rateHz > 0f && !float.IsNaN(rateHz) && !float.IsInfinity(rateHz)
                ? rateHz
                : DefaultRateHz;
    }
}
