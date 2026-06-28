// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxService schema payload and Unity Editor polish contracts.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxServiceEditorSchemaPolishValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 141E Tests ---");
            _passCount = 0;

            VerifyGeneratedDescriptorSchemaSurface();
            VerifyRoslynGeneratedServiceSchemaPayloads();
            VerifySchemaPreviewMatchesRuntimeSerializationShape();
            VerifyHubForwardsGeneratedSchemas();
            VerifyManagerInspectorServiceStatusSurface();
            VerifyDocumentationPolish();
            VerifyValidationWiring();

            Console.WriteLine("Phase 141E: " + _passCount + " checks passed.\n");
        }

        private static void VerifyGeneratedDescriptorSchemaSurface()
        {
            var type = typeof(FoxgloveGeneratedServiceDescriptor);
            Check(type.GetProperty("RequestSchema") != null,
                "141E-1: generated descriptor exposes request schema payload");
            Check(type.GetProperty("ResponseSchema") != null,
                "141E-2: generated descriptor exposes response schema payload");
        }

        private static void VerifyRoslynGeneratedServiceSchemaPayloads()
        {
            var result = RunGenerator(ServiceFixtureSource());
            var generated = GeneratedFoxServiceSource(result);

            Check(generated.Contains("\"/phase141e/schema\"", StringComparison.Ordinal),
                "141E-3: Roslyn generator emits schema fixture service descriptor");
            Check(!generated.Contains("new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor($\"", StringComparison.Ordinal)
                  && !generated.Contains("new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor(@\"", StringComparison.Ordinal),
                "141E-3b: generated service descriptor arguments use regular string literals");
            var literals = ExtractDescriptorStringLiterals(generated, "/phase141e/schema");
            Check(literals.Length >= 7, "141E-5: generated descriptor includes schema payload constructor arguments");

            var requestSchema = JObject.Parse(literals[literals.Length - 2]);
            var responseSchema = JObject.Parse(literals[literals.Length - 1]);
            Check(requestSchema["properties"] != null
                  && responseSchema["properties"] != null,
                "141E-4: Roslyn generated descriptor carries request/response schema property previews");
            Check((string)requestSchema["type"] == "object"
                  && requestSchema["properties"]?["enabled"] != null
                  && requestSchema["properties"]?["count"] != null,
                "141E-6: request schema payload is valid JSON with DTO properties");
            Check((string)responseSchema["type"] == "object"
                  && responseSchema["properties"]?["status"] != null,
                "141E-7: response schema payload is valid JSON with DTO properties");
        }

        private static void VerifySchemaPreviewMatchesRuntimeSerializationShape()
        {
            var generator = RunGenerator(SchemaParityFixtureSource());
            Check(generator.Diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE007"),
                "141E-7a: warning-only DTO fixture still reports DTO warning diagnostics");

            var generated = GeneratedFoxServiceSource(generator);
            var roslynLiterals = ExtractDescriptorStringLiterals(generated, "/phase141e/schema_parity");
            var roslynRequest = JObject.Parse(roslynLiterals[roslynLiterals.Length - 2]);
            var roslynResponse = JObject.Parse(roslynLiterals[roslynLiterals.Length - 1]);

            var reflectionRequest = JObject.Parse(FoxServiceSchemaEmitter.Emit(FoxServiceSchemaReflectionBuilder.Build(
                typeof(SchemaParityRequest),
                FoxServiceDtoRules.RequestSide)));
            var reflectionResponse = JObject.Parse(FoxServiceSchemaEmitter.Emit(FoxServiceSchemaReflectionBuilder.Build(
                typeof(SchemaParityResponse),
                FoxServiceDtoRules.ResponseSide)));

            Check(JToken.DeepEquals(roslynRequest, reflectionRequest)
                  && JToken.DeepEquals(roslynResponse, reflectionResponse),
                "141E-7b: Roslyn and reflection schema previews are equivalent");

            var requestProperties = (JObject)roslynRequest["properties"];
            var responseProperties = (JObject)roslynResponse["properties"];
            Check(requestProperties?["user_id"]?["type"]?.Value<string>() == "integer"
                  && requestProperties["UserId"] == null,
                "141E-7c: schema previews honor Newtonsoft JsonProperty names");
            Check(requestProperties?["Secret"] == null
                  && requestProperties?["InternalNote"] == null,
                "141E-7d: Roslyn schema previews exclude private and internal DTO members");
            Check(requestProperties?["Mode"]?["type"]?.Value<string>() == "integer"
                  && responseProperties?["Mode"]?["type"]?.Value<string>() == "integer",
                "141E-7e: enum schema previews match Newtonsoft numeric enum serialization");
            Check(requestProperties?["ReadOnlyScalar"] != null,
                "141E-7f: warning-only DTO members do not suppress schema preview output");
        }

        private static void VerifyHubForwardsGeneratedSchemas()
        {
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxService/FoxgloveServiceHub.cs");
            Check(hub.Contains("Schema = generated.RequestSchema", StringComparison.Ordinal)
                  && hub.Contains("Schema = generated.ResponseSchema", StringComparison.Ordinal),
                "141E-8: FoxgloveServiceHub forwards generated schema payloads into service descriptors");
            Check(hub.Contains("GetRegisteredServiceSnapshots", StringComparison.Ordinal),
                "141E-9: FoxgloveServiceHub exposes read-only registered service snapshots");
        }

        private static void VerifyManagerInspectorServiceStatusSurface()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            Check(editor.Contains("FoxServices", StringComparison.Ordinal)
                  && editor.Contains("DrawFoxServicesSection", StringComparison.Ordinal),
                "141E-10: FoxgloveManager Inspector includes a FoxServices section");
            var mcapIndex = editor.IndexOf("\"MCAP Record & Replay\"", StringComparison.Ordinal);
            var foxServicesIndex = editor.IndexOf("\"FoxServices\"", StringComparison.Ordinal);
            var diagnosticsIndex = editor.IndexOf("\"Diagnostics\"", StringComparison.Ordinal);
            Check(mcapIndex >= 0
                  && foxServicesIndex > mcapIndex
                  && diagnosticsIndex > foxServicesIndex,
                "141E-10a: FoxServices Inspector section sits between MCAP and Diagnostics");
            Check(editor.Contains("GetRegisteredServiceSnapshots", StringComparison.Ordinal)
                  && editor.Contains("EditorGUIUtility.systemCopyBuffer", StringComparison.Ordinal),
                "141E-11: FoxServices Inspector section reads snapshots and supports copy workflow");
            Check(editor.Contains("\" | Source: \"", StringComparison.Ordinal)
                  && editor.Contains("\" | Request: \"", StringComparison.Ordinal)
                  && editor.Contains("\" | Response: \"", StringComparison.Ordinal)
                  && editor.Contains("\" | Service Id: \"", StringComparison.Ordinal),
                "141E-11a: Copy Service List includes service metadata, not just names");
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("FoxServiceEditorSchemaPolishValidation.cs", StringComparison.Ordinal),
                "141E-14: runtime test project includes FoxService editor/schema polish validation");
            Check(registry.Contains("--phase141e", StringComparison.Ordinal)
                  && registry.Contains("FoxServiceEditorSchemaPolishValidation.Validate", StringComparison.Ordinal),
                "141E-15: validation registry wires --phase141e");
        }

        private static void VerifyDocumentationPolish()
        {
            var services = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/06_Parameters_and_Services.md");
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/12_Inspector_Reference.md");

            Check(services.Contains("Generated Service Schemas", StringComparison.Ordinal)
                  && services.Contains("Copy Service List", StringComparison.Ordinal),
                "141E-12: services documentation mentions generated schema previews and copy workflow");
            Check(inspector.Contains("FoxServices", StringComparison.Ordinal)
                  && inspector.Contains("Copy Service List", StringComparison.Ordinal),
                "141E-13: Inspector reference documents the FoxServices section");
        }

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "Phase141EFoxServiceFixture",
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            return driver.GetRunResult();
        }

        private static string GeneratedFoxServiceSource(GeneratorDriverRunResult result)
        {
            var generated = result.Results
                .SelectMany(item => item.GeneratedSources)
                .FirstOrDefault(item => item.HintName.EndsWith("_FoxService.g.cs", StringComparison.Ordinal));
            if (generated.HintName == null)
                throw new InvalidOperationException("Generated sources do not contain an expected *_FoxService.g.cs hint.");
            return generated.SourceText.ToString();
        }

        private static string[] ExtractDescriptorStringLiterals(string generated, string serviceName)
        {
            var serviceIndex = generated.IndexOf(serviceName, StringComparison.Ordinal);
            if (serviceIndex < 0)
                return Array.Empty<string>();

            var lineStart = generated.LastIndexOf("new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor", serviceIndex, StringComparison.Ordinal);
            var lineEnd = generated.IndexOf(")", serviceIndex, StringComparison.Ordinal);
            if (lineStart < 0 || lineEnd < 0)
                return Array.Empty<string>();

            var descriptor = generated.Substring(lineStart, lineEnd - lineStart);
            var values = new System.Collections.Generic.List<string>();
            var i = 0;
            while (i < descriptor.Length)
            {
                if (descriptor[i] != '"')
                {
                    i++;
                    continue;
                }

                var start = i;
                i++;
                var escaped = false;
                while (i < descriptor.Length)
                {
                    var ch = descriptor[i++];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (ch == '"')
                        break;
                }

                var literal = descriptor.Substring(start, i - start);
                values.Add(JToken.Parse(literal).Value<string>());
            }

            return values.ToArray();
        }

        private static MetadataReference[] References()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase141E Roslyn reference resolution.");

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(typeof(FoxServiceAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(JToken).Assembly.Location)
                })
                .ToArray();
        }

        private static string ServiceFixtureSource()
            => @"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace Phase141E
{
    public sealed class SchemaRequest
    {
        public bool enabled { get; set; }
        public int count { get; set; }
        public List<string> labels { get; set; }
    }

    public sealed class SchemaResponse
    {
        public string status { get; set; }
        public Dictionary<string, float> metrics { get; set; }
    }

    public partial class SchemaFixture
    {
        [FoxService(""/phase141e/schema"", Type = ""Phase141E.Schema"")]
        private SchemaResponse Check(SchemaRequest request)
        {
            return new SchemaResponse { status = request != null ? ""ok"" : ""missing"" };
        }
    }
}
";

        private static string SchemaParityFixtureSource()
            => @"
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace Phase141EParity
{
    public enum Mode
    {
        Idle = 0,
        Run = 1
    }

    public sealed class SchemaParityRequest
    {
        [JsonProperty(""user_id"")]
        public int UserId { get; set; }

        private string Secret { get; set; }

        internal string InternalNote { get; set; }

        public Mode Mode { get; set; }

        public string ReadOnlyScalar { get; } = ""readonly"";
    }

    public sealed class SchemaParityResponse
    {
        [JsonProperty(""status_text"")]
        public string StatusText { get; set; }

        public Mode Mode { get; set; }
    }

    public partial class SchemaParityFixture
    {
        [FoxService(""/phase141e/schema_parity"", Type = ""Phase141E.SchemaParity"", RequestSchemaName = ""Phase141E.SchemaParity.Request"", ResponseSchemaName = ""Phase141E.SchemaParity.Response"")]
        private SchemaParityResponse Check(SchemaParityRequest request)
        {
            return new SchemaParityResponse { StatusText = ""ok"", Mode = Mode.Run };
        }
    }
}
";

        private enum SchemaParityMode
        {
            Idle = 0,
            Run = 1
        }

        private sealed class SchemaParityRequest
        {
            [JsonProperty("user_id")]
            public int UserId { get; set; }

            private string Secret { get; set; }

            internal string InternalNote { get; set; }

            public SchemaParityMode Mode { get; set; }

            public string ReadOnlyScalar { get; } = "readonly";
        }

        private sealed class SchemaParityResponse
        {
            [JsonProperty("status_text")]
            public string StatusText { get; set; }

            public SchemaParityMode Mode { get; set; }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase141E validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
