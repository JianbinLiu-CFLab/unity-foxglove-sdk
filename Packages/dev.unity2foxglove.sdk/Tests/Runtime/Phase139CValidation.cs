// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139C validation for Remote Data Loader workflow documentation and smoke tooling.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// CI-safe checks for the Phase 139C Remote Data Loader workflow surface.
    /// Unity cursor bridge behavior is intentionally documented as optional
    /// until a Foxglove extension channel is proven by manual evidence.
    /// </summary>
    public static class Phase139CValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 139C validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 139C: DataLoader Integration And Cursor Boundary ===");
            _passed = 0;

            VerifySmokeScriptContract();
            VerifyWorkflowDocumentation();
            VerifyManagerInspectorRemoteFileAccess();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 139C: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void VerifySmokeScriptContract()
        {
            var script = Read("Scripts/smoke/phase139c_dataloader_cursor_acceptance.py");

            Check(script.Contains("--mode", StringComparison.Ordinal)
                  && script.Contains("curve-only", StringComparison.Ordinal),
                "139C-1A: smoke script exposes curve-only mode");
            Check(script.Contains("--mcap", StringComparison.Ordinal)
                  && script.Contains("--base-url", StringComparison.Ordinal)
                  && script.Contains("--json-out", StringComparison.Ordinal),
                "139C-1B: smoke script can launch or probe a Remote Data Loader backend");
            Check(script.Contains("build_remote_file_url", StringComparison.Ordinal)
                  && script.Contains(".mcap", StringComparison.Ordinal)
                  && script.Contains("\"Range\": \"bytes=0-7\"", StringComparison.Ordinal),
                "139C-1B2: smoke script verifies Foxglove Remote files direct MCAP URL compatibility");
            Check(script.Contains("cursor_bridge", StringComparison.Ordinal)
                  && script.Contains("optional", StringComparison.OrdinalIgnoreCase),
                "139C-1C: smoke evidence records cursor bridge as an optional channel");
            Check(script.Contains("phase139b_remote_data_loader_acceptance", StringComparison.Ordinal)
                  || script.Contains("PHASE139B_SERVER_READY", StringComparison.Ordinal),
                "139C-1D: smoke script reuses the Phase139B backend contract instead of inventing another server");
        }

        private static void VerifyWorkflowDocumentation()
        {
            var docs = Read("docs/research-remote-timeline-scene-reproduction.md");

            Check(docs.Contains("Phase139C Remote Data Loader Workflow", StringComparison.Ordinal),
                "139C-2A: research document contains a Phase139C workflow section");
            Check(docs.Contains("Remote Data Loader", StringComparison.Ordinal)
                  && docs.Contains("/v1/manifest", StringComparison.Ordinal)
                  && docs.Contains("/v1/data", StringComparison.Ordinal),
                "139C-2B: documentation names the manifest and data endpoints");
            Check(docs.Contains("Remote files", StringComparison.Ordinal)
                  && docs.Contains("/v1/files/local-mcap.mcap", StringComparison.Ordinal)
                  && docs.Contains("URL must end with a filename and extension", StringComparison.Ordinal),
                "139C-2B2: documentation points Foxglove's stock Remote files dialog at the direct MCAP URL");
            Check(docs.Contains("continuous", StringComparison.OrdinalIgnoreCase)
                  && docs.Contains("Plot", StringComparison.Ordinal),
                "139C-2C: documentation describes continuous curve inspection");
            Check(docs.Contains("cursor bridge", StringComparison.OrdinalIgnoreCase)
                  && docs.Contains("separate optional", StringComparison.OrdinalIgnoreCase),
                "139C-2D: documentation separates DataLoader analysis from Unity cursor sync");
        }

        private static void VerifyManagerInspectorRemoteFileAccess()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");

            Check(manager.Contains("_enableRemoteMcapFileServer", StringComparison.Ordinal)
                  && manager.Contains("_remoteMcapFileServerPort = 8891", StringComparison.Ordinal),
                "139C-3A: manager owns built-in Remote files server settings");
            Check(server.Contains("RemoteMcapHttpServer.Start", StringComparison.Ordinal)
                  && server.Contains("StopRemoteMcapFileServer", StringComparison.Ordinal)
                  && server.Contains("BuildRemoteMcapFileUrl", StringComparison.Ordinal),
                "139C-3B: manager lifecycle starts and stops the Remote files server");
            Check(editor.Contains("Remote File Access", StringComparison.Ordinal)
                  && editor.Contains("Copy Remote URL", StringComparison.Ordinal)
                  && editor.Contains("Open in Foxglove", StringComparison.Ordinal)
                  && editor.Contains("/v1/files/", StringComparison.Ordinal),
                "139C-3C: manager Inspector exposes copy/open controls for the direct MCAP URL");
            Check(editor.Contains("Foxglove.exe", StringComparison.Ordinal)
                  || editor.Contains("foxglove", StringComparison.Ordinal),
                "139C-3D: manager Inspector can open Foxglove without requiring a separate Tools workflow");
        }

        private static void VerifyValidationWiring()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase139c\"", StringComparison.Ordinal),
                "139C-4A: registry wires --phase139c");
            Check(registry.Contains("Phase139CValidation.Validate", StringComparison.Ordinal),
                "139C-4B: registry points Phase139C at the validation entrypoint");
            Check(project.Contains("Phase139CValidation.cs", StringComparison.Ordinal),
                "139C-4C: test project compiles Phase139CValidation");
        }

        private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase139C validation.");
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
