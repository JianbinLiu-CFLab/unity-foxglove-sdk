// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Lower compiler data into the Provider-neutral generation model.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunRoslynGenerationModelLowerer
    {
        public static FoxRunGenerationModel Lower(
            IReadOnlyList<FoxRunRoslynGenerationMember> members)
            => FoxRunGenerationModel.FromMembers(
                (members
                 ?? Array.Empty<FoxRunRoslynGenerationMember>())
                .Select(
                    member => new FoxRunGenerationMember(
                        ns: member.Namespace,
                        className: member.ClassName,
                        memberName: member.MemberName,
                        memberKind: member.MemberKind,
                        rawObservedTypeName: member.RawTypeName,
                        emissionTypeName:
                            member.EmissionTypeName,
                        isValueType: member.IsValueType,
                        isArray: member.IsArray,
                        elementTypeName:
                            member.ElementTypeName,
                        topic: member.Topic,
                        hz: member.Hz,
                        schemaName: member.SchemaName,
                        policy: member.Policy,
                        tolerance: member.Tolerance,
                        hostKind: "Roslyn",
                        rawMemberOrder:
                            member.RawMemberOrder,
                        conditionalSymbols:
                            member.ConditionalSymbols,
                        onlyIf: member.OnlyIf,
                        isAggregateMember:
                            member.IsAggregateMember,
                        jsonFieldName:
                            member.JsonFieldName,
                        mode: member.Mode,
                        encoding:
                            FoxRunGenerationMember
                                .DeclaredEncodingToText(
                                    member.Encoding),
                        protobufFieldNumber:
                            member.ProtobufFieldNumber,
                        typeShape: member.TypeShape,
                        generatesWebSocketCodec:
                            member.GeneratesWebSocketCodec,
                        namedArgumentPresence:
                            member.NamedArgumentPresence,
                        conditionMemberKind:
                            member.ConditionMemberKind,
                        isStream: member.IsStream,
                        publishTransportIds:
                            member.PublishTransportIds,
                        subscribeTransportId:
                            member.SubscribeTransportId,
                        reliability:
                            FoxRunGenerationMember
                                .DeclaredReliabilityToText(
                                    member.Reliability),
                        durability:
                            FoxRunGenerationMember
                                .DeclaredDurabilityToText(
                                    member.Durability),
                        history:
                            FoxRunGenerationMember
                                .DeclaredHistoryToText(
                                    member.History),
                        depth: member.Depth))
                .ToList());
    }

    internal sealed class FoxRunRoslynGenerationMember
    {
        public FoxRunRoslynGenerationMember(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string rawTypeName,
            string emissionTypeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            string schemaName,
            float hz,
            int policy,
            float tolerance,
            int rawMemberOrder,
            string conditionalSymbols,
            string onlyIf = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 1,
            int encoding = 0,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            bool? generatesWebSocketCodec = null,
            FoxRunNamedArgumentPresence namedArgumentPresence =
                FoxRunNamedArgumentPresence.None,
            FoxRunConditionMemberKind conditionMemberKind =
                FoxRunConditionMemberKind.None,
            bool isStream = false,
            IReadOnlyList<string> publishTransportIds = null,
            string subscribeTransportId = null,
            int reliability = 0,
            int durability = 0,
            int history = 0,
            int depth = 0)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            RawTypeName = rawTypeName ?? string.Empty;
            EmissionTypeName =
                string.IsNullOrEmpty(emissionTypeName)
                    ? FoxRunEmissionTypeNameFormatter
                        .NormalizeCSharpTypeName(rawTypeName)
                    : FoxRunEmissionTypeNameFormatter
                        .NormalizeCSharpTypeName(
                            emissionTypeName);
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = elementTypeName ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Hz = hz;
            Policy = policy;
            Mode = mode;
            Encoding = encoding;
            PublishTransportIds =
                publishTransportIds == null
                    ? null
                    : Array.AsReadOnly(
                        publishTransportIds.ToArray());
            SubscribeTransportId = subscribeTransportId;
            Reliability = reliability;
            Durability = durability;
            History = history;
            Depth = depth;
            GeneratesWebSocketCodec =
                generatesWebSocketCodec
                ?? (typeShape != null
                    || FoxRunCanonicalTypeNormalizer
                        .IsKnownCanonicalType(
                            FoxRunCanonicalTypeNormalizer
                                .NormalizeTypeName(
                                    isArray
                                    && !string.IsNullOrEmpty(
                                        elementTypeName)
                                        ? elementTypeName
                                        : EmissionTypeName)));
            ProtobufFieldNumber = protobufFieldNumber;
            TypeShape = typeShape;
            Tolerance = tolerance;
            RawMemberOrder = rawMemberOrder;
            ConditionalSymbols =
                conditionalSymbols ?? string.Empty;
            OnlyIf = onlyIf ?? string.Empty;
            ConditionMemberKind = conditionMemberKind;
            NamedArgumentPresence = namedArgumentPresence;
            IsAggregateMember = isAggregateMember;
            JsonFieldName = jsonFieldName ?? string.Empty;
            IsStream = isStream;
        }

        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string MemberName;
        public readonly string MemberKind;
        public readonly string RawTypeName;
        public readonly string EmissionTypeName;
        public readonly bool IsValueType;
        public readonly bool IsArray;
        public readonly string ElementTypeName;
        public readonly string Topic;
        public readonly string SchemaName;
        public readonly float Hz;
        public readonly int Policy;
        public readonly int Mode;
        public readonly int Encoding;
        public readonly IReadOnlyList<string> PublishTransportIds;
        public readonly string SubscribeTransportId;
        public readonly int Reliability;
        public readonly int Durability;
        public readonly int History;
        public readonly int Depth;
        public readonly bool GeneratesWebSocketCodec;
        public readonly int ProtobufFieldNumber;
        public readonly FoxRunTypeShape TypeShape;
        public readonly float Tolerance;
        public readonly int RawMemberOrder;
        public readonly string ConditionalSymbols;
        public readonly string OnlyIf;
        public readonly FoxRunConditionMemberKind ConditionMemberKind;
        public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;
        public readonly bool IsAggregateMember;
        public readonly string JsonFieldName;
        public readonly bool IsStream;
    }
}
