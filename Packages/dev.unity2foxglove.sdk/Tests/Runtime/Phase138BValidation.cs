// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138B multi-vendor LiDAR middleware validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public static class Phase138BValidation
    {
        private static int _passed;

        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- Phase 138B: Multi-Vendor LiDAR Middleware ---");
            _passed = 0;

            VerifyRegistry();
            VerifySpinningRegression();
            VerifyMode();
            VerifyRosette();
            VerifySourceText();
            VerifyFixtureRoundTrip();

            Console.WriteLine($"Phase 138B: {_passed} checks passed.");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------
        // Registry checks
        // ---------------------------------------------------------------

        private static void VerifyRegistry()
        {
            // 1. Every model yields non-null ILidarScanPattern
            foreach (var spec in LidarModelRegistry.All)
            {
                var mode = spec.Modes?[0] ?? "";
                var step = spec.Kind == LidarScanKind.Spinning ? 4 : 1;
                var pattern = LidarScanPatternFactory.Create(spec, mode, step);
                Check(pattern != null,
                    $"8.1-1: {spec.Vendor}/{spec.Model} yields non-null ILidarScanPattern");
            }

            // 2. Every spinning model has RayCount > 0 and sane ScanRateHz (0..100)
            foreach (var spec in LidarModelRegistry.All.Where(s => s.Kind == LidarScanKind.Spinning))
            {
                var mode = spec.Modes?[0] ?? "";
                var pattern = LidarScanPatternFactory.Create(spec, mode, 4);
                Check(pattern.RayCount > 0,
                    $"8.1-2a: {spec.Vendor}/{spec.Model} RayCount > 0 ({pattern.RayCount})");
                Check(pattern.ScanRateHz > 0 && pattern.ScanRateHz < 100,
                    $"8.1-2b: {spec.Vendor}/{spec.Model} ScanRateHz sane ({pattern.ScanRateHz})");
            }

            // 3. Every non-repetitive (Livox) model has RayCount > 0
            foreach (var spec in LidarModelRegistry.All.Where(s => s.Kind == LidarScanKind.NonRepetitive))
            {
                var mode = spec.Modes?[0] ?? "";
                var pattern = LidarScanPatternFactory.Create(spec, mode, 1);
                Check(pattern.RayCount > 0,
                    $"8.1-3: {spec.Vendor}/{spec.Model} RayCount > 0 ({pattern.RayCount})");
            }
        }

        // ---------------------------------------------------------------
        // Spinning regression
        // ---------------------------------------------------------------

        private static void VerifySpinningRegression()
        {
            var profile = LidarProfileLoader.CreateOs132Default();
            var pattern = LidarScanPatternFactory.FromProfile(profile, 4);

            // 4. RayCount == 8192
            Check(pattern.RayCount == 8192,
                "8.2-1: OS-1-32 from profile, step=4 RayCount == 8192");

            // 5. First 100 ray directions are unit-length and finite
            var limit = Math.Min(100, pattern.RayCount);
            for (var i = 0; i < limit; i++)
            {
                if (!pattern.TryGetRay(i, 0, out var dir, out _))
                    continue;
                var mag = dir.Length();
                Check(!float.IsNaN(mag) && !float.IsInfinity(mag)
                      && mag >= 0.9999f && mag <= 1.0001f,
                    $"8.2-2: ray[{i}] direction is unit-length and finite ({mag:F6})");
            }

            // 6. Time offsets monotonic [0..1)
            {
                float prevOffset = -1f;
                var monotonic = true;
                var belowOne = true;
                var nonNegative = true;

                for (var i = 0; i < limit; i++)
                {
                    if (!pattern.TryGetRay(i, 0, out _, out var t))
                        continue;
                    if (t < 0) nonNegative = false;
                    if (t <= prevOffset) monotonic = false;
                    if (t >= 1.0f) belowOne = false;
                    prevOffset = t;
                }

                Check(nonNegative && monotonic && belowOne,
                    "8.2-3: time offsets in [0..1) and monotonic");
            }

            // 6b. Column azimuth sweep present: ring-0 direction varies across columns.
            //     Catches the B1 regression (all columns collapsing to one direction).
            {
                var effCols = profile.ColumnsPerFrame / 4;
                pattern.TryGetRay(0, 0, out var d0, out _);           // ring 0, column 0 (forward)
                pattern.TryGetRay(effCols / 4, 0, out var dq, out _); // ring 0, ~quarter turn
                var dx = Math.Abs(d0.X - dq.X);
                Check(dx > 0.5f, $"8.2-4: ring-0 azimuth sweep present (|dx| {dx:F3} > 0.5)");
            }

            // 6c. Equivalence with the Phase 138 LidarRayGenerator (locks B1 + B2).
            {
                var gen = new LidarRayGenerator(profile, 4);
                var effCols = profile.ColumnsPerFrame / 4;
                var maxDiff = 0.0;
                var checkN = Math.Min(300, pattern.RayCount);
                for (var i = 0; i < checkN; i++)
                {
                    var ring = i / effCols;
                    var col = (i % effCols) * 4;
                    pattern.TryGetRay(i, 0, out var p, out _);
                    gen.TryGetRay(col, ring, out var g, out _);
                    maxDiff = Math.Max(maxDiff, Math.Abs(p.X - g.X));
                    maxDiff = Math.Max(maxDiff, Math.Abs(p.Y - g.Y));
                    maxDiff = Math.Max(maxDiff, Math.Abs(p.Z - g.Z));
                }
                Check(maxDiff < 1e-5,
                    $"8.2-5: SpinningScanPattern matches LidarRayGenerator (maxDiff {maxDiff:E2})");
            }
        }

        // ---------------------------------------------------------------
        // Scan-mode override (M1)
        // ---------------------------------------------------------------

        private static void VerifyMode()
        {
            if (!LidarModelRegistry.TryGet(LidarVendor.Ouster, "OS-1-32", out var spec))
            {
                Check(false, "8.6-0: OS-1-32 not found in registry");
                return;
            }

            var p1024 = LidarScanPatternFactory.Create(spec, "1024x10", 4);
            var p2048 = LidarScanPatternFactory.Create(spec, "2048x10", 4);
            var p512 = LidarScanPatternFactory.Create(spec, "512x20", 4);

            Check(p2048.RayCount == p1024.RayCount * 2,
                $"8.6-1: mode 2048x10 doubles RayCount ({p1024.RayCount} -> {p2048.RayCount})");
            Check(Math.Abs(p1024.ScanRateHz - 10.0) < 1e-9,
                $"8.6-2: mode 1024x10 -> 10 Hz ({p1024.ScanRateHz})");
            Check(Math.Abs(p512.ScanRateHz - 20.0) < 1e-9,
                $"8.6-3: mode 512x20 -> 20 Hz ({p512.ScanRateHz})");
        }

        // ---------------------------------------------------------------
        // Rosette (Livox) checks
        // ---------------------------------------------------------------

        private static void VerifyRosette()
        {
            if (!LidarModelRegistry.TryGet(LidarVendor.Livox, "Mid-360", out var spec))
            {
                Check(false, "8.3-0: Mid-360 not found in registry");
                return;
            }

            var pattern = LidarScanPatternFactory.Create(spec, "", 1);

            // 7. RayCount == spec.BeamsPerFrame
            Check(pattern.RayCount == spec.BeamsPerFrame,
                $"8.3-1: Mid-360 RayCount == BeamsPerFrame ({pattern.RayCount} == {spec.BeamsPerFrame})");

            // 8. First 100 ray directions are finite
            var limit = Math.Min(100, pattern.RayCount);
            for (var i = 0; i < limit; i++)
            {
                if (!pattern.TryGetRay(i, 0, out var dir, out _))
                    continue;
                Check(!float.IsNaN(dir.X) && !float.IsNaN(dir.Y) && !float.IsNaN(dir.Z)
                      && !float.IsInfinity(dir.X) && !float.IsInfinity(dir.Y) && !float.IsInfinity(dir.Z),
                    $"8.3-2: Mid-360 ray[{i}] direction is finite");
            }

            // 9. Non-repetition: frameIndex=0 vs frameIndex=1 produce different directions for same ray index
            pattern.TryGetRay(0, 0, out var dir0, out _);
            pattern.TryGetRay(0, 1, out var dir1, out _);
            var diff = Math.Abs(dir0.X - dir1.X) + Math.Abs(dir0.Y - dir1.Y) + Math.Abs(dir0.Z - dir1.Z);
            Check(diff > 1e-6f,
                $"8.3-3: Mid-360 golden-angle rotation changes direction (frame0 vs frame1 diff={diff:F6})");
        }

        // ---------------------------------------------------------------
        // Source-text checks
        // ---------------------------------------------------------------

        private static void VerifySourceText()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var path = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk",
                "Runtime", "Sensors", "Lidar", "VirtualLidar.cs");

            Check(File.Exists(path), "8.4-1: VirtualLidar.cs exists");

            var content = File.ReadAllText(path);

            // 10. Contains ILidarScanPattern (middleware wired)
            Check(content.Contains("ILidarScanPattern"),
                "8.4-2: VirtualLidar.cs contains ILidarScanPattern");

            // 11. Does NOT contain ROS2/sensor_msgs using directives
            Check(!content.Contains("using ROS2;") && !content.Contains("using sensor_msgs;"),
                "8.4-3: VirtualLidar.cs has no ROS2/sensor_msgs using directives");

            // 12. Registry has >= 17 models across 4 vendors
            var vendorCount = LidarModelRegistry.All.Select(s => s.Vendor).Distinct().Count();
            var modelCount = LidarModelRegistry.All.Count;
            Check(modelCount >= 17,
                $"8.4-4a: Registry has >= 17 models ({modelCount})");
            Check(vendorCount >= 4,
                $"8.4-4b: Registry covers >= 4 vendors ({vendorCount})");
        }

        // ---------------------------------------------------------------
        // Fixture round-trip
        // ---------------------------------------------------------------

        private static void VerifyFixtureRoundTrip()
        {
            // 13. Legacy Ouster metadata with pixels_per_column and beam_altitude_angles at top level
            // Build a 16-beam legacy JSON string programmatically to avoid manual counting errors.
            var legacyJson = "{ \"pixels_per_column\": 16, \"columns_per_frame\": 1024, \"columns_per_packet\": 16, \"beam_altitude_angles\": [";
            for (var i = 0; i < 16; i++)
                legacyJson += (i == 0 ? "" : ", ") + string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}", 15.0 - i * 2.0);
            legacyJson += "], \"beam_azimuth_angles\": [";
            for (var i = 0; i < 16; i++)
                legacyJson += (i == 0 ? "0.0" : ", 0.0");
            legacyJson += "] }";

            var legacyOk = LidarProfileLoader.TryParseFromJson(legacyJson, "1024x10", out var legacyProfile, out var legacyError);

            Check(legacyOk,
                $"8.5-1: Legacy Ouster metadata parses ({legacyError ?? "OK"})");
            if (legacyOk)
            {
                Check(legacyProfile.PixelsPerColumn == 16,
                    "8.5-2: Legacy profile PixelsPerColumn == 16");
                Check(legacyProfile.BeamAltitudeAngles.Length == 16,
                    "8.5-3: Legacy profile BeamAltitudeAngles.Length == 16");
            }

            // 14. v2/v3 nested Ouster metadata now parses (beam_intrinsics +
            //     lidar_data_format + config_params + sensor_info). lidar_mode is
            //     taken from config_params (mode argument passed as null).
            const string v2v3Json = @"{
                ""sensor_info"": { ""prod_line"": ""OS-1-128"" },
                ""beam_intrinsics"": {
                    ""beam_altitude_angles"": [16.6, 15.5, 14.4, 13.3, 12.2, 11.1, 10.0, 8.9, 7.8, 6.7, 5.6, 4.5, 3.4, 2.3, 1.2, 0.1, -1.0, -2.1, -3.2, -4.3, -5.4, -6.5, -7.6, -8.7, -9.8, -10.9, -12.0, -13.1, -14.2, -15.3, -16.4, -17.5],
                    ""lidar_origin_to_beam_origin_mm"": 15.806
                },
                ""lidar_data_format"": {
                    ""pixels_per_column"": 32,
                    ""columns_per_frame"": 2048,
                    ""columns_per_packet"": 16
                },
                ""config_params"": { ""lidar_mode"": ""2048x10"" }
            }";

            var v2v3Ok = LidarProfileLoader.TryParseFromJson(v2v3Json, null, out var v2v3Profile, out var v2v3Error);
            Check(v2v3Ok, $"8.5-4: v2/v3 nested Ouster metadata parses ({v2v3Error ?? "OK"})");
            if (v2v3Ok)
            {
                Check(v2v3Profile.PixelsPerColumn == 32, "8.5-4a: v2/v3 PixelsPerColumn == 32 (lidar_data_format)");
                Check(v2v3Profile.ColumnsPerFrame == 2048, "8.5-4b: v2/v3 ColumnsPerFrame == 2048 (lidar_data_format)");
                Check(Math.Abs(v2v3Profile.ScanRateHz - 10.0) < 1e-9, "8.5-4c: v2/v3 ScanRateHz from config_params lidar_mode == 10");
                Check(v2v3Profile.ProductLine == "OS-1-128", "8.5-4d: v2/v3 ProductLine from sensor_info");
            }

            // 15. Real legacy schema: beam angles at top level, pixels under
            //     "data_format", lidar_mode a top-level string (mode arg null).
            const string legacyNestedJson = @"{
                ""prod_line"": ""OS-2-128"",
                ""lidar_mode"": ""1024x10"",
                ""lidar_origin_to_beam_origin_mm"": 13.762,
                ""beam_altitude_angles"": [10.7, 10.0, 9.2, 8.4, 7.6, 6.8, 6.0, 5.2, 4.4, 3.6, 2.8, 2.0, 1.2, 0.4, -0.4, -1.2, -2.0, -2.8, -3.6, -4.4, -5.2, -6.0, -6.8, -7.6, -8.4, -9.2, -10.0, -10.8, -11.0, -11.05, -11.07, -11.09],
                ""beam_azimuth_angles"": [],
                ""data_format"": { ""pixels_per_column"": 32, ""columns_per_frame"": 1024, ""columns_per_packet"": 16 }
            }";

            var legacyNestedOk = LidarProfileLoader.TryParseFromJson(legacyNestedJson, null, out var legacyNestedProfile, out var legacyNestedError);
            Check(legacyNestedOk, $"8.5-7: legacy data_format-nested metadata parses ({legacyNestedError ?? "OK"})");
            if (legacyNestedOk)
            {
                Check(legacyNestedProfile.PixelsPerColumn == 32, "8.5-7a: legacy-nested PixelsPerColumn == 32 (from data_format)");
                Check(legacyNestedProfile.ColumnsPerFrame == 1024, "8.5-7b: legacy-nested ColumnsPerFrame == 1024");
            }

            // Source-text: verify the parser source file exists so v2/v3 handling can be added later
            var repoRoot = Phase16Validation.FindRepoRoot();
            var loaderPath = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk",
                "Runtime", "Sensors", "Lidar", "LidarProfileLoader.cs");
            Check(File.Exists(loaderPath),
                "8.5-5: LidarProfileLoader.cs exists (parser source present for future v2/v3 support)");

            // Verify that the parser file contains pixels_per_column (legacy path is wired)
            var loaderContent = File.ReadAllText(loaderPath);
            Check(loaderContent.Contains("pixels_per_column"),
                "8.5-6: LidarProfileLoader.cs references pixels_per_column (legacy parse path is wired)");
        }

        private static void Check(bool condition, string label)
        {
            _passed++;
            Console.WriteLine(condition ? $"[PASS] {label}" : $"[FAIL] {label}");
            if (!condition)
                throw new InvalidOperationException($"Phase 138B validation failed: {label}");
        }
    }
}

