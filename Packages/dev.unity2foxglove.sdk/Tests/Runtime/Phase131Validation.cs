// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 131 ROS2 standard visualization productization gate validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase131Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string SampleName = "RViz2 Standard Visualization v1";
        private const string SamplePath = OptionalPackage + "/Samples~/RViz2 Standard Visualization v1";
        private const string SampleReadmePath = SamplePath + "/README.md";
        private const string RvizConfigPath = SamplePath + "/rviz2_phase131_standard_visualization.rviz";
        private const string EvidenceTemplatePath = SamplePath + "/phase131_standard_visualization_evidence_template.md";
        private const string AcceptanceScriptPath = "Scripts/smoke/phase131_standard_visualization_acceptance.py";
        private const string RvizLauncherScriptPath = "Scripts/smoke/launch_phase131_rviz2.py";
        private const string SharedHelperPath = "Scripts/smoke/_ros2_windows_env.py";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 131: ROS2 Standard Visualization Productization Gate ===");
            _passed = 0;

            VerifyPackageSampleMetadata();
            VerifyConsolidatedKitFiles();
            VerifyRvizConfig();
            VerifyAcceptanceHelper();
            VerifyReadmeAndEvidenceTemplate();
            VerifyReleaseValidatorAcceptsV1SampleSet();
            VerifyPhaseWiringAndRegistry();
            VerifyCoreAndOptionalRuntimeBoundaries();

            Console.WriteLine($"Phase 131: {_passed} checks passed.");
        }

        private static void VerifyPackageSampleMetadata()
        {
            using var document = JsonDocument.Parse(ReadRepoText(OptionalPackage + "/package.json"));
            var root = document.RootElement;
            Check(root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array,
                "131A-1: optional package declares package samples");

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

            Check(sample.HasValue, "131A-2: optional package sample entry exists for RViz2 v1 kit");
            if (!sample.HasValue)
                return;

            var value = sample.Value;
            Check(value.TryGetProperty("path", out var path)
                  && path.GetString() == "Samples~/RViz2 Standard Visualization v1",
                "131A-3: optional package sample entry points at the RViz2 v1 kit");
            Check(value.TryGetProperty("description", out var description)
                  && AllTokens(description.GetString() ?? string.Empty, "/tf", "/scan", "/points", "/markers"),
                "131A-4: optional package sample description names all v1 topics");
        }

        private static void VerifyConsolidatedKitFiles()
        {
            foreach (var path in new[]
                     {
                         SampleReadmePath,
                         RvizConfigPath,
                         EvidenceTemplatePath,
                         AcceptanceScriptPath,
                         RvizLauncherScriptPath,
                         SharedHelperPath
                     })
            {
                Check(RepoFileExists(path), "131B-1: Phase131 file exists: " + path);
            }
        }

        private static void VerifyRvizConfig()
        {
            var rviz = ReadRepoText(RvizConfigPath);

            Check(rviz.Contains("Fixed Frame: map", StringComparison.Ordinal)
                  && AllTokens(rviz, "/tf", "/scan", "/points", "/markers")
                  && rviz.Contains("rviz_default_plugins/TF", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/LaserScan", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/PointCloud2", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/MarkerArray", StringComparison.Ordinal),
                "131C-1: RViz2 config references map and all v1 displays");
            Check(!ContainsAny(rviz, new[]
                  {
                      "CameraInfo",
                      "CompressedImage",
                      "NavSatFix",
                      "Odometry",
                      "PoseStamped",
                      "rviz_default_plugins/Image",
                      "sensor_msgs/msg/Image",
                      "MCAP",
                      "rosbag2"
                  }),
                "131C-2: RViz2 config avoids deferred message families and workflows");
        }

        private static void VerifyAcceptanceHelper()
        {
            var helper = ReadRepoText(AcceptanceScriptPath);
            var launcher = ReadRepoText(RvizLauncherScriptPath);
            var shared = ReadRepoText(SharedHelperPath);

            Check(helper.Contains("import _ros2_windows_env as ros2env", StringComparison.Ordinal)
                  && shared.Contains("def build_ros_env", StringComparison.Ordinal)
                  && shared.Contains("def validate_ros2_root", StringComparison.Ordinal)
                  && shared.Contains("def run_ros2", StringComparison.Ordinal)
                  && shared.Contains("def launch_rviz", StringComparison.Ordinal),
                "131D-1: helper uses shared Windows ROS2 environment module");
            Check(helper.Contains("ros2-script.py", StringComparison.Ordinal)
                  && helper.Contains("ros2env.DEFAULT_ROS2_ROOT", StringComparison.Ordinal)
                  && shared.Contains(@"C:\ros2_jazzy\ros2-windows", StringComparison.Ordinal)
                  && shared.Contains("ros2-script.py", StringComparison.Ordinal)
                  && shared.Contains(".pixi", StringComparison.Ordinal),
                "131D-2: helper uses pinned Windows Jazzy pixi Python and ros2-script.py");
            Check(AllTokens(helper, "/tf", "/scan", "/points", "/markers")
                  && AllTokens(helper, "tf2_msgs/msg/TFMessage", "sensor_msgs/msg/LaserScan", "sensor_msgs/msg/PointCloud2", "visualization_msgs/msg/MarkerArray")
                  && helper.Contains("unity2foxglove_phase128_rviz2", StringComparison.Ordinal)
                  && helper.Contains("unity2foxglove_phase129_pointcloud2", StringComparison.Ordinal)
                  && helper.Contains("unity2foxglove_phase130_markerarray", StringComparison.Ordinal),
                "131D-3: helper validates all v1 topics, message types, and publisher nodes");
            Check(shared.Contains("--once", StringComparison.Ordinal)
                  && shared.Contains("--spin-time", StringComparison.Ordinal)
                  && helper.Contains("validate_scan_echo", StringComparison.Ordinal)
                  && helper.Contains("validate_pointcloud2_echo", StringComparison.Ordinal)
                  && helper.Contains("validate_markerarray_echo", StringComparison.Ordinal)
                  && helper.Contains("echo_until_tokens", StringComparison.Ordinal),
                "131D-4: helper performs bounded representative echo validation");
            Check(helper.Contains("--launch-rviz", StringComparison.Ordinal)
                  && helper.Contains("--no-launch-rviz", StringComparison.Ordinal)
                  && helper.Contains("parser.set_defaults(launch_rviz=True)", StringComparison.Ordinal)
                  && helper.Contains("--rviz-config", StringComparison.Ordinal)
                  && helper.Contains("--rmw", StringComparison.Ordinal)
                  && helper.Contains("--discovery-range", StringComparison.Ordinal)
                  && shared.Contains("rviz2.exe", StringComparison.Ordinal)
                  && shared.Contains("rviz_ogre_vendor", StringComparison.Ordinal)
                  && shared.Contains("gz_math_vendor", StringComparison.Ordinal)
                  && !helper.Contains("\"run\", \"rviz2\"", StringComparison.Ordinal),
                "131D-5: helper supports RMW/discovery selection and direct rviz2.exe launch");
            var launchCallIndex = helper.IndexOf("launch_rviz_before_echo(\n        args.launch_rviz", StringComparison.Ordinal);
            var nodeListIndex = helper.IndexOf("print(\"--- node list", StringComparison.Ordinal);
            Check(RepoFileExists(RvizLauncherScriptPath)
                  && helper.Contains("launch_rviz_before_echo", StringComparison.Ordinal)
                  && launchCallIndex >= 0
                  && nodeListIndex >= 0
                  && launchCallIndex < nodeListIndex,
                "131D-5b: helper and launcher can open RViz2 before flaky graph and echo validation");
            Check(helper.Contains("--rviz-window-wait-seconds", StringComparison.Ordinal)
                  && helper.Contains("--rviz-startup-check-seconds", StringComparison.Ordinal)
                  && launcher.Contains("--rviz-window-wait-seconds", StringComparison.Ordinal)
                  && launcher.Contains("--rviz-startup-check-seconds", StringComparison.Ordinal)
                  && shared.Contains("def log_event", StringComparison.Ordinal)
                  && shared.Contains("visible_windows_for_pid", StringComparison.Ordinal)
                  && shared.Contains("GetWindowThreadProcessId", StringComparison.Ordinal),
                "131D-5c: helper timestamps RViz2 startup and measures visible-window readiness");
            Check(shared.Contains("startup_check_seconds", StringComparison.Ordinal)
                  && shared.Contains("process.poll()", StringComparison.Ordinal)
                  && shared.Contains("RViz2 exited immediately", StringComparison.Ordinal),
                "131D-6: shared helper detects immediate RViz2 launch failures instead of reporting a dead pid as launched");
            Check(shared.Contains("poll_interval_seconds", StringComparison.Ordinal)
                  && shared.Contains("remaining = deadline - time.monotonic()", StringComparison.Ordinal)
                  && shared.Contains("min(5.0, remaining)", StringComparison.Ordinal),
                "131D-7: shared publisher wait loop is deadline-aware and avoids fixed long sleeps");
            foreach (var path in new[]
                     {
                         "Scripts/smoke/phase128_rviz2_acceptance.py",
                         "Scripts/smoke/phase129_pointcloud2_acceptance.py",
                         "Scripts/smoke/phase130_markerarray_acceptance.py",
                         "Scripts/smoke/phase131_standard_visualization_acceptance.py",
                         "Scripts/smoke/phase132_standard_messages_acceptance.py"
                     })
            {
                var smokeHelper = ReadRepoText(path);
                Check(smokeHelper.Contains("import _ros2_windows_env as ros2env", StringComparison.Ordinal)
                      && !ContainsAny(smokeHelper, new[]
                      {
                          "def build_ros_env(",
                          "def validate_ros2_root(",
                          "def run_ros2(",
                          "def probe_node_list(",
                          "def probe_topic_info(",
                          "def wait_for_publisher(",
                          "def echo_once(",
                          "def launch_rviz("
                      }),
                    "131D-8: standard visualization helper reuses shared ROS2 env module: " + path);
            }
        }

        private static void VerifyReadmeAndEvidenceTemplate()
        {
            var optionalReadme = ReadRepoText(OptionalPackage + "/README.md");
            var sampleReadme = ReadRepoText(SampleReadmePath);
            var evidence = ReadRepoText(EvidenceTemplatePath);
            var pointcloudReadme = ReadRepoText(OptionalPackage + "/Samples~/RViz2 PointCloud2 Acceptance/README.md");
            var combined = optionalReadme + "\n" + sampleReadme + "\n" + evidence + "\n" + pointcloudReadme;

            Check(optionalReadme.Contains("RViz2 Standard Visualization v1", StringComparison.Ordinal)
                  && AllTokens(optionalReadme, "/tf", "/scan", "/points", "/markers")
                  && optionalReadme.Contains("core SDK remains ROS-free", StringComparison.OrdinalIgnoreCase),
                "131E-1: optional package README documents the v1 topic matrix and core boundary");
            Check(sampleReadme.Contains("not a publisher sample by itself", StringComparison.Ordinal)
                  && sampleReadme.Contains("Import the three publisher samples", StringComparison.Ordinal)
                  && sampleReadme.Contains("RViz2 Standard Visualization Acceptance", StringComparison.Ordinal)
                  && sampleReadme.Contains("RViz2 PointCloud2 Acceptance", StringComparison.Ordinal)
                  && sampleReadme.Contains("RViz2 MarkerArray Acceptance", StringComparison.Ordinal),
                "131E-2: sample README explains consolidated kit boundary and required publisher imports");
            Check(sampleReadme.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && sampleReadme.Contains("external ROS2 For Unity", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("python Scripts\\smoke\\phase131_standard_visualization_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("--ros2-root C:\\ros2_jazzy\\ros2-windows", StringComparison.Ordinal)
                  && sampleReadme.Contains("--no-launch-rviz", StringComparison.Ordinal)
                  && sampleReadme.Contains("timestamped RViz2 startup diagnostics", StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2-script.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("rviz2.exe", StringComparison.Ordinal),
                "131E-3: sample README includes canonical Python helper command and Windows path rules");
            Check(sampleReadme.Contains("Use bare ROS2 commands only after", StringComparison.Ordinal)
                  && AllTokens(sampleReadme,
                      "ros2 topic info /tf",
                      "ros2 topic info /scan",
                      "ros2 topic info /points",
                      "ros2 topic info /markers",
                      "ros2 topic echo --once /tf",
                      "ros2 topic echo --once /scan",
                      "ros2 topic echo --once /points",
                      "ros2 topic echo --once /markers"),
                "131E-4: sample README keeps bare ROS2 commands as secondary diagnostics");
            Check(sampleReadme.Contains("single owner", StringComparison.Ordinal)
                  && sampleReadme.Contains("map -> base_link", StringComparison.Ordinal)
                  && sampleReadme.Contains("Publish Shared Base Tf", StringComparison.Ordinal)
                  && pointcloudReadme.Contains("Publish Shared Base Tf", StringComparison.Ordinal),
                "131E-5: docs include the single-TF-owner rule and PointCloud2 shared-base toggle");
            Check(sampleReadme.Contains("This kit does not bump the package version", StringComparison.Ordinal)
                  && sampleReadme.Contains("release tag", StringComparison.Ordinal),
                "131E-6: docs state version/tag handling remains a separate release process");
            Check(AllTokens(evidence,
                      "Commit hash",
                      "Package version",
                      "Unity version",
                      "ROS2 distro",
                      "RMW implementation",
                      "RViz2 version",
                      "/tf",
                      "/scan",
                      "/points",
                      "/markers",
                      "Screenshot",
                      "Verdict"),
                "131E-7: evidence template captures environment, v1 topics, screenshots, and verdict");
            Check(AllTokens(evidence,
                      "PASS",
                      "PASS WITH NOTED LIMITATIONS",
                      "BLOCKED",
                      "SKIPPED")
                  && !evidence.Contains("SKIPPED LIVE", StringComparison.Ordinal),
                "131E-8: evidence template uses the same verdict vocabulary as the v1 README");
            Check(!ContainsAny(combined, new[]
                  {
                      "CameraInfo is supported",
                      "raw Image is supported",
                      "CompressedImage is supported",
                      "IMU is supported",
                      "Odometry is supported",
                      "NavSatFix is supported",
                      "MCAP replay fanout is supported",
                      "rosbag2 interop is supported",
                      "all marker types are supported"
                  }),
                "131E-9: docs do not over-claim deferred standard message families");
        }

        private static void VerifyReleaseValidatorAcceptsV1SampleSet()
        {
            var script = ReadRepoText("Scripts/release/validate_ros2forunity_package.py");
            Check(script.Contains("RVIZ_V1_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("RViz2 Standard Visualization v1", StringComparison.Ordinal)
                  && script.Contains("len(samples) >=", StringComparison.Ordinal)
                  && AllTokens(script, "RVIZ_SAMPLE", "RVIZ_POINTCLOUD2_SAMPLE", "RVIZ_MARKERARRAY_SAMPLE"),
                "131F-1: release validator accepts the RViz2 v1 sample set");
        }

        private static void VerifyPhaseWiringAndRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");

            Check(AllTokens(registry, "Ci(\"--phase128\"", "Ci(\"--phase129\"", "Ci(\"--phase130\"", "Ci(\"--phase131\"")
                  && AllTokens(registry, "Phase128Validation.Validate", "Phase129Validation.Validate", "Phase130Validation.Validate", "Phase131Validation.Validate"),
                "131G-1: validation registry keeps --phase128 through --phase131 wired");
            Check(project.Contains("Phase131Validation.cs", StringComparison.Ordinal),
                "131G-2: test project compiles Phase131Validation");
            Check(program.Contains("TryRunRegisteredValidation", StringComparison.Ordinal)
                  && program.Contains("PhaseValidationRegistry.Find", StringComparison.Ordinal),
                "131G-3: Program dispatches registered validation flags through the registry");
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
                "131H-1: core SDK production surface has no hard R2FU or standard ROS2 message references"
                + (coreHits.Count == 0 ? string.Empty : " (" + string.Join(", ", coreHits) + ")"));

            var optionalRuntimeHits = ExistingTextFilesOrSingleFile(OptionalPackage + "/Runtime")
                .SelectMany(path => OptionalRuntimeForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(optionalRuntimeHits.Count == 0,
                "131H-2: optional package Runtime remains facade-only with no R2FU/message references"
                + (optionalRuntimeHits.Count == 0 ? string.Empty : " (" + string.Join(", ", optionalRuntimeHits) + ")"));
        }

        private static IEnumerable<string> CoreProductionForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "tf2_msgs.msg",
                "sensor_msgs.msg",
                "visualization_msgs.msg"
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
                "sensor_msgs",
                "visualization_msgs"
            };
        }

        private static bool AllTokens(string text, params string[] tokens)
        {
            return tokens.All(token => text.Contains(token, StringComparison.Ordinal));
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
                throw new FileNotFoundException("Missing required Phase131 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
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
