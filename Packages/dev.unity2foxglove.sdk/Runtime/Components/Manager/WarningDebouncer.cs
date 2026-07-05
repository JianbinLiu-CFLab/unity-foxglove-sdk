// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Pure warning cooldown helpers for FoxgloveManager diagnostics.

using System;
using System.Threading;

namespace Unity.FoxgloveSDK.Components
{
    internal static class WarningDebouncer
    {
        internal static bool TryUpdateCooldown(ref long lastTicks, long nowTicks, long intervalTicks)
        {
            var previousTicks = Interlocked.Read(ref lastTicks);
            if (!ShouldEmitCooldown(previousTicks, nowTicks, intervalTicks))
                return false;

            return Interlocked.CompareExchange(ref lastTicks, nowTicks, previousTicks) == previousTicks;
        }

        internal static bool ShouldEmitKeyedCooldown(
            string key,
            string lastKey,
            long lastTicks,
            long nowTicks,
            long intervalTicks)
        {
            if (!string.Equals(key, lastKey, StringComparison.Ordinal))
                return true;

            return ShouldEmitCooldown(lastTicks, nowTicks, intervalTicks);
        }

        private static bool ShouldEmitCooldown(long lastTicks, long nowTicks, long intervalTicks)
            => lastTicks == 0 || intervalTicks <= 0 || nowTicks - lastTicks >= intervalTicks;
    }
}
