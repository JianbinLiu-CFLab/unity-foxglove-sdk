// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-74 source-shape regression coverage for Core SDK sample optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_74Validation.
    /// </summary>
    public static class Phase140_74Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-74: Core SDK Samples Optimization ===");
            _passed = 0;

            VerifyFullDemoAssetsStaySyncedWithPackageOptimizations();
            VerifyLidarWanderDirectionIsNormalizedAtWriteSites();
            VerifyPointCloudSampleListsArePreSized();
            VerifyRos2BridgeStatusAvoidsPerFrameStringFormatting();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-74: {_passed} checks passed.");
        }

        private static void VerifyFullDemoAssetsStaySyncedWithPackageOptimizations()
        {
            var assetsSetup = Read("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            Check(assetsSetup.Contains("private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);", StringComparison.Ordinal)
                  && assetsSetup.Contains("StrictUtf8.GetString(payload, 0, count)", StringComparison.Ordinal)
                  && !assetsSetup.Contains("new UTF8Encoding(false, true).GetString", StringComparison.Ordinal),
                "140-74A-1: Assets FullDemo source keeps StrictUtf8 cached before sync_full_demo copies it to Samples~");

            var assetsMouse = Read("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/MouseDragCube.cs");
            var update = Slice(assetsMouse, "private void Update()", "    private void HandleRotation");
            Check(assetsMouse.Contains("private Camera _camera;", StringComparison.Ordinal)
                  && assetsMouse.Contains("_camera = Camera.main;", StringComparison.Ordinal)
                  && update.Contains("var cam = _camera;", StringComparison.Ordinal)
                  && update.Contains("_camera = cam;", StringComparison.Ordinal),
                "140-74A-2: Assets MouseDragCube caches Camera.main and refreshes only after null");
        }

        private static void VerifyLidarWanderDirectionIsNormalizedAtWriteSites()
        {
            var controller = Read("Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138LidarVehicleController.cs");
            Check(controller.Contains("SetWanderDirection(Vector3.forward);", StringComparison.Ordinal)
                  && controller.Contains("SetWanderDirection(Quaternion.Euler(0f, angle, 0f) * _wanderDirection);", StringComparison.Ordinal)
                  && controller.Contains("SetWanderDirection(Quaternion.Euler(0f, jitter, 0f) * _wanderDirection);", StringComparison.Ordinal),
                "140-74B-1: auto-wander direction writes normalize through one helper");

            var autoWander = Slice(controller, "private void ComputeAutoWander", "        /// <summary>True while");
            Check(autoWander.Contains("worldVelocity = _wanderDirection * _moveSpeed;", StringComparison.Ordinal)
                  && !autoWander.Contains("_wanderDirection.normalized", StringComparison.Ordinal),
                "140-74B-2: auto-wander FixedUpdate path avoids per-frame normalized sqrt");
        }

        private static void VerifyPointCloudSampleListsArePreSized()
        {
            var smoke = Read("Unity2Foxglove/Assets/Scripts/PointCloud/PointCloudSmokeSource.cs");
            var fanout = Read("Unity2Foxglove/Assets/Scripts/PointCloud/Phase88PointCloudFanoutSource.cs");
            Check(smoke.Contains("frame.Points.Capacity = count;", StringComparison.Ordinal),
                "140-74C-1: PointCloudSmokeSource pre-sizes Points before point loop");
            Check(fanout.Contains("frame.Points.Capacity = count;", StringComparison.Ordinal),
                "140-74C-2: Phase88PointCloudFanoutSource pre-sizes Points before point loop");
        }

        private static void VerifyRos2BridgeStatusAvoidsPerFrameStringFormatting()
        {
            var controller = Read("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs");
            var update = Slice(controller, "private void Update()", "    private void UpdateStatusIfChanged");
            Check(controller.Contains("private bool _lastRos2BridgeEnabled;", StringComparison.Ordinal)
                  && controller.Contains("private bool _hasStatusSnapshot;", StringComparison.Ordinal)
                  && controller.Contains("private void UpdateStatusIfChanged(Ros2BridgeStatsSnapshot stats, bool ros2BridgeEnabled)", StringComparison.Ordinal)
                  && update.Contains("UpdateStatusIfChanged(stats, _manager.Ros2BridgeEnabled);", StringComparison.Ordinal),
                "140-74D-1: ROS2 bridge sample status caches previous values");
            Check(controller.Contains("private void UpdateStatusIfChanged", StringComparison.Ordinal)
                  && controller.Contains("_status = $\"ROS2 Bridge", StringComparison.Ordinal)
                  && !update.Contains("_status = $\"ROS2 Bridge", StringComparison.Ordinal),
                "140-74D-2: status string formatting runs only when visible bridge stats change");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_74Validation.cs", StringComparison.Ordinal),
                "140-74E-1: test project compiles Phase140_74Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-74\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_74Validation.Validate", StringComparison.Ordinal),
                "140-74E-2: validation registry exposes --phase140-74");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
