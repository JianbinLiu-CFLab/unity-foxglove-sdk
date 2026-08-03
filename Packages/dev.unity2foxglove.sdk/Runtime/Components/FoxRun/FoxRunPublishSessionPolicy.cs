// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable Provider-neutral publish-session policy.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxRunPublishSessionPolicy
    {
        private readonly IReadOnlyList<FoxRunTransportId>
            _publishTransportIds;

        internal FoxRunPublishSessionPolicy(
            ulong sessionGeneration,
            bool sessionActive,
            IEnumerable<FoxRunTransportId> publishTransportIds,
            FoxRunEncoding webSocketEncoding,
            float defaultPublishRateHz,
            FoxRunDeliveryPolicy defaultDeliveryPolicy)
        {
            SessionGeneration = sessionGeneration;
            SessionActive = sessionActive;
            _publishTransportIds = Array.AsReadOnly(
                (publishTransportIds
                 ?? Enumerable.Empty<FoxRunTransportId>())
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray());
            WebSocketEncoding = webSocketEncoding;
            DefaultPublishRateHz = defaultPublishRateHz;
            DefaultDeliveryPolicy = defaultDeliveryPolicy;
        }

        public ulong SessionGeneration { get; }
        public bool SessionActive { get; }
        public IReadOnlyList<FoxRunTransportId>
            PublishTransportIds => _publishTransportIds;
        public FoxRunEncoding WebSocketEncoding { get; }
        public float DefaultPublishRateHz { get; }
        public FoxRunDeliveryPolicy DefaultDeliveryPolicy { get; }

        internal static FoxRunPublishSessionPolicy Disabled(
            ulong generation)
            => new FoxRunPublishSessionPolicy(
                generation,
                false,
                Array.Empty<FoxRunTransportId>(),
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunDeliveryPolicy.ProviderDefault);
    }

    internal sealed class FoxRunPublishSessionState
    {
        internal FoxRunPublishSessionState(
            ulong initialGeneration = 0)
        {
            Current =
                FoxRunPublishSessionPolicy.Disabled(
                    initialGeneration);
        }

        internal FoxRunPublishSessionPolicy Current
        {
            get;
            private set;
        }

        internal FoxRunPublishSessionPolicy BeginIfNeeded(
            IEnumerable<FoxRunTransportId> publishTransportIds,
            FoxRunEncoding webSocketEncoding,
            float defaultPublishRateHz,
            FoxRunDeliveryPolicy defaultDeliveryPolicy)
        {
            if (Current.SessionActive)
                return Current;
            if (Current.SessionGeneration == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "FoxRun publish session generation is exhausted.");
            }

            Current = new FoxRunPublishSessionPolicy(
                Current.SessionGeneration + 1UL,
                true,
                publishTransportIds,
                FoxRunEncodingResolver.ValidateProfileDefault(
                    webSocketEncoding),
                NormalizeRate(defaultPublishRateHz),
                defaultDeliveryPolicy);
            return Current;
        }

        internal FoxRunPublishSessionPolicy End()
        {
            if (Current.SessionActive)
            {
                Current =
                    FoxRunPublishSessionPolicy.Disabled(
                        Current.SessionGeneration);
            }

            return Current;
        }

        private static float NormalizeRate(float rateHz)
            => float.IsNaN(rateHz)
               || float.IsInfinity(rateHz)
               || rateHz <= 0f
                ? 10f
                : rateHz;
    }
}
