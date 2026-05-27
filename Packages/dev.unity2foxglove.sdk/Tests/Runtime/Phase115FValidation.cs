// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Hardens FoxRun generation-model equivalence after Phase 115E.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115FValidation
    {
        private const string FixtureRelativePath = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture.cs";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115F: FoxRun Generation Model Hardening ===");
            _passed = 0;

            VerifyTypeIdentityContract();
            VerifyReaderAndRoundTripCoverage();
            VerifyDescriptorReaderRoundTripBehavior();
            VerifySupportedListDiagnostics();
            VerifyFixtureCoversHardEmissionTypes();
            VerifyReaderMediatedCrossHostEquivalence();
            VerifySingleLoweringAndEmitterBoundary();
            VerifyAnalyzerDllFreshnessGuard();

            Console.WriteLine($"Phase 115F: {_passed} checks passed.");
        }

        private static void VerifyTypeIdentityContract()
        {
            var model = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            Check(model.Contains("EmissionTypeName", StringComparison.Ordinal),
                "115F-A1: generation model has explicit EmissionTypeName");
            Check(model.Contains("RawObservedTypeName", StringComparison.Ordinal),
                "115F-A2: generation model has explicit RawObservedTypeName");
            Check(model.Contains("EmissionTypeName,", StringComparison.Ordinal)
                  && !model.Contains("RawTypeName,\r\n                Topic", StringComparison.Ordinal)
                  && !model.Contains("RawTypeName,\n                Topic", StringComparison.Ordinal),
                "115F-A3: ToTopicMember uses emission type, not raw observed type");

            var comparer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorComparer.cs");
            Check(comparer.Contains("emissionTypeName", StringComparison.Ordinal)
                  || comparer.Contains("EmissionTypeName", StringComparison.Ordinal),
                "115F-A4: descriptor comparer treats emission type as semantic");
        }

        private static void VerifyReaderAndRoundTripCoverage()
        {
            Check(File.Exists(RepoPath("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunGenerationDescriptorJsonReader.cs")),
                "115F-B1: descriptor JSON reader is test-owned");
            var csproj = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(csproj.Contains("FoxRunGenerationDescriptorJsonReader.cs", StringComparison.Ordinal),
                "115F-B2: runtime test project explicitly includes descriptor JSON reader");
            var json = FoxRunGenerationDescriptorJsonWriter.Write(
                new FoxRunGenerationModel(Array.Empty<FoxRunGenerationType>()));
            var parsed = FoxRunGenerationDescriptorJsonReader.Read(json);
            Check(parsed.Types.Count == 0 && FoxRunGenerationDescriptorJsonWriter.Write(parsed) == json,
                "115F-B3: descriptor writer-reader round-trip works without self-referential source text");
        }

        private static void VerifyDescriptorReaderRoundTripBehavior()
        {
            var original = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "HardTypes", new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "HardTypes",
                        "_list",
                        "field",
                        "System.Collections.Generic.List`1[[System.Single, mscorlib]]",
                        "System.Collections.Generic.List<float>",
                        "float32",
                        false,
                        true,
                        "System.Single",
                        "/debug/list",
                        10f,
                        string.Empty,
                        0,
                        0f,
                        0f,
                        "Roslyn",
                        7,
                        "FOXRUN_FIXTURE_EXTRA")
                })
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(original);
            var parsed = FoxRunGenerationDescriptorJsonReader.Read(json);
            var comparison = FoxRunGenerationDescriptorComparer.Compare(original, parsed);

            Check(comparison.IsSemanticEqual && comparison.ProvenanceDifferences.Count == 0,
                "115F-B4: writer -> JSON -> reader -> model round-trip preserves compared fields");
            Check(FoxRunGenerationDescriptorJsonWriter.Write(parsed) == json,
                "115F-B5: descriptor reader round-trip preserves deterministic JSON bytes");
        }

        private static void VerifySupportedListDiagnostics()
        {
            var model = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "ListDiagnostics", new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "ListDiagnostics",
                        "_list",
                        "field",
                        "System.Collections.Generic.List`1[[System.Single, mscorlib]]",
                        "System.Collections.Generic.List<float>",
                        false,
                        true,
                        "System.Single",
                        "/debug/list",
                        10f,
                        string.Empty,
                        0,
                        0f,
                        0f,
                        "Reflection",
                        1,
                        string.Empty),
                    new FoxRunGenerationMember(
                        "Demo",
                        "ListDiagnostics",
                        "_dict",
                        "field",
                        "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[System.Single, mscorlib]]",
                        "System.Collections.Generic.Dictionary<string, float>",
                        false,
                        false,
                        string.Empty,
                        "/debug/dict",
                        10f,
                        string.Empty,
                        0,
                        0f,
                        0f,
                        "Reflection",
                        2,
                        string.Empty)
                })
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);
            Check(!diagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN007" && diagnostic.MemberName == "_list"),
                "115F-B6: supported List<T> array-like members do not emit generic safety warnings");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN007" && diagnostic.MemberName == "_dict"),
                "115F-B7: unsupported generic members still emit generic safety warnings");
        }

        private static void VerifyFixtureCoversHardEmissionTypes()
        {
            var fixture = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture.cs");
            Check(fixture.Contains("float[]", StringComparison.Ordinal),
                "115F-C1: fixture covers array emission type");
            Check(fixture.Contains("List<float>", StringComparison.Ordinal)
                  || fixture.Contains("System.Collections.Generic.List<float>", StringComparison.Ordinal),
                "115F-C2: fixture covers generic list emission type");
            Check(fixture.Contains("int?", StringComparison.Ordinal)
                  || fixture.Contains("System.Nullable<int>", StringComparison.Ordinal),
                "115F-C3: fixture covers nullable value emission type");
            Check(fixture.Contains("Nested", StringComparison.Ordinal),
                "115F-C4: fixture covers nested type emission type");
            Check(fixture.Contains("UnityEngine.Vector3", StringComparison.Ordinal),
                "115F-C5: fixture covers Unity vector-style emission type");
        }

        private static void VerifyReaderMediatedCrossHostEquivalence()
        {
            var roslynJson = ExtractDescriptorJsonFromRoslyn();
            var roslynModel = FoxRunGenerationDescriptorJsonReader.Read(roslynJson);
            var reflectionModel = BuildReflectionModelFromFixtureAssembly();
            var comparison = FoxRunGenerationDescriptorComparer.Compare(roslynModel, reflectionModel);

            Check(comparison.IsSemanticEqual,
                "115F-C6: Roslyn descriptor JSON parses to a model semantically equal to reflection model",
                string.Join("; ", comparison.SemanticDifferences));
            Check(ContainsTopicWithEmission(roslynModel, "/debug/list", "System.Collections.Generic.List<float>")
                  && ContainsTopicWithEmission(reflectionModel, "/debug/list", "System.Collections.Generic.List<float>"),
                "115F-C7: generic list emission type matches across Roslyn and reflection hosts");
            Check(ContainsTopicWithEmission(roslynModel, "/debug/array", "float[]")
                  && ContainsTopicWithEmission(reflectionModel, "/debug/array", "float[]"),
                "115F-C8: array emission type matches across Roslyn and reflection hosts");
            Check(ContainsTopicWithEmission(roslynModel, "/debug/nullable", "int?")
                  && ContainsTopicWithEmission(reflectionModel, "/debug/nullable", "int?"),
                "115F-C9: nullable emission type matches across Roslyn and reflection hosts");
            Check(ContainsTopicWithEmission(roslynModel, "/debug/vector", "UnityEngine.Vector3")
                  && ContainsTopicWithEmission(reflectionModel, "/debug/vector", "UnityEngine.Vector3"),
                "115F-C10: Unity vector emission type matches across Roslyn and reflection hosts");

            var reflectionType = reflectionModel.Types.Single(type => type.ClassName == "FoxRunGenerationModelFixture");
            var reflectionCoreSource = FoxgloveSourceEmitter.EmitClass(reflectionType);
            var roslynCoreSource = ExtractFoxRunSourceFromRoslyn();
            Check(NormalizeNewlines(reflectionCoreSource).TrimEnd() == NormalizeNewlines(roslynCoreSource).TrimEnd(),
                "115F-C11: build-time model emission matches Roslyn model emission byte-for-byte");
        }

        private static void VerifySingleLoweringAndEmitterBoundary()
        {
            var generator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(Count(generator, "FoxRunRoslynGenerationModelLowerer.Lower") == 1,
                "115F-D1: Roslyn source generator lowers once for descriptor and source emission");
            Check(!generator.Contains("EmitClass(spc, grp.ToArray())", StringComparison.Ordinal),
                "115F-D2: Roslyn source generator emits from the shared lowered model");

            var emitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter.cs");
            Check(!emitter.Contains("public static string EmitClass(string ns, string className, IReadOnlyList<TopicMember> members)", StringComparison.Ordinal),
                "115F-D3: legacy TopicMember emitter overload is not public production API");
            Check(!emitter.Contains("RawObservedTypeName", StringComparison.Ordinal),
                "115F-D4: emitter does not consume raw observed type names");

            var buildTime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            Check(Count(buildTime, "FoxRunReflectionGenerationModelLowerer.Lower") <= 1,
                "115F-D5: build-time generator has a single reflection lowering site");

            var roslynSource = ExtractFoxRunSourceFromRoslyn();
            Check(!roslynSource.Contains("new {", StringComparison.Ordinal)
                  && roslynSource.Contains("new Dictionary<string, object>", StringComparison.Ordinal),
                "115F-D6: generated JSON payloads avoid anonymous types for IL2CPP-safe serialization");
        }

        private static void VerifyAnalyzerDllFreshnessGuard()
        {
            var dllPath = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll");
            Check(File.Exists(dllPath),
                "115F-E1: checked-in analyzer DLL exists");
            if (File.Exists(dllPath))
            {
                var checkedIn = File.ReadAllBytes(dllPath);
                var sourceGenerated = RunRoslynGenerator();
                var checkedInGenerated = RunGenerator(LoadGeneratorFromDll(dllPath), "FoxRunGenerationModelFixtureCheckedInDll115F");
                Check(GeneratedSourceText(sourceGenerated, "FoxRunGeneratedDescriptorInfo.g.cs")
                      == GeneratedSourceText(checkedInGenerated, "FoxRunGeneratedDescriptorInfo.g.cs")
                      && GeneratedFoxRunSource(sourceGenerated) == GeneratedFoxRunSource(checkedInGenerated),
                    "115F-E2: checked-in analyzer DLL matches source generator semantics");

                Check(BytesContainText(checkedIn, "EmissionTypeName") && BytesContainText(checkedIn, "RawObservedTypeName"),
                    "115F-E3: checked-in analyzer DLL contains 115F type-boundary artifacts");
            }
        }

        private static int Count(string value, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += pattern.Length;
            }
            return count;
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

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static bool ContainsTopicWithEmission(FoxRunGenerationModel model, string topic, string emissionTypeName)
        {
            return model.Types
                .SelectMany(type => type.Members)
                .Any(member => string.Equals(member.Topic, topic, StringComparison.Ordinal)
                               && string.Equals(member.EmissionTypeName, emissionTypeName, StringComparison.Ordinal));
        }

        private static string ExtractDescriptorJsonFromRoslyn()
        {
            var descriptorSource = GeneratedSourceText(RunRoslynGenerator(), "FoxRunGeneratedDescriptorInfo.g.cs");
            var match = Regex.Match(descriptorSource, "DescriptorJson = \"(?<json>.*)\";");
            if (!match.Success)
                throw new InvalidOperationException("Could not extract DescriptorJson from Roslyn descriptor carrier.");
            return Regex.Unescape(match.Groups["json"].Value);
        }

        private static string ExtractFoxRunSourceFromRoslyn()
            => GeneratedFoxRunSource(RunRoslynGenerator());

        private static IReadOnlyList<GeneratedSourceResult> RunRoslynGenerator()
            => RunGenerator(new FoxgloveLogSourceGenerator(), "FoxRunGenerationModelFixtureRoslyn115F");

        private static IReadOnlyList<GeneratedSourceResult> RunGenerator(IIncrementalGenerator generator, string assemblyName)
        {
            var compilation = CreateCompilation(ReadRepoText(FixtureRelativePath), assemblyName);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { generator.AsSourceGenerator() },
                parseOptions: FixtureParseOptions());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            return driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).ToList();
        }

        private static IIncrementalGenerator LoadGeneratorFromDll(string dllPath)
        {
            var assembly = Assembly.Load(File.ReadAllBytes(Path.GetFullPath(dllPath)));
            var type = assembly.GetType("Unity.FoxgloveSDK.SourceGenerators.FoxgloveLogSourceGenerator")
                       ?? throw new InvalidOperationException("Checked-in analyzer DLL does not contain FoxgloveLogSourceGenerator.");
            return (IIncrementalGenerator)Activator.CreateInstance(type);
        }

        private static string GeneratedSourceText(IReadOnlyList<GeneratedSourceResult> sources, string hintName)
            => sources.First(source => source.HintName == hintName).SourceText.ToString();

        private static string GeneratedFoxRunSource(IReadOnlyList<GeneratedSourceResult> sources)
            => sources.First(source => source.HintName.EndsWith("_FoxRun.g.cs", StringComparison.Ordinal)).SourceText.ToString();

        private static FoxRunGenerationModel BuildReflectionModelFromFixtureAssembly()
        {
            var compilation = CreateCompilation(ReadRepoText(FixtureRelativePath), "FoxRunGenerationModelFixtureReflection115F");
            var outputPath = Path.Combine(Path.GetTempPath(), "foxrun_generation_model_115f_" + Guid.NewGuid().ToString("N") + ".dll");
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

                var assembly = Assembly.Load(File.ReadAllBytes(outputPath));
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
                    FoxRunEmissionTypeNameFormatter.FromReflectionType(memberType),
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
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase115F Roslyn reference resolution.");

            var trusted = trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>();
            var local = new[]
            {
                MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location)
            };
            return trusted.Concat(local).ToArray();
        }

        private static string NormalizeNewlines(string value)
            => value.Replace("\r\n", "\n").Replace("\r", "\n");

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
            => Check(condition, name, string.Empty);

        private static void Check(bool condition, string name, string details)
        {
            if (!condition)
                throw new InvalidOperationException(string.IsNullOrEmpty(details) ? name : name + " :: " + details);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
