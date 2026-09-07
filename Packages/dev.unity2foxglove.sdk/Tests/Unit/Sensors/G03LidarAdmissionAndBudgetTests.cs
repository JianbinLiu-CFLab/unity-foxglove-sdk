// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Round4 G03 LiDAR admission, geometry, and fixed-step budget regressions.

using System;
using System.IO;
using System.Numerics;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "187-R2-G03")]
    [Trait("Domain", "Lidar")]
    public sealed class G03LidarAdmissionAndBudgetTests
    {
        [Fact]
        public void BuiltInModeRejectsColumnCountOutsideSharedGeometryBound()
        {
            Assert.False(LidarProfileLoader.TryParseMode("2147483647x10", out _, out _));
        }

        [Fact]
        public void UniformProducerRejectsUnrepresentableRayProductBeforeAllocation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LidarProfileLoader.CreateUniform("Custom", 65536, 65536, 10.0, 1.0, -1.0, 0.5));
        }

        [Fact]
        public void PublicConsumersRejectInvalidProfilesAndPatternInputs()
        {
            var profile = new LidarProfile
            {
                ProductLine = "Custom",
                LidarMode = "16x10",
                PixelsPerColumn = 2,
                ColumnsPerFrame = 16,
                ScanRateHz = 10.0,
                MinRangeMeters = 0.5,
                BeamAltitudeAngles = new[] { double.NaN, 0.0 },
                BeamAzimuthAngles = new[] { 0.0, 0.0 }
            };

            Assert.False(profile.Validate(out _));
            Assert.Throws<ArgumentException>(() => new LidarRayGenerator(profile));
            Assert.Throws<ArgumentException>(() => new SpinningScanPattern(
                "bad", 10.0, 0.5, 16, 1, new[] { 0.0 }, new[] { 0.0, 0.0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpinningScanPattern(
                "bad", double.PositiveInfinity, 0.5, 16, 1, new[] { 0.0 }, new[] { 0.0 }));
        }

        [Fact]
        public void SpinningPatternRetainsPartialColumnWithoutIntegerOverflow()
        {
            var pattern = SpinningScanPattern.FromUniformFov(
                "test", 10.0, 0.1, rings: 1, columns: 1_048_576, columnStep: 1_048_575,
                fovTopDeg: 0.0, fovBottomDeg: 0.0);

            Assert.Equal(2, pattern.RayCount);
            Assert.True(pattern.TryGetRay(1, 0, out _, out var timeOffset));
            Assert.Equal(1_048_575d / 1_048_576d, timeOffset, 6);
        }

        [Fact]
        public void PublicRayGeneratorKeepsLargeColumnStepArithmeticBounded()
        {
            var profile = LidarProfileLoader.CreateUniform("Custom", 1, 1_048_576, 10.0, 0.0, 0.0, 0.5);
            var generator = new LidarRayGenerator(profile, int.MaxValue);

            Assert.Equal(1, generator.RayCount);
        }

        [Fact]
        public void RingIdentityIsBoundToThePackedPointCloudDomain()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LidarProfileLoader.CreateUniform("Custom", 4097, 1024, 10.0, 1.0, -1.0, 0.5));
        }

        [Fact]
        public void SchedulerSourceKeepsEveryRaycastBatchInsideTheFixedStepBudget()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Packages")))
                root = root.Parent;
            Assert.NotNull(root);
            var source = File.ReadAllText(Path.Combine(root.FullName,
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs"));

            Assert.Contains("var commandBudget = Math.Max(1, maxRaycastCommandsPerFixedUpdate);", source);
            Assert.Contains("batchCount < commandBudget", source);
            Assert.Contains("ref int scanColumnRayCursor", source);
        }
    }
}
