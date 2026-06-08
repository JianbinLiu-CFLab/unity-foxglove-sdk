// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-21 regression coverage for FoxRun generation host parity.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_21Validation.
    /// </summary>
    public static class Phase140_21Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-21: FoxRun Generation Hosts and Source Generators ===");
            _passed = 0;

            ReflectionPathRecognizesIListArrayLikeMembers();
            SharedValidatorReportsHostIndependentConflictDiagnostics();
            SharedValidatorRejectsNonFinitePolicyValues();
            SourceGeneratorEscapesDescriptorCarrierControlCharacters();
            SourceGeneratorDescriptorExcludesNonPartialTypes();
            BuildTimeGeneratedSourceWritesAreAtomic();
            ManifestRefreshApiDocumentsAllArtifacts();
            PlayModeHookDocumentsArtifactSetRisk();
            BuildTimeSkipsReflectionLoadFailuresWithWarning();
            RedundantOutputDirConstantIsRemoved();
            LocationLookupUsesExactMemberKeys();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-21: {_passed} checks passed.");
        }

        private static void ReflectionPathRecognizesIListArrayLikeMembers()
        {
            var codegen = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            Check(codegen.Contains("typeof(IList<>)", StringComparison.Ordinal),
                "140-21A-1: reflection fallback recognizes IList<T> as array-like");
        }

        private static void SharedValidatorReportsHostIndependentConflictDiagnostics()
        {
            var model = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "Conflicts", new[]
                {
                    Member("_value", "/demo/conflict", "schema.A", publishMode: 0),
                    Member("__value", "/demo/conflict", "schema.B", publishMode: 1)
                })
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);
            Check(diagnostics.Any(d => d.Id == "FOXRUN002"),
                "140-21B-1: shared validator reports schema conflicts for build-time path");
            Check(diagnostics.Any(d => d.Id == "FOXRUN003"),
                "140-21B-2: shared validator reports underscore-stripped name collisions for build-time path");
            Check(diagnostics.Any(d => d.Id == "FOXRUN005"),
                "140-21B-3: shared validator reports mixed same-topic policy for build-time path");
        }

        private static void SharedValidatorRejectsNonFinitePolicyValues()
        {
            var model = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "Policy", new[]
                {
                    Member("_nanRate", "/demo/nan", rateHz: float.NaN),
                    Member("_infEpsilon", "/demo/inf_eps", changeEpsilon: float.PositiveInfinity),
                    Member("_infInterval", "/demo/inf_interval", forceIntervalSeconds: float.NegativeInfinity)
                })
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);
            Check(diagnostics.Any(d => d.Id == "FOXRUN009" && d.MemberName == "_nanRate"),
                "140-21C-1: shared validator rejects NaN RateHz");
            Check(diagnostics.Any(d => d.Id == "FOXRUN009" && d.MemberName == "_infEpsilon"),
                "140-21C-2: shared validator rejects infinite ChangeEpsilon");
            Check(diagnostics.Any(d => d.Id == "FOXRUN009" && d.MemberName == "_infInterval"),
                "140-21C-3: shared validator rejects infinite ForceIntervalSeconds");
        }

        private static void SourceGeneratorEscapesDescriptorCarrierControlCharacters()
        {
            var generator = Read("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(generator.Contains("case '\\t':", StringComparison.Ordinal)
                  && generator.Contains("case '\\b':", StringComparison.Ordinal)
                  && generator.Contains("ToString(\"x4\", CultureInfo.InvariantCulture)", StringComparison.Ordinal),
                "140-21D-1: descriptor carrier string escaping handles all C# control characters");
        }

        private static void SourceGeneratorDescriptorExcludesNonPartialTypes()
        {
            var generator = Read("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(generator.Contains("emittedTypes", StringComparison.Ordinal)
                  && generator.Contains("FoxRunGenerationModel(emittedTypes", StringComparison.Ordinal),
                "140-21E-1: descriptor carrier is written from emitted partial types only");
        }

        private static void BuildTimeGeneratedSourceWritesAreAtomic()
        {
            var codegen = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var build = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            Check(codegen.Contains("WriteSourceFileIfChanged", StringComparison.Ordinal)
                  && !codegen.Contains("File.WriteAllBytes(absolutePath, sourceBytes)", StringComparison.Ordinal),
                "140-21F-1: build-time .g.cs source writes use temp-and-replace");
            Check(build.Contains("WriteTextIfChanged", StringComparison.Ordinal)
                  && !build.Contains("File.WriteAllText(linkPath, linkXml)", StringComparison.Ordinal),
                "140-21F-2: build-time link.xml writes use temp-and-replace");
        }

        private static void ManifestRefreshApiDocumentsAllArtifacts()
        {
            var codegen = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var methodComment = Slice(codegen, "/// Refresh canonical FoxRun manifest artifacts", "public static FoxRunCanonicalManifest GenerateManifestFilesOnly()");
            Check(methodComment.Contains("schema info", StringComparison.Ordinal)
                  && methodComment.Contains("generation descriptor", StringComparison.Ordinal),
                "140-21G-1: GenerateManifestFilesOnly documents all artifact side effects");
        }

        private static void PlayModeHookDocumentsArtifactSetRisk()
        {
            var hook = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs");
            Check(hook.Contains("artifact set", StringComparison.Ordinal)
                  && hook.Contains("next Play attempt", StringComparison.Ordinal),
                "140-21H-1: Play Mode hook documents accepted multi-file artifact risk");
        }

        private static void BuildTimeSkipsReflectionLoadFailuresWithWarning()
        {
            var codegen = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            Check(codegen.Contains("ReflectionTypeLoadException ex", StringComparison.Ordinal)
                  && codegen.Contains("Debug.LogWarning", StringComparison.Ordinal)
                  && codegen.Contains("LoaderExceptions", StringComparison.Ordinal),
                "140-21I-1: ignored reflection load failures produce a visible warning");
        }

        private static void RedundantOutputDirConstantIsRemoved()
        {
            var codegen = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            Check(!codegen.Contains("const string OutputDir", StringComparison.Ordinal),
                "140-21J-1: dead OutputDir constant is removed");
        }

        private static void LocationLookupUsesExactMemberKeys()
        {
            var generator = Read("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(generator.Contains("diagnostic.MemberName", StringComparison.Ordinal)
                  && generator.Contains("TryGetValue", StringComparison.Ordinal)
                  && !generator.Contains("foreach (var pair in memberLocations)", StringComparison.Ordinal),
                "140-21K-1: shared diagnostic location lookup uses exact member keys");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_21Validation.cs", StringComparison.Ordinal),
                "140-21L-1: test project compiles Phase140_21Validation");
            Check(registry.Contains("Ci(\"--phase140-21\", \"Phase 140-21\", Phase140_21Validation.Validate", StringComparison.Ordinal),
                "140-21L-2: validation registry exposes --phase140-21");
        }

        private static FoxRunGenerationMember Member(
            string name,
            string topic,
            string schemaName = "",
            float rateHz = 10f,
            int publishMode = 0,
            float changeEpsilon = 0f,
            float forceIntervalSeconds = 0f)
        {
            return new FoxRunGenerationMember(
                "Demo",
                "Probe",
                name,
                "field",
                "System.Single",
                "float",
                true,
                false,
                string.Empty,
                topic,
                rateHz,
                schemaName,
                publishMode,
                changeEpsilon,
                forceIntervalSeconds,
                "Test",
                0,
                string.Empty);
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] Missing start token: " + startToken);

            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;

            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
