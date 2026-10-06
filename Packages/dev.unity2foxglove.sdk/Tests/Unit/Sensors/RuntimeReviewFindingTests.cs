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
    [Trait("Phase", "173-079")]
    [Trait("Domain", "Runtime")]
    public sealed class RuntimeReviewFindingTests
    {
        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(-180.0)]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.PositiveInfinity)]
        public void AutoIntrinsicsRejectsInvalidVerticalFov(double verticalFovDegrees)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CameraCalibrationMessageBuilder.CreateAutoIntrinsics(1UL, "camera", 1920, 1080, verticalFovDegrees));
        }

        [Fact]
        public void AutoIntrinsicsKeepsPlausibleFocalLengthForValidFov()
        {
            var calibration = CameraCalibrationMessageBuilder.CreateAutoIntrinsics(1UL, "camera", 640, 480, 60.0);

            Assert.InRange(calibration.K[4], 480.0 / 4.0, 480.0 * 4.0);
        }

        [Fact]
        public void PoseHistoryIgnoresOutOfOrderSamplesWithoutDroppingLatestCoverage()
        {
            var history = new SensorMotionPoseHistory(capacity: 4, maxAgeNs: 10_000_000_000UL);
            history.Add(100UL, new Vector3(0f, 0f, 0f), Quaternion.Identity);
            history.Add(200UL, new Vector3(2f, 0f, 0f), Quaternion.Identity);
            history.Add(150UL, new Vector3(9f, 0f, 0f), Quaternion.Identity);

            var snapshot = history.Snapshot();

            Assert.Equal(2, history.Count);
            Assert.Equal(200UL, snapshot[1].UnixNs);
            Assert.True(history.Covers(100UL, 200UL));
        }

        [Fact]
        public void PoseHistoryReplacesEqualTimestampWithoutGrowingBuffer()
        {
            var history = new SensorMotionPoseHistory(capacity: 4, maxAgeNs: 10_000_000_000UL);
            history.Add(100UL, new Vector3(0f, 0f, 0f), Quaternion.Identity);
            history.Add(200UL, new Vector3(2f, 0f, 0f), Quaternion.Identity);
            history.Add(200UL, new Vector3(7f, 0f, 0f), Quaternion.Identity);
            history.Add(300UL, new Vector3(3f, 0f, 0f), Quaternion.Identity);

            var snapshot = history.Snapshot();

            Assert.Equal(3, history.Count);
            Assert.Equal(200UL, snapshot[1].UnixNs);
            Assert.Equal(7f, snapshot[1].Translation.X);
            Assert.True(history.Covers(100UL, 300UL));
        }

        [Fact]
        public void PoseHistoryInterpolatesInteriorTimestampWhenSearchIndexStartsAtLastSample()
        {
            var samples = new[]
            {
                new SensorMotionPoseSample(100UL, new Vector3(0f, 0f, 0f), Quaternion.Identity),
                new SensorMotionPoseSample(200UL, new Vector3(2f, 0f, 0f), Quaternion.Identity),
                new SensorMotionPoseSample(300UL, new Vector3(4f, 0f, 0f), Quaternion.Identity)
            };
            var searchIndex = samples.Length - 1;

            Assert.True(SensorMotionPoseHistoryMath.TryInterpolateMonotonic(samples, 250UL, ref searchIndex, out var pose));
            Assert.InRange(pose.Translation.X, 2.99f, 3.01f);
            Assert.Equal(1, searchIndex);
        }

        [Fact]
        public void SpinningLidarRejectsNonPositiveScanDimensionsAndRate()
        {
            Assert.Throws<ArgumentException>(() => LidarModelSpec.Velodyne("VLP-16", 16, 1800, 0.0, null));
            Assert.Throws<ArgumentException>(() => new LidarModelSpec(
                LidarVendor.Ouster,
                "bad",
                LidarScanKind.Spinning,
                rings: 0,
                columns: 1024,
                rateHz: 10.0,
                fovTopDeg: 10.0,
                fovBottomDeg: -10.0,
                beamAltitudeAnglesDeg: null,
                modes: null,
                fovHDeg: 0.0,
                fovVDeg: 0.0,
                beamsPerFrame: 0,
                minRangeMeters: 0.5,
                maxRangeMeters: 120.0));
        }

        [Fact]
        public void LidarModelSpecClonesAndReadOnlyWrapsArrayInputs()
        {
            var altitudes = new[] { 10.0, -10.0 };
            var modes = new[] { "1024x10", "2048x10" };
            var spec = LidarModelSpec.Ouster(
                "immutable", 2, 1024, modes, 10.0, -10.0, altitudes);

            altitudes[0] = 99.0;
            modes[0] = "corrupted";

            Assert.Equal(10.0, spec.BeamAltitudeAnglesDeg[0]);
            Assert.Equal("1024x10", spec.Modes[0]);
            Assert.False(spec.BeamAltitudeAnglesDeg is double[]);
            Assert.False(spec.Modes is string[]);

            var exposedAltitudes = Assert.IsAssignableFrom<IList<double>>(spec.BeamAltitudeAnglesDeg);
            var exposedModes = Assert.IsAssignableFrom<IList<string>>(spec.Modes);
            Assert.True(exposedAltitudes.IsReadOnly);
            Assert.True(exposedModes.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => exposedAltitudes[0] = 42.0);
            Assert.Throws<NotSupportedException>(() => exposedModes[0] = "corrupted");
        }

        [Fact]
        public void MultiTickLidarAcquisitionUsesElapsedPhysicsTime()
        {
            var offset = LidarScanTiming.AcquisitionOffsetSeconds(
                acquisitionPhysSeconds: 0.120,
                scanStartPhysSeconds: 0.040,
                normalizedOffset: 0.5f,
                fixedDeltaTimeSeconds: 0.020f);

            Assert.Equal(0.09f, offset, 6);
        }

        [Fact]
        public void CameraSubscriberFanoutCatchesPerSubscriberFailure()
        {
            foreach (var relativePath in new[]
                     {
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Raw.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs"
                     })
            {
                var source = Text(relativePath);
                Assert.Contains("handlers.GetInvocationList()", source, StringComparison.Ordinal);
                Assert.Contains("catch (Exception ex)", source, StringComparison.Ordinal);
            }
        }

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
            Assert.Contains("_captureResources.RegisterReadback(generation);", publisher, StringComparison.Ordinal);
            Assert.Contains("_captureResources.CompleteReadback(generation);", publisher, StringComparison.Ordinal);
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
        public void CameraInfoUsesImageSourceAndRejectsOrthographicProjection()
        {
            var info = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs");
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Assert.Contains("imagePublisher?.SensorCameraSourceCamera", info, StringComparison.Ordinal);
            Assert.Contains("WarnSourceCameraMismatch", info, StringComparison.Ordinal);
            Assert.Contains("WarnOrthographicCamera", info, StringComparison.Ordinal);
            Assert.Contains("var sourceCamera = ResolveSourceCamera();", info, StringComparison.Ordinal);
            Assert.Contains("var cam = _autoFromCamera ? ResolveSourceCamera() : null;", info, StringComparison.Ordinal);
            Assert.Contains("CameraInfo is not published for orthographic cameras", info, StringComparison.Ordinal);
            Assert.DoesNotContain("cam.orthographicSize", info, StringComparison.Ordinal);
            Assert.Contains("public Camera SensorCameraSourceCamera", publisher, StringComparison.Ordinal);
            Assert.Contains("CameraInfoProjectionPolicy.ShouldSuppressOrthographic", info, StringComparison.Ordinal);
            Assert.Contains("capture camera is authoritative", editor, StringComparison.Ordinal);
            Assert.Contains("Orthographic sources are never published as pinhole CameraInfo", editor, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        public void CameraInfoProjectionPolicyRejectsEveryOrthographicSource(
            bool hasSourceCamera,
            bool isOrthographic,
            bool expected)
        {
            Assert.Equal(
                expected,
                CameraInfoProjectionPolicy.ShouldSuppressOrthographic(
                    hasSourceCamera,
                    isOrthographic));
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
        }

        [Fact]
        public void CameraSourceFallbackUsesUnityNullSemantics()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Assert.Contains("var sourceCamera = _captureResources.SourceCamera;", publisher, StringComparison.Ordinal);
            Assert.Contains("return sourceCamera != null ? sourceCamera : GetComponent<Camera>();", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("_captureResources.SourceCamera ?? GetComponent<Camera>()", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void SensorGenerationAndConfigurationContractsRemainBoundToRuntimeState()
        {
            var managerClock = Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveSharedSensorClock.cs");
            var imu = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");

            Assert.Contains("public int Generation", managerClock, StringComparison.Ordinal);
            Assert.Contains("SharedSensorClockGeneration", imu, StringComparison.Ordinal);
            Assert.Contains("SharedSensorClockGeneration", lidar, StringComparison.Ordinal);
            Assert.Contains("NormalizeSerializedNumericConfiguration();", lidar, StringComparison.Ordinal);
            Assert.Contains("RebuildScanConfiguration();", lidar, StringComparison.Ordinal);
            Assert.Contains("Configuration changes during Play are deferred until the component is disabled and re-enabled.", lidar, StringComparison.Ordinal);
        }

        [Fact]
        public void LidarDeskewBindsMotionCompensationToSensorTransform()
        {
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            Assert.Contains("SetMotionCompensationTransform(transform);", lidar, StringComparison.Ordinal);
        }

        [Fact]
        public void VideoInputAdmissionAndJpegOrientationContractsRemainExplicit()
        {
            var policy = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraPipelineHealthPolicy.cs");
            var pipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var asyncJpeg = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegWorkerPayloads.cs");
            var syncJpeg = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");

            Assert.Contains("VideoInputQueueFull", policy, StringComparison.Ordinal);
            Assert.Contains("InputQueueDepth", pipeline, StringComparison.Ordinal);
            Assert.Contains("flipVertical: true", asyncJpeg, StringComparison.Ordinal);
            Assert.Contains("FlipRgb24RowsInPlace", syncJpeg, StringComparison.Ordinal);
        }

        private sealed class FakeVideoSidecar : ICameraVideoEncoderSidecar
        {
            private readonly Queue<byte[]> _units = new Queue<byte[]>();

            internal FakeVideoSidecar(int count)
            {
                for (var i = 0; i < count; i++)
                    _units.Enqueue(new byte[] { (byte)i });
            }

            public int Remaining => _units.Count;
            public bool IsRunning => true;
            public int OutputQueueDepth => _units.Count;
            public int MaxOutputQueue => 8;
            public int InputQueueDepth => 0;
            public int MaxInputQueue => 8;
            public string LastDiagnosticLine => "";
            public string LastError => "";
            public bool TrySubmitFrame(byte[] frame) => true;
            public bool TryDequeueAccessUnit(out byte[] accessUnit)
            {
                if (_units.Count == 0)
                {
                    accessUnit = null;
                    return false;
                }

                accessUnit = _units.Dequeue();
                return true;
            }
            public void Dispose() { }
        }

        private sealed class FakeTimestampedVideoSidecar : ITimestampedCameraVideoEncoderSidecar
        {
            private readonly Queue<EncodedVideoAccessUnit> _units;

            internal FakeTimestampedVideoSidecar(IEnumerable<EncodedVideoAccessUnit> units)
            {
                _units = new Queue<EncodedVideoAccessUnit>(units);
            }

            public int Remaining => _units.Count;
            public bool IsRunning => true;
            public int OutputQueueDepth => _units.Count;
            public int MaxOutputQueue => 8;
            public int InputQueueDepth => 0;
            public int MaxInputQueue => 8;
            public string LastDiagnosticLine => "";
            public string LastError => "";
            public bool TrySubmitFrame(byte[] frame) => true;
            public bool TrySubmitFrame(byte[] frame, ulong timestampNs) => true;
            public bool TryDequeueAccessUnit(out byte[] accessUnit)
            {
                if (!TryDequeueEncodedAccessUnit(out var timestamped))
                {
                    accessUnit = null;
                    return false;
                }

                accessUnit = timestamped.Data;
                return true;
            }
            public bool TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit)
            {
                if (_units.Count == 0)
                {
                    accessUnit = default;
                    return false;
                }

                accessUnit = _units.Dequeue();
                return true;
            }
            public void Dispose() { }
        }

        private static string Text(string relativePath)
        {
            var path = PathOf(relativePath);
            Assert.True(File.Exists(path), "Required source file was not found: " + relativePath);
            return File.ReadAllText(path);
        }

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                        && Directory.Exists(Path.Combine(dir.FullName, "Unity2Foxglove"))
                        && Directory.Exists(Path.Combine(dir.FullName, "Packages")))
                        return dir.FullName;
                    dir = dir.Parent;
                }

                Assert.Fail("Could not locate repository root for sensor review tests.");
                return string.Empty;
            }
        }
    }
}
