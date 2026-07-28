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
            if (descriptorVersion != FoxRunGenerationDescriptorConstants.DescriptorVersion)
            {
                throw new InvalidOperationException(
                    "Unsupported FoxRun generation descriptor version: "
                    + descriptorVersion);
            }
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
                        mode: ModeValue(member),
                        encoding: StringValue(member, "encoding"),
                        protobufFieldNumber: IntValue(member, "protobufFieldNumber"),
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
                        isStream: BoolValue(member, "isStream")));
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
