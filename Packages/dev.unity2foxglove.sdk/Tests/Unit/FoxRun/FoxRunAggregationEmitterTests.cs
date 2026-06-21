// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunAggregationEmitterTests
    {
        [Fact]
        public void AggregateMemberEmitsExplicitJsonBytesWithoutDictionaryPayload()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "VehicleTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "VehicleTelemetry",
                        "_speed",
                        "field",
                        "System.Single",
                        true,
                        false,
                        "",
                        "/phase154/vehicle",
                        10f,
                        "Demo.VehicleTelemetry",
                        0,
                        0f,
                        0f,
                        "UnitTest",
                        0,
                        "",
                        isAggregateMember: true,
                        jsonFieldName: "speed"),
                    new FoxRunGenerationMember(
                        "Demo",
                        "VehicleTelemetry",
                        "_enabled",
                        "field",
                        "System.Boolean",
                        true,
                        false,
                        "",
                        "/phase154/vehicle",
                        10f,
                        "Demo.VehicleTelemetry",
                        0,
                        0f,
                        0f,
                        "UnitTest",
                        1,
                        "",
                        isAggregateMember: true,
                        jsonFieldName: "enabled")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("PublishFoxRunJsonBytes", source, StringComparison.Ordinal);
            Assert.Contains("mgr.PublishFoxRunJsonBytes(\"/phase154/vehicle\", \"Demo.VehicleTelemetry\"", source, StringComparison.Ordinal);
            Assert.Contains("__WriteFoxRunJson_0", source, StringComparison.Ordinal);
            Assert.Contains("\\\"speed\\\"", source, StringComparison.Ordinal);
            Assert.Contains("\\\"enabled\\\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase154/vehicle\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new Dictionary<string, object>", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorLowersFoxRunMessageFieldsToAggregateJsonPublish()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(""/phase154/vehicle"", SchemaName = ""Demo.VehicleTelemetry"")]
    public partial class VehicleTelemetry
    {
        [FoxRunField(""speed"")]
        private float _speed;

        [FoxRunField]
        private bool _enabled;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase154GeneratorProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                BasicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(compilation);
            var result = driver.GetRunResult();
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .SingleOrDefault(text => text.Contains("partial class VehicleTelemetry", StringComparison.Ordinal));

            Assert.True(
                generated != null,
                "Expected VehicleTelemetry generated source. Diagnostics: " +
                string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString())) +
                " Generated: " + string.Join(" | ", result.GeneratedTrees.Select(tree => tree.FilePath)));

            Assert.Contains("PublishFoxRunJsonBytes", generated, StringComparison.Ordinal);
            Assert.Contains("mgr.PublishFoxRunJsonBytes(\"/phase154/vehicle\", \"Demo.VehicleTelemetry\"", generated, StringComparison.Ordinal);
            Assert.Contains("__WriteFoxRunJson_0", generated, StringComparison.Ordinal);
            Assert.Contains("\\\"speed\\\"", generated, StringComparison.Ordinal);
            Assert.Contains("\\\"enabled\\\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase154/vehicle\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("new Dictionary<string, object>", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedFoxRunSchemaInfoRegistersAggregateJsonSchema()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
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
                            "Demo.VehicleTelemetry",
                            new[]
                            {
                                new FoxRunSchemaContractInfo(
                                    "Demo.VehicleTelemetry",
                                    "/phase154/vehicle",
                                    "Demo.VehicleTelemetry",
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
                                        new FoxRunSchemaFieldInfo("speed", "_speed", "field", "float", false, false, aggregate: true),
                                        new FoxRunSchemaFieldInfo("enabled", "_enabled", "field", "bool", false, false, aggregate: true)
                                    })
                            })
                    });
                var registry = new DefaultSchemaRegistry();

                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.True(registry.TryGetSchema("Demo.VehicleTelemetry", "jsonschema", out var entry));
                Assert.Equal("jsonschema", entry.Encoding);
                Assert.Contains("\"speed\":{\"type\":\"number\"}", entry.Content, StringComparison.Ordinal);
                Assert.Contains("\"enabled\":{\"type\":\"boolean\"}", entry.Content, StringComparison.Ordinal);
                Assert.Contains("\"required\":[\"speed\",\"enabled\"]", entry.Content, StringComparison.Ordinal);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        public void GeneratedFoxRunSchemaInfoSkipsLegacySingleFieldSchemaNames()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
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
                            "Demo.LegacyTelemetry",
                            new[]
                            {
                                new FoxRunSchemaContractInfo(
                                    "Demo.LegacyTelemetry",
                                    "/phase154/legacy",
                                    "foxglove.Log",
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
                                        new FoxRunSchemaFieldInfo("message", "_message", "field", "string", false, false)
                                    })
                            })
                    });
                var registry = new DefaultSchemaRegistry();

                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.False(registry.TryGetSchema("foxglove.Log", "jsonschema", out _));
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        private static MetadataReference[] BasicReferences()
        {
            return new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Unity.FoxgloveSDK.Components.FoxRunMessageAttribute).Assembly.Location)
            };
        }
    }
}
