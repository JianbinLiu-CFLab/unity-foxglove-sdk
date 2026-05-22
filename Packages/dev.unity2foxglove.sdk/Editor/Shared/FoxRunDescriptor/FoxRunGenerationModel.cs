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
    public sealed class FoxRunGenerationModel
    {
        public readonly int DescriptorVersion;
        public readonly string GeneratorVersion;
        public readonly IReadOnlyList<FoxRunGenerationType> Types;

        public FoxRunGenerationModel(
            IReadOnlyList<FoxRunGenerationType> types,
            int descriptorVersion = FoxRunGenerationDescriptorConstants.DescriptorVersion,
            string generatorVersion = FoxRunGenerationDescriptorConstants.GeneratorVersion)
        {
            DescriptorVersion = descriptorVersion;
            GeneratorVersion = generatorVersion ?? string.Empty;
            Types = CopyTypes(types);
        }

        public static FoxRunGenerationModel FromMembers(IReadOnlyList<FoxRunGenerationMember> members)
        {
            var source = members ?? Array.Empty<FoxRunGenerationMember>();
            var types = source
                .GroupBy(member => new TypeKey(member.Namespace, member.ClassName))
                .OrderBy(group => group.Key.DeclaringType, StringComparer.Ordinal)
                .Select(group => new FoxRunGenerationType(group.Key.Namespace, group.Key.ClassName, group.ToList()))
                .ToList();
            return new FoxRunGenerationModel(types);
        }

        private static IReadOnlyList<FoxRunGenerationType> CopyTypes(IReadOnlyList<FoxRunGenerationType> types)
        {
            return (types ?? Array.Empty<FoxRunGenerationType>())
                .OrderBy(type => type.DeclaringType, StringComparer.Ordinal)
                .Select(type => new FoxRunGenerationType(type.Namespace, type.ClassName, type.Members))
                .ToList()
                .AsReadOnly();
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
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            DeclaringType = string.IsNullOrEmpty(Namespace) ? ClassName : Namespace + "." + ClassName;
            Members = (members ?? Array.Empty<FoxRunGenerationMember>())
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ThenBy(member => member.SchemaName, StringComparer.Ordinal)
                .ThenBy(member => member.CanonicalType, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
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
        public readonly string RawObservedTypeName;
        public readonly string EmissionTypeName;
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
        public readonly float ChangeEpsilon;
        public readonly float ForceIntervalSeconds;
        public readonly string HostKind;
        public readonly int RawMemberOrder;
        public readonly string ConditionalSymbols;

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
            string conditionalSymbols)
            : this(
                ns,
                className,
                memberName,
                memberKind,
                rawTypeName,
                FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(rawTypeName),
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
                conditionalSymbols)
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
            string conditionalSymbols)
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
                conditionalSymbols)
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
            string conditionalSymbols)
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
            ElementTypeName = elementTypeName ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = FoxRunGenerationDescriptorConstants.JsonEncoding;
            RateHz = NormalizeRateHz(rateHz);
            PublishMode = publishMode;
            PublishModeName = PublishModeToName(publishMode);
            ChangeEpsilon = NormalizeNonNegative(changeEpsilon);
            ForceIntervalSeconds = NormalizeNonNegative(forceIntervalSeconds);
            HostKind = hostKind ?? string.Empty;
            RawMemberOrder = rawMemberOrder;
            ConditionalSymbols = conditionalSymbols ?? string.Empty;
            CanonicalType = string.IsNullOrEmpty(canonicalType)
                ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(SelectCanonicalSourceType())
                : canonicalType;
        }

        private string SelectCanonicalSourceType()
        {
            if (IsArray && !string.IsNullOrEmpty(ElementTypeName))
                return FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(ElementTypeName);

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
                ForceIntervalSeconds);
        }

        public static float NormalizeRateHz(float rateHz)
        {
            if (float.IsNaN(rateHz) || float.IsInfinity(rateHz) || rateHz <= 0f)
                return 0f;
            return rateHz;
        }

        public static float NormalizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return 0f;
            return value;
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

        private static string NormalizeMemberKind(string memberKind)
        {
            return string.Equals(memberKind, "property", StringComparison.OrdinalIgnoreCase)
                ? "property"
                : "field";
        }
    }
}
