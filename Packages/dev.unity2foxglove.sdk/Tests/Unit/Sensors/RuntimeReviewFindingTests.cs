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
    public sealed partial class RuntimeReviewFindingTests
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
        public void LidarProgressReservationPreservesBacklogWhileBatchIsInFlight()
        {
            const double progress = 3.75d;

            Assert.False(LidarScanTiming.TryReserveColumns(
                hasPendingScan: true,
                progress: progress,
                budgetColumns: 2,
                remainingColumns: 4,
                out var pendingColumns,
                out var pendingProgress));
            Assert.Equal(0, pendingColumns);
            Assert.Equal(progress, pendingProgress);

            Assert.True(LidarScanTiming.TryReserveColumns(
                hasPendingScan: false,
                progress: progress,
                budgetColumns: 2,
                remainingColumns: 4,
                out var reservedColumns,
                out var remainingProgress));
            Assert.Equal(2, reservedColumns);
            Assert.Equal(1.75d, remainingProgress, 10);
        }

        [Fact]
        public void MultiTickLidarProgressUsesElapsedPhysicsTicksAndBudget()
        {
            var progress = 0d;
            var emitted = 0;
            for (var tick = 0; tick < 4; tick++)
            {
                progress = LidarScanTiming.AdvanceColumnProgress(
                    progress,
                    elapsedPhysicsSeconds: 0.02d,
                    scanColumnCount: 100,
                    scanPeriodSeconds: 1.0d,
                    budgetColumns: 2);

                Assert.True(LidarScanTiming.TryReserveColumns(
                    hasPendingScan: false,
                    progress: progress,
                    budgetColumns: 2,
                    remainingColumns: 100 - emitted,
                    out var columns,
                    out progress));
                emitted += columns;
            }

            Assert.Equal(8, emitted);
            Assert.Equal(0d, progress, 10);
        }

        [Fact]
        public void LidarBoundaryTimestampAdvancesFromScanTimeline()
        {
            Assert.Equal(
                12.5d,
                LidarScanTiming.NextScanStartPhysSeconds(10.0d, 2.5d),
                10);
            Assert.Equal(
                10.0d,
                LidarScanTiming.NextScanStartPhysSeconds(10.0d, double.NaN),
                10);
            Assert.Equal(
                10.0d,
                LidarScanTiming.NextScanStartPhysSeconds(10.0d, double.PositiveInfinity),
                10);
        }

        [Fact]
        public void SharedSensorClockResetReanchorsAndAdvancesGeneration()
        {
            var clock = new FoxgloveSharedSensorClock();

            Assert.Equal(0, clock.Generation);
            Assert.Equal(1_000_000_000UL, clock.GetUnixTime(10.0d, 1_000_000_000UL));
            Assert.Equal(1_500_000_000UL, clock.GetUnixTime(10.5d, 9_000_000_000UL));

            clock.Reset();

            Assert.Equal(1, clock.Generation);
            Assert.Equal(2_000_000_000UL, clock.GetUnixTime(20.0d, 2_000_000_000UL));
            Assert.Equal(2_250_000_000UL, clock.GetUnixTime(20.25d, 9_000_000_000UL));
        }

        [Fact]
        public void SensorGenerationTransitionRetiresBeforeResetAndKeepsActiveGeneration()
        {
            var observedGeneration = 0;
            var queuedSamples = new List<string> { "old" };
            var events = new List<string>();

            Assert.True(SensorGenerationTransition.Apply(
                ref observedGeneration,
                currentGeneration: 1,
                retireQueuedWork: () =>
                {
                    events.Add("retire");
                    queuedSamples.Clear();
                },
                resetEpoch: () => events.Add(queuedSamples.Count == 0 ? "reset" : "reset-with-stale-data")));

            Assert.Empty(queuedSamples);
            Assert.Equal(new[] { "retire", "reset" }, events);
            Assert.Equal(1, observedGeneration);
            Assert.False(SensorGenerationTransition.Apply(
                ref observedGeneration,
                currentGeneration: 1,
                retireQueuedWork: () => events.Add("unexpected-retire"),
                resetEpoch: () => events.Add("unexpected-reset")));
            Assert.DoesNotContain("unexpected-retire", events);
            Assert.DoesNotContain("unexpected-reset", events);

            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var imu = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            Assert.Contains("SensorGenerationTransition.Apply", lidar, StringComparison.Ordinal);
            Assert.Contains("SensorGenerationTransition.Apply", imu, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraSubscriberFanoutCatchesPerSubscriberFailure()
        {
            var calls = new List<int>();
            var failures = new List<Exception>();
            Action<int> handlers = value => throw new InvalidOperationException("first");
            handlers += value => calls.Add(value);

            SubscriberFanout.Invoke(handlers, 7, failures.Add);

            Assert.Equal(new[] { 7 }, calls);
            Assert.Single(failures);

            foreach (var relativePath in new[]
                     {
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Raw.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs"
                     })
            {
                var source = Text(relativePath);
                Assert.Contains("SubscriberFanout.Invoke", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Rgb24OrientationUsesOneSharedImplementation()
        {
            var source = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
            var expected = new byte[] { 7, 8, 9, 10, 11, 12, 1, 2, 3, 4, 5, 6 };
            var destination = new byte[source.Length];

            Rgb24Orientation.CopyRows(source, destination, 2, 2, flipVertical: true);
            Assert.Equal(expected, destination);

            var inPlace = (byte[])source.Clone();
            byte[] scratch = null;
            Rgb24Orientation.FlipRowsInPlace(inPlace, 2, 2, ref scratch);
            Assert.Equal(expected, inPlace);

            var copyInPlace = (byte[])source.Clone();
            Rgb24Orientation.CopyRows(copyInPlace, copyInPlace, 2, 2, flipVertical: true);
            Assert.Equal(expected, copyInPlace);

            var managedJpeg = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/ManagedJpegEncoder.cs");
            var rawBuilder = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraRawImageFrameBuilder.cs");
            var captureResources = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");
            Assert.Contains("Rgb24Orientation", managedJpeg, StringComparison.Ordinal);
            Assert.Contains("Rgb24Orientation", rawBuilder, StringComparison.Ordinal);
            Assert.Contains("Rgb24Orientation", captureResources, StringComparison.Ordinal);
            Assert.DoesNotContain("FlipRgb24Rows", managedJpeg, StringComparison.Ordinal);
            Assert.DoesNotContain("FlipRgb24Rows", rawBuilder, StringComparison.Ordinal);
            Assert.DoesNotContain("FlipRgb24Rows", captureResources, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraCaptureIdentityKeepsReadbackMetadataTogether()
        {
            var identity = new CameraCaptureIdentity(4, 123UL, 0, -2);

            Assert.Equal(4, identity.Generation);
            Assert.Equal(123UL, identity.TimestampNs);
            Assert.Equal(1, identity.Width);
            Assert.Equal(1, identity.Height);

            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Assert.Contains("new CameraCaptureIdentity(", publisher, StringComparison.Ordinal);
            Assert.Contains("OnReadbackComplete(req, captureIdentity)", publisher, StringComparison.Ordinal);

            var request = new JpegEncodeRequest(
                new byte[] { 1, 2, 3 },
                identity,
                70,
                "camera",
                publishWebSocket: false,
                publishProvider: false,
                publishNativeFrame: false,
                PublisherEffectiveEncoding.Json,
                maxEncodedBytes: 0,
                jpegWorkerGeneration: 2);
            Assert.Equal(identity.TimestampNs, request.CaptureUnixNs);
            Assert.Equal(identity.Width, request.Width);
            Assert.Equal(identity.Height, request.Height);
            Assert.Equal(identity.Generation, request.Generation);

            var jpegPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");
            var videoPipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var rawPublisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Raw.cs");
            var jpegPublisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs");
            var videoPublisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Video.cs");
            Assert.DoesNotContain("new CameraCaptureIdentity(0", jpegPipeline, StringComparison.Ordinal);
            Assert.DoesNotContain("new CameraCaptureIdentity(0", videoPipeline, StringComparison.Ordinal);
            Assert.DoesNotContain("new CameraCaptureIdentity(0", rawPublisher, StringComparison.Ordinal);
            Assert.DoesNotContain("new CameraCaptureIdentity(0", jpegPublisher, StringComparison.Ordinal);
            Assert.DoesNotContain("new CameraCaptureIdentity(0", videoPublisher, StringComparison.Ordinal);
        }

    }
}
