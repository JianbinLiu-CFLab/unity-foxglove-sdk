// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-independent semantic model consumed by FoxRun source emission.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Records which optional <c>[FoxRun]</c> named arguments were written by
    /// the author. Values and presence are separate: explicit zero, empty
    /// string, and invalid enum casts must not collapse into omission.
    /// </summary>
    [Flags]
    public enum FoxRunNamedArgumentPresence : long
    {
        None = 0,
        Hz = 1L << 0,
        Tolerance = 1L << 1,
        OnlyIf = 1L << 2,
        SchemaName = 1L << 3,
        Policy = 1L << 4,
        Mode = 1L << 5,
        Encoding = 1L << 6,
        Source = 1L << 7,
        QoS = 1L << 8,
        ProtobufFieldNumber = 1L << 9,
        Targets = 1L << 10,
        Reliability = 1L << 11,
        Durability = 1L << 12,
        History = 1L << 13,
        Depth = 1L << 14
    }

    /// <summary>
    /// Canonical member shape resolved for an explicit <c>OnlyIf</c>
    /// declaration. Generation hosts must preserve the distinction because a
    /// zero-argument bool method requires invocation while fields and
    /// properties require direct access.
    /// </summary>
    public enum FoxRunConditionMemberKind
    {
        None = 0,
        Field = 1,
        Property = 2,
        Method = 3,
        Missing = 4,
        Invalid = 5,
        Unresolved = 6
    }

    /// <summary>
    /// Canonical schedule semantics used to decide whether multiple members
    /// can share one typed MessagePack topic transaction.
    /// </summary>
    public sealed class FoxRunNormalizedScheduleTuple : IEquatable<FoxRunNormalizedScheduleTuple>
    {
        public FoxRunNormalizedScheduleTuple(
            int policy,
            bool hasExplicitHz,
            float hz,
            float tolerance,
            string onlyIf,
            FoxRunConditionMemberKind conditionMemberKind)
        {
            Policy = policy;
            HasExplicitHz = hasExplicitHz;
            Hz = hz > 0f && !float.IsNaN(hz) && !float.IsInfinity(hz) ? hz : 0f;
            Tolerance = tolerance >= 0f && !float.IsNaN(tolerance) && !float.IsInfinity(tolerance)
                ? tolerance
                : 0f;
            OnlyIf = (onlyIf ?? string.Empty).Trim();
            ConditionMemberKind = conditionMemberKind;
        }

        public int Policy { get; }
        public bool HasExplicitHz { get; }
        public float Hz { get; }
        public float Tolerance { get; }
        public string OnlyIf { get; }
        public FoxRunConditionMemberKind ConditionMemberKind { get; }

        public bool Equals(FoxRunNormalizedScheduleTuple other)
            => other != null
               && Policy == other.Policy
               && HasExplicitHz == other.HasExplicitHz
               && Hz.Equals(other.Hz)
               && Tolerance.Equals(other.Tolerance)
               && string.Equals(OnlyIf, other.OnlyIf, StringComparison.Ordinal)
               && ConditionMemberKind == other.ConditionMemberKind;

        public override bool Equals(object obj) => Equals(obj as FoxRunNormalizedScheduleTuple);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Policy;
                hash = (hash * 397) ^ HasExplicitHz.GetHashCode();
                hash = (hash * 397) ^ Hz.GetHashCode();
                hash = (hash * 397) ^ Tolerance.GetHashCode();
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(OnlyIf);
                hash = (hash * 397) ^ (int)ConditionMemberKind;
                return hash;
            }
        }
    }

    /// <summary>Availability of one encoding variant in each FoxRun direction.</summary>
    public sealed class FoxRunEncodingVariantAvailability
    {
        public FoxRunEncodingVariantAvailability(
            string encoding,
            bool publishAvailable,
            bool subscribeAvailable,
            string unavailableDiagnosticId = "",
            string unavailableReason = "",
            string publishUnavailableDiagnosticId = null,
            string publishUnavailableReason = null,
            string subscribeUnavailableDiagnosticId = null,
            string subscribeUnavailableReason = null)
        {
            Encoding = encoding ?? string.Empty;
            PublishAvailable = publishAvailable;
            SubscribeAvailable = subscribeAvailable;
            PublishUnavailableDiagnosticId = publishAvailable
                ? string.Empty
                : publishUnavailableDiagnosticId ?? unavailableDiagnosticId ?? string.Empty;
            PublishUnavailableReason = publishAvailable
                ? string.Empty
                : publishUnavailableReason ?? unavailableReason ?? string.Empty;
            SubscribeUnavailableDiagnosticId = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableDiagnosticId ?? unavailableDiagnosticId ?? string.Empty;
            SubscribeUnavailableReason = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableReason ?? unavailableReason ?? string.Empty;
        }

        public string Encoding { get; }
        public bool PublishAvailable { get; }
        public bool SubscribeAvailable { get; }
        public string PublishUnavailableDiagnosticId { get; }
        public string PublishUnavailableReason { get; }
        public string SubscribeUnavailableDiagnosticId { get; }
        public string SubscribeUnavailableReason { get; }

        /// <summary>
        /// Compatibility alias for callers that inspect a single unavailable
        /// direction. It is intentionally empty when both directions fail for
        /// different reasons so an ambiguous diagnostic cannot be misreported.
        /// </summary>
        public string UnavailableDiagnosticId
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableDiagnosticId,
                SubscribeAvailable,
                SubscribeUnavailableDiagnosticId);

        /// <summary>Compatibility alias; see <see cref="UnavailableDiagnosticId"/>.</summary>
        public string UnavailableReason
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableReason,
                SubscribeAvailable,
                SubscribeUnavailableReason);

        private static string SharedUnavailableValue(
            bool publishAvailable,
            string publishValue,
            bool subscribeAvailable,
            string subscribeValue)
        {
            if (publishAvailable)
                return subscribeAvailable ? string.Empty : subscribeValue;
            if (subscribeAvailable)
                return publishValue;
            if (string.IsNullOrEmpty(publishValue))
                return subscribeValue;
            if (string.IsNullOrEmpty(subscribeValue))
                return publishValue;
            return string.Equals(publishValue, subscribeValue, StringComparison.Ordinal)
                ? publishValue
                : string.Empty;
        }
    }

    /// <summary>
    /// Host-independent semantic model shared by FoxRun emission, descriptor
    /// evidence, and validation so all hosts report the same contract they emit.
    /// </summary>
    public sealed class FoxRunGenerationModel
    {
        public readonly int DescriptorVersion;
        public readonly string GeneratorVersion;
        public readonly IReadOnlyList<FoxRunGenerationType> Types;

        public FoxRunGenerationModel(
            IReadOnlyList<FoxRunGenerationType> types,
            int descriptorVersion = FoxRunGenerationDescriptorConstants.DescriptorVersion,
            string generatorVersion = FoxRunGenerationDescriptorConstants.GeneratorVersion)
            : this(types, descriptorVersion, generatorVersion, typesAlreadySortedAndCopied: false)
        {
        }

        private FoxRunGenerationModel(
            IReadOnlyList<FoxRunGenerationType> types,
            int descriptorVersion,
            string generatorVersion,
            bool typesAlreadySortedAndCopied)
        {
            DescriptorVersion = descriptorVersion;
            GeneratorVersion = generatorVersion ?? string.Empty;
            Types = typesAlreadySortedAndCopied
                ? ToReadOnlyTypes(types)
                : CopyTypes(types);
        }

        public static FoxRunGenerationModel FromMembers(IReadOnlyList<FoxRunGenerationMember> members)
        {
            var source = members ?? Array.Empty<FoxRunGenerationMember>();
            var normalized = ApplyMessagePackVariantAvailability(source);
            var types = normalized
                .GroupBy(member => new TypeKey(member.Namespace, member.ClassName))
                .OrderBy(group => group.Key.DeclaringType, StringComparer.Ordinal)
                .Select(group => new FoxRunGenerationType(group.Key.Namespace, group.Key.ClassName, group.ToList()))
                .ToList();
            return new FoxRunGenerationModel(
                types,
                FoxRunGenerationDescriptorConstants.DescriptorVersion,
                FoxRunGenerationDescriptorConstants.GeneratorVersion,
                typesAlreadySortedAndCopied: true);
        }

        private static IReadOnlyList<FoxRunGenerationMember> ApplyMessagePackVariantAvailability(
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            var replacements = new Dictionary<FoxRunGenerationMember, FoxRunGenerationMember>();
            foreach (var topicGroup in members
                         .Where(member => member != null && !string.IsNullOrEmpty(member.Topic))
                         .GroupBy(
                             member => member.DeclaringType + "\n" + member.Topic,
                             StringComparer.Ordinal))
            {
                var values = topicGroup.ToList();
                var publishing = values
                    .Where(member => member.Mode == 1 || member.Mode == 3)
                    .ToList();
                var subscribing = values
                    .Where(member => member.Mode == 2 || member.Mode == 3)
                    .ToList();
                var streamCount = subscribing.Count(member => member.IsStream);
                var ordinaryCount = subscribing.Count - streamCount;
                var invalidSubscribeTopology = streamCount > 1
                                               || (streamCount > 0 && ordinaryCount > 0);
                var invalidPublishSchedule = HasMixedNormalizedSchedule(publishing);
                var invalidSubscribeSchedule = HasMixedNormalizedSchedule(subscribing);

                foreach (var member in values)
                {
                    replacements[member] = member.WithMessagePackVariantAvailability(
                        invalidPublishSchedule,
                        invalidSubscribeTopology,
                        invalidSubscribeSchedule,
                        invalidPublishShape:
                            !FoxRunMessagePackTypeShapeRules.IsPublishSupported(
                                member.TypeShape,
                                member.CanonicalType),
                        invalidSubscribeShape:
                            !FoxRunMessagePackTypeShapeRules.IsSubscribeSupported(
                                member.TypeShape,
                                member.CanonicalType));
                }
            }

            return members
                .Select(member => replacements.TryGetValue(member, out var replacement)
                    ? replacement
                    : member)
                .ToList()
                .AsReadOnly();
        }

        private static bool HasMixedNormalizedSchedule(
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            if (members == null || members.Count < 2)
                return false;
            var first = members[0].NormalizedSchedule;
            for (var index = 1; index < members.Count; index++)
            {
                if (!Equals(first, members[index].NormalizedSchedule))
                    return true;
            }
            return false;
        }

        private static IReadOnlyList<FoxRunGenerationType> CopyTypes(IReadOnlyList<FoxRunGenerationType> types)
        {
            // Public FoxRunGenerationType construction already sorts members; this copy preserves that stable order.
            return (types ?? Array.Empty<FoxRunGenerationType>())
                .OrderBy(type => type.DeclaringType, StringComparer.Ordinal)
                .Select(type => new FoxRunGenerationType(type.Namespace, type.ClassName, type.Members, membersAlreadySorted: true))
                .ToList()
                .AsReadOnly();
        }

        private static IReadOnlyList<FoxRunGenerationType> ToReadOnlyTypes(IReadOnlyList<FoxRunGenerationType> types)
        {
            if (types == null)
                return Array.Empty<FoxRunGenerationType>();
            return types is List<FoxRunGenerationType> list
                ? list.AsReadOnly()
                : types.ToList().AsReadOnly();
        }

        private readonly struct TypeKey
        {
            public readonly string Namespace;
            public readonly string ClassName;
            public readonly string DeclaringType;

            public TypeKey(string ns, string className)
            {
                Namespace = ns ?? string.Empty;
                ClassName = className ?? string.Empty;
                DeclaringType = string.IsNullOrEmpty(Namespace) ? ClassName : Namespace + "." + ClassName;
            }
        }
    }

    public sealed class FoxRunGenerationType
    {
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string DeclaringType;
        public readonly IReadOnlyList<FoxRunGenerationMember> Members;

        public FoxRunGenerationType(string ns, string className, IReadOnlyList<FoxRunGenerationMember> members)
            : this(ns, className, members, membersAlreadySorted: false)
        {
        }

        internal FoxRunGenerationType(string ns, string className, IReadOnlyList<FoxRunGenerationMember> members, bool membersAlreadySorted)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            DeclaringType = string.IsNullOrEmpty(Namespace) ? ClassName : Namespace + "." + ClassName;
            Members = membersAlreadySorted
                ? CopyMembers(members)
                : SortMembers(members);
        }

        private static IReadOnlyList<FoxRunGenerationMember> SortMembers(IReadOnlyList<FoxRunGenerationMember> members)
        {
            return (members ?? Array.Empty<FoxRunGenerationMember>())
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ThenBy(member => member.SchemaName, StringComparer.Ordinal)
                .ThenBy(member => member.CanonicalType, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        private static IReadOnlyList<FoxRunGenerationMember> CopyMembers(IReadOnlyList<FoxRunGenerationMember> members)
        {
            if (members == null)
                return Array.Empty<FoxRunGenerationMember>();
            return members is List<FoxRunGenerationMember> list
                ? list.AsReadOnly()
                : members.ToList().AsReadOnly();
        }
    }

    public sealed class FoxRunGenerationMember
    {
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string DeclaringType;
        public readonly string MemberName;
        public readonly string MemberKind;
        public readonly string RawTypeName;
        /// <summary>Host-observed type text retained for provenance and debug evidence only.</summary>
        public readonly string RawObservedTypeName;
        /// <summary>Legal generated C# type expression consumed by the shared emitter.</summary>
        public readonly string EmissionTypeName;
        /// <summary>Canonical schema identity token used for manifests and replay hashes.</summary>
        public readonly string CanonicalType;
        public readonly bool IsValueType;
        public readonly bool IsArray;
        public readonly string ElementTypeName;
        public readonly string Topic;
        public readonly string SchemaName;
        public readonly string Encoding;
        public readonly string Source;
        public readonly string Targets;
        public readonly string QosProfile;
        public readonly string QosReliability;
        public readonly string QosDurability;
        public readonly string QosHistory;
        public readonly int QosDepth;
        public readonly bool GeneratesWebSocketCodec;
        public readonly bool GeneratesRos2NativeRegistration;
        public readonly FoxRunRos2MessageShape Ros2MessageShape;
        /// <summary>
        /// Explicit native contract capability.  Consumers must use this
        /// discriminant rather than inferring a route from a CLR type name.
        /// </summary>
        public readonly FoxRunRos2ContractKind Ros2ContractKind;
        /// <summary>
        /// Host-neutral schema for a project DTO that will later be lowered to
        /// a generated ROS2 interface.  This is deliberately distinct from a
        /// precompiled ros2cs <see cref="FoxRunRos2MessageShape"/>.
        /// </summary>
        public readonly FoxRunRos2CustomDtoShape Ros2CustomDtoShape;
        public readonly FoxRunProtobufMetadata ProtobufMetadata;
        public readonly FoxRunTypeShape TypeShape;
        /// <summary>Raw declaration value retained separately from explicit presence.</summary>
        public readonly float DeclaredHz;
        public readonly bool HasExplicitHz;
        public readonly float Hz;
        public readonly int Policy;
        public readonly string PolicyName;
        public readonly int Mode;
        public readonly string FlowName;
        public readonly float DeclaredTolerance;
        public readonly float Tolerance;
        public readonly bool HasExplicitTolerance;
        public readonly bool HasNonFiniteHz;
        public readonly bool HasNonFiniteTolerance;
        public readonly string HostKind;
        public readonly int RawMemberOrder;
        public readonly string ConditionalSymbols;
        public readonly string OnlyIf;
        public readonly bool HasExplicitOnlyIf;
        public readonly FoxRunConditionMemberKind ConditionMemberKind;
        public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;
        public readonly bool IsAggregateMember;
        public readonly string JsonFieldName;
        public readonly bool IsStream;
        public readonly FoxRunNormalizedScheduleTuple NormalizedSchedule;
        public IReadOnlyList<FoxRunEncodingVariantAvailability> EncodingVariants { get; }

        public FoxRunGenerationMember(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string rawTypeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            float hz,
            string schemaName,
            int policy,
            float tolerance,
            string hostKind,
            int rawMemberOrder,
            string conditionalSymbols,
            string onlyIf = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 1,
            string encoding = FoxRunGenerationDescriptorConstants.InheritEncoding,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            string source = FoxRunGenerationDescriptorConstants.InheritSource,
            string qosProfile = FoxRunGenerationDescriptorConstants.InheritQosProfile,
            bool generatesWebSocketCodec = true,
            bool generatesRos2NativeRegistration = false,
            FoxRunRos2MessageShape ros2MessageShape = null,
            FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null,
            FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None,
            string targets = FoxRunGenerationDescriptorConstants.InheritTargets,
            string qosReliability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosDurability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosHistory = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            int qosDepth = 0,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability> encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null)
            : this(
                ns,
                className,
                memberName,
                memberKind,
                rawTypeName,
                rawTypeName,
                isValueType,
                isArray,
                elementTypeName,
                topic,
                hz,
                schemaName,
                policy,
                tolerance,
                hostKind,
                rawMemberOrder,
                conditionalSymbols,
                onlyIf,
                isAggregateMember,
                jsonFieldName,
                mode,
                encoding,
                protobufFieldNumber,
                typeShape,
                source,
                qosProfile,
                generatesWebSocketCodec,
                generatesRos2NativeRegistration,
                ros2MessageShape,
                ros2CustomDtoShape,
                ros2ContractKind,
                namedArgumentPresence,
                conditionMemberKind,
                targets,
                qosReliability,
                qosDurability,
                qosHistory,
                qosDepth,
                isStream,
                encodingVariants,
                normalizedSchedule,
                protobufMetadata)
        {
        }

        public FoxRunGenerationMember(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string rawObservedTypeName,
            string emissionTypeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            float hz,
            string schemaName,
            int policy,
            float tolerance,
            string hostKind,
            int rawMemberOrder,
            string conditionalSymbols,
            string onlyIf = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 1,
            string encoding = FoxRunGenerationDescriptorConstants.InheritEncoding,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            string source = FoxRunGenerationDescriptorConstants.InheritSource,
            string qosProfile = FoxRunGenerationDescriptorConstants.InheritQosProfile,
            bool generatesWebSocketCodec = true,
            bool generatesRos2NativeRegistration = false,
            FoxRunRos2MessageShape ros2MessageShape = null,
            FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null,
            FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None,
            string targets = FoxRunGenerationDescriptorConstants.InheritTargets,
            string qosReliability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosDurability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosHistory = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            int qosDepth = 0,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability> encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null)
            : this(
                ns,
                className,
                memberName,
                memberKind,
                rawObservedTypeName,
                emissionTypeName,
                null,
                isValueType,
                isArray,
                elementTypeName,
                topic,
                hz,
                schemaName,
                policy,
                tolerance,
                hostKind,
                rawMemberOrder,
                conditionalSymbols,
                onlyIf,
                isAggregateMember,
                jsonFieldName,
                mode,
                encoding,
                protobufFieldNumber,
                typeShape,
                source,
                qosProfile,
                generatesWebSocketCodec,
                generatesRos2NativeRegistration,
                ros2MessageShape,
                ros2CustomDtoShape,
                ros2ContractKind,
                namedArgumentPresence,
                conditionMemberKind,
                targets,
                qosReliability,
                qosDurability,
                qosHistory,
                qosDepth,
                isStream,
                encodingVariants,
                normalizedSchedule,
                protobufMetadata)
        {
        }

        public FoxRunGenerationMember(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string rawObservedTypeName,
            string emissionTypeName,
            string canonicalType,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            float hz,
            string schemaName,
            int policy,
            float tolerance,
            string hostKind,
            int rawMemberOrder,
            string conditionalSymbols,
            string onlyIf = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 1,
            string encoding = FoxRunGenerationDescriptorConstants.InheritEncoding,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            string source = FoxRunGenerationDescriptorConstants.InheritSource,
            string qosProfile = FoxRunGenerationDescriptorConstants.InheritQosProfile,
            bool generatesWebSocketCodec = true,
            bool generatesRos2NativeRegistration = false,
            FoxRunRos2MessageShape ros2MessageShape = null,
            FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null,
            FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None,
            string targets = FoxRunGenerationDescriptorConstants.InheritTargets,
            string qosReliability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosDurability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosHistory = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            int qosDepth = 0,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability> encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            DeclaringType = string.IsNullOrEmpty(Namespace) ? ClassName : Namespace + "." + ClassName;
            MemberName = memberName ?? string.Empty;
            MemberKind = NormalizeMemberKind(memberKind);
            RawObservedTypeName = rawObservedTypeName ?? string.Empty;
            RawTypeName = RawObservedTypeName;
            EmissionTypeName = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(emissionTypeName);
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = string.IsNullOrEmpty(elementTypeName)
                ? string.Empty
                : FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(elementTypeName);
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            Source = source ?? string.Empty;
            Targets = targets ?? string.Empty;
            QosProfile = qosProfile ?? string.Empty;
            QosReliability = qosReliability ?? string.Empty;
            QosDurability = qosDurability ?? string.Empty;
            QosHistory = qosHistory ?? string.Empty;
            QosDepth = qosDepth;
            IsStream = isStream;
            GeneratesWebSocketCodec = generatesWebSocketCodec;
            GeneratesRos2NativeRegistration = generatesRos2NativeRegistration;
            Ros2MessageShape = ros2MessageShape;
            Ros2CustomDtoShape = ros2CustomDtoShape;
            Ros2ContractKind = ResolveRos2ContractKind(
                ros2ContractKind,
                ros2MessageShape,
                ros2CustomDtoShape);
            TypeShape = typeShape;
            ProtobufMetadata = protobufMetadata
                               ?? (protobufFieldNumber != 0
                                   || string.Equals(
                                       Encoding,
                                       FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                                       StringComparison.Ordinal)
                                   || string.Equals(
                                       Encoding,
                                       FoxRunGenerationDescriptorConstants.InheritEncoding,
                                       StringComparison.Ordinal)
                                       ? FoxRunProtobufMetadata.FromTypeShape(
                                           typeShape,
                                           protobufFieldNumber)
                                       : null);
            NamedArgumentPresence = namedArgumentPresence
                ?? InferNamedArgumentPresence(
                    hz,
                    tolerance,
                    onlyIf,
                    schemaName,
                    policy,
                    mode,
                    encoding,
                    source,
                    targets,
                    qosProfile,
                    qosReliability,
                    qosDurability,
                    qosHistory,
                    qosDepth,
                    protobufFieldNumber);
            DeclaredHz = hz;
            HasExplicitHz = HasNamedArgument(FoxRunNamedArgumentPresence.Hz);
            HasNonFiniteHz = IsNonFinite(hz);
            Hz = NormalizeHz(hz);
            Policy = policy;
            PolicyName = PolicyToName(policy);
            DeclaredTolerance = tolerance;
            HasExplicitTolerance = HasNamedArgument(FoxRunNamedArgumentPresence.Tolerance);
            HasNonFiniteTolerance = IsNonFinite(tolerance);
            Tolerance = NormalizeNonNegative(tolerance);
            Mode = mode;
            FlowName = FlowToName(mode);
            HostKind = hostKind ?? string.Empty;
            RawMemberOrder = rawMemberOrder;
            ConditionalSymbols = conditionalSymbols ?? string.Empty;
            OnlyIf = onlyIf ?? string.Empty;
            HasExplicitOnlyIf = HasNamedArgument(FoxRunNamedArgumentPresence.OnlyIf);
            ConditionMemberKind = NormalizeConditionMemberKind(
                conditionMemberKind,
                OnlyIf,
                HasExplicitOnlyIf);
            IsAggregateMember = isAggregateMember;
            JsonFieldName = string.IsNullOrWhiteSpace(jsonFieldName)
                ? DefaultJsonFieldName(MemberName)
                : jsonFieldName;
            CanonicalType = string.IsNullOrEmpty(canonicalType)
                ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(SelectCanonicalSourceType())
                : FoxRunCanonicalTypeNormalizer.NormalizeTypeName(canonicalType);
            NormalizedSchedule = normalizedSchedule ?? new FoxRunNormalizedScheduleTuple(
                Policy,
                HasExplicitHz,
                Hz,
                Tolerance,
                OnlyIf,
                ConditionMemberKind);
            EncodingVariants = CopyEncodingVariants(
                encodingVariants ?? DefaultEncodingVariants(Encoding, Mode));
        }

        internal FoxRunGenerationMember WithMessagePackVariantAvailability(
            bool invalidPublishSchedule,
            bool invalidSubscribeTopology,
            bool invalidSubscribeSchedule,
            bool invalidPublishShape,
            bool invalidSubscribeShape)
        {
            var changed = false;
            var values = new List<FoxRunEncodingVariantAvailability>(EncodingVariants.Count);
            foreach (var variant in EncodingVariants)
            {
                if (!string.Equals(
                        variant.Encoding,
                        FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                        StringComparison.Ordinal))
                {
                    values.Add(variant);
                    continue;
                }

                var publishAvailable = variant.PublishAvailable
                                       && !invalidPublishSchedule
                                       && !invalidPublishShape;
                var subscribeAvailable = variant.SubscribeAvailable
                                         && !invalidSubscribeTopology
                                         && !invalidSubscribeSchedule
                                         && !invalidSubscribeShape;
                var publishDiagnosticId = variant.PublishUnavailableDiagnosticId;
                var publishReason = variant.PublishUnavailableReason;
                if (variant.PublishAvailable && invalidPublishShape)
                {
                    publishDiagnosticId = "FOXRUN616";
                    publishReason = "MessagePack Publish requires a supported bounded readable type shape.";
                }
                else if (variant.PublishAvailable && invalidPublishSchedule)
                {
                    publishDiagnosticId = "FOXRUN619";
                    publishReason =
                        "MessagePack members in one direction must share one normalized schedule.";
                }

                var subscribeDiagnosticId = variant.SubscribeUnavailableDiagnosticId;
                var subscribeReason = variant.SubscribeUnavailableReason;
                if (variant.SubscribeAvailable && invalidSubscribeShape)
                {
                    subscribeDiagnosticId = "FOXRUN616";
                    subscribeReason =
                        "MessagePack Subscribe requires a constructible DTO with writable members.";
                }
                else if (variant.SubscribeAvailable && invalidSubscribeTopology)
                {
                    subscribeDiagnosticId = "FOXRUN618";
                    subscribeReason =
                        "MessagePack subscribe topics must contain only ordinary members or exactly one stream.";
                }
                else if (variant.SubscribeAvailable && invalidSubscribeSchedule)
                {
                    subscribeDiagnosticId = "FOXRUN619";
                    subscribeReason =
                        "MessagePack members in one direction must share one normalized schedule.";
                }

                changed |= publishAvailable != variant.PublishAvailable
                           || subscribeAvailable != variant.SubscribeAvailable
                           || !string.Equals(
                               publishDiagnosticId,
                               variant.PublishUnavailableDiagnosticId,
                               StringComparison.Ordinal)
                           || !string.Equals(
                               publishReason,
                               variant.PublishUnavailableReason,
                               StringComparison.Ordinal)
                           || !string.Equals(
                               subscribeDiagnosticId,
                               variant.SubscribeUnavailableDiagnosticId,
                               StringComparison.Ordinal)
                           || !string.Equals(
                               subscribeReason,
                               variant.SubscribeUnavailableReason,
                               StringComparison.Ordinal);
                values.Add(new FoxRunEncodingVariantAvailability(
                    variant.Encoding,
                    publishAvailable,
                    subscribeAvailable,
                    publishUnavailableDiagnosticId: publishDiagnosticId,
                    publishUnavailableReason: publishReason,
                    subscribeUnavailableDiagnosticId: subscribeDiagnosticId,
                    subscribeUnavailableReason: subscribeReason));
            }

            if (!changed)
                return this;

            return new FoxRunGenerationMember(
                Namespace,
                ClassName,
                MemberName,
                MemberKind,
                RawObservedTypeName,
                EmissionTypeName,
                CanonicalType,
                IsValueType,
                IsArray,
                ElementTypeName,
                Topic,
                DeclaredHz,
                SchemaName,
                Policy,
                DeclaredTolerance,
                HostKind,
                RawMemberOrder,
                ConditionalSymbols,
                OnlyIf,
                IsAggregateMember,
                JsonFieldName,
                Mode,
                Encoding,
                ProtobufMetadata?.FieldNumber ?? 0,
                TypeShape,
                Source,
                QosProfile,
                GeneratesWebSocketCodec,
                GeneratesRos2NativeRegistration,
                Ros2MessageShape,
                Ros2CustomDtoShape,
                Ros2ContractKind,
                NamedArgumentPresence,
                ConditionMemberKind,
                Targets,
                QosReliability,
                QosDurability,
                QosHistory,
                QosDepth,
                IsStream,
                values.AsReadOnly(),
                NormalizedSchedule,
                ProtobufMetadata);
        }

        private static IReadOnlyList<FoxRunEncodingVariantAvailability> DefaultEncodingVariants(
            string encoding,
            int mode)
        {
            var publish = mode == 1 || mode == 3;
            var subscribe = mode == 2 || mode == 3;
            var values = new List<FoxRunEncodingVariantAvailability>();
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, StringComparison.Ordinal))
            {
                values.Add(new FoxRunEncodingVariantAvailability(
                    FoxRunGenerationDescriptorConstants.JsonEncoding, publish, subscribe));
                values.Add(new FoxRunEncodingVariantAvailability(
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding, publish, subscribe));
                values.Add(new FoxRunEncodingVariantAvailability(
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding, publish, subscribe));
            }
            else
            {
                values.Add(new FoxRunEncodingVariantAvailability(encoding, publish, subscribe));
            }
            return values;
        }

        private static IReadOnlyList<FoxRunEncodingVariantAvailability> CopyEncodingVariants(
            IReadOnlyList<FoxRunEncodingVariantAvailability> values)
            => new List<FoxRunEncodingVariantAvailability>(
                values ?? Array.Empty<FoxRunEncodingVariantAvailability>()).AsReadOnly();

        private string SelectCanonicalSourceType()
        {
            if (IsArray && !string.IsNullOrEmpty(ElementTypeName))
                return ElementTypeName;

            return EmissionTypeName;
        }

        public FoxgloveSourceEmitter.TopicMember ToTopicMember()
        {
            return new FoxgloveSourceEmitter.TopicMember(
                MemberName,
                EmissionTypeName,
                Topic,
                Hz,
                SchemaName,
                Policy,
                Tolerance,
                OnlyIf,
                IsAggregateMember,
                JsonFieldName,
                Mode,
                CanonicalType,
                Encoding,
                0,
                TypeShape,
                Source,
                QosProfile,
                GeneratesWebSocketCodec,
                GeneratesRos2NativeRegistration,
                Ros2MessageShape,
                Ros2CustomDtoShape,
                Ros2ContractKind,
                hasExplicitHz: HasExplicitHz,
                conditionMemberKind: ConditionMemberKind,
                namedArgumentPresence: NamedArgumentPresence,
                targets: Targets,
                qosReliability: QosReliability,
                qosDurability: QosDurability,
                qosHistory: QosHistory,
                qosDepth: QosDepth,
                isStream: IsStream,
                protobufMetadata: ProtobufMetadata);
        }

        public bool HasNamedArgument(FoxRunNamedArgumentPresence argument)
            => (NamedArgumentPresence & argument) == argument;

        private static FoxRunRos2ContractKind ResolveRos2ContractKind(
            FoxRunRos2ContractKind declared,
            FoxRunRos2MessageShape packagedShape,
            FoxRunRos2CustomDtoShape customShape)
        {
            if (declared != FoxRunRos2ContractKind.Unsupported)
                return declared;

            // Contract kind identifies the source family.  Shape validity is a
            // separate capability decision: invalid packaged messages must keep
            // their Phase179 diagnostics, while invalid custom DTOs need their
            // custom-schema diagnostics.
            if (packagedShape != null)
            {
                return FoxRunRos2ContractKind.PackagedRos2Message;
            }

            return customShape != null
                ? FoxRunRos2ContractKind.CustomDto
                : FoxRunRos2ContractKind.Unsupported;
        }

        private static string DefaultJsonFieldName(string memberName)
        {
            var name = memberName != null && memberName.StartsWith("@", StringComparison.Ordinal)
                ? memberName.Substring(1)
                : memberName ?? string.Empty;
            return name.TrimStart('_');
        }

        public static string DeclaredEncodingToText(int encoding)
        {
            switch (encoding)
            {
                case 0: return FoxRunGenerationDescriptorConstants.InheritEncoding;
                case 1: return FoxRunGenerationDescriptorConstants.ProtobufEncoding;
                case 2: return FoxRunGenerationDescriptorConstants.JsonEncoding;
                case 3: return FoxRunGenerationDescriptorConstants.MessagePackEncoding;
                default: return string.Empty;
            }
        }

        public static string DeclaredSourceToText(int provider)
        {
            switch (provider)
            {
                case 0: return FoxRunGenerationDescriptorConstants.InheritSource;
                case 1: return FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource;
                case 2: return FoxRunGenerationDescriptorConstants.Ros2NativeSource;
                default: return string.Empty;
            }
        }

        public static string DeclaredTargetsToText(int targets)
        {
            if (targets == 0)
                return FoxRunGenerationDescriptorConstants.InheritTargets;

            const int knownTargets = 1 | 2 | 4;
            if ((targets & ~knownTargets) != 0)
                return string.Empty;

            var values = new List<string>(3);
            if ((targets & 1) != 0)
                values.Add(FoxRunGenerationDescriptorConstants.FoxgloveTarget);
            if ((targets & 2) != 0)
                values.Add(FoxRunGenerationDescriptorConstants.Ros2NativeTarget);
            if ((targets & 4) != 0)
                values.Add(FoxRunGenerationDescriptorConstants.Ros2BridgeTarget);
            return string.Join(",", values);
        }

        public static string DeclaredQosProfileToText(int qos)
        {
            switch (qos)
            {
                case 0: return FoxRunGenerationDescriptorConstants.InheritQosProfile;
                case 1: return FoxRunGenerationDescriptorConstants.DefaultQosProfile;
                case 2: return FoxRunGenerationDescriptorConstants.SensorDataQosProfile;
                case 3: return FoxRunGenerationDescriptorConstants.SystemDefaultQosProfile;
                default: return string.Empty;
            }
        }

        public static string DeclaredQosReliabilityToText(int value)
        {
            switch (value)
            {
                case 0: return FoxRunGenerationDescriptorConstants.InheritQosPolicy;
                case 1: return FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy;
                case 2: return FoxRunGenerationDescriptorConstants.ReliableQosReliability;
                case 3: return FoxRunGenerationDescriptorConstants.BestEffortQosReliability;
                default: return string.Empty;
            }
        }

        public static string DeclaredQosDurabilityToText(int value)
        {
            switch (value)
            {
                case 0: return FoxRunGenerationDescriptorConstants.InheritQosPolicy;
                case 1: return FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy;
                case 2: return FoxRunGenerationDescriptorConstants.VolatileQosDurability;
                case 3: return FoxRunGenerationDescriptorConstants.TransientLocalQosDurability;
                default: return string.Empty;
            }
        }

        public static string DeclaredQosHistoryToText(int value)
        {
            switch (value)
            {
                case 0: return FoxRunGenerationDescriptorConstants.InheritQosPolicy;
                case 1: return FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy;
                case 2: return FoxRunGenerationDescriptorConstants.KeepLastQosHistory;
                case 3: return FoxRunGenerationDescriptorConstants.KeepAllQosHistory;
                default: return string.Empty;
            }
        }

        public static float NormalizeHz(float hz)
        {
            return !IsNonFinite(hz) && hz > 0f ? hz : 10f;
        }

        public static float NormalizeNonNegative(float value)
        {
            if (IsNonFinite(value) || value < 0f)
                return 0f;
            return value;
        }

        private static bool IsNonFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }

        public static string PolicyToName(int policy)
        {
            switch (policy)
            {
                case 1: return "FixedRate";
                case 2: return "Change";
                case 4: return "Trigger";
                default: return "Unknown";
            }
        }

        public static string ConditionMemberKindToName(FoxRunConditionMemberKind kind)
        {
            switch (kind)
            {
                case FoxRunConditionMemberKind.None: return "None";
                case FoxRunConditionMemberKind.Field: return "Field";
                case FoxRunConditionMemberKind.Property: return "Property";
                case FoxRunConditionMemberKind.Method: return "Method";
                case FoxRunConditionMemberKind.Missing: return "Missing";
                case FoxRunConditionMemberKind.Invalid: return "Invalid";
                case FoxRunConditionMemberKind.Unresolved: return "Unresolved";
                default: return "Invalid";
            }
        }

        public static string ExplicitArgumentsToText(FoxRunNamedArgumentPresence presence)
        {
            var names = new List<string>();
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Hz, "Hz");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Tolerance, "Tolerance");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.OnlyIf, "OnlyIf");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.SchemaName, "SchemaName");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Policy, "Policy");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Mode, "Mode");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Encoding, "Encoding");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Source, "Source");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Targets, "Targets");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.QoS, "QoS");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Reliability, "Reliability");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Durability, "Durability");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.History, "History");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.Depth, "Depth");
            AppendPresenceName(names, presence, FoxRunNamedArgumentPresence.ProtobufFieldNumber, "ProtobufFieldNumber");
            return string.Join(",", names);
        }

        private static void AppendPresenceName(
            List<string> names,
            FoxRunNamedArgumentPresence presence,
            FoxRunNamedArgumentPresence value,
            string name)
        {
            if ((presence & value) == value)
                names.Add(name);
        }

        private static FoxRunNamedArgumentPresence InferNamedArgumentPresence(
            float hz,
            float tolerance,
            string onlyIf,
            string schemaName,
            int policy,
            int mode,
            string encoding,
            string source,
            string targets,
            string qosProfile,
            string qosReliability,
            string qosDurability,
            string qosHistory,
            int qosDepth,
            int protobufFieldNumber)
        {
            var presence = FoxRunNamedArgumentPresence.None;
            if (hz > 0f || IsNonFinite(hz)) presence |= FoxRunNamedArgumentPresence.Hz;
            if (tolerance != 0f || IsNonFinite(tolerance)) presence |= FoxRunNamedArgumentPresence.Tolerance;
            if (!string.IsNullOrEmpty(onlyIf)) presence |= FoxRunNamedArgumentPresence.OnlyIf;
            if (!string.IsNullOrEmpty(schemaName)) presence |= FoxRunNamedArgumentPresence.SchemaName;
            if (policy != 1) presence |= FoxRunNamedArgumentPresence.Policy;
            if (mode != 1) presence |= FoxRunNamedArgumentPresence.Mode;
            if (!string.Equals(encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Encoding;
            if (!string.Equals(source, FoxRunGenerationDescriptorConstants.InheritSource, StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Source;
            if (!string.Equals(targets, FoxRunGenerationDescriptorConstants.InheritTargets, StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Targets;
            if (!string.Equals(qosProfile, FoxRunGenerationDescriptorConstants.InheritQosProfile, StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.QoS;
            if (!string.Equals(qosReliability, FoxRunGenerationDescriptorConstants.InheritQosPolicy, StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Reliability;
            if (!string.Equals(qosDurability, FoxRunGenerationDescriptorConstants.InheritQosPolicy, StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Durability;
            if (!string.Equals(qosHistory, FoxRunGenerationDescriptorConstants.InheritQosPolicy, StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.History;
            if (qosDepth != 0) presence |= FoxRunNamedArgumentPresence.Depth;
            if (protobufFieldNumber != 0) presence |= FoxRunNamedArgumentPresence.ProtobufFieldNumber;
            return presence;
        }

        private static FoxRunConditionMemberKind NormalizeConditionMemberKind(
            FoxRunConditionMemberKind declared,
            string onlyIf,
            bool hasExplicitOnlyIf)
        {
            if (declared != FoxRunConditionMemberKind.None)
                return declared;
            if (!hasExplicitOnlyIf)
                return FoxRunConditionMemberKind.None;
            return string.IsNullOrWhiteSpace(onlyIf)
                ? FoxRunConditionMemberKind.Missing
                : FoxRunConditionMemberKind.Unresolved;
        }

        public static string FlowToName(int flow)
        {
            switch (flow)
            {
                case 1: return "Publish";
                case 2: return "Subscribe";
                case 3: return "PublishAndSubscribe";
                default: return "Unknown";
            }
        }

        private static string NormalizeMemberKind(string memberKind)
        {
            var kind = (memberKind ?? string.Empty).Trim();
            if (string.Equals(kind, "field", StringComparison.OrdinalIgnoreCase))
                return "field";
            if (string.Equals(kind, "property", StringComparison.OrdinalIgnoreCase))
                return "property";
            return kind;
        }
    }

}
