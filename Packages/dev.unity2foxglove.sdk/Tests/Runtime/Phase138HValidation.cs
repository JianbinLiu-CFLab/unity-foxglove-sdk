// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138H validation for shared timeline and streaming LiDAR scan state.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression checks for 138H LiDAR-IMU timeline alignment and streaming scan
    /// behavior.
    /// </summary>
    public static class Phase138HValidation
    {
        private const string VirtualLidarRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs";
        private const string FoxgloveManagerRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs";
        private const string LidarModelSpecRelativePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarModelSpec.cs";
        private const string DemoEditorRelativePath =
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoSceneBuilder.cs";
        private const string DemoEditorFileName = "Phase138MazeDemoSceneBuilder.cs";

        /// <summary>Run all Phase 138H checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138H: LiDAR-IMU Time Sync + Streaming Scan ===");

            VerifySharedSensorClock();
            VerifyStreamingLiDARState();
            VerifyPhase138hDemoHooks();

            Console.WriteLine("Phase 138H: all checks passed.");
            Console.WriteLine();
        }

        private static void VerifySharedSensorClock()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var managerSource = ReadText(repoRoot, FoxgloveManagerRelativePath);

            Check(managerSource.Contains("GetSharedSensorClockUnixTime"),
                "138H-1: FoxgloveManager exposes shared sensor clock API");
            Check(Regex.IsMatch(managerSource,
                        @"_sensorClockInitialized|_sensorClockEpochUnixNs|_sensorClockEpochPhysSeconds"),
                    "138H-2: FoxgloveManager stores shared clock epoch state");
        }

        private static void VerifyStreamingLiDARState()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var source = ReadText(repoRoot, VirtualLidarRelativePath);

            Check(source.Contains("_scanSubSteps"), "138H-3: VirtualLidar exposes configurable _scanSubSteps");
            Check(source.Contains("_activeScanStartPhysSeconds"),
                "138H-4: VirtualLidar tracks each scan's physical-start epoch for shared timing");
            Check(source.Contains("_scanColumnProgress") && source.Contains("subStepsPerScan"),
                "138H-5: VirtualLidar advances streaming scans by scan sub-steps");
            Check(source.Contains("StartNewScan(_activeScanStartPhysSeconds + _scanPeriod)"),
                "138H-6: VirtualLidar restarts each completed scan from prior scan start + period");
            Check(source.Contains("scanSubStepOffset = _scanColumnCursor % subStepsPerScan")
                && source.Contains("Math.Min(1f, timeOffset + subStepOffset)"),
                "138H-7: per-sub-step time offset adds sub-step phase inside scan columns");
            Check(source.Contains("RunStreamingColumn("),
                "138H-8: VirtualLidar has streaming column worker");
            Check(source.Contains("RaycastCommand.ScheduleBatch") &&
                  source.Contains("_pointCloudPublisher.SetFrame(_activeScanFrame)"),
                "138H-9: streaming worker writes managed frame buffer and publishes via publisher SetFrame");
        }

        private static void VerifyPhase138hDemoHooks()
        {
            var repoRoot = Phase16Validation.FindRepoRoot();
            var specSource = ReadText(repoRoot, LidarModelSpecRelativePath);
            var demoEditorSource = ResolveDemoBuilderSource(repoRoot);

            Check(Regex.IsMatch(specSource, @"TIlTranslationMeters"),
                "138H-10: LidarModelSpec includes T_IL translation field");
            Check(Regex.IsMatch(specSource, @"TIlRotation"),
                "138H-11: LidarModelSpec includes T_IL rotation field");
            Check(demoEditorSource.Contains("ApplyImuMountTransform"),
                "138H-12: Demo builder applies IMU mount transform from model spec data");
        }

        private static string ReadText(string repoRoot, string relativePath)
        {
            var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new InvalidOperationException($"Phase 138H cannot find expected file: {path}");
            return File.ReadAllText(path);
        }

        private static string ResolveDemoBuilderSource(string repoRoot)
        {
            try
            {
                var literal = Path.Combine(repoRoot, DemoEditorRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(literal))
                    return File.ReadAllText(literal);
            }
            catch (Exception)
            {
                // Ignore and fall back to filename search for environments where relative
                // paths with `~` or spaces are normalized differently.
            }

            var matches = Directory.GetFiles(repoRoot, DemoEditorFileName, SearchOption.AllDirectories);
            var packageMatches = Array.FindAll(matches, path =>
                path.Replace('\\', '/').Contains("/Packages/dev.unity2foxglove.sdk/Samples~/", StringComparison.OrdinalIgnoreCase));
            if (packageMatches.Length == 1)
                return File.ReadAllText(packageMatches[0]);
            if (packageMatches.Length > 1)
                throw new InvalidOperationException(
                    $"Phase 138H cannot disambiguate demo source file: {DemoEditorRelativePath} ({packageMatches.Length} package matches)");

            if (matches.Length == 1)
                return File.ReadAllText(matches[0]);

            throw new InvalidOperationException(
                $"Phase 138H cannot find expected file: {DemoEditorRelativePath} (found {matches.Length} matches)");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException($"Phase 138H validation failed: {label}");
            Console.WriteLine($"[PASS] {label}");
        }
    }
}
