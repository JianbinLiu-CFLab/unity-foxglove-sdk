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
                FoxRunEncodingResolver.FromProtocolEncoding(encoding),
                mode,
                (FoxRunEndpoint)0,
                hasExplicitSource: false,
                hasExplicitEncoding: true,
                supportsWebSocket: true,
                supportsRos2Native: false,
                policy: FoxRunPolicy.FixedRate,
                hz: -1f,
                hasExplicitHz: false)
        {
        }

        public FoxgloveInputTopicInfo(string topic, FoxRunEncoding declaredEncoding, FoxRunFlow mode)
            : this(
                topic,
                declaredEncoding,
                mode,
                (FoxRunEndpoint)0,
                hasExplicitSource: false,
                hasExplicitEncoding: declaredEncoding != 0,
                supportsWebSocket: true,
                supportsRos2Native: false,
                policy: FoxRunPolicy.FixedRate,
                hz: -1f,
                hasExplicitHz: false)
        {
        }

        public FoxgloveInputTopicInfo(
            string topic,
            FoxRunEncoding declaredEncoding,
            FoxRunFlow mode,
            FoxRunEndpoint declaredSource,
            bool supportsWebSocket,
            bool supportsRos2Native)
            : this(
                topic,
                declaredEncoding,
                mode,
                declaredSource,
                hasExplicitSource: declaredSource != 0,
                hasExplicitEncoding: declaredEncoding != 0,
                supportsWebSocket,
                supportsRos2Native,
                FoxRunPolicy.FixedRate,
                -1f,
                false)
        {
        }

        public FoxgloveInputTopicInfo(
            string topic,
            FoxRunEncoding declaredEncoding,
            FoxRunFlow mode,
            FoxRunEndpoint declaredSource,
            bool supportsWebSocket,
            bool supportsRos2Native,
            FoxRunPolicy policy = FoxRunPolicy.FixedRate,
            float hz = -1f,
            bool hasExplicitHz = false,
            FoxRunEndpoint declaredTargets = 0,
            bool hasExplicitTargets = false,
            bool hasExplicitQos = false)
            : this(
                topic,
                declaredEncoding,
                mode,
                declaredSource,
                hasExplicitSource: declaredSource != 0,
                hasExplicitEncoding: declaredEncoding != 0,
                supportsWebSocket,
                supportsRos2Native,
                policy,
                hz,
                hasExplicitHz,
                declaredTargets,
                hasExplicitTargets,
                hasExplicitQos)
        {
        }

        public FoxgloveInputTopicInfo(
            string topic,
            FoxRunEncoding declaredEncoding,
            FoxRunFlow mode,
            FoxRunEndpoint declaredSource,
            bool hasExplicitSource,
            bool hasExplicitEncoding,
            bool supportsWebSocket,
            bool supportsRos2Native,
            FoxRunPolicy policy = FoxRunPolicy.FixedRate,
            float hz = -1f,
            bool hasExplicitHz = false,
            FoxRunEndpoint declaredTargets = 0,
            bool hasExplicitTargets = false,
            bool hasExplicitQos = false)
        {
            Topic = topic ?? string.Empty;
            DeclaredEncoding = declaredEncoding;
            Encoding = declaredEncoding == (FoxRunEncoding)0
                ? "inherit"
                : FoxRunEncodingResolver.ToProtocolEncoding(declaredEncoding);
            Mode = mode;
            DeclaredSource = declaredSource;
            HasExplicitSource = hasExplicitSource;
            HasExplicitEncoding = hasExplicitEncoding;
            SupportsWebSocket = supportsWebSocket;
            SupportsRos2Native = supportsRos2Native;
            Policy = policy;
            Hz = hz;
            HasExplicitHz = hasExplicitHz;
            DeclaredTargets = declaredTargets;
            HasExplicitTargets = hasExplicitTargets;
            HasExplicitQos = hasExplicitQos;
            HeartbeatIntervalSeconds = policy == FoxRunPolicy.Change
                                       && hasExplicitHz
                                       && hz > 0f
                ? 1f / hz
                : 0f;
        }

        public string Topic { get; }
        public FoxRunEncoding DeclaredEncoding { get; }
        public string Encoding { get; }
        public FoxRunFlow Mode { get; }
        public FoxRunEndpoint DeclaredSource { get; }
        public bool HasExplicitSource { get; }
        public bool HasExplicitEncoding { get; }
        public bool SupportsWebSocket { get; }
        public bool SupportsRos2Native { get; }
        public FoxRunEndpoint DeclaredTargets { get; }
        public bool HasExplicitTargets { get; }
        public bool HasExplicitQos { get; }
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
