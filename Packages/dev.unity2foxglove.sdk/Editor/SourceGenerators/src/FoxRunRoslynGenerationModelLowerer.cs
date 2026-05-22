// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Lowers Roslyn-extracted FoxRun declaration data into the shared generation model.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunRoslynGenerationModelLowerer
    {
        public static FoxRunGenerationModel Lower(IReadOnlyList<FoxRunRoslynGenerationMember> members)
        {
            var lowered = (members ?? Array.Empty<FoxRunRoslynGenerationMember>())
                .Select(member => new FoxRunGenerationMember(
                    member.Namespace,
                    member.ClassName,
                    member.MemberName,
                    member.MemberKind,
                    member.RawTypeName,
                    member.EmissionTypeName,
                    member.IsValueType,
                    member.IsArray,
                    member.ElementTypeName,
                    member.Topic,
                    member.RateHz,
                    member.SchemaName,
                    member.PublishMode,
                    member.ChangeEpsilon,
                    member.ForceIntervalSeconds,
                    "Roslyn",
                    member.RawMemberOrder,
                    member.ConditionalSymbols))
                .ToList();
            return FoxRunGenerationModel.FromMembers(lowered);
        }
    }

    internal sealed class FoxRunRoslynGenerationMember
    {
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
        public readonly float RateHz;
        public readonly int PublishMode;
        public readonly float ChangeEpsilon;
        public readonly float ForceIntervalSeconds;
        public readonly int RawMemberOrder;
        public readonly string ConditionalSymbols;

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
            float rateHz,
            int publishMode,
            float changeEpsilon,
            float forceIntervalSeconds,
            int rawMemberOrder,
            string conditionalSymbols)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            RawTypeName = rawTypeName ?? string.Empty;
            EmissionTypeName = string.IsNullOrEmpty(emissionTypeName)
                ? FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(rawTypeName)
                : FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(emissionTypeName);
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = elementTypeName ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            RateHz = rateHz;
            PublishMode = publishMode;
            ChangeEpsilon = changeEpsilon;
            ForceIntervalSeconds = forceIntervalSeconds;
            RawMemberOrder = rawMemberOrder;
            ConditionalSymbols = conditionalSymbols ?? string.Empty;
        }

        public FoxRunRoslynGenerationMember(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string rawTypeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            string schemaName,
            float rateHz,
            int publishMode,
            float changeEpsilon,
            float forceIntervalSeconds,
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
                schemaName,
                rateHz,
                publishMode,
                changeEpsilon,
                forceIntervalSeconds,
                rawMemberOrder,
                conditionalSymbols)
        {
        }
    }
}
