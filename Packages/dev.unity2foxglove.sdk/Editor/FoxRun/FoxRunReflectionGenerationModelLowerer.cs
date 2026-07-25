// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Lowers reflection-scanned FoxRun members into the shared generation model.

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
            if (declaringType == null || string.IsNullOrWhiteSpace(conditionName))
                return FoxRunConditionMemberKind.Missing;
            if (!IsCSharpIdentifier(conditionName))
                return FoxRunConditionMemberKind.Missing;

            const BindingFlags flags = BindingFlags.Instance
                                       | BindingFlags.Static
                                       | BindingFlags.Public
                                       | BindingFlags.NonPublic
                                       | BindingFlags.DeclaredOnly;
            for (var candidateType = declaringType;
                 candidateType != null;
                 candidateType = candidateType.BaseType)
            {
                var declared = candidateType.GetMember(conditionName, flags);
                if (declared.Length == 0)
                    continue;

                var declaredOnContainingType = candidateType == declaringType;
                var accessible = declared
                    .Where(member => IsAccessibleFromDerived(
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
                        case FieldInfo field when field.FieldType == typeof(bool):
                            return FoxRunConditionMemberKind.Field;
                        case PropertyInfo property
                            when property.GetGetMethod(nonPublic: true) != null
                                 && property.PropertyType == typeof(bool)
                                 && property.GetIndexParameters().Length == 0:
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

            var sameAssembly = member?.DeclaringType?.Assembly == containingType?.Assembly;
            switch (member)
            {
                case FieldInfo field:
                    return field.IsPublic
                           || field.IsFamily
                           || field.IsFamilyOrAssembly
                           || sameAssembly && (field.IsAssembly || field.IsFamilyAndAssembly);
                case PropertyInfo property:
                    var getter = property.GetGetMethod(nonPublic: true);
                    return getter != null
                           && IsAccessibleFromDerived(getter, containingType, false);
                case MethodBase method:
                    return method.IsPublic
                           || method.IsFamily
                           || method.IsFamilyOrAssembly
                           || sameAssembly && (method.IsAssembly || method.IsFamilyAndAssembly);
                default:
                    return false;
            }
        }

        private static bool IsCSharpIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)
                || !(value[0] == '_' || char.IsLetter(value[0])))
            {
                return false;
            }
            for (var index = 1; index < value.Length; index++)
            {
                if (value[index] != '_' && !char.IsLetterOrDigit(value[index]))
                    return false;
            }
            return true;
        }
    }

    public static class FoxRunReflectionGenerationModelLowerer
    {
        public static FoxRunGenerationModel Lower(IReadOnlyList<FoxRunReflectionGenerationMember> members)
        {
            var lowered = (members ?? Array.Empty<FoxRunReflectionGenerationMember>())
                .Select((member, index) => new FoxRunGenerationMember(
                    ns: member.Namespace,
                    className: member.ClassName,
                    memberName: member.MemberName,
                    memberKind: member.MemberKind,
                    rawObservedTypeName: member.RawTypeName,
                    emissionTypeName: member.EmissionTypeName,
                    isValueType: member.IsValueType,
                    isArray: member.IsArray,
                    elementTypeName: member.ElementTypeName,
                    topic: member.Topic,
                    hz: member.Hz,
                    schemaName: member.SchemaName,
                    policy: member.Policy,
                    tolerance: member.Tolerance,
                    hostKind: "Reflection",
                    rawMemberOrder: member.RawMemberOrder >= 0 ? member.RawMemberOrder : index,
                    conditionalSymbols: member.ConditionalSymbols,
                    onlyIf: member.OnlyIf,
                    isAggregateMember: member.IsAggregateMember,
                    jsonFieldName: member.JsonFieldName,
                    mode: member.Mode,
                    encoding: FoxRunGenerationMember.DeclaredEncodingToText(member.Encoding),
                    protobufFieldNumber: member.ProtobufFieldNumber,
                    protobufTypeShape: member.ProtobufTypeShape,
                    source: FoxRunGenerationMember.DeclaredSourceToText(member.Source),
                    qosProfile: FoxRunGenerationMember.DeclaredQosProfileToText(member.QosProfile),
                    generatesWebSocketCodec: member.GeneratesWebSocketCodec,
                    generatesRos2NativeRegistration: member.GeneratesRos2NativeRegistration,
                    ros2MessageShape: member.Ros2MessageShape,
                    ros2CustomDtoShape: member.Ros2CustomDtoShape,
                    ros2ContractKind: member.Ros2ContractKind,
                    namedArgumentPresence: member.NamedArgumentPresence,
                    conditionMemberKind: member.ConditionMemberKind,
                    targets: FoxRunGenerationMember.DeclaredTargetsToText(member.Targets),
                    qosReliability: FoxRunGenerationMember.DeclaredQosReliabilityToText(member.QosReliability),
                    qosDurability: FoxRunGenerationMember.DeclaredQosDurabilityToText(member.QosDurability),
                    qosHistory: FoxRunGenerationMember.DeclaredQosHistoryToText(member.QosHistory),
                    qosDepth: member.QosDepth,
                    isStream: member.IsStream))
                .ToList();
            return FoxRunGenerationModel.FromMembers(lowered);
        }
    }

    public sealed class FoxRunReflectionGenerationMember
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
        public readonly float Hz;
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
        public readonly bool GeneratesWebSocketCodec;
        public readonly bool GeneratesRos2NativeRegistration;
        public readonly FoxRunRos2MessageShape Ros2MessageShape;
        public readonly FoxRunRos2CustomDtoShape Ros2CustomDtoShape;
        public readonly FoxRunRos2ContractKind Ros2ContractKind;
        public readonly int ProtobufFieldNumber;
        public readonly FoxRunProtobufTypeShape ProtobufTypeShape;
        public readonly float Tolerance;
        public readonly int RawMemberOrder;
        public readonly string ConditionalSymbols;
        public readonly string OnlyIf;
        public readonly FoxRunConditionMemberKind ConditionMemberKind;
        public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;
        public readonly bool IsAggregateMember;
        public readonly string JsonFieldName;
        public readonly bool IsStream;

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
            FoxRunProtobufTypeShape protobufTypeShape = null,
            int source = 0,
            int qosProfile = 0,
            bool? generatesWebSocketCodec = null,
            bool? generatesRos2NativeRegistration = null,
            FoxRunRos2MessageShape ros2MessageShape = null,
            FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            FoxRunNamedArgumentPresence namedArgumentPresence = FoxRunNamedArgumentPresence.None,
            FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None,
            int targets = 0,
            int qosReliability = 0,
            int qosDurability = 0,
            int qosHistory = 0,
            int qosDepth = 0,
            bool isStream = false)
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
            Hz = hz;
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
            GeneratesWebSocketCodec = generatesWebSocketCodec
                ?? (protobufTypeShape != null
                    || FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(
                        FoxRunCanonicalTypeNormalizer.NormalizeTypeName(
                            isArray && !string.IsNullOrEmpty(elementTypeName)
                                ? elementTypeName
                                : EmissionTypeName)));
            GeneratesRos2NativeRegistration = generatesRos2NativeRegistration
                ?? FoxRunRos2ContractCapability.IsNativeRegistrationCapable(
                    ros2MessageShape,
                    ros2CustomDtoShape);
            Ros2MessageShape = ros2MessageShape;
            Ros2CustomDtoShape = ros2CustomDtoShape;
            Ros2ContractKind = ros2ContractKind;
            ProtobufFieldNumber = protobufFieldNumber;
            ProtobufTypeShape = protobufTypeShape;
            Tolerance = tolerance;
            RawMemberOrder = rawMemberOrder;
            ConditionalSymbols = conditionalSymbols ?? string.Empty;
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
            FoxRunProtobufTypeShape protobufTypeShape = null,
            int source = 0,
            int qosProfile = 0,
            bool? generatesWebSocketCodec = null,
            bool? generatesRos2NativeRegistration = null,
            FoxRunRos2MessageShape ros2MessageShape = null,
            FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            FoxRunNamedArgumentPresence namedArgumentPresence = FoxRunNamedArgumentPresence.None,
            FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None,
            int targets = 0,
            int qosReliability = 0,
            int qosDurability = 0,
            int qosHistory = 0,
            int qosDepth = 0,
            bool isStream = false)
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
                protobufTypeShape,
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
                isStream)
        {
        }
    }
}
