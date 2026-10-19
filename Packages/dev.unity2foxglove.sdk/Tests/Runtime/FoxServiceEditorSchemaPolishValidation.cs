// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxService schema payload and Unity Editor polish contracts.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class FoxServiceEditorSchemaPolishValidation
    {
        private static readonly Lazy<MetadataReference[]> CachedReferences = new Lazy<MetadataReference[]>(CreateReferences);
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 141E Tests ---");
            _passCount = 0;

            VerifyGeneratedDescriptorSchemaSurface();
            VerifyRoslynGeneratedServiceSchemaPayloads();
            VerifySchemaPreviewMatchesRuntimeSerializationShape();
            VerifyDescriptorLiteralParserSkipsParenthesesInsideStrings();
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

            VerifySchemaParityFixtureSourceMatchesReflectionTypes();

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
            var hub = PhaseValidationSourceHelpers.ReadFoxgloveServiceHubSources();
            Check(hub.Contains("Schema = generated.RequestSchema", StringComparison.Ordinal)
                  && hub.Contains("Schema = generated.ResponseSchema", StringComparison.Ordinal),
                "141E-8: FoxgloveServiceHub forwards generated schema payloads into service descriptors");
            Check(hub.Contains("GetRegisteredServiceSnapshots", StringComparison.Ordinal),
                "141E-9: FoxgloveServiceHub exposes read-only registered service snapshots");
        }

        private static void VerifyManagerInspectorServiceStatusSurface()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var editorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            Check(editor.Contains("FoxServices", StringComparison.Ordinal)
                  && editor.Contains("DrawFoxServicesSection", StringComparison.Ordinal),
                "141E-10: FoxgloveManager Inspector includes a FoxServices section");
            var inspectorGui = PhaseValidationSourceHelpers.SourceMethod(editor, "OnInspectorGUI");
            var mcapIndex = inspectorGui.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal);
            var foxServicesIndex = inspectorGui.IndexOf("DrawSection(\"FoxServices\"", StringComparison.Ordinal);
            var diagnosticsIndex = inspectorGui.IndexOf("DrawSection(\"Diagnostics\"", StringComparison.Ordinal);
            var foxServicesSection = PhaseValidationSourceHelpers.SourceMethod(
                editorSources,
                "DrawFoxServicesSection");
            var foxRunIndex = foxServicesSection.IndexOf("FoxRun Runtime Topics", StringComparison.Ordinal);
            var generatedServicesIndex = foxServicesSection.IndexOf("Generated Services", StringComparison.Ordinal);
            Check(mcapIndex >= 0
                  && foxServicesIndex > mcapIndex
                  && diagnosticsIndex > foxServicesIndex
                  && foxRunIndex >= 0
                  && generatedServicesIndex > foxRunIndex,
                "141E-10a: FoxRun topics stay inside FoxServices between MCAP and Diagnostics");
            Check(editorSources.Contains("GetRegisteredServiceSnapshots", StringComparison.Ordinal)
                  && editorSources.Contains("EditorGUIUtility.systemCopyBuffer", StringComparison.Ordinal),
                "141E-11: FoxServices Inspector section reads snapshots and supports copy workflow");
            Check(editorSources.Contains("\" | Source: \"", StringComparison.Ordinal)
                  && editorSources.Contains("\" | Request: \"", StringComparison.Ordinal)
                  && editorSources.Contains("\" | Response: \"", StringComparison.Ordinal)
                  && editorSources.Contains("\" | Service Id: \"", StringComparison.Ordinal),
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
            var lineEnd = FindMatchingInvocationCloseParen(generated, lineStart);
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

        private static void VerifyDescriptorLiteralParserSkipsParenthesesInsideStrings()
        {
            var generated = "new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor("
                            + "\"/phase141e/paren\", \"value)\", \"{\\\"enum\\\":[\\\"a)\\\",\\\"b\\\"]}\")";

            var literals = ExtractDescriptorStringLiterals(generated, "/phase141e/paren");

            Check(literals.Length == 3
                  && literals[1] == "value)"
                  && literals[2].Contains("a)", StringComparison.Ordinal),
                "173-055-F1: descriptor literal parser ignores parentheses inside string literals");
        }

        private static int FindMatchingInvocationCloseParen(string source, int constructorIndex)
        {
            if (constructorIndex < 0)
                return -1;

            var openParen = source.IndexOf('(', constructorIndex);
            if (openParen < 0)
                return -1;

            var depth = 0;
            var inString = false;
            var inVerbatimString = false;
            for (var i = openParen; i < source.Length; i++)
            {
                var ch = source[i];
                if (inString)
                {
                    if (inVerbatimString)
                    {
                        if (ch == '"' && i + 1 < source.Length && source[i + 1] == '"')
                        {
                            i++;
                            continue;
                        }

                        if (ch == '"')
                        {
                            inString = false;
                            inVerbatimString = false;
                        }

                        continue;
                    }

                    if (ch == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (ch == '"')
                        inString = false;

                    continue;
                }

                if (ch == '@' && i + 1 < source.Length && source[i + 1] == '"')
                {
                    inString = true;
                    inVerbatimString = true;
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '(')
                {
                    depth++;
                    continue;
                }

                if (ch == ')')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }
    }
}
