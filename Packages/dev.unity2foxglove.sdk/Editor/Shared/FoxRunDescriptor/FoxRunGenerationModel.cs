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
            var types = source
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
        public readonly float RateHz;
        public readonly int PublishMode;
        public readonly string PublishModeName;
        public readonly int Mode;
        public readonly string ModeName;
        public readonly float ChangeEpsilon;
        public readonly float ForceIntervalSeconds;
        public readonly bool HasNonFiniteRateHz;
        public readonly bool HasNonFiniteChangeEpsilon;
        public readonly bool HasNonFiniteForceIntervalSeconds;
        public readonly string HostKind;
        public readonly int RawMemberOrder;
        public readonly string ConditionalSymbols;
        public readonly string When;
        public readonly string Unless;
        public readonly bool IsAggregateMember;
        public readonly string JsonFieldName;

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
            float rateHz,
            string schemaName,
            int publishMode,
            float changeEpsilon,
            float forceIntervalSeconds,
            string hostKind,
            int rawMemberOrder,
            string conditionalSymbols,
            string when = "",
            string unless = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 0)
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
                rateHz,
                schemaName,
                publishMode,
                changeEpsilon,
                forceIntervalSeconds,
                hostKind,
                rawMemberOrder,
                conditionalSymbols,
                when,
                unless,
                isAggregateMember,
                jsonFieldName,
                mode)
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
            float rateHz,
            string schemaName,
            int publishMode,
            float changeEpsilon,
            float forceIntervalSeconds,
            string hostKind,
            int rawMemberOrder,
            string conditionalSymbols,
            string when = "",
            string unless = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 0)
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
                rateHz,
                schemaName,
                publishMode,
                changeEpsilon,
                forceIntervalSeconds,
                hostKind,
                rawMemberOrder,
                conditionalSymbols,
                when,
                unless,
                isAggregateMember,
                jsonFieldName,
                mode)
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
            float rateHz,
            string schemaName,
            int publishMode,
            float changeEpsilon,
            float forceIntervalSeconds,
            string hostKind,
            int rawMemberOrder,
            string conditionalSymbols,
            string when = "",
            string unless = "",
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int mode = 0)
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
            Encoding = FoxRunGenerationDescriptorConstants.JsonEncoding;
            HasNonFiniteRateHz = IsNonFinite(rateHz);
            HasNonFiniteChangeEpsilon = IsNonFinite(changeEpsilon);
            HasNonFiniteForceIntervalSeconds = IsNonFinite(forceIntervalSeconds);
            RateHz = NormalizeRateHz(rateHz);
            PublishMode = publishMode;
            PublishModeName = PublishModeToName(publishMode);
            ChangeEpsilon = NormalizeNonNegative(changeEpsilon);
            ForceIntervalSeconds = NormalizeNonNegative(forceIntervalSeconds);
            Mode = mode;
            ModeName = ModeToName(mode);
            HostKind = hostKind ?? string.Empty;
            RawMemberOrder = rawMemberOrder;
            ConditionalSymbols = conditionalSymbols ?? string.Empty;
            When = when ?? string.Empty;
            Unless = unless ?? string.Empty;
            IsAggregateMember = isAggregateMember;
            JsonFieldName = string.IsNullOrWhiteSpace(jsonFieldName)
                ? DefaultJsonFieldName(MemberName)
                : jsonFieldName;
            CanonicalType = string.IsNullOrEmpty(canonicalType)
                ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(SelectCanonicalSourceType())
                : FoxRunCanonicalTypeNormalizer.NormalizeTypeName(canonicalType);
        }

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
                RateHz,
                SchemaName,
                PublishMode,
                ChangeEpsilon,
                ForceIntervalSeconds,
                When,
                Unless,
                IsAggregateMember,
                JsonFieldName,
                Mode,
                CanonicalType);
        }

        private static string DefaultJsonFieldName(string memberName)
        {
            var name = memberName != null && memberName.StartsWith("@", StringComparison.Ordinal)
                ? memberName.Substring(1)
                : memberName ?? string.Empty;
            return name.TrimStart('_');
        }

        public static float NormalizeRateHz(float rateHz)
        {
            if (IsNonFinite(rateHz) || rateHz <= 0f)
                return 0f;
            return rateHz;
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

        public static string PublishModeToName(int mode)
        {
            switch (mode)
            {
                case 0: return "FixedRate";
                case 1: return "OnChange";
                case 2: return "OnChangeOrInterval";
                case 3: return "OnTrigger";
                default: return "Unknown";
            }
        }

        public static string ModeToName(int mode)
        {
            switch (mode)
            {
                case 0: return "PublishOnly";
                case 1: return "SubscribeOnly";
                case 2: return "PublishAndSubscribe";
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
