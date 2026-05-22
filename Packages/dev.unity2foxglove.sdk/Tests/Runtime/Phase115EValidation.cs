// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxRun analyzer diagnostics and generation-model equivalence.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115EValidation
    {
        private const string FixtureRelativePath = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture.cs";
        private const string GoldenRelativePath = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture_FoxRun.golden.cs";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115E: FoxRun Analyzer Diagnostics And Model Equivalence ===");
            _passed = 0;

            VerifyRoslynHarnessCanRunGeneratorSource();
            VerifyReflectionHarnessCanLoadFixtureAssembly();
            VerifySharedModelSourceBoundaries();
            VerifyCanonicalTypeNormalizer();
            VerifyRoslynReflectionDescriptorEquivalence();
            VerifyDescriptorComparerSemantics();
            VerifyOptionalDescriptorSidecarBehavior();
            VerifyCheckedInAnalyzerDllContains115EArtifacts();

            Console.WriteLine($"Phase 115E: {_passed} checks passed.");
        }

        private static void VerifyRoslynHarnessCanRunGeneratorSource()
        {
            var source = ReadRepoText(FixtureRelativePath);
            var compilation = CreateCompilation(source, "FoxRunGenerationModelFixtureRoslyn");
            var generator = new FoxgloveLogSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { generator.AsSourceGenerator() },
                parseOptions: FixtureParseOptions());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            var result = driver.GetRunResult();
            var generated = result.Results.SelectMany(r => r.GeneratedSources).ToList();
            var foxRunSource = generated
                .FirstOrDefault(s => s.HintName.EndsWith("_FoxRun.g.cs", StringComparison.Ordinal))
                .SourceText
                .ToString();

            Check(!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
                "115E-A1: Roslyn generator source harness runs without generator errors");
            Check(foxRunSource.Contains("FoxRun_TriggerAll", StringComparison.Ordinal),
                "115E-A2: Roslyn generator source harness extracts generated FoxRun source");
            Check(generated.Any(s => s.HintName == "FoxRunGeneratedDescriptorInfo.g.cs"
                                     && s.SourceText.ToString().Contains("DescriptorJson", StringComparison.Ordinal)),
                "115E-A5: Roslyn generator source harness exposes descriptor carrier source");
            var golden = ReadRepoText(GoldenRelativePath);
            Check(NormalizeNewlines(foxRunSource).TrimEnd() == NormalizeNewlines(golden).TrimEnd(),
                "115E-A4: Roslyn generated source matches the golden baseline");
        }

        private static void VerifyReflectionHarnessCanLoadFixtureAssembly()
        {
            var source = ReadRepoText(FixtureRelativePath);
            var compilation = CreateCompilation(source, "FoxRunGenerationModelFixtureReflection");
            var outputPath = Path.Combine(Path.GetTempPath(), "foxrun_generation_model_fixture_" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                using (var stream = File.Create(outputPath))
                {
                    var emit = compilation.Emit(stream);
                    if (!emit.Success)
                    {
                        var errors = string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                        throw new InvalidOperationException(errors);
                    }
                }

                var assembly = Assembly.LoadFile(outputPath);
                var type = assembly.GetType("Unity.FoxgloveSDK.Tests.Fixtures.FoxRunGenerationModelFixture");
                var member = type?.GetMember("_value").FirstOrDefault();
                var attr = member?.GetCustomAttributes(false).FirstOrDefault(a => a.GetType().FullName == typeof(FoxRunAttribute).FullName);
                Check(type != null && member != null && attr != null,
                    "115E-A3: reflection harness compiles and scans a FoxRun fixture outside Unity");
            }
            finally
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); }
                catch { /* Best-effort cleanup only. */ }
            }
        }

        private static void VerifySharedModelSourceBoundaries()
        {
            Check(File.Exists(RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs")),
                "115E-B1: shared FoxRunGenerationModel source exists");
            Check(File.Exists(RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorJsonWriter.cs")),
                "115E-B2: shared generation descriptor writer source exists");
            Check(File.Exists(RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunCanonicalTypeNormalizer.cs")),
                "115E-B3: shared canonical type normalizer source exists");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj")
                      .Contains("FoxRunDescriptor", StringComparison.Ordinal)
                  && ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj")
                      .Contains("FoxRunDescriptor", StringComparison.Ordinal),
                "115E-B4: source-generator and runtime tests explicitly link shared descriptor sources");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs")
                      .Contains("FoxRunReflectionGenerationModelLowerer.Lower", StringComparison.Ordinal)
                  && ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs")
                      .Contains("FoxRunRoslynGenerationModelLowerer.Lower", StringComparison.Ordinal),
                "115E-B5: Roslyn and build-time hosts lower into FoxRunGenerationModel before emission");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter.cs")
                      .Contains("EmitClass(FoxRunGenerationType type)", StringComparison.Ordinal),
                "115E-B6: shared emitter accepts FoxRunGenerationModel type input");
        }

        private static void VerifyCanonicalTypeNormalizer()
        {
            Check(FoxRunCanonicalTypeNormalizer.NormalizeTypeName("float") == "float32"
                  && FoxRunCanonicalTypeNormalizer.NormalizeTypeName("System.Single") == "float32"
                  && FoxRunCanonicalTypeNormalizer.NormalizeTypeName("System.Nullable`1[[System.Int32, mscorlib]]") == "int32"
                  && FoxRunCanonicalTypeNormalizer.NormalizeTypeName("UnityEngine.Vector3") == "unity.vector3.float32",
                "115E-C1: shared canonical type normalizer covers aliases, nullable wrappers, and Unity value types");
        }

        private static void VerifyRoslynReflectionDescriptorEquivalence()
        {
            var roslynDescriptor = ExtractDescriptorJsonFromRoslyn();
            var reflectionModel = BuildReflectionModelFromFixtureAssembly();
            var reflectionDescriptor = FoxRunGenerationDescriptorJsonWriter.Write(reflectionModel);
            var roslynSemantic = SemanticDescriptor(roslynDescriptor);
            var reflectionSemantic = SemanticDescriptor(reflectionDescriptor);

            Check(JToken.DeepEquals(roslynSemantic, reflectionSemantic),
                "115E-D1: Roslyn descriptor and reflection descriptor are semantically equivalent at fixture scope");
            Check(roslynDescriptor.Contains("\"hostKind\":\"Roslyn\"", StringComparison.Ordinal)
                  && reflectionDescriptor.Contains("\"hostKind\":\"Reflection\"", StringComparison.Ordinal),
                "115E-D2: descriptor provenance records host differences without defining semantic equality");
            Check(!roslynDescriptor.Contains(RepoRoot, StringComparison.OrdinalIgnoreCase)
                  && !reflectionDescriptor.Contains(RepoRoot, StringComparison.OrdinalIgnoreCase),
                "115E-D3: descriptors exclude machine-local repository paths");
        }

        private static void VerifyDescriptorComparerSemantics()
        {
            var left = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "A", new[]
                {
                    Member("Demo", "A", "_value", "/a", "float", "Roslyn", 1)
                })
            });
            var rightDifferentTopic = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "A", new[]
                {
                    Member("Demo", "A", "_value", "/b", "float", "Reflection", 9)
                })
            });
            var rightProvenanceOnly = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "A", new[]
                {
                    Member("Demo", "A", "_value", "/a", "System.Single", "Reflection", 9)
                })
            });

            Check(!FoxRunGenerationDescriptorComparer.Compare(left, rightDifferentTopic).IsSemanticEqual,
                "115E-E1: descriptor comparer fails on semantic topic drift");
            var provenance = FoxRunGenerationDescriptorComparer.Compare(left, rightProvenanceOnly);
            Check(provenance.IsSemanticEqual && provenance.ProvenanceDifferences.Count > 0,
                "115E-E2: descriptor comparer reports provenance-only drift without failing semantic equality");
        }

        private static void VerifyOptionalDescriptorSidecarBehavior()
        {
            var root = Path.Combine(Path.GetTempPath(), "foxrun_115e_sidecar_" + Guid.NewGuid().ToString("N"));
            var mcapPath = Path.Combine(root, "recording.mcap");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "current", "FoxRun"));
                Directory.CreateDirectory(Path.Combine(root, "current", "Unity2Foxglove"));
                File.WriteAllText(mcapPath, "mcap");
                File.WriteAllText(Path.Combine(root, "current", "FoxRun", "foxrun.manifest.json"), "{}");
                File.WriteAllText(Path.Combine(root, "current", "FoxRun", "foxrun.manifest.hash"), "abc\n");
                File.WriteAllText(Path.Combine(root, "current", "FoxRun", "foxrun.manifest.report.json"), "{}");
                File.WriteAllText(Path.Combine(root, "current", "FoxRun", "FoxRunSchemaInfo.g.cs"), "// info");
                File.WriteAllText(Path.Combine(root, "current", "Unity2Foxglove", "unity2foxglove.schema-manifest.json"), "{}");
                File.WriteAllText(Path.Combine(root, "current", "Unity2Foxglove", "unity2foxglove.schema-manifest.hash"), "def\n");
                File.WriteAllText(Path.Combine(root, "current", "Unity2Foxglove", "unity2foxglove.schema-manifest.report.json"), "{}");

                var result = SchemaEvidenceSidecarWriter.WriteSidecar(
                    mcapPath,
                    Path.Combine(root, "current"),
                    SchemaIdentityMode.Strict,
                    requireComplete: true);
                Check(result.Success && result.Complete
                                      && result.Warnings.Any(w => w.Contains("Optional schema evidence file missing", StringComparison.Ordinal)),
                    "115E-F1: missing generation descriptor is optional sidecar evidence and does not block Strict recording");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
                catch { /* Best-effort cleanup only. */ }
            }
        }

        private static void VerifyCheckedInAnalyzerDllContains115EArtifacts()
        {
            var dllPath = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll");
            var metaPath = dllPath + ".meta";
            var dllBytes = File.Exists(dllPath) ? File.ReadAllBytes(dllPath) : Array.Empty<byte>();
            var meta = File.Exists(metaPath) ? File.ReadAllText(metaPath) : string.Empty;
            Check(File.Exists(dllPath)
                  && BytesContainText(dllBytes, "FOXRUN006")
                  && BytesContainText(dllBytes, "FoxRunGeneratedDescriptorInfo"),
                "115E-G1: checked-in Unity analyzer DLL contains 115E diagnostics and descriptor carrier");
            Check(meta.Contains("RoslynAnalyzer", StringComparison.Ordinal),
                "115E-G2: checked-in analyzer DLL meta keeps the RoslynAnalyzer label");
        }

        private static CSharpCompilation CreateCompilation(string source, string assemblyName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, FixtureParseOptions());
            return CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static CSharpParseOptions FixtureParseOptions()
            => CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.CSharp9)
                .WithPreprocessorSymbols("FOXRUN_FIXTURE_EXTRA");

        private static MetadataReference[] References()
        {
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>();
            var local = new[]
            {
                MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location)
            };
            return trusted.Concat(local).ToArray();
        }

        private static string ExtractDescriptorJsonFromRoslyn()
        {
            var source = ReadRepoText(FixtureRelativePath);
            var compilation = CreateCompilation(source, "FoxRunGenerationModelFixtureDescriptor");
            var generator = new FoxgloveLogSourceGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { generator.AsSourceGenerator() },
                parseOptions: FixtureParseOptions());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            var descriptorSource = driver.GetRunResult()
                .Results
                .SelectMany(r => r.GeneratedSources)
                .First(s => s.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();
            var match = Regex.Match(descriptorSource, "DescriptorJson = \"(?<json>.*)\";");
            if (!match.Success)
                throw new InvalidOperationException("Could not extract DescriptorJson from Roslyn descriptor carrier.");
            return Regex.Unescape(match.Groups["json"].Value);
        }

        private static FoxRunGenerationModel BuildReflectionModelFromFixtureAssembly()
        {
            var source = ReadRepoText(FixtureRelativePath);
            var compilation = CreateCompilation(source, "FoxRunGenerationModelFixtureReflectionModel");
            var outputPath = Path.Combine(Path.GetTempPath(), "foxrun_generation_model_reflection_" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                using (var stream = File.Create(outputPath))
                {
                    var emit = compilation.Emit(stream);
                    if (!emit.Success)
                    {
                        var errors = string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                        throw new InvalidOperationException(errors);
                    }
                }

                var assembly = Assembly.LoadFile(outputPath);
                var type = assembly.GetType("Unity.FoxgloveSDK.Tests.Fixtures.FoxRunGenerationModelFixture")
                           ?? throw new InvalidOperationException("Missing reflection fixture type.");
                var members = new List<FoxRunReflectionGenerationMember>();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var field in type.GetFields(flags))
                    AddReflectionMembers(type, field.Name, "field", field.FieldType, field.MetadataToken, field.GetCustomAttributes(false), members);
                foreach (var property in type.GetProperties(flags))
                    AddReflectionMembers(type, property.Name, "property", property.PropertyType, property.MetadataToken, property.GetCustomAttributes(false), members);
                return FoxRunReflectionGenerationModelLowerer.Lower(members);
            }
            finally
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); }
                catch { /* Best-effort cleanup only. */ }
            }
        }

        private static void AddReflectionMembers(
            Type declaringType,
            string memberName,
            string memberKind,
            Type memberType,
            int rawMemberOrder,
            object[] attributes,
            List<FoxRunReflectionGenerationMember> members)
        {
            foreach (var attr in attributes.Where(a => a.GetType().FullName == typeof(FoxRunAttribute).FullName))
            {
                var attrType = attr.GetType();
                var topic = (string)attrType.GetProperty("Topic").GetValue(attr, null);
                var rateHz = (float)attrType.GetProperty("RateHz").GetValue(attr, null);
                var schemaName = (string)attrType.GetProperty("SchemaName").GetValue(attr, null) ?? string.Empty;
                var publishMode = Convert.ToInt32(attrType.GetProperty("PublishMode").GetValue(attr, null));
                var changeEpsilon = (float)attrType.GetProperty("ChangeEpsilon").GetValue(attr, null);
                var forceIntervalSeconds = (float)attrType.GetProperty("ForceIntervalSeconds").GetValue(attr, null);
                var isArray = TryGetArrayElementType(memberType, out var elementType);
                members.Add(new FoxRunReflectionGenerationMember(
                    declaringType.Namespace ?? string.Empty,
                    declaringType.Name,
                    memberName,
                    memberKind,
                    memberType.FullName ?? memberType.Name,
                    memberType.IsValueType,
                    isArray,
                    elementType == null ? string.Empty : elementType.FullName ?? elementType.Name,
                    topic,
                    schemaName,
                    rateHz,
                    publishMode,
                    changeEpsilon,
                    forceIntervalSeconds,
                    rawMemberOrder,
                    "FOXRUN_FIXTURE_EXTRA"));
            }
        }

        private static bool TryGetArrayElementType(Type type, out Type elementType)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }

        private static bool BytesContainText(byte[] bytes, string text)
        {
            return IndexOf(bytes, Encoding.UTF8.GetBytes(text)) >= 0
                   || IndexOf(bytes, Encoding.Unicode.GetBytes(text)) >= 0;
        }

        private static int IndexOf(byte[] source, byte[] pattern)
        {
            if (source == null || pattern == null || pattern.Length == 0 || source.Length < pattern.Length)
                return -1;
            for (var i = 0; i <= source.Length - pattern.Length; i++)
            {
                var matched = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] == pattern[j])
                        continue;
                    matched = false;
                    break;
                }
                if (matched)
                    return i;
            }
            return -1;
        }

        private static JToken SemanticDescriptor(string descriptorJson)
        {
            var json = JToken.Parse(descriptorJson);
            foreach (var property in ((JContainer)json).DescendantsAndSelf().OfType<JProperty>().ToList())
            {
                if (property.Name == "hostKind"
                    || property.Name == "rawTypeName"
                    || property.Name == "rawMemberOrder"
                    || property.Name == "conditionalSymbols")
                    property.Remove();
            }
            return json;
        }

        private static FoxRunGenerationMember Member(
            string ns,
            string className,
            string memberName,
            string topic,
            string rawType,
            string hostKind,
            int rawMemberOrder)
        {
            return new FoxRunGenerationMember(
                ns,
                className,
                memberName,
                "field",
                rawType,
                rawType == "float" || rawType == "System.Single",
                false,
                string.Empty,
                topic,
                10f,
                string.Empty,
                0,
                0f,
                0f,
                hostKind,
                rawMemberOrder,
                string.Empty);
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string NormalizeNewlines(string value)
            => value.Replace("\r\n", "\n").Replace("\r", "\n");

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
