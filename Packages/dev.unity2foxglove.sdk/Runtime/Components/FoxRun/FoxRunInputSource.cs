// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Generated Provider-neutral FoxRun inbound source contract.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxgloveInputTopicInfo
    {
        public FoxgloveInputTopicInfo(
            string topic,
            string encoding,
            FoxRunFlow mode)
            : this(
                topic,
                FoxRunEncodingResolver.FromProtocolEncoding(
                    encoding),
                mode,
                null,
                null,
                hasExplicitEncoding: true,
                supportsWebSocket: true,
                deliveryPolicy:
                    FoxRunDeliveryPolicy.ProviderDefault,
                hasExplicitDeliveryPolicy: false,
                policy: FoxRunPolicy.FixedRate,
                hz: -1f,
                hasExplicitHz: false)
        {
        }

        public FoxgloveInputTopicInfo(
            string topic,
            FoxRunEncoding declaredEncoding,
            FoxRunFlow mode,
            IReadOnlyList<string> publishTransportIds,
            string subscribeTransportId,
            bool hasExplicitEncoding,
            bool supportsWebSocket,
            FoxRunDeliveryPolicy deliveryPolicy,
            bool hasExplicitDeliveryPolicy,
            FoxRunPolicy policy = FoxRunPolicy.FixedRate,
            float hz = -1f,
            bool hasExplicitHz = false,
            bool isStream = false)
        {
            Topic = topic ?? string.Empty;
            DeclaredEncoding = declaredEncoding;
            Encoding = declaredEncoding == (FoxRunEncoding)0
                ? "inherit"
                : FoxRunEncodingResolver.ToProtocolEncoding(
                    declaredEncoding);
            Mode = mode;
            PublishTransportIds = publishTransportIds == null
                ? null
                : Array.AsReadOnly(
                    publishTransportIds
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .ToArray());
            SubscribeTransportId = subscribeTransportId;
            HasExplicitEncoding = hasExplicitEncoding;
            SupportsWebSocket = supportsWebSocket;
            DeliveryPolicy = deliveryPolicy;
            HasExplicitDeliveryPolicy =
                hasExplicitDeliveryPolicy;
            Policy = policy;
            Hz = hz;
            HasExplicitHz = hasExplicitHz;
            IsStream = isStream;
            HeartbeatIntervalSeconds =
                policy == FoxRunPolicy.Change
                && hasExplicitHz
                && hz > 0f
                    ? 1f / hz
                    : 0f;
        }

        public string Topic { get; }
        public FoxRunEncoding DeclaredEncoding { get; }
        public string Encoding { get; }
        public FoxRunFlow Mode { get; }
        public IReadOnlyList<string> PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
        public bool HasExplicitEncoding { get; }
        public bool SupportsWebSocket { get; }
        public FoxRunDeliveryPolicy DeliveryPolicy { get; }
        public bool HasExplicitDeliveryPolicy { get; }
        public bool IsStream { get; }
        public FoxRunPolicy Policy { get; }
        public float Hz { get; }
        public bool HasExplicitHz { get; }
        public float HeartbeatIntervalSeconds { get; }
    }

    public interface IFoxgloveInputSource
    {
        int FoxgloveInput_TopicCount { get; }
        FoxgloveInputTopicInfo FoxgloveInput_GetTopic(
            int index);
        bool FoxgloveInput_TryStage(
            int topicIndex,
            byte[] payload,
            string encoding,
            out string error);
        int FoxgloveInput_Flush(
            double nowSeconds,
            int inheritedSubscribeRateHz);
    }

    public interface IFoxgloveTransactionalInputSource
    {
        int FoxgloveInput_TransactionCount { get; }
        FoxgloveInputTopicInfo FoxgloveInput_GetTransaction(
            int transactionIndex);
        bool FoxgloveInput_TryStageTransaction(
            int transactionIndex,
            byte[] payload,
            Unity.FoxgloveSDK.Schemas.MsgPack
                .FoxgloveMsgPackReadLimits limits,
            out string error);
        void FoxgloveInput_ClearTransaction(
            int transactionIndex);
    }

    public interface IFoxgloveOwnedInputSource
    {
        bool FoxgloveInput_TryAcquireOwned(
            int topicIndex,
            out string error);
        void FoxgloveInput_ClearOwned(int topicIndex);
    }

    public interface IFoxgloveTransactionalOwnedInputSource
    {
        bool FoxgloveInput_TryAcquireTransactionalOwned(
            int transactionIndex,
            out string error);
        void FoxgloveInput_ClearTransactionalOwned(
            int transactionIndex);
    }
}
