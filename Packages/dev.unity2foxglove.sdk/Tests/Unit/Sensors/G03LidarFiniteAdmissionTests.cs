// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Round4 G03 finite scalar admission regressions.

using System;
using System.IO;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "187-R2-G03")]
    [Trait("Domain", "Lidar")]
    public sealed class G03LidarFiniteAdmissionTests
    {
        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void UniformProducerRejectsInvalidScanRate(double scanRateHz)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LidarProfileLoader.CreateUniform(
                    "Custom", 2, 16, scanRateHz, 10.0, -10.0, 0.5));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1.0)]
        public void UniformProducerRejectsInvalidMinimumRange(double minRangeMeters)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LidarProfileLoader.CreateUniform(
                    "Custom", 2, 16, 10.0, 10.0, -10.0, minRangeMeters));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void UniformProducerRejectsNonFiniteFov(double invalidFov)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LidarProfileLoader.CreateUniform(
                    "Custom", 2, 16, 10.0, invalidFov, -10.0, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LidarProfileLoader.CreateUniform(
                    "Custom", 2, 16, 10.0, 10.0, invalidFov, 0.5));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void ModelSpecRejectsNonFiniteRateFovAndRanges(double invalidValue)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSpinningSpec(
                rateHz: invalidValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSpinningSpec(
                fovTopDeg: invalidValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSpinningSpec(
                fovBottomDeg: invalidValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSpinningSpec(
                minRangeMeters: invalidValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSpinningSpec(
                maxRangeMeters: invalidValue));

            Assert.Throws<ArgumentOutOfRangeException>(() => new LidarModelSpec(
                LidarVendor.Livox, "invalid", LidarScanKind.NonRepetitive,
                rings: 0, columns: 0, rateHz: 10.0,
                fovTopDeg: 0.0, fovBottomDeg: 0.0,
                beamAltitudeAnglesDeg: null, modes: null,
                fovHDeg: invalidValue, fovVDeg: 20.0,
                beamsPerFrame: 100, minRangeMeters: 0.1, maxRangeMeters: 100.0));
        }

        [Fact]
        public void ModelSpecRejectsMaximumRangeBelowMinimum()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NewSpinningSpec(
                minRangeMeters: 10.0, maxRangeMeters: 1.0));
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(-1.0f)]
        public void VirtualLidarInspectorNormalizerRejectsNonFiniteRangeState(float invalidValue)
        {
            Assert.Equal(0f, LidarGeometryLimits.NormalizeFiniteNonNegative(invalidValue));
        }

        [Fact]
        public void VirtualLidarUsesTheSharedFiniteNormalizerBeforeScheduling()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Packages")))
                root = root.Parent;
            Assert.NotNull(root);
            var source = File.ReadAllText(Path.Combine(root.FullName,
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs"));

            Assert.Contains("NormalizeSerializedNumericConfiguration();", source, StringComparison.Ordinal);
            Assert.Contains("LidarGeometryLimits.NormalizeFiniteNonNegative(_maxRangeMeters)", source, StringComparison.Ordinal);
            Assert.Contains("LidarGeometryLimits.NormalizeFiniteNonNegative(_scanRateHzOverride)", source, StringComparison.Ordinal);
        }

        private static LidarModelSpec NewSpinningSpec(
            double rateHz = 10.0,
            double fovTopDeg = 10.0,
            double fovBottomDeg = -10.0,
            double minRangeMeters = 0.5,
            double maxRangeMeters = 120.0)
            => new LidarModelSpec(
                LidarVendor.Ouster, "test", LidarScanKind.Spinning,
                rings: 2, columns: 16, rateHz: rateHz,
                fovTopDeg: fovTopDeg, fovBottomDeg: fovBottomDeg,
                beamAltitudeAnglesDeg: null, modes: null,
                fovHDeg: 0.0, fovVDeg: 0.0,
                beamsPerFrame: 0, minRangeMeters: minRangeMeters,
                maxRangeMeters: maxRangeMeters);
    }
}
