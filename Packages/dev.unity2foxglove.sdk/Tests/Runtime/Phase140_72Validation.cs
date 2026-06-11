// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-72 source-shape regression coverage for ROS2 For Unity adapter scan optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_72Validation.
    /// </summary>
    public static class Phase140_72Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-72: ROS2 For Unity Adapter Package Optimization ===");
            _passed = 0;

            VerifyReusableScanCollections();
            VerifyCameraScanReusesPublisherDiscovery();
            VerifyOwnershipRiskOptimizationsRemainDeferred();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-72: {_passed} checks passed.");
        }

        private static void VerifyReusableScanCollections()
        {
            VerifyBridgeScanCollections(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs",
                "140-72A-1: camera native bridge reuses scan collections");
            VerifyBridgeScanCollections(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs",
                "140-72A-2: IMU native bridge reuses scan collections");
            VerifyBridgeScanCollections(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs",
                "140-72A-3: PointCloud2 native bridge reuses scan collections");
            VerifyBridgeScanCollections(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs",
                "140-72A-4: transform native bridge reuses scan collections");
        }

        private static void VerifyBridgeScanCollections(string relativePath, string label)
        {
            var source = Read(relativePath);
            Check(source.Contains("readonly HashSet<int>", StringComparison.Ordinal)
                  && source.Contains("readonly List<int>", StringComparison.Ordinal)
                  && source.Contains(".Clear();", StringComparison.Ordinal)
                  && !source.Contains("var seen = new HashSet<int>();", StringComparison.Ordinal)
                  && !source.Contains("var stale = new List<int>();", StringComparison.Ordinal),
                label);
        }

        private static void VerifyCameraScanReusesPublisherDiscovery()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");
            var refreshBindings = Slice(source, "private void RefreshBindings()", "        private void RefreshRawImageBindings");
            Check(refreshBindings.Contains("var cameraPublishers = FindObjectsByType<FoxgloveCameraPublisher>", StringComparison.Ordinal)
                  && refreshBindings.Contains("RefreshImageBindings(cameraPublishers)", StringComparison.Ordinal)
                  && refreshBindings.Contains("RefreshRawImageBindings(cameraPublishers)", StringComparison.Ordinal),
                "140-72B-1: camera bridge discovers FoxgloveCameraPublisher once per scan");

            Check(CountOccurrences(source, "FindObjectsByType<FoxgloveCameraPublisher>") == 1,
                "140-72B-2: camera bridge no longer performs duplicate camera publisher discovery");
        }

        private static void VerifyOwnershipRiskOptimizationsRemainDeferred()
        {
            var cameraInfo = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraInfoBinding.cs");
            var pointCloudBridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs");
            var transformBridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs");
            Check(cameraInfo.Contains("Transforms = new[]", StringComparison.Ordinal)
                  && pointCloudBridge.Contains("Transforms = new[]", StringComparison.Ordinal)
                  && transformBridge.Contains("Transforms = new[]", StringComparison.Ordinal),
                "140-72C-1: TFMessage transform arrays remain per-message until R2FU publish ownership is proven");

            var builder = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2MessageBuilder.cs");
            Check(builder.Contains("new sensor_msgs.msg.PointField[packedFields.Count]", StringComparison.Ordinal),
                "140-72C-2: PointField arrays remain per-message until R2FU publish ownership is proven");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_72Validation.cs", StringComparison.Ordinal),
                "140-72D-1: test project compiles Phase140_72Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-72\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_72Validation.Validate", StringComparison.Ordinal),
                "140-72D-2: validation registry exposes --phase140-72");
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

        private static int CountOccurrences(string source, string text)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(text, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += text.Length;
            }
            return count;
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
