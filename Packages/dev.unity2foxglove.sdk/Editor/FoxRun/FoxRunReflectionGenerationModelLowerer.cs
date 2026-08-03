// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Lower reflection members into the Provider-neutral model.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class FoxRunReflectionConditionMemberResolver
    {
        internal static FoxRunConditionMemberKind Resolve(
            Type declaringType,
            string conditionName,
            FoxRunNamedArgumentPresence presence)
        {
            if ((presence & FoxRunNamedArgumentPresence.OnlyIf) == 0)
                return FoxRunConditionMemberKind.None;
            if (declaringType == null
                || string.IsNullOrWhiteSpace(conditionName)
                || !IsCSharpIdentifier(conditionName))
            {
                return FoxRunConditionMemberKind.Missing;
            }

            const BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            for (var candidateType = declaringType;
                 candidateType != null;
                 candidateType = candidateType.BaseType)
            {
                var declared =
                    candidateType.GetMember(conditionName, flags);
                if (declared.Length == 0)
                    continue;
                var declaredOnContainingType =
                    candidateType == declaringType;
                var accessible = declared
                    .Where(
                        member => IsAccessibleFromDerived(
                            member,
                            declaringType,
                            declaredOnContainingType))
                    .ToArray();
                if (accessible.Length == 0)
                    return FoxRunConditionMemberKind.Missing;
                foreach (var member in accessible)
                {
                    switch (member)
                    {
                        case FieldInfo field
                            when field.FieldType == typeof(bool):
                            return FoxRunConditionMemberKind.Field;
                        case PropertyInfo property
                            when property.GetGetMethod(true) != null
                                 && property.PropertyType
                                 == typeof(bool)
                                 && property.GetIndexParameters()
                                     .Length == 0:
                            return FoxRunConditionMemberKind.Property;
                        case MethodInfo method
                            when !method.IsGenericMethodDefinition
                                 && method.ReturnType == typeof(bool)
                                 && method.GetParameters().Length == 0:
                            return FoxRunConditionMemberKind.Method;
                    }
                }

                return FoxRunConditionMemberKind.Invalid;
            }

            return FoxRunConditionMemberKind.Missing;
        }

        private static bool IsAccessibleFromDerived(
            MemberInfo member,
            Type containingType,
            bool declaredOnContainingType)
        {
            if (declaredOnContainingType)
                return true;
            var sameAssembly =
                member?.DeclaringType?.Assembly
                == containingType?.Assembly;
            switch (member)
            {
                case FieldInfo field:
                    return field.IsPublic
                           || field.IsFamily
                           || field.IsFamilyOrAssembly
                           || sameAssembly
                           && (field.IsAssembly
                               || field.IsFamilyAndAssembly);
                case PropertyInfo property:
                    var getter =
                        property.GetGetMethod(nonPublic: true);
                    return getter != null
                           && IsAccessibleFromDerived(
                               getter,
                               containingType,
                               false);
                case MethodBase method:
                    return method.IsPublic
                           || method.IsFamily
                           || method.IsFamilyOrAssembly
                           || sameAssembly
                           && (method.IsAssembly
                               || method.IsFamilyAndAssembly);
                default:
                    return false;
            }
        }

        private static bool IsCSharpIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)
                || !(value[0] == '_' || char.IsLetter(value[0])))
                return false;
            for (var index = 1; index < value.Length; index++)
            {
                if (value[index] != '_'
                    && !char.IsLetterOrDigit(value[index]))
                    return false;
            }

            return true;
        }
    }

    public static class FoxRunReflectionGenerationModelLowerer
    {
        public static FoxRunGenerationModel Lower(
            IReadOnlyList<FoxRunReflectionGenerationMember> members)
        {
            var lowered =
                (members
                 ?? Array.Empty<FoxRunReflectionGenerationMember>())
                .Select(
                    (member, index) =>
                        new FoxRunGenerationMember(
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
                            hostKind: "Reflection",
                            rawMemberOrder:
                                member.RawMemberOrder >= 0
                                    ? member.RawMemberOrder
                                    : index,
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
                .ToList();
            return FoxRunGenerationModel.FromMembers(lowered);
        }
    }

    public sealed class FoxRunReflectionGenerationMember
    {
        public FoxRunReflectionGenerationMember(
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

        public FoxRunReflectionGenerationMember(
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
            : this(
                ns,
                className,
                memberName,
                memberKind,
                rawTypeName,
                FoxRunEmissionTypeNameFormatter
                    .NormalizeCSharpTypeName(rawTypeName),
                isValueType,
                isArray,
                elementTypeName,
                topic,
                schemaName,
                hz,
                policy,
                tolerance,
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
                publishTransportIds,
                subscribeTransportId,
                reliability,
                durability,
                history,
                depth)
        {
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
