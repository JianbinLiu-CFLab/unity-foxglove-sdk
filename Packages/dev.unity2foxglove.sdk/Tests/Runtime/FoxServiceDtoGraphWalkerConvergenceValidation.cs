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
    public static class FoxServiceDtoGraphWalkerConvergenceValidation
    {
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

        private static string PrivateJsonNameShadowFixtureSource()
            => @"
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace Phase141FPrivateShadow
{
    public class PrivateJsonNameShadowBase
    {
        [JsonProperty(""shared"")]
        public int BaseValue { get; set; }
    }

    public sealed class PrivateJsonNameShadowDerived : PrivateJsonNameShadowBase
    {
        [JsonProperty(""shared"")]
        private string PrivateValue { get; set; }
    }

    public sealed class PrivateJsonNameShadowRequest
    {
        public PrivateJsonNameShadowDerived Item { get; set; }
    }

    public partial class Fixture
    {
        [FoxService(""/phase141f/private-shadow"", Type = ""Phase141F.PrivateShadow"", RequestSchemaName = ""Phase141F.PrivateShadow.Request"", ResponseSchemaName = ""Phase141F.PrivateShadow.Response"")]
        private void Check(PrivateJsonNameShadowRequest request) {}
    }
}
";

        private static string MultiDimensionalArrayFixtureSource()
            => @"
using Unity.FoxgloveSDK.Components;

namespace Phase141FMultiDim
{
    public sealed class MultiDimensionalArrayRequest
    {
        public int[,] Grid { get; set; }
    }

    public partial class Fixture
    {
        [FoxService(""/phase141f/multidim"", Type = ""Phase141F.MultiDim"", RequestSchemaName = ""Phase141F.MultiDim.Request"", ResponseSchemaName = ""Phase141F.MultiDim.Response"")]
        private void Check(MultiDimensionalArrayRequest request) {}
    }
}
";

        private sealed class Leaf
        {
            public int Value { get; set; }
        }

        private sealed class DepthMemoRequest
        {
            public Leaf Shallow { get; set; }
            public DepthNode0 Deep { get; set; }
        }

        private sealed class DepthNode0 { public DepthNode1 Next { get; set; } }
        private sealed class DepthNode1 { public DepthNode2 Next { get; set; } }
        private sealed class DepthNode2 { public DepthNode3 Next { get; set; } }
        private sealed class DepthNode3 { public DepthNode4 Next { get; set; } }
        private sealed class DepthNode4 { public DepthNode5 Next { get; set; } }
        private sealed class DepthNode5 { public DepthNode6 Next { get; set; } }
        private sealed class DepthNode6 { public DepthNode7 Next { get; set; } }
        private sealed class DepthNode7 { public DepthNode8 Next { get; set; } }
        private sealed class DepthNode8 { public DepthNode9 Next { get; set; } }
        private sealed class DepthNode9 { public DepthNode10 Next { get; set; } }
        private sealed class DepthNode10 { public DepthNode11 Next { get; set; } }
        private sealed class DepthNode11 { public DepthNode12 Next { get; set; } }
        private sealed class DepthNode12 { public DepthNode13 Next { get; set; } }
        private sealed class DepthNode13 { public DepthNode14 Next { get; set; } }
        private sealed class DepthNode14 { public DepthNode15 Next { get; set; } }
        private sealed class DepthNode15 { public DepthNode16 Next { get; set; } }
        private sealed class DepthNode16 { public DepthNode17 Next { get; set; } }
        private sealed class DepthNode17 { public DepthNode18 Next { get; set; } }
        private sealed class DepthNode18 { public DepthNode19 Next { get; set; } }
        private sealed class DepthNode19 { public DepthNode20 Next { get; set; } }
        private sealed class DepthNode20 { public DepthNode21 Next { get; set; } }
        private sealed class DepthNode21 { public DepthNode22 Next { get; set; } }
        private sealed class DepthNode22 { public DepthNode23 Next { get; set; } }
        private sealed class DepthNode23 { public DepthNode24 Next { get; set; } }
        private sealed class DepthNode24 { public DepthNode25 Next { get; set; } }
        private sealed class DepthNode25 { public DepthNode26 Next { get; set; } }
        private sealed class DepthNode26 { public DepthNode27 Next { get; set; } }
        private sealed class DepthNode27 { public DepthNode28 Next { get; set; } }
        private sealed class DepthNode28 { public DepthNode29 Next { get; set; } }
        private sealed class DepthNode29 { public DepthNode30 Next { get; set; } }
        private sealed class DepthNode30 { public DepthNode31 Next { get; set; } }
        private sealed class DepthNode31 { public DepthNode32 Next { get; set; } }
        private sealed class DepthNode32 { public DepthNode33 Next { get; set; } }
        private sealed class DepthNode33 { public Leaf Leaf { get; set; } }

        private class HiddenBase
        {
            public object Value { get; set; }

            [JsonProperty("alias")]
            public object BaseAlias { get; set; }
        }

        private sealed class HiddenDerived : HiddenBase
        {
            public new int Value { get; set; }

            [JsonProperty("alias")]
            public int DerivedAlias { get; set; }
        }

        private sealed class HiddenMemberRequest
        {
            public HiddenDerived Item { get; set; }
        }

        private class PrivateJsonNameShadowBase
        {
            [JsonProperty("shared")]
            public int BaseValue { get; set; }
        }

        private sealed class PrivateJsonNameShadowDerived : PrivateJsonNameShadowBase
        {
            [JsonProperty("shared")]
            private string PrivateValue { get; set; }
        }

        private sealed class PrivateJsonNameShadowRequest
        {
            public PrivateJsonNameShadowDerived Item { get; set; }
        }

        private sealed class MultiDimensionalArrayRequest
        {
            public int[,] Grid { get; set; }
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath));

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passCount++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
