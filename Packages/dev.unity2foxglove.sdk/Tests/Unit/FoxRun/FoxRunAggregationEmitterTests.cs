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
        public void AggregateMemberEmitsSinkFanoutSideChannelReusingExplicitJsonBytes()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "VehicleTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "VehicleTelemetry", "_speed", "field", "System.Single",
                        true, false, "", "/phase155/vehicle", 10f, "Demo.VehicleTelemetry",
                        0, 0f, 0f, "UnitTest", 0, "",
                        isAggregateMember: true, jsonFieldName: "speed")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("IFoxgloveTopicSinkSource", source, StringComparison.Ordinal);
            Assert.Contains("void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)", source, StringComparison.Ordinal);
            Assert.Contains("if (router == null || !router.HasSinks)", source, StringComparison.Ordinal);
            Assert.Contains("private byte[] __foxRunLastJson_0;", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunLastJson_0 = __payload_0;", source, StringComparison.Ordinal);
            Assert.Contains("var __sink_0 = __foxRunLastJson_0 ?? __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunLastJson_0 = null;", source, StringComparison.Ordinal);
            Assert.Contains("router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), nowNs, __sink_0,", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LegacySingleFieldTopicEmitsSinkFanoutSideChannel()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "ScalarTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "ScalarTelemetry", "_status", "field", "System.String",
                        true, false, "", "/phase155/status", 10f, "foxglove.Log",
                        0, 0f, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "message")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("mgr.PublishJson(\"/phase155/status\", \"foxglove.Log\"", source, StringComparison.Ordinal);
            Assert.Contains("void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)", source, StringComparison.Ordinal);
            Assert.Contains("var __sink_0 = __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.Contains("router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), nowNs, __sink_0,", source, StringComparison.Ordinal);
            Assert.Contains("\\\"message\\\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LegacyArrayFieldTopicEmitsJsonArrayForSinkFanout()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "ArrayTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "ArrayTelemetry", "_samples", "field", "System.Single[]",
                        true, false, "", "/phase155/array", 10f, "",
                        0, 0f, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "samples")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("mgr.PublishJson(\"/phase155/array\", \"\"", source, StringComparison.Ordinal);
            Assert.Contains("var __sink_0 = __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_samples == null ? null : _samples.ToString()", source, StringComparison.Ordinal);
            Assert.Contains("__json.Append('[');", source, StringComparison.Ordinal);
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
            var result = RunGenerator(source);
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
        public void RoslynGeneratorRejectsFoxRunFieldOutsideMessage()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class VehicleTelemetry
    {
        [FoxRunField(""speed"")]
        private float _speed;
    }
}";

            AssertGeneratorDiagnostic(source, "FOXRUN018");
        }

        [Fact]
        public void RoslynGeneratorRejectsAggregateArrayFields()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(""/phase154/vehicle"")]
    public partial class VehicleTelemetry
    {
        [FoxRunField(""samples"")]
        private float[] _samples;
    }
}";

            AssertGeneratorDiagnostic(source, "FOXRUN020");
        }

        [Fact]
        public void RoslynGeneratorRejectsUnsupportedAggregateFieldTypes()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(""/phase154/vehicle"")]
    public partial class VehicleTelemetry
    {
        [FoxRunField(""payload"")]
        private object _payload;
    }
}";

            AssertGeneratorDiagnostic(source, "FOXRUN006");
        }

        [Fact]
        public void RoslynGeneratorRejectsMixedAggregateAndFieldLevelTopics()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(""/phase154/vehicle"")]
    public partial class VehicleTelemetry
    {
        [FoxRunField(""speed"")]
        private float _speed;

        [FoxRun(""/phase154/vehicle"")]
        private float _legacySpeed;
    }
}";

            AssertGeneratorDiagnostic(source, "FOXRUN019");
        }

        [Fact]
        public void RoslynGeneratorRejectsDuplicateAggregateJsonFieldNames()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(""/phase154/vehicle"")]
    public partial class VehicleTelemetry
    {
        [FoxRunField(""speed"")]
        private float _speed;

        [FoxRunField(""speed"")]
        private float _velocity;
    }
}";

            AssertGeneratorDiagnostic(source, "FOXRUN022");
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
                                        new FoxRunSchemaFieldInfo("enabled", "_enabled", "field", "bool", false, false, aggregate: true),
                                        new FoxRunSchemaFieldInfo("position", "_position", "field", "unity.vector3.float32", false, false, aggregate: true)
                                    })
                            })
                    });
                var registry = new DefaultSchemaRegistry();

                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.True(registry.TryGetSchema("Demo.VehicleTelemetry", "jsonschema", out var entry));
                Assert.Equal("jsonschema", entry.Encoding);
                Assert.Contains("\"speed\":{\"anyOf\":[{\"type\":\"number\"},{\"type\":\"null\"}]}", entry.Content, StringComparison.Ordinal);
                Assert.Contains("\"enabled\":{\"type\":\"boolean\"}", entry.Content, StringComparison.Ordinal);
                Assert.Contains("\"position\":{\"type\":\"object\"", entry.Content, StringComparison.Ordinal);
                Assert.Contains("\"x\":{\"anyOf\":[{\"type\":\"number\"},{\"type\":\"null\"}]}", entry.Content, StringComparison.Ordinal);
                Assert.Contains("\"required\":[\"speed\",\"enabled\",\"position\"]", entry.Content, StringComparison.Ordinal);
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

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var compilation = CSharpCompilation.Create(
                "Phase154GeneratorProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                BasicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static void AssertGeneratorDiagnostic(string source, string diagnosticId)
        {
            var result = RunGenerator(source);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == diagnosticId);
        }
    }
}
