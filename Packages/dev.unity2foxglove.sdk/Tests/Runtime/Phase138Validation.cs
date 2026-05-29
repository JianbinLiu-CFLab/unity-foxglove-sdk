// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138 Virtual LiDAR Digital Twin validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase138Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- Phase 138: Virtual LiDAR Digital Twin ---");
            _passed = 0;

            VerifyProfileDefaults();
            VerifyJsonParseErrors();
            VerifyRayGenerator();
            VerifyFrameIntegration();
            VerifyVirtualLidarSource();
            VerifyMazeDemoFiles();

            Console.WriteLine($"Phase 138: {_passed} checks passed.");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------
        // 7.1  Sensor / profile / ray generator checks (pure C#)
        // ---------------------------------------------------------------

        private static void VerifyProfileDefaults()
        {
            var profile = LidarProfileLoader.CreateOs132Default();
            Check(profile.PixelsPerColumn == 32, "7.1-1: PixelsPerColumn == 32");
            Check(profile.ColumnsPerFrame == 1024, "7.1-2: ColumnsPerFrame == 1024");
            Check(profile.ScanRateHz > 9.9 && profile.ScanRateHz < 10.1, "7.1-3: ScanRateHz ~ 10.0 Hz");
            Check(Math.Abs(profile.MinRangeMeters - 0.7) < 1e-9, "7.1-4: MinRangeMeters == 0.7");
            Check(profile.BeamAltitudeAngles.Length == 32, "7.1-5: BeamAltitudeAngles.Length == 32");
            Check(profile.BeamAzimuthAngles.Length == 32, "7.1-6: BeamAzimuthAngles.Length == 32");
        }

        private static void VerifyJsonParseErrors()
        {
            // 2. Mismatched beam counts
            {
                var json = "{ \"pixels_per_column\": 32, \"beam_altitude_angles\": [";
                for (var i = 0; i < 31; i++)
                    json += (i == 0 ? "" : ",") + "0.0";
                json += "] }";

                var ok = LidarProfileLoader.TryParseFromJson(json, "1024x10", out var profile, out var error);
                Check(!ok, "7.1-7: TryParseFromJson mismatched beam counts returns false");
                Check(error != null && error.Contains("does not match"), "7.1-8: error message mentions mismatch");
                Check(profile == null, "7.1-9: profile is null on mismatch failure");
            }

            // 3. Unsupported mode "1024x99"
            {
                var json = "{ \"pixels_per_column\": 32, \"beam_altitude_angles\": [";
                for (var i = 0; i < 32; i++)
                    json += (i == 0 ? "" : ",") + "0.0";
                json += "] }";

                var ok = LidarProfileLoader.TryParseFromJson(json, "1024x99", out var profile, out var error);
                Check(!ok && error != null && error.Contains("1024x99", StringComparison.Ordinal),
                    "7.1-10: unsupported mode \"1024x99\" returns false with mode in error message");
            }
        }

        private static void VerifyRayGenerator()
        {
            var profile = LidarProfileLoader.CreateOs132Default();

            // 4. columnStep=4 → 32 * (1024/4) = 8192 rays
            var gen4 = new LidarRayGenerator(profile, 4);
            Check(gen4.RayCount == 32 * (1024 / 4), "7.1-11: RayGenerator(step=4) RayCount == 8192");

            // 5. columnStep=1 → 32768 rays
            var gen1 = new LidarRayGenerator(profile, 1);
            Check(gen1.RayCount == 32 * 1024, "7.1-12: RayGenerator(step=1) RayCount == 32768");

            // 6. All generated ray directions are finite and unit-length
            {
                var allValid = true;
                for (var c = 0; c < profile.ColumnsPerFrame && allValid; c++)
                {
                    for (var r = 0; r < profile.PixelsPerColumn && allValid; r++)
                    {
                        if (!gen1.TryGetRay(c, r, out var dir, out _))
                            continue;
                        var mag = dir.Length();
                        if (float.IsNaN(mag) || float.IsInfinity(mag)
                            || mag < 0.9999f || mag > 1.0001f)
                        {
                            allValid = false;
                        }
                    }
                }

                Check(allValid, "7.1-13: all ray directions are finite and unit-length");
            }

            // 7. Time offsets monotonic by column, normalized [0..1)
            {
                const int ring = 0;
                float prevOffset = -1f;
                var monotonic = true;
                var belowOne = true;

                for (var c = 0; c < profile.ColumnsPerFrame; c++)
                {
                    gen1.TryGetRay(c, ring, out _, out var t);
                    if (t <= prevOffset) monotonic = false;
                    if (t >= 1.0f) belowOne = false;
                    prevOffset = t;
                }

                Check(monotonic, "7.1-14: time offsets monotonic by column");
                Check(belowOne, "7.1-15: time offsets normalized [0..1)");
            }

            // 8. Column sweep: columns must cover ~360 degrees, i.e. the
            //    aggregate XZ direction set must span both hemispheres.
            {
                const int ring = 16; // mid-ring (non-zero altitude OK)
                gen1.TryGetRay(0, ring, out var dirAt0, out _);
                gen1.TryGetRay(profile.ColumnsPerFrame / 2, ring, out var dirAtHalf, out _);
                gen1.TryGetRay(profile.ColumnsPerFrame / 4, ring, out var dirAtQuarter, out _);

                // After half a rotation the XZ heading must be roughly opposite.
                var dot = dirAt0.X * dirAtHalf.X + dirAt0.Z * dirAtHalf.Z;
                var quarterDot = dirAt0.X * dirAtQuarter.X + dirAt0.Z * dirAtQuarter.Z;

                Check(dot < -0.99f,
                    "7.1-16: column at half-rotation points opposite to column 0 (dot ~= -1)");
                Check(Math.Abs(quarterDot) < 0.01f,
                    "7.1-17: column at quarter-rotation is orthogonal to column 0 (dot ~= 0)");
            }
        }

        // ---------------------------------------------------------------
        // 7.2  Frame integration checks
        // ---------------------------------------------------------------

        private static void VerifyFrameIntegration()
        {
            // 8. Build a synthetic PointCloudFrame with 4 points
            var frame = new PointCloudFrame
            {
                UnixNs = 1000000000UL,
                FrameId = "test_frame"
            };
            frame.Points.Add(new PointCloudPoint(1f, 0f, 0f) { Intensity = 0.5f, Ring = 1 });
            frame.Points.Add(new PointCloudPoint(0f, 2f, 0f) { Reflectivity = 0.3f, Ring = 2 });
            frame.Points.Add(new PointCloudPoint(0f, 0f, 3f) { Intensity = 0.8f, TimeOffsetSeconds = 0.05f });
            frame.Points.Add(new PointCloudPoint(-1f, -1f, 1f));

            var packed = PointCloudPackedDataBuilder.Build(frame);
            Check(packed.Data.Length > 0, "7.2-1: PointCloudPackedDataBuilder builds non-empty data");
            Check(packed.PointStride > 0, "7.2-2: packed PointStride > 0");
            Check(packed.Fields.Count >= 3, "7.2-3: packed has at least x/y/z fields");

            // 9. Frame serialized as JSON via PointCloudMessageBuilder.CreateJson without throwing
            try
            {
                var jsonMsg = PointCloudMessageBuilder.CreateJson(frame);
                Check(jsonMsg != null && jsonMsg.PointStride > 0,
                    "7.2-4: CreateJson produces valid non-null message");
            }
            catch (Exception ex)
            {
                Check(false, "7.2-4: CreateJson threw: " + ex.Message);
            }

            // 10. Frame serialized as protobuf via PointCloudMessageBuilder.CreateProtobuf without throwing
            try
            {
                var protoMsg = PointCloudMessageBuilder.CreateProtobuf(frame);
                Check(protoMsg != null && protoMsg.PointStride > 0,
                    "7.2-5: CreateProtobuf produces valid non-null message");
            }
            catch (Exception ex)
            {
                Check(false, "7.2-5: CreateProtobuf threw: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // Source-text checks (Unity-free — File.ReadAllText only)
        // ---------------------------------------------------------------

        private static void VerifyVirtualLidarSource()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var path = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk",
                "Runtime", "Sensors", "Lidar", "VirtualLidar.cs");

            Check(File.Exists(path), "7.3-1: VirtualLidar.cs exists at expected path");

            var content = File.ReadAllText(path);

            Check(Regex.IsMatch(content, @"namespace\s+Unity\.FoxgloveSDK\.Components\b"),
                "7.3-2: VirtualLidar.cs namespace is Unity.FoxgloveSDK.Components");

            Check(!content.Contains("using ROS2;") && !content.Contains("using sensor_msgs;"),
                "7.3-3: VirtualLidar.cs has no ROS2/sensor_msgs using directives");

            Check((content.Contains("SetFrame") || content.Contains("PublishFrame"))
                  && !content.Contains("PublishJson") && !content.Contains("PublishProto"),
                "7.3-4: VirtualLidar.cs calls SetFrame/PublishFrame, not PublishJson/PublishProto directly");
        }

        // ---------------------------------------------------------------
        // Maze demo checks
        // ---------------------------------------------------------------

        private static void VerifyMazeDemoFiles()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var mazeDir = Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk",
                "Samples~", "Virtual LiDAR Maze Demo");
            var readmePath = Path.Combine(mazeDir, "README.md");

            if (!Directory.Exists(mazeDir))
            {
                Check(false, "7.4-1: maze demo directory not found");
                return;
            }

            // 15. Maze demo files exist
            var csFiles = Directory.GetFiles(mazeDir, "Phase138*.cs");
            var csCount = csFiles.Length;

            Check(csCount >= 4, $"7.4-2: maze demo has >= 4 Phase138*.cs files (found {csCount})");

            var hasReadme = File.Exists(readmePath);
            Check(hasReadme, "7.4-3: MazeDemo README.md exists");

            // 16. Maze demo files do NOT contain "using ROS2;"
            var ros2Violations = new List<string>();
            foreach (var f in csFiles)
            {
                var text = File.ReadAllText(f);
                if (text.Contains("using ROS2;"))
                    ros2Violations.Add(Path.GetFileName(f));
            }

            Check(ros2Violations.Count == 0,
                "7.4-5: maze demo files have no \"using ROS2;\"" +
                (ros2Violations.Count == 0 ? "" : " (" + string.Join(", ", ros2Violations) + ")"));

            // 17. Maze vehicle controller supports both input systems or falls back gracefully
            var controllerFiles = csFiles
                .Where(f => Path.GetFileName(f).Contains("Controller", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (controllerFiles.Length > 0)
            {
                var hasInputSupport = false;
                foreach (var f in controllerFiles)
                {
                    var content = File.ReadAllText(f);
                    // Either native Input System support or legacy fallback
                    if (content.Contains("ENABLE_INPUT_SYSTEM", StringComparison.Ordinal)
                        || content.Contains("InputAction", StringComparison.Ordinal)
                        || content.Contains("Input Manager is disabled", StringComparison.Ordinal))
                    {
                        hasInputSupport = true;
                        break;
                    }
                }

                Check(hasInputSupport,
                    "7.4-5: vehicle controller supports Input System or falls back gracefully on legacy");
            }
            else
            {
                Check(false, "7.4-5: no vehicle controller file found");
            }
        }

        private static void Check(bool condition, string label)
        {
            _passed++;
            Console.WriteLine(condition ? $"[PASS] {label}" : $"[FAIL] {label}");
            if (!condition)
                throw new InvalidOperationException($"Phase 138 validation failed: {label}");
        }
    }
}
