// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-22 regression coverage for Inspector and publisher editor hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_22Validation.
    /// </summary>
    public static class Phase140_22Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-22: Inspector Manager and Publisher Editors ===");
            _passed = 0;

            OpenH264ChecksAreAsyncAndBounded();
            McapReplayPreflightUsesAsyncEditorPolling();
            SmallInspectorFixesArePresent();
            LowPriorityInspectorHardeningIsPresent();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-22: {_passed} checks passed.");
        }

        private static void OpenH264ChecksAreAsyncAndBounded()
        {
            var check = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");

            Check(!check.Contains("process.WaitForExit();", StringComparison.Ordinal)
                  && !check.Contains("WaitForStreamDrain(stdoutTask, stderrTask, -1)", StringComparison.Ordinal),
                "140-22A-1: OpenH264 executable check has no unbounded post-exit waits");
            Check(editor.Contains("StartOpenH264Check(", StringComparison.Ordinal)
                  && editor.Contains("Task.Run(() => OpenH264ExecutableCheck.Check", StringComparison.Ordinal)
                  && editor.Contains("EditorApplication.update += CompleteOpenH264CheckIfReady", StringComparison.Ordinal),
                "140-22A-2: OpenH264 Inspector check runs asynchronously with editor polling");
            Check(editor.Contains("serializedObject.targetObject == null", StringComparison.Ordinal)
                  && editor.Contains("StartOpenH264Check(installedHelperPath, installedDllPath)", StringComparison.Ordinal),
                "140-22A-3: OpenH264 install callback guards destroyed editors and defers validation");
        }

        private static void McapReplayPreflightUsesAsyncEditorPolling()
        {
            var preflight = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs");

            Check(preflight.Contains("Task.Run(() => AnalyzeReplayMcapWorker", StringComparison.Ordinal)
                  && preflight.Contains("EditorApplication.update += CompleteAnalyzeReplayMcapIfReady", StringComparison.Ordinal)
                  && preflight.Contains("Analyzing replay file", StringComparison.Ordinal),
                "140-22B-1: MCAP replay analysis runs asynchronously with progress UI");
            Check(preflight.Contains("Task.Run(() => FindLatestReadableRecordingWorker", StringComparison.Ordinal)
                  && preflight.Contains("EditorApplication.update += CompleteFindLatestRecordingIfReady", StringComparison.Ordinal)
                  && preflight.Contains("Searching latest readable recording", StringComparison.Ordinal),
                "140-22B-2: latest recording search runs asynchronously with progress UI");
            Check(preflight.Contains("catch (Exception ex)", StringComparison.Ordinal)
                  && preflight.Contains("Skipping unreadable MCAP", StringComparison.Ordinal),
                "140-22B-3: latest recording search catches unexpected MCAP reader exceptions");
        }

        private static void SmallInspectorFixesArePresent()
        {
            var mcap = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var camera = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");

            Check(mcap.Contains("Undo.RecordObject(target, \"Disable Replay Auto Play\")", StringComparison.Ordinal),
                "140-22C-1: Replay Auto Play automatic disable is undoable");
            Check(mcap.Contains("OpenCurrentEvidenceRoot()", StringComparison.Ordinal)
                  && mcap.Contains("catch (System.Exception ex)", StringComparison.Ordinal)
                  && mcap.Contains("Failed to open current schema evidence", StringComparison.Ordinal),
                "140-22C-2: Open Current Evidence handles filesystem exceptions");
            Check(camera.Contains("BuildCameraOutputModeLabels()", StringComparison.Ordinal)
                  && camera.Contains("CameraVideoOutputProfile.ForMode", StringComparison.Ordinal)
                  && !camera.Contains("currentIndex = 0;", StringComparison.Ordinal),
                "140-22C-3: camera output mode labels follow the enum without silent JPEG fallback");
        }

        private static void LowPriorityInspectorHardeningIsPresent()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var cameraInfo = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");

            Check(manager.Contains("_cachedRootCaFingerprintPath", StringComparison.Ordinal)
                  && manager.Contains("GetCachedRootCaFingerprint", StringComparison.Ordinal),
                "140-22D-1: Root CA fingerprint is cached by resolved path");
            Check(manager.Contains("_lastRootCaDistributorPath", StringComparison.Ordinal)
                  && manager.Contains("RestartEditorRootCaDistributorIfPossible", StringComparison.Ordinal)
                  && manager.Contains("PlayModeStateChange.EnteredEditMode", StringComparison.Ordinal),
                "140-22D-2: Root CA distributor restarts after returning to edit mode");
            Check(cameraInfo.Contains("ObjectFieldTypeCache", StringComparison.Ordinal)
                  && cameraInfo.Contains("TryGetValue(typeName", StringComparison.Ordinal),
                "140-22D-3: CameraInfo fallback object field type lookup is cached");
            Check(!manager.Contains("private static bool _connectionSecurityExpanded", StringComparison.Ordinal)
                  && manager.Contains("private bool _connectionSecurityExpanded", StringComparison.Ordinal),
                "140-22D-4: Manager foldout state is per Inspector instance");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_22Validation.cs", StringComparison.Ordinal),
                "140-22E-1: test project compiles Phase140_22Validation");
            Check(registry.Contains("Ci(\"--phase140-22\", \"Phase 140-22\", Phase140_22Validation.Validate", StringComparison.Ordinal),
                "140-22E-2: validation registry exposes --phase140-22");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
