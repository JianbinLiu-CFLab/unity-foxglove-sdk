// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-24 validation for schema evidence and build tooling guards.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_24Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-24: Schema Evidence and Build Tooling ===");
            _passed = 0;

            SchemaGeneratedOutputFreshnessIsGated();
            SourceGeneratorDllFreshnessIsGated();
            UnityIl2CppBuildPreflightsGeneratedArtifacts();
            SchemaEvidencePathsStayProjectRelative();
            BuildPreprocessRefreshesGeneratedAssets();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-24: {_passed} checks passed.");
        }

        private static void SchemaGeneratedOutputFreshnessIsGated()
        {
            var validator = ReadRepoText("Scripts/schema/validate_schema_generated_outputs.py");
            var runCi = ReadRepoText("Scripts/release/run_ci.py");
            var tests = ReadRepoText("Scripts/schema/regression_checks/test_schema_tooling.py");

            Check(validator.Contains("generate_ros2_msg_schema_catalog.py", StringComparison.Ordinal)
                  && validator.Contains("generate_ros2_cdr_serializers.py", StringComparison.Ordinal)
                  && validator.Contains("committed.read_bytes() != fresh.read_bytes()", StringComparison.Ordinal),
                "163-24A-1: schema validator fresh-generates and byte-compares committed outputs");
            Check(runCi.Contains("Scripts/schema/validate_schema_generated_outputs.py", StringComparison.Ordinal)
                  && runCi.Contains("validate-schema-generated", StringComparison.Ordinal),
                "163-24A-2: run_ci packages suite includes schema generated-output freshness");
            Check(tests.Contains("test_generated_output_validator_reports_stale_committed_files", StringComparison.Ordinal)
                  && tests.Contains("stale generated output", StringComparison.Ordinal),
                "163-24A-3: schema tooling regression tests stale-output diagnostics");
        }

        private static void SourceGeneratorDllFreshnessIsGated()
        {
            var validator = ReadRepoText("Scripts/package/validate_source_generator_dll.py");

            Check(validator.Contains("dotnet", StringComparison.Ordinal)
                  && validator.Contains("build", StringComparison.Ordinal)
                  && validator.Contains("CHECKED_IN_ARTIFACTS", StringComparison.Ordinal)
                  && validator.Contains("built_hash = sha256(built_artifacts[name])", StringComparison.Ordinal)
                  && validator.Contains("checked_hash = sha256(checked_in)", StringComparison.Ordinal)
                  && validator.Contains("if built_hash != checked_hash:", StringComparison.Ordinal)
                  && !validator.Contains("BUILT_DLL.read_bytes() != CHECKED_IN_DLL.read_bytes()", StringComparison.Ordinal),
                "163-24B-1: source generator validator rebuilds and hash-compares every checked-in analyzer artifact");
        }

        private static void UnityIl2CppBuildPreflightsGeneratedArtifacts()
        {
            var build = ReadRepoText("Scripts/unity_build/unity_il2cpp.py");
            var tests = ReadRepoText("Scripts/release/regression_checks/test_release_tooling.py");
            var dryRunIndex = build.IndexOf("if args.dry_run:", StringComparison.Ordinal);
            var preflightIndex = build.IndexOf("generated_failures = validate_generated_artifacts(root)", StringComparison.Ordinal);

            Check(build.Contains("REQUIRED_GENERATED_ARTIFACTS", StringComparison.Ordinal)
                  && build.Contains("FoxgloveLogSourceGenerator.dll", StringComparison.Ordinal)
                  && build.Contains("FoxgloveRos2MsgSchemaCatalog.cs", StringComparison.Ordinal)
                  && build.Contains("Ros2CdrGeneratedSerializers.g.cs", StringComparison.Ordinal),
                "163-24C-1: IL2CPP build driver declares required generated artifacts");
            Check(preflightIndex >= 0 && dryRunIndex > preflightIndex,
                "163-24C-2: IL2CPP build driver checks generated artifacts before dry-run or Unity launch");
            Check(tests.Contains("test_generated_artifact_preflight_reports_missing_files", StringComparison.Ordinal),
                "163-24C-3: release tooling tests generated artifact preflight diagnostics");
        }

        private static void SchemaEvidencePathsStayProjectRelative()
        {
            var paths = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");

            Check(paths.Contains("Application.dataPath", StringComparison.Ordinal)
                  && paths.Contains("Schema evidence root must stay inside Assets.", StringComparison.Ordinal)
                  && paths.Contains("IsSameOrChildPath(fullCandidate, assetsRoot)", StringComparison.Ordinal),
                "163-24D-1: schema evidence paths are derived from the project Assets root and bounded");
        }

        private static void BuildPreprocessRefreshesGeneratedAssets()
        {
            var preprocess = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var playMode = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs");

            Check(preprocess.Contains("Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts", StringComparison.Ordinal)
                  && preprocess.Contains("AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)", StringComparison.Ordinal),
                "163-24E-1: build preprocess refreshes schema manifest assets synchronously");
            var playModeCallback = PhaseValidationSourceHelpers.SourceMethod(
                playMode,
                "private static void OnPlayModeStateChanged");
            Check(playMode.Contains("Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts(refresh.Manifest)", StringComparison.Ordinal)
                  && playModeCallback.Contains("QueueSchemaInfoRefreshAfterPlayCancellation", StringComparison.Ordinal)
                  && !playModeCallback.Contains("AssetDatabase.Refresh", StringComparison.Ordinal)
                  && playMode.Contains("EditorApplication.delayCall", StringComparison.Ordinal)
                  && playMode.Contains("AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)", StringComparison.Ordinal),
                "163-24E-2: play-mode manifest refresh defers synchronous import until after Play cancellation");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_24Validation.cs", StringComparison.Ordinal),
                "163-24F-1: runtime test project compiles Phase163_24Validation");
            Check(registry.Contains("--phase163-24", StringComparison.Ordinal)
                  && registry.Contains("Phase163_24Validation.Validate", StringComparison.Ordinal),
                "163-24F-2: validation registry exposes --phase163-24");
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
                if (File.Exists(candidate))
                    return candidate;
                var parent = Directory.GetParent(root);
                if (parent == null)
                    break;
                root = parent.FullName;
            }

            throw new FileNotFoundException("Could not locate repository file: " + relativePath);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException(label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
