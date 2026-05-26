// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Shared fixed-rate publish cadence scheduler for live publishers.

using System;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Per-topic fixed-rate scheduler state.
    /// </summary>
    public struct FixedRatePublishState
    {
        public double NextDueSec;
        public float LastRateHz;
        public bool HasSchedule;
    }

    /// <summary>
    /// Schedules fixed-rate publishing from an absolute monotonic time source.
    /// </summary>
    public static class FixedRatePublishScheduler
    {
        private const double EpsilonSeconds = 1e-9;

        /// <summary>
        /// Returns true when one publish is due at <paramref name="nowSec"/>.
        /// The first call for a topic, and any rate change, publishes
        /// immediately and schedules the next due time at one interval later.
        /// Missed intervals advance the schedule without producing bursts.
        /// </summary>
        public static bool ShouldPublish(
            double nowSec,
            float rateHz,
            ref FixedRatePublishState state,
            bool nonPositivePublishesEveryFrame)
        {
            if (rateHz <= 0f)
            {
                state = default;
                return nonPositivePublishesEveryFrame;
            }

            var intervalSec = 1d / rateHz;
            if (!state.HasSchedule || Math.Abs(state.LastRateHz - rateHz) > float.Epsilon)
            {
                state.HasSchedule = true;
                state.LastRateHz = rateHz;
                state.NextDueSec = nowSec + intervalSec;
                return true;
            }

            if (nowSec + EpsilonSeconds < state.NextDueSec)
                return false;

            var elapsedIntervals = Math.Floor((nowSec + EpsilonSeconds - state.NextDueSec) / intervalSec) + 1d;
            if (elapsedIntervals < 1d)
                elapsedIntervals = 1d;
            state.NextDueSec += elapsedIntervals * intervalSec;
            if (state.NextDueSec <= nowSec + EpsilonSeconds)
                state.NextDueSec += intervalSec;

            return true;
        }
    }
}
