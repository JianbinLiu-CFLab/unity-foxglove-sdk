// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable Provider-neutral subscription-session policy.

using System;
using Unity.FoxgloveSDK.Schemas.MsgPack;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxRunSubscriptionSessionPolicy
    {
        internal FoxRunSubscriptionSessionPolicy(
            ulong sessionGeneration,
            bool subscriptionsEnabled,
            FoxRunTransportId defaultProvider,
            FoxRunEncoding webSocketSubscriptionEncoding,
            FoxRunDeliveryPolicy defaultDeliveryPolicy,
            int transportAdmissionRateLimitHz,
            int defaultSubscribeRateHz,
            int maxPayloadBytes)
        {
            SessionGeneration = sessionGeneration;
            SubscriptionsEnabled = subscriptionsEnabled;
            DefaultProvider = defaultProvider;
            WebSocketEncoding = webSocketSubscriptionEncoding;
            DefaultDeliveryPolicy = defaultDeliveryPolicy;
            TransportAdmissionRateLimitHz =
                transportAdmissionRateLimitHz;
            DefaultSubscribeRateHz = defaultSubscribeRateHz;
            MaxPayloadBytes = maxPayloadBytes;
            MessagePackReadLimits =
                FoxgloveMsgPackReadLimits.ForPayloadBytes(
                    maxPayloadBytes);
        }

        public ulong SessionGeneration { get; }
        public bool SubscriptionsEnabled { get; }
        public FoxRunTransportId DefaultProvider { get; }
        public FoxRunEncoding WebSocketEncoding { get; }
        public FoxRunDeliveryPolicy DefaultDeliveryPolicy { get; }
        public int TransportAdmissionRateLimitHz { get; }
        public int DefaultSubscribeRateHz { get; }
        public int MaxPayloadBytes { get; }
        public FoxgloveMsgPackReadLimits MessagePackReadLimits { get; }

        internal static FoxRunSubscriptionSessionPolicy Disabled(
            ulong generation)
            => new FoxRunSubscriptionSessionPolicy(
                generation,
                false,
                FoxgloveWebSocketTransport.TransportId,
                FoxRunEncoding.Protobuf,
                FoxRunDeliveryPolicy.ProviderDefault,
                1,
                1,
                64 * 1024);
    }

    internal sealed class FoxRunSubscriptionSessionState
    {
        internal FoxRunSubscriptionSessionState(
            ulong initialGeneration = 0)
        {
            Current =
                FoxRunSubscriptionSessionPolicy.Disabled(
                    initialGeneration);
        }

        internal FoxRunSubscriptionSessionPolicy Current
        {
            get;
            private set;
        }

        internal FoxRunSubscriptionSessionPolicy BeginIfNeeded(
            FoxRunTransportId defaultProvider,
            FoxRunEncoding webSocketSubscriptionEncoding,
            FoxRunDeliveryPolicy defaultDeliveryPolicy,
            int transportAdmissionRateLimitHz,
            int defaultSubscribeRateHz,
            int maxPayloadBytes = 64 * 1024)
        {
            if (Current.SubscriptionsEnabled)
                return Current;
            if (Current.SessionGeneration == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "FoxRun subscription session generation is exhausted.");
            }

            Current = new FoxRunSubscriptionSessionPolicy(
                Current.SessionGeneration + 1UL,
                true,
                defaultProvider,
                FoxRunEncodingResolver.ValidateProfileDefault(
                    webSocketSubscriptionEncoding),
                defaultDeliveryPolicy,
                Math.Max(1, transportAdmissionRateLimitHz),
                Math.Max(1, defaultSubscribeRateHz),
                Math.Max(1, maxPayloadBytes));
            return Current;
        }

        internal FoxRunSubscriptionSessionPolicy End()
        {
            if (Current.SubscriptionsEnabled)
            {
                Current =
                    FoxRunSubscriptionSessionPolicy.Disabled(
                        Current.SessionGeneration);
            }

            return Current;
        }
    }
}
