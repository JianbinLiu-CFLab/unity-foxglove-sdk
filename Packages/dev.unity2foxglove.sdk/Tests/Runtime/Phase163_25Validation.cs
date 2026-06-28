// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-25 validation for Inspector UI lifecycle and state guards.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_25Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-25: Manager and Publisher Inspector UI ===");
            _passed = 0;

            McapReplayPreflightCleansEditorCallbacks();
            CameraInfoInspectorHandlesOptionalFields();
            ManagerInspectorAvoidsSerializedPropertyUndoConflicts();
            PublisherInspectorsSurfaceInvalidState();
            CameraPublisherInspectorUsesInstanceState();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-25: {_passed} checks passed.");
        }

        private static void McapReplayPreflightCleansEditorCallbacks()
        {
            var drawer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs");
            var managerRos2 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Ros2Bridge.cs");

            Check(drawer.Contains("McapReplayPreflightDrawer : IDisposable", StringComparison.Ordinal)
                  && drawer.Contains("AssemblyReloadEvents.beforeAssemblyReload += CancelPendingWork", StringComparison.Ordinal)
                  && drawer.Contains("AssemblyReloadEvents.beforeAssemblyReload -= CancelPendingWork", StringComparison.Ordinal),
                "163-25A-1: MCAP preflight drawer registers and unregisters reload cleanup");
            Check(drawer.Contains("EditorApplication.update -= CompleteAnalyzeReplayMcapIfReady", StringComparison.Ordinal)
                  && drawer.Contains("EditorApplication.update -= CompleteFindLatestRecordingIfReady", StringComparison.Ordinal)
                  && drawer.Contains("_pendingLatestSerializedObject = null", StringComparison.Ordinal),
                "163-25A-2: MCAP preflight cleanup drops update callbacks and stale serialized targets");
            Check(managerRos2.Contains("_mcapReplayPreflight.Dispose();", StringComparison.Ordinal),
                "163-25A-3: manager editor disables MCAP preflight drawer with other sub-drawers");
        }

        private static void CameraInfoInspectorHandlesOptionalFields()
        {
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");

            Check(editor.Contains("var tfAnchorEnabled = publishCameraTfAnchor != null && publishCameraTfAnchor.boolValue", StringComparison.Ordinal)
                  && editor.Contains("new EditorGUI.DisabledScope(!tfAnchorEnabled)", StringComparison.Ordinal),
                "163-25B-1: CameraInfo TF anchor section guards missing serialized field");
            Check(editor.Contains("publishRateSource.enumValueIndex == (int)PublisherRateSource.OverrideLocal", StringComparison.Ordinal)
                  && editor.Contains("new EditorGUI.DisabledScope(!usesLocalRate)", StringComparison.Ordinal),
                "163-25B-2: CameraInfo local publish rate is disabled when manager default is selected");
        }

        private static void ManagerInspectorAvoidsSerializedPropertyUndoConflicts()
        {
            var mcap = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var replayBlock = Slice(mcap, "private void DrawReplayAutoPlayControl()", "private void DrawRemoteFileAccessSection");
            var certificateBlock = Slice(manager, "private void GenerateLocalDevCertificate()", "private void DrawCertificateUtilityButtons");

            Check(!replayBlock.Contains("Undo.RecordObject", StringComparison.Ordinal)
                  && !replayBlock.Contains("EditorUtility.SetDirty", StringComparison.Ordinal)
                  && replayBlock.Contains("replayAutoPlay.boolValue = false", StringComparison.Ordinal),
                "163-25C-1: replay auto-play coercion relies on SerializedProperty apply without double undo");
            Check(certificateBlock.IndexOf("Undo.RecordObject(target, \"Generate Local Dev WSS Certificate\")", StringComparison.Ordinal)
                  < certificateBlock.IndexOf("serializedObject.ApplyModifiedProperties();", StringComparison.Ordinal),
                "163-25C-2: local certificate generation records undo before flushing serialized properties");
        }

        private static void PublisherInspectorsSurfaceInvalidState()
        {
            var pointCloud = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");

            Check(pointCloud.Contains("Point cloud output mode is outside the supported enum range", StringComparison.Ordinal)
                  && pointCloud.Contains("MessageType.Error", StringComparison.Ordinal)
                  && pointCloud.Contains("return;", StringComparison.Ordinal),
                "163-25D-1: point cloud inspector reports unsupported output enum values instead of silently clamping");
        }

        private static void CameraPublisherInspectorUsesInstanceState()
        {
            var camera = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");

            Check(camera.Contains("private bool _showRos2Outputs;", StringComparison.Ordinal)
                  && camera.Contains("private bool _showAdvancedJpeg;", StringComparison.Ordinal)
                  && camera.Contains("private bool _showDiagnostics;", StringComparison.Ordinal)
                  && !camera.Contains("private static bool _showRos2Outputs", StringComparison.Ordinal),
                "163-25E-1: camera publisher foldout state is instance-scoped");
            Check(Slice(camera, "private void OnDisable()", "private static GUIContent Label").Contains("_openH264CheckTask = null;", StringComparison.Ordinal),
                "163-25E-2: camera publisher editor drops OpenH264 task reference on disable");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_25Validation.cs", StringComparison.Ordinal),
                "163-25F-1: runtime test project compiles Phase163_25Validation");
            Check(registry.Contains("--phase163-25", StringComparison.Ordinal)
                  && registry.Contains("Phase163_25Validation.Validate", StringComparison.Ordinal),
                "163-25F-2: validation registry exposes --phase163-25");
        }

        private static string Slice(string text, string startMarker, string endMarker)
        {
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing start marker: " + startMarker);
            var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidOperationException("Missing end marker: " + endMarker);
            return text.Substring(start, end - start);
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
