// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Additive grouped views over the canonical member descriptor.

namespace Unity.FoxgloveSDK.Editor
{
    public readonly struct FoxRunTypeContract
    {
        public FoxRunTypeContract(string rawTypeName, string emissionTypeName, string canonicalType, string provenanceTypeName, int rawMemberOrder, bool isValueType, bool isArray, string elementTypeName)
        {
            RawTypeName = rawTypeName ?? string.Empty;
            EmissionTypeName = emissionTypeName ?? string.Empty;
            CanonicalType = canonicalType ?? string.Empty;
            ProvenanceTypeName = provenanceTypeName ?? string.Empty;
            RawMemberOrder = rawMemberOrder;
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = elementTypeName ?? string.Empty;
        }
        public string RawTypeName { get; }
        public string EmissionTypeName { get; }
        public string CanonicalType { get; }
        public string ProvenanceTypeName { get; }
        public int RawMemberOrder { get; }
        public bool IsValueType { get; }
        public bool IsArray { get; }
        public string ElementTypeName { get; }
    }

    public readonly struct FoxRunScheduleContract
    {
        public FoxRunScheduleContract(int policy, float hz, float tolerance)
        { Policy = policy; Hz = hz; Tolerance = tolerance; }
        public int Policy { get; }
        public float Hz { get; }
        public float Tolerance { get; }
    }

    public readonly struct FoxRunEncodingContract
    {
        public FoxRunEncodingContract(string encoding, bool generatesWebSocketCodec)
        { Encoding = encoding ?? string.Empty; GeneratesWebSocketCodec = generatesWebSocketCodec; }
        public string Encoding { get; }
        public bool GeneratesWebSocketCodec { get; }
    }

    public readonly struct FoxRunTransportContract
    {
        public FoxRunTransportContract(string[] publishTransportIds, string subscribeTransportId)
        { PublishTransportIds = publishTransportIds; SubscribeTransportId = subscribeTransportId; }
        public string[] PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
    }

    public readonly struct FoxRunQosContract
    {
        public FoxRunQosContract(string reliability, string durability, string history, int depth)
        { Reliability = reliability ?? string.Empty; Durability = durability ?? string.Empty; History = history ?? string.Empty; Depth = depth; }
        public string Reliability { get; }
        public string Durability { get; }
        public string History { get; }
        public int Depth { get; }
    }

    public readonly struct FoxRunConditionContract
    {
        public FoxRunConditionContract(string onlyIf, FoxRunConditionMemberKind memberKind)
        { OnlyIf = onlyIf ?? string.Empty; MemberKind = memberKind; }
        public string OnlyIf { get; }
        public FoxRunConditionMemberKind MemberKind { get; }
    }

    public sealed class FoxRunGenerationMemberDecomposition
    {
        private FoxRunGenerationMemberDecomposition(FoxRunGenerationMember member)
        {
            Type = new FoxRunTypeContract(member.RawTypeName, member.EmissionTypeName, member.CanonicalType, member.RawObservedTypeName, member.RawMemberOrder, member.IsValueType, member.IsArray, member.ElementTypeName);
            Schedule = new FoxRunScheduleContract(member.Policy, member.Hz, member.Tolerance);
            Encoding = new FoxRunEncodingContract(member.Encoding, member.GeneratesWebSocketCodec);
            Transport = new FoxRunTransportContract(member.PublishTransportIds == null ? null : new System.Collections.Generic.List<string>(member.PublishTransportIds).ToArray(), member.SubscribeTransportId);
            Qos = new FoxRunQosContract(member.Reliability, member.Durability, member.History, member.Depth);
            Condition = new FoxRunConditionContract(member.OnlyIf, member.ConditionMemberKind);
        }

        public FoxRunTypeContract Type { get; }
        public FoxRunScheduleContract Schedule { get; }
        public FoxRunEncodingContract Encoding { get; }
        public FoxRunTransportContract Transport { get; }
        public FoxRunQosContract Qos { get; }
        public FoxRunConditionContract Condition { get; }

        public static FoxRunGenerationMemberDecomposition From(FoxRunGenerationMember member)
            => member == null ? throw new System.ArgumentNullException(nameof(member)) : new FoxRunGenerationMemberDecomposition(member);
    }
}
