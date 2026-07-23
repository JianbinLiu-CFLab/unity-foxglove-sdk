// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Generated FoxRun inbound source contract.

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxgloveInputTopicInfo
    {
        public FoxgloveInputTopicInfo(string topic, string encoding, FoxRunFlow mode)
            : this(
                topic,
                FoxRunWireEncodingResolver.FromProtocolEncoding(encoding),
                mode,
                FoxRunSubscriptionProvider.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: false,
                policy: FoxRunPolicy.FixedRate,
                rateHz: -1f,
                hasExplicitRateHz: false,
                forceIntervalSeconds: 0f)
        {
        }

        public FoxgloveInputTopicInfo(string topic, FoxRunWireEncoding declaredWireEncoding, FoxRunFlow mode)
            : this(
                topic,
                declaredWireEncoding,
                mode,
                FoxRunSubscriptionProvider.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: false,
                policy: FoxRunPolicy.FixedRate,
                rateHz: -1f,
                hasExplicitRateHz: false,
                forceIntervalSeconds: 0f)
        {
        }

        public FoxgloveInputTopicInfo(
            string topic,
            FoxRunWireEncoding declaredWireEncoding,
            FoxRunFlow mode,
            FoxRunSubscriptionProvider declaredSubscriptionProvider,
            bool supportsWebSocket,
            bool supportsRos2Native)
            : this(
                topic,
                declaredWireEncoding,
                mode,
                declaredSubscriptionProvider,
                supportsWebSocket,
                supportsRos2Native,
                FoxRunPolicy.FixedRate,
                -1f,
                false,
                0f)
        {
        }

        public FoxgloveInputTopicInfo(
            string topic,
            FoxRunWireEncoding declaredWireEncoding,
            FoxRunFlow mode,
            FoxRunSubscriptionProvider declaredSubscriptionProvider,
            bool supportsWebSocket,
            bool supportsRos2Native,
            FoxRunPolicy policy = FoxRunPolicy.FixedRate,
            float rateHz = -1f,
            bool hasExplicitRateHz = false,
            float forceIntervalSeconds = 0f)
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
            Policy = policy;
            RateHz = rateHz;
            HasExplicitRateHz = hasExplicitRateHz;
            ForceIntervalSeconds = forceIntervalSeconds;
        }

        public string Topic { get; }
        public FoxRunWireEncoding DeclaredWireEncoding { get; }
        public string Encoding { get; }
        public FoxRunFlow Mode { get; }
        public FoxRunSubscriptionProvider DeclaredSubscriptionProvider { get; }
        public bool SupportsWebSocket { get; }
        public bool SupportsRos2Native { get; }
        /// <summary>Per-contract policy applied after transport admission.</summary>
        public FoxRunPolicy Policy { get; }
        /// <summary>Declared effective output rate; input uses it only when <see cref="HasExplicitRateHz"/> is true.</summary>
        public float RateHz { get; }
        /// <summary>True only when the author supplied a positive per-contract rate.</summary>
        public bool HasExplicitRateHz { get; }
        /// <summary>Fresh-duplicate interval used only by <see cref="FoxRunPolicy.ChangeOrInterval"/>.</summary>
        public float ForceIntervalSeconds { get; }
    }

    public interface IFoxgloveInputSource
    {
        int FoxgloveInput_TopicCount { get; }
        FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index);
        bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error);
        int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz);
    }
}
