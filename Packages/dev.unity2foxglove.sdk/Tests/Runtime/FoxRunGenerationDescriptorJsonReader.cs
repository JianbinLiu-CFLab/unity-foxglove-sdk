// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test-only reader for FoxRun generation descriptor JSON equivalence checks.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class FoxRunGenerationDescriptorJsonReader
    {
        public static FoxRunGenerationModel Read(string json)
        {
            var root = JObject.Parse(json ?? throw new ArgumentNullException(nameof(json)));
            var types = new List<FoxRunGenerationType>();
            foreach (var typeToken in root["types"] as JArray ?? new JArray())
            {
                var type = (JObject)typeToken;
                var ns = StringValue(type, "namespace");
                var className = StringValue(type, "className");
                var members = new List<FoxRunGenerationMember>();
                foreach (var memberToken in type["members"] as JArray ?? new JArray())
                {
                    var member = (JObject)memberToken;
                    members.Add(new FoxRunGenerationMember(
                        ns,
                        className,
                        StringValue(member, "memberName"),
                        StringValue(member, "memberKind"),
                        StringValue(member, "rawTypeName"),
                        StringValue(member, "emissionTypeName"),
                        StringValue(member, "canonicalType"),
                        isValueType: false,
                        isArray: BoolValue(member, "isArray"),
                        elementTypeName: StringValue(member, "elementTypeName"),
                        topic: StringValue(member, "topic"),
                        rateHz: FloatValue(member, "rateHz"),
                        schemaName: StringValue(member, "schemaName"),
                        publishMode: PublishModeValue(member),
                        changeEpsilon: FloatValue(member, "changeEpsilon"),
                        forceIntervalSeconds: FloatValue(member, "forceIntervalSeconds"),
                        hostKind: StringValue(member, "hostKind"),
                        rawMemberOrder: IntValue(member, "rawMemberOrder"),
                        conditionalSymbols: StringValue(member, "conditionalSymbols")));
                }
                types.Add(new FoxRunGenerationType(ns, className, members));
            }

            return new FoxRunGenerationModel(
                types,
                IntValue(root, "descriptorVersion"),
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

        private static int PublishModeValue(JObject member)
        {
            switch (StringValue(member, "publishMode"))
            {
                case "OnChange": return 1;
                case "OnChangeOrInterval": return 2;
                case "OnTrigger": return 3;
                default: return 0;
            }
        }
    }
}
