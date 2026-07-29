// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-33 validation for R2FU sample and RViz acceptance hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_33Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-33: R2FU Sample and RViz Acceptance Hardening ===");
            _passed = 0;

            VirtualLidarBuilderFilenameMatchesItsClass();
            TransformBridgeWarnsWhenRosTimeSecondsClamp();
            Phase160RvizUsesSensorDataQosForPointCloud2();
            LyricalZenohConfigMirrorsAreDocumentedAndChecked();
            Ros2SampleReviewFalsePositivesRemainGuarded();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-33: {_passed} checks passed.");
        }

        private static void VirtualLidarBuilderFilenameMatchesItsClass()
        {
            foreach (var root in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/Virtual LiDAR PointCloud2 Digital Twin",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/Virtual LiDAR PointCloud2 Digital Twin",
            })
            {
                Check(File.Exists(RepoPath(root + "/Phase138CPointCloud2MessageBuilder.cs"))
                      && File.Exists(RepoPath(root + "/Phase138CPointCloud2MessageBuilder.cs.meta"))
                      && !File.Exists(RepoPath(root + "/Phase129PointCloud2MessageBuilder.cs"))
                      && !File.Exists(RepoPath(root + "/Phase129PointCloud2MessageBuilder.cs.meta")),
                    "163-33A: Phase138c virtual LiDAR builder file name matches its Phase138C class in " + root);

                var source = ReadRepoText(root + "/Phase138CPointCloud2MessageBuilder.cs");
                Check(source.Contains("public static class Phase138CPointCloud2MessageBuilder", StringComparison.Ordinal)
                      && source.Contains("Maps SDK packed point-cloud frames", StringComparison.Ordinal),
                    "163-33B: Phase138c virtual LiDAR builder keeps the distinct Phase138C type name");
            }
        }

        private static void TransformBridgeWarnsWhenRosTimeSecondsClamp()
        {
            var bridge = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs");

            Check(bridge.Contains("private static bool _warnedTimestampClamp", StringComparison.Ordinal)
                  && bridge.Contains("Sec = ClampRosTimeSeconds(sec)", StringComparison.Ordinal)
                  && bridge.Contains("private static int ClampRosTimeSeconds(ulong seconds)", StringComparison.Ordinal)
                  && bridge.Contains("seconds <= int.MaxValue", StringComparison.Ordinal)
                  && bridge.Contains("builtin_interfaces/Time int32 range", StringComparison.Ordinal)
                  && bridge.Contains("return int.MaxValue", StringComparison.Ordinal),
                "163-33C: TF bridge documents and warns once when ROS time seconds exceed int32 range");
        }

        private static void Phase160RvizUsesSensorDataQosForPointCloud2()
        {
            var script = ReadRepoText("Scripts/smoke/ros2/phase160_humble_lidar_deskew_acceptance.py");
            var display = ExtractPythonFunction(script, "pointcloud_display");

            Check(display.Contains("Reliability Policy: Best Effort", StringComparison.Ordinal)
                  && display.Contains("Depth: 1", StringComparison.Ordinal)
                  && display.Contains("sensor-data PointCloud2 publisher QoS", StringComparison.Ordinal)
                  && !display.Contains("Reliability Policy: Reliable", StringComparison.Ordinal),
                "163-33D: Phase160 RViz PointCloud2 display matches R2FU sensor-data QoS");
        }

        private static void LyricalZenohConfigMirrorsAreDocumentedAndChecked()
        {
            var validator = ReadRepoText("Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py");
            var readme = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/README.md");
            var builder = ReadRepoText("Scripts/ros2forunity/windows/lyrical/build_r2fu_runtime_package.py");

            Check(validator.Contains("ZENOH_CONFIG_MIRRORS", StringComparison.Ordinal)
                  && validator.Contains("plugin_config.read_bytes() == streaming_assets_config.read_bytes()", StringComparison.Ordinal)
                  && validator.Contains("Zenoh config mirror matches StreamingAssets", StringComparison.Ordinal),
                "163-33E: Lyrical runtime validator fails when mirrored Zenoh configs diverge");
            Check(readme.Contains("StreamingAssets/Ros2ForUnity/share", StringComparison.Ordinal)
                  && readme.Contains("byte-identical", StringComparison.Ordinal)
                  && builder.Contains("byte-identical", StringComparison.Ordinal),
                "163-33F: Lyrical runtime docs and generator explain Zenoh config mirror ownership");
        }

        private static void Ros2SampleReviewFalsePositivesRemainGuarded()
        {
            var ros2BridgeSample = RepoPath("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample");
            Check(File.Exists(Path.Combine(ros2BridgeSample, "README.md"))
                  && File.Exists(Path.Combine(ros2BridgeSample, "Scenes", "Ros2BridgeSample.unity"))
                  && File.Exists(Path.Combine(ros2BridgeSample, "Scripts", "Ros2BridgeSampleController.cs")),
                "163-33G: Ros2BridgeSample is populated and not an empty package sample");

            var phase132Readme = ReadRepoText(
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/ROS2 Standard Message Expansion/README.md");
            Check(phase132Readme.Contains("This sample does not publish `/tf`", StringComparison.Ordinal)
                  && phase132Readme.Contains("not the pass/fail gate", StringComparison.Ordinal)
                  && phase132Readme.Contains("No transform", StringComparison.Ordinal),
                "163-33H: Phase132 documents the no-TF RViz helper boundary");

            foreach (var validator in new[]
            {
                "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
                "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py",
                "Scripts/ros2forunity/windows/lyrical/validate_ros2forunity_package.py",
            })
            {
                var source = ReadRepoText(validator);
                Check(source.Contains("rviz2_phase129_pointcloud2.rviz", StringComparison.Ordinal)
                      && source.Contains("rviz2_phase132_standard_messages.rviz", StringComparison.Ordinal)
                      && source.Contains("for path in required:", StringComparison.Ordinal)
                      && source.Contains("path.exists()", StringComparison.Ordinal),
                    "163-33I: " + validator + " has explicit sample file existence checks before content assertions");
            }
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_33Validation.cs", StringComparison.Ordinal),
                "163-33J: runtime test project compiles Phase163_33Validation");
            Check(registry.Contains("Ci(\"--phase163-33\", \"Phase 163-33\", Phase163_33Validation.Validate", StringComparison.Ordinal),
                "163-33K: validation registry exposes --phase163-33");
        }

        private static string ExtractPythonFunction(string source, string name)
        {
            var marker = "def " + name + "(";
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var next = source.IndexOf("\ndef ", start + marker.Length, StringComparison.Ordinal);
            return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
        }

        private static string ReadRepoText(string relativePath) => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
