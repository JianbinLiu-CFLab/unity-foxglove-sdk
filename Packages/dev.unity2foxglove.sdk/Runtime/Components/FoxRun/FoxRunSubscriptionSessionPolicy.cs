// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable FoxRun subscription-session policy and lifecycle state.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Immutable policy captured when a FoxRun subscription session begins.
    /// Consumers must check <see cref="SubscriptionsEnabled"/> before treating
    /// any remaining field as an effective subscription policy.
    /// </summary>
    public sealed class FoxRunSubscriptionSessionPolicy
    {
        internal FoxRunSubscriptionSessionPolicy(
            ulong sessionGeneration,
            bool subscriptionsEnabled,
            FoxRunSubscriptionProvider defaultProvider,
            FoxRunWireEncoding webSocketSubscriptionEncoding,
            FoxRunRos2QosPreset defaultRos2Qos,
            int nativeCopyBudgetBytes,
            int transportAdmissionRateLimitHz,
            int defaultSubscribeRateHz)
        {
            SessionGeneration = sessionGeneration;
            SubscriptionsEnabled = subscriptionsEnabled;
            DefaultProvider = defaultProvider;
            WebSocketSubscriptionEncoding = webSocketSubscriptionEncoding;
            DefaultRos2Qos = defaultRos2Qos;
            NativeCopyBudgetBytes = nativeCopyBudgetBytes;
            TransportAdmissionRateLimitHz = transportAdmissionRateLimitHz;
            DefaultSubscribeRateHz = defaultSubscribeRateHz;
        }

        /// <summary>Monotonic identifier for the captured subscription session.</summary>
        public ulong SessionGeneration { get; }

        /// <summary>
        /// Whether this snapshot represents an active subscription session. Disabled snapshots
        /// retain inert placeholder values in the remaining fields to keep lifecycle state non-null.
        /// </summary>
        public bool SubscriptionsEnabled { get; }

        /// <summary>Concrete default subscription provider.</summary>
        public FoxRunSubscriptionProvider DefaultProvider { get; }

        /// <summary>Concrete encoding retained for Foxglove WebSocket subscriptions.</summary>
        public FoxRunWireEncoding WebSocketSubscriptionEncoding { get; }

        /// <summary>Concrete portable QoS preset for native ROS2 subscriptions.</summary>
        public FoxRunRos2QosPreset DefaultRos2Qos { get; }

        /// <summary>Maximum copied native message data retained before main-thread apply.</summary>
        public int NativeCopyBudgetBytes { get; }

        /// <summary>Frozen per-topic transport-admission safety limit in hertz.</summary>
        public int TransportAdmissionRateLimitHz { get; }

        /// <summary>
        /// Frozen default subscription frequency inherited by a
        /// subscription declaration that does not specify a positive Hz.
        /// </summary>
        public int DefaultSubscribeRateHz { get; }

        internal static FoxRunSubscriptionSessionPolicy Disabled(ulong generation)
            => new(
                generation,
                false,
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunWireEncoding.Protobuf,
                FoxRunRos2QosPreset.Default,
                FoxRunWireEncodingPolicyMigration.DefaultRos2NativeCopyBudgetBytes,
                1,
                1);
    }

    /// <summary>
    /// Pure lifecycle state for capturing and ending immutable subscription sessions.
    /// </summary>
    internal sealed class FoxRunSubscriptionSessionState
    {
        internal FoxRunSubscriptionSessionState(ulong initialGeneration = 0)
        {
            Current = FoxRunSubscriptionSessionPolicy.Disabled(initialGeneration);
        }

        internal FoxRunSubscriptionSessionPolicy Current { get; private set; }

        internal FoxRunSubscriptionSessionPolicy BeginIfNeeded(
            FoxRunSubscriptionProvider defaultProvider,
            FoxRunWireEncoding webSocketSubscriptionEncoding,
            FoxRunRos2QosPreset defaultRos2Qos,
            int nativeCopyBudgetBytes,
            int transportAdmissionRateLimitHz,
            int defaultSubscribeRateHz)
        {
            if (Current.SubscriptionsEnabled)
                return Current;

            var generation = Current.SessionGeneration;
            if (generation == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "FoxRun subscription session generation is exhausted.");
            }

            var nextGeneration = generation + 1UL;
            Current = new FoxRunSubscriptionSessionPolicy(
                nextGeneration,
                true,
                FoxRunSubscriptionProviderResolver.NormalizeManagerDefault(defaultProvider),
                FoxRunWireEncodingResolver.ValidateManagerDefault(webSocketSubscriptionEncoding),
                FoxRunRos2QosResolver.NormalizeManagerDefault(defaultRos2Qos),
                FoxRunWireEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(nativeCopyBudgetBytes),
                Math.Max(1, transportAdmissionRateLimitHz),
                Math.Max(1, defaultSubscribeRateHz));
            return Current;
        }

        internal FoxRunSubscriptionSessionPolicy End()
        {
            if (!Current.SubscriptionsEnabled)
                return Current;

            Current = FoxRunSubscriptionSessionPolicy.Disabled(Current.SessionGeneration);
            return Current;
        }
    }
}
