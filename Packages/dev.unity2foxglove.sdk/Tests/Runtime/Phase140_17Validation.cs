// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-17 regression coverage for virtual LiDAR and IMU sensor lifecycle fixes.

using System;
using System.IO;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_17Validation.
    /// </summary>
    public static class Phase140_17Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-17: Virtual LiDAR and IMU Sensor Stack ===");
            _passed = 0;

            VirtualLidarReinitializesScanClockAfterManagerResolution();
            VirtualImuPhysicsRateOverrideIsReferenceCounted();
            VirtualImuReenableResetsState();
            RosettePositiveElevationUsesYUpSensorFrame();
            MetadataJsonUsesModelDefaultMinRange();
            MetadataJsonParsesExplicitMinRange();
            DeadSerializedSensorFieldsAreRemoved();
            VirtualLidarWarnsOnUnknownBuiltinModelFallback();
            VirtualLidarScanSchedulerDocumentsGrowOnlyCrossingBuffer();
            VirtualLidarScanFramePublisherHasNoSelfReferencingUsing();
            LidarRayGeneratorIsHiddenFromNormalRuntimeApiDiscovery();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-17: {_passed} checks passed.");
        }

        private static void VirtualLidarReinitializesScanClockAfterManagerResolution()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var clock = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanClock.cs");
            var start = Slice(lidar, "private void Start()", "private SensorUnitProfile ResolveSensorUnitProfile()");
            var resetIndex = start.IndexOf("_scanClock.Reset()", StringComparison.Ordinal);
            var resetStateIndex = start.IndexOf("ResetScanState(Time.fixedTimeAsDouble)", StringComparison.Ordinal);

            Check(clock.Contains("public void Reset()", StringComparison.Ordinal)
                  && resetIndex >= 0
                  && resetStateIndex >= 0
                  && resetIndex < resetStateIndex,
                "140-17A-1: VirtualLidar resets scan clock after manager resolution before scan-state reset");
        }

        private static void VirtualImuPhysicsRateOverrideIsReferenceCounted()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var apply = Slice(source, "private void ApplyGlobalPhysicsRateOverride", "private void RestoreFixedDeltaTime()");
            var restore = Slice(source, "private void RestoreFixedDeltaTime()", "private void EnsureSchemaRegistered()");

            Check(source.Contains("private static int _fixedDeltaOverrideUsers", StringComparison.Ordinal)
                  && source.Contains("private static float _fixedDeltaOverrideOriginal", StringComparison.Ordinal)
                  && apply.Contains("_fixedDeltaOverrideUsers == 0", StringComparison.Ordinal)
                  && restore.Contains("_fixedDeltaOverrideUsers--", StringComparison.Ordinal)
                  && restore.Contains("_fixedDeltaOverrideUsers == 0", StringComparison.Ordinal),
                "140-17B-1: VirtualImu global physics-rate override is reference-counted across instances");
        }

        private static void VirtualImuReenableResetsState()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var onEnable = Slice(source, "private void OnEnable()", "private void OnDisable()");
            Check(onEnable.Contains("_hasLastVelocity = false", StringComparison.Ordinal)
                  && onEnable.Contains("_hasEpoch = false", StringComparison.Ordinal)
                  && onEnable.Contains("_nextSampleIndex = 0", StringComparison.Ordinal),
                "140-17C-1: VirtualImu OnEnable resets velocity and epoch state after re-enable");
        }

        private static void RosettePositiveElevationUsesYUpSensorFrame()
        {
            const int beams = 1000;
            var pattern = new RosetteScanPattern("test", 10.0, 0.1, 30.0, 20.0, beams);
            var foundPositiveElevation = false;
            for (var i = 0; i < beams; i++)
            {
                var tau = (double)i / beams * 2.0 * Math.PI * 3.2;
                if (Math.Sin(11.0 * tau) <= 0.9)
                    continue;

                foundPositiveElevation = true;
                Check(pattern.TryGetRay(i, 0, out var direction, out _)
                      && direction.Y > 0f,
                    "140-17D-1: Rosette positive elevation points upward in the y-up sensor frame");
                break;
            }

            Check(foundPositiveElevation, "140-17D-2: Rosette test found a positive-elevation sample");
        }

        private static void MetadataJsonUsesModelDefaultMinRange()
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

            Check(LidarProfileLoader.TryParseFromJson(json, null, out var profile, out var error)
                  && Math.Abs(profile.MinRangeMeters - 0.5) < 1e-9,
                "140-17E-1: Ouster metadata JSON without min_range_m falls back to model default min range (error: " + error + ")");
        }

        private static void MetadataJsonParsesExplicitMinRange()
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

            Check(LidarProfileLoader.TryParseFromJson(json, null, out var profile, out var error)
                  && Math.Abs(profile.MinRangeMeters - 0.25) < 1e-9,
                "140-17E-2: metadata JSON min_range_m overrides model default min range (error: " + error + ")");
        }

        private static void DeadSerializedSensorFieldsAreRemoved()
        {
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var lidarEditor = Read("Packages/dev.unity2foxglove.sdk/Editor/Sensors/VirtualLidarEditor.cs");
            var imu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");

            Check(!lidar.Contains("_scanSubSteps", StringComparison.Ordinal)
                  && !lidarEditor.Contains("_scanSubSteps", StringComparison.Ordinal)
                  && !imu.Contains("_publishOnStart", StringComparison.Ordinal)
                  && !imu.Contains("_enableNoise", StringComparison.Ordinal),
                "140-17F-1: dead serialized sensor fields are removed instead of silently ignored");
        }

        private static void VirtualLidarWarnsOnUnknownBuiltinModelFallback()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            Check(source.Contains("Unknown built-in LiDAR model", StringComparison.Ordinal)
                  && source.Contains("using OS-1-32 fallback", StringComparison.Ordinal),
                "140-17G-1: VirtualLidar logs a warning before unknown built-in model fallback");
        }

        private static void VirtualLidarScanSchedulerDocumentsGrowOnlyCrossingBuffer()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            Check(source.Contains("grow-only", StringComparison.Ordinal)
                  && source.Contains("_pendingScanCrossings", StringComparison.Ordinal),
                "140-17H-1: VirtualLidar scan scheduler documents grow-only crossing-buffer retention");
        }

        private static void VirtualLidarScanFramePublisherHasNoSelfReferencingUsing()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanFramePublisher.cs");
            Check(!source.Contains("using Unity.FoxgloveSDK.Components;", StringComparison.Ordinal),
                "140-17I-1: VirtualLidarScanFramePublisher has no self-referencing namespace using");
        }

        private static void LidarRayGeneratorIsHiddenFromNormalRuntimeApiDiscovery()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarRayGenerator.cs");
            Check(source.Contains("[EditorBrowsable(EditorBrowsableState.Never)]", StringComparison.Ordinal),
                "140-17J-1: LidarRayGenerator public reference helper is hidden from normal API discovery");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_17Validation.cs", StringComparison.Ordinal),
                "140-17K-1: test project compiles Phase140_17Validation");
            Check(registry.Contains("Ci(\"--phase140-17\", \"Phase 140-17\", Phase140_17Validation.Validate", StringComparison.Ordinal),
                "140-17K-2: validation registry exposes --phase140-17");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] Missing start token: " + startToken);

            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;

            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
