// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 115C schema evidence hardening validation.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115CValidation
    {
        private const string ExpectedFoxRunFixtureHash = "9a0f11b37e2893c60aadd6edddf6b83cae27407041c8a5dc413579ead7a1d58e";
        private const string SdkFixtureHash = "0000000000000000000000000000000000000000000000000000000000000000";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115C: Schema Evidence Hardening ===");
            _passed = 0;

            VerifySidecarSafety();
            VerifyCanonicalAndWriterHardening();
            VerifyRecordingCleanupSource();
            VerifyDebugOverlayNoThrowBoundary();
            VerifyRos2RegistryGuard();
            VerifyInspectorAndSettingsSyncSource();
            VerifyReplayIdentityPreflightSource();

            Console.WriteLine($"Phase 115C: {_passed} checks passed.");
        }

        private static void VerifySidecarSafety()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase115c-" + Guid.NewGuid().ToString("N"));
            var currentRoot = Path.Combine(tempRoot, "Generated");
            var mcapPath = Path.Combine(tempRoot, "recording_20260521_190000.mcap");
            var sidecarRoot = Path.ChangeExtension(mcapPath, ".schema");

            try
            {
                Directory.CreateDirectory(tempRoot);
                CreateIncompleteEvidenceFixture(currentRoot);
                var strict = SchemaEvidenceSidecarWriter.StageSidecar(
                    mcapPath,
                    currentRoot,
                    SchemaIdentityMode.Strict,
                    requireComplete: true);

                Check(!strict.Success
                      && !Directory.Exists(sidecarRoot)
                      && !Directory.EnumerateDirectories(tempRoot, "*.tmp-*").Any(),
                    "115C-A1: Strict incomplete evidence leaves no target or staged sidecar");

                Directory.CreateDirectory(sidecarRoot);
                var sentinel = Path.Combine(sidecarRoot, "sentinel.txt");
                File.WriteAllText(sentinel, "previous sidecar", Encoding.UTF8);

                var failedReplacement = SchemaEvidenceSidecarWriter.StageSidecar(
                    mcapPath,
                    currentRoot,
                    SchemaIdentityMode.Strict,
                    requireComplete: true);

                Check(!failedReplacement.Success
                      && File.Exists(sentinel)
                      && File.ReadAllText(sentinel, Encoding.UTF8) == "previous sidecar",
                    "115C-A2: failed sidecar replacement preserves an existing sidecar");

                CreateCompleteEvidenceFixture(currentRoot);
                var staged = SchemaEvidenceSidecarWriter.StageSidecar(
                    mcapPath,
                    currentRoot,
                    SchemaIdentityMode.Warn,
                    requireComplete: false);
                Check(staged.Success
                      && Directory.Exists(staged.TemporaryDirectory)
                      && File.Exists(sentinel),
                    "115C-A3: successful sidecar staging does not replace target before publish");

            Check(SchemaEvidenceSidecarWriter.PublishStagedSidecar(staged, out _)
                      && File.Exists(Path.Combine(sidecarRoot, "schema-evidence.json"))
                      && !File.Exists(sentinel),
                    "115C-A4: staged sidecar publishes into final location after explicit commit");

                var setup = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
                var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
                var setupRecordingEnd = setup.IndexOf("private bool PublishPendingRecordingSidecar", StringComparison.Ordinal);
                var foundSetupRecordingBoundary = setupRecordingEnd >= 0;
                var setupRecordingSource = foundSetupRecordingBoundary ? setup.Substring(0, setupRecordingEnd) : string.Empty;
                Check(foundSetupRecordingBoundary
                      && setupRecordingSource.Contains("SchemaEvidenceSidecarWriter.StageSidecar", StringComparison.Ordinal)
                      && !setupRecordingSource.Contains("SchemaEvidenceSidecarWriter.PublishStagedSidecar", StringComparison.Ordinal)
                      && server.IndexOf("_runtime.Start", StringComparison.Ordinal) <
                      server.IndexOf("PublishPendingRecordingSidecar", StringComparison.Ordinal)
                      && server.Contains("CleanupPendingRecordingSidecar", StringComparison.Ordinal),
                    "115C-A5: recording sidecar publishes only after runtime recording startup succeeds");
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static void VerifyCanonicalAndWriterHardening()
        {
            var manifestWriter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestJsonWriter.cs");
            var schemaInfoWriter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");
            var typeExprEmitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TypeExprEmitter.cs");
            Check(!manifestWriter.Contains("ToString(\"R\"", StringComparison.Ordinal)
                  && !schemaInfoWriter.Contains("ToString(\"R\"", StringComparison.Ordinal)
                  && !typeExprEmitter.Contains("ToString(\"R\"", StringComparison.Ordinal)
                  && manifestWriter.Contains("ToString(\"G9\"", StringComparison.Ordinal)
                  && schemaInfoWriter.Contains("ToString(\"G9\"", StringComparison.Ordinal)
                  && typeExprEmitter.Contains("ToString(\"G9\"", StringComparison.Ordinal),
                "115C-B1: canonical writers and generated source constants use deterministic G9 float formatting");

            var manifest = FoxRunManifestBuilder.Build(Phase115BFixtureMembers());
            Check(manifest.GlobalManifestHash == ExpectedFoxRunFixtureHash,
                "115C-B2: updated FoxRun fixture hash reflects G9 canonical text");

            Check(HashWrittenLast("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs", "ManifestReportFileName", "ManifestHashFileName")
                  && HashWrittenLast("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestWriter.cs", "ManifestReportFileName", "ManifestHashFileName"),
                "115C-B3: generated manifest hash sidecars are written after JSON and report files");

            var foxRunWriter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");
            var aggregateWriter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestWriter.cs");
            Check(foxRunWriter.Contains(".tmp-", StringComparison.Ordinal)
                  && foxRunWriter.Contains("ReplaceFile", StringComparison.Ordinal)
                  && foxRunWriter.Contains("IOException", StringComparison.Ordinal)
                  && foxRunWriter.Contains("UnauthorizedAccessException", StringComparison.Ordinal)
                  && foxRunWriter.Contains("File.Copy(tempPath, path, overwrite: true)", StringComparison.Ordinal)
                  && aggregateWriter.Contains(".tmp-", StringComparison.Ordinal)
                  && aggregateWriter.Contains("ReplaceFile", StringComparison.Ordinal)
                  && aggregateWriter.Contains("IOException", StringComparison.Ordinal)
                  && aggregateWriter.Contains("UnauthorizedAccessException", StringComparison.Ordinal)
                  && aggregateWriter.Contains("File.Copy(tempPath, path, overwrite: true)", StringComparison.Ordinal),
                "115C-B4: generated manifest writers use temp-and-replace writes with Windows sync-folder fallback");
        }

        private static void VerifyRecordingCleanupSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs");
            var catchIndex = source.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
            var detachIndex = catchIndex >= 0
                ? source.IndexOf("parameters.OnParameterChanged -= OnParameterChanged;", catchIndex, StringComparison.Ordinal)
                : -1;
            var clearIndex = catchIndex >= 0
                ? source.IndexOf("session.SetRecorder(null);", catchIndex, StringComparison.Ordinal)
                : -1;
            Check(catchIndex >= 0 && detachIndex > catchIndex && detachIndex < clearIndex,
                "115C-C1: recording attach failure detaches parameter change handler before clearing state");

            Check(source.Contains("if (_parameters != null) _parameters.OnParameterChanged -= OnParameterChanged;", StringComparison.Ordinal),
                "115C-C2: recording detach unsubscribes even when no recorder was installed");
        }

        private static void VerifyDebugOverlayNoThrowBoundary()
        {
            Check(!FoxgloveDebugOverlayEnvelope.TryCreateValue(
                    "/debug/hostile",
                    "phase115c",
                    "bad",
                    new HostileObject(),
                    null,
                    out _),
                "115C-D1: debug overlay rejects arbitrary custom reference values");

            var throwingEnumerableAccepted = false;
            var throwingEnumerableThrew = Throws(() => throwingEnumerableAccepted = FoxgloveDebugOverlayEnvelope.TryCreateValue(
                "/debug/hostile",
                "phase115c",
                "bad",
                new ThrowingEnumerable(),
                null,
                out _));
            Check(!throwingEnumerableThrew && !throwingEnumerableAccepted,
                "115C-D2: debug overlay validation returns false instead of throwing for hostile enumerables");

            var helper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveDebugOverlay.cs");
            Check(helper.Contains("catch", StringComparison.Ordinal)
                  && helper.Contains("return false;", StringComparison.Ordinal)
                  && !helper.Contains("FoxRunManifest", StringComparison.Ordinal)
                  && !helper.Contains("Mcap", StringComparison.Ordinal)
                  && !helper.Contains("protobuf", StringComparison.OrdinalIgnoreCase)
                  && !helper.Contains("ROS2", StringComparison.Ordinal),
                "115C-D3: debug overlay helper catches publish failures and stays non-contract");
        }

        private static void VerifyRos2RegistryGuard()
        {
            var builder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            Check(builder.Contains("entries.Count != FoxgloveRos2MsgSchemaCatalog.SourceFileCount", StringComparison.Ordinal)
                  && builder.Contains("ROS2 .msg schema catalog count mismatch", StringComparison.Ordinal),
                "115C-E1: aggregate builder guards ROS2 source count drift");

            var aggregate = Unity2FoxgloveSchemaManifestBuilder.Build(FoxRunManifestBuilder.Build(Phase115BFixtureMembers()));
            Check(aggregate.Sections.Ros2MsgRegistry.EntryCount == aggregate.Sections.Ros2MsgRegistry.SourceFileCount,
                "115C-E2: valid ROS2 registry still builds with matching source count");
        }

        private static void VerifyInspectorAndSettingsSyncSource()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var mcapEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var layout = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerInspectorLayout.cs");
            var settings = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs");
            var hook = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs");
            var build = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");

            Check(editor.Contains("private static bool _connectionSecurityExpanded;", StringComparison.Ordinal)
                  && editor.Contains("private static bool _publishDataExpanded;", StringComparison.Ordinal)
                  && editor.Contains("private static bool _schemaEvidenceAdvancedExpanded;", StringComparison.Ordinal)
                  && layout.Contains("WorkflowSubsection", StringComparison.Ordinal),
                "115C-F1: low-frequency Inspector sections and schema evidence default collapsed");

            Check(mcapEditor.Contains("Schema Evidence (Advanced)", StringComparison.Ordinal)
                  && mcapEditor.Contains("Refresh Evidence Now", StringComparison.Ordinal)
                  && mcapEditor.Contains("Edit Project Settings", StringComparison.Ordinal)
                  && mcapEditor.Contains("Evidence refreshes automatically on Play, Build, and Recording", StringComparison.Ordinal)
                  && mcapEditor.Contains("SettingsService.OpenProjectSettings", StringComparison.Ordinal),
                "115C-F2: Inspector labels explain automatic evidence refresh and project settings ownership");

            Check(settings.Contains("SyncSerializedManager", StringComparison.Ordinal)
                  && settings.Contains("SyncOpenSceneManagers", StringComparison.Ordinal)
                  && settings.Contains("SyncManagersInScene", StringComparison.Ordinal)
                  && settings.Contains("SaveAndSync", StringComparison.Ordinal)
                  && hook.Contains("SyncOpenSceneManagers", StringComparison.Ordinal)
                  && build.Contains("IProcessSceneWithReport", StringComparison.Ordinal)
                  && build.Contains("SyncManagersInScene(scene)", StringComparison.Ordinal),
                "115C-F3: project schema evidence defaults sync to uninspected Managers on settings changes, Play, and Build");

            Check(hook.Contains("EditorApplication.isPlaying = false;", StringComparison.Ordinal)
                  && hook.Contains("Failed to refresh canonical manifest before Play Mode", StringComparison.Ordinal),
                "115C-F4: Play Mode refresh failure cancels Play instead of using stale evidence");

            Check(!manager.Contains("[Header(\"Schema Evidence\")]", StringComparison.Ordinal),
                "115C-F5: runtime Manager no longer emits a duplicate Schema Evidence header");
        }

        private static void VerifyReplayIdentityPreflightSource()
        {
            var drawer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs");

            Check(drawer.Contains("Replay Identity Preflight", StringComparison.Ordinal)
                  && drawer.Contains("Recorded FoxRun Hash", StringComparison.Ordinal)
                  && drawer.Contains("Current FoxRun Hash", StringComparison.Ordinal)
                  && drawer.Contains("Match", StringComparison.Ordinal)
                  && drawer.Contains("Mismatch", StringComparison.Ordinal)
                  && drawer.Contains("Missing Evidence", StringComparison.Ordinal),
                "115C-G1: replay preflight displays recorded/current FoxRun hash and identity status");

            Check(drawer.Contains("schema-evidence.json", StringComparison.Ordinal)
                  && drawer.Contains("globalManifestHash", StringComparison.Ordinal)
                  && drawer.Contains("foxRun", StringComparison.Ordinal)
                  && drawer.Contains("Unity2FoxgloveSchemaEvidencePaths.ResolveFoxRunOutputDirectory", StringComparison.Ordinal),
                "115C-G2: replay preflight compares recording sidecar evidence with current evidence");

            Check(drawer.Contains("Open Recording Evidence", StringComparison.Ordinal)
                  && drawer.Contains("Compare With Current", StringComparison.Ordinal)
                  && drawer.Contains("Copy Identity Summary", StringComparison.Ordinal)
                  && drawer.Contains("EditorUtility.RevealInFinder", StringComparison.Ordinal)
                  && drawer.Contains("Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts", StringComparison.Ordinal)
                  && drawer.Contains("EditorGUIUtility.systemCopyBuffer", StringComparison.Ordinal),
                "115C-G3: replay preflight exposes open, compare, and copy identity actions");

            Check(drawer.Contains("GUI.FocusControl(null)", StringComparison.Ordinal)
                  && drawer.Contains("EditorGUIUtility.editingTextField = false", StringComparison.Ordinal)
                  && drawer.Contains("serializedObject.ApplyModifiedProperties", StringComparison.Ordinal)
                  && drawer.Contains("serializedObject.Update", StringComparison.Ordinal)
                  && drawer.Contains("InternalEditorUtility.RepaintAllViews", StringComparison.Ordinal),
                "115C-G4: Use Latest Recording applies the replay path and repaints the Inspector");
        }

        private static IReadOnlyList<FoxRunManifestMember> Phase115BFixtureMembers()
        {
            return new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "RobotState",
                    "_batteryLevel",
                    "field",
                    "System.Single",
                    true,
                    false,
                    "",
                    "/phase112/battery",
                    10f,
                    "",
                    1,
                    0.001f,
                    0f)
            };
        }

        private static bool HashWrittenLast(string relativePath, string reportToken, string hashToken)
        {
            var source = ReadRepoText(relativePath);
            var jsonIndex = source.IndexOf("WriteIfChanged(Path.Combine(outputDirectory, ManifestJsonFileName)", StringComparison.Ordinal);
            var reportIndex = source.IndexOf("WriteIfChanged(Path.Combine(outputDirectory, " + reportToken + ")", StringComparison.Ordinal);
            var hashIndex = source.IndexOf("WriteIfChanged(Path.Combine(outputDirectory, " + hashToken + ")", StringComparison.Ordinal);
            return jsonIndex >= 0 && reportIndex > jsonIndex && hashIndex > reportIndex;
        }

        private static void CreateIncompleteEvidenceFixture(string currentRoot)
        {
            var foxRun = Path.Combine(currentRoot, "FoxRun");
            Directory.CreateDirectory(foxRun);
            File.WriteAllText(Path.Combine(foxRun, "foxrun.manifest.json"), "{}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(foxRun, "foxrun.manifest.hash"), ExpectedFoxRunFixtureHash + "\n", Encoding.UTF8);
        }

        private static void CreateCompleteEvidenceFixture(string currentRoot)
        {
            CreateIncompleteEvidenceFixture(currentRoot);
            var foxRun = Path.Combine(currentRoot, "FoxRun");
            File.WriteAllText(Path.Combine(foxRun, "foxrun.manifest.report.json"), "{}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(foxRun, "FoxRunSchemaInfo.g.cs"), "// schema info\n", Encoding.UTF8);

            var aggregate = Path.Combine(currentRoot, "Unity2Foxglove");
            Directory.CreateDirectory(aggregate);
            File.WriteAllText(Path.Combine(aggregate, "unity2foxglove.schema-manifest.json"), "{}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(aggregate, "unity2foxglove.schema-manifest.hash"), SdkFixtureHash + "\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(aggregate, "unity2foxglove.schema-manifest.report.json"), "{}", Encoding.UTF8);
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase115C file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase115C validation.");
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temporary validation directories.
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private sealed class HostileObject
        {
            public string Value => throw new InvalidOperationException("getter should not be evaluated");
        }

        private sealed class ThrowingEnumerable : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                throw new InvalidOperationException("enumeration failed");
            }
        }
    }
}
