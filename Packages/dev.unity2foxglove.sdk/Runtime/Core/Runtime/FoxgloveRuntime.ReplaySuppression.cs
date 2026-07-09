// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: FoxgloveRuntime replay publish suppression diagnostics.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveRuntime
    {
        private readonly HashSet<ReplaySuppressionWarningKey> _replaySuppressionWarnings =
            new HashSet<ReplaySuppressionWarningKey>();

        private void WarnReplaySuppressed(string operation, uint? channelId)
        {
            var key = new ReplaySuppressionWarningKey(operation, channelId);
            lock (_replaySuppressionWarnings)
            {
                if (!_replaySuppressionWarnings.Add(key))
                    return;
            }

            var channelSuffix = channelId.HasValue ? $" for channel {channelId.Value}" : string.Empty;
            _logger.LogWarning(
                $"Replay is enabled; ignoring live {operation}{channelSuffix}. Disable replay before publishing live data.");
        }

        private void ClearReplaySuppressionWarnings()
        {
            lock (_replaySuppressionWarnings)
                _replaySuppressionWarnings.Clear();
        }

        private readonly struct ReplaySuppressionWarningKey : IEquatable<ReplaySuppressionWarningKey>
        {
            private readonly string _operation;
            private readonly uint? _channelId;

            public ReplaySuppressionWarningKey(string operation, uint? channelId)
            {
                _operation = operation ?? string.Empty;
                _channelId = channelId;
            }

            public bool Equals(ReplaySuppressionWarningKey other)
                => string.Equals(_operation, other._operation, StringComparison.Ordinal)
                   && _channelId == other._channelId;

            public override bool Equals(object obj)
                => obj is ReplaySuppressionWarningKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(_operation);
                    return (hash * 397) ^ (_channelId.HasValue ? _channelId.Value.GetHashCode() : 0);
                }
            }
        }
    }
}
