// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Generated publish declaration metadata shared by runtime resolvers.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Metadata for a FoxRun-published topic.</summary>
    public readonly struct FoxgloveLogTopicInfo
    {
        public readonly string Topic;
        public readonly float Hz;
        public readonly FoxRunPolicy Policy;
        public readonly float Tolerance;
        public readonly FoxRunFlow Flow;
        public readonly FoxRunEndpoint DeclaredSource;
        public readonly bool HasExplicitSource;
        public readonly FoxRunEndpoint DeclaredTargets;
        public readonly bool HasExplicitTargets;
        public readonly FoxRunEncoding DeclaredEncoding;
        public readonly bool HasExplicitEncoding;
        public readonly FoxRunQosProfile QosProfile;
        public readonly bool HasExplicitQosProfile;
        public readonly FoxRunQosReliability QosReliability;
        public readonly bool HasExplicitReliability;
        public readonly FoxRunQosDurability QosDurability;
        public readonly bool HasExplicitDurability;
        public readonly FoxRunQosHistory QosHistory;
        public readonly bool HasExplicitHistory;
        public readonly int QosDepth;
        public readonly bool HasExplicitDepth;
        public readonly bool HasExplicitQos;
        public readonly bool HasExplicitHz;

        public FoxgloveLogTopicInfo(string topic, float hz)
            : this(topic, hz, FoxRunPolicy.FixedRate, 0f)
        {
        }

        public FoxgloveLogTopicInfo(
            string topic,
            float hz,
            FoxRunPolicy policy,
            float tolerance)
            : this(
                topic,
                hz,
                policy,
                tolerance,
                FoxRunFlow.Publish,
                0,
                false,
                0,
                false,
                hasExplicitQos: false,
                hasExplicitHz: true)
        {
        }

        public FoxgloveLogTopicInfo(
            string topic,
            float hz,
            FoxRunPolicy policy,
            float tolerance,
            FoxRunFlow flow,
            FoxRunEndpoint declaredSource,
            bool hasExplicitSource,
            FoxRunEndpoint declaredTargets,
            bool hasExplicitTargets,
            bool hasExplicitQos,
            bool hasExplicitHz = true)
        {
            Topic = topic;
            Hz = hz;
            Policy = policy;
            Tolerance = tolerance < 0 ? 0 : tolerance;
            Flow = flow;
            DeclaredSource = declaredSource;
            HasExplicitSource = hasExplicitSource;
            DeclaredTargets = declaredTargets;
            HasExplicitTargets = hasExplicitTargets;
            DeclaredEncoding = 0;
            HasExplicitEncoding = false;
            QosProfile = 0;
            HasExplicitQosProfile = false;
            QosReliability = 0;
            HasExplicitReliability = false;
            QosDurability = 0;
            HasExplicitDurability = false;
            QosHistory = 0;
            HasExplicitHistory = false;
            QosDepth = 0;
            HasExplicitDepth = false;
            HasExplicitQos = hasExplicitQos;
            HasExplicitHz = hasExplicitHz;
        }

        public FoxgloveLogTopicInfo(
            string topic,
            float hz,
            FoxRunPolicy policy,
            float tolerance,
            FoxRunFlow flow,
            FoxRunEndpoint declaredSource,
            bool hasExplicitSource,
            FoxRunEndpoint declaredTargets,
            bool hasExplicitTargets,
            FoxRunEncoding declaredEncoding,
            bool hasExplicitEncoding,
            FoxRunQosProfile qosProfile,
            bool hasExplicitQosProfile,
            FoxRunQosReliability qosReliability,
            bool hasExplicitReliability,
            FoxRunQosDurability qosDurability,
            bool hasExplicitDurability,
            FoxRunQosHistory qosHistory,
            bool hasExplicitHistory,
            int qosDepth,
            bool hasExplicitDepth,
            bool hasExplicitHz = true)
        {
            Topic = topic;
            Hz = hz;
            Policy = policy;
            Tolerance = tolerance < 0 ? 0 : tolerance;
            Flow = flow;
            DeclaredSource = declaredSource;
            HasExplicitSource = hasExplicitSource;
            DeclaredTargets = declaredTargets;
            HasExplicitTargets = hasExplicitTargets;
            DeclaredEncoding = declaredEncoding;
            HasExplicitEncoding = hasExplicitEncoding;
            QosProfile = qosProfile;
            HasExplicitQosProfile = hasExplicitQosProfile;
            QosReliability = qosReliability;
            HasExplicitReliability = hasExplicitReliability;
            QosDurability = qosDurability;
            HasExplicitDurability = hasExplicitDurability;
            QosHistory = qosHistory;
            HasExplicitHistory = hasExplicitHistory;
            QosDepth = qosDepth;
            HasExplicitDepth = hasExplicitDepth;
            HasExplicitQos = hasExplicitQosProfile
                             || hasExplicitReliability
                             || hasExplicitDurability
                             || hasExplicitHistory
                             || hasExplicitDepth;
            HasExplicitHz = hasExplicitHz;
        }
    }
}
