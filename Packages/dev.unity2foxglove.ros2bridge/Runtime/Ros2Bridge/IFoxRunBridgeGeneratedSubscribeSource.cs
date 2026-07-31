// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Bridge-owned generated CDR subscription seam.

using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>
    /// Immutable physical binding emitted by the Bridge package. Core
    /// generation remains unaware of ROS identities and CDR.
    /// </summary>
    public readonly struct FoxRunBridgeGeneratedSubscribeBinding
    {
        public FoxRunBridgeGeneratedSubscribeBinding(
            int bindingIndex,
            int topicIndex,
            int publishTopicIndex,
            string stableMemberId,
            string topic,
            string canonicalRosType,
            string schemaSha256,
            FoxRunDeliveryPolicy deliveryPolicy,
            int maxPayloadBytes)
        {
            if (bindingIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(bindingIndex));
            if (topicIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(topicIndex));
            if (publishTopicIndex < -1)
                throw new ArgumentOutOfRangeException(
                    nameof(publishTopicIndex));
            if (string.IsNullOrWhiteSpace(stableMemberId))
                throw new ArgumentException(
                    "Stable member ID cannot be empty.",
                    nameof(stableMemberId));
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException(
                    "Topic cannot be empty.",
                    nameof(topic));
            if (string.IsNullOrWhiteSpace(canonicalRosType))
                throw new ArgumentException(
                    "Canonical ROS type cannot be empty.",
                    nameof(canonicalRosType));
            if (maxPayloadBytes < 4)
                throw new ArgumentOutOfRangeException(
                    nameof(maxPayloadBytes));

            BindingIndex = bindingIndex;
            TopicIndex = topicIndex;
            PublishTopicIndex = publishTopicIndex;
            StableMemberId = stableMemberId;
            Topic = topic;
            CanonicalRosType = canonicalRosType;
            SchemaSha256 = schemaSha256 ?? string.Empty;
            DeliveryPolicy = deliveryPolicy;
            MaxPayloadBytes = maxPayloadBytes;
        }

        public int BindingIndex { get; }
        public int TopicIndex { get; }
        public int PublishTopicIndex { get; }
        public string StableMemberId { get; }
        public string Topic { get; }
        public string CanonicalRosType { get; }
        public string SchemaSha256 { get; }
        public FoxRunDeliveryPolicy DeliveryPolicy { get; }
        public int MaxPayloadBytes { get; }
        public string MessageEncoding => "cdr";
    }

    /// <summary>
    /// Implemented only by the Bridge physical emitter. Decode and apply are
    /// invoked by the Bridge main-thread subscription hub.
    /// </summary>
    public interface IFoxRunBridgeGeneratedSubscribeSource
    {
        int FoxRunBridge_SubscribeBindingCount { get; }

        bool FoxRunBridge_TryGetSubscribeBinding(
            int bindingIndex,
            out FoxRunBridgeGeneratedSubscribeBinding binding,
            out string reason);

        bool FoxRunBridge_TryDecodeAndApply(
            int bindingIndex,
            ReadOnlyMemory<byte> payload,
            string ownershipTransportId,
            ulong ownershipGeneration,
            bool markRemoteOwned,
            out string reason);

        void FoxRunBridge_ReleaseRemoteOwnership(
            int topicIndex,
            string ownershipTransportId,
            ulong ownershipGeneration);
    }
}
