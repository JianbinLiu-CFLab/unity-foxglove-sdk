// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Pilot xUnit migration of Phase 140-17 behavioral checks (Rosette y-up
//          elevation sign and Ouster metadata min-range parsing).

using System;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// Behavioral coverage for the LiDAR scan pattern and metadata profile loader.
    /// Ported from Phase140_17Validation (checks 140-17D / 140-17E).
    /// </summary>
    [Trait("Phase", "140-17")]
    [Trait("Domain", "Lidar")]
    public class LidarProfileAndPatternTests
    {
        private const double RosetteAzimuthFrequencyRatio = 3.2;
        private const double RosettePositiveAzimuthProbeFrequency = 7.0;
        private const double RosettePositiveElevationProbeFrequency = 11.0;

        /// <summary>140-17D: Rosette positive elevation points upward in the y-up sensor frame.</summary>
        [Fact]
        public void Rosette_PositiveElevation_PointsUpInYUpSensorFrame()
        {
            const int beams = 1000;
            var pattern = new RosetteScanPattern("test", 10.0, 0.1, 30.0, 20.0, beams);

            var foundPositiveElevation = false;
            for (var i = 0; i < beams; i++)
            {
                // Mirrors RosetteScanPattern's internal frequency ratios so the test probes a
                // high-elevation beam deterministically instead of relying on a random sample.
                var tau = (double)i / beams * 2.0 * Math.PI * RosetteAzimuthFrequencyRatio;
                if (Math.Sin(RosettePositiveElevationProbeFrequency * tau) <= 0.9)
                    continue;

                foundPositiveElevation = true;
                Assert.True(pattern.TryGetRay(i, 0, out var direction, out _));
                Assert.True(direction.Y > 0f, "positive elevation should point up in the y-up sensor frame");
                break;
            }

            Assert.True(foundPositiveElevation, "test should find a positive-elevation sample");
        }

        [Fact]
        public void SpinningPattern_RetainsFinalPartialColumnStep()
        {
            var pattern = SpinningScanPattern.FromUniformFov(
                "test",
                10.0,
                0.1,
                rings: 1,
                columns: 1024,
                columnStep: 3,
                fovTopDeg: 0.0,
                fovBottomDeg: 0.0);

            Assert.Equal(342, pattern.RayCount);
            Assert.True(pattern.TryGetRay(pattern.RayCount - 1, 0, out _, out var timeOffset));
            Assert.Equal(1023d / 1024d, timeOffset, 6);
        }

        [Fact]
        public void Rosette_PositiveAzimuth_PointsRightLikeSpinningPattern()
        {
            const int beams = 1000;
            var pattern = new RosetteScanPattern("test", 10.0, 0.1, 30.0, 20.0, beams);

            var foundPositiveAzimuth = false;
            for (var i = 0; i < beams; i++)
            {
                // Mirrors RosetteScanPattern's azimuth probe frequency so this test fails with
                // a meaningful direction assertion if the pattern ratios are intentionally changed.
                var tau = (double)i / beams * 2.0 * Math.PI * RosetteAzimuthFrequencyRatio;
                if (Math.Sin(RosettePositiveAzimuthProbeFrequency * tau) <= 0.9)
                    continue;

                foundPositiveAzimuth = true;
                Assert.True(pattern.TryGetRay(i, 0, out var direction, out _));
                Assert.True(direction.X > 0f, "positive azimuth should point right in the x-right sensor frame");
                break;
            }

            Assert.True(foundPositiveAzimuth, "test should find a positive-azimuth sample");
        }

        /// <summary>140-17E-1: Ouster metadata JSON without min_range_m falls back to model default min range.</summary>
        [Fact]
        public void MetadataJson_WithoutMinRange_UsesModelDefault()
        {
            const string json = @"{
                ""prod_line"": ""OS-2-128"",
                ""lidar_mode"": ""1024x10"",
                ""beam_altitude_angles"": [10.7, 10.0, 9.2, 8.4],
                ""beam_azimuth_angles"": [],
                ""data_format"": {
                    ""pixels_per_column"": 4,
                    ""columns_per_frame"": 1024,
                    ""columns_per_packet"": 16
                }
            }";

            Assert.True(LidarProfileLoader.TryParseFromJson(json, null, out var profile, out var error), error);
            Assert.Equal(0.5, profile.MinRangeMeters, 9);
        }

        [Fact]
        public void MetadataJson_BeamAnglesAreConvertedFromDegreesToRadians()
        {
            const string json = @"{
                ""prod_line"": ""OS-1-16"",
                ""lidar_mode"": ""1024x10"",
                ""beam_altitude_angles"": [90.0, 0.0],
                ""beam_azimuth_angles"": [180.0, -90.0],
                ""data_format"": {
                    ""pixels_per_column"": 2,
                    ""columns_per_frame"": 1024,
                    ""columns_per_packet"": 16
                }
            }";

            Assert.True(LidarProfileLoader.TryParseFromJson(json, null, out var profile, out var error), error);

            Assert.Equal(Math.PI / 2.0, profile.BeamAltitudeAngles[0], 9);
            Assert.Equal(0.0, profile.BeamAltitudeAngles[1], 9);
            Assert.Equal(Math.PI, profile.BeamAzimuthAngles[0], 9);
            Assert.Equal(-Math.PI / 2.0, profile.BeamAzimuthAngles[1], 9);
        }

        /// <summary>140-17E-2: metadata JSON min_range_m overrides model default min range.</summary>
        [Fact]
        public void MetadataJson_WithExplicitMinRange_OverridesModelDefault()
        {
            const string json = @"{
                ""sensor_info"": { ""prod_line"": ""OS-1-128"" },
                ""beam_intrinsics"": {
                    ""beam_altitude_angles"": [1.0, -1.0]
                },
                ""lidar_data_format"": {
                    ""pixels_per_column"": 2,
                    ""columns_per_frame"": 1024,
                    ""columns_per_packet"": 16
                },
                ""config_params"": {
                    ""lidar_mode"": ""1024x10"",
                    ""min_range_m"": 0.25
                }
            }";

            Assert.True(LidarProfileLoader.TryParseFromJson(json, null, out var profile, out var error), error);
            Assert.Equal(0.25, profile.MinRangeMeters, 9);
        }

        [Fact]
        public void LidarProfileValidateRejectsMissingBeamArrays()
        {
            var profile = new LidarProfile
            {
                ProductLine = "Custom",
                LidarMode = "1024x10",
                PixelsPerColumn = 2,
                ColumnsPerFrame = 1024,
                ScanRateHz = 10.0,
                BeamAltitudeAngles = new[] { 0.0 },
                BeamAzimuthAngles = null
            };

            Assert.False(profile.Validate(out var error));
            Assert.Contains("BeamAltitudeAngles", error);
        }

        [Fact]
        public void LidarScanPatternFactoryRejectsInvalidProfilesBeforeScanning()
        {
            var profile = new LidarProfile
            {
                ProductLine = "Custom",
                LidarMode = "1024x10",
                PixelsPerColumn = 2,
                ColumnsPerFrame = 1024,
                ScanRateHz = 10.0,
                BeamAltitudeAngles = new[] { 0.0, 0.1 },
                BeamAzimuthAngles = new[] { 0.0 }
            };

            var ex = Assert.Throws<ArgumentException>(() => LidarScanPatternFactory.FromProfile(profile, 1));
            Assert.Contains("Invalid LiDAR profile", ex.Message);
        }
    }
}
