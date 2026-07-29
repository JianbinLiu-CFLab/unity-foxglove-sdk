// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable FoxRun subscription-session policy and lifecycle state.

using System;
using Unity.FoxgloveSDK.Schemas.MsgPack;

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
            FoxRunEndpoint defaultProvider,
            FoxRunEncoding webSocketSubscriptionEncoding,
            FoxRunResolvedQos defaultRos2Qos,
            int nativeCopyBudgetBytes,
            int transportAdmissionRateLimitHz,
            int defaultSubscribeRateHz,
            int maxPayloadBytes)
        {
            SessionGeneration = sessionGeneration;
            SubscriptionsEnabled = subscriptionsEnabled;
            DefaultSource = defaultProvider;
            FoxgloveEncoding = webSocketSubscriptionEncoding;
            DefaultRos2Qos = defaultRos2Qos;
            NativeCopyBudgetBytes = nativeCopyBudgetBytes;
            TransportAdmissionRateLimitHz = transportAdmissionRateLimitHz;
            DefaultSubscribeRateHz = defaultSubscribeRateHz;
            MaxPayloadBytes = maxPayloadBytes;
            MessagePackReadLimits = FoxgloveMsgPackReadLimits.ForPayloadBytes(maxPayloadBytes);
        }

        /// <summary>Monotonic identifier for the captured subscription session.</summary>
        public ulong SessionGeneration { get; }

        /// <summary>
        /// Whether this snapshot represents an active subscription session. Disabled snapshots
        /// retain inert placeholder values in the remaining fields to keep lifecycle state non-null.
        /// </summary>
        public bool SubscriptionsEnabled { get; }

        /// <summary>Concrete default subscription provider.</summary>
        public FoxRunEndpoint DefaultSource { get; }

        /// <summary>Concrete encoding retained for Foxglove WebSocket subscriptions.</summary>
        public FoxRunEncoding FoxgloveEncoding { get; }

        /// <summary>Concrete portable QoS preset for native ROS2 subscriptions.</summary>
        public FoxRunResolvedQos DefaultRos2Qos { get; }

        /// <summary>Maximum copied native message data retained before main-thread apply.</summary>
        public int NativeCopyBudgetBytes { get; }

        /// <summary>Frozen per-topic transport-admission safety limit in hertz.</summary>
        public int TransportAdmissionRateLimitHz { get; }

        /// <summary>
        /// Frozen default subscription frequency inherited by a
        /// subscription declaration that does not specify a positive Hz.
        /// </summary>
        public int DefaultSubscribeRateHz { get; }

        /// <summary>Frozen inbound byte cap for every registration in this session.</summary>
        public int MaxPayloadBytes { get; }

        /// <summary>Frozen bounded MessagePack reader limits derived from the byte cap.</summary>
        public FoxgloveMsgPackReadLimits MessagePackReadLimits { get; }

        internal static FoxRunSubscriptionSessionPolicy Disabled(ulong generation)
            => new(
                generation,
                false,
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                FoxRunResolvedQos.Default,
                FoxRunEncodingPolicyMigration.DefaultRos2NativeCopyBudgetBytes,
                1,
                1,
                64 * 1024);
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
            FoxRunEndpoint defaultProvider,
            FoxRunEncoding webSocketSubscriptionEncoding,
            FoxRunResolvedQos defaultRos2Qos,
            int nativeCopyBudgetBytes,
            int transportAdmissionRateLimitHz,
            int defaultSubscribeRateHz,
            int maxPayloadBytes = 64 * 1024)
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
                FoxRunEndpointResolver.ValidateProfileSource(defaultProvider),
                FoxRunEncodingResolver.ValidateProfileDefault(webSocketSubscriptionEncoding),
                defaultRos2Qos,
                FoxRunEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(nativeCopyBudgetBytes),
                Math.Max(1, transportAdmissionRateLimitHz),
                Math.Max(1, defaultSubscribeRateHz),
                Math.Max(1, maxPayloadBytes));
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
