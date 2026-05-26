// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Deterministic publish policy for [FoxRun] telemetry.
// Kept UnityEngine-free so runtime tests can validate decision logic.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publish policy used by generated <c>[FoxRun]</c> sources after the hub's
    /// rate timer fires.
    /// </summary>
    public enum FoxRunPublishMode
    {
        /// <summary>Publish on every scheduled rate tick.</summary>
        FixedRate = 0,
        /// <summary>Publish the first sample and subsequent changed values only.</summary>
        OnChange = 1,
        /// <summary>Publish changed values, plus heartbeat samples at the force interval.</summary>
        OnChangeOrInterval = 2,
        /// <summary>Publish only when user code calls a generated trigger method.</summary>
        OnTrigger = 3,
    }
}

// The public enum stays in Components because generated MonoBehaviour-facing
// FoxRun sources expose it in inspectors, while the stateless policy helper
// remains Unity-free under Util for runtime tests and generated code reuse.
namespace Unity.FoxgloveSDK.Util
{
    using Components;

    /// <summary>
    /// Stateless decision helper used by generated FoxRun code.
    /// </summary>
    public static class FoxRunPublishPolicy
    {
        /// <summary>
        /// Decide whether a FoxRun topic should publish this frame.
        /// </summary>
        /// <param name="mode">Publish mode from the attribute.</param>
        /// <param name="nowSec">Current monotonic time in seconds.</param>
        /// <param name="hasPreviousValue">True after the first publish.</param>
        /// <param name="valueChanged">True when the value differs from last published.</param>
        /// <param name="lastPublishSec">Last successful publish time. 0 before first publish.</param>
        /// <param name="forceIntervalSec">Heartbeat interval; non-positive disables.</param>
        /// <returns>True if the value should be published.</returns>
        public static bool ShouldPublish(
            FoxRunPublishMode mode,
            double nowSec,
            bool hasPreviousValue,
            bool valueChanged,
            double lastPublishSec,
            double forceIntervalSec)
        {
            switch (mode)
            {
                case FoxRunPublishMode.FixedRate:
                    return true; // Hub already rate-limits via timer

                case FoxRunPublishMode.OnChange:
                    if (!hasPreviousValue) return true;  // first sample always
                    return valueChanged;

                case FoxRunPublishMode.OnChangeOrInterval:
                    if (!hasPreviousValue) return true;
                    if (valueChanged) return true;
                    if (forceIntervalSec > 0 && nowSec - lastPublishSec >= forceIntervalSec) return true;
                    return false;

                case FoxRunPublishMode.OnTrigger:
                    return false;

                default:
                    return false;
            }
        }
    }
}
