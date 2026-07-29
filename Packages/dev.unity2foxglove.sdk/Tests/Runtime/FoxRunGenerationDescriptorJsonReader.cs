// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test-only reader for FoxRun generation descriptor JSON equivalence checks.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class FoxRunGenerationDescriptorJsonReader
    {
        public static FoxRunGenerationModel Read(string json)
        {
            var root = JObject.Parse(json ?? throw new ArgumentNullException(nameof(json)));
            var descriptorVersion = IntValue(root, "descriptorVersion");
            var generatorVersion = StringValue(root, "generatorVersion");
            ValidateVersionPair(descriptorVersion, generatorVersion);
            var modernV5 = descriptorVersion >= 5;
            var strictV6 =
                descriptorVersion
                == FoxRunGenerationDescriptorConstants.DescriptorVersion;
            var types = new List<FoxRunGenerationType>();
            foreach (var typeToken in ArrayValue(root, "types", required: modernV5))
            {
                var type = typeToken as JObject
                    ?? throw new InvalidOperationException("FoxRun generation descriptor 'types' entries must be JSON objects.");
                var ns = StringValue(type, "namespace");
                var className = StringValue(type, "className");
                var members = new List<FoxRunGenerationMember>();
                foreach (var memberToken in ArrayValue(type, "members", required: modernV5))
                {
                    var member = memberToken as JObject
                        ?? throw new InvalidOperationException("FoxRun generation descriptor 'members' entries must be JSON objects.");
                    var mode = ModeValue(member);
                    var encoding = StringValue(member, "encoding");
                    var typeShape = modernV5
                        ? TypeShapeValue(RequiredProperty(member, "typeShape"), "typeShape")
                        : null;
                    var encodingVariants = modernV5
                        ? EncodingVariantsValue(RequiredProperty(member, "encodingVariants"))
                        : LegacyEncodingVariants(encoding, mode);
                    if (modernV5
                        && typeShape == null
                        && ContainsMessagePackVariant(encodingVariants))
                    {
                        throw new InvalidOperationException(
                            "FoxRun generation descriptor v5 requires a non-null typeShape when a MessagePack variant is present.");
                    }
                    var protobufMetadata = modernV5
                        ? ProtobufMetadataValue(RequiredProperty(member, "protobuf"))
                        : null;
                    var normalizedSchedule = modernV5
                        ? NormalizedScheduleValue(RequiredProperty(member, "normalizedSchedule"))
                        : null;
                    members.Add(new FoxRunGenerationMember(
                        ns,
                        className,
                        StringValue(member, "memberName"),
                        StringValue(member, "memberKind"),
                        StringValue(member, "rawTypeName"),
                        StringValue(member, "emissionTypeName"),
                        StringValue(member, "canonicalType"),
                        isValueType: BoolValue(member, "isValueType"),
                        isArray: BoolValue(member, "isArray"),
                        elementTypeName: StringValue(member, "elementTypeName"),
                        topic: StringValue(member, "topic"),
                        hz: FloatValue(member, "hz"),
                        schemaName: StringValue(member, "schemaName"),
                        policy: PolicyValue(member),
                        tolerance: FloatValue(member, "tolerance"),
                        hostKind: StringValue(member, "hostKind"),
                        rawMemberOrder: IntValue(member, "rawMemberOrder"),
                        conditionalSymbols: StringValue(member, "conditionalSymbols"),
                        onlyIf: StringValue(member, "onlyIf"),
                        isAggregateMember: BoolValue(member, "isAggregateMember"),
                        jsonFieldName: StringValue(member, "jsonFieldName"),
                        mode: mode,
                        encoding: encoding,
                        protobufFieldNumber: modernV5
                            ? protobufMetadata?.FieldNumber ?? 0
                            : IntValue(member, "protobufFieldNumber"),
                        typeShape: typeShape,
                        source: StringValue(member, "source"),
                        qosProfile: StringValue(member, "qosProfile"),
                        targets: StringValue(member, "targets"),
                        qosReliability: StringValue(member, "qosReliability"),
                        qosDurability: StringValue(member, "qosDurability"),
                        qosHistory: StringValue(member, "qosHistory"),
                        qosDepth: IntValue(member, "qosDepth"),
                        generatesWebSocketCodec: BoolValue(member, "generatesWebSocketCodec"),
                        generatesRos2NativeRegistration: BoolValue(member, "generatesRos2NativeRegistration"),
                        ros2MessageShape: Ros2MessageShapeValue(member),
                        namedArgumentPresence: ExplicitArgumentsValue(member),
                        conditionMemberKind: ConditionMemberKindValue(member),
                        isStream: BoolValue(member, "isStream"),
                        encodingVariants: encodingVariants,
                        normalizedSchedule: normalizedSchedule,
                        protobufMetadata: protobufMetadata,
                        publishTransportIds: strictV6
                            ? NullableStringArrayValue(
                                member,
                                "publishTransportIds")
                            : null,
                        subscribeTransportId: strictV6
                            ? NullableStringValue(
                                member,
                                "subscribeTransportId")
                            : null));
                }
                types.Add(new FoxRunGenerationType(ns, className, members));
            }

            return new FoxRunGenerationModel(
                types,
                descriptorVersion,
                generatorVersion);
        }

        private static void ValidateVersionPair(int descriptorVersion, string generatorVersion)
        {
            if (descriptorVersion == 4
                && string.Equals(generatorVersion, "4.0.0", StringComparison.Ordinal))
            {
                return;
            }

            if (descriptorVersion == 5
                && string.Equals(
                    generatorVersion,
                    "5.0.0",
                    StringComparison.Ordinal))
            {
                return;
            }

            if (descriptorVersion == FoxRunGenerationDescriptorConstants.DescriptorVersion
                && string.Equals(
                    generatorVersion,
                    FoxRunGenerationDescriptorConstants.GeneratorVersion,
                    StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                "Unsupported FoxRun generation descriptor version pair: "
                + descriptorVersion
                + "/"
                + (generatorVersion ?? string.Empty)
                + ".");
        }

        private static JArray ArrayValue(JObject obj, string name, bool required)
        {
            if (!obj.TryGetValue(name, out var token))
            {
                if (!required)
                    return new JArray();
                throw new InvalidOperationException(
                    "FoxRun generation descriptor v5 requires '" + name + "'.");
            }

            return token as JArray
                   ?? throw new InvalidOperationException(
                       "FoxRun generation descriptor '" + name + "' must be a JSON array.");
        }

        private static IReadOnlyList<string> NullableStringArrayValue(
            JObject obj,
            string name)
        {
            var token = RequiredProperty(obj, name);
            if (token.Type == JTokenType.Null)
                return null;
            if (!(token is JArray array))
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor '"
                    + name
                    + "' must be an array or null.");
            }

            var values = new List<string>(array.Count);
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String)
                {
                    throw new InvalidOperationException(
                        "FoxRun generation descriptor '"
                        + name
                        + "' entries must be strings.");
                }
                values.Add(item.Value<string>() ?? string.Empty);
            }
            return values.AsReadOnly();
        }

        private static string NullableStringValue(JObject obj, string name)
        {
            var token = RequiredProperty(obj, name);
            if (token.Type == JTokenType.Null)
                return null;
            if (token.Type != JTokenType.String)
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor '"
                    + name
                    + "' must be a string or null.");
            }
            return token.Value<string>();
        }

        private static JToken RequiredProperty(JObject obj, string name)
        {
            if (!obj.TryGetValue(name, out var token))
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor v5 requires '" + name + "'.");
            }

            return token;
        }

        private static IReadOnlyList<FoxRunEncodingVariantAvailability> LegacyEncodingVariants(
            string encoding,
            int mode)
        {
            var publish = mode == (int)FoxRunFlow.Publish
                          || mode == (int)FoxRunFlow.PublishAndSubscribe;
            var subscribe = mode == (int)FoxRunFlow.Subscribe
                            || mode == (int)FoxRunFlow.PublishAndSubscribe;
            var values = new List<FoxRunEncodingVariantAvailability>();
            if (string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    StringComparison.Ordinal))
            {
                values.Add(new FoxRunEncodingVariantAvailability(
                    FoxRunGenerationDescriptorConstants.JsonEncoding,
                    publish,
                    subscribe));
                values.Add(new FoxRunEncodingVariantAvailability(
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                    publish,
                    subscribe));
                return values;
            }

            if (!string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.JsonEncoding,
                    StringComparison.Ordinal)
                && !string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor v4 contains an unsupported encoding: "
                    + (encoding ?? string.Empty)
                    + ".");
            }

            values.Add(new FoxRunEncodingVariantAvailability(encoding, publish, subscribe));
            return values;
        }

        private static IReadOnlyList<FoxRunEncodingVariantAvailability> EncodingVariantsValue(
            JToken token)
        {
            var array = token as JArray
                        ?? throw new InvalidOperationException(
                            "FoxRun generation descriptor 'encodingVariants' must be a JSON array.");
            if (array.Count == 0)
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor v5 requires at least one encoding variant.");
            }

            var values = new List<FoxRunEncodingVariantAvailability>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var valueToken in array)
            {
                var value = valueToken as JObject
                            ?? throw new InvalidOperationException(
                                "FoxRun encoding variants must be JSON objects.");
                var encoding = RequiredStringValue(value, "encoding");
                if (!seen.Add(encoding))
                {
                    throw new InvalidOperationException(
                        "FoxRun generation descriptor contains duplicate encoding variant '"
                        + encoding
                        + "'.");
                }

                values.Add(new FoxRunEncodingVariantAvailability(
                    encoding,
                    RequiredBoolValue(value, "publishAvailable"),
                    RequiredBoolValue(value, "subscribeAvailable"),
                    publishUnavailableDiagnosticId: RequiredStringValue(
                        value,
                        "publishUnavailableDiagnosticId",
                        allowEmpty: true),
                    publishUnavailableReason: RequiredStringValue(
                        value,
                        "publishUnavailableReason",
                        allowEmpty: true),
                    subscribeUnavailableDiagnosticId: RequiredStringValue(
                        value,
                        "subscribeUnavailableDiagnosticId",
                        allowEmpty: true),
                    subscribeUnavailableReason: RequiredStringValue(
                        value,
                        "subscribeUnavailableReason",
                        allowEmpty: true)));
            }

            return values;
        }

        private static FoxRunNormalizedScheduleTuple NormalizedScheduleValue(JToken token)
        {
            var schedule = token as JObject
                           ?? throw new InvalidOperationException(
                               "FoxRun generation descriptor 'normalizedSchedule' must be a JSON object.");
            var conditionName = RequiredStringValue(
                schedule,
                "conditionMemberKind",
                allowEmpty: true);
            var conditionKind = FoxRunConditionMemberKind.None;
            if (!string.IsNullOrEmpty(conditionName)
                && !Enum.TryParse(conditionName, ignoreCase: false, out conditionKind))
            {
                throw new InvalidOperationException(
                    "Unknown FoxRun normalized schedule condition kind: " + conditionName);
            }

            return new FoxRunNormalizedScheduleTuple(
                RequiredIntValue(schedule, "policy"),
                RequiredBoolValue(schedule, "hasExplicitHz"),
                RequiredFloatValue(schedule, "hz"),
                RequiredFloatValue(schedule, "tolerance"),
                RequiredStringValue(schedule, "onlyIf", allowEmpty: true),
                conditionKind);
        }

        private static bool ContainsMessagePackVariant(
            IReadOnlyList<FoxRunEncodingVariantAvailability> variants)
        {
            foreach (var variant in variants ?? Array.Empty<FoxRunEncodingVariantAvailability>())
            {
                if (string.Equals(
                        variant.Encoding,
                        FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static FoxRunTypeShape TypeShapeValue(JToken token, string path)
        {
            if (token.Type == JTokenType.Null)
                return null;
            var shape = token as JObject
                        ?? throw new InvalidOperationException(
                            "FoxRun type shape '" + path + "' must be a JSON object or null.");
            var kindName = RequiredStringValue(shape, "kind");
            if (!Enum.TryParse(kindName, ignoreCase: false, out FoxRunTypeShapeKind kind))
                throw new InvalidOperationException("Unknown FoxRun type-shape kind: " + kindName);

            var nullable = RequiredBoolValue(shape, "nullable");
            var canConstruct = RequiredBoolValue(shape, "canConstruct");
            var isValueType = RequiredBoolValue(shape, "isValueType");
            var collectionName = RequiredStringValue(shape, "collectionKind");
            if (!Enum.TryParse(collectionName, ignoreCase: false, out FoxRunCollectionKind collectionKind))
            {
                throw new InvalidOperationException(
                    "Unknown FoxRun collection kind: " + collectionName);
            }

            switch (kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    return RequireValueTypeIdentity(
                        FoxRunTypeShape.Canonical(
                            RequiredStringValue(shape, "canonicalType"),
                            nullable),
                        isValueType,
                        path);
                case FoxRunTypeShapeKind.Enum:
                {
                    var values = new List<FoxRunEnumValue>();
                    foreach (var valueToken in RequiredArrayValue(shape, "enumValues"))
                    {
                        var value = valueToken as JObject
                                    ?? throw new InvalidOperationException(
                                        "FoxRun enum values must be JSON objects.");
                        values.Add(new FoxRunEnumValue(
                            RequiredStringValue(value, "name"),
                            RequiredIntValue(value, "number")));
                    }
                    return RequireValueTypeIdentity(
                        FoxRunTypeShape.Enum(
                            RequiredStringValue(shape, "typeName"),
                            values,
                            nullable),
                        isValueType,
                        path);
                }
                case FoxRunTypeShapeKind.Object:
                {
                    var fields = new List<FoxRunTypeField>();
                    foreach (var fieldToken in RequiredArrayValue(shape, "fields"))
                    {
                        var field = fieldToken as JObject
                                    ?? throw new InvalidOperationException(
                                        "FoxRun type-shape fields must be JSON objects.");
                        var repeatedKindName = RequiredStringValue(field, "collectionKind");
                        if (!Enum.TryParse(
                                repeatedKindName,
                                ignoreCase: false,
                                out FoxRunCollectionKind repeatedKind))
                        {
                            throw new InvalidOperationException(
                                "Unknown FoxRun field collection kind: " + repeatedKindName);
                        }

                        fields.Add(new FoxRunTypeField(
                            RequiredStringValue(field, "jsonName", allowEmpty: true),
                            RequiredStringValue(field, "memberName", allowEmpty: true),
                            TypeShapeValue(
                                RequiredProperty(field, "shape"),
                                path + ".fields[" + fields.Count + "].shape"),
                            RequiredBoolValue(field, "repeated"),
                            repeatedKind,
                            RequiredBoolValue(field, "canAssign"),
                            RequiredBoolValue(field, "nullable")));
                    }
                    return FoxRunTypeShape.Object(
                        RequiredStringValue(shape, "typeName"),
                        fields,
                        nullable,
                        canConstruct,
                        isValueType);
                }
                case FoxRunTypeShapeKind.Collection:
                {
                    if (collectionKind == FoxRunCollectionKind.None)
                    {
                        throw new InvalidOperationException(
                            "FoxRun collection shape requires a non-empty collection kind.");
                    }

                    return RequireValueTypeIdentity(
                        FoxRunTypeShape.Collection(
                            collectionKind,
                            TypeShapeValue(
                                RequiredProperty(shape, "elementShape"),
                                path + ".elementShape")
                            ?? throw new InvalidOperationException(
                                "FoxRun collection shape requires an element shape."),
                            nullable),
                        isValueType,
                        path);
                }
                default:
                    throw new InvalidOperationException(
                        "Unsupported FoxRun type-shape kind: " + kindName);
            }
        }

        private static FoxRunTypeShape RequireValueTypeIdentity(
            FoxRunTypeShape shape,
            bool isValueType,
            string path)
        {
            if (shape.IsValueType != isValueType)
            {
                throw new InvalidOperationException(
                    "FoxRun type shape '"
                    + path
                    + "' has an inconsistent isValueType value.");
            }
            return shape;
        }

        private static FoxRunProtobufMetadata ProtobufMetadataValue(JToken token)
        {
            if (token.Type == JTokenType.Null)
                return null;
            var metadata = token as JObject
                           ?? throw new InvalidOperationException(
                               "FoxRun generation descriptor 'protobuf' must be a JSON object or null.");
            return new FoxRunProtobufMetadata(
                RequiredIntValue(metadata, "fieldNumber"),
                ProtobufTypeMetadataValue(RequiredProperty(metadata, "type")));
        }

        private static FoxRunProtobufTypeMetadata ProtobufTypeMetadataValue(JToken token)
        {
            if (token.Type == JTokenType.Null)
                return null;
            var metadata = token as JObject
                           ?? throw new InvalidOperationException(
                               "FoxRun Protobuf type metadata must be a JSON object or null.");
            var fields = new List<FoxRunProtobufFieldMetadata>();
            foreach (var fieldToken in RequiredArrayValue(metadata, "fields"))
            {
                var field = fieldToken as JObject
                            ?? throw new InvalidOperationException(
                                "FoxRun Protobuf field metadata must be a JSON object.");
                fields.Add(new FoxRunProtobufFieldMetadata(
                    RequiredStringValue(field, "memberName", allowEmpty: true),
                    RequiredStringValue(field, "jsonName", allowEmpty: true),
                    RequiredIntValue(field, "fieldNumber"),
                    ProtobufTypeMetadataValue(RequiredProperty(field, "type")),
                    RequiredBoolValue(field, "presenceOnly"),
                    RequiredBoolValue(field, "presenceUsesHasValue")));
            }
            return new FoxRunProtobufTypeMetadata(
                RequiredStringValue(metadata, "typeName", allowEmpty: true),
                fields);
        }

        private static JArray RequiredArrayValue(JObject obj, string name)
            => ArrayValue(obj, name, required: true);

        private static string RequiredStringValue(
            JObject obj,
            string name,
            bool allowEmpty = false)
        {
            var token = RequiredProperty(obj, name);
            if (token.Type != JTokenType.String)
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor '" + name + "' must be a JSON string.");
            }
            var value = token.Value<string>() ?? string.Empty;
            if (!allowEmpty && string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor '" + name + "' must not be empty.");
            }
            return value;
        }

        private static bool RequiredBoolValue(JObject obj, string name)
        {
            var token = RequiredProperty(obj, name);
            if (token.Type != JTokenType.Boolean)
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor '" + name + "' must be a JSON boolean.");
            }
            return token.Value<bool>();
        }

        private static int RequiredIntValue(JObject obj, string name)
        {
            var token = RequiredProperty(obj, name);
            if (token.Type != JTokenType.Integer)
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor '" + name + "' must be a JSON integer.");
            }
            return token.Value<int>();
        }

        private static float RequiredFloatValue(JObject obj, string name)
        {
            var token = RequiredProperty(obj, name);
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                throw new InvalidOperationException(
                    "FoxRun generation descriptor '" + name + "' must be a JSON number.");
            }
            return token.Value<float>();
        }

        private static string StringValue(JObject obj, string name)
            => obj.TryGetValue(name, out var token) ? token.Value<string>() ?? string.Empty : string.Empty;

        private static int IntValue(JObject obj, string name)
            => obj.TryGetValue(name, out var token) ? token.Value<int>() : 0;

        private static bool BoolValue(JObject obj, string name)
            => obj.TryGetValue(name, out var token) && token.Value<bool>();

        private static float FloatValue(JObject obj, string name)
        {
            if (!obj.TryGetValue(name, out var token))
                return 0f;
            return token.Value<float>();
        }

        private static FoxRunRos2MessageShape Ros2MessageShapeValue(JObject member)
        {
            if (!(member["ros2MessageShape"] is JObject shape))
                return null;

            var members = new List<FoxRunRos2MessageMemberShape>();
            foreach (var memberToken in shape["members"] as JArray ?? new JArray())
            {
                var shapeMember = memberToken as JObject
                    ?? throw new InvalidOperationException("FoxRun ROS2 message-shape members must be JSON objects.");
                if (!Enum.TryParse(StringValue(shapeMember, "kind"), out FoxRunRos2MessageMemberKind kind))
                    throw new InvalidOperationException("Unknown FoxRun ROS2 message member kind: " + StringValue(shapeMember, "kind"));
                if (!Enum.TryParse(StringValue(shapeMember, "sequenceRepresentation"), out FoxRunRos2SequenceRepresentation representation))
                    throw new InvalidOperationException("Unknown FoxRun ROS2 sequence representation: " + StringValue(shapeMember, "sequenceRepresentation"));
                members.Add(new FoxRunRos2MessageMemberShape(
                    StringValue(shapeMember, "name"),
                    kind,
                    StringValue(shapeMember, "fullyQualifiedTypeName"),
                    StringValue(shapeMember, "sequenceElementTypeName"),
                    StringValue(shapeMember, "nestedShapeIdentity"),
                    BoolValue(shapeMember, "canRead"),
                    BoolValue(shapeMember, "canWrite"),
                    representation,
                    IntValue(shapeMember, "fixedSize"),
                    Ros2MessageShapeValue(shapeMember, "nestedShape")));
            }

            var diagnostics = new List<string>();
            foreach (var diagnostic in shape["diagnostics"] as JArray ?? new JArray())
                diagnostics.Add(diagnostic.Value<string>() ?? string.Empty);

            return new FoxRunRos2MessageShape(
                StringValue(shape, "fullyQualifiedTypeName"),
                StringValue(shape, "canonicalRosType"),
                BoolValue(shape, "hasPublicParameterlessConstructor"),
                BoolValue(shape, "implementsRos2Message"),
                StringValue(shape, "copyShapeIdentity"),
                members,
                diagnostics);
        }

        private static FoxRunRos2MessageShape Ros2MessageShapeValue(JObject parent, string propertyName)
        {
            if (!(parent[propertyName] is JObject shape))
                return null;

            var wrapper = new JObject { ["ros2MessageShape"] = shape };
            return Ros2MessageShapeValue(wrapper);
        }

        private static int PolicyValue(JObject member)
        {
            var mode = StringValue(member, "policy");
            switch (mode)
            {
                case "":
                case "FixedRate": return (int)FoxRunPolicy.FixedRate;
                case "Change": return (int)FoxRunPolicy.Change;
                case "Trigger": return (int)FoxRunPolicy.Trigger;
                default: throw new InvalidOperationException("Unknown FoxRun policy: " + mode);
            }
        }

        private static int ModeValue(JObject member)
        {
            var mode = StringValue(member, "mode");
            switch (mode)
            {
                case "":
                case "Publish": return (int)FoxRunFlow.Publish;
                case "Subscribe": return (int)FoxRunFlow.Subscribe;
                case "PublishAndSubscribe": return (int)FoxRunFlow.PublishAndSubscribe;
                default: throw new InvalidOperationException("Unknown FoxRun mode: " + mode);
            }
        }

        private static FoxRunNamedArgumentPresence? ExplicitArgumentsValue(JObject member)
        {
            if (!member.TryGetValue("explicitArguments", out var token))
                return null;

            var result = FoxRunNamedArgumentPresence.None;
            foreach (var name in (token.Value<string>() ?? string.Empty)
                         .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Enum.TryParse(name, ignoreCase: false, out FoxRunNamedArgumentPresence value)
                    || value == FoxRunNamedArgumentPresence.None)
                    throw new InvalidOperationException("Unknown FoxRun explicit argument: " + name);
                result |= value;
            }

            return result;
        }

        private static FoxRunConditionMemberKind ConditionMemberKindValue(JObject member)
        {
            var name = StringValue(member, "onlyIfMemberKind");
            if (string.IsNullOrEmpty(name))
                return FoxRunConditionMemberKind.None;
            if (Enum.TryParse(name, ignoreCase: false, out FoxRunConditionMemberKind value))
                return value;
            throw new InvalidOperationException("Unknown FoxRun OnlyIf member kind: " + name);
        }
    }
}
