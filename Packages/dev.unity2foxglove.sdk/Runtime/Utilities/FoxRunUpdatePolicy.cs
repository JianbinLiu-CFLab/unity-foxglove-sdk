// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Deterministic direction-aware update policy for [FoxRun] telemetry.
// Kept UnityEngine-free so runtime tests can validate decision logic.

// The public policy enum stays in Components because generated
// MonoBehaviour-facing FoxRun sources expose it in inspectors, while the
// stateless evaluator remains Unity-free under Util for runtime tests and
// generated code reuse.
namespace Unity.FoxgloveSDK.Util
{
    using Components;

    /// <summary>
    /// Stateless decision helper used by generated FoxRun code. Transport
    /// admission and decode happen before this helper; it decides only whether
    /// a locally owned value may cross the Unity boundary.
    /// </summary>
    public static class FoxRunUpdatePolicy
    {
        /// <summary>
        /// Decide whether a FoxRun topic should publish this frame.
        /// </summary>
        /// <param name="policy">Policy from the attribute.</param>
        /// <param name="nowSec">Current monotonic time in seconds.</param>
        /// <param name="hasPreviousValue">True after the first publish.</param>
        /// <param name="valueChanged">True when the value differs from last published.</param>
        /// <param name="lastPublishSec">Last successful publish time. 0 before first publish.</param>
        /// <param name="heartbeatIntervalSec">
        /// Heartbeat interval derived from an explicit positive Hz on a Change
        /// declaration; non-positive disables the heartbeat.
        /// </param>
        /// <returns>True if the value should be published.</returns>
        public static bool ShouldPublish(
            FoxRunPolicy policy,
            double nowSec,
            bool hasPreviousValue,
            bool valueChanged,
            double lastPublishSec,
            double heartbeatIntervalSec)
        {
            if (!IsFinite(nowSec))
                return false;

            switch (policy)
            {
                case FoxRunPolicy.FixedRate:
                    return true; // Hub already rate-limits via timer

                case FoxRunPolicy.Change:
                    if (!hasPreviousValue) return true;
                    if (valueChanged) return true;
                    if (IsFinite(lastPublishSec)
                        && IsFinite(heartbeatIntervalSec)
                        && heartbeatIntervalSec > 0d
                        && nowSec - lastPublishSec >= heartbeatIntervalSec) return true;
                    return false;

                case FoxRunPolicy.Trigger:
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Decides whether a newly staged input may be applied on the Unity
        /// main thread. A caller must invoke this only after its cadence gate
        /// is eligible. The helper never invents a stale heartbeat: every true
        /// result requires a value that arrived since the prior application.
        /// </summary>
        /// <param name="policy">The contract update policy.</param>
        /// <param name="hasPendingValue">Whether a newer owned value is staged.</param>
        /// <param name="hasLastAppliedValue">Whether this direction has applied before.</param>
        /// <param name="valueChanged">Whether the staged value differs from the last applied value.</param>
        /// <param name="nowSec">Current monotonic time in seconds.</param>
        /// <param name="lastApplySec">Time of the prior application.</param>
        /// <param name="heartbeatIntervalSec">
        /// Fresh-duplicate refresh interval derived from an explicit positive
        /// Hz on a Change declaration. A refresh still requires newer pending
        /// input; this helper never replays stale applied state.
        /// </param>
        /// <returns>True when the caller may apply its staged value now.</returns>
        public static bool ShouldApply(
            FoxRunPolicy policy,
            bool hasPendingValue,
            bool hasLastAppliedValue,
            bool valueChanged,
            double nowSec,
            double lastApplySec,
            double heartbeatIntervalSec)
        {
            if (!hasPendingValue || !IsFinite(nowSec))
                return false;

            switch (policy)
            {
                case FoxRunPolicy.FixedRate:
                    return true;

                case FoxRunPolicy.Change:
                    if (!hasLastAppliedValue || valueChanged)
                        return true;
                    return IsFinite(lastApplySec)
                           && IsFinite(heartbeatIntervalSec)
                           && heartbeatIntervalSec > 0d
                           && nowSec - lastApplySec >= heartbeatIntervalSec;

                case FoxRunPolicy.Trigger:
                default:
                    return false;
            }
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
