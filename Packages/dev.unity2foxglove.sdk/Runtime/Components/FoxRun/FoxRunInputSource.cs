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
        {
            Topic = topic ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            Mode = mode;
        }

        public string Topic { get; }
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
