// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139 validation for end-to-end smoke harness contracts.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// CI-safe checks for the Phase 139 end-to-end smoke script and validation
    /// boundary. Live Unity, ROS2, RViz2, and MCAP evidence remain manual/local.
    /// </summary>
    public static class Phase139Validation
    {
        private const string ScriptPath = "Scripts/smoke/phase139_e2e_integration_smoke.py";
        private const string ForbiddenPowerShellWrapper = "Scripts/smoke/phase139_e2e_integration_smoke.ps1";

        private static readonly string CachedRepoRoot = ResolveRepoRoot();
        private static readonly Dictionary<string, string> SourceCache = new Dictionary<string, string>();

        private static int _passed;

        /// <summary>Runs all Phase 139 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 139: End-to-End Integration Smoke Harness ===");
            _passed = 0;

            VerifySmokeScriptSurface();
            VerifySmokeScriptSelfTest();
            VerifyValidationWiring();
            VerifyMazeReplayAdapterWiring();

            Console.WriteLine($"Phase 139: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void VerifySmokeScriptSurface()
        {
            Check(File.Exists(RepoPath(ScriptPath)), "139-1A: Phase139 Python smoke script exists");
            Check(!File.Exists(RepoPath(ForbiddenPowerShellWrapper)), "139-1B: Phase139 does not add a project PowerShell wrapper");

            var script = Read(ScriptPath);
            Check(script.Contains("foxglove.sdk.v1", StringComparison.Ordinal),
                "139-1C: smoke script uses Foxglove WebSocket subprotocol");
            Check(script.Contains("--mode", StringComparison.Ordinal)
                  && script.Contains("websocket-core", StringComparison.Ordinal)
                  && script.Contains("ros2-native", StringComparison.Ordinal),
                "139-1D: smoke script exposes websocket-core and optional ros2-native modes");
            Check(script.Contains("--scenario", StringComparison.Ordinal)
                  && script.Contains("maze-websocket-default", StringComparison.Ordinal),
                "139-1E: smoke script exposes scenario selection for the default Maze demo");
            Check(script.Contains("--json-out", StringComparison.Ordinal)
                  && script.Contains("--self-test", StringComparison.Ordinal),
                "139-1F: smoke script exposes JSON output and offline self-test");
            Check(script.Contains("/imu/data", StringComparison.Ordinal)
                  && script.Contains("/unity/point_cloud2", StringComparison.Ordinal)
                  && script.Contains("/unity/point_cloud2_deskewed", StringComparison.Ordinal)
                  && script.Contains("/unity/sensor/camera/image", StringComparison.Ordinal),
                "139-1G: smoke script documents current 138S/T/U product topics");
            Check(!script.Contains("\"/scan\"", StringComparison.Ordinal)
                  && !script.Contains("\"/points\"", StringComparison.Ordinal)
                  && !script.Contains("\"/markers\"", StringComparison.Ordinal),
                "139-1H: smoke script does not use legacy ROS visualization topics as defaults");
            Check(script.Contains("_ros2_windows_env", StringComparison.Ordinal)
                  && script.Contains("rclpy", StringComparison.Ordinal),
                "139-1I: optional DDS path uses shared Jazzy helper and direct rclpy subscribers");
            Check(script.Contains("not SLAM input", StringComparison.Ordinal)
                  || script.Contains("not a SLAM input", StringComparison.Ordinal),
                "139-1J: smoke script documents deskewed PointCloud2 as visualization-only");
            Check(!script.Contains("Phase 102", StringComparison.Ordinal)
                  && !script.Contains("Phase102", StringComparison.Ordinal),
                "139-1K: smoke script does not present old Phase102 backend as active route");
        }

        private static void VerifySmokeScriptSelfTest()
        {
            var output = RunPythonSelfTest();
            Check(output.Contains("\"phase\": \"139\"", StringComparison.Ordinal),
                "139-2A: self-test emits phase 139 JSON");
            Check(output.Contains("\"mode\": \"self-test\"", StringComparison.Ordinal),
                "139-2B: self-test reports self-test mode");
            Check(output.Contains("\"status\": \"pass\"", StringComparison.Ordinal),
                "139-2C: self-test reports pass status");
            Check(output.Contains("\"/tf\"", StringComparison.Ordinal)
                  && output.Contains("\"/imu/data\"", StringComparison.Ordinal),
                "139-2D: self-test exercises default WebSocket topic summary");
            Check(output.Contains("\"classification\": \"required\"", StringComparison.Ordinal)
                  && output.Contains("\"classification\": \"optional\"", StringComparison.Ordinal),
                "139-2E: self-test covers required and optional topic classification");
        }

        private static void VerifyValidationWiring()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase139\"", StringComparison.Ordinal),
                "139-3A: registry wires --phase139");
            Check(registry.Contains("Phase139Validation.Validate", StringComparison.Ordinal),
                "139-3B: registry points Phase139 at the validation entrypoint");
            Check(project.Contains("Phase139Validation.cs", StringComparison.Ordinal),
                "139-3C: test project compiles Phase139Validation");
        }

        private static void VerifyMazeReplayAdapterWiring()
        {
            VerifyMazeReplayAdapterSource(
                "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs",
                "139-4A");
            VerifyMazeReplayAdapterSource(
                "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs",
                "139-4B");
        }

        private static void VerifyMazeReplayAdapterSource(string path, string labelPrefix)
        {
            var source = Read(path);
            Check(source.Contains("FoxgloveReplayObjectAdapter", StringComparison.Ordinal),
                $"{labelPrefix}-1: Maze demo wires replay object adapter");
            Check(source.Contains("_frameOverrides", StringComparison.Ordinal)
                  && source.Contains("base_link", StringComparison.Ordinal)
                  && source.Contains("os_sensor", StringComparison.Ordinal)
                  && source.Contains("os_lidar", StringComparison.Ordinal)
                  && source.Contains("os_imu", StringComparison.Ordinal)
                  && source.Contains("os_camera", StringComparison.Ordinal),
                $"{labelPrefix}-2: Maze demo maps replay TF frames to generated objects");
        }

        private static string RunPythonSelfTest()
        {
            var python = ResolvePythonExecutable();
            var start = new ProcessStartInfo(python, $"{Quote(RepoPath(ScriptPath))} --self-test")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process;
            try
            {
                process = Process.Start(start);
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "Phase139 Python self-test could not start '" + python + "'. " +
                    "Install Python or set PYTHON to a valid executable before running --phase139.",
                    ex);
            }

            if (process == null)
                throw new InvalidOperationException(
                    "Phase139 Python self-test could not start '" + python + "'. "
                    + "Install Python or set PYTHON to a valid executable before running --phase139.");
            using (process)
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(10_000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    process.WaitForExit(5_000);
                    throw new InvalidOperationException(
                        "Phase139 Python self-test timed out."
                        + "\nstdout:\n" + ReadCompletedOutput(outputTask)
                        + "\nstderr:\n" + ReadCompletedOutput(errorTask));
                }

                var output = outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Phase139 Python self-test failed: " + output + error);

                return output;
            }
        }

        private static string ReadCompletedOutput(Task<string> task)
        {
            return task.IsCompleted ? task.GetAwaiter().GetResult() : string.Empty;
        }

        private static string ResolvePythonExecutable()
        {
            var configured = Environment.GetEnvironmentVariable("PYTHON");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            return Environment.OSVersion.Platform == PlatformID.Win32NT ? "python" : "python3";
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        private static string Read(string relativePath)
        {
            if (SourceCache.TryGetValue(relativePath, out var cached))
                return cached;

            var text = File.ReadAllText(RepoPath(relativePath));
            SourceCache[relativePath] = text;
            return text;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot() => CachedRepoRoot;

        private static string ResolveRepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase139 validation.");
            return root;
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
