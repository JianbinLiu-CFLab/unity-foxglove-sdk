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
            : this(topic, FoxRunWireEncodingResolver.FromProtocolEncoding(encoding), mode)
        {
        }

        public FoxgloveInputTopicInfo(string topic, FoxRunWireEncoding declaredWireEncoding, FoxRunMode mode)
        {
            Topic = topic ?? string.Empty;
            DeclaredWireEncoding = declaredWireEncoding;
            Encoding = declaredWireEncoding == FoxRunWireEncoding.Inherit
                ? "inherit"
                : FoxRunWireEncodingResolver.ToProtocolEncoding(declaredWireEncoding);
            Mode = mode;
        }

        public string Topic { get; }
        public FoxRunWireEncoding DeclaredWireEncoding { get; }
        public string Encoding { get; }
        public FoxRunMode Mode { get; }
    }

    public interface IFoxgloveInputSource
    {
        int FoxgloveInput_TopicCount { get; }
        FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index);
        bool FoxgloveInput_TryApply(int topicIndex, byte[] payload, string encoding, out string error);
    }
}
