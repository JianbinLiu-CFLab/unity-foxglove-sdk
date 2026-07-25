// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Roslyn source-generator data carriers for FoxRun and FoxService emission.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal sealed class ServiceDiagnostic
    {
        public ServiceDiagnostic(string id, Location location, string target)
        {
            Id = id;
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
            Diagnostics = diagnostics ?? Array.Empty<ServiceDiagnostic>();
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
        {
            return new FoxServiceSourceEmitter.ServiceMethod(
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
    }

    /// <summary>
    /// Internal record produced by <c>ExtractMember</c>. Carries namespace, class
    /// name, member identity, topic entries, partial status, and optional
    /// diagnostic location for error reporting.
    /// </summary>
    internal sealed class MemberData
    {
        /// <summary>Containing namespace (empty for global).</summary>
        public readonly string Ns;
        /// <summary>Containing class name.</summary>
        public readonly string ClassName;
        /// <summary>Field or property name.</summary>
        public readonly string MemberName;
        /// <summary>Field or property type as fully-qualified string.</summary>
        public readonly string MemberType;
        public readonly string EmissionTypeName;
        public readonly string MemberKind;
        public readonly bool IsValueType;
        public readonly bool IsArray;
        public readonly string ElementTypeName;
        public readonly FoxRunProtobufTypeShape ProtobufTypeShape;
        public readonly FoxRunRos2MessageShape Ros2MessageShape;
        public readonly FoxRunRos2CustomDtoShape Ros2CustomDtoShape;
        public readonly FoxRunRos2ContractKind Ros2ContractKind;
        public readonly int RawMemberOrder;
        public readonly Location MemberLocation;
        /// <summary>Whether the containing class is declared <c>partial</c>.</summary>
        public readonly bool IsPartial;
        /// <summary>Extracted topic entries from <c>[FoxRun]</c> attributes.</summary>
        public readonly TopicEntry[] Topics;
        /// <summary>Non-null when this represents a diagnostic-only placeholder.</summary>
        public readonly Location DiagnosticLocation;
        public readonly string DiagnosticId;
        public readonly IReadOnlyList<string> DeclaredMemberNames;
        public readonly bool IsStream;

        /// <summary>
        /// Factory for diagnostic-only instances (e.g. multi-variable declaration error).
        /// </summary>
        public static MemberData ForDiagnostic(Location location, string diagnosticId = "FOXRUN004") =>
            new MemberData("", "", false, "", "", "", "", false, false, "", 0, Location.None, Array.Empty<TopicEntry>(), location, diagnosticId);

        /// <summary>
        /// Creates a valid member-data record with no diagnostic.
        /// </summary>
        public MemberData(string ns, string cn, bool partial, string mn, string memberKind, string mt, string emissionTypeName, bool isValueType, bool isArray, string elementTypeName, int rawMemberOrder, Location memberLocation, TopicEntry[] t, FoxRunProtobufTypeShape protobufTypeShape = null, FoxRunRos2MessageShape ros2MessageShape = null, FoxRunRos2CustomDtoShape ros2CustomDtoShape = null, FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported, IReadOnlyList<string> declaredMemberNames = null, bool isStream = false)
            : this(ns, cn, partial, mn, memberKind, mt, emissionTypeName, isValueType, isArray, elementTypeName, rawMemberOrder, memberLocation, t, null, string.Empty, protobufTypeShape, ros2MessageShape, ros2CustomDtoShape, ros2ContractKind, declaredMemberNames, isStream)
        {
        }

        /// <summary>
        /// Core constructor used by both the public constructor and
        /// <c>ForDiagnostic</c>.
        /// </summary>
        private MemberData(string ns, string cn, bool partial, string mn, string memberKind, string mt, string emissionTypeName, bool isValueType, bool isArray, string elementTypeName, int rawMemberOrder, Location memberLocation, TopicEntry[] t, Location diagnosticLocation)
            : this(ns, cn, partial, mn, memberKind, mt, emissionTypeName, isValueType, isArray, elementTypeName, rawMemberOrder, memberLocation, t, diagnosticLocation, string.Empty, null)
        {
        }

        private MemberData(string ns, string cn, bool partial, string mn, string memberKind, string mt, string emissionTypeName, bool isValueType, bool isArray, string elementTypeName, int rawMemberOrder, Location memberLocation, TopicEntry[] t, Location diagnosticLocation, string diagnosticId, FoxRunProtobufTypeShape protobufTypeShape = null, FoxRunRos2MessageShape ros2MessageShape = null, FoxRunRos2CustomDtoShape ros2CustomDtoShape = null, FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported, IReadOnlyList<string> declaredMemberNames = null, bool isStream = false)
        {
            Ns = ns;
            ClassName = cn;
            IsPartial = partial;
            MemberName = mn;
            MemberKind = memberKind;
            MemberType = mt;
            EmissionTypeName = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(emissionTypeName);
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = elementTypeName;
            ProtobufTypeShape = protobufTypeShape;
            Ros2MessageShape = ros2MessageShape;
            Ros2CustomDtoShape = ros2CustomDtoShape;
            Ros2ContractKind = ResolveRos2ContractKind(
                ros2ContractKind,
                ros2MessageShape,
                ros2CustomDtoShape);
            RawMemberOrder = rawMemberOrder;
            MemberLocation = memberLocation;
            Topics = t;
            DiagnosticLocation = diagnosticLocation;
            DiagnosticId = string.IsNullOrEmpty(diagnosticId) ? "FOXRUN004" : diagnosticId;
            DeclaredMemberNames = declaredMemberNames == null
                ? Array.Empty<string>()
                : new List<string>(declaredMemberNames).AsReadOnly();
            IsStream = isStream;
        }

        public IReadOnlyList<FoxRunRoslynGenerationMember> ToRoslynMembers()
        {
            var members = new List<FoxRunRoslynGenerationMember>(Topics.Length);
            AppendRoslynMembers(members);
            return members;
        }

        public void AppendRoslynMembers(List<FoxRunRoslynGenerationMember> members)
        {
            if (members == null)
                throw new ArgumentNullException(nameof(members));

            foreach (var topic in Topics)
                members.Add(ToRoslynMember(topic));
        }

        private FoxRunRoslynGenerationMember ToRoslynMember(TopicEntry topic)
        {
            return new FoxRunRoslynGenerationMember(
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
                ProtobufTypeShape,
                topic.Source,
                topic.QosProfile,
                ProtobufTypeShape != null
                    || FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(
                        FoxRunCanonicalTypeNormalizer.NormalizeTypeName(
                            IsArray && !string.IsNullOrEmpty(ElementTypeName)
                                ? ElementTypeName
                                : EmissionTypeName)),
                FoxRunRos2ContractCapability.IsNativeRegistrationCapable(
                    Ros2MessageShape,
                    Ros2CustomDtoShape),
                Ros2MessageShape,
                Ros2CustomDtoShape,
                Ros2ContractKind,
                topic.NamedArgumentPresence,
                topic.ConditionMemberKind,
                topic.Targets,
                topic.QosReliability,
                topic.QosDurability,
                topic.QosHistory,
                topic.QosDepth,
                IsStream);
        }

        private static FoxRunRos2ContractKind ResolveRos2ContractKind(
            FoxRunRos2ContractKind declared,
            FoxRunRos2MessageShape packagedShape,
            FoxRunRos2CustomDtoShape customShape)
        {
            if (declared != FoxRunRos2ContractKind.Unsupported)
                return declared;

            // This is a family classification, not a readiness predicate.
            // Keeping those concerns separate preserves legacy packaged-message
            // diagnostics and gives unsupported DTOs their custom diagnostics.
            if (packagedShape != null)
            {
                return FoxRunRos2ContractKind.PackagedRos2Message;
            }

            return customShape != null
                ? FoxRunRos2ContractKind.CustomDto
                : FoxRunRos2ContractKind.Unsupported;
        }
    }

    /// <summary>
    /// Immutable tuple representing one <c>[FoxRun]</c> attribute's topic, rate,
    /// and optional schema name.
    /// </summary>
    internal sealed class TopicEntry
    {
        /// <summary>Topic string from the attribute's constructor argument.</summary>
        public readonly string Topic;
        /// <summary>Optional schema name from the attribute's named argument.</summary>
        public readonly string SchemaName;
        /// <summary>Optional update rate in Hz.</summary>
        public readonly float Hz;
        /// <summary>Update policy enum value.</summary>
        public readonly int Policy;
        public readonly int Mode;
        public readonly int Encoding;
        public readonly int Source;
        public readonly int Targets;
        public readonly int QosProfile;
        public readonly int QosReliability;
        public readonly int QosDurability;
        public readonly int QosHistory;
        public readonly int QosDepth;
        public readonly int ProtobufFieldNumber;
        /// <summary>Change tolerance.</summary>
        public readonly float Tolerance;
        public readonly string OnlyIf;
        public readonly FoxRunConditionMemberKind ConditionMemberKind;
        public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;
        public readonly bool IsAggregateMember;
        public readonly string JsonFieldName;

        /// <summary>
        /// Creates a topic entry with the given topic, rate, and schema.
        /// </summary>
        public TopicEntry(string topic, float hz, string schema)
            : this(topic, hz, schema, 1, 0f) { }

        /// <summary>
        /// Creates a topic entry with update policy.
        /// </summary>
        public TopicEntry(string topic, float hz, string schema,
            int policy, float tolerance, string onlyIf = "",
            bool isAggregateMember = false, string jsonFieldName = "", int mode = 1, int encoding = 0, int protobufFieldNumber = 0,
            int source = 0, int qosProfile = 0,
            FoxRunNamedArgumentPresence namedArgumentPresence = FoxRunNamedArgumentPresence.None,
            FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None,
            int targets = 0,
            int qosReliability = 0,
            int qosDurability = 0,
            int qosHistory = 0,
            int qosDepth = 0)
        {
            Topic = topic; Hz = hz; SchemaName = schema;
            Policy = policy;
            Mode = mode;
            Encoding = encoding;
            Source = source;
            Targets = targets;
            QosProfile = qosProfile;
            QosReliability = qosReliability;
            QosDurability = qosDurability;
            QosHistory = qosHistory;
            QosDepth = qosDepth;
            ProtobufFieldNumber = protobufFieldNumber;
            Tolerance = tolerance;
            OnlyIf = onlyIf ?? string.Empty;
            ConditionMemberKind = conditionMemberKind;
            NamedArgumentPresence = namedArgumentPresence;
            IsAggregateMember = isAggregateMember;
            JsonFieldName = jsonFieldName ?? string.Empty;
        }
    }
}
