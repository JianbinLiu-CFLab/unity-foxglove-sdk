// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-18 validation for virtual LiDAR, IMU, and sensor simulation fixes.

using System;
using System.IO;
using System.Numerics;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Imu;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_18Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-18: Virtual LiDAR, IMU, and Sensor Simulation ===");
            _passed = 0;

            VirtualLidarScanClockRejectsNonFiniteDelta();
            SpinningPatternRetainsFinalPartialColumnStep();
            RosettePatternUsesSpinningPositiveAzimuthConvention();
            ImuQueueReportsDroppedSamples();
            ImuSubStepTimestampsRoundFractionalNanoseconds();
            LidarDiagnosticsSeparateTimingAndProfileInvalidations();
            VirtualLidarWarnsWhenOwnLayerIsIncluded();
            VirtualLidarSchedulerDoesNotConsumePartialColumns();
            VirtualImuGuardsConflictingPhysicsOverrides();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-18: {_passed} checks passed.");
        }

        private static void VirtualLidarScanClockRejectsNonFiniteDelta()
        {
            var clock = new VirtualLidarScanClock();
            Check(clock.EnsureInitialized(10.0, _ => 1_000_000_000UL),
                "163-18A-1: scan clock initializes a stable Unix epoch");

            Check(clock.GetScanStartUnixNs(double.NaN) == 1_000_000_000UL,
                "163-18A-2: scan clock maps NaN physics time back to the epoch");
            Check(clock.GetScanStartUnixNs(double.PositiveInfinity) == 1_000_000_000UL,
                "163-18A-3: scan clock maps infinite physics time back to the epoch");
            Check(clock.GetScanStartUnixNs(10.25) == 1_250_000_000UL,
                "163-18A-4: scan clock rounds finite positive deltas in nanoseconds");
        }

        private static void SpinningPatternRetainsFinalPartialColumnStep()
        {
            var pattern = SpinningScanPattern.FromUniformFov(
                "test",
                scanRateHz: 10.0,
                minRangeMeters: 0.1,
                rings: 1,
                columns: 1024,
                columnStep: 3,
                fovTopDeg: 0.0,
                fovBottomDeg: 0.0);

            Check(pattern.RayCount == 342,
                "163-18B-1: spinning pattern uses ceiling column-step count");
            Check(pattern.TryGetRay(341, 0, out var _, out var timeOffset)
                  && Math.Abs(timeOffset - (1023f / 1024f)) < 0.000001f,
                "163-18B-2: spinning pattern maps the final partial step to the last physical column");
        }

        private static void RosettePatternUsesSpinningPositiveAzimuthConvention()
        {
            var rosette = new RosetteScanPattern(
                "livox",
                scanRateHz: 10.0,
                minRangeMeters: 0.1,
                fovHDeg: 80.0,
                fovVDeg: 10.0,
                beamsPerFrame: 256);

            var foundPositiveAzimuth = false;
            for (var i = 0; i < rosette.RayCount; i++)
            {
                if (!rosette.TryGetRay(i, 0, out var direction, out _))
                    continue;

                if (direction.X > 0.2f)
                {
                    foundPositiveAzimuth = true;
                    break;
                }
            }

            Check(foundPositiveAzimuth,
                "163-18C-1: rosette positive azimuth points toward +X like spinning LiDAR");
        }

        private static void ImuQueueReportsDroppedSamples()
        {
            var queue = new ImuSampleQueue();
            queue.Resize(2, minCapacity: 2);
            queue.Enqueue(default);
            queue.Enqueue(default);
            queue.Enqueue(default);

            Check(queue.Count == 2 && queue.DroppedCount == 1,
                "163-18D-1: IMU queue reports overwritten oldest samples");
            queue.Resize(3, minCapacity: 2);
            Check(queue.DroppedCount == 0,
                "163-18D-2: IMU queue drop counter resets on capacity changes");
        }

        private static void ImuSubStepTimestampsRoundFractionalNanoseconds()
        {
            Check(ImuSubStep.SampleTimestampNs(100UL, sampleIndex: 1, targetRateHz: 333) == 3_003_103UL,
                "163-18E-1: IMU sub-step timestamps round fractional nanoseconds instead of truncating");
        }

        private static void LidarDiagnosticsSeparateTimingAndProfileInvalidations()
        {
            var diagnostics = new LidarScanDiagnostics();
            LidarScanDiagnosticSnapshot snapshot = default;
            for (var i = 0; i < 59; i++)
            {
                Check(!diagnostics.Record(
                        enabled: true,
                        scanId: i,
                        rayCount: 1,
                        validPointCount: 1,
                        completeMs: 0.1,
                        buildMs: 0.0,
                        appendMs: 0.0,
                        asyncOverrun: false,
                        profileInvalidation: false,
                        fixedDeltaTimeSeconds: 1.0f,
                        out snapshot),
                    "163-18F-1: LiDAR diagnostics waits for the interval before snapshot " + i);
            }

            Check(diagnostics.Record(
                    enabled: true,
                    scanId: 60,
                    rayCount: 1,
                    validPointCount: 0,
                    completeMs: 0.1,
                    buildMs: 0.0,
                    appendMs: 0.0,
                    asyncOverrun: false,
                    profileInvalidation: true,
                    fixedDeltaTimeSeconds: 1.0f,
                    out snapshot),
                "163-18F-2: LiDAR diagnostics emits an interval snapshot");
            Check(snapshot.TimingOverruns == 0 && snapshot.ProfileInvalidations == 1,
                "163-18F-3: LiDAR diagnostics keeps timing overruns separate from profile invalidations");
        }

        private static void VirtualLidarWarnsWhenOwnLayerIsIncluded()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var start = ExtractMethod(source, "private void Start");
            var warning = ExtractMethod(source, "private void WarnIfOwnLayerIncludedInRaycastMask");

            Check(source.Contains("Physics layers included in LiDAR raycasts", StringComparison.Ordinal)
                  && source.Contains("own layer to avoid self-collision returns", StringComparison.Ordinal),
                "163-18G-1: VirtualLidar Inspector tooltip documents self-hit risk");
            Check(start.Contains("WarnIfOwnLayerIncludedInRaycastMask();", StringComparison.Ordinal),
                "163-18G-2: VirtualLidar checks the self-layer mask during startup");
            Check(warning.Contains("1 << gameObject.layer", StringComparison.Ordinal)
                  && warning.Contains("includes this GameObject's layer", StringComparison.Ordinal),
                "163-18G-3: VirtualLidar emits a contextual warning when its own layer is included");
        }

        private static void VirtualLidarSchedulerDoesNotConsumePartialColumns()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            var schedule = ExtractMethod(source, "public void SchedulePendingScan");

            Check(schedule.Contains("batchCount + rays.Length > scanBuffers.EffectiveRayCount", StringComparison.Ordinal)
                  && CheckOrdered(schedule, "batchCount + rays.Length > scanBuffers.EffectiveRayCount", "scanColumnCursor++;"),
                "163-18H-1: VirtualLidar scheduler stops before consuming a partial column at the batch cap");
            Check(source.Contains("profileInvalidation: true", StringComparison.Ordinal)
                  && source.Contains("timingOverrun={8} profileInvalidation={9}", StringComparison.Ordinal),
                "163-18H-2: VirtualLidar scheduler reports profile invalidations separately from timing overruns");
        }

        private static void VirtualImuGuardsConflictingPhysicsOverrides()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var fixedUpdate = ExtractMethod(source, "private void FixedUpdate");
            var applyOverride = ExtractMethod(source, "private void ApplyGlobalPhysicsRateOverride");
            var restore = ExtractMethod(source, "private void RestoreFixedDeltaTime");
            var update = ExtractMethod(source, "private void Update");

            Check(source.Contains("private static int _fixedDeltaOverrideTargetHz;", StringComparison.Ordinal)
                  && source.Contains("private static bool _warnedFixedDeltaOverrideConflict;", StringComparison.Ordinal),
                "163-18I-1: VirtualImu stores the active fixed-delta override target");
            Check(applyOverride.Contains("ignoring conflicting request", StringComparison.Ordinal)
                  && applyOverride.Contains("_fixedDeltaOverrideTargetHz", StringComparison.Ordinal),
                "163-18I-2: VirtualImu warns and keeps the first active physics-rate override");
            Check(restore.Contains("_fixedDeltaOverrideTarget = 0f;", StringComparison.Ordinal)
                  && restore.Contains("_warnedFixedDeltaOverrideConflict = false;", StringComparison.Ordinal),
                "163-18I-3: VirtualImu clears override-conflict state when the last user exits");
            Check(fixedUpdate.Contains("initializedEpochThisTick", StringComparison.Ordinal)
                  && fixedUpdate.Contains("initializedEpochThisTick ? linearBody : _lastBodyAcceleration", StringComparison.Ordinal),
                "163-18I-4: VirtualImu first sub-step starts from the current tick sample instead of zero");
            Check(update.Contains("LogDroppedSamplesIfNeeded();", StringComparison.Ordinal)
                  && source.Contains("_queue.DroppedCount", StringComparison.Ordinal),
                "163-18I-5: VirtualImu surfaces bounded-queue drops during publish drain");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_18Validation.cs", StringComparison.Ordinal),
                "163-18J-1: runtime test project compiles Phase163_18Validation");
            Check(registry.Contains("--phase163-18", StringComparison.Ordinal)
                  && registry.Contains("Phase163_18Validation.Validate", StringComparison.Ordinal),
                "163-18J-2: validation registry exposes --phase163-18");
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Check(start >= 0, "Phase 163-18 validation helper found method: " + signature);
            return ExtractBlock(source, start);
        }

        private static string ExtractBlock(string source, int start)
        {
            var brace = source.IndexOf('{', start);
            Check(brace >= 0, "Phase 163-18 validation helper found opening brace");

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unable to extract source block.");
        }

        private static bool CheckOrdered(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
