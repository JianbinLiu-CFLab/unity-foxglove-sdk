// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
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
    [CollectionDefinition("FoxRunSchemaRegistry")]
    public sealed class FoxRunSchemaRegistryCollectionDefinition
    {
    }

    [Collection("FoxRunSchemaRegistry")]
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
                        0, 0f, "UnitTest", 0, "",
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
        public void AggregateMemberEmitsBusSideChannelReusingExplicitJsonBytes()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "VehicleTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "VehicleTelemetry", "_speed", "field", "System.Single",
                        true, false, "", "/phase173/bus", 10f, "Demo.VehicleTelemetry",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: true, jsonFieldName: "speed")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("var __payload = __foxRunLastJson_0 ?? __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.Contains("bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), nowNs, in __payload,", source, StringComparison.Ordinal);
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
                        0, 0f, "UnitTest", 0, "",
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
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false,
                        jsonFieldName: "samples",
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("mgr.PublishJson(\"/phase155/array\", \"\"", source, StringComparison.Ordinal);
            Assert.Contains("var __sink_0 = __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_samples == null ? null : _samples.ToString()", source, StringComparison.Ordinal);
            Assert.Contains("__json.Append('[');", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LegacyStringSinkFanoutEscapesSurrogates()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "StringTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "StringTelemetry", "_text", "field", "System.String",
                        true, false, "", "/phase173/string", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "text")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("global::System.Char.IsSurrogate(__c)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LegacyUnityVector4AndColor32EmitStructuredJsonForSinkFanout()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "UnityTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "UnityTelemetry", "_vector", "field", "UnityEngine.Vector4",
                        true, false, "", "/phase173/unity", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false,
                        jsonFieldName: "vector",
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding),
                    new FoxRunGenerationMember(
                        "Demo", "UnityTelemetry", "_color", "field", "UnityEngine.Color32",
                        true, false, "", "/phase173/unity", 10f, "",
                        0, 0f, "UnitTest", 1, "",
                        isAggregateMember: false,
                        jsonFieldName: "color",
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("__foxRunCapture_0_1.w", source, StringComparison.Ordinal);
            Assert.Contains("((float)__foxRunCapture_0_0.r / 255f)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ArrayFieldContractFingerprintUsesElementCanonicalType()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "ArrayTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "ArrayTelemetry", "_samples", "field", "System.Single[]",
                        true, true, "System.Single", "/phase155/array", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "samples")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("fields=samples:float32", source, StringComparison.Ordinal);
            Assert.DoesNotContain("fields=samples:float[]", source, StringComparison.Ordinal);
        }

        [Fact]
        public void NullableIntegralFieldEmitsNumericJsonForSinkFanout()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "NullableTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "NullableTelemetry", "_optionalCount", "field", "System.Nullable<System.Int32>",
                        false, false, "", "/phase163/nullable", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "optionalCount")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("__foxRunCapture_0_0 == null", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunCapture_0_0.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("__AppendFoxRunJsonString(__json, __foxRunCapture_0_0 == null ? null : __foxRunCapture_0_0", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedFoxRunSchemaInfoContinuesAfterInvalidAggregateContract()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            var warnings = 0;
            void OnWarning(string message, Exception exception)
            {
                warnings++;
                Assert.Contains("/phase154/bad", message, StringComparison.Ordinal);
                Assert.IsType<InvalidOperationException>(exception);
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
                            "Demo.VehicleTelemetry",
                            new[]
                            {
                                new FoxRunSchemaContractInfo(
                                    "Demo.BadTelemetry",
                                    "/phase154/bad",
                                    "Demo.BadTelemetry",
                                    "json",
                                    "contract",
                                    "binding",
                                    "policy",
                                    "FixedRate",
                                    10f,
                                    0f,
                                    new[]
                                    {
                                        new FoxRunSchemaFieldInfo("payload", "_payload", "field", "object", false, false, aggregate: true)
                                    }),
                                new FoxRunSchemaContractInfo(
                                    "Demo.GoodTelemetry",
                                    "/phase154/good",
                                    "Demo.GoodTelemetry",
                                    "json",
                                    "contract",
                                    "binding",
                                    "policy",
                                    "FixedRate",
                                    10f,
                                    0f,
                                    new[]
                                    {
                                        new FoxRunSchemaFieldInfo("speed", "_speed", "field", "float", false, false, aggregate: true)
                                    })
                            })
                    });
                var registry = new DefaultSchemaRegistry();

                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.Equal(1, warnings);
                Assert.False(registry.TryGetSchema("Demo.BadTelemetry", "jsonschema", out _));
                Assert.True(registry.TryGetSchema("Demo.GoodTelemetry", "jsonschema", out var entry));
                Assert.Contains("\"speed\"", entry.Content, StringComparison.Ordinal);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed -= OnWarning;
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
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
        public void AggregateSchedulingUsesTheSameShortVocabulary()
        {
            var output = CreateCompilation(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    [FoxRunMessage(""/phase184/aggregate"", Policy = Change, Hz = 10f,
        Tolerance = 0.01f, OnlyIf = nameof(Enabled))]
    public partial class VehicleTelemetry
    {
        private bool Enabled => true;

        [FoxRunField(""speed"")]
        private float _speed;
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void GeneratedAggregatePublisherObserverSideChannelCompilesWithoutCaptureSequenceState()
        {
            var output = RunGeneratorAndUpdateCompilation(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    [FoxRunMessage(""/phase184/aggregate-observer"")]
    public partial class AggregatePublisher
    {
        [FoxRunField(""value"")]
        private float _value;
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
#endif

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

                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);
                Assert.True(registry.TryGetSchema("Demo.VehicleTelemetry", "jsonschema", out var cachedEntry));
                Assert.Equal(entry.Content, cachedEntry.Content);
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

        private static readonly MetadataReference[] BasicReferences =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Append(typeof(UnityEngine.Vector3).Assembly.Location)
            .Append(typeof(FoxRunMessageAttribute).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var compilation = CSharpCompilation.Create(
                "Phase154GeneratorProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                BasicReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static CSharpCompilation CreateCompilation(string source)
            => CSharpCompilation.Create(
                "Phase184AggregateGeneratorProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                BasicReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        private static Compilation RunGeneratorAndUpdateCompilation(string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out _);
            return outputCompilation;
        }

        private static void AssertGeneratorDiagnostic(string source, string diagnosticId)
        {
            var result = RunGenerator(source);
            var errors = result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();

            Assert.Contains(errors, diagnostic => diagnostic.Id == diagnosticId);
            Assert.All(errors, diagnostic => Assert.Equal(diagnosticId, diagnostic.Id));
        }
    }
}
