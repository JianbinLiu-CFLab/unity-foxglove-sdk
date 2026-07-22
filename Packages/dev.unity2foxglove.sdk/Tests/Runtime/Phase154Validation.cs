// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 154 validation for FoxRun message aggregation and schema inference.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase154Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 154 Tests ---");
            _passCount = 0;

            VerifyAggregationAttributeSurface();
            VerifyGeneratedAggregationPublishPath();
            VerifySchemaInferenceRegistrationBoundary();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 154: " + _passCount + " checks passed.\n");
        }

        private static void VerifyAggregationAttributeSurface()
        {
            var message = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunMessageAttribute.cs");
            var field = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunFieldAttribute.cs");

            Check(message.Contains("public sealed class FoxRunMessageAttribute", StringComparison.Ordinal)
                  && message.Contains("AttributeTargets.Class", StringComparison.Ordinal)
                  && message.Contains("AllowMultiple = false", StringComparison.Ordinal)
                  && message.Contains("public string Topic { get; }", StringComparison.Ordinal)
                  && message.Contains("public FoxRunPolicy Policy", StringComparison.Ordinal),
                "FoxRunMessage is a single class-level aggregate topic attribute with cadence options");

            Check(field.Contains("public sealed class FoxRunFieldAttribute", StringComparison.Ordinal)
                  && field.Contains("AttributeTargets.Field | AttributeTargets.Property", StringComparison.Ordinal)
                  && field.Contains("public string JsonName { get; }", StringComparison.Ordinal),
                "FoxRunField marks aggregate members and supports explicit JSON names");
        }

        private static void VerifyGeneratedAggregationPublishPath()
        {
            var generator = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");

            Check(generator.Contains("MessageAttrFullName", StringComparison.Ordinal)
                  && generator.Contains("FieldAttrFullName", StringComparison.Ordinal)
                  && generator.Contains("isAggregateMember: true", StringComparison.Ordinal)
                  && generator.Contains("DeclaringTypeName(containingType)", StringComparison.Ordinal),
                "Source generator lowers FoxRunMessage/FoxRunField members into aggregate topic entries");

            Check(generator.Contains("FOXRUN018", StringComparison.Ordinal)
                  && generator.Contains("FOXRUN019", StringComparison.Ordinal)
                  && generator.Contains("FOXRUN020", StringComparison.Ordinal)
                  && generator.Contains("FOXRUN022", StringComparison.Ordinal),
                "Source generator exposes fail-closed diagnostics for invalid aggregate message shapes");

            Check(publish.Contains("PublishFoxRunJsonBytes", StringComparison.Ordinal)
                  && publish.Contains("__WriteFoxRunJson_", StringComparison.Ordinal)
                  && publish.Contains("__AppendFoxRunJsonString", StringComparison.Ordinal)
                  && !publish.Contains("JsonConvert.SerializeObject", StringComparison.Ordinal),
                "Aggregate publish path emits explicit JSON bytes without runtime reflection serialization");

            Check(manager.Contains("public void PublishFoxRunJsonBytes", StringComparison.Ordinal)
                  && manager.Contains("_runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), logTimeNs)", StringComparison.Ordinal)
                  && manager.Contains("RecordPublishCadence(topic, JsonEncoding)", StringComparison.Ordinal),
                "FoxgloveManager publishes aggregate JSON bytes directly while preserving channel cadence accounting");
        }

        private static void VerifySchemaInferenceRegistrationBoundary()
        {
            var builder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunJsonSchemaBuilder.cs");
            var registrySource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaInfoRegistry.cs");
            var schemaField = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaFieldInfo.cs");
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");

            Check(builder.Contains("public static string Build(FoxRunSchemaContractInfo contract)", StringComparison.Ordinal)
                  && builder.Contains("AppendNumberObject", StringComparison.Ordinal)
                  && builder.Contains("UnityEngine.Vector3", StringComparison.Ordinal)
                  && builder.Contains("UnityEngine.Quaternion", StringComparison.Ordinal)
                  && builder.Contains("Unsupported FoxRun aggregate schema field type", StringComparison.Ordinal)
                  && !builder.Contains("default:\r\n                    sb.Append(\"{\\\"type\\\":\\\"string\\\"}\");", StringComparison.Ordinal),
                "FoxRun JSON schema builder emits inline Unity shapes and rejects unsupported aggregate field types");

            Check(schemaField.Contains("public bool Aggregate { get; }", StringComparison.Ordinal)
                  && writer.Contains("field.Aggregate", StringComparison.Ordinal)
                  && registrySource.Contains("IsGeneratedAggregateContract(contract)", StringComparison.Ordinal),
                "Generated schema metadata records aggregate fields and schema registration requires aggregate contracts");

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                var registry = new DefaultSchemaRegistry();
                FoxRunSchemaInfoRegistry.RegisterGenerated(CreateSchemaInfo(aggregate: true, schemaName: "Demo.VehicleTelemetry"));
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);
                Check(registry.TryGetSchema("Demo.VehicleTelemetry", FoxgloveSchemaDefinitions.JsonSchemaEncoding, out var entry)
                      && entry.Content.Contains("\"speed\":{\"anyOf\":[{\"type\":\"number\"},{\"type\":\"null\"}]}", StringComparison.Ordinal)
                      && entry.Content.Contains("\"enabled\":{\"type\":\"boolean\"}", StringComparison.Ordinal)
                      && entry.Content.Contains("\"position\":{\"type\":\"object\"", StringComparison.Ordinal)
                      && entry.Content.Contains("\"x\":{\"anyOf\":[{\"type\":\"number\"},{\"type\":\"null\"}]}", StringComparison.Ordinal),
                    "Aggregate FoxRun schema info registers inferred canonical JSON schema content");
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                var registry = new DefaultSchemaRegistry();
                FoxRunSchemaInfoRegistry.RegisterGenerated(CreateSchemaInfo(aggregate: false, schemaName: "foxglove.Log"));
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);
                Check(!registry.TryGetSchema("foxglove.Log", FoxgloveSchemaDefinitions.JsonSchemaEncoding, out _),
                    "Legacy single-field FoxRun schema names are not auto-registered as aggregate schemas");
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase154"),
                "Validation registry exposes the Phase154 flag");
        }

        private static FoxRunSchemaManifestInfo CreateSchemaInfo(bool aggregate, string schemaName)
        {
            return new FoxRunSchemaManifestInfo(
                1,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[]
                {
                    new FoxRunSchemaTypeInfo(
                        "Demo.VehicleTelemetry",
                        new[]
                        {
                            new FoxRunSchemaContractInfo(
                                "Demo.VehicleTelemetry",
                                "/phase154/vehicle",
                                schemaName,
                                "json",
                                "contract",
                                "binding",
                                "policy",
                                "FixedRate",
                                10f,
                                0f,
                                0f,
                                new[]
                                {
                                    new FoxRunSchemaFieldInfo("speed", "_speed", "field", "float32", false, false, aggregate),
                                    new FoxRunSchemaFieldInfo("enabled", "_enabled", "field", "bool", false, false, aggregate),
                                    new FoxRunSchemaFieldInfo("position", "_position", "field", "unity.vector3.float32", false, false, aggregate)
                                })
                        })
                });
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
