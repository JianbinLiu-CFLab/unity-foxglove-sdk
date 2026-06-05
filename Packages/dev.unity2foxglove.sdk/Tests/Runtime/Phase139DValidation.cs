// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139D validation for the Unity cursor bridge feasibility surface.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// CI-safe checks for the Phase 139D cursor-bridge scaffold. These checks
    /// validate the extension signal contract and deliberately avoid any claim
    /// that Remote Data Loader range requests are Unity playhead controls.
    /// </summary>
    public static class Phase139DValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 139D validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 139D: Unity Cursor Bridge Feasibility Scaffold ===");
            _passed = 0;

            VerifyExtensionScaffold();
            VerifySmokeScript();
            VerifyWorkflowDocumentation();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 139D: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void VerifyExtensionScaffold()
        {
            var packageJson = Read("Tools/foxglove-extensions/unity-cursor-bridge/package.json");
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var readme = Read("Tools/foxglove-extensions/unity-cursor-bridge/README.md");

            Check(packageJson.Contains("\"name\": \"unity-cursor-bridge\"", StringComparison.Ordinal)
                  && packageJson.Contains("foxglove-extension build", StringComparison.Ordinal),
                "139D-1A: extension package declares the Unity cursor bridge panel");
            Check(source.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && source.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-1B: extension watches and reads Foxglove currentTime");
            Check(source.Contains("context.watch(\"startTime\")", StringComparison.Ordinal)
                  && source.Contains("context.watch(\"endTime\")", StringComparison.Ordinal)
                  && source.Contains("context.watch(\"didSeek\")", StringComparison.Ordinal),
                "139D-1C: extension watches timeline bounds and seek state");
            Check(source.Contains("sec: currentTime.sec", StringComparison.Ordinal)
                  && source.Contains("nsec: currentTime.nsec", StringComparison.Ordinal)
                  && source.Contains("fetch(endpoint", StringComparison.Ordinal),
                "139D-1D: extension sends split sec/nsec cursor metadata to loopback");
            Check(!source.Contains("/v1/data", StringComparison.Ordinal),
                "139D-1E: extension does not infer cursor state from Remote Data Loader ranges");
            Check(readme.Contains("disabled by default", StringComparison.OrdinalIgnoreCase)
                  && readme.Contains("/v1/data", StringComparison.Ordinal)
                  && readme.Contains("playhead signal", StringComparison.OrdinalIgnoreCase),
                "139D-1F: extension README documents the disabled default and DataLoader boundary");
        }

        private static void VerifySmokeScript()
        {
            var script = Read("Scripts/smoke/phase139d_unity_cursor_bridge_acceptance.py");

            Check(script.Contains("extension-metadata", StringComparison.Ordinal)
                  && script.Contains("endpoint-loopback", StringComparison.Ordinal),
                "139D-2A: smoke helper separates metadata and endpoint-loopback modes");
            Check(script.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && script.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-2B: smoke helper validates the extension currentTime contract");
            Check(script.Contains("build_cursor_payload", StringComparison.Ordinal)
                  && script.Contains("\"sec\"", StringComparison.Ordinal)
                  && script.Contains("\"nsec\"", StringComparison.Ordinal),
                "139D-2C: smoke helper sends explicit split-time cursor payloads");
            Check(script.Contains("not playhead-control evidence", StringComparison.Ordinal)
                  && script.Contains("/v1/data", StringComparison.Ordinal),
                "139D-2D: smoke helper documents that /v1/data is not a cursor source");
        }

        private static void VerifyWorkflowDocumentation()
        {
            var docs = Read("docs/research-remote-timeline-scene-reproduction.md");

            Check(docs.Contains("Phase139D Unity Cursor Bridge Boundary", StringComparison.Ordinal),
                "139D-3A: research document contains a Phase139D cursor bridge section");
            Check(docs.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && docs.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-3B: documentation records the Foxglove extension currentTime contract");
            Check(docs.Contains("Do not infer Unity cursor state from `/v1/data`", StringComparison.Ordinal),
                "139D-3C: documentation forbids using Remote Data Loader data ranges as cursor signals");
            Check(docs.Contains("disabled by default", StringComparison.OrdinalIgnoreCase)
                  && docs.Contains("loopback", StringComparison.OrdinalIgnoreCase),
                "139D-3D: documentation keeps the bridge optional and loopback-bounded");
        }

        private static void VerifyValidationWiring()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase139d\"", StringComparison.Ordinal),
                "139D-4A: registry wires --phase139d");
            Check(registry.Contains("Phase139DValidation.Validate", StringComparison.Ordinal),
                "139D-4B: registry points Phase139D at the validation entrypoint");
            Check(project.Contains("Phase139DValidation.cs", StringComparison.Ordinal),
                "139D-4C: test project compiles Phase139DValidation");
        }

        private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase139D validation.");
            return root;
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
