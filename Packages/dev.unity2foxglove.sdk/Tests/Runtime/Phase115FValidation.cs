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
    /// <summary>
    /// Validation type for Phase115FValidation.
    /// </summary>
    public static partial class Phase115FValidation
    {
        private const string FixtureRelativePath = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture.cs";

        private static int _passed;
        private static readonly Lazy<MetadataReference[]> CachedReferences = new Lazy<MetadataReference[]>(CreateReferences);

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115F: FoxRun Generation Model Hardening ===");
            _passed = 0;

            VerifyTypeIdentityContract();
            VerifyReaderAndRoundTripCoverage();
            VerifyDescriptorReaderRoundTripBehavior();
            VerifyLegacyV1ReaderRejection();
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
                        (int)FoxRunPolicy.FixedRate,
                         0f,
                         "Roslyn",
                         7,
                         "FOXRUN_FIXTURE_EXTRA",
                         typeShape: FoxRunTypeShape.Collection(
                             FoxRunCollectionKind.List,
                             FoxRunTypeShape.Canonical("float32")))
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

        private static void VerifyLegacyV1ReaderRejection()
        {
            const string json = "{\"descriptorVersion\":1,\"generatorVersion\":\"1.0.0\",\"types\":[{\"namespace\":\"Demo\",\"className\":\"Legacy\",\"members\":[{\"memberName\":\"_value\",\"memberKind\":\"field\",\"rawTypeName\":\"System.Single\",\"emissionTypeName\":\"float\",\"canonicalType\":\"float32\",\"topic\":\"/legacy\",\"encoding\":\"json\"}]}]}";
            try
            {
                FoxRunGenerationDescriptorJsonReader.Read(json);
                Check(false, "115F-B6: incompatible legacy descriptors fail closed instead of inventing current directional defaults");
            }
            catch (InvalidOperationException exception)
            {
                Check(
                     exception.Message.Contains(
                         "Unsupported FoxRun generation descriptor version pair: 1/1.0.0.",
                         StringComparison.Ordinal),
                    "115F-B6: incompatible legacy descriptors fail closed instead of inventing current directional defaults");
            }
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
                        (int)FoxRunPolicy.FixedRate,
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
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        "Reflection",
                        2,
                        string.Empty),
                    new FoxRunGenerationMember(
                        "Demo",
                        "ListDiagnostics",
                        "_nullable",
                        "field",
                        "System.Nullable`1[[System.Int32, mscorlib]]",
                        "int?",
                        true,
                        false,
                        "System.Int32",
                        "/debug/nullable",
                        10f,
                        string.Empty,
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        "Reflection",
                        3,
                        string.Empty)
                })
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);
            Check(!diagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN007" && diagnostic.MemberName == "_list"),
                "115F-B6: supported List<T> array-like members do not emit generic safety warnings");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN007" && diagnostic.MemberName == "_dict"),
                "115F-B7: unsupported generic members still emit generic safety warnings");
            Check(!diagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN007" && diagnostic.MemberName == "_nullable"),
                "115F-B8: supported Nullable<T> value members do not emit generic safety warnings");
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
            var generator = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();
            var compactGenerator = Regex.Replace(
                generator,
                @"\s+",
                string.Empty);
            Check(Count(compactGenerator, "FoxRunRoslynGenerationModelLowerer.Lower(") == 1,
                "115F-D1: Roslyn source generator lowers once for descriptor and source emission");
            Check(!generator.Contains("EmitClass(spc, grp.ToArray())", StringComparison.Ordinal),
                "115F-D2: Roslyn source generator emits from the shared lowered model");

            var emitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs");
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

        private static MetadataReference[] References() => CachedReferences.Value;
}
}
