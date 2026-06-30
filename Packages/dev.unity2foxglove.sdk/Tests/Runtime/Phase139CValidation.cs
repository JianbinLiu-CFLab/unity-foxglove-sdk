// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139C validation for Remote Data Loader workflow documentation and smoke tooling.

using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// CI-safe checks for the Phase 139C Remote Data Loader workflow surface.
    /// Remote file access is intentionally validated as file serving only; it
    /// must not imply that Unity and Foxglove playback cursors are synchronized.
    /// </summary>
    public static class Phase139CValidation
    {
        private static readonly string CachedRepoRoot = ResolveRepoRoot();
        private static readonly Dictionary<string, string> SourceCache = new Dictionary<string, string>();

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
            var script = Read("Scripts/smoke/replay/phase139c_dataloader_cursor_acceptance.py");

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
            Check(docs.Contains("Foxglove Timeline Replay", StringComparison.Ordinal)
                  && docs.Contains("owner of replay time", StringComparison.OrdinalIgnoreCase)
                  && docs.Contains("Unity remains a scene reproduction", StringComparison.Ordinal)
                  && docs.Contains("follower", StringComparison.Ordinal),
                "139C-2D: documentation names Foxglove as the timeline owner for the Remote File workflow");
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
            Check(editor.Contains("Foxglove Timeline Replay", StringComparison.Ordinal)
                  && editor.Contains("Foxglove as Replay Timeline", StringComparison.Ordinal)
                  && !editor.Contains("Use Foxglove as Replay Timeline", StringComparison.Ordinal)
                  && editor.Contains("Copy Foxglove URL", StringComparison.Ordinal)
                  && editor.Contains("Open in Foxglove", StringComparison.Ordinal)
                  && editor.Contains("/v1/files/", StringComparison.Ordinal),
                "139C-3C: manager Inspector exposes Foxglove timeline replay controls for the direct MCAP URL");
            Check(editor.Contains("Foxglove can load it and control replay time", StringComparison.OrdinalIgnoreCase)
                  && editor.Contains("Replay Auto Play is disabled", StringComparison.OrdinalIgnoreCase),
                "139C-3C2: manager Inspector states Foxglove owns replay time in timeline replay mode");
            Check(editor.Contains("remoteFileServerEnabled = GetBool(\"_enableRemoteMcapFileServer\")", StringComparison.Ordinal)
                  && editor.Contains("DisabledScope(remoteFileServerEnabled)", StringComparison.Ordinal)
                  && editor.Contains("Foxglove as Replay Timeline is on", StringComparison.Ordinal)
                  && editor.Contains("Replay Auto Play is unavailable", StringComparison.Ordinal),
                "139C-3C4: manager Inspector disables Replay Auto Play while Foxglove owns the timeline");
            Check(!editor.Contains("Open Local MCAP", StringComparison.Ordinal)
                  && !editor.Contains("Copy Manifest URL", StringComparison.Ordinal),
                "139C-3C3: manager Inspector omits local-file and manifest diagnostic buttons from the product path");
            Check(editor.Contains("Foxglove.exe", StringComparison.Ordinal)
                  || editor.Contains("foxglove", StringComparison.Ordinal),
                "139C-3D: manager Inspector can open Foxglove without requiring a separate Tools workflow");
            Check(editor.Contains("BuildRemoteFileDesktopUrl(remoteUrl)", StringComparison.Ordinal)
                  && editor.Contains("Application.OpenURL(foxgloveUrl)", StringComparison.Ordinal)
                  && !editor.Contains("FindFoxgloveCliExecutable", StringComparison.Ordinal),
                "139C-3D2: Open in Foxglove uses a remote-file deeplink instead of the data-platform CLI");

            var setup = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            Check(setup.Contains("_replayAutoPlay && !_enableRemoteMcapFileServer", StringComparison.Ordinal)
                  && setup.Contains("Replay Auto Play ignored", StringComparison.Ordinal),
                "139C-3F: runtime ignores Replay Auto Play while Foxglove owns the replay timeline");
            Check(manager.Contains("_disableLivePublishers;", StringComparison.Ordinal)
                  || manager.Contains("_disableLivePublishers = false", StringComparison.Ordinal),
                "139C-3G: Disable Live Publishers defaults off for normal replay setup");
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

        private static string Read(string relativePath)
        {
            if (SourceCache.TryGetValue(relativePath, out var cached))
                return cached;

            var text = File.ReadAllText(RepoPath(relativePath));
            SourceCache[relativePath] = text;
            return text;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot() => CachedRepoRoot;

        private static string ResolveRepoRoot()
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
