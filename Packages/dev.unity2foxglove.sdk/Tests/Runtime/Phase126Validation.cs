// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 126 architecture coupling, local-boundary, and validation-registry gate.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase126Validation.
    /// </summary>
    public static class Phase126Validation
    {
        private const string ArchitectureScriptPath = "Scripts/architecture/analyze_coupling.py";
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 126: Architecture Coupling Fan-in/Fan-out Refactor Gate ===");
            _passed = 0;

            VerifyArchitectureScript();
            VerifyValidationRegistry();
            VerifyExperimentalDemoAssets();
            VerifyFullDemoSampleSync();
            VerifyArchitectureDocumentation();

            Console.WriteLine($"Phase 126: {_passed} checks passed.");
        }

        private static void VerifyArchitectureScript()
        {
            var script = ReadRepoText(ArchitectureScriptPath);
            Check(script.Contains("# Purpose:", StringComparison.Ordinal)
                  && script.Contains("# Usage:", StringComparison.Ordinal)
                  && script.Contains("# Inputs:", StringComparison.Ordinal)
                  && script.Contains("# Outputs:", StringComparison.Ordinal),
                "126A-1: architecture script has repository script headers");
            Check(script.Contains("--format", StringComparison.Ordinal)
                  && script.Contains("--output", StringComparison.Ordinal)
                  && script.Contains("fan-in", StringComparison.Ordinal)
                  && script.Contains("fan-out", StringComparison.Ordinal),
                "126A-2: architecture script exposes report format/output and coupling metrics");
            Check(script.Contains("largest_csharp_files", StringComparison.Ordinal)
                  && script.Contains("asmdef_cycles", StringComparison.Ordinal),
                "126A-3: architecture script reports public architecture hotspots and asmdef cycles");
        }

        private static void VerifyValidationRegistry()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registrySource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var phase126 = PhaseValidationRegistry.Find(new[] { "--phase126" });
            var phase138b = PhaseValidationRegistry.Find(new[] { "--phase138b" });
            var representedEvidence = PhaseValidationRegistry.All.Aggregate(
                ValidationEvidence.None,
                (combined, item) => combined | item.Evidence);

            Check(program.Contains("PhaseValidationRegistry.FindAll", StringComparison.Ordinal)
                  && program.Contains("RunTests(argSet.Contains(\"--local-evidence\"))", StringComparison.Ordinal),
                "126B-1: Program.cs dispatches through the phase validation registry");
            Check(program.Contains("--all-ci-safe", StringComparison.Ordinal)
                  && program.Contains("RunAllCiSafeValidations", StringComparison.Ordinal)
                  && registrySource.Contains("CiSafeValidations", StringComparison.Ordinal),
                "126B-1b: the runner exposes an explicit all-CI-safe registry gate");
            Check(phase126 != null
                  && phase126.Category == ValidationCategory.CiSafe
                  && phase126.Evidence == ValidationEvidence.Structural
                  && phase138b != null
                  && phase138b.Category == ValidationCategory.LocalEvidence
                  && PhaseValidationRegistry.All.All(item => item.Evidence != ValidationEvidence.None)
                  && !PhaseValidationRegistry.DefaultValidations(includeLocalEvidence: false).Contains(phase138b)
                  && PhaseValidationRegistry.DefaultValidations(includeLocalEvidence: true).Contains(phase138b),
                "126B-2: validation registry classifies CI-safe and local-evidence phases");
            Check(project.Contains("Phase126Validation.cs", StringComparison.Ordinal)
                  && project.Contains("PhaseValidationRegistry.cs", StringComparison.Ordinal)
                  && project.Contains("ValidationEvidence.cs", StringComparison.Ordinal)
                  && project.Contains("Phase138BValidation.cs", StringComparison.Ordinal),
                "126B-3: test project compiles Phase126 registry files and shifted Phase138B validation");
            Check(representedEvidence == ValidationEvidenceFormatter.All,
                "126B-4: validation registry exposes every supported evidence classification");
        }

        private static void VerifyExperimentalDemoAssets()
        {
            Check(RepoFileExists("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs")
                  && RepoFileExists("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs"),
                "126D-1: OpenH264 demo probe lives under Experimental");

            var phase80 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase80Validation.cs");
            Check(phase80.Contains("Unity2Foxglove/Assets/Experimental/OpenH264", StringComparison.Ordinal),
                "126D-2: OpenH264 validation follows the Experimental path");
        }

        private static void VerifyFullDemoSampleSync()
        {
            var sync = ReadRepoText("Scripts/samples/sync_full_demo.py");
            Check(sync.Contains("FULL_DEMO_VISUALIZATION_SCRIPTS", StringComparison.Ordinal)
                  && sync.Contains("FOXRUN_SCRIPTS", StringComparison.Ordinal),
                "126E-1: sample sync names the live FullDemoVisualization and FoxRun script roots");
            Check(sync.Contains("\"validate\"", StringComparison.Ordinal)
                  && sync.Contains("validate_file_maps", StringComparison.Ordinal),
                "126E-2: sample sync has an explicit validation dry-run mode");
        }

        private static void VerifyArchitectureDocumentation()
        {
            var docs = ReadRepoText("docs/architecture-patterns.md");
            Check(docs.Contains("Phase 126", StringComparison.Ordinal)
                  && docs.Contains("Experimental", StringComparison.Ordinal),
                "126F-1: architecture docs describe Phase126 boundary and Experimental convention");
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase126 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
        }

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException(description);

            _passed++;
            Console.WriteLine("[PASS] " + description);
        }
    }
}
