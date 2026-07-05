// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Applies the per-tick replay message soft cap without splitting log-time groups.
    /// </summary>
    internal static class McapReplayTickThrottler
    {
        internal static int CountPrefixPreservingLogTimeGroup(
            IReadOnlyList<McapMessage> result,
            int maxMessagesPerTick)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.Count == 0)
                return 0;
            if (maxMessagesPerTick == 0)
                return result.Count;
            if (maxMessagesPerTick < 0)
                maxMessagesPerTick = 1;

            if (result.Count <= maxMessagesPerTick)
                return result.Count;

            var takeCount = maxMessagesPerTick;
            var cutoffLogTime = result[takeCount - 1].LogTime;
            while (takeCount < result.Count && result[takeCount].LogTime == cutoffLogTime)
                takeCount++;
            return takeCount;
        }
    }
}
