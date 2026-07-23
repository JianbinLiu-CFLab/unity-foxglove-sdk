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
                hz: -1f,
                hasExplicitHz: false)
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
                hz: -1f,
                hasExplicitHz: false)
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
                false)
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
            float hz = -1f,
            bool hasExplicitHz = false)
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
            Hz = hz;
            HasExplicitHz = hasExplicitHz;
            HeartbeatIntervalSeconds = policy == FoxRunPolicy.Change
                                       && hasExplicitHz
                                       && hz > 0f
                ? 1f / hz
                : 0f;
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
        /// <summary>Declared cadence; input uses it only when <see cref="HasExplicitHz"/> is true and positive.</summary>
        public float Hz { get; }
        /// <summary>True when the author explicitly supplied Hz.</summary>
        public bool HasExplicitHz { get; }
        /// <summary>Derived fresh-duplicate heartbeat interval for Change policy.</summary>
        public float HeartbeatIntervalSeconds { get; }
    }

    public interface IFoxgloveInputSource
    {
        int FoxgloveInput_TopicCount { get; }
        FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index);
        bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error);
        int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz);
    }
}
