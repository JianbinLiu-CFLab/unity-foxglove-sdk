// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Generated FoxRun inbound source contract.

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxgloveInputTopicInfo
    {
        public FoxgloveInputTopicInfo(string topic, string encoding, FoxRunMode mode)
            : this(
                topic,
                FoxRunWireEncodingResolver.FromProtocolEncoding(encoding),
                mode,
                FoxRunSubscriptionProvider.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: false)
        {
        }

        public FoxgloveInputTopicInfo(string topic, FoxRunWireEncoding declaredWireEncoding, FoxRunMode mode)
            : this(
                topic,
                declaredWireEncoding,
                mode,
                FoxRunSubscriptionProvider.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: false)
        {
        }

        public FoxgloveInputTopicInfo(
            string topic,
            FoxRunWireEncoding declaredWireEncoding,
            FoxRunMode mode,
            FoxRunSubscriptionProvider declaredSubscriptionProvider,
            bool supportsWebSocket,
            bool supportsRos2Native)
        {
            Topic = topic ?? string.Empty;
            DeclaredWireEncoding = declaredWireEncoding;
            Encoding = declaredWireEncoding == FoxRunWireEncoding.Inherit
                ? "inherit"
                : FoxRunWireEncodingResolver.ToProtocolEncoding(declaredWireEncoding);
            Mode = mode;
            DeclaredSubscriptionProvider = declaredSubscriptionProvider;
            SupportsWebSocket = supportsWebSocket;
            SupportsRos2Native = supportsRos2Native;
        }

        public string Topic { get; }
        public FoxRunWireEncoding DeclaredWireEncoding { get; }
        public string Encoding { get; }
        public FoxRunMode Mode { get; }
        public FoxRunSubscriptionProvider DeclaredSubscriptionProvider { get; }
        public bool SupportsWebSocket { get; }
        public bool SupportsRos2Native { get; }
    }

    public interface IFoxgloveInputSource
    {
        int FoxgloveInput_TopicCount { get; }
        FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index);
        bool FoxgloveInput_TryApply(int topicIndex, byte[] payload, string encoding, out string error);
    }
}
