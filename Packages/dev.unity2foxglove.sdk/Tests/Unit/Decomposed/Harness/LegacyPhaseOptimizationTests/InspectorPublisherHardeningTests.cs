// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-21/22/23/24/27 legacy phase optimization checks.

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-22")]
    [Trait("Domain", "Harness")]
    public sealed class InspectorPublisherHardeningTests
    {
        [Fact]
        public void InspectorAsyncChecksAndSmallFixesRemainPresent()
        {
            var check = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs");
            var cameraEditor = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var preflight = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs");
            var mcap = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var cameraInfo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");

            Assert.DoesNotContain("process.WaitForExit();", check, StringComparison.Ordinal);
            Assert.DoesNotContain("WaitForStreamDrain(stdoutTask, stderrTask, -1)", check, StringComparison.Ordinal);
            Assert.Contains("StartFfmpegCheck(", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => FfmpegExecutableCheck.Check", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update += CompleteFfmpegCheckIfReady", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("StartOpenH264Check(", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => OpenH264ExecutableCheck.Check", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update += CompleteOpenH264CheckIfReady", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("var windowStillAlive = this != null;", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.delayCall += () =>", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("ScheduleBrowsePath(", cameraEditor, StringComparison.Ordinal);
            Assert.DoesNotContain("BrowseOpenH264Path(property, dialogTitle, extension);", cameraEditor, StringComparison.Ordinal);
            Assert.DoesNotContain("BrowseFfmpeg(ffmpegPath);", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("serializedObject.targetObject == null", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("StartOpenH264Check(installedHelperPath, installedDllPath)", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => AnalyzeReplayMcapWorker", preflight, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update += CompleteAnalyzeReplayMcapIfReady", preflight, StringComparison.Ordinal);
            Assert.Contains("AssemblyReloadEvents.beforeAssemblyReload += CancelPendingWork", preflight, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update -= CompleteFindLatestRecordingIfReady", preflight, StringComparison.Ordinal);
            Assert.Contains("Analyzing replay file", preflight, StringComparison.Ordinal);
            Assert.Contains("Task.Run(() => FindLatestReadableRecordingWorker", preflight, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.update += CompleteFindLatestRecordingIfReady", preflight, StringComparison.Ordinal);
            Assert.Contains("Searching latest readable recording", preflight, StringComparison.Ordinal);
            Assert.Contains("catch (Exception ex)", preflight, StringComparison.Ordinal);
            Assert.Contains("Skipping unreadable MCAP", preflight, StringComparison.Ordinal);
            Assert.DoesNotContain("Undo.RecordObject(target, \"Disable Replay Auto Play\")", mcap, StringComparison.Ordinal);
            Assert.DoesNotContain("EditorUtility.SetDirty(target);", mcap, StringComparison.Ordinal);
            Assert.Contains("OpenCurrentEvidenceRoot()", mcap, StringComparison.Ordinal);
            Assert.Contains("catch (System.Exception ex)", mcap, StringComparison.Ordinal);
            Assert.Contains("Failed to open current schema evidence", mcap, StringComparison.Ordinal);
            Assert.Contains("_openH264CheckTask = null;", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("BuildCameraOutputModeLabels()", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("CameraVideoOutputProfile.ForMode", cameraEditor, StringComparison.Ordinal);
            Assert.DoesNotContain("currentIndex = 0;", cameraEditor, StringComparison.Ordinal);
            Assert.Contains("_cachedRootCaFingerprintPath", manager, StringComparison.Ordinal);
            Assert.Contains("GetCachedRootCaFingerprint", manager, StringComparison.Ordinal);
            Assert.Contains("_lastRootCaDistributorPath", manager, StringComparison.Ordinal);
            Assert.Contains("RestartEditorRootCaDistributorIfPossible", manager, StringComparison.Ordinal);
            Assert.Contains("PlayModeStateChange.EnteredEditMode", manager, StringComparison.Ordinal);
            Assert.Contains("ObjectFieldTypeCache", cameraInfo, StringComparison.Ordinal);
            Assert.Contains("TryGetValue(typeName", cameraInfo, StringComparison.Ordinal);
            Assert.DoesNotContain("private static bool _connectionSecurityExpanded", manager, StringComparison.Ordinal);
            Assert.Contains("private bool _connectionSecurityExpanded", manager, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14022MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_22Validation.cs", "--phase140-22", "Phase140_22Validation.Validate");
    }
}
