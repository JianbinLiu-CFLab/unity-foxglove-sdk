// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Provider-neutral reflection member data for physical generation.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class FoxrunCodeGenerator
    {
        public sealed class MemberData
        {
            public readonly string MemberName;
            public readonly string MemberKind;
            public readonly string RawTypeName;
            public readonly string EmissionTypeName;
            public readonly bool IsValueType;
            public readonly bool IsArray;
            public readonly string ElementTypeName;
            public readonly string Topic;
            public readonly string SchemaName;
            public readonly string ClassName;
            public readonly string Ns;
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
            public readonly bool GeneratesWebSocketCodec;

            public MemberData(
                string name,
                Type type,
                string memberKind,
                string ns,
                string className,
                string topic,
                float hz,
                string schema,
                int policy = 1,
                float tolerance = 0f,
                int rawMemberOrder = -1,
                string conditionalSymbols = "",
                string onlyIf = "",
                bool isAggregateMember = false,
                string jsonFieldName = "",
                int mode = 1,
                int encoding = 0,
                int protobufFieldNumber = 0,
                FoxRunNamedArgumentPresence namedArgumentPresence =
                    FoxRunNamedArgumentPresence.None,
                FoxRunConditionMemberKind conditionMemberKind =
                    FoxRunConditionMemberKind.None,
                int reliability = 0,
                int durability = 0,
                int history = 0,
                int depth = 0,
                IReadOnlyList<string> publishTransportIds = null,
                string subscribeTransportId = null)
            {
                IsStream = IsFoxRunStreamType(type);
                if (IsStream)
                    type = type.GetGenericArguments()[0];
                MemberName = name ?? string.Empty;
                MemberKind = memberKind ?? "field";
                RawTypeName = type?.FullName ?? type?.Name ?? string.Empty;
                EmissionTypeName =
                    FoxRunEmissionTypeNameFormatter.FromReflectionType(
                        type);
                IsValueType = type?.IsValueType == true;
                IsArray = TryGetArrayElementType(
                    type,
                    out var elementType);
                ElementTypeName =
                    elementType?.FullName
                    ?? elementType?.Name
                    ?? string.Empty;
                Ns = ns ?? string.Empty;
                ClassName = className ?? string.Empty;
                Topic = topic ?? string.Empty;
                Hz = hz;
                SchemaName = schema ?? string.Empty;
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                PublishTransportIds = CopyTransportIds(
                    publishTransportIds);
                SubscribeTransportId = subscribeTransportId;
                Reliability = reliability;
                Durability = durability;
                History = history;
                Depth = depth;
                ProtobufFieldNumber = protobufFieldNumber;
                TypeShape = TryBuildTypeShape(type);
                Tolerance = tolerance;
                RawMemberOrder = rawMemberOrder;
                ConditionalSymbols = conditionalSymbols ?? string.Empty;
                OnlyIf = onlyIf ?? string.Empty;
                ConditionMemberKind = conditionMemberKind;
                NamedArgumentPresence = namedArgumentPresence;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = jsonFieldName ?? string.Empty;
                GeneratesWebSocketCodec =
                    ResolvesAnyDirectionToWebSocket(
                        mode,
                        PublishTransportIds,
                        SubscribeTransportId);
            }

            public MemberData(
                string name,
                string rawType,
                string topic,
                float hz,
                string schema,
                int policy = 1,
                float tolerance = 0f,
                int rawMemberOrder = -1,
                string conditionalSymbols = "",
                string onlyIf = "",
                bool isAggregateMember = false,
                string jsonFieldName = "",
                int mode = 1,
                int encoding = 0,
                int protobufFieldNumber = 0,
                FoxRunNamedArgumentPresence namedArgumentPresence =
                    FoxRunNamedArgumentPresence.None,
                FoxRunConditionMemberKind conditionMemberKind =
                    FoxRunConditionMemberKind.None,
                int reliability = 0,
                int durability = 0,
                int history = 0,
                int depth = 0,
                IReadOnlyList<string> publishTransportIds = null,
                string subscribeTransportId = null)
            {
                if (LooksLikeArrayType(rawType))
                {
                    throw new ArgumentException(
                        "Raw collection type strings are ambiguous; use the Type-based constructor.",
                        nameof(rawType));
                }

                MemberName = name ?? string.Empty;
                MemberKind = "field";
                RawTypeName = rawType ?? string.Empty;
                EmissionTypeName =
                    FoxRunEmissionTypeNameFormatter
                        .NormalizeCSharpTypeName(rawType);
                IsValueType = false;
                IsArray = false;
                ElementTypeName = string.Empty;
                Topic = topic ?? string.Empty;
                Hz = hz;
                SchemaName = schema ?? string.Empty;
                Ns = string.Empty;
                ClassName = string.Empty;
                Policy = policy;
                Mode = mode;
                Encoding = encoding;
                PublishTransportIds = CopyTransportIds(
                    publishTransportIds);
                SubscribeTransportId = subscribeTransportId;
                Reliability = reliability;
                Durability = durability;
                History = history;
                Depth = depth;
                ProtobufFieldNumber = protobufFieldNumber;
                TypeShape = null;
                Tolerance = tolerance;
                RawMemberOrder = rawMemberOrder;
                ConditionalSymbols = conditionalSymbols ?? string.Empty;
                OnlyIf = onlyIf ?? string.Empty;
                ConditionMemberKind = conditionMemberKind;
                NamedArgumentPresence = namedArgumentPresence;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = jsonFieldName ?? string.Empty;
                IsStream = false;
                GeneratesWebSocketCodec =
                    ResolvesAnyDirectionToWebSocket(
                        mode,
                        PublishTransportIds,
                        SubscribeTransportId);
            }

            public FoxRunManifestMember ToManifestMember()
            {
                var model =
                    FoxRunReflectionGenerationModelLowerer.Lower(
                        new[] { ToReflectionMember() });
                if (model.Types.Count != 1
                    || model.Types[0].Members.Count != 1)
                {
                    throw new InvalidOperationException(
                        "Expected one normalized FoxRun member.");
                }

                return FoxRunManifestMember.FromGenerationMember(
                    model.Types[0].Members[0]);
            }

            public FoxRunReflectionGenerationMember ToReflectionMember()
                => new FoxRunReflectionGenerationMember(
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
                    GeneratesWebSocketCodec,
                    NamedArgumentPresence,
                    ConditionMemberKind,
                    IsStream,
                    PublishTransportIds,
                    SubscribeTransportId,
                    Reliability,
                    Durability,
                    History,
                    Depth);

            private static bool IsFoxRunStreamType(Type type)
                => type != null
                   && type.IsGenericType
                   && type.GetGenericTypeDefinition()
                   == typeof(FoxRunStream<>);
        }

        private static bool TryGetArrayElementType(
            Type type,
            out Type elementType)
        {
            if (type != null
                && type.IsArray
                && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (type?.IsGenericType == true)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>)
                    || definition == typeof(IReadOnlyList<>)
                    || definition == typeof(IList<>))
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
                   || text.IndexOf(
                       "List<",
                       StringComparison.Ordinal) >= 0
                   || text.IndexOf(
                       "IList<",
                       StringComparison.Ordinal) >= 0
                   || text.IndexOf(
                       "IReadOnlyList<",
                       StringComparison.Ordinal) >= 0;
        }

        private static IReadOnlyList<FoxRunAttributeSnapshot>
            ReadFoxRunAttributeSnapshots(MemberInfo member)
        {
            var result = new List<FoxRunAttributeSnapshot>();
            foreach (var data in
                     CustomAttributeData.GetCustomAttributes(member))
            {
                if (data.AttributeType != typeof(FoxRunAttribute))
                    continue;
                var snapshot = new FoxRunAttributeSnapshot(
                    ReadConstructorString(data, 0),
                    (int)FoxRunFlow.Publish);
                ApplyNamedArguments(data, snapshot);
                result.Add(snapshot);
            }

            return result;
        }

        private static FoxRunMessageAttributeSnapshot
            ReadFoxRunMessageAttributeSnapshot(Type type)
        {
            foreach (var data in
                     CustomAttributeData.GetCustomAttributes(type))
            {
                if (data.AttributeType
                    != typeof(FoxRunMessageAttribute))
                    continue;
                var snapshot =
                    new FoxRunMessageAttributeSnapshot(
                        ReadConstructorString(data, 0));
                ApplyNamedArguments(data, snapshot);
                return snapshot;
            }

            return null;
        }

        private static FoxRunNamedArgumentPresence
            ReadFoxRunFieldPresence(MemberInfo member)
        {
            foreach (var data in
                     CustomAttributeData.GetCustomAttributes(member))
            {
                if (data.AttributeType
                    != typeof(FoxRunFieldAttribute))
                    continue;
                if (data.NamedArguments.Any(
                        argument => string.Equals(
                            argument.MemberName,
                            "ProtobufFieldNumber",
                            StringComparison.Ordinal)))
                {
                    return FoxRunNamedArgumentPresence
                        .ProtobufFieldNumber;
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
                        snapshot.Hz =
                            Convert.ToSingle(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Hz;
                        break;
                    case "Tolerance":
                        snapshot.Tolerance =
                            Convert.ToSingle(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Tolerance;
                        break;
                    case "OnlyIf":
                        snapshot.OnlyIf =
                            argument.TypedValue.Value as string
                            ?? string.Empty;
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.OnlyIf;
                        break;
                    case "SchemaName":
                        snapshot.SchemaName =
                            argument.TypedValue.Value as string
                            ?? string.Empty;
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.SchemaName;
                        break;
                    case "Policy":
                        snapshot.Policy =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Policy;
                        break;
                    case "Mode":
                        snapshot.Mode =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Mode;
                        break;
                    case "Encoding":
                        snapshot.Encoding =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Encoding;
                        break;
                    case "PublishTransportIds":
                        snapshot.PublishTransportIds =
                            ReadStringArray(argument.TypedValue);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence
                                .PublishTransportIds;
                        break;
                    case "SubscribeTransportId":
                        snapshot.SubscribeTransportId =
                            argument.TypedValue.Value as string;
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence
                                .SubscribeTransportId;
                        break;
                    case "Reliability":
                        snapshot.Reliability =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Reliability;
                        break;
                    case "Durability":
                        snapshot.Durability =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Durability;
                        break;
                    case "History":
                        snapshot.History =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.History;
                        break;
                    case "Depth":
                        snapshot.Depth =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Depth;
                        break;
                    case "ProtobufFieldNumber":
                        snapshot.ProtobufFieldNumber =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence
                                .ProtobufFieldNumber;
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
                        snapshot.Hz =
                            Convert.ToSingle(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Hz;
                        break;
                    case "Tolerance":
                        snapshot.Tolerance =
                            Convert.ToSingle(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Tolerance;
                        break;
                    case "OnlyIf":
                        snapshot.OnlyIf =
                            argument.TypedValue.Value as string
                            ?? string.Empty;
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.OnlyIf;
                        break;
                    case "SchemaName":
                        snapshot.SchemaName =
                            argument.TypedValue.Value as string
                            ?? string.Empty;
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.SchemaName;
                        break;
                    case "Policy":
                        snapshot.Policy =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Policy;
                        break;
                    case "Encoding":
                        snapshot.Encoding =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Encoding;
                        break;
                    case "PublishTransportIds":
                        snapshot.PublishTransportIds =
                            ReadStringArray(argument.TypedValue);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence
                                .PublishTransportIds;
                        break;
                    case "Reliability":
                        snapshot.Reliability =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Reliability;
                        break;
                    case "Durability":
                        snapshot.Durability =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Durability;
                        break;
                    case "History":
                        snapshot.History =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.History;
                        break;
                    case "Depth":
                        snapshot.Depth =
                            Convert.ToInt32(
                                argument.TypedValue.Value);
                        snapshot.NamedArgumentPresence |=
                            FoxRunNamedArgumentPresence.Depth;
                        break;
                }
            }
        }

        private static string ReadConstructorString(
            CustomAttributeData data,
            int index)
            => data.ConstructorArguments.Count > index
                ? data.ConstructorArguments[index].Value as string
                  ?? string.Empty
                : string.Empty;

        private static string[] ReadStringArray(
            CustomAttributeTypedArgument argument)
        {
            if (argument.Value == null)
                return null;
            if (!(argument.Value
                  is IList<CustomAttributeTypedArgument> values))
                return Array.Empty<string>();
            var result = new string[values.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = values[index].Value as string;
            return result;
        }

        private sealed class FoxRunAttributeSnapshot
        {
            public FoxRunAttributeSnapshot(string topic, int mode)
            {
                Topic = topic ?? string.Empty;
                Mode = mode;
            }

            public readonly string Topic;
            public float Hz = -1f;
            public float Tolerance;
            public string OnlyIf = string.Empty;
            public string SchemaName = string.Empty;
            public int Policy = (int)FoxRunPolicy.FixedRate;
            public int Mode;
            public int Encoding;
            public string[] PublishTransportIds;
            public string SubscribeTransportId;
            public int Reliability;
            public int Durability;
            public int History;
            public int Depth;
            public int ProtobufFieldNumber;
            public FoxRunNamedArgumentPresence NamedArgumentPresence;
        }

        private sealed class FoxRunMessageAttributeSnapshot
        {
            public FoxRunMessageAttributeSnapshot(string topic)
            {
                Topic = topic ?? string.Empty;
            }

            public readonly string Topic;
            public float Hz = -1f;
            public float Tolerance;
            public string OnlyIf = string.Empty;
            public string SchemaName = string.Empty;
            public int Policy = (int)FoxRunPolicy.FixedRate;
            public int Encoding;
            public string[] PublishTransportIds;
            public int Reliability;
            public int Durability;
            public int History;
            public int Depth;
            public FoxRunNamedArgumentPresence NamedArgumentPresence;
        }

        private static FoxRunTypeShape TryBuildTypeShape(Type type)
        {
            try
            {
                return type == null
                    ? null
                    : FoxRunReflectionTypeShapeBuilder.Build(type);
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

        private static IReadOnlyList<string> CopyTransportIds(
            IReadOnlyList<string> values)
            => values == null
                ? null
                : Array.AsReadOnly(
                    values
                        .Select(
                            value =>
                                new FoxRunTransportId(value).Value)
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray());

        private static bool ResolvesAnyDirectionToWebSocket(
            int mode,
            IReadOnlyList<string> publishTransportIds,
            string subscribeTransportId)
        {
            var publishes = mode == 1 || mode == 3;
            var subscribes = mode == 2 || mode == 3;
            var publishUsesWebSocket =
                publishes
                && (publishTransportIds == null
                    || publishTransportIds.Contains(
                        FoxgloveWebSocketTransport.Id,
                        StringComparer.Ordinal));
            var subscribeUsesWebSocket =
                subscribes
                && (subscribeTransportId == null
                    || string.Equals(
                        subscribeTransportId,
                        FoxgloveWebSocketTransport.Id,
                        StringComparison.Ordinal));
            return publishUsesWebSocket || subscribeUsesWebSocket;
        }
    }
}
