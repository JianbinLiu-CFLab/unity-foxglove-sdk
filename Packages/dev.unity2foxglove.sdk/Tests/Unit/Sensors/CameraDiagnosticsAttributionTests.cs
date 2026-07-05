// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 169B camera/frame-stall attribution diagnostics checks.

using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "169B")]
    [Trait("Domain", "Sensors")]
    public sealed class CameraDiagnosticsAttributionTests
    {
        [Fact]
        public void CameraSlowStageDiagnosticsAreGatedAndBounded()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var publisherDiagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Diagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var cameraDiagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("[SerializeField, Min(1f)] private float _cameraSlowStageThresholdMs = 50f;", publisher, StringComparison.Ordinal);
            Assert.Contains("_cameraSlowStageThresholdMs", publisherDiagnostics, StringComparison.Ordinal);
            Assert.Contains("TryBuildCameraSlowStageMessage", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("RecordReadbackScheduled", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("RecordCompletedJpegDrain", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("[Foxglove][CameraSlow]", cameraDiagnostics, StringComparison.Ordinal);
            Assert.True(
                cameraDiagnostics.IndexOf("if (!enabled)", StringComparison.Ordinal)
                < cameraDiagnostics.IndexOf("[Foxglove][CameraSlow]", StringComparison.Ordinal),
                "Slow camera diagnostics must check the diagnostics toggle before formatting log text.");
        }

        [Fact]
        public void FrameStallDiagnosticsIncludeLastCameraSnapshotWithSentinels()
        {
            var managerDiagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Diagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var cameraDiagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("readonly struct CameraTimingSnapshot", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("public static CameraTimingSnapshot LastSnapshotOrDefault", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("CameraTimingSnapshot.NoFrame", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("CameraPublishDiagnostics.LastSnapshotOrDefault", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraSnapshotAgeMs=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraRenderMs=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraPendingReadbacksBefore=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraPendingReadbacksAfter=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraEncodeQueue=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraCompletedQueue=", managerDiagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraInspectorExposesSlowStageThresholdUnderDiagnostics()
        {
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("private SerializedProperty _cameraSlowStageThresholdMs;", editor, StringComparison.Ordinal);
            Assert.Contains("serializedObject.FindProperty(\"_cameraSlowStageThresholdMs\")", editor, StringComparison.Ordinal);
            Assert.Contains("Slow Stage Threshold Ms", editor, StringComparison.Ordinal);
            Assert.True(
                editor.IndexOf("Log Camera Diagnostics", StringComparison.Ordinal)
                < editor.IndexOf("Slow Stage Threshold Ms", StringComparison.Ordinal),
                "The slow-stage threshold should live under the existing camera diagnostics toggle.");
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
