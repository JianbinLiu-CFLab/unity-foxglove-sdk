// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 132 ROS2 standard message expansion validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase132Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string SampleName = "ROS2 Standard Message Expansion";
        private const string SamplePath = OptionalPackage + "/Samples~/ROS2 Standard Message Expansion";
        private const string ReadmePath = SamplePath + "/README.md";
        private const string EvidenceTemplatePath = SamplePath + "/phase132_standard_messages_evidence_template.md";
        private const string SmokePath = SamplePath + "/Phase132StandardMessagesSmoke.cs";
        private const string CameraSourcePath = SamplePath + "/Phase132StandardCameraSource.cs";
        private const string ImuSourcePath = SamplePath + "/Phase132StandardImuSource.cs";
        private const string OdometrySourcePath = SamplePath + "/Phase132StandardOdometrySource.cs";
        private const string PoseSourcePath = SamplePath + "/Phase132StandardPoseSource.cs";
        private const string NavSatFixSourcePath = SamplePath + "/Phase132StandardNavSatFixSource.cs";
        private const string RvizConfigPath = SamplePath + "/rviz2_phase132_standard_messages.rviz";
        private const string AcceptanceScriptPath = "Scripts/smoke/phase132_standard_messages_acceptance.py";
        private const string RvizLauncherPath = "Scripts/smoke/launch_phase132_rviz2.py";
        private const string SharedHelperPath = "Scripts/smoke/_ros2_windows_env.py";

        private static readonly string[] Topics =
        {
            "/camera/camera_info",
            "/camera/image_raw",
            "/imu/data",
            "/odom",
            "/pose",
            "/fix"
        };

        private static readonly string[] Types =
        {
            "sensor_msgs/msg/CameraInfo",
            "sensor_msgs/msg/Image",
            "sensor_msgs/msg/Imu",
            "nav_msgs/msg/Odometry",
            "geometry_msgs/msg/PoseStamped",
            "sensor_msgs/msg/NavSatFix"
        };

        private static int _passed;
        private static string _repoRoot;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 132: ROS2 Standard Message Expansion ===");
            _passed = 0;

            VerifyPackageSampleMetadata();
            VerifyFilesExist();
            VerifySourceGuardsAndMessageRules();
            VerifyAcceptanceHelper();
            VerifyReadmeAndEvidenceTemplate();
            VerifyReleaseValidatorAndWiring();
            VerifyCoreAndOptionalRuntimeBoundaries();

            Console.WriteLine($"Phase 132: {_passed} checks passed.");
        }

        private static void VerifyPackageSampleMetadata()
        {
            using var document = JsonDocument.Parse(ReadRepoText(OptionalPackage + "/package.json"));
            var root = document.RootElement;
            Check(root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array,
                "132A-1: optional package declares package samples");

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

            Check(sample.HasValue, "132A-2: optional package sample entry exists for standard messages");
            if (!sample.HasValue)
                return;

            var value = sample.Value;
            Check(value.TryGetProperty("path", out var path)
                  && path.GetString() == "Samples~/ROS2 Standard Message Expansion",
                "132A-3: optional package sample entry points at the standard message sample");
            Check(value.TryGetProperty("description", out var description)
                  && AllTokens(description.GetString() ?? string.Empty, "CameraInfo", "raw Image", "IMU", "Odometry", "PoseStamped", "NavSatFix"),
                "132A-4: optional package sample description names all six message families");
        }

        private static void VerifyFilesExist()
        {
            foreach (var path in new[]
                     {
                         ReadmePath,
                         EvidenceTemplatePath,
                         SmokePath,
                         CameraSourcePath,
                         ImuSourcePath,
                          OdometrySourcePath,
                          PoseSourcePath,
                          NavSatFixSourcePath,
                          RvizConfigPath,
                          AcceptanceScriptPath,
                          RvizLauncherPath,
                          SharedHelperPath
                      })
            {
                Check(RepoFileExists(path), "132B-1: Phase132 file exists: " + path);
            }
        }

        private static void VerifySourceGuardsAndMessageRules()
        {
            foreach (var path in new[] { SmokePath, CameraSourcePath, ImuSourcePath, OdometrySourcePath, PoseSourcePath, NavSatFixSourcePath })
                Check(AllR2fuReferencesAreGuarded(ReadRepoText(path)), "132C-1: R2FU/message references are guarded in " + Path.GetFileName(path));

            var smoke = ReadRepoText(SmokePath);
            var camera = ReadRepoText(CameraSourcePath);
            var imu = ReadRepoText(ImuSourcePath);
            var odom = ReadRepoText(OdometrySourcePath);
            var pose = ReadRepoText(PoseSourcePath);
            var nav = ReadRepoText(NavSatFixSourcePath);
            var allSources = string.Join("\n", smoke, camera, imu, odom, pose, nav);

            Check(AllTokens(smoke, "ROS2UnityComponent", "StartExecutor", "CreateNode(NodeName)", "unity2foxglove_phase132_standard_messages"),
                "132C-2: smoke driver owns ROS2UnityComponent, executor fallback, and node");
            Check(AllTokens(smoke,
                      "CreatePublisher<sensor_msgs.msg.CameraInfo>",
                      "CreatePublisher<sensor_msgs.msg.Image>",
                      "CreatePublisher<sensor_msgs.msg.Imu>",
                      "CreatePublisher<nav_msgs.msg.Odometry>",
                      "CreatePublisher<geometry_msgs.msg.PoseStamped>",
                      "CreatePublisher<sensor_msgs.msg.NavSatFix>")
                   && !ContainsAny(smoke, new[] { "CreatePublisher<tf2_msgs", "/tf", "/clock", "rosgraph_msgs" }),
                "132C-3: smoke driver publishes only the six Phase132 topics and no /tf or /clock");
            Check(AllTokens(smoke,
                      "RemovePublisherIfPresent(_cameraInfoPublisher)",
                      "RemovePublisherIfPresent(_imagePublisher)",
                      "RemovePublisherIfPresent(_imuPublisher)",
                      "RemovePublisherIfPresent(_odometryPublisher)",
                      "RemovePublisherIfPresent(_posePublisher)",
                      "RemovePublisherIfPresent(_navSatFixPublisher)",
                      "RemoveNode(_node)",
                      "RemovePublisher<T>(publisher)")
                  && smoke.IndexOf("RemovePublisherIfPresent(_navSatFixPublisher)", StringComparison.Ordinal)
                  < smoke.IndexOf("RemoveNode(_node)", StringComparison.Ordinal),
                "132C-4: smoke cleanup removes all publishers before removing the node");
            Check(smoke.Contains("DefaultPublishIntervalSeconds = 0.5f", StringComparison.Ordinal)
                  && smoke.Contains("[SerializeField, Min(0.2f)]", StringComparison.Ordinal),
                "132C-5: smoke defaults to no more than 5 Hz per source");
            Check(AllTokens(smoke,
                      "_cameraInfoPublisher != null",
                      "_imagePublisher != null",
                      "_imuPublisher != null",
                      "_odometryPublisher != null",
                      "_posePublisher != null",
                      "_navSatFixPublisher != null"),
                "132C-6: publish loop guards every optional publisher before publishing");
            Check(smoke.Contains("monotonic", StringComparison.OrdinalIgnoreCase)
                  && smoke.Contains("Y2038", StringComparison.Ordinal)
                  && smoke.Contains("Time.sec is int32", StringComparison.Ordinal)
                  && smoke.Contains("_lastStampSeconds", StringComparison.Ordinal),
                "132C-7: smoke uses monotonic timestamps and documents ROS2 Time.sec/Y2038");
            Check(AllTokens(camera,
                      "CameraInfo.k[9]",
                      "CameraInfo.r[9]",
                      "CameraInfo.p[12]",
                      "Encoding = \"rgb8\"",
                      "step = checked((uint)(width * 3))",
                      "expectedLength = checked(height * (int)step)",
                      "new byte[expectedLength]",
                      "Range(1, 128)",
                      "_width = 32",
                      "_height = 24"),
                "132C-8: camera source validates calibration arrays and tiny rgb8 image rules");
            Check(AllTokens(imu, "sensor_msgs.msg.Imu", "double[9]", "9.80665", "Linear_acceleration"),
                "132C-8: IMU source has fixed covariance arrays and non-zero defaults");
            Check(AllTokens(odom, "nav_msgs.msg.Odometry", "double[36]", "Child_frame_id", "Linear", "Angular"),
                "132C-9: Odometry source has covariance arrays, child frame, and non-zero orientation");
            Check(AllTokens(pose, "geometry_msgs.msg.PoseStamped", "_frameId = \"map\"", "CreatePose"),
                "132C-10: Pose source publishes map-frame PoseStamped with non-zero orientation");
            Check(AllTokens(nav, "sensor_msgs.msg.NavSatFix", "synthetic", "37.7749", "-122.4194", "COVARIANCE_TYPE_DIAGONAL_KNOWN", "double[9]")
                  && !ContainsAny(nav, new[] { "Transform", "Rigidbody", "world coordinates" }),
                "132C-11: NavSatFix source uses explicit synthetic WGS84 coordinates");
            Check(!ContainsAny(allSources, new[] { "MCAP", "rosbag2", "Nav2", "image_pipeline", "calibration service" }),
                "132C-12: sources do not claim deferred workflows");
        }

        private static void VerifyAcceptanceHelper()
        {
            var helper = ReadRepoText(AcceptanceScriptPath);
            var launcher = ReadRepoText(RvizLauncherPath);
            var rvizConfig = ReadRepoText(RvizConfigPath);
            var shared = ReadRepoText(SharedHelperPath);

            Check(helper.Contains("import _ros2_windows_env as ros2env", StringComparison.Ordinal)
                  && shared.Contains("def build_ros_env", StringComparison.Ordinal)
                  && shared.Contains("domain_id", StringComparison.Ordinal)
                  && shared.Contains("def validate_ros2_root", StringComparison.Ordinal)
                  && shared.Contains("def run_ros2", StringComparison.Ordinal),
                "132D-1: helper uses shared Windows ROS2 environment module with domain-id support");
            Check(helper.Contains("ros2-script.py", StringComparison.Ordinal)
                  && helper.Contains("ros2env.DEFAULT_ROS2_ROOT", StringComparison.Ordinal)
                  && shared.Contains(@"C:\ros2_jazzy\ros2-windows", StringComparison.Ordinal)
                  && shared.Contains(".pixi", StringComparison.Ordinal)
                  && shared.Contains("ros2-script.py", StringComparison.Ordinal)
                  && !helper.Contains("subprocess.run([\"ros2\"", StringComparison.Ordinal),
                "132D-2: helper uses pinned Windows Jazzy pixi Python and ros2-script.py");
            Check(helper.Contains("DEFAULT_RVIZ_CONFIG", StringComparison.Ordinal)
                  && helper.Contains("rviz2_phase132_standard_messages.rviz", StringComparison.Ordinal)
                  && helper.Contains("parser.set_defaults(launch_rviz=True)", StringComparison.Ordinal)
                  && helper.Contains("ros2env.launch_rviz", StringComparison.Ordinal)
                  && helper.Contains("--no-launch-rviz", StringComparison.Ordinal),
                "132D-3: helper launches RViz2 by default with CLI-only opt-out");
            Check(AllTokens(helper, Topics)
                  && AllTokens(helper, Types)
                  && helper.Contains("unity2foxglove_phase132_standard_messages", StringComparison.Ordinal),
                "132D-4: helper validates all six topics, types, and publisher node");
            Check(helper.Contains("--domain-id", StringComparison.Ordinal)
                  && helper.Contains("--rmw", StringComparison.Ordinal)
                  && helper.Contains("--discovery-range", StringComparison.Ordinal)
                  && helper.Contains("--wait-seconds", StringComparison.Ordinal)
                  && helper.Contains("--echo-spin-seconds", StringComparison.Ordinal),
                "132D-5: helper supports RMW/domain/discovery/wait/echo options");
            Check(AllTokens(helper,
                      "validate_camera_info",
                      "validate_image",
                      "validate_imu",
                      "validate_odometry",
                      "validate_pose",
                      "validate_navsatfix",
                      "expected at least",
                      "appears truncated",
                      "non-zero",
                      "GREEN"),
                "132D-6: helper validates bounded full image echo and non-zero defaults");
            Check(launcher.Contains("DEFAULT_RVIZ_CONFIG", StringComparison.Ordinal)
                  && launcher.Contains("rviz2_phase132_standard_messages.rviz", StringComparison.Ordinal)
                  && launcher.Contains("ros2env.launch_rviz", StringComparison.Ordinal)
                  && launcher.Contains("--domain-id", StringComparison.Ordinal),
                "132D-7: standalone Phase132 RViz2 launcher uses shared Windows environment");
            Check(rvizConfig.Contains("Fixed Frame: map", StringComparison.Ordinal)
                  && rvizConfig.Contains("/pose", StringComparison.Ordinal)
                  && rvizConfig.Contains("/camera/image_raw", StringComparison.Ordinal)
                  && rvizConfig.Contains("rviz_default_plugins/Pose", StringComparison.Ordinal)
                  && rvizConfig.Contains("rviz_default_plugins/Image", StringComparison.Ordinal),
                "132D-8: RViz2 helper config visualizes supported Phase132 topics without requiring /tf");
        }

        private static void VerifyReadmeAndEvidenceTemplate()
        {
            var optionalReadme = ReadRepoText(OptionalPackage + "/README.md");
            var readme = ReadRepoText(ReadmePath);
            var evidence = ReadRepoText(EvidenceTemplatePath);
            var combined = optionalReadme + "\n" + readme + "\n" + evidence;

            Check(AllTokens(readme, Topics) && AllTokens(readme, Types),
                "132E-1: sample README lists all six default topics and ROS2 types");
            Check(readme.Contains("python Scripts\\smoke\\phase132_standard_messages_acceptance.py", StringComparison.Ordinal)
                  && readme.Contains("--ros2-root C:\\ros2_jazzy\\ros2-windows", StringComparison.Ordinal)
                  && readme.Contains("ros2-script.py", StringComparison.Ordinal)
                  && readme.Contains("launches RViz2 by default", StringComparison.Ordinal)
                  && readme.Contains("--no-launch-rviz", StringComparison.Ordinal)
                  && readme.Contains("python Scripts\\smoke\\launch_phase132_rviz2.py", StringComparison.Ordinal)
                  && readme.Contains("Use bare ROS2 commands only after", StringComparison.Ordinal)
                  && AllTokens(readme,
                      "ros2 topic info /camera/camera_info",
                      "ros2 topic echo --once /camera/image_raw",
                      "ros2 topic echo --once /imu/data",
                      "ros2 topic echo --once /odom",
                      "ros2 topic echo --once /pose",
                      "ros2 topic echo --once /fix"),
                "132E-2: README documents canonical Python helper and secondary bare ROS2 diagnostics");
            Check(readme.Contains("R2FU default QoS", StringComparison.Ordinal)
                  && readme.Contains("does not claim ROS2 `sensor_data` QoS profile parity", StringComparison.Ordinal)
                  && readme.Contains("best-effort sensor subscribers", StringComparison.Ordinal)
                  && readme.Contains("RViz2 Image displays", StringComparison.Ordinal),
                "132E-3: README warns about default QoS and best-effort subscriber mismatch");
            Check(readme.Contains("does not publish `/tf`", StringComparison.Ordinal)
                  && readme.Contains("Fixed Frame to the message frame", StringComparison.Ordinal)
                  && readme.Contains("external TF tree", StringComparison.Ordinal)
                  && readme.Contains("No transform", StringComparison.Ordinal),
                "132E-4: README documents TF policy and optional RViz2 limitations");
            Check(readme.Contains("can collide with Nav2, real drivers, or other samples", StringComparison.Ordinal)
                  && readme.Contains("Production projects should namespace", StringComparison.Ordinal)
                  && readme.Contains("/unity/odom", StringComparison.Ordinal)
                  && readme.Contains("/unity/camera/image_raw", StringComparison.Ordinal),
                "132E-5: README warns about conventional topic collisions and namespacing");
            Check(readme.Contains("synthetic constant WGS84", StringComparison.Ordinal)
                  && readme.Contains("not real geolocation", StringComparison.Ordinal)
                  && readme.Contains("does not convert Unity world coordinates", StringComparison.Ordinal),
                "132E-6: README documents NavSatFix synthetic-constant boundary");
            Check(readme.Contains("width = 32", StringComparison.Ordinal)
                  && readme.Contains("height = 24", StringComparison.Ordinal)
                  && readme.Contains("step = width * 3", StringComparison.Ordinal)
                  && readme.Contains("data.Length = height * step", StringComparison.Ordinal),
                "132E-7: README documents tiny rgb8 image dimensions for bounded echo");
            Check(AllTokens(evidence,
                      "Unity version",
                      "ROS2 distro",
                      "RMW implementation",
                      "ROS_DOMAIN_ID",
                      "enabled source count",
                      "/camera/camera_info publisher",
                      "/camera/image_raw publisher",
                      "/imu/data publisher",
                      "/odom publisher",
                      "/pose publisher",
                      "/fix publisher",
                      "R2FU default QoS caveat",
                      "No /tf published",
                      "Verdict"),
                "132E-8: evidence template captures environment, topics, limitations, and verdict");
            Check(!ContainsAny(combined, new[]
                  {
                      "image rectification is supported",
                      "calibration service is supported",
                      "state estimation is supported",
                      "Nav2 integration is supported",
                      "/clock is supported",
                      "MCAP fanout is supported",
                      "rosbag2 interop is supported",
                      "sensor_data QoS parity"
                  }),
                "132E-9: docs do not over-claim deferred workflows");
        }

        private static void VerifyReleaseValidatorAndWiring()
        {
            var script = ReadRepoText("Scripts/release/validate_ros2forunity_package.py");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(script.Contains("STANDARD_MESSAGES_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("ROS2 Standard Message Expansion", StringComparison.Ordinal)
                  && script.Contains("phase132_standard_messages_acceptance.py", StringComparison.Ordinal)
                  && script.Contains("len(samples) >= 6", StringComparison.Ordinal),
                "132F-1: release validator accepts the standard message sample set");
            Check(registry.Contains("Ci(\"--phase132\"", StringComparison.Ordinal)
                  && registry.Contains("Phase132Validation.Validate", StringComparison.Ordinal),
                "132F-2: validation registry wires --phase132");
            Check(project.Contains("Phase132Validation.cs", StringComparison.Ordinal),
                "132F-3: test project compiles Phase132Validation");
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
                "132G-1: core SDK production surface has no hard R2FU or standard ROS2 message references"
                + (coreHits.Count == 0 ? string.Empty : " (" + string.Join(", ", coreHits) + ")"));

            var optionalRuntimeHits = ExistingTextFilesOrSingleFile(OptionalPackage + "/Runtime")
                .SelectMany(path => OptionalRuntimeForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(optionalRuntimeHits.Count == 0,
                "132G-2: optional package Runtime remains facade-only with no R2FU/message references"
                + (optionalRuntimeHits.Count == 0 ? string.Empty : " (" + string.Join(", ", optionalRuntimeHits) + ")"));
        }

        private const string R2fuDefine = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";

        private static bool AllR2fuReferencesAreGuarded(string text)
        {
            return PhaseRos2ForUnityValidationHelpers.AllR2fuReferencesAreGuarded(
                text, R2fuDefine, PhaseRos2ForUnityValidationHelpers.R2fuGuardTokens, out _);
        }

        private static IEnumerable<string> CoreProductionForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "sensor_msgs.msg",
                "nav_msgs.msg",
                "geometry_msgs.msg"
            };
        }

        private static IEnumerable<string> OptionalRuntimeForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "namespace ROS2",
                "ISubscription<",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "tf2_msgs",
                "sensor_msgs.msg",
                "sensor_msgs",
                "nav_msgs.msg",
                "nav_msgs",
                "geometry_msgs.msg",
                "geometry_msgs",
                "std_msgs",
                "visualization_msgs",
                "builtin_interfaces",
                "ros2cs"
            };
        }

        private static IEnumerable<string> ExistingTextFilesOrSingleFile(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (File.Exists(path))
                return new[] { path };
            if (!Directory.Exists(path))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        }

        private static bool AllTokens(string text, params string[] tokens)
        {
            return tokens.All(token => text.Contains(token, StringComparison.Ordinal));
        }

        private static bool AllTokens(string text, IEnumerable<string> tokens)
        {
            return tokens.All(token => text.Contains(token, StringComparison.Ordinal));
        }

        private static bool ContainsAny(string text, IEnumerable<string> tokens)
        {
            return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase132 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string Rel(string absolutePath)
        {
            var root = FindRepoRoot().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? absolutePath.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/')
                : absolutePath;
        }

        private static string FindRepoRoot()
        {
            if (_repoRoot != null)
                return _repoRoot;
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            _repoRoot = root;
            return _repoRoot;
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] Phase 132: " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
