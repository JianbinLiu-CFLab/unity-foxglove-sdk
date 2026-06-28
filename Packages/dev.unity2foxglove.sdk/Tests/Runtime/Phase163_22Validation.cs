// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-22 validation for FoxRun emitter model and descriptor contracts.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_22Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-22: FoxRun Emitter Model and Descriptor Contracts ===");
            _passed = 0;

            DescriptorRoundTripsSemanticPolicyFields();
            DescriptorComparerDetectsSemanticPolicyDrift();
            SchemaRegistrationContinuesAfterInvalidAggregateContract();
            ChannelRegistryReportsConflictingOverwrite();
            ArrayTopicFingerprintUsesCanonicalElementType();
            JsonWritersEscapeSurrogatePairs();
            SourceShapeGuardsArePresent();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-22: {_passed} checks passed.");
        }

        private static void DescriptorRoundTripsSemanticPolicyFields()
        {
            var model = Model(Member(
                "value",
                "/phase163/descriptor",
                "float",
                when: "isReady",
                unless: "isPaused",
                isAggregateMember: true,
                jsonFieldName: "renamedValue"));

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);
            var root = JObject.Parse(json);
            var member = (JObject)root["types"][0]["members"][0];
            var reread = FoxRunGenerationDescriptorJsonReader.Read(json);
            var rereadMember = reread.Types[0].Members[0];

            Check(member.Value<string>("when") == "isReady"
                  && member.Value<string>("unless") == "isPaused"
                  && member.Value<bool>("isAggregateMember")
                  && member.Value<string>("jsonFieldName") == "renamedValue",
                "163-22A-1: descriptor JSON writes conditional, aggregate, and JSON field semantics");
            Check(rereadMember.When == "isReady"
                  && rereadMember.Unless == "isPaused"
                  && rereadMember.IsAggregateMember
                  && rereadMember.JsonFieldName == "renamedValue",
                "163-22A-2: descriptor reader restores conditional, aggregate, and JSON field semantics");
            Check(FoxRunGenerationDescriptorComparer.Compare(model, reread).IsSemanticEqual,
                "163-22A-3: descriptor comparer treats round-tripped semantic policy fields as equal");
        }

        private static void DescriptorComparerDetectsSemanticPolicyDrift()
        {
            var left = Model(Member("value", "/phase163/descriptor", "float", when: "ready"));

            Check(!FoxRunGenerationDescriptorComparer.Compare(left, Model(Member("value", "/phase163/descriptor", "float", when: "other"))).IsSemanticEqual,
                "163-22B-1: descriptor comparer detects When drift");
            Check(!FoxRunGenerationDescriptorComparer.Compare(left, Model(Member("value", "/phase163/descriptor", "float", unless: "paused"))).IsSemanticEqual,
                "163-22B-2: descriptor comparer detects Unless drift");
            Check(!FoxRunGenerationDescriptorComparer.Compare(left, Model(Member("value", "/phase163/descriptor", "float", isAggregateMember: true))).IsSemanticEqual,
                "163-22B-3: descriptor comparer detects aggregate membership drift");
            Check(!FoxRunGenerationDescriptorComparer.Compare(left, Model(Member("value", "/phase163/descriptor", "float", jsonFieldName: "renamed"))).IsSemanticEqual,
                "163-22B-4: descriptor comparer detects JSON field name drift");
            Check(!FoxRunGenerationDescriptorComparer.Compare(left, Model(Member("value", "/phase163/descriptor", "double", when: "ready"))).IsSemanticEqual,
                "163-22B-5: descriptor comparer detects same-member canonical type drift");
        }

        private static void SchemaRegistrationContinuesAfterInvalidAggregateContract()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            var warnings = 0;
            void OnWarning(string message, Exception exception)
            {
                warnings++;
                Check(message.Contains("/phase163/bad", StringComparison.Ordinal)
                      && exception is InvalidOperationException,
                    "163-22C-1: schema registration failure warning includes topic and exception");
            }

            FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed += OnWarning;
            try
            {
                var manifest = new FoxRunSchemaManifestInfo(
                    1,
                    "Unity2Foxglove",
                    "FoxRun",
                    1,
                    "global",
                    "foxrun",
                    new[]
                    {
                        new FoxRunSchemaTypeInfo(
                            "Demo.SchemaProbe",
                            new[]
                            {
                                Contract("Demo.Bad", "/phase163/bad", new FoxRunSchemaFieldInfo("payload", "_payload", "field", "object", false, false, aggregate: true)),
                                Contract("Demo.Good", "/phase163/good", new FoxRunSchemaFieldInfo("speed", "_speed", "field", "float", false, false, aggregate: true))
                            })
                    });
                var registry = new DefaultSchemaRegistry();

                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Check(warnings == 1, "163-22C-2: invalid aggregate schema emits exactly one warning");
                Check(!registry.TryGetSchema("Demo.Bad", FoxgloveSchemaDefinitions.JsonSchemaEncoding, out _)
                      && registry.TryGetSchema("Demo.Good", FoxgloveSchemaDefinitions.JsonSchemaEncoding, out var entry)
                      && entry.Content.Contains("\"speed\"", StringComparison.Ordinal),
                    "163-22C-3: schema registry continues with later aggregate contracts after a build failure");
            }
            finally
            {
                FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed -= OnWarning;
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        private static void ChannelRegistryReportsConflictingOverwrite()
        {
            var registry = new ChannelRegistry();
            var warnings = 0;
            registry.ChannelOverwritten += (previous, next) =>
            {
                warnings++;
                Check(previous.Topic == "/phase163/a" && next.Topic == "/phase163/b",
                    "163-22D-1: channel overwrite event exposes previous and replacement descriptors");
            };

            registry.Register(Channel(7, "/phase163/a", "foxglove.A"));
            registry.Register(Channel(7, "/phase163/a", "foxglove.A"));
            registry.Register(Channel(7, "/phase163/b", "foxglove.B"));

            Check(warnings == 1, "163-22D-2: identical channel re-registration is quiet but conflicting overwrite is reported");
            Check(registry.Get(7).Topic == "/phase163/b", "163-22D-3: channel registry preserves replacement descriptor compatibility");
        }

        private static void ArrayTopicFingerprintUsesCanonicalElementType()
        {
            var source = FoxgloveSourceEmitter.EmitClass(new FoxRunGenerationType(
                "Demo",
                "ArrayProbe",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "ArrayProbe",
                        "_samples",
                        "field",
                        "System.Single[]",
                        "float[]",
                        null,
                        true,
                        true,
                        "System.Single",
                        "/phase163/array",
                        10f,
                        string.Empty,
                        0,
                        0f,
                        0f,
                        "Test",
                        0,
                        string.Empty,
                        jsonFieldName: "samples")
                }));

            Check(source.Contains("fields=samples:float32", StringComparison.Ordinal)
                  && !source.Contains("fields=samples:float[]", StringComparison.Ordinal),
                "163-22E-1: emitted FoxTopicContract fingerprint shape uses canonical array element type");
        }

        private static void JsonWritersEscapeSurrogatePairs()
        {
            const string face = "\U0001F600";
            var model = Model(Member("value", "/phase163/emoji", "string", jsonFieldName: "face" + face));
            var descriptorJson = FoxRunGenerationDescriptorJsonWriter.Write(model);
            var descriptorMember = (JObject)JObject.Parse(descriptorJson)["types"][0]["members"][0];

            var schemaJson = FoxRunJsonSchemaBuilder.Build(Contract(
                "Demo.Emoji",
                "/phase163/emoji",
                new FoxRunSchemaFieldInfo("face" + face, "_face", "field", "string", false, false, aggregate: true)));
            var schema = JObject.Parse(schemaJson);

            Check(descriptorJson.Contains("\\ud83d\\ude00", StringComparison.Ordinal)
                  && descriptorMember.Value<string>("jsonFieldName") == "face" + face,
                "163-22F-1: descriptor JSON writer escapes surrogate pairs and round-trips them");
            Check(schemaJson.Contains("\\ud83d\\ude00", StringComparison.Ordinal)
                  && schema["properties"]["face" + face] != null,
                "163-22F-2: FoxRun JSON schema writer escapes surrogate pairs and remains parseable");
        }

        private static void SourceShapeGuardsArePresent()
        {
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorJsonWriter.cs");
            var comparer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorComparer.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaInfoRegistry.cs");
            var topicEmitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");
            var channelRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ChannelRegistry.cs");

            Check(writer.Contains("\"when\"", StringComparison.Ordinal)
                  && writer.Contains("\"unless\"", StringComparison.Ordinal)
                  && writer.Contains("\"isAggregateMember\"", StringComparison.Ordinal)
                  && writer.Contains("\"jsonFieldName\"", StringComparison.Ordinal),
                "163-22G-1: descriptor writer includes all semantic policy fields");
            Check(comparer.Contains("member.CanonicalType", StringComparison.Ordinal)
                  && comparer.Contains("\"jsonFieldName\"", StringComparison.Ordinal),
                "163-22G-2: descriptor comparer keys and compares semantic identity fields");
            Check(registry.Contains("GeneratedSchemaRegistrationFailed", StringComparison.Ordinal)
                  && registry.Contains("IsRecoverableSchemaException", StringComparison.Ordinal),
                "163-22G-3: schema registry isolates recoverable generated schema build failures");
            Check(topicEmitter.Contains("field.CanonicalType", StringComparison.Ordinal)
                  && topicEmitter.Contains("field.JsonFieldName", StringComparison.Ordinal),
                "163-22G-4: topic metadata emitter uses model-provided canonical type and JSON field names");
            Check(channelRegistry.Contains("ChannelOverwritten", StringComparison.Ordinal)
                  && channelRegistry.Contains("IsConflictingDescriptor", StringComparison.Ordinal),
                "163-22G-5: channel registry exposes conflicting overwrite diagnostics");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_22Validation.cs", StringComparison.Ordinal),
                "163-22H-1: runtime test project compiles Phase163_22Validation");
            Check(registry.Contains("--phase163-22", StringComparison.Ordinal)
                  && registry.Contains("Phase163_22Validation.Validate", StringComparison.Ordinal),
                "163-22H-2: validation registry exposes --phase163-22");
        }

        private static FoxRunGenerationModel Model(FoxRunGenerationMember member)
            => new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "DescriptorProbe", new[] { member })
            });

        private static FoxRunGenerationMember Member(
            string memberName,
            string topic,
            string typeName,
            string when = "",
            string unless = "",
            bool isAggregateMember = false,
            string jsonFieldName = "")
            => new FoxRunGenerationMember(
                "Demo",
                "DescriptorProbe",
                memberName,
                "field",
                typeName,
                true,
                false,
                string.Empty,
                topic,
                1f,
                string.Empty,
                0,
                0f,
                0f,
                "Test",
                0,
                string.Empty,
                when,
                unless,
                isAggregateMember,
                jsonFieldName);

        private static FoxRunSchemaContractInfo Contract(string schemaName, string topic, params FoxRunSchemaFieldInfo[] fields)
            => new FoxRunSchemaContractInfo(
                schemaName,
                topic,
                schemaName,
                "json",
                "contract",
                "binding",
                "policy",
                "FixedRate",
                10f,
                0f,
                0f,
                fields);

        private static AdvertiseChannel Channel(uint id, string topic, string schemaName)
            => new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "protobuf",
                SchemaName = schemaName,
                SchemaEncoding = "protobuf",
                Schema = "schema"
            };

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
