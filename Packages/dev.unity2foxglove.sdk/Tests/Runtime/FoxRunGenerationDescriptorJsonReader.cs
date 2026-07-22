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
            var isLegacyV1 = descriptorVersion <= 1;
            var types = new List<FoxRunGenerationType>();
            foreach (var typeToken in root["types"] as JArray ?? new JArray())
            {
                var type = typeToken as JObject
                    ?? throw new InvalidOperationException("FoxRun generation descriptor 'types' entries must be JSON objects.");
                var ns = StringValue(type, "namespace");
                var className = StringValue(type, "className");
                var members = new List<FoxRunGenerationMember>();
                foreach (var memberToken in type["members"] as JArray ?? new JArray())
                {
                    var member = memberToken as JObject
                        ?? throw new InvalidOperationException("FoxRun generation descriptor 'members' entries must be JSON objects.");
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
                        rateHz: FloatValue(member, "rateHz"),
                        schemaName: StringValue(member, "schemaName"),
                        policy: PolicyValue(member),
                        changeEpsilon: FloatValue(member, "changeEpsilon"),
                        forceIntervalSeconds: FloatValue(member, "forceIntervalSeconds"),
                        hostKind: StringValue(member, "hostKind"),
                        rawMemberOrder: IntValue(member, "rawMemberOrder"),
                        conditionalSymbols: StringValue(member, "conditionalSymbols"),
                        when: StringValue(member, "when"),
                        unless: StringValue(member, "unless"),
                        isAggregateMember: BoolValue(member, "isAggregateMember"),
                        jsonFieldName: StringValue(member, "jsonFieldName"),
                        mode: ModeValue(member),
                        encoding: StringValue(member, "encoding"),
                        protobufFieldNumber: IntValue(member, "protobufFieldNumber"),
                        subscriptionProvider: StringValueOrDefault(
                            member,
                            "subscriptionProvider",
                            isLegacyV1 ? FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider : string.Empty),
                        ros2Qos: StringValueOrDefault(
                            member,
                            "ros2Qos",
                            isLegacyV1 ? FoxRunGenerationDescriptorConstants.InheritRos2Qos : string.Empty),
                        generatesWebSocketCodec: BoolValueOrDefault(
                            member,
                            "generatesWebSocketCodec",
                            isLegacyV1),
                        generatesRos2NativeRegistration: BoolValue(member, "generatesRos2NativeRegistration"),
                        ros2MessageShape: Ros2MessageShapeValue(member)));
                }
                types.Add(new FoxRunGenerationType(ns, className, members));
            }

            return new FoxRunGenerationModel(
                types,
                descriptorVersion,
                StringValue(root, "generatorVersion"));
        }

        private static string StringValue(JObject obj, string name)
            => obj.TryGetValue(name, out var token) ? token.Value<string>() ?? string.Empty : string.Empty;

        private static string StringValueOrDefault(JObject obj, string name, string defaultValue)
            => obj.TryGetValue(name, out var token) ? token.Value<string>() ?? string.Empty : defaultValue ?? string.Empty;

        private static int IntValue(JObject obj, string name)
            => obj.TryGetValue(name, out var token) ? token.Value<int>() : 0;

        private static bool BoolValue(JObject obj, string name)
            => obj.TryGetValue(name, out var token) && token.Value<bool>();

        private static bool BoolValueOrDefault(JObject obj, string name, bool defaultValue)
            => obj.TryGetValue(name, out var token) ? token.Value<bool>() : defaultValue;

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
                case "ChangeOrInterval": return (int)FoxRunPolicy.ChangeOrInterval;
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
    }
}
