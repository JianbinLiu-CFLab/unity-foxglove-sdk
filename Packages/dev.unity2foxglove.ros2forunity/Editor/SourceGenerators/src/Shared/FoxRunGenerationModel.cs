// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Provider-neutral semantic model consumed by FoxRun source emission.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
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
        ProtobufFieldNumber = 1L << 9,
        Reliability = 1L << 11,
        Durability = 1L << 12,
        History = 1L << 13,
        Depth = 1L << 14,
        PublishTransportIds = 1L << 15,
        SubscribeTransportId = 1L << 16
    }

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

    public sealed class FoxRunNormalizedScheduleTuple :
        IEquatable<FoxRunNormalizedScheduleTuple>
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
            Hz = hz > 0f && !float.IsNaN(hz) && !float.IsInfinity(hz)
                ? hz
                : 0f;
            Tolerance =
                tolerance >= 0f
                && !float.IsNaN(tolerance)
                && !float.IsInfinity(tolerance)
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

        public override bool Equals(object obj)
            => Equals(obj as FoxRunNormalizedScheduleTuple);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Policy;
                hash = (hash * 397) ^ HasExplicitHz.GetHashCode();
                hash = (hash * 397) ^ Hz.GetHashCode();
                hash = (hash * 397) ^ Tolerance.GetHashCode();
                hash =
                    (hash * 397)
                    ^ StringComparer.Ordinal.GetHashCode(OnlyIf);
                return (hash * 397) ^ (int)ConditionMemberKind;
            }
        }
    }

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
                : publishUnavailableDiagnosticId
                  ?? unavailableDiagnosticId
                  ?? string.Empty;
            PublishUnavailableReason = publishAvailable
                ? string.Empty
                : publishUnavailableReason
                  ?? unavailableReason
                  ?? string.Empty;
            SubscribeUnavailableDiagnosticId = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableDiagnosticId
                  ?? unavailableDiagnosticId
                  ?? string.Empty;
            SubscribeUnavailableReason = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableReason
                  ?? unavailableReason
                  ?? string.Empty;
        }

        public string Encoding { get; }
        public bool PublishAvailable { get; }
        public bool SubscribeAvailable { get; }
        public string PublishUnavailableDiagnosticId { get; }
        public string PublishUnavailableReason { get; }
        public string SubscribeUnavailableDiagnosticId { get; }
        public string SubscribeUnavailableReason { get; }

        public string UnavailableDiagnosticId
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableDiagnosticId,
                SubscribeAvailable,
                SubscribeUnavailableDiagnosticId);

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
            return string.Equals(
                publishValue,
                subscribeValue,
                StringComparison.Ordinal)
                ? publishValue
                : string.Empty;
        }
    }

    public sealed class FoxRunGenerationModel
    {
        public readonly int DescriptorVersion;
        public readonly string GeneratorVersion;
        public readonly IReadOnlyList<FoxRunGenerationType> Types;

        public FoxRunGenerationModel(
            IReadOnlyList<FoxRunGenerationType> types,
            int descriptorVersion =
                FoxRunGenerationDescriptorConstants.DescriptorVersion,
            string generatorVersion =
                FoxRunGenerationDescriptorConstants.GeneratorVersion)
        {
            DescriptorVersion = descriptorVersion;
            GeneratorVersion = generatorVersion ?? string.Empty;
            Types = CopyTypes(types);
        }

        public static FoxRunGenerationModel FromMembers(
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            var normalized = ApplyMessagePackVariantAvailability(
                members ?? Array.Empty<FoxRunGenerationMember>());
            var types = normalized
                .GroupBy(
                    member => new TypeKey(
                        member.Namespace,
                        member.ClassName))
                .OrderBy(
                    group => group.Key.DeclaringType,
                    StringComparer.Ordinal)
                .Select(
                    group => new FoxRunGenerationType(
                        group.Key.Namespace,
                        group.Key.ClassName,
                        group.ToList()))
                .ToList();
            return new FoxRunGenerationModel(types);
        }

        private static IReadOnlyList<FoxRunGenerationMember>
            ApplyMessagePackVariantAvailability(
                IReadOnlyList<FoxRunGenerationMember> members)
        {
            var replacements =
                new Dictionary<FoxRunGenerationMember, FoxRunGenerationMember>();
            foreach (var topicGroup in members
                         .Where(
                             member =>
                                 member != null
                                 && !string.IsNullOrEmpty(member.Topic))
                         .GroupBy(
                             member =>
                                 member.DeclaringType
                                 + "\n"
                                 + member.Topic,
                             StringComparer.Ordinal))
            {
                var values = topicGroup.ToList();
                var publishing = values
                    .Where(member => member.Mode == 1 || member.Mode == 3)
                    .ToList();
                var subscribing = values
                    .Where(member => member.Mode == 2 || member.Mode == 3)
                    .ToList();
                var streamCount =
                    subscribing.Count(member => member.IsStream);
                var invalidSubscribeTopology =
                    streamCount > 1
                    || (streamCount > 0
                        && subscribing.Count > streamCount);
                var invalidPublishSchedule =
                    HasMixedNormalizedSchedule(publishing);
                var invalidSubscribeSchedule =
                    HasMixedNormalizedSchedule(subscribing);

                foreach (var member in values)
                {
                    replacements[member] =
                        member.WithMessagePackVariantAvailability(
                            invalidPublishSchedule,
                            invalidSubscribeTopology,
                            invalidSubscribeSchedule,
                            !FoxRunMessagePackTypeShapeRules
                                .IsPublishSupported(
                                    member.TypeShape,
                                    member.CanonicalType),
                            !FoxRunMessagePackTypeShapeRules
                                .IsSubscribeSupported(
                                    member.TypeShape,
                                    member.CanonicalType));
                }
            }

            return members
                .Select(
                    member =>
                        replacements.TryGetValue(member, out var replacement)
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

        private static IReadOnlyList<FoxRunGenerationType> CopyTypes(
            IReadOnlyList<FoxRunGenerationType> types)
            => (types ?? Array.Empty<FoxRunGenerationType>())
                .OrderBy(type => type.DeclaringType, StringComparer.Ordinal)
                .Select(
                    type => new FoxRunGenerationType(
                        type.Namespace,
                        type.ClassName,
                        type.Members))
                .ToList()
                .AsReadOnly();

        private readonly struct TypeKey
        {
            public TypeKey(string ns, string className)
            {
                Namespace = ns ?? string.Empty;
                ClassName = className ?? string.Empty;
                DeclaringType = string.IsNullOrEmpty(Namespace)
                    ? ClassName
                    : Namespace + "." + ClassName;
            }

            public readonly string Namespace;
            public readonly string ClassName;
            public readonly string DeclaringType;
        }
    }

    public sealed class FoxRunGenerationType
    {
        public FoxRunGenerationType(
            string ns,
            string className,
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            DeclaringType = string.IsNullOrEmpty(Namespace)
                ? ClassName
                : Namespace + "." + ClassName;
            Members = (members ?? Array.Empty<FoxRunGenerationMember>())
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ThenBy(member => member.SchemaName, StringComparer.Ordinal)
                .ThenBy(member => member.CanonicalType, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string DeclaringType;
        public readonly IReadOnlyList<FoxRunGenerationMember> Members;
    }

    public sealed class FoxRunGenerationMember
    {
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string DeclaringType;
        public readonly string MemberName;
        public readonly string MemberKind;
        public readonly string RawTypeName;
        public readonly string RawObservedTypeName;
        public readonly string EmissionTypeName;
        public readonly string CanonicalType;
        public readonly bool IsValueType;
        public readonly bool IsArray;
        public readonly string ElementTypeName;
        public readonly string Topic;
        public readonly string SchemaName;
        public readonly string Encoding;
        public readonly IReadOnlyList<string> PublishTransportIds;
        public readonly string SubscribeTransportId;
        public readonly string Reliability;
        public readonly string Durability;
        public readonly string History;
        public readonly int Depth;
        public readonly bool GeneratesWebSocketCodec;
        public readonly object ProviderData;
        public readonly FoxRunProtobufMetadata ProtobufMetadata;
        public readonly FoxRunTypeShape TypeShape;
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
        public IReadOnlyList<FoxRunEncodingVariantAvailability>
            EncodingVariants { get; }

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
            string encoding =
                FoxRunGenerationDescriptorConstants.InheritEncoding,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            bool generatesWebSocketCodec = true,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null,
            FoxRunConditionMemberKind conditionMemberKind =
                FoxRunConditionMemberKind.None,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability>
                encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null,
            IReadOnlyList<string> publishTransportIds = null,
            string subscribeTransportId = null,
            string reliability = "inherit",
            string durability = "inherit",
            string history = "inherit",
            int depth = 0,
            object providerData = null)
            : this(
                ns,
                className,
                memberName,
                memberKind,
                rawTypeName,
                rawTypeName,
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
                generatesWebSocketCodec,
                namedArgumentPresence,
                conditionMemberKind,
                isStream,
                encodingVariants,
                normalizedSchedule,
                protobufMetadata,
                publishTransportIds,
                subscribeTransportId,
                reliability,
                durability,
                history,
                depth,
                providerData)
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
            string encoding =
                FoxRunGenerationDescriptorConstants.InheritEncoding,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            bool generatesWebSocketCodec = true,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null,
            FoxRunConditionMemberKind conditionMemberKind =
                FoxRunConditionMemberKind.None,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability>
                encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null,
            IReadOnlyList<string> publishTransportIds = null,
            string subscribeTransportId = null,
            string reliability = "inherit",
            string durability = "inherit",
            string history = "inherit",
            int depth = 0,
            object providerData = null)
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
                generatesWebSocketCodec,
                namedArgumentPresence,
                conditionMemberKind,
                isStream,
                encodingVariants,
                normalizedSchedule,
                protobufMetadata,
                publishTransportIds,
                subscribeTransportId,
                reliability,
                durability,
                history,
                depth,
                providerData)
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
            string encoding =
                FoxRunGenerationDescriptorConstants.InheritEncoding,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            bool generatesWebSocketCodec = true,
            FoxRunNamedArgumentPresence? namedArgumentPresence = null,
            FoxRunConditionMemberKind conditionMemberKind =
                FoxRunConditionMemberKind.None,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability>
                encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null,
            IReadOnlyList<string> publishTransportIds = null,
            string subscribeTransportId = null,
            string reliability = "inherit",
            string durability = "inherit",
            string history = "inherit",
            int depth = 0,
            object providerData = null)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            DeclaringType = string.IsNullOrEmpty(Namespace)
                ? ClassName
                : Namespace + "." + ClassName;
            MemberName = memberName ?? string.Empty;
            MemberKind = NormalizeMemberKind(memberKind);
            RawObservedTypeName = rawObservedTypeName ?? string.Empty;
            RawTypeName = RawObservedTypeName;
            EmissionTypeName =
                FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(
                    emissionTypeName);
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = string.IsNullOrEmpty(elementTypeName)
                ? string.Empty
                : FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(
                    elementTypeName);
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            PublishTransportIds =
                CanonicalTransportIds(publishTransportIds);
            SubscribeTransportId = subscribeTransportId;
            Reliability = reliability ?? "inherit";
            Durability = durability ?? "inherit";
            History = history ?? "inherit";
            Depth = depth;
            GeneratesWebSocketCodec = generatesWebSocketCodec;
            ProviderData = providerData;
            IsStream = isStream;
            TypeShape = typeShape;
            ProtobufMetadata =
                protobufMetadata
                ?? (protobufFieldNumber != 0
                    || string.Equals(
                        Encoding,
                        FoxRunGenerationDescriptorConstants
                            .ProtobufEncoding,
                        StringComparison.Ordinal)
                    || string.Equals(
                        Encoding,
                        FoxRunGenerationDescriptorConstants
                            .InheritEncoding,
                        StringComparison.Ordinal)
                        ? FoxRunProtobufMetadata.FromTypeShape(
                            typeShape,
                            protobufFieldNumber)
                        : null);
            NamedArgumentPresence =
                namedArgumentPresence
                ?? InferNamedArgumentPresence(
                    hz,
                    tolerance,
                    onlyIf,
                    schemaName,
                    policy,
                    mode,
                    encoding,
                    protobufFieldNumber,
                    publishTransportIds,
                    subscribeTransportId,
                    reliability,
                    durability,
                    history,
                    depth);
            DeclaredHz = hz;
            HasExplicitHz =
                HasNamedArgument(FoxRunNamedArgumentPresence.Hz);
            HasNonFiniteHz = IsNonFinite(hz);
            Hz = NormalizeHz(hz);
            Policy = policy;
            PolicyName = PolicyToName(policy);
            DeclaredTolerance = tolerance;
            HasExplicitTolerance =
                HasNamedArgument(FoxRunNamedArgumentPresence.Tolerance);
            HasNonFiniteTolerance = IsNonFinite(tolerance);
            Tolerance = NormalizeNonNegative(tolerance);
            Mode = mode;
            FlowName = FlowToName(mode);
            HostKind = hostKind ?? string.Empty;
            RawMemberOrder = rawMemberOrder;
            ConditionalSymbols = conditionalSymbols ?? string.Empty;
            OnlyIf = onlyIf ?? string.Empty;
            HasExplicitOnlyIf =
                HasNamedArgument(FoxRunNamedArgumentPresence.OnlyIf);
            ConditionMemberKind = NormalizeConditionMemberKind(
                conditionMemberKind,
                OnlyIf,
                HasExplicitOnlyIf);
            IsAggregateMember = isAggregateMember;
            JsonFieldName = string.IsNullOrWhiteSpace(jsonFieldName)
                ? DefaultJsonFieldName(MemberName)
                : jsonFieldName;
            CanonicalType = string.IsNullOrEmpty(canonicalType)
                ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(
                    SelectCanonicalSourceType())
                : FoxRunCanonicalTypeNormalizer.NormalizeTypeName(
                    canonicalType);
            NormalizedSchedule =
                normalizedSchedule
                ?? new FoxRunNormalizedScheduleTuple(
                    Policy,
                    HasExplicitHz,
                    Hz,
                    Tolerance,
                    OnlyIf,
                    ConditionMemberKind);
            EncodingVariants = CopyEncodingVariants(
                encodingVariants
                ?? DefaultEncodingVariants(Encoding, Mode));
        }

        internal FoxRunGenerationMember
            WithMessagePackVariantAvailability(
                bool invalidPublishSchedule,
                bool invalidSubscribeTopology,
                bool invalidSubscribeSchedule,
                bool invalidPublishShape,
                bool invalidSubscribeShape)
        {
            var changed = false;
            var values =
                new List<FoxRunEncodingVariantAvailability>(
                    EncodingVariants.Count);
            foreach (var variant in EncodingVariants)
            {
                if (!string.Equals(
                        variant.Encoding,
                        FoxRunGenerationDescriptorConstants
                            .MessagePackEncoding,
                        StringComparison.Ordinal))
                {
                    values.Add(variant);
                    continue;
                }

                var publishAvailable =
                    variant.PublishAvailable
                    && !invalidPublishSchedule
                    && !invalidPublishShape;
                var subscribeAvailable =
                    variant.SubscribeAvailable
                    && !invalidSubscribeTopology
                    && !invalidSubscribeSchedule
                    && !invalidSubscribeShape;
                var publishId =
                    variant.PublishUnavailableDiagnosticId;
                var publishReason =
                    variant.PublishUnavailableReason;
                if (variant.PublishAvailable && invalidPublishShape)
                {
                    publishId = "FOXRUN616";
                    publishReason =
                        "MessagePack Publish requires a supported bounded readable type shape.";
                }
                else if (
                    variant.PublishAvailable
                    && invalidPublishSchedule)
                {
                    publishId = "FOXRUN619";
                    publishReason =
                        "MessagePack members in one direction must share one normalized schedule.";
                }

                var subscribeId =
                    variant.SubscribeUnavailableDiagnosticId;
                var subscribeReason =
                    variant.SubscribeUnavailableReason;
                if (variant.SubscribeAvailable && invalidSubscribeShape)
                {
                    subscribeId = "FOXRUN616";
                    subscribeReason =
                        "MessagePack Subscribe requires a constructible DTO with writable members.";
                }
                else if (
                    variant.SubscribeAvailable
                    && invalidSubscribeTopology)
                {
                    subscribeId = "FOXRUN618";
                    subscribeReason =
                        "MessagePack subscribe topics must contain only ordinary members or exactly one stream.";
                }
                else if (
                    variant.SubscribeAvailable
                    && invalidSubscribeSchedule)
                {
                    subscribeId = "FOXRUN619";
                    subscribeReason =
                        "MessagePack members in one direction must share one normalized schedule.";
                }

                changed |=
                    publishAvailable != variant.PublishAvailable
                    || subscribeAvailable != variant.SubscribeAvailable
                    || !string.Equals(
                        publishId,
                        variant.PublishUnavailableDiagnosticId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        subscribeId,
                        variant.SubscribeUnavailableDiagnosticId,
                        StringComparison.Ordinal);
                values.Add(
                    new FoxRunEncodingVariantAvailability(
                        variant.Encoding,
                        publishAvailable,
                        subscribeAvailable,
                        publishUnavailableDiagnosticId: publishId,
                        publishUnavailableReason: publishReason,
                        subscribeUnavailableDiagnosticId: subscribeId,
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
                GeneratesWebSocketCodec,
                NamedArgumentPresence,
                ConditionMemberKind,
                IsStream,
                values.AsReadOnly(),
                NormalizedSchedule,
                ProtobufMetadata,
                PublishTransportIds,
                SubscribeTransportId,
                Reliability,
                Durability,
                History,
                Depth,
                ProviderData);
        }

        public bool HasNamedArgument(
            FoxRunNamedArgumentPresence argument)
            => (NamedArgumentPresence & argument) == argument;

#if !FOXRUN_PROVIDER_ANALYZER
        public FoxgloveSourceEmitter.TopicMember ToTopicMember()
            => new FoxgloveSourceEmitter.TopicMember(
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
                protobufFieldNumber: ProtobufMetadata?.FieldNumber ?? 0,
                typeShape: TypeShape,
                generatesWebSocketCodec: GeneratesWebSocketCodec,
                hasExplicitHz: HasExplicitHz,
                conditionMemberKind: ConditionMemberKind,
                namedArgumentPresence: NamedArgumentPresence,
                isStream: IsStream,
                protobufMetadata: ProtobufMetadata,
                publishTransportIds: PublishTransportIds,
                subscribeTransportId: SubscribeTransportId,
                reliability: Reliability,
                durability: Durability,
                history: History,
                depth: Depth,
                providerData: ProviderData);
#endif

        public static string DeclaredEncodingToText(int value)
        {
            switch (value)
            {
                case 0:
                    return FoxRunGenerationDescriptorConstants
                        .InheritEncoding;
                case 1:
                    return FoxRunGenerationDescriptorConstants
                        .ProtobufEncoding;
                case 2:
                    return FoxRunGenerationDescriptorConstants
                        .JsonEncoding;
                case 3:
                    return FoxRunGenerationDescriptorConstants
                        .MessagePackEncoding;
                default:
                    return "invalid:" + value;
            }
        }

        public static string DeclaredReliabilityToText(int value)
            => DeclaredDeliveryAxisToText(
                value,
                "reliable",
                "best-effort");

        public static string DeclaredDurabilityToText(int value)
            => DeclaredDeliveryAxisToText(
                value,
                "volatile",
                "transient-local");

        public static string DeclaredHistoryToText(int value)
            => DeclaredDeliveryAxisToText(
                value,
                "keep-last",
                "keep-all");

        public static string FlowToName(int value)
        {
            switch (value)
            {
                case 1:
                    return "Publish";
                case 2:
                    return "Subscribe";
                case 3:
                    return "PublishAndSubscribe";
                default:
                    return "Invalid";
            }
        }

        public static string ConditionMemberKindToName(
            FoxRunConditionMemberKind value)
            => value.ToString();

        public static string ExplicitArgumentsToText(
            FoxRunNamedArgumentPresence presence)
        {
            var names = new List<string>();
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Hz,
                "Hz");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Tolerance,
                "Tolerance");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.OnlyIf,
                "OnlyIf");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.SchemaName,
                "SchemaName");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Policy,
                "Policy");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Mode,
                "Mode");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Encoding,
                "Encoding");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.ProtobufFieldNumber,
                "ProtobufFieldNumber");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Reliability,
                "Reliability");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Durability,
                "Durability");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.History,
                "History");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.Depth,
                "Depth");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.PublishTransportIds,
                "PublishTransportIds");
            AppendPresenceName(
                names,
                presence,
                FoxRunNamedArgumentPresence.SubscribeTransportId,
                "SubscribeTransportId");
            return string.Join(",", names);
        }

        private static string DeclaredDeliveryAxisToText(
            int value,
            string first,
            string second)
        {
            switch (value)
            {
                case 0:
                    return "inherit";
                case 1:
                    return first;
                case 2:
                    return second;
                case 3:
                    return "system-default";
                default:
                    return "invalid:" + value;
            }
        }

        private static string PolicyToName(int policy)
        {
            switch (policy)
            {
                case 1:
                    return "FixedRate";
                case 2:
                    return "Change";
                case 4:
                    return "Trigger";
                default:
                    return "Invalid";
            }
        }

        private static IReadOnlyList<FoxRunEncodingVariantAvailability>
            DefaultEncodingVariants(string encoding, int mode)
        {
            var publish = mode == 1 || mode == 3;
            var subscribe = mode == 2 || mode == 3;
            var values =
                new List<FoxRunEncodingVariantAvailability>();
            if (string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    StringComparison.Ordinal))
            {
                values.Add(
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants.JsonEncoding,
                        publish,
                        subscribe));
                values.Add(
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants
                            .ProtobufEncoding,
                        publish,
                        subscribe));
                values.Add(
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants
                            .MessagePackEncoding,
                        publish,
                        subscribe));
            }
            else
            {
                values.Add(
                    new FoxRunEncodingVariantAvailability(
                        encoding,
                        publish,
                        subscribe));
            }

            return values.AsReadOnly();
        }

        private static IReadOnlyList<FoxRunEncodingVariantAvailability>
            CopyEncodingVariants(
                IReadOnlyList<FoxRunEncodingVariantAvailability> values)
            => new List<FoxRunEncodingVariantAvailability>(
                values
                ?? Array.Empty<FoxRunEncodingVariantAvailability>())
                .AsReadOnly();

        private string SelectCanonicalSourceType()
            => IsArray && !string.IsNullOrEmpty(ElementTypeName)
                ? ElementTypeName
                : EmissionTypeName;

        private static string DefaultJsonFieldName(string memberName)
        {
            var name =
                memberName != null
                && memberName.StartsWith(
                    "@",
                    StringComparison.Ordinal)
                    ? memberName.Substring(1)
                    : memberName ?? string.Empty;
            return name.TrimStart('_');
        }

        private static string NormalizeMemberKind(string value)
        {
            var kind = (value ?? string.Empty).Trim();
            if (string.Equals(
                    kind,
                    "field",
                    StringComparison.OrdinalIgnoreCase))
                return "field";
            if (string.Equals(
                    kind,
                    "property",
                    StringComparison.OrdinalIgnoreCase))
                return "property";
            return kind;
        }

        private static FoxRunConditionMemberKind
            NormalizeConditionMemberKind(
                FoxRunConditionMemberKind value,
                string onlyIf,
                bool hasExplicitOnlyIf)
        {
            if (!hasExplicitOnlyIf
                || string.IsNullOrWhiteSpace(onlyIf))
                return FoxRunConditionMemberKind.None;
            return value == FoxRunConditionMemberKind.None
                ? FoxRunConditionMemberKind.Unresolved
                : value;
        }

        private static float NormalizeHz(float value)
            => !IsNonFinite(value) && value > 0f ? value : 10f;

        private static float NormalizeNonNegative(float value)
            => IsNonFinite(value) || value < 0f ? 0f : value;

        private static bool IsNonFinite(float value)
            => float.IsNaN(value) || float.IsInfinity(value);

        private static IReadOnlyList<string> CanonicalTransportIds(
            IReadOnlyList<string> values)
        {
            if (values == null)
                return null;
            return values
                .Select(value => value ?? string.Empty)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        private static FoxRunNamedArgumentPresence
            InferNamedArgumentPresence(
                float hz,
                float tolerance,
                string onlyIf,
                string schemaName,
                int policy,
                int mode,
                string encoding,
                int protobufFieldNumber,
                IReadOnlyList<string> publishTransportIds,
                string subscribeTransportId,
                string reliability,
                string durability,
                string history,
                int depth)
        {
            var presence = FoxRunNamedArgumentPresence.None;
            if (hz != -1f)
                presence |= FoxRunNamedArgumentPresence.Hz;
            if (tolerance != 0f)
                presence |= FoxRunNamedArgumentPresence.Tolerance;
            if (!string.IsNullOrEmpty(onlyIf))
                presence |= FoxRunNamedArgumentPresence.OnlyIf;
            if (!string.IsNullOrEmpty(schemaName))
                presence |= FoxRunNamedArgumentPresence.SchemaName;
            if (policy != 1)
                presence |= FoxRunNamedArgumentPresence.Policy;
            if (mode != 1)
                presence |= FoxRunNamedArgumentPresence.Mode;
            if (!string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Encoding;
            if (protobufFieldNumber != 0)
                presence |=
                    FoxRunNamedArgumentPresence.ProtobufFieldNumber;
            if (publishTransportIds != null)
                presence |=
                    FoxRunNamedArgumentPresence.PublishTransportIds;
            if (subscribeTransportId != null)
                presence |=
                    FoxRunNamedArgumentPresence.SubscribeTransportId;
            if (!string.Equals(
                    reliability,
                    "inherit",
                    StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Reliability;
            if (!string.Equals(
                    durability,
                    "inherit",
                    StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.Durability;
            if (!string.Equals(
                    history,
                    "inherit",
                    StringComparison.Ordinal))
                presence |= FoxRunNamedArgumentPresence.History;
            if (depth != 0)
                presence |= FoxRunNamedArgumentPresence.Depth;
            return presence;
        }

        private static void AppendPresenceName(
            ICollection<string> names,
            FoxRunNamedArgumentPresence presence,
            FoxRunNamedArgumentPresence flag,
            string name)
        {
            if ((presence & flag) == flag)
                names.Add(name);
        }
    }
}
