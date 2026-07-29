// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Holds reflection member data used by the FoxRun build-time generator.

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.FoxgloveSDK.Components;

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
            /// <summary>Directional cadence in Hz.</summary>
            public readonly float Hz;
            /// <summary>Publish mode as int enum value.</summary>
            public readonly int Policy;
            public readonly int Mode;
            public readonly int Encoding;
            public readonly int Source;
            public readonly int Targets;
            public readonly IReadOnlyList<string> PublishTransportIds;
            public readonly string SubscribeTransportId;
            public readonly int QosProfile;
            public readonly int QosReliability;
            public readonly int QosDurability;
            public readonly int QosHistory;
            public readonly int QosDepth;
            public readonly FoxRunRos2MessageShape Ros2MessageShape;
            public readonly FoxRunRos2CustomDtoShape Ros2CustomDtoShape;
            public readonly FoxRunRos2ContractKind Ros2ContractKind;
            public readonly int ProtobufFieldNumber;
            public readonly FoxRunTypeShape TypeShape;
            /// <summary>Change tolerance.</summary>
            public readonly float Tolerance;
            public readonly int RawMemberOrder;
            public readonly string ConditionalSymbols;
            public readonly string OnlyIf;
            public readonly FoxRunConditionMemberKind ConditionMemberKind;
            public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;
            public readonly bool IsAggregateMember;
            public readonly string JsonFieldName;
            public readonly bool IsStream;

            /// <summary>
            /// Constructs a <c>MemberData</c> from a reflection <c>Type</c> and
            /// namespace/class context.
            /// </summary>
            public MemberData(string name, Type type, string memberKind, string ns, string cn, string topic, float hz, string schema,
                int policy = 1, float tolerance = 0f, int rawMemberOrder = -1, string conditionalSymbols = "", string onlyIf = "", bool isAggregateMember = false, string jsonFieldName = "", int mode = 1, int encoding = 0, int protobufFieldNumber = 0, int source = 0, int qosProfile = 0, FoxRunRos2MessageShape ros2MessageShape = null, FoxRunRos2CustomDtoShape ros2CustomDtoShape = null, FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported, FoxRunNamedArgumentPresence namedArgumentPresence = FoxRunNamedArgumentPresence.None, FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None, int targets = 0, int qosReliability = 0, int qosDurability = 0, int qosHistory = 0, int qosDepth = 0, IReadOnlyList<string> publishTransportIds = null, string subscribeTransportId = null)
            {
                IsStream = IsFoxRunStreamType(type);
                if (IsStream)
                    type = type.GetGenericArguments()[0];
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
                Hz = hz;
                SchemaName = schema;
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                Source = source;
                Targets = targets;
                PublishTransportIds = publishTransportIds == null
                    ? null
                    : Array.AsReadOnly(new List<string>(publishTransportIds).ToArray());
                SubscribeTransportId = subscribeTransportId;
                QosProfile = qosProfile;
                QosReliability = qosReliability;
                QosDurability = qosDurability;
                QosHistory = qosHistory;
                QosDepth = qosDepth;
                Ros2MessageShape = ros2MessageShape
                    ?? TryBuildRos2MessageShape(type, source);
                Ros2CustomDtoShape = ros2CustomDtoShape
                    ?? TryBuildRos2CustomDtoShape(type, Ros2MessageShape, source);
                Ros2ContractKind = ResolveRos2ContractKind(
                    ros2ContractKind,
                    Ros2MessageShape,
                    Ros2CustomDtoShape);
                ProtobufFieldNumber = protobufFieldNumber;
                TypeShape = TryBuildTypeShape(type);
                Tolerance = tolerance;
                RawMemberOrder = rawMemberOrder;
                ConditionalSymbols = conditionalSymbols ?? "";
                OnlyIf = onlyIf ?? "";
                ConditionMemberKind = conditionMemberKind;
                NamedArgumentPresence = namedArgumentPresence;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = jsonFieldName ?? "";
            }

            /// <summary>
            /// Constructs a <c>MemberData</c> with a raw type string and no
            /// namespace/class context (used in tests or diagnostics).
            /// </summary>
            public MemberData(string name, string rawType, string topic, float hz, string schema,
                int policy = 1, float tolerance = 0f, int rawMemberOrder = -1, string conditionalSymbols = "", string onlyIf = "", bool isAggregateMember = false, string jsonFieldName = "", int mode = 1, int encoding = 0, int protobufFieldNumber = 0, int source = 0, int qosProfile = 0, FoxRunRos2MessageShape ros2MessageShape = null, FoxRunRos2CustomDtoShape ros2CustomDtoShape = null, FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported, FoxRunNamedArgumentPresence namedArgumentPresence = FoxRunNamedArgumentPresence.None, FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None, int targets = 0, int qosReliability = 0, int qosDurability = 0, int qosHistory = 0, int qosDepth = 0, IReadOnlyList<string> publishTransportIds = null, string subscribeTransportId = null)
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
                Hz = hz;
                SchemaName = schema;
                Ns = "";
                ClassName = "";
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                Source = source;
                Targets = targets;
                PublishTransportIds = publishTransportIds == null
                    ? null
                    : Array.AsReadOnly(new List<string>(publishTransportIds).ToArray());
                SubscribeTransportId = subscribeTransportId;
                QosProfile = qosProfile;
                QosReliability = qosReliability;
                QosDurability = qosDurability;
                QosHistory = qosHistory;
                QosDepth = qosDepth;
                Ros2MessageShape = ros2MessageShape;
                Ros2CustomDtoShape = ros2CustomDtoShape;
                Ros2ContractKind = ResolveRos2ContractKind(
                    ros2ContractKind,
                    Ros2MessageShape,
                    Ros2CustomDtoShape);
                ProtobufFieldNumber = protobufFieldNumber;
                TypeShape = null;
                Tolerance = tolerance;
                RawMemberOrder = rawMemberOrder;
                ConditionalSymbols = conditionalSymbols ?? "";
                OnlyIf = onlyIf ?? "";
                ConditionMemberKind = conditionMemberKind;
                NamedArgumentPresence = namedArgumentPresence;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = jsonFieldName ?? "";
                IsStream = false;
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
                    Hz,
                    Policy,
                    Tolerance,
                    RawMemberOrder,
                    ConditionalSymbols,
                    OnlyIf,
                    IsAggregateMember,
                    JsonFieldName,
                    Mode,
                    Encoding,
                    ProtobufFieldNumber,
                    TypeShape,
                    Source,
                    QosProfile,
                    TypeShape != null
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
                    NamedArgumentPresence,
                    ConditionMemberKind,
                    Targets,
                    QosReliability,
                    QosDurability,
                    QosHistory,
                    QosDepth,
                    IsStream,
                    PublishTransportIds,
                    SubscribeTransportId);
            }

            private static bool IsFoxRunStreamType(Type type)
                => type != null
                   && type.IsGenericType
                   && type.GetGenericTypeDefinition() == typeof(FoxRunStream<>);
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

        private static IReadOnlyList<FoxRunAttributeSnapshot> ReadFoxRunAttributeSnapshots(MemberInfo member)
        {
            var result = new List<FoxRunAttributeSnapshot>();
            foreach (var data in CustomAttributeData.GetCustomAttributes(member))
            {
                if (data.AttributeType != typeof(FoxRunAttribute))
                    continue;

                var snapshot = new FoxRunAttributeSnapshot(
                    ReadConstructorString(data, 0),
                    mode: (int)FoxRunFlow.Publish);
                ApplyNamedArguments(data, snapshot);
                result.Add(snapshot);
            }
            return result;
        }

        private static FoxRunMessageAttributeSnapshot ReadFoxRunMessageAttributeSnapshot(Type type)
        {
            foreach (var data in CustomAttributeData.GetCustomAttributes(type))
            {
                if (data.AttributeType != typeof(FoxRunMessageAttribute))
                    continue;

                var snapshot = new FoxRunMessageAttributeSnapshot(
                    ReadConstructorString(data, 0));
                ApplyNamedArguments(data, snapshot);
                return snapshot;
            }
            return null;
        }

        private static FoxRunNamedArgumentPresence ReadFoxRunFieldPresence(MemberInfo member)
        {
            foreach (var data in CustomAttributeData.GetCustomAttributes(member))
            {
                if (data.AttributeType != typeof(FoxRunFieldAttribute))
                    continue;

                foreach (var argument in data.NamedArguments)
                {
                    if (string.Equals(argument.MemberName, "ProtobufFieldNumber", StringComparison.Ordinal))
                        return FoxRunNamedArgumentPresence.ProtobufFieldNumber;
                }
            }
            return FoxRunNamedArgumentPresence.None;
        }

        private static void ApplyNamedArguments(
            CustomAttributeData data,
            FoxRunAttributeSnapshot snapshot)
        {
            foreach (var argument in data.NamedArguments)
            {
                switch (argument.MemberName)
                {
                    case "Hz":
                        snapshot.Hz = Convert.ToSingle(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Hz;
                        break;
                    case "Tolerance":
                        snapshot.Tolerance = Convert.ToSingle(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Tolerance;
                        break;
                    case "OnlyIf":
                        snapshot.OnlyIf = argument.TypedValue.Value as string ?? string.Empty;
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.OnlyIf;
                        break;
                    case "SchemaName":
                        snapshot.SchemaName = argument.TypedValue.Value as string ?? string.Empty;
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.SchemaName;
                        break;
                    case "Policy":
                        snapshot.Policy = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Policy;
                        break;
                    case "Mode":
                        snapshot.Mode = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Mode;
                        break;
                    case "Encoding":
                        snapshot.Encoding = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Encoding;
                        break;
                    case "Source":
                        snapshot.Source = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Source;
                        break;
                    case "Targets":
                        snapshot.Targets = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Targets;
                        break;
                    case "PublishTransportIds":
                        snapshot.PublishTransportIds =
                            ReadStringArray(argument.TypedValue);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.PublishTransportIds;
                        break;
                    case "SubscribeTransportId":
                        snapshot.SubscribeTransportId =
                            argument.TypedValue.Value as string;
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.SubscribeTransportId;
                        break;
                    case "QoS":
                        snapshot.QosProfile = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.QoS;
                        break;
                    case "Reliability":
                        snapshot.QosReliability = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Reliability;
                        break;
                    case "Durability":
                        snapshot.QosDurability = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Durability;
                        break;
                    case "History":
                        snapshot.QosHistory = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.History;
                        break;
                    case "Depth":
                        snapshot.QosDepth = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Depth;
                        break;
                    case "ProtobufFieldNumber":
                        snapshot.ProtobufFieldNumber = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.ProtobufFieldNumber;
                        break;
                }
            }
        }

        private static void ApplyNamedArguments(
            CustomAttributeData data,
            FoxRunMessageAttributeSnapshot snapshot)
        {
            foreach (var argument in data.NamedArguments)
            {
                switch (argument.MemberName)
                {
                    case "Hz":
                        snapshot.Hz = Convert.ToSingle(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Hz;
                        break;
                    case "Tolerance":
                        snapshot.Tolerance = Convert.ToSingle(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Tolerance;
                        break;
                    case "OnlyIf":
                        snapshot.OnlyIf = argument.TypedValue.Value as string ?? string.Empty;
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.OnlyIf;
                        break;
                    case "SchemaName":
                        snapshot.SchemaName = argument.TypedValue.Value as string ?? string.Empty;
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.SchemaName;
                        break;
                    case "Policy":
                        snapshot.Policy = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Policy;
                        break;
                    case "Encoding":
                        snapshot.Encoding = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Encoding;
                        break;
                    case "Targets":
                        snapshot.Targets = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Targets;
                        break;
                    case "PublishTransportIds":
                        snapshot.PublishTransportIds =
                            ReadStringArray(argument.TypedValue);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.PublishTransportIds;
                        break;
                    case "QoS":
                        snapshot.QosProfile = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.QoS;
                        break;
                    case "Reliability":
                        snapshot.QosReliability = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Reliability;
                        break;
                    case "Durability":
                        snapshot.QosDurability = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Durability;
                        break;
                    case "History":
                        snapshot.QosHistory = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.History;
                        break;
                    case "Depth":
                        snapshot.QosDepth = Convert.ToInt32(argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |= FoxRunNamedArgumentPresence.Depth;
                        break;
                }
            }
        }

        private static string ReadConstructorString(CustomAttributeData data, int index)
        {
            return data.ConstructorArguments.Count > index
                ? data.ConstructorArguments[index].Value as string ?? string.Empty
                : string.Empty;
        }

        private static string[] ReadStringArray(
            CustomAttributeTypedArgument argument)
        {
            if (argument.Value == null)
                return null;
            if (!(argument.Value is IList<CustomAttributeTypedArgument> values))
                return Array.Empty<string>();

            var result = new string[values.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = values[index].Value as string;
            return result;
        }

        private sealed class FoxRunAttributeSnapshot
        {
            public readonly string Topic;
            public float Hz = -1f;
            public float Tolerance;
            public string OnlyIf = string.Empty;
            public string SchemaName = string.Empty;
            public int Policy = (int)FoxRunPolicy.FixedRate;
            public int Mode;
            public int Encoding;
            public int Source;
            public int Targets;
            public string[] PublishTransportIds;
            public string SubscribeTransportId;
            public int QosProfile;
            public int QosReliability;
            public int QosDurability;
            public int QosHistory;
            public int QosDepth;
            public int ProtobufFieldNumber;
            public FoxRunNamedArgumentPresence NamedArgumentPresence;

            public FoxRunAttributeSnapshot(string topic, int mode)
            {
                Topic = topic ?? string.Empty;
                Mode = mode;
            }
        }

        private sealed class FoxRunMessageAttributeSnapshot
        {
            public readonly string Topic;
            public float Hz = -1f;
            public float Tolerance;
            public string OnlyIf = string.Empty;
            public string SchemaName = string.Empty;
            public int Policy = (int)FoxRunPolicy.FixedRate;
            public int Encoding;
            public int Targets;
            public string[] PublishTransportIds;
            public int QosProfile;
            public int QosReliability;
            public int QosDurability;
            public int QosHistory;
            public int QosDepth;
            public FoxRunNamedArgumentPresence NamedArgumentPresence;

            public FoxRunMessageAttributeSnapshot(string topic)
            {
                Topic = topic ?? string.Empty;
            }
        }

        private static FoxRunTypeShape TryBuildTypeShape(Type type)
        {
            try
            {
                return FoxRunReflectionTypeShapeBuilder.Build(type);
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

        private static FoxRunRos2MessageShape TryBuildRos2MessageShape(Type type, int source)
        {
            var shape = FoxRunReflectionRos2MessageShapeBuilder.Build(type);
            return shape.ImplementsRos2Message
                   || (source == 2 && IsTopLevelPackagedRos2MessageCollection(type))
                ? shape
                : null;
        }

        private static FoxRunRos2CustomDtoShape TryBuildRos2CustomDtoShape(
            Type type,
            FoxRunRos2MessageShape packagedShape,
            int source)
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
