// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Deterministic backpressure policy for camera capture gating.
// Kept UnityEngine-free so runtime tests can validate decision logic.

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Result of one camera backpressure evaluation.
    /// </summary>
    public struct CameraBackpressureResult
    {
        /// <summary>Whether the caller should proceed with camera capture.</summary>
        public bool AllowCapture;
        /// <summary>True when capture was blocked by the cooldown window.</summary>
        public bool SkippedByCooldown;
        /// <summary>True when the current dropped-data-frame count increased.</summary>
        public bool PressureObserved;
        /// <summary>Updated dropped-data-frame count for the next evaluation.</summary>
        public long NextDropCount;
        /// <summary>Cooldown expiration time (seconds) for the next evaluation.</summary>
        public double NextCooldownUntilSec;
    }

    /// <summary>
    /// Detects transport backpressure from Phase 36 stats and enforces a cooldown
    /// window to suppress expensive camera capture/encode/publish work.
    /// </summary>
    public static class CameraBackpressurePolicy
    {
        /// <summary>
        /// Evaluate whether camera capture should proceed given the current
        /// transport dropped-frame count.
        /// </summary>
        /// <param name="enabled">Whether backpressure adaptation is active.</param>
        /// <param name="currentTimeSec">Monotonic time in seconds.</param>
        /// <param name="cooldownSec">How long to suppress after observing pressure.</param>
        /// <param name="previousDropCount">Dropped-frame count from the previous evaluation.</param>
        /// <param name="currentDropCount">Current aggregate dropped-frame count from transport stats.</param>
        /// <param name="currentCooldownUntilSec">Previous cooldown expiration time.</param>
        public static CameraBackpressureResult Evaluate(
            bool enabled,
            double currentTimeSec,
            double cooldownSec,
            long previousDropCount,
            long currentDropCount,
            double currentCooldownUntilSec)
        {
            if (!enabled)
            {
                return new CameraBackpressureResult
                {
                    AllowCapture = true,
                    SkippedByCooldown = false,
                    PressureObserved = false,
                    NextDropCount = previousDropCount,
                    NextCooldownUntilSec = currentCooldownUntilSec
                };
            }

            var effectiveCooldown = cooldownSec > 0 ? cooldownSec : 0;
            var dropIncreased = currentDropCount > previousDropCount;

            if (dropIncreased)
            {
                var nextUntil = currentTimeSec + effectiveCooldown;
                return new CameraBackpressureResult
                {
                    AllowCapture = effectiveCooldown <= 0,
                    SkippedByCooldown = effectiveCooldown > 0,
                    PressureObserved = true,
                    NextDropCount = currentDropCount,
                    NextCooldownUntilSec = nextUntil
                };
            }

            if (currentCooldownUntilSec > currentTimeSec)
            {
                return new CameraBackpressureResult
                {
                    AllowCapture = false,
                    SkippedByCooldown = true,
                    PressureObserved = false,
                    NextDropCount = previousDropCount,
                    NextCooldownUntilSec = currentCooldownUntilSec
                };
            }

            return new CameraBackpressureResult
            {
                AllowCapture = true,
                SkippedByCooldown = false,
                PressureObserved = false,
                NextDropCount = previousDropCount,
                NextCooldownUntilSec = currentTimeSec
            };
        }

        /// <summary>
        /// Check whether an encoded JPEG payload exceeds the byte budget.
        /// A budget of 0 means unlimited (always accept).
        /// </summary>
        public static bool ExceedsBudget(byte[] encodedBytes, int maxEncodedBytes)
        {
            if (maxEncodedBytes <= 0) return false;
            if (encodedBytes == null) return false;
            return encodedBytes.Length > maxEncodedBytes;
        }
    }
}
