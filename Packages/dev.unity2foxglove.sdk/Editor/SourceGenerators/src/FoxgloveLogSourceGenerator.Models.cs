// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Provider-neutral compiler data carriers for FoxRun and FoxService.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal sealed class ServiceDiagnostic
    {
        public ServiceDiagnostic(
            string id,
            Location location,
            string target)
        {
            Id = id ?? string.Empty;
            Location = location ?? Location.None;
            Target = target ?? string.Empty;
        }

        public string Id { get; }
        public Location Location { get; }
        public string Target { get; }
    }

    internal sealed class ServiceMethodData
    {
        public ServiceMethodData(
            string ns,
            string className,
            string methodName,
            string serviceName,
            string serviceType,
            string description,
            string requestSchemaName,
            string responseSchemaName,
            string requestSchema,
            string responseSchema,
            string requestTypeName,
            string responseTypeName,
            bool hasRequest,
            bool hasResponse,
            Location location,
            ServiceDiagnostic[] diagnostics)
        {
            Ns = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            MethodName = methodName ?? string.Empty;
            ServiceName = serviceName ?? string.Empty;
            ServiceType = serviceType ?? string.Empty;
            Description = description ?? string.Empty;
            RequestSchemaName = requestSchemaName ?? string.Empty;
            ResponseSchemaName = responseSchemaName ?? string.Empty;
            RequestSchema = requestSchema ?? string.Empty;
            ResponseSchema = responseSchema ?? string.Empty;
            RequestTypeName = requestTypeName ?? string.Empty;
            ResponseTypeName = responseTypeName ?? string.Empty;
            HasRequest = hasRequest;
            HasResponse = hasResponse;
            Location = location ?? Location.None;
            Diagnostics =
                diagnostics ?? Array.Empty<ServiceDiagnostic>();
        }

        public string Ns { get; }
        public string ClassName { get; }
        public string MethodName { get; }
        public string ServiceName { get; }
        public string ServiceType { get; }
        public string Description { get; }
        public string RequestSchemaName { get; }
        public string ResponseSchemaName { get; }
        public string RequestSchema { get; }
        public string ResponseSchema { get; }
        public string RequestTypeName { get; }
        public string ResponseTypeName { get; }
        public bool HasRequest { get; }
        public bool HasResponse { get; }
        public Location Location { get; }
        public ServiceDiagnostic[] Diagnostics { get; }

        public FoxServiceSourceEmitter.ServiceMethod ToEmitterMethod()
            => new FoxServiceSourceEmitter.ServiceMethod(
                MethodName,
                ServiceName,
                ServiceType,
                Description,
                RequestSchemaName,
                ResponseSchemaName,
                RequestSchema,
                ResponseSchema,
                RequestTypeName,
                ResponseTypeName,
                HasRequest,
                HasResponse);
    }

    internal sealed class MemberData
    {
        public readonly string Ns;
        public readonly string ClassName;
        public readonly string MemberName;
        public readonly string MemberType;
        public readonly string EmissionTypeName;
        public readonly string MemberKind;
        public readonly bool IsValueType;
        public readonly bool IsArray;
        public readonly string ElementTypeName;
        public readonly FoxRunTypeShape TypeShape;
        public readonly int RawMemberOrder;
        public readonly Location MemberLocation;
        public readonly bool IsPartial;
        public readonly TopicEntry[] Topics;
        public readonly Location DiagnosticLocation;
        public readonly string DiagnosticId;
        public readonly IReadOnlyList<string> DeclaredMemberNames;
        public readonly bool IsStream;

        public static MemberData ForDiagnostic(
            Location location,
            string diagnosticId = "FOXRUN004")
            => new MemberData(
                string.Empty,
                string.Empty,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                false,
                string.Empty,
                0,
                Location.None,
                Array.Empty<TopicEntry>(),
                location,
                diagnosticId,
                null,
                null,
                false);

        public MemberData(
            string ns,
            string className,
            bool partial,
            string memberName,
            string memberKind,
            string memberType,
            string emissionTypeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            int rawMemberOrder,
            Location memberLocation,
            TopicEntry[] topics,
            FoxRunTypeShape typeShape = null,
            IReadOnlyList<string> declaredMemberNames = null,
            bool isStream = false)
            : this(
                ns,
                className,
                partial,
                memberName,
                memberKind,
                memberType,
                emissionTypeName,
                isValueType,
                isArray,
                elementTypeName,
                rawMemberOrder,
                memberLocation,
                topics,
                null,
                string.Empty,
                typeShape,
                declaredMemberNames,
                isStream)
        {
        }

        private MemberData(
            string ns,
            string className,
            bool partial,
            string memberName,
            string memberKind,
            string memberType,
            string emissionTypeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            int rawMemberOrder,
            Location memberLocation,
            TopicEntry[] topics,
            Location diagnosticLocation,
            string diagnosticId,
            FoxRunTypeShape typeShape,
            IReadOnlyList<string> declaredMemberNames,
            bool isStream)
        {
            Ns = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            IsPartial = partial;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            MemberType = memberType ?? string.Empty;
            EmissionTypeName =
                FoxRunEmissionTypeNameFormatter
                    .NormalizeCSharpTypeName(
                        emissionTypeName);
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = elementTypeName ?? string.Empty;
            TypeShape = typeShape;
            RawMemberOrder = rawMemberOrder;
            MemberLocation = memberLocation ?? Location.None;
            Topics = topics ?? Array.Empty<TopicEntry>();
            DiagnosticLocation = diagnosticLocation;
            DiagnosticId = string.IsNullOrEmpty(diagnosticId)
                ? "FOXRUN004"
                : diagnosticId;
            DeclaredMemberNames = declaredMemberNames == null
                ? Array.Empty<string>()
                : new List<string>(declaredMemberNames)
                    .AsReadOnly();
            IsStream = isStream;
        }

        public IReadOnlyList<FoxRunRoslynGenerationMember>
            ToRoslynMembers()
        {
            var result = new List<
                FoxRunRoslynGenerationMember>(Topics.Length);
            AppendRoslynMembers(result);
            return result;
        }

        public void AppendRoslynMembers(
            List<FoxRunRoslynGenerationMember> members)
        {
            if (members == null)
                throw new ArgumentNullException(nameof(members));
            foreach (var topic in Topics)
            {
                members.Add(
                    new FoxRunRoslynGenerationMember(
                        Ns,
                        ClassName,
                        MemberName,
                        MemberKind,
                        MemberType,
                        EmissionTypeName,
                        IsValueType,
                        IsArray,
                        ElementTypeName,
                        topic.Topic,
                        topic.SchemaName,
                        topic.Hz,
                        topic.Policy,
                        topic.Tolerance,
                        RawMemberOrder,
                        string.Empty,
                        topic.OnlyIf,
                        topic.IsAggregateMember,
                        topic.JsonFieldName,
                        topic.Mode,
                        topic.Encoding,
                        topic.ProtobufFieldNumber,
                        TypeShape,
                        topic.GeneratesWebSocketCodec(
                            topic.Mode),
                        topic.NamedArgumentPresence,
                        topic.ConditionMemberKind,
                        IsStream,
                        topic.PublishTransportIds,
                        topic.SubscribeTransportId,
                        topic.Reliability,
                        topic.Durability,
                        topic.History,
                        topic.Depth));
            }
        }
    }

    internal sealed class TopicEntry
    {
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
        public readonly int ProtobufFieldNumber;
        public readonly float Tolerance;
        public readonly string OnlyIf;
        public readonly FoxRunConditionMemberKind ConditionMemberKind;
        public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;
        public readonly bool IsAggregateMember;
        public readonly string JsonFieldName;

        public TopicEntry(
            string topic,
            float hz,
            string schema)
            : this(topic, hz, schema, 1, 0f)
        {
        }

        public TopicEntry(
            string topic,
            float hz,
            string schema,
            int policy,
            float tolerance,
            string onlyIf = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 1,
            int encoding = 0,
            int protobufFieldNumber = 0,
            FoxRunNamedArgumentPresence namedArgumentPresence =
                FoxRunNamedArgumentPresence.None,
            FoxRunConditionMemberKind conditionMemberKind =
                FoxRunConditionMemberKind.None,
            IReadOnlyList<string> publishTransportIds = null,
            string subscribeTransportId = null,
            int reliability = 0,
            int durability = 0,
            int history = 0,
            int depth = 0)
        {
            Topic = topic ?? string.Empty;
            Hz = hz;
            SchemaName = schema ?? string.Empty;
            Policy = policy;
            Mode = mode;
            Encoding = encoding;
            PublishTransportIds = publishTransportIds == null
                ? null
                : Array.AsReadOnly(
                    new List<string>(
                        publishTransportIds)
                        .ToArray());
            SubscribeTransportId = subscribeTransportId;
            Reliability = reliability;
            Durability = durability;
            History = history;
            Depth = depth;
            ProtobufFieldNumber = protobufFieldNumber;
            Tolerance = tolerance;
            OnlyIf = onlyIf ?? string.Empty;
            ConditionMemberKind = conditionMemberKind;
            NamedArgumentPresence = namedArgumentPresence;
            IsAggregateMember = isAggregateMember;
            JsonFieldName = jsonFieldName ?? string.Empty;
        }

        public bool GeneratesWebSocketCodec(int mode)
        {
            var publish = mode == 1 || mode == 3;
            var subscribe = mode == 2 || mode == 3;
            var publishIds = PublishTransportIds;
            var publishWebSocket = publish
                && (publishIds == null
                    || ContainsWebSocket(publishIds));
            var subscribeWebSocket = subscribe
                && (SubscribeTransportId == null
                    || string.Equals(
                        SubscribeTransportId,
                        FoxRunGenerationDescriptorConstants
                            .FoxgloveWebSocketTransportId,
                        StringComparison.Ordinal));
            return publishWebSocket || subscribeWebSocket;
        }

        private static bool ContainsWebSocket(
            IReadOnlyList<string> values)
        {
            foreach (var value in values)
            {
                if (string.Equals(
                        value,
                        FoxRunGenerationDescriptorConstants
                            .FoxgloveWebSocketTransportId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
