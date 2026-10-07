// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-079 runtime review regression checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Numerics;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{


public sealed partial class RuntimeReviewFindingTests
    {
        [Fact]
        public void VideoDrainUsesAStablePerTickBudget()
        {
            var session = new CameraVideoSidecarSession();
            var sidecar = new FakeVideoSidecar(5);
            typeof(CameraVideoSidecarSession)
                .GetField("_sidecar", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, sidecar);
            typeof(CameraVideoSidecarSession)
                .GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, CameraOutputMode.H264Ffmpeg);

            var published = new List<ulong>();
            Assert.True(session.TryDrain(
                () => 900UL,
                (_, timestamp, _) => published.Add(timestamp),
                null));
            Assert.Equal(4, published.Count);
            Assert.Equal(1, sidecar.Remaining);

            Assert.True(session.TryDrain(
                () => 900UL,
                (_, timestamp, _) => published.Add(timestamp),
                null));
            Assert.Equal(5, published.Count);
            Assert.Equal(0, sidecar.Remaining);
        }

        [Fact]
        public void VideoDrainCountsTimestampedUnitsWithoutTimestampsAgainstBudget()
        {
            var session = new CameraVideoSidecarSession();
            var sidecar = new FakeTimestampedVideoSidecar(new[]
            {
                new EncodedVideoAccessUnit(new byte[] { 1 }, 0UL),
                new EncodedVideoAccessUnit(new byte[] { 2 }, 0UL),
                new EncodedVideoAccessUnit(new byte[] { 3 }, 0UL),
                new EncodedVideoAccessUnit(new byte[] { 4 }, 400UL),
                new EncodedVideoAccessUnit(new byte[] { 5 }, 500UL)
            });
            typeof(CameraVideoSidecarSession)
                .GetField("_sidecar", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, sidecar);
            typeof(CameraVideoSidecarSession)
                .GetField("_mode", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(session, CameraOutputMode.H264Ffmpeg);

            var published = new List<ulong>();
            Assert.True(session.TryDrain(
                () => 900UL,
                (_, timestamp, _) => published.Add(timestamp),
                null,
                maxAccessUnits: 4));
            Assert.Equal(new[] { 400UL }, published);
            Assert.Equal(1, sidecar.Remaining);

            Assert.True(session.TryDrain(
                () => 900UL,
                (_, timestamp, _) => published.Add(timestamp),
                null,
                maxAccessUnits: 4));
            Assert.Equal(new[] { 400UL, 500UL }, published);
            Assert.Equal(0, sidecar.Remaining);
        }

        [Fact]
        public void CameraCaptureResizeRetiresOldRenderTextureUntilDrain()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var resize = source.IndexOf("RetireRenderTexture();", StringComparison.Ordinal);
            var release = source.IndexOf("ReleaseRetiredRenderTextures", StringComparison.Ordinal);

            Assert.True(resize >= 0);
            Assert.True(release > resize);
            Assert.Contains("private readonly List<RetiredRenderTexture> _retiredRenderTextures", source, StringComparison.Ordinal);
            Assert.Contains("CameraReadbackGenerationTracker", source, StringComparison.Ordinal);
            Assert.Contains("_captureResources.RegisterReadback(captureIdentity.Generation);", publisher, StringComparison.Ordinal);
            Assert.Contains("CompletePendingReadback(identity.Generation);", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void SensorGenerationResetDropsStaleWorkBeforeStartingNewEpoch()
        {
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var imu = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            Assert.Contains("SensorGenerationTransition.Apply", lidar, StringComparison.Ordinal);
            Assert.Contains("ScanScheduler.DrainPendingScan", lidar, StringComparison.Ordinal);
            Assert.Contains("_scanClock.Reset", lidar, StringComparison.Ordinal);
            Assert.Contains("SensorGenerationTransition.Apply", imu, StringComparison.Ordinal);
            Assert.Contains("RetireQueuedSamplesForLifecycleTransition", imu, StringComparison.Ordinal);
            Assert.Contains("_hasEpoch = false", imu, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraCaptureReResolvesAReplacementAfterUnitySourceDestruction()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");

            Assert.Contains("if (_sourceCamera == null)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("&& !_sourceCameraResolved", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RetiredReadbackTrackerReleasesEachGenerationAfterItsOwnDrain()
        {
            var tracker = new CameraReadbackGenerationTracker();
            tracker.Register(1);
            tracker.Register(1);
            tracker.Register(2);

            Assert.True(tracker.HasPending(1));
            Assert.False(tracker.Complete(1));
            Assert.True(tracker.HasPending(1));
            Assert.True(tracker.Complete(1));
            Assert.False(tracker.HasPending(1));
            Assert.True(tracker.HasPending(2));
            Assert.True(tracker.Complete(2));
            Assert.False(tracker.HasPending(2));
        }

        [Fact]
        public void CameraReadbackResizeKeepsOldGenerationUntilItsCallbackDrains()
        {
            var tracker = new CameraReadbackGenerationTracker();
            tracker.Register(10);
            Assert.False(tracker.IsDrained(10));

            var currentGeneration = 11;
            tracker.Register(currentGeneration);
            Assert.False(CameraCaptureGenerationPolicy.Accepts(
                destroyed: false,
                active: true,
                callbackGeneration: 10,
                currentGeneration: currentGeneration));
            Assert.False(tracker.IsDrained(10));

            Assert.True(tracker.Complete(10));
            Assert.True(tracker.IsDrained(10));
            Assert.True(tracker.HasPending(currentGeneration));
            Assert.True(CameraCaptureGenerationPolicy.Accepts(
                destroyed: false,
                active: true,
                callbackGeneration: currentGeneration,
                currentGeneration: currentGeneration));

            var resources = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Assert.Contains("_readbackGenerations.IsDrained", resources, StringComparison.Ordinal);
            Assert.Contains("CameraCaptureGenerationPolicy.Accepts", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraInfoUsesImageSourceAndRejectsOrthographicProjection()
        {
            var info = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs");
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Assert.Contains("imagePublisher?.SensorCameraSourceCamera", info, StringComparison.Ordinal);
            Assert.Contains("WarnSourceCameraMismatch", info, StringComparison.Ordinal);
            Assert.Contains("WarnOrthographicCamera", info, StringComparison.Ordinal);
            Assert.Contains("var imageSourceCamera = imagePublisher?.SensorCameraSourceCamera;", info, StringComparison.Ordinal);
            Assert.Contains("var fallbackCamera = imageSourceCamera == null && _autoFromCamera", info, StringComparison.Ordinal);
            Assert.Contains("var cam = _autoFromCamera ? ResolveSourceCamera() : null;", info, StringComparison.Ordinal);
            Assert.Contains("CameraInfo is not published for orthographic cameras", info, StringComparison.Ordinal);
            Assert.DoesNotContain("cam.orthographicSize", info, StringComparison.Ordinal);
            Assert.Contains("public Camera SensorCameraSourceCamera", publisher, StringComparison.Ordinal);
            Assert.Contains("CameraInfoProjectionPolicy.ShouldSuppressOrthographic", info, StringComparison.Ordinal);
            Assert.DoesNotContain("var sourceCamera = ResolveSourceCamera();", info, StringComparison.Ordinal);
            Assert.Contains("capture camera is authoritative", editor, StringComparison.Ordinal);
            Assert.Contains("Orthographic sources are never published as pinhole CameraInfo", editor, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true, true, false, false, true)]
        [InlineData(true, true, true, false, true)]
        [InlineData(true, false, false, true, false)]
        [InlineData(false, false, true, true, true)]
        [InlineData(false, false, true, false, false)]
        [InlineData(false, false, false, true, false)]
        public void CameraInfoProjectionPolicyUsesPublishedImageAuthority(
            bool hasImageSourceCamera,
            bool imageSourceIsOrthographic,
            bool autoFromCamera,
            bool fallbackSourceIsOrthographic,
            bool expected)
        {
            Assert.Equal(
                expected,
                CameraInfoProjectionPolicy.ShouldSuppressOrthographic(
                    hasImageSourceCamera,
                    imageSourceIsOrthographic,
                    autoFromCamera,
                    fallbackSourceIsOrthographic));
        }

        [Fact]
        public void CameraInfoAuthoritySelectsOneImageCameraSnapshotForAllCalibrationFields()
        {
            var imageCamera = new CameraAuthorityProbe(1920, 1080, 900, 901, 960, 540, false);
            var configuredCamera = new CameraAuthorityProbe(640, 480, 300, 301, 320, 240, false);

            var selected = CameraInfoAuthorityPolicy.Select(true, imageCamera, configuredCamera);

            Assert.Equal(imageCamera.Width, selected.Width);
            Assert.Equal(imageCamera.Height, selected.Height);
            Assert.Equal(imageCamera.Fx, selected.Fx);
            Assert.Equal(imageCamera.Fy, selected.Fy);
            Assert.Equal(imageCamera.Cx, selected.Cx);
            Assert.Equal(imageCamera.Cy, selected.Cy);

            var info = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs");
            Assert.Contains("CameraInfoAuthorityPolicy.Select", info, StringComparison.Ordinal);
        }

        [Fact]
        public void LidarTransformAuthorityUsesSensorPoseInsteadOfPublisherPose()
        {
            var selected = SensorTransformAuthorityPolicy.Select(
                sensorTransform: 1,
                publisherTransform: 100,
                isValid: value => value > 0);

            Assert.Equal(1, selected);
            Assert.Equal(
                100,
                SensorTransformAuthorityPolicy.Select(0, 100, value => value > 0));

            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            Assert.Contains("SensorTransformAuthorityPolicy.Select", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualLidarPlayModeWarningIsLimitedToOnePerEnable()
        {
            var gate = new PlayModeConfigurationWarningGate();
            Assert.True(gate.TryIssue(true));
            Assert.False(gate.TryIssue(true));
            gate.Reset();
            Assert.True(gate.TryIssue(true));

            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            Assert.Contains("_playModeConfigurationWarningGate.Reset();", lidar, StringComparison.Ordinal);
            Assert.Contains("TryIssue(Application.isPlaying && isActiveAndEnabled)", lidar, StringComparison.Ordinal);
            var rebuild = lidar.IndexOf("RebuildScanConfiguration();", StringComparison.Ordinal);
            var warning = lidar.IndexOf("WarnIfOwnLayerIncludedInRaycastMask();", rebuild, StringComparison.Ordinal);
            Assert.True(rebuild >= 0);
            Assert.True(warning > rebuild);
        }

        [Fact]
        public void CameraHealthGateRunsBeforeRender()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var gate = publisher.IndexOf("AllowCameraCaptureByHealthPolicy(profile)", StringComparison.Ordinal);
            var render = publisher.IndexOf("_captureResources.CaptureCamera.Render();", StringComparison.Ordinal);

            Assert.True(gate >= 0);
            Assert.True(render > gate);
        }

        [Fact]
        public void VirtualLidarRuntimeConfigurationRemainsStableUntilTheNextEnableCycle()
        {
            var active = new LidarRuntimeConfiguration(
                "scan-a",
                50f,
                7,
                publishEmptyFrames: false,
                logPerformanceDiagnostics: false,
                maxRaycastCommandsPerFixedUpdate: 6144,
                syntheticReflectivity: 0.25f,
                syntheticIntensity: 0.75f);

            var editedFrameId = "scan-b";
            var editedMaxRange = 10f;

            Assert.Equal("scan-a", active.FrameId);
            Assert.Equal(50f, active.MaxRangeMeters);
            Assert.Equal(7, active.LayerMaskValue);
            Assert.Equal(6144, active.MaxRaycastCommandsPerFixedUpdate);

            var refreshed = new LidarRuntimeConfiguration(
                editedFrameId,
                editedMaxRange,
                3,
                publishEmptyFrames: true,
                logPerformanceDiagnostics: true,
                maxRaycastCommandsPerFixedUpdate: 1024,
                syntheticReflectivity: 0.5f,
                syntheticIntensity: 0.5f);

            Assert.Equal("scan-b", refreshed.FrameId);
            Assert.Equal(10f, refreshed.MaxRangeMeters);
            Assert.True(refreshed.PublishEmptyFrames);
            Assert.True(refreshed.LogPerformanceDiagnostics);
            Assert.Equal(1024, refreshed.MaxRaycastCommandsPerFixedUpdate);
        }

        [Fact]
        public void VirtualLidarUsesTheEnableCycleConfigurationSnapshot()
        {
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");

            Assert.Contains("_configurationLifecycle.Activate(new LidarRuntimeConfiguration(", lidar, StringComparison.Ordinal);
            Assert.Contains("_configurationLifecycle.Configuration.MaxRangeMeters", lidar, StringComparison.Ordinal);
            Assert.Contains("_configurationLifecycle.Configuration.LayerMaskValue", lidar, StringComparison.Ordinal);
            Assert.Contains("_configurationLifecycle.Configuration.PublishEmptyFrames", lidar, StringComparison.Ordinal);
            Assert.DoesNotContain("_maxRangeMeters,\n                    _syntheticIntensity", lidar, StringComparison.Ordinal);
        }

        [Fact]
        public void LidarConfigurationLifecycleRequiresReenableToApplyEditedValues()
        {
            var lifecycle = new LidarRuntimeConfigurationLifecycle();
            var initial = new LidarRuntimeConfiguration(
                "initial", 50f, 1, false, false, 256, 1f, 1f);
            var edited = new LidarRuntimeConfiguration(
                "edited", 10f, 2, true, true, 512, 0.5f, 0.5f);

            lifecycle.Activate(initial);
            Assert.True(lifecycle.IsActive);
            Assert.Equal("initial", lifecycle.Configuration.FrameId);
            lifecycle.Deactivate();
            Assert.False(lifecycle.IsActive);
            lifecycle.Activate(edited);
            Assert.Equal("edited", lifecycle.Configuration.FrameId);

            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            Assert.Contains("_configurationLifecycle.Activate(new LidarRuntimeConfiguration(", lidar, StringComparison.Ordinal);
            Assert.Contains("_configurationLifecycle.Configuration", lidar, StringComparison.Ordinal);
            Assert.DoesNotContain("_activeConfiguration", lidar, StringComparison.Ordinal);
            Assert.Contains("_configurationLifecycle.Deactivate()", lidar, StringComparison.Ordinal);
        }

    }
}
