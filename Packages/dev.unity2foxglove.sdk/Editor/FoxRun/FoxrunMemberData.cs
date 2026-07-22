// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Holds reflection member data used by the FoxRun build-time generator.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class FoxrunCodeGenerator
    {
        /// <summary>
        /// Immutable data record for one <c>[FoxRun]</c>-attributed member, used
        /// during reflection-based scanning and source file generation.
        /// </summary>
        public sealed class MemberData
        {
            /// <summary>Field or property name.</summary>
            public readonly string MemberName;
            /// <summary>Field or property kind.</summary>
            public readonly string MemberKind;
            /// <summary>Field or property type as full-qualified string.</summary>
            public readonly string RawTypeName;
            public readonly string EmissionTypeName;
            /// <summary>Whether the source CLR type is a value type.</summary>
            public readonly bool IsValueType;
            /// <summary>Whether the source CLR type is a supported array/list shape.</summary>
            public readonly bool IsArray;
            /// <summary>Element type for supported array/list source CLR types.</summary>
            public readonly string ElementTypeName;
            /// <summary>Topic string from the attribute.</summary>
            public readonly string Topic;
            /// <summary>Optional schema name.</summary>
            public readonly string SchemaName;
            /// <summary>Containing class name.</summary>
            public readonly string ClassName;
            /// <summary>Containing namespace (empty for global).</summary>
            public readonly string Ns;
            /// <summary>Publishing rate in Hz.</summary>
            public readonly float RateHz;
            /// <summary>Publish mode as int enum value.</summary>
            public readonly int Policy;
            public readonly int Mode;
            public readonly int Encoding;
            public readonly int SubscriptionProvider;
            public readonly int Ros2Qos;
            public readonly FoxRunRos2MessageShape Ros2MessageShape;
            public readonly FoxRunRos2CustomDtoShape Ros2CustomDtoShape;
            public readonly FoxRunRos2ContractKind Ros2ContractKind;
            public readonly int ProtobufFieldNumber;
            public readonly FoxRunProtobufTypeShape ProtobufTypeShape;
            /// <summary>Change epsilon.</summary>
            public readonly float ChangeEpsilon;
            /// <summary>Heartbeat interval seconds.</summary>
            public readonly float ForceIntervalSeconds;
            public readonly int RawMemberOrder;
            public readonly string ConditionalSymbols;
            public readonly string When;
            public readonly string Unless;
            public readonly bool IsAggregateMember;
            public readonly string JsonFieldName;

            /// <summary>
            /// Constructs a <c>MemberData</c> from a reflection <c>Type</c> and
            /// namespace/class context.
            /// </summary>
            public MemberData(string name, Type type, string memberKind, string ns, string cn, string topic, float rate, string schema,
                int policy = 1, float changeEpsilon = 0f, float forceIntervalSeconds = 0f, int rawMemberOrder = -1, string conditionalSymbols = "", string when = "", string unless = "", bool isAggregateMember = false, string jsonFieldName = "", int mode = 1, int encoding = 0, int protobufFieldNumber = 0, int subscriptionProvider = 0, int ros2Qos = 0, FoxRunRos2MessageShape ros2MessageShape = null, FoxRunRos2CustomDtoShape ros2CustomDtoShape = null, FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported)
            {
                MemberName = name;
                MemberKind = memberKind;
                RawTypeName = type.FullName ?? type.Name;
                EmissionTypeName = FoxRunEmissionTypeNameFormatter.FromReflectionType(type);
                IsValueType = type.IsValueType;
                IsArray = TryGetArrayElementType(type, out var elementType);
                ElementTypeName = elementType == null ? "" : elementType.FullName ?? elementType.Name;
                Ns = ns;
                ClassName = cn;
                Topic = topic;
                RateHz = rate;
                SchemaName = schema;
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                SubscriptionProvider = subscriptionProvider;
                Ros2Qos = ros2Qos;
                Ros2MessageShape = ros2MessageShape
                    ?? TryBuildRos2MessageShape(type, subscriptionProvider);
                Ros2CustomDtoShape = ros2CustomDtoShape
                    ?? TryBuildRos2CustomDtoShape(type, Ros2MessageShape, subscriptionProvider);
                Ros2ContractKind = ResolveRos2ContractKind(
                    ros2ContractKind,
                    Ros2MessageShape,
                    Ros2CustomDtoShape);
                ProtobufFieldNumber = protobufFieldNumber;
                ProtobufTypeShape = TryBuildProtobufTypeShape(elementType ?? type);
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
                RawMemberOrder = rawMemberOrder;
                ConditionalSymbols = conditionalSymbols ?? "";
                When = when ?? "";
                Unless = unless ?? "";
                IsAggregateMember = isAggregateMember;
                JsonFieldName = jsonFieldName ?? "";
            }

            /// <summary>
            /// Constructs a <c>MemberData</c> with a raw type string and no
            /// namespace/class context (used in tests or diagnostics).
            /// </summary>
            public MemberData(string name, string rawType, string topic, float rate, string schema,
                int policy = 1, float changeEpsilon = 0f, float forceIntervalSeconds = 0f, int rawMemberOrder = -1, string conditionalSymbols = "", string when = "", string unless = "", bool isAggregateMember = false, string jsonFieldName = "", int mode = 1, int encoding = 0, int protobufFieldNumber = 0, int subscriptionProvider = 0, int ros2Qos = 0, FoxRunRos2MessageShape ros2MessageShape = null, FoxRunRos2CustomDtoShape ros2CustomDtoShape = null, FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported)
            {
                if (LooksLikeArrayType(rawType))
                    throw new ArgumentException("Raw array/list type strings are ambiguous; use the Type-based MemberData constructor.", nameof(rawType));

                MemberName = name;
                MemberKind = "field";
                RawTypeName = rawType;
                EmissionTypeName = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(rawType);
                IsValueType = false;
                IsArray = false;
                ElementTypeName = "";
                Topic = topic;
                RateHz = rate;
                SchemaName = schema;
                Ns = "";
                ClassName = "";
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                SubscriptionProvider = subscriptionProvider;
                Ros2Qos = ros2Qos;
                Ros2MessageShape = ros2MessageShape;
                Ros2CustomDtoShape = ros2CustomDtoShape;
                Ros2ContractKind = ResolveRos2ContractKind(
                    ros2ContractKind,
                    Ros2MessageShape,
                    Ros2CustomDtoShape);
                ProtobufFieldNumber = protobufFieldNumber;
                ProtobufTypeShape = null;
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
                RawMemberOrder = rawMemberOrder;
                ConditionalSymbols = conditionalSymbols ?? "";
                When = when ?? "";
                Unless = unless ?? "";
                IsAggregateMember = isAggregateMember;
                JsonFieldName = jsonFieldName ?? "";
            }

            public FoxRunManifestMember ToManifestMember()
            {
                var model = FoxRunReflectionGenerationModelLowerer.Lower(
                    new[] { ToReflectionMember() });
                if (model.Types.Count != 1 || model.Types[0].Members.Count != 1)
                    throw new InvalidOperationException("Expected one normalized FoxRun member for manifest projection.");
                return FoxRunManifestMember.FromGenerationMember(model.Types[0].Members[0]);
            }

            public FoxRunReflectionGenerationMember ToReflectionMember()
            {
                return new FoxRunReflectionGenerationMember(
                    Ns,
                    ClassName,
                    MemberName,
                    MemberKind,
                    RawTypeName,
                    EmissionTypeName,
                    IsValueType,
                    IsArray,
                    ElementTypeName,
                    Topic,
                    SchemaName,
                    RateHz,
                    Policy,
                    ChangeEpsilon,
                    ForceIntervalSeconds,
                    RawMemberOrder,
                    ConditionalSymbols,
                    When,
                    Unless,
                    IsAggregateMember,
                    JsonFieldName,
                    Mode,
                    Encoding,
                    ProtobufFieldNumber,
                    ProtobufTypeShape,
                    SubscriptionProvider,
                    Ros2Qos,
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
                    Ros2ContractKind);
            }
        }

        private static bool TryGetArrayElementType(Type type, out Type elementType)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IList<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }

        private static bool LooksLikeArrayType(string rawType)
        {
            var text = rawType ?? string.Empty;
            return text.EndsWith("[]", StringComparison.Ordinal)
                   || text.IndexOf("List<", StringComparison.Ordinal) >= 0
                   || text.IndexOf("IList<", StringComparison.Ordinal) >= 0
                   || text.IndexOf("IReadOnlyList<", StringComparison.Ordinal) >= 0;
        }

        private static FoxRunProtobufTypeShape TryBuildProtobufTypeShape(Type type)
        {
            try
            {
                return FoxRunProtobufReflectionTypeShapeBuilder.Build(type);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static FoxRunRos2MessageShape TryBuildRos2MessageShape(Type type, int subscriptionProvider)
        {
            var shape = FoxRunReflectionRos2MessageShapeBuilder.Build(type);
            return shape.ImplementsRos2Message
                   || (subscriptionProvider == 2 && IsTopLevelPackagedRos2MessageCollection(type))
                ? shape
                : null;
        }

        private static FoxRunRos2CustomDtoShape TryBuildRos2CustomDtoShape(
            Type type,
            FoxRunRos2MessageShape packagedShape,
            int subscriptionProvider)
        {
            // Native output is selected at the Manager route.  A custom DTO
            // therefore needs a stable shape even when its subscription
            // provider remains Inherit or WebSocket-only.  Packaged message
            // collections remain a distinct unsupported top-level contract.
            return packagedShape != null
                   || IsTopLevelPackagedRos2MessageCollection(type)
                ? null
                : FoxRunReflectionRos2CustomDtoShapeBuilder.Build(type);
        }

        private static FoxRunRos2ContractKind ResolveRos2ContractKind(
            FoxRunRos2ContractKind declared,
            FoxRunRos2MessageShape packagedShape,
            FoxRunRos2CustomDtoShape customShape)
        {
            if (declared != FoxRunRos2ContractKind.Unsupported)
                return declared;

            // Preserve the source family independently from whether the shape
            // is currently usable for native generation.  The validator owns
            // the corresponding packaged-vs-custom diagnostic family.
            if (packagedShape != null)
            {
                return FoxRunRos2ContractKind.PackagedRos2Message;
            }

            return customShape != null
                ? FoxRunRos2ContractKind.CustomDto
                : FoxRunRos2ContractKind.Unsupported;
        }

        private static bool IsTopLevelPackagedRos2MessageCollection(Type type)
        {
            if (!TryGetArrayElementType(type, out var elementType))
                return false;

            return FoxRunReflectionRos2MessageShapeBuilder.Build(elementType)
                .ImplementsRos2Message;
        }
    }
}
