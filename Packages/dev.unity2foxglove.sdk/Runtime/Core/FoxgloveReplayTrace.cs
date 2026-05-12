// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Temporary replay diagnostics for tracing outgoing playback frames.

using System.Threading;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>Temporary replay diagnostics for seek/playback ordering.</summary>
    internal static class FoxgloveReplayTrace
    {
        internal static bool Enabled = false;

        private const int MaxLines = 512;
        private static long _ordinal;
        private static int _lines;

        internal static void ResetBudget()
        {
            Interlocked.Exchange(ref _lines, 0);
        }

        internal static bool TryFrame(
            string source,
            string topic,
            ulong logTimeNs,
            uint clientId,
            uint subscriptionId,
            uint channelId,
            string queue,
            out string message)
        {
            message = null;
            if (!Enabled || !TryTakeLine(out var suffix))
                return false;

            var ordinal = Interlocked.Increment(ref _ordinal);
            message =
                $"[ReplayTrace] #{ordinal} FRAME source={source} queue={queue} topic={topic} " +
                $"logTime={logTimeNs} client={clientId} sub={subscriptionId} channel={channelId}{suffix}";
            return true;
        }

        internal static bool TryTime(string source, ulong timeNs, string queue, out string message)
        {
            message = null;
            if (!Enabled || !TryTakeLine(out var suffix))
                return false;

            var ordinal = Interlocked.Increment(ref _ordinal);
            message = $"[ReplayTrace] #{ordinal} TIME source={source} queue={queue} time={timeNs}{suffix}";
            return true;
        }

        internal static bool TryEvent(string name, string details, out string message)
        {
            message = null;
            if (!Enabled || !TryTakeLine(out var suffix))
                return false;

            var ordinal = Interlocked.Increment(ref _ordinal);
            message = $"[ReplayTrace] #{ordinal} {name} {details}{suffix}";
            return true;
        }

        private static bool TryTakeLine(out string suffix)
        {
            suffix = "";
            var line = Interlocked.Increment(ref _lines);
            if (line <= MaxLines)
                return true;

            if (line == MaxLines + 1)
            {
                suffix = " (trace limit reached; later lines suppressed)";
                return true;
            }

            return false;
        }
    }
}
