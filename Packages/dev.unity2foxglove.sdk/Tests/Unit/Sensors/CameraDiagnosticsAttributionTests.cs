// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 169B camera/frame-stall attribution diagnostics checks.

using System;
using System.IO;
using Unity.FoxgloveSDK.Util;
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
            var sharedSnapshotPath = PathOf("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraTimingDiagnostics.cs");

            Assert.True(
                File.Exists(sharedSnapshotPath),
                "Camera timing snapshots must live in the core runtime assembly so FoxgloveManager does not depend on Unity.FoxgloveSDK.Proto.");
            var sharedSnapshot = File.ReadAllText(sharedSnapshotPath).Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("readonly struct CameraTimingSnapshot", sharedSnapshot, StringComparison.Ordinal);
            Assert.Contains("internal static class CameraTimingDiagnostics", sharedSnapshot, StringComparison.Ordinal);
            Assert.Contains("CameraTimingSnapshot.NoFrame", sharedSnapshot, StringComparison.Ordinal);
            Assert.Contains("CameraTimingDiagnostics.LastSnapshotOrDefault", managerDiagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("CameraPublishDiagnostics", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("CameraTimingDiagnostics.Publish", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("CameraTimingDiagnostics.Reset", cameraDiagnostics, StringComparison.Ordinal);
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

        [Fact]
        public void CameraSourceCaptureGateDefaultsToBoundedVisualizationRate()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var publisherDiagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Diagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var cameraDiagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var gatePath = PathOf("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraCaptureRateGate.cs");

            Assert.True(
                File.Exists(gatePath),
                "Camera capture needs a shared pre-render rate gate so heavy render/readback work is skipped before Camera.Render().");
            Assert.Contains("private const float DefaultMaxCaptureRateHz = 6f;", publisher, StringComparison.Ordinal);
            Assert.Contains("[SerializeField, Min(0f)] private float _maxCaptureRateHz = DefaultMaxCaptureRateHz;", publisher, StringComparison.Ordinal);
            Assert.Contains("AllowCameraCaptureBySourceRate", publisherDiagnostics, StringComparison.Ordinal);
            Assert.Contains("CameraCaptureRateGate.ShouldCapture", publisherDiagnostics, StringComparison.Ordinal);
            var rateGateIndex = publisher.IndexOf("AllowCameraCaptureBySourceRate(renderUnixNs)", StringComparison.Ordinal);
            var renderIndex = publisher.IndexOf("_captureResources.CaptureCamera.Render();", StringComparison.Ordinal);
            Assert.True(rateGateIndex >= 0, "Camera publisher should call the source rate gate with the resolved capture timestamp.");
            Assert.True(
                rateGateIndex < renderIndex,
                "The source rate gate must run before Camera.Render so skipped frames do not touch the GPU/readback path.");
            Assert.Contains("RecordRateSkip", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("rateSkip=", cameraDiagnostics, StringComparison.Ordinal);
            Assert.Contains("private SerializedProperty _maxCaptureRateHz;", editor, StringComparison.Ordinal);
            Assert.Contains("Max Capture Rate Hz", editor, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraCaptureRateGateThrottlesAndResetsOnBackwardClockJump()
        {
            const ulong startNs = 1_700_000_000_000_000_000UL;
            ulong lastCaptureNs = 0UL;
            var sixHzIntervalNs = CameraCaptureRateGate.ResolveIntervalNs(6f);
            var tenHzStepNs = 100_000_000UL;

            var captured = 0;
            for (var i = 0UL; i <= 10UL; i++)
            {
                if (CameraCaptureRateGate.ShouldCapture(ref lastCaptureNs, startNs + i * tenHzStepNs, sixHzIntervalNs))
                    captured++;
            }

            Assert.Equal(6, captured);
            Assert.True(CameraCaptureRateGate.ShouldCapture(ref lastCaptureNs, startNs + tenHzStepNs / 2UL, sixHzIntervalNs));
            Assert.False(CameraCaptureRateGate.ShouldCapture(ref lastCaptureNs, startNs + tenHzStepNs + tenHzStepNs / 2UL, sixHzIntervalNs));
        }

        private static string Text(string relativePath)
            => File.ReadAllText(PathOf(relativePath));

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

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
