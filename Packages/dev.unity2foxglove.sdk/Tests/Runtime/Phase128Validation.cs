// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 128 RViz2 standard visualization acceptance kit validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase128Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string RuntimePackage = "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
        private const string SampleName = "RViz2 Standard Visualization Acceptance";
        private const string SamplePath = OptionalPackage + "/Samples~/RViz2 Standard Visualization Acceptance";
        private const string SampleReadmePath = SamplePath + "/README.md";
        private const string SampleScriptPath = SamplePath + "/Phase128Rviz2TfLaserScanSmoke.cs";
        private const string RvizConfigPath = SamplePath + "/rviz2_phase128_tf_laserscan.rviz";
        private const string EvidenceTemplatePath = SamplePath + "/phase128_rviz2_evidence_template.md";
        private const string AcceptanceScriptPath = "Scripts/smoke/phase128_rviz2_acceptance.py";
        private const string RvizLauncherPath = "Scripts/smoke/launch_phase128_rviz2.py";
        private const string SharedHelperPath = "Scripts/smoke/_ros2_windows_env.py";
        private const string Define = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";

        private static int _passed;
        private static string _repoRoot;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 128: RViz2 Standard Visualization Acceptance v1 ===");
            _passed = 0;

            VerifyPackageSampleMetadata();
            VerifySampleFiles();
            VerifySampleScript();
            VerifyRvizConfig();
            VerifyAcceptanceHelper();
            VerifyDocsAndEvidenceTemplate();
            VerifyCoreAndOptionalRuntimeBoundaries();
            VerifyValidationWiring();
            VerifyRuntimePackageIdentityStillPresent();

            Console.WriteLine($"Phase 128: {_passed} checks passed.");
        }

        private static void VerifyPackageSampleMetadata()
        {
            using var document = JsonDocument.Parse(ReadRepoText(OptionalPackage + "/package.json"));
            var root = document.RootElement;
            Check(root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array,
                "128A-1: optional package declares package samples");

            JsonElement? sample = null;
            foreach (var entry in samples.EnumerateArray())
            {
                if (entry.TryGetProperty("displayName", out var displayName)
                    && displayName.GetString() == SampleName)
                {
                    sample = entry;
                    break;
                }
            }

            Check(sample.HasValue, "128A-2: optional package sample entry exists for RViz2 acceptance");
            if (!sample.HasValue)
                return;

            var value = sample.Value;
            Check(value.TryGetProperty("path", out var path)
                  && path.GetString() == "Samples~/RViz2 Standard Visualization Acceptance",
                "128A-3: optional package sample entry points at the RViz2 acceptance sample");
            Check(value.TryGetProperty("description", out var description)
                  && (description.GetString() ?? string.Empty).Contains("/tf", StringComparison.Ordinal)
                  && (description.GetString() ?? string.Empty).Contains("/scan", StringComparison.Ordinal),
                "128A-4: optional package sample description names /tf and /scan");
        }

        private static void VerifySampleFiles()
        {
            foreach (var path in new[] { SampleReadmePath, SampleScriptPath, RvizConfigPath, EvidenceTemplatePath })
                Check(RepoFileExists(path), "128B-1: sample file exists: " + path);
        }

        private static void VerifySampleScript()
        {
            var script = ReadRepoText(SampleScriptPath);

            Check(script.Contains(Define, StringComparison.Ordinal)
                  && AllR2fuReferencesAreGuarded(script),
                "128C-1: sample script guards ROS2 For Unity and generated message references");
            Check(script.Contains("NodeName = \"unity2foxglove_phase128_rviz2\"", StringComparison.Ordinal)
                  && script.Contains("TfTopic = \"/tf\"", StringComparison.Ordinal)
                  && script.Contains("ScanTopic = \"/scan\"", StringComparison.Ordinal),
                "128C-2: sample script uses the required node and topics");
            Check(script.Contains("GetComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && script.Contains("AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && script.Contains(".Ok()", StringComparison.Ordinal),
                "128C-3: sample script finds or adds ROS2UnityComponent and waits for Ok()");
            Check(script.Contains("CreatePublisher<tf2_msgs.msg.TFMessage>", StringComparison.Ordinal)
                  && script.Contains("CreatePublisher<sensor_msgs.msg.LaserScan>", StringComparison.Ordinal)
                  && !script.Contains("QualityOfServiceProfile", StringComparison.Ordinal),
                "128C-4: sample script publishes TFMessage and LaserScan with default R2FU QoS");
            Check(script.Contains("FrameMap = \"map\"", StringComparison.Ordinal)
                  && script.Contains("FrameBaseLink = \"base_link\"", StringComparison.Ordinal)
                  && script.Contains("FrameLaser = \"laser\"", StringComparison.Ordinal)
                  && script.Contains("Child_frame_id = FrameBaseLink", StringComparison.Ordinal)
                  && script.Contains("Child_frame_id = FrameLaser", StringComparison.Ordinal),
                "128C-5: sample script publishes map -> base_link -> laser TF frames");
            Check(script.Contains("Header = CreateHeader(FrameLaser", StringComparison.Ordinal)
                  && script.Contains("Angle_min", StringComparison.Ordinal)
                  && script.Contains("Angle_max", StringComparison.Ordinal)
                  && script.Contains("Angle_increment", StringComparison.Ordinal)
                  && script.Contains("Ranges = _ranges", StringComparison.Ordinal)
                  && script.Contains("IsFiniteRange", StringComparison.Ordinal)
                  && script.Contains("float.IsNaN", StringComparison.Ordinal)
                  && script.Contains("float.IsInfinity", StringComparison.Ordinal),
                "128C-6: sample script builds deterministic finite LaserScan data in laser frame");
            Check(script.Contains("CreateStamp", StringComparison.Ordinal)
                  && script.Contains("Stamp = stamp", StringComparison.Ordinal)
                  && script.Contains("Nanosec", StringComparison.Ordinal)
                  && script.Contains("ROS2 Time.sec is int32", StringComparison.Ordinal)
                  && script.Contains("Y2038", StringComparison.Ordinal)
                  && !script.Contains("\"/clock\"", StringComparison.Ordinal),
                "128C-7: sample script writes ROS-compatible timestamps, documents the ROS2 Time.sec limit, and does not publish /clock");
            Check(script.Contains("_runtimeRoot", StringComparison.Ordinal)
                  && script.Contains("_runtimeRootIsPackage", StringComparison.Ordinal)
                  && script.Contains("_assetRuntimePresent", StringComparison.Ordinal)
                  && script.Contains("_lastError", StringComparison.Ordinal)
                  && script.Contains("_publishedTfCount", StringComparison.Ordinal)
                  && script.Contains("_publishedScanCount", StringComparison.Ordinal),
                "128C-8: sample script exposes status, counters, runtime root, package mode, asset runtime, and errors");
            Check(script.Contains("Import ROS2 For Unity", StringComparison.Ordinal)
                  && script.Contains("Debug.LogWarning", StringComparison.Ordinal),
                "128C-9: sample script reports unavailable state clearly when the compile define is absent");
            Check(script.Contains("_warnedMissingStartExecutor", StringComparison.Ordinal)
                  && script.Contains("StartExecutor reflection hook was not found", StringComparison.Ordinal)
                  && script.Contains("continuing without explicit executor start", StringComparison.Ordinal),
                "128C-10: sample script warns once when the optional StartExecutor reflection hook is unavailable");
        }

        private static void VerifyRvizConfig()
        {
            var rviz = ReadRepoText(RvizConfigPath);
            Check(rviz.Contains("Fixed Frame: map", StringComparison.Ordinal)
                  && rviz.Contains("/tf", StringComparison.Ordinal)
                  && rviz.Contains("/scan", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/TF", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/LaserScan", StringComparison.Ordinal),
                "128D-1: RViz2 config targets map, TF, and /scan LaserScan");
            Check(!ContainsAny(rviz, new[]
                  {
                      "PointCloud2",
                      "MarkerArray",
                      "CameraInfo",
                      "rviz_default_plugins/Image",
                      "sensor_msgs/msg/Image"
                  }),
                "128D-2: RViz2 config avoids deferred displays");
        }

        private static void VerifyAcceptanceHelper()
        {
            var script = ReadRepoText(AcceptanceScriptPath);
            var launcher = ReadRepoText(RvizLauncherPath);
            var shared = ReadRepoText(SharedHelperPath);
            var helperSurface = script + "\n" + shared;
            var launcherSurface = launcher + "\n" + shared;

            Check(script.Contains("# Purpose:", StringComparison.Ordinal)
                  && script.Contains("argparse", StringComparison.Ordinal)
                  && script.Contains("phase128", StringComparison.Ordinal),
                "128E-1: Python acceptance helper has repository header and CLI entry point");
            Check(script.Contains("import _ros2_windows_env as ros2env", StringComparison.Ordinal)
                  && script.Contains("ros2env.DEFAULT_ROS2_ROOT", StringComparison.Ordinal)
                  && shared.Contains("ros2-script.py", StringComparison.Ordinal)
                  && shared.Contains(".pixi", StringComparison.Ordinal)
                  && shared.Contains(@"C:\ros2_jazzy\ros2-windows", StringComparison.Ordinal),
                "128E-2: helper uses pinned Windows Jazzy pixi Python and ros2-script.py");
            Check(shared.Contains("--no-daemon", StringComparison.Ordinal)
                  && shared.Contains("topic\", \"info\"", StringComparison.Ordinal)
                  && shared.Contains("\"-v\"", StringComparison.Ordinal)
                  && shared.Contains("node\", \"list\"", StringComparison.Ordinal),
                "128E-3: helper uses no-daemon graph checks, node list, and topic info -v");
            Check(script.Contains("unity2foxglove_phase128_rviz2", StringComparison.Ordinal)
                  && script.Contains("/tf", StringComparison.Ordinal)
                  && script.Contains("/scan", StringComparison.Ordinal)
                  && shared.Contains("Publisher count:", StringComparison.Ordinal)
                  && shared.Contains("Node name:", StringComparison.Ordinal)
                  && script.Contains("node_name=NODE_NAME", StringComparison.Ordinal),
                "128E-4: helper proves required publisher endpoints belong to the Phase128 node");
            Check(script.Contains("probe_node_list", StringComparison.Ordinal)
                  && script.Contains("node list did not include", StringComparison.Ordinal)
                  && script.Contains("continuing with publisher endpoint and echo checks", StringComparison.Ordinal)
                  && script.Contains("topic info -v /tf (diagnostic)", StringComparison.Ordinal),
                "128E-4b: helper treats flaky node list and /tf topic info as diagnostics instead of hard gates");
            Check(script.Contains("tf2_msgs/msg/TFMessage", StringComparison.Ordinal)
                  && script.Contains("sensor_msgs/msg/LaserScan", StringComparison.Ordinal)
                  && helperSurface.Contains("--once", StringComparison.Ordinal)
                  && helperSurface.Contains("--spin-time", StringComparison.Ordinal)
                  && script.Contains("map", StringComparison.Ordinal)
                  && script.Contains("base_link", StringComparison.Ordinal)
                  && script.Contains("laser", StringComparison.Ordinal)
                  && script.Contains("math.isfinite", StringComparison.Ordinal),
                "128E-5: helper echoes TF/LaserScan once with bounded spin time and content checks");
            Check(script.Contains("--launch-rviz", StringComparison.Ordinal)
                  && script.Contains("--rviz-config", StringComparison.Ordinal)
                  && shared.Contains("rviz2.exe", StringComparison.Ordinal)
                  && shared.Contains("rviz_ogre_vendor", StringComparison.Ordinal)
                  && shared.Contains("gz_math_vendor", StringComparison.Ordinal)
                  && !script.Contains("\"run\", \"rviz2\"", StringComparison.Ordinal),
                "128E-6: helper can optionally launch RViz2 through direct rviz2.exe with required DLL paths");
            Check(script.Contains("launch_rviz_before_echo", StringComparison.Ordinal)
                  && script.IndexOf("launch_rviz_before_echo", StringComparison.Ordinal)
                     < script.IndexOf("print(\"--- echo /tf ---\")", StringComparison.Ordinal),
                "128E-6b: helper launches RViz2 before bounded echo checks for faster manual feedback");
            Check(script.Contains("--rmw", StringComparison.Ordinal)
                  && helperSurface.Contains("rmw_implementation", StringComparison.Ordinal)
                  && shared.Contains("env.get(\"RMW_IMPLEMENTATION\")", StringComparison.Ordinal),
                "128E-7: helper lets manual acceptance select or preserve the RMW implementation");
            Check(script.Contains("--discovery-range", StringComparison.Ordinal)
                  && helperSurface.Contains("discovery_range", StringComparison.Ordinal)
                  && !script.Contains("env[\"ROS_AUTOMATIC_DISCOVERY_RANGE\"] = \"SUBNET\"", StringComparison.Ordinal),
                "128E-8: helper can override discovery range without forcing SUBNET by default");
            Check(launcher.Contains("# Purpose:", StringComparison.Ordinal)
                  && launcher.Contains("argparse", StringComparison.Ordinal)
                  && launcherSurface.Contains("subprocess.Popen", StringComparison.Ordinal)
                  && launcherSurface.Contains("rviz2.exe", StringComparison.Ordinal)
                  && launcherSurface.Contains("rviz_ogre_vendor", StringComparison.Ordinal)
                  && launcherSurface.Contains("gz_math_vendor", StringComparison.Ordinal)
                  && launcherSurface.Contains("ROS_AUTOMATIC_DISCOVERY_RANGE", StringComparison.Ordinal)
                  && launcher.Contains("--dry-run", StringComparison.Ordinal),
                "128E-9: Python RViz2 launcher replaces durable PowerShell launch asset");
        }

        private static void VerifyDocsAndEvidenceTemplate()
        {
            var optionalReadme = ReadRepoText(OptionalPackage + "/README.md");
            var sampleReadme = ReadRepoText(SampleReadmePath);
            var evidence = ReadRepoText(EvidenceTemplatePath);
            var combined = optionalReadme + "\n" + sampleReadme + "\n" + evidence;

            Check(optionalReadme.Contains("RViz2 Standard Visualization Acceptance", StringComparison.Ordinal)
                  && optionalReadme.Contains("/tf", StringComparison.Ordinal)
                  && optionalReadme.Contains("/scan", StringComparison.Ordinal),
                "128F-1: optional package README mentions the RViz2 acceptance kit");
            Check(sampleReadme.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && sampleReadme.Contains("external ROS2 For Unity", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("python Scripts\\smoke\\phase128_rviz2_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("--ros2-root C:\\ros2_jazzy\\ros2-windows", StringComparison.Ordinal)
                  && sampleReadme.Contains("--rviz-config", StringComparison.Ordinal)
                  && sampleReadme.Contains("ROS_AUTOMATIC_DISCOVERY_RANGE", StringComparison.Ordinal)
                  && sampleReadme.Contains("launch_phase128_rviz2.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2-script.py", StringComparison.Ordinal)
                  && !sampleReadme.Contains("\nros2 topic", StringComparison.Ordinal),
                "128F-2: sample README gives Python helper/launcher paths and does not make bare ros2 primary");
            Check(evidence.Contains("OS", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("Unity version", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("ROS2 distro", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RMW implementation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("ROS2 For Unity version/source", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("runtime mode", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("runtime root", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("Assets/Ros2ForUnity", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("node list", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("topic info -v /tf", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("topic info -v /scan", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("/tf echo", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("/scan echo", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RViz2 TF observation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RViz2 LaserScan observation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("verdict", StringComparison.OrdinalIgnoreCase),
                "128F-3: evidence template captures CLI, runtime, RViz2, screenshot, and verdict evidence");
            Check(!ContainsAny(combined, new[]
                  {
                      "supports PointCloud2",
                      "supports MarkerArray",
                      "supports CameraInfo",
                      "supports Image",
                      "supports MCAP replay fanout",
                      "supports rosbag2"
                  }),
                "128F-4: docs do not over-claim deferred ROS2 standard visualization support");
        }

        private static void VerifyCoreAndOptionalRuntimeBoundaries()
        {
            var coreProductionRoots = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime",
                "Packages/dev.unity2foxglove.sdk/Editor",
                "Packages/dev.unity2foxglove.sdk/Samples~"
            };

            var coreHits = coreProductionRoots
                .SelectMany(ExistingTextFilesOrSingleFile)
                .SelectMany(path => CoreProductionForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(coreHits.Count == 0,
                "128G-1: core SDK production surface has no hard R2FU or standard ROS2 message references"
                + (coreHits.Count == 0 ? string.Empty : " (" + string.Join(", ", coreHits) + ")"));

            var optionalRuntimeHits = ExistingTextFilesOrSingleFile(OptionalPackage + "/Runtime")
                .SelectMany(path => OptionalRuntimeForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(optionalRuntimeHits.Count == 0,
                "128G-2: optional package Runtime remains facade-only with no R2FU/message references"
                + (optionalRuntimeHits.Count == 0 ? string.Empty : " (" + string.Join(", ", optionalRuntimeHits) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase128\"", StringComparison.Ordinal)
                  && registry.Contains("Phase128Validation.Validate", StringComparison.Ordinal),
                "128H-1: validation registry wires --phase128");
            Check(project.Contains("Phase128Validation.cs", StringComparison.Ordinal),
                "128H-2: test project compiles Phase128Validation");
        }

        private static void VerifyRuntimePackageIdentityStillPresent()
        {
            var packageJson = ReadRepoText(RuntimePackage + "/package.json");
            var manifest = ReadRepoText(RuntimePackage + "/RuntimeSupport/runtime-manifest.json");

            Check(packageJson.Contains("dev.unity2foxglove.ros2forunity.runtime.jazzy.win64", StringComparison.Ordinal)
                  && manifest.Contains("22baf2b624b0fb171efc94b403876491a66e57b39b6f747a3c2e30644ce32188", StringComparison.Ordinal),
                "128I-1: Phase127 Jazzy runtime package identity and artifact hash remain intact");
        }

        private static IEnumerable<string> CoreProductionForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "tf2_msgs.msg",
                "sensor_msgs.msg"
            };
        }

        private static IEnumerable<string> OptionalRuntimeForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "namespace ROS2",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "ISubscription<",
                "tf2_msgs",
                "sensor_msgs"
            };
        }

        private static bool AllR2fuReferencesAreGuarded(string text)
        {
            return PhaseRos2ForUnityValidationHelpers.AllR2fuReferencesAreGuarded(
                text, Define, PhaseRos2ForUnityValidationHelpers.R2fuGuardTokens, out _);
        }

        private static bool ContainsAny(string text, IEnumerable<string> tokens)
        {
            return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> ExistingTextFilesOrSingleFile(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (File.Exists(path))
                return new[] { path };
            if (!Directory.Exists(path))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(IsTextFile)
                .ToArray();
        }

        private static bool IsTextFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase128 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            if (_repoRoot != null)
                return _repoRoot;
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            _repoRoot = root;
            return _repoRoot;
        }

        private static string Rel(string path)
        {
            return path.Replace(RepoRoot() + Path.DirectorySeparatorChar, string.Empty)
                .Replace('\\', '/');
        }

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException(description);

            _passed++;
            Console.WriteLine("[PASS] " + description);
        }
    }
}
