// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 141F FoxService DTO graph walker convergence.

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
    public static partial class FoxServiceDtoGraphWalkerConvergenceValidation
    {
        private static readonly Lazy<MetadataReference[]> CachedReferences = new Lazy<MetadataReference[]>(CreateReferences);
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 141F Tests ---");
            _passCount = 0;

            VerifyDepthMemoDoesNotHideDeepTraversal();
            VerifyHiddenMembersUseDerivedJsonShape();
            VerifyPrivateDerivedJsonNameDoesNotHidePublicBaseMember();
            VerifyMultiDimensionalArraysAreBlockingInBothPaths();
            VerifyValidationWiring();

            Console.WriteLine("Phase 141F: " + _passCount + " checks passed.\n");
        }

        private static void VerifyDepthMemoDoesNotHideDeepTraversal()
        {
            var diagnostics = RunGenerator(DepthMemoFixtureSource()).Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();

            Check(diagnostics.Any(diagnostic => diagnostic.Id == FoxServiceDtoRules.DepthDiagnosticId
                                                && diagnostic.GetMessage().Contains("Request.Deep", StringComparison.Ordinal)),
                "141F-1: Roslyn DTO walker reports deep graph even when leaf type was seen shallowly first");

            var reflectionDiagnostics = FoxServiceDtoReflectionValidator.Validate(
                typeof(DepthMemoRequest),
                FoxServiceDtoSide.Request,
                "/phase141f/depth");
            Check(reflectionDiagnostics.Any(diagnostic => diagnostic.Id == FoxServiceDtoRules.DepthDiagnosticId
                                                          && diagnostic.Path.StartsWith("Request.Deep", StringComparison.Ordinal)),
                "141F-2: reflection DTO walker reports deep graph even when leaf type was seen shallowly first");
        }

        private static void VerifyHiddenMembersUseDerivedJsonShape()
        {
            var result = RunGenerator(HiddenMemberFixtureSource());
            var diagnostics = result.Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();
            Check(!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                "141F-3: Roslyn DTO walker lets derived hidden members override unsupported base members");

            var generated = GeneratedFoxServiceSource(result);
            var literals = ExtractDescriptorStringLiterals(generated, "/phase141f/hidden");
            var roslynSchema = JObject.Parse(literals[literals.Length - 2]);
            var reflectionSchema = JObject.Parse(FoxServiceSchemaEmitter.Emit(FoxServiceSchemaReflectionBuilder.Build(
                typeof(HiddenMemberRequest),
                FoxServiceDtoRules.RequestSide)));

            Check(JToken.DeepEquals(roslynSchema, reflectionSchema),
                "141F-4: hidden member schema shape matches in Roslyn and reflection paths");

            var itemProperties = (JObject)roslynSchema["properties"]?["Item"]?["properties"];
            Check(itemProperties?["Value"]?["type"]?.Value<string>() == "integer"
                  && itemProperties?["alias"]?["type"]?.Value<string>() == "integer"
                  && itemProperties.Properties().Count(property => property.Name == "alias") == 1,
                "141F-5: hidden member schema uses derived JSON property names once");

            var reflectionDiagnostics = FoxServiceDtoReflectionValidator.Validate(
                typeof(HiddenMemberRequest),
                FoxServiceDtoSide.Request,
                "/phase141f/hidden");
            Check(!reflectionDiagnostics.Any(diagnostic => !diagnostic.IsWarning),
                "141F-6: reflection DTO walker matches derived hidden member validation semantics");
        }

        private static void VerifyPrivateDerivedJsonNameDoesNotHidePublicBaseMember()
        {
            var result = RunGenerator(PrivateJsonNameShadowFixtureSource());
            var diagnostics = result.Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();
            Check(!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                "141F-7: Roslyn DTO walker accepts private JSON-name shadows without losing public base members");

            var generated = GeneratedFoxServiceSource(result);
            var literals = ExtractDescriptorStringLiterals(generated, "/phase141f/private-shadow");
            var roslynSchema = JObject.Parse(literals[literals.Length - 2]);
            var reflectionSchema = JObject.Parse(FoxServiceSchemaEmitter.Emit(FoxServiceSchemaReflectionBuilder.Build(
                typeof(PrivateJsonNameShadowRequest),
                FoxServiceDtoRules.RequestSide)));

            Check(JToken.DeepEquals(roslynSchema, reflectionSchema),
                "141F-8: private JSON-name shadow schema shape matches in Roslyn and reflection paths");

            var itemProperties = (JObject)roslynSchema["properties"]?["Item"]?["properties"];
            Check(itemProperties?["shared"]?["type"]?.Value<string>() == "integer"
                  && itemProperties.Properties().Count(property => property.Name == "shared") == 1,
                "141F-9: private JSON-name shadow keeps the public base property once");
        }

        private static void VerifyMultiDimensionalArraysAreBlockingInBothPaths()
        {
            var result = RunGenerator(MultiDimensionalArrayFixtureSource());
            var diagnostics = result.Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();

            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE003"
                                                && diagnostic.GetMessage().Contains("Request.Grid", StringComparison.Ordinal)),
                "141F-10: Roslyn rejects multi-dimensional request arrays");

            Check(!TryGeneratedFoxServiceSource(result, out _),
                "141F-11: blocked multi-dimensional array service does not emit a descriptor");

            var reflectionDiagnostics = FoxServiceDtoReflectionValidator.Validate(
                typeof(MultiDimensionalArrayRequest),
                FoxServiceDtoSide.Request,
                "/phase141f/multidim");
            Check(reflectionDiagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE003"
                                                          && diagnostic.Path.Contains("Grid", StringComparison.Ordinal)),
                "141F-12: reflection rejects multi-dimensional request arrays");
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("FoxServiceDtoGraphWalkerConvergenceValidation.cs", StringComparison.Ordinal),
                "141F-13: runtime test project includes graph walker convergence validation");
            Check(registry.Contains("--phase141f", StringComparison.Ordinal)
                  && registry.Contains("FoxServiceDtoGraphWalkerConvergenceValidation.Validate", StringComparison.Ordinal),
                "141F-14: validation registry wires --phase141f");
        }

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "Phase141FFoxServiceFixture",
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                // FoxgloveLogSourceGenerator is intentionally the combined
                // Roslyn entry point for both [FoxRun] and [FoxService].
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

        private static bool TryGeneratedFoxServiceSource(GeneratorDriverRunResult result, out string source)
        {
            var generated = result.Results
                .SelectMany(item => item.GeneratedSources)
                .FirstOrDefault(item => item.HintName.EndsWith("_FoxService.g.cs", StringComparison.Ordinal));
            if (generated.HintName == null)
            {
                source = string.Empty;
                return false;
            }

            source = generated.SourceText.ToString();
            return true;
        }

        private static string[] ExtractDescriptorStringLiterals(string generated, string serviceName)
        {
            var serviceIndex = generated.IndexOf(serviceName, StringComparison.Ordinal);
            if (serviceIndex < 0)
                return Array.Empty<string>();

            var lineStart = generated.LastIndexOf("new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor", serviceIndex, StringComparison.Ordinal);
            var lineEnd = FindDescriptorConstructorEnd(generated, lineStart);
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

        private static int FindDescriptorConstructorEnd(string generated, int start)
        {
            if (start < 0)
                return -1;

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < generated.Length; i++)
            {
                var ch = generated[i];
                if (inString)
                {
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
                        inString = false;
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

        private static MetadataReference[] References()
            => CachedReferences.Value;

        private static MetadataReference[] CreateReferences()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase141F Roslyn reference resolution.");

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

        private static string DepthMemoFixtureSource()
        {
            // MaxDepth is 32; 34 links force the deep branch past the limit even
            // when the same leaf type has already been accepted through Shallow.
            var nodes = string.Join(Environment.NewLine, Enumerable.Range(0, 34).Select(index =>
                index == 33
                    ? "    public sealed class DepthNode33 { public Leaf Leaf { get; set; } }"
                    : "    public sealed class DepthNode" + index + " { public DepthNode" + (index + 1) + " Next { get; set; } }"));

            return @"
using Unity.FoxgloveSDK.Components;

namespace Phase141FDepth
{
    public sealed class Leaf { public int Value { get; set; } }
    public sealed class DepthRequest
    {
        public Leaf Shallow { get; set; }
        public DepthNode0 Deep { get; set; }
    }
" + nodes + @"

    public partial class Fixture
    {
        [FoxService(""/phase141f/depth"", Type = ""Phase141F.Depth"", RequestSchemaName = ""Phase141F.Depth.Request"", ResponseSchemaName = ""Phase141F.Depth.Response"")]
        private void Check(DepthRequest request) {}
    }
}
";
        }

        private static string HiddenMemberFixtureSource()
            => @"
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace Phase141FHidden
{
    public class HiddenBase
    {
        public object Value { get; set; }

        [JsonProperty(""alias"")]
        public object BaseAlias { get; set; }
    }

    public class HiddenDerived : HiddenBase
    {
        public new int Value { get; set; }

        [JsonProperty(""alias"")]
        public int DerivedAlias { get; set; }
    }

    public sealed class HiddenRequest
    {
        public HiddenDerived Item { get; set; }
    }

    public partial class Fixture
    {
        [FoxService(""/phase141f/hidden"", Type = ""Phase141F.Hidden"", RequestSchemaName = ""Phase141F.Hidden.Request"", ResponseSchemaName = ""Phase141F.Hidden.Response"")]
        private void Check(HiddenRequest request) {}
    }
}
";
    }
}
