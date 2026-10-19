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
    internal static partial class FoxRunGenerationDescriptorJsonReader
    {
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
                            nullable,
                            RequiredStringValue(shape, "canonicalType")),
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

        private static IReadOnlyList<string> LegacyPublishTransportIds(
            JObject member,
            int mode)
        {
            if (mode != (int)FoxRunFlow.Publish
                && mode != (int)FoxRunFlow.PublishAndSubscribe)
            {
                return null;
            }

            var targets = StringValue(member, "targets");
            if (string.IsNullOrWhiteSpace(targets))
                return null;

            var result = new List<string>();
            foreach (var value in targets.Split(','))
            {
                var mapped = LegacyTransportId(value.Trim());
                if (mapped != null && !result.Contains(mapped))
                    result.Add(mapped);
            }
            return result.AsReadOnly();
        }

        private static string LegacySubscribeTransportId(
            JObject member,
            int mode)
        {
            if (mode != (int)FoxRunFlow.Subscribe
                && mode != (int)FoxRunFlow.PublishAndSubscribe)
            {
                return null;
            }

            return LegacyTransportId(StringValue(member, "source"));
        }

        private static string LegacyTransportId(string value)
        {
            switch (value)
            {
                case "":
                case "inherit":
                    return null;
                case "Foxglove":
                    return FoxgloveWebSocketTransport.Id;
                case "Ros2Native":
                    return "unity2foxglove.r2fu";
                case "Ros2Bridge":
                    return "unity2foxglove.ros2bridge";
                default:
                    throw new InvalidOperationException(
                        "Unknown legacy FoxRun transport: " + value);
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

        private static FoxRunNamedArgumentPresence? ExplicitArgumentsValue(
            JObject member,
            bool strictV6)
        {
            if (!member.TryGetValue("explicitArguments", out var token))
                return null;

            var result = FoxRunNamedArgumentPresence.None;
            foreach (var name in (token.Value<string>() ?? string.Empty)
                         .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Enum.TryParse(
                        name,
                        ignoreCase: false,
                        out FoxRunNamedArgumentPresence value)
                    || value == FoxRunNamedArgumentPresence.None)
                {
                    if (!strictV6
                        && (name == "Source"
                            || name == "Targets"
                            || name == "QoS"))
                    {
                        continue;
                    }
                    throw new InvalidOperationException("Unknown FoxRun explicit argument: " + name);
                }
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
