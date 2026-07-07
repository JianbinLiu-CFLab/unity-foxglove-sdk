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
            Assert.DoesNotContain("Debug.Log(message)", publisherDiagnostics, StringComparison.Ordinal);
            Assert.Contains("LogOption.NoStacktrace", publisherDiagnostics, StringComparison.Ordinal);
            Assert.True(
                cameraDiagnostics.IndexOf("if (!enabled)", StringComparison.Ordinal)
                < cameraDiagnostics.IndexOf("[Foxglove][CameraSlow]", StringComparison.Ordinal),
                "Slow camera diagnostics must check the diagnostics toggle before formatting log text.");
        }

        [Fact]
        public void VideoDiagnosticsUseNoStacktraceLogging()
        {
            var video = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Video.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("EmitVideoDiagnosticsIfNeeded", video, StringComparison.Ordinal);
            Assert.Contains("LogVideoIfNeeded", video, StringComparison.Ordinal);
            Assert.DoesNotContain("Debug.Log(message)", video, StringComparison.Ordinal);
            Assert.Contains("LogOption.NoStacktrace", video, StringComparison.Ordinal);
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
            Assert.Contains("Main-thread diagnostics bridge", sharedSnapshot, StringComparison.Ordinal);
            Assert.Contains("cameraSnapshotAgeMs=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraRenderMs=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraPendingReadbacksBefore=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraPendingReadbacksAfter=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraEncodeQueue=", managerDiagnostics, StringComparison.Ordinal);
            Assert.Contains("cameraCompletedQueue=", managerDiagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraHealthSkipsAreLoggedSeparatelyFromHardBudgetSkips()
        {
            var diagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("_readbackBudgetSkipCount", diagnostics, StringComparison.Ordinal);
            Assert.Contains("_healthReadbackSkipCount", diagnostics, StringComparison.Ordinal);
            Assert.Contains("healthSkips(readback=", diagnostics, StringComparison.Ordinal);
            Assert.Contains("renderPressureSkip=", diagnostics, StringComparison.Ordinal);
            Assert.Contains("videoOutputSkip=", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("case CameraPipelineHealthSkipReason.ReadbackQueueFull:\n                    _readbackBudgetSkipCount++;", diagnostics, StringComparison.Ordinal);
            Assert.DoesNotContain("case CameraPipelineHealthSkipReason.PixelBudgetExceeded:\n                    _pixelBudgetSkipCount++;", diagnostics, StringComparison.Ordinal);
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
            Assert.Contains("private const float DefaultMaxCaptureRateHz = 10f;", publisher, StringComparison.Ordinal);
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

        [Fact]
        public void CameraPipelineHealthGateRequiresIdlePipelineBeforeRender()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var jpeg = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var diagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("[SerializeField] private bool _requireIdleJpegPipeline = true;", publisher, StringComparison.Ordinal);
            Assert.Contains("[SerializeField, Min(0f)] private float _pipelineCooldownThresholdMs = 50f;", publisher, StringComparison.Ordinal);
            Assert.Contains("[SerializeField, Min(0f)] private float _pipelineCooldownMs = 1000f;", publisher, StringComparison.Ordinal);
            Assert.Contains("AllowJpegCaptureByPipelineHealth", jpeg, StringComparison.Ordinal);
            Assert.Contains("requireIdlePipeline: _requireIdleJpegPipeline", jpeg, StringComparison.Ordinal);
            Assert.Contains("pipelineCooldownActive: PipelineCooldownActive()", jpeg, StringComparison.Ordinal);
            Assert.Contains("RecordPipelineCooldownIfNeeded(renderMs)", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("RecordPipelineCooldownIfNeeded(readbackLatencyMs)", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("RecordPipelineCooldownIfNeeded(result.EncodeMs)", jpeg, StringComparison.Ordinal);
            var healthGateIndex = publisher.IndexOf("AllowJpegCaptureByPipelineHealth()", StringComparison.Ordinal);
            var renderIndex = publisher.IndexOf("_captureResources.CaptureCamera.Render();", StringComparison.Ordinal);
            Assert.True(healthGateIndex >= 0, "Camera publisher should check pipeline health before scheduling capture.");
            Assert.True(
                healthGateIndex < renderIndex,
                "The pipeline health gate must run before Camera.Render so busy pipelines do not touch the GPU/readback path.");
            Assert.Contains("RecordPipelineCooldownSkip", diagnostics, StringComparison.Ordinal);
            Assert.Contains("cooldownSkip=", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Require Idle JPEG Pipeline", editor, StringComparison.Ordinal);
            Assert.Contains("Pipeline Cooldown Threshold Ms", editor, StringComparison.Ordinal);
            Assert.Contains("Pipeline Cooldown Ms", editor, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraFrameBudgetPolicyBlocksBusyOrCoolingPipeline()
        {
            var idle = new CameraFrameBudgetInput
            {
                RequireIdlePipeline = true,
                PendingReadbacks = 0,
                MaxPendingReadbacks = 1,
                EncodeQueueDepth = 0,
                MaxEncodeQueueDepth = 2,
                CompletedQueueDepth = 0,
                MaxCompletedQueueDepth = 2,
                Width = 640,
                Height = 480
            };

            Assert.True(CameraFrameBudgetPolicy.Evaluate(idle).AllowCapture);

            var pending = idle;
            pending.PendingReadbacks = 1;
            var pendingResult = CameraFrameBudgetPolicy.Evaluate(pending);
            Assert.False(pendingResult.AllowCapture);
            Assert.Equal(CameraFrameBudgetSkipReason.ReadbackQueueFull, pendingResult.SkipReason);

            var encodeBusy = idle;
            encodeBusy.EncodeQueueDepth = 1;
            var encodeResult = CameraFrameBudgetPolicy.Evaluate(encodeBusy);
            Assert.False(encodeResult.AllowCapture);
            Assert.Equal(CameraFrameBudgetSkipReason.EncodeQueueFull, encodeResult.SkipReason);

            var completedBusy = idle;
            completedBusy.CompletedQueueDepth = 1;
            var completedResult = CameraFrameBudgetPolicy.Evaluate(completedBusy);
            Assert.False(completedResult.AllowCapture);
            Assert.Equal(CameraFrameBudgetSkipReason.CompletedQueueFull, completedResult.SkipReason);

            var cooling = idle;
            cooling.PipelineCooldownActive = true;
            var coolingResult = CameraFrameBudgetPolicy.Evaluate(cooling);
            Assert.False(coolingResult.AllowCapture);
            Assert.Equal(CameraFrameBudgetSkipReason.PipelineCooldown, coolingResult.SkipReason);

            var tooManyPixels = idle;
            tooManyPixels.MaxPixelsPerFrame = (640 * 480) - 1;
            var pixelResult = CameraFrameBudgetPolicy.Evaluate(tooManyPixels);
            Assert.False(pixelResult.AllowCapture);
            Assert.Equal(CameraFrameBudgetSkipReason.PixelBudgetExceeded, pixelResult.SkipReason);
        }

        [Fact]
        public void CameraMainLoopHealthGateRequiresStableFramesBeforeRender()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var jpeg = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var diagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            var stableFramesRemaining = 0;
            Assert.False(CameraFrameHealthGatePolicy.ShouldCapture(
                ref stableFramesRemaining,
                frameDeltaMs: 333.33,
                maxHealthyFrameDeltaMs: 100d,
                stableFramesRequired: 2));
            Assert.Equal(2, stableFramesRemaining);
            Assert.False(CameraFrameHealthGatePolicy.ShouldCapture(
                ref stableFramesRemaining,
                frameDeltaMs: 16.67,
                maxHealthyFrameDeltaMs: 100d,
                stableFramesRequired: 2));
            Assert.Equal(1, stableFramesRemaining);
            Assert.False(CameraFrameHealthGatePolicy.ShouldCapture(
                ref stableFramesRemaining,
                frameDeltaMs: 16.67,
                maxHealthyFrameDeltaMs: 100d,
                stableFramesRequired: 2));
            Assert.Equal(0, stableFramesRemaining);
            Assert.True(CameraFrameHealthGatePolicy.ShouldCapture(
                ref stableFramesRemaining,
                frameDeltaMs: 16.67,
                maxHealthyFrameDeltaMs: 100d,
                stableFramesRequired: 2));

            Assert.Contains("[SerializeField, Min(0f)] private float _mainLoopCaptureCooldownThresholdMs = 100f;", publisher, StringComparison.Ordinal);
            Assert.Contains("[SerializeField, Min(0)] private int _mainLoopStableFramesBeforeCapture = 2;", publisher, StringComparison.Ordinal);
            Assert.Contains("AllowJpegCaptureByMainLoopHealth", jpeg, StringComparison.Ordinal);
            Assert.Contains("Time.unscaledDeltaTime", jpeg, StringComparison.Ordinal);
            Assert.Contains("CameraFrameHealthGatePolicy.ShouldCapture", jpeg, StringComparison.Ordinal);
            var mainLoopHealthIndex = publisher.IndexOf("AllowJpegCaptureByMainLoopHealth()", StringComparison.Ordinal);
            var renderIndex = publisher.IndexOf("_captureResources.CaptureCamera.Render();", StringComparison.Ordinal);
            Assert.True(mainLoopHealthIndex >= 0, "Camera publisher should check frame health before scheduling capture.");
            Assert.True(
                mainLoopHealthIndex < renderIndex,
                "The main-loop health gate must run before Camera.Render so cooldown cannot expire inside a blocked frame.");
            Assert.Contains("RecordMainLoopCooldownSkip", diagnostics, StringComparison.Ordinal);
            Assert.Contains("mainLoopSkip=", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Main Loop Cooldown Threshold Ms", editor, StringComparison.Ordinal);
            Assert.Contains("Main Loop Stable Frames", editor, StringComparison.Ordinal);
        }

        private static string Text(string relativePath)
            => File.ReadAllText(PathOf(relativePath));

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
        {
            get
            {
                var explicitRoot = Environment.GetEnvironmentVariable("UNITY2FOXGLOVE_REPO_ROOT");
                if (!string.IsNullOrWhiteSpace(explicitRoot))
                {
                    var candidate = Path.GetFullPath(explicitRoot);
                    if (File.Exists(Path.Combine(candidate, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(candidate, ".git")))
                        return candidate;

                    throw new DirectoryNotFoundException(
                        "UNITY2FOXGLOVE_REPO_ROOT does not point at a Unity2Foxglove repository root: "
                        + explicitRoot);
                }

                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException(
                    "Could not locate repository root from " + AppContext.BaseDirectory
                    + ". Set UNITY2FOXGLOVE_REPO_ROOT when running published test binaries outside the repository tree.");
            }
        }
    }
}
