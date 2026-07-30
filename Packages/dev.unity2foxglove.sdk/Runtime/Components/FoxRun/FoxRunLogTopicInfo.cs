// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Generated Provider-neutral publish declaration metadata.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxgloveLogTopicInfo
    {
        public FoxgloveLogTopicInfo(
            string topic,
            float hz)
            : this(
                topic,
                hz,
                FoxRunPolicy.FixedRate,
                0f,
                FoxRunFlow.Publish,
                null,
                null,
                0,
                false,
                FoxRunDeliveryPolicy.ProviderDefault,
                false,
                true)
        {
        }

        public FoxgloveLogTopicInfo(
            string topic,
            float hz,
            FoxRunPolicy policy,
            float tolerance,
            FoxRunFlow flow,
            IReadOnlyList<string> publishTransportIds,
            string subscribeTransportId,
            FoxRunEncoding declaredEncoding,
            bool hasExplicitEncoding,
            FoxRunDeliveryPolicy deliveryPolicy,
            bool hasExplicitDeliveryPolicy,
            bool hasExplicitHz = true)
        {
            Topic = topic ?? string.Empty;
            Hz = hz;
            Policy = policy;
            Tolerance = tolerance < 0f ? 0f : tolerance;
            Flow = flow;
            PublishTransportIds = publishTransportIds == null
                ? null
                : Array.AsReadOnly(
                    publishTransportIds
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .ToArray());
            SubscribeTransportId = subscribeTransportId;
            DeclaredEncoding = declaredEncoding;
            HasExplicitEncoding = hasExplicitEncoding;
            DeliveryPolicy = deliveryPolicy;
            HasExplicitDeliveryPolicy =
                hasExplicitDeliveryPolicy;
            HasExplicitHz = hasExplicitHz;
        }

        public string Topic { get; }
        public float Hz { get; }
        public FoxRunPolicy Policy { get; }
        public float Tolerance { get; }
        public FoxRunFlow Flow { get; }
        public IReadOnlyList<string> PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
        public FoxRunEncoding DeclaredEncoding { get; }
        public bool HasExplicitEncoding { get; }
        public FoxRunDeliveryPolicy DeliveryPolicy { get; }
        public bool HasExplicitDeliveryPolicy { get; }
        public bool HasExplicitHz { get; }
    }
}
