// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Shared publisher rate policy used by Inspector UI and runtime
// publish throttling. Kept UnityEngine-free so policy can be unit tested.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Per-publisher publish-rate source.
    /// </summary>
    public enum PublisherRateSource
    {
        UseManagerDefault,
        OverrideLocal
    }

    /// <summary>
    /// Resolves manager defaults and publisher-local overrides into one
    /// effective publish rate.
    /// </summary>
    public static class PublisherRatePolicy
    {
        /// <summary>
        /// Resolves the effective publish rate from manager and publisher
        /// settings. Non-positive values pass through so callers can preserve
        /// existing no-throttle semantics.
        /// </summary>
        public static float Resolve(
            PublisherRateSource source,
            float managerRateHz,
            float localRateHz,
            bool hasManager)
        {
            return source == PublisherRateSource.UseManagerDefault && hasManager
                ? managerRateHz
                : localRateHz;
        }
    }
}
