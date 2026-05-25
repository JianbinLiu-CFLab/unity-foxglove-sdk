// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 129 generic PointCloud2 RViz2 acceptance kit validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase129Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string SampleName = "RViz2 PointCloud2 Acceptance";
        private const string SamplePath = OptionalPackage + "/Samples~/RViz2 PointCloud2 Acceptance";
        private const string SampleReadmePath = SamplePath + "/README.md";
        private const string SmokeScriptPath = SamplePath + "/Phase129Rviz2PointCloud2Smoke.cs";
        private const string BuilderScriptPath = SamplePath + "/Phase129PointCloud2MessageBuilder.cs";
        private const string RvizConfigPath = SamplePath + "/rviz2_phase129_pointcloud2.rviz";
        private const string EvidenceTemplatePath = SamplePath + "/phase129_pointcloud2_evidence_template.md";
        private const string AcceptanceScriptPath = "Scripts/smoke/phase129_pointcloud2_acceptance.py";
        private const string RvizLauncherPath = "Scripts/smoke/launch_phase129_rviz2.py";
        private const string SharedHelperPath = "Scripts/smoke/_ros2_windows_env.py";
        private const string Define = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 129: Generic PointCloud2 Standard Mapping v1 ===");
            _passed = 0;

            VerifyPackageSampleMetadata();
            VerifySampleFiles();
            VerifySmokeScript();
            VerifyPointCloud2Builder();
            VerifyRvizConfig();
            VerifyAcceptanceHelper();
            VerifyDocsAndEvidenceTemplate();
            VerifyReleaseValidatorAcceptsV1SampleSet();
            VerifyCoreAndOptionalRuntimeBoundaries();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 129: {_passed} checks passed.");
        }

        private static void VerifyPackageSampleMetadata()
        {
            using var document = JsonDocument.Parse(ReadRepoText(OptionalPackage + "/package.json"));
            var root = document.RootElement;
            Check(root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array,
                "129A-1: optional package declares package samples");

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

            Check(sample.HasValue, "129A-2: optional package sample entry exists for PointCloud2 acceptance");
            if (!sample.HasValue)
                return;

            var value = sample.Value;
            Check(value.TryGetProperty("path", out var path)
                  && path.GetString() == "Samples~/RViz2 PointCloud2 Acceptance",
                "129A-3: optional package sample entry points at the PointCloud2 acceptance sample");
            Check(value.TryGetProperty("description", out var description)
                  && (description.GetString() ?? string.Empty).Contains("sensor_msgs/msg/PointCloud2", StringComparison.Ordinal)
                  && (description.GetString() ?? string.Empty).Contains("/points", StringComparison.Ordinal),
                "129A-4: optional package sample description names PointCloud2 and /points");
        }

        private static void VerifySampleFiles()
        {
            foreach (var path in new[]
                     {
                         SampleReadmePath,
                         SmokeScriptPath,
                         BuilderScriptPath,
                         RvizConfigPath,
                         EvidenceTemplatePath,
                         AcceptanceScriptPath,
                         RvizLauncherPath,
                         SharedHelperPath
                     })
            {
                Check(RepoFileExists(path), "129B-1: Phase129 file exists: " + path);
            }
        }

        private static void VerifySmokeScript()
        {
            var script = ReadRepoText(SmokeScriptPath);

            Check(script.Contains(Define, StringComparison.Ordinal)
                  && AllR2fuReferencesAreGuarded(script),
                "129C-1: smoke script guards ROS2 For Unity and generated message references");
            Check(script.Contains("NodeName = \"unity2foxglove_phase129_pointcloud2\"", StringComparison.Ordinal)
                  && script.Contains("TfTopic = \"/tf\"", StringComparison.Ordinal)
                  && script.Contains("PointsTopic = \"/points\"", StringComparison.Ordinal),
                "129C-2: smoke script uses the required node and topics");
            Check(script.Contains("FrameMap = \"map\"", StringComparison.Ordinal)
                  && script.Contains("FrameBaseLink = \"base_link\"", StringComparison.Ordinal)
                  && script.Contains("FramePointCloudSensor = \"point_cloud_sensor\"", StringComparison.Ordinal)
                  && script.Contains("Child_frame_id = FrameBaseLink", StringComparison.Ordinal)
                  && script.Contains("Child_frame_id = FramePointCloudSensor", StringComparison.Ordinal),
                "129C-3: smoke script publishes map -> base_link -> point_cloud_sensor TF frames");
            Check(script.Contains("GetComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && script.Contains("AddComponent<ROS2UnityComponent>()", StringComparison.Ordinal)
                  && script.Contains(".Ok()", StringComparison.Ordinal),
                "129C-4: smoke script finds or adds ROS2UnityComponent and waits for Ok()");
            Check(script.Contains("CreatePublisher<tf2_msgs.msg.TFMessage>", StringComparison.Ordinal)
                  && script.Contains("CreatePublisher<sensor_msgs.msg.PointCloud2>", StringComparison.Ordinal)
                  && !script.Contains("QualityOfServiceProfile", StringComparison.Ordinal),
                "129C-5: smoke script publishes TFMessage and PointCloud2 with default R2FU QoS");
            Check(script.Contains("new PointCloudFrame", StringComparison.Ordinal)
                  && script.Contains("new PointCloudPoint", StringComparison.Ordinal)
                  && script.Contains("DefaultPointCount = 1000", StringComparison.Ordinal)
                  && script.Contains("DefaultColumns = 50", StringComparison.Ordinal)
                  && script.Contains("DefaultSpacingMeters = 0.08f", StringComparison.Ordinal)
                  && script.Contains("DefaultWaveHeightMeters = 0.35f", StringComparison.Ordinal)
                  && script.Contains("Intensity", StringComparison.Ordinal)
                  && script.Contains("index / (float)(count - 1)", StringComparison.Ordinal)
                  && script.Contains("Phase129PointCloud2MessageBuilder.Build", StringComparison.Ordinal)
                  && script.Contains("FrameId = FramePointCloudSensor", StringComparison.Ordinal),
                "129C-6: smoke script builds a deterministic synthetic wave PointCloudFrame and publishes it in point_cloud_sensor");
            Check(!ContainsAny(script, new[]
                  {
                      "IPhase129PointCloudFrameSource",
                      "FindObject",
                      "GetComponents<",
                      "provider hook",
                      "source hook"
                  }),
                "129C-7: smoke script stays synthetic-only without a live source-provider hook");
            Check(script.Contains("CreateStamp", StringComparison.Ordinal)
                  && script.Contains("CreateUnixNanoseconds", StringComparison.Ordinal)
                  && script.Contains("ROS2 Time.sec is int32", StringComparison.Ordinal)
                  && script.Contains("Y2038", StringComparison.Ordinal)
                  && !script.Contains("(uint)sec", StringComparison.Ordinal)
                  && !script.Contains("\"/clock\"", StringComparison.Ordinal),
                "129C-8: smoke script writes monotonic ROS-compatible timestamps without unsigned sec casts and does not publish /clock");
            Check(script.Contains("_publishedTfCount", StringComparison.Ordinal)
                  && script.Contains("_publishedPointCloudCount", StringComparison.Ordinal)
                  && script.Contains("_pointCount", StringComparison.Ordinal)
                  && script.Contains("_pointStep", StringComparison.Ordinal)
                  && script.Contains("_rowStep", StringComparison.Ordinal)
                  && script.Contains("_runtimeRoot", StringComparison.Ordinal)
                  && script.Contains("_runtimeRootIsPackage", StringComparison.Ordinal)
                  && script.Contains("_assetRuntimePresent", StringComparison.Ordinal)
                  && script.Contains("_lastError", StringComparison.Ordinal),
                "129C-9: smoke script exposes required Inspector evidence fields");
            Check(script.Contains("_warnedMissingStartExecutor", StringComparison.Ordinal)
                  && script.Contains("StartExecutor reflection hook was not found", StringComparison.Ordinal)
                  && script.Contains("Import ROS2 For Unity", StringComparison.Ordinal),
                "129C-10: smoke script reports missing define and optional StartExecutor diagnostics clearly");
        }

        private static void VerifyPointCloud2Builder()
        {
            var builder = ReadRepoText(BuilderScriptPath);

            Check(builder.Contains(Define, StringComparison.Ordinal)
                  && AllR2fuReferencesAreGuarded(builder),
                "129D-1: PointCloud2 builder guards generated ROS2 message references");
            Check(builder.Contains("PointCloudPackedDataBuilder.Build(frame)", StringComparison.Ordinal)
                  && builder.Contains("checked((uint)frame.Points.Count)", StringComparison.Ordinal)
                  && builder.Contains("checked(pointStep * width)", StringComparison.Ordinal)
                  && builder.Contains("packed.Data.Length != rowStep", StringComparison.Ordinal),
                "129D-2: builder uses packed SDK layout with checked width, row_step, and data length");
            Check(builder.Contains("Height = 1", StringComparison.Ordinal)
                  && builder.Contains("Width = width", StringComparison.Ordinal)
                  && builder.Contains("Is_bigendian = false", StringComparison.Ordinal)
                  && builder.Contains("Point_step = pointStep", StringComparison.Ordinal)
                  && builder.Contains("Row_step = rowStep", StringComparison.Ordinal)
                  && builder.Contains("Data = packed.Data", StringComparison.Ordinal)
                  && builder.Contains("Is_dense = true", StringComparison.Ordinal),
                "129D-3: builder writes the required unorganized PointCloud2 shape");
            Check(builder.Contains("PointFieldInt8 = 1", StringComparison.Ordinal)
                  && builder.Contains("PointFieldUint8 = 2", StringComparison.Ordinal)
                  && builder.Contains("PointFieldInt16 = 3", StringComparison.Ordinal)
                  && builder.Contains("PointFieldUint16 = 4", StringComparison.Ordinal)
                  && builder.Contains("PointFieldInt32 = 5", StringComparison.Ordinal)
                  && builder.Contains("PointFieldUint32 = 6", StringComparison.Ordinal)
                  && builder.Contains("PointFieldFloat32 = 7", StringComparison.Ordinal)
                  && builder.Contains("PointFieldFloat64 = 8", StringComparison.Ordinal),
                "129D-4: builder declares explicit SDK-to-ROS2 PointField datatype constants");
            Check(builder.Contains("PointCloudPackedNumericType.Int8", StringComparison.Ordinal)
                  && builder.Contains("PointCloudPackedNumericType.Uint8", StringComparison.Ordinal)
                  && builder.Contains("PointCloudPackedNumericType.Int16", StringComparison.Ordinal)
                  && builder.Contains("PointCloudPackedNumericType.Uint16", StringComparison.Ordinal)
                  && builder.Contains("PointCloudPackedNumericType.Int32", StringComparison.Ordinal)
                  && builder.Contains("PointCloudPackedNumericType.Uint32", StringComparison.Ordinal)
                  && builder.Contains("PointCloudPackedNumericType.Float32", StringComparison.Ordinal)
                  && builder.Contains("PointCloudPackedNumericType.Float64", StringComparison.Ordinal)
                  && builder.Contains("Unsupported PointCloud packed numeric type", StringComparison.Ordinal)
                  && !builder.Contains("(byte)field.Type", StringComparison.Ordinal)
                  && !builder.Contains("(int)field.Type", StringComparison.Ordinal),
                "129D-5: builder maps SDK numeric types explicitly and rejects unsupported types");
            Check(builder.Contains("Count = 1", StringComparison.Ordinal),
                "129D-6: builder sets PointField.count to 1 for every field");
        }

        private static void VerifyRvizConfig()
        {
            var rviz = ReadRepoText(RvizConfigPath);
            Check(rviz.Contains("Fixed Frame: map", StringComparison.Ordinal)
                  && rviz.Contains("/tf", StringComparison.Ordinal)
                  && rviz.Contains("/points", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/TF", StringComparison.Ordinal)
                  && rviz.Contains("rviz_default_plugins/PointCloud2", StringComparison.Ordinal),
                "129E-1: RViz2 config targets map, TF, and /points PointCloud2");
            Check(!ContainsAny(rviz, new[]
                  {
                      "MarkerArray",
                      "CameraInfo",
                      "rviz_default_plugins/Image",
                      "sensor_msgs/msg/Image",
                      "MCAP",
                      "rosbag2"
                  }),
                "129E-2: RViz2 config avoids deferred displays and workflows");
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
                  && script.Contains("phase129", StringComparison.Ordinal),
                "129F-1: Python acceptance helper has repository header and CLI entry point");
            Check(script.Contains("import _ros2_windows_env as ros2env", StringComparison.Ordinal)
                  && script.Contains("ros2env.DEFAULT_ROS2_ROOT", StringComparison.Ordinal)
                  && shared.Contains("ros2-script.py", StringComparison.Ordinal)
                  && shared.Contains(".pixi", StringComparison.Ordinal)
                  && shared.Contains(@"C:\ros2_jazzy\ros2-windows", StringComparison.Ordinal),
                "129F-2: helper uses pinned Windows Jazzy pixi Python and ros2-script.py");
            Check(shared.Contains("--no-daemon", StringComparison.Ordinal)
                  && shared.Contains("topic\", \"info\"", StringComparison.Ordinal)
                  && shared.Contains("\"-v\"", StringComparison.Ordinal)
                  && shared.Contains("node\", \"list\"", StringComparison.Ordinal),
                "129F-3: helper uses no-daemon graph checks, node list, and topic info -v");
            Check(script.Contains("unity2foxglove_phase129_pointcloud2", StringComparison.Ordinal)
                  && script.Contains("/tf", StringComparison.Ordinal)
                  && script.Contains("/points", StringComparison.Ordinal)
                  && shared.Contains("Publisher count:", StringComparison.Ordinal)
                  && shared.Contains("Node name:", StringComparison.Ordinal)
                  && script.Contains("node_name=NODE_NAME", StringComparison.Ordinal),
                "129F-4: helper proves required publisher endpoints belong to the Phase129 node");
            Check(script.Contains("probe_node_list", StringComparison.Ordinal)
                  && script.Contains("node list did not include", StringComparison.Ordinal)
                  && script.Contains("continuing with publisher endpoint and echo checks", StringComparison.Ordinal)
                  && script.Contains("topic info -v /tf (diagnostic)", StringComparison.Ordinal),
                "129F-4b: helper treats flaky node list and /tf topic info as diagnostics instead of hard gates");
            Check(script.Contains("tf2_msgs/msg/TFMessage", StringComparison.Ordinal)
                  && script.Contains("sensor_msgs/msg/PointCloud2", StringComparison.Ordinal)
                  && helperSurface.Contains("--once", StringComparison.Ordinal)
                  && helperSurface.Contains("--spin-time", StringComparison.Ordinal)
                  && script.Contains("EXPECTED_POINT_COUNT = 1000", StringComparison.Ordinal)
                  && script.Contains("EXPECTED_ROW_STEP = EXPECTED_POINT_COUNT * EXPECTED_POINT_STEP", StringComparison.Ordinal)
                  && script.Contains("map", StringComparison.Ordinal)
                  && script.Contains("base_link", StringComparison.Ordinal)
                  && script.Contains("point_cloud_sensor", StringComparison.Ordinal)
                  && script.Contains("height: 1", StringComparison.Ordinal)
                  && script.Contains("width", StringComparison.Ordinal)
                  && script.Contains("fields:", StringComparison.Ordinal)
                  && script.Contains("data:", StringComparison.Ordinal),
                "129F-5: helper echoes TF/PointCloud2 once with bounded spin time and content checks");
            Check(script.Contains("--launch-rviz", StringComparison.Ordinal)
                  && script.Contains("--rviz-config", StringComparison.Ordinal)
                  && script.Contains("--rmw", StringComparison.Ordinal)
                  && script.Contains("--discovery-range", StringComparison.Ordinal)
                  && shared.Contains("rviz2.exe", StringComparison.Ordinal)
                  && shared.Contains("rviz_ogre_vendor", StringComparison.Ordinal)
                  && shared.Contains("gz_math_vendor", StringComparison.Ordinal)
                  && !script.Contains("\"run\", \"rviz2\"", StringComparison.Ordinal)
                  && !script.Contains("env[\"ROS_AUTOMATIC_DISCOVERY_RANGE\"] = \"SUBNET\"", StringComparison.Ordinal),
                "129F-6: helper supports RMW/discovery selection and launches RViz2 through direct rviz2.exe");
            Check(script.Contains("launch_rviz_before_echo", StringComparison.Ordinal)
                  && script.IndexOf("launch_rviz_before_echo", StringComparison.Ordinal)
                     < script.IndexOf("print(\"--- echo /tf ---\")", StringComparison.Ordinal),
                "129F-6b: helper launches RViz2 before bounded echo checks for faster manual feedback");
            Check(launcher.Contains("# Purpose:", StringComparison.Ordinal)
                  && launcher.Contains("argparse", StringComparison.Ordinal)
                  && launcherSurface.Contains("subprocess.Popen", StringComparison.Ordinal)
                  && launcherSurface.Contains("rviz2.exe", StringComparison.Ordinal)
                  && launcherSurface.Contains("rviz_ogre_vendor", StringComparison.Ordinal)
                  && launcherSurface.Contains("gz_math_vendor", StringComparison.Ordinal)
                  && launcherSurface.Contains("ROS_AUTOMATIC_DISCOVERY_RANGE", StringComparison.Ordinal)
                  && launcher.Contains("--dry-run", StringComparison.Ordinal),
                "129F-7: Python RViz2 launcher replaces durable PowerShell launch asset");
        }

        private static void VerifyDocsAndEvidenceTemplate()
        {
            var optionalReadme = ReadRepoText(OptionalPackage + "/README.md");
            var sampleReadme = ReadRepoText(SampleReadmePath);
            var evidence = ReadRepoText(EvidenceTemplatePath);
            var combined = optionalReadme + "\n" + sampleReadme + "\n" + evidence;

            Check(optionalReadme.Contains("RViz2 PointCloud2 Acceptance", StringComparison.Ordinal)
                  && optionalReadme.Contains("/points", StringComparison.Ordinal)
                  && optionalReadme.Contains("sensor_msgs/msg/PointCloud2", StringComparison.Ordinal),
                "129G-1: optional package README mentions the PointCloud2 acceptance kit");
            Check(sampleReadme.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && sampleReadme.Contains("external ROS2 For Unity", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("python Scripts\\smoke\\phase129_pointcloud2_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("--ros2-root C:\\ros2_jazzy\\ros2-windows", StringComparison.Ordinal)
                  && sampleReadme.Contains("--rviz-config", StringComparison.Ordinal)
                  && sampleReadme.Contains("ROS_AUTOMATIC_DISCOVERY_RANGE", StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2-script.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("/tf", StringComparison.Ordinal)
                  && sampleReadme.Contains("map -> base_link -> point_cloud_sensor", StringComparison.Ordinal)
                  && !sampleReadme.Contains("\nros2 topic", StringComparison.Ordinal),
                "129G-2: sample README documents TF, the Windows helper path, and does not make bare ros2 primary");
            Check(sampleReadme.Contains("generic", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("not vendor-specific", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("unorganized", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("height = 1", StringComparison.Ordinal)
                  && sampleReadme.Contains("width = 1000", StringComparison.Ordinal),
                "129G-3: sample README documents generic, unorganized PointCloud2 scope");
            Check(evidence.Contains("OS", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("Unity version", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("ROS2 distro", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RMW implementation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("runtime root", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("topic info -v /tf", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("topic info -v /points", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("/tf echo", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("/points echo", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RViz2 TF observation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("RViz2 PointCloud2 observation", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                  && evidence.Contains("verdict", StringComparison.OrdinalIgnoreCase),
                "129G-4: evidence template captures CLI, runtime, RViz2, screenshot, and verdict evidence");
            Check(!ContainsAny(combined, new[]
                  {
                      "supports all LiDAR vendors",
                      "supports organized point clouds",
                      "supports PointCloud2 subscription",
                      "supports vendor-specific metadata",
                      "supports MarkerArray",
                      "supports CameraInfo",
                      "supports Image",
                      "supports MCAP replay fanout",
                      "supports rosbag2"
                  }),
                "129G-5: docs do not over-claim deferred PointCloud2 or ROS2 workflows");
        }

        private static void VerifyReleaseValidatorAcceptsV1SampleSet()
        {
            var script = ReadRepoText("Scripts/release/validate_ros2forunity_package.py");
            Check(script.Contains("RVIZ_POINTCLOUD2_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("RViz2 PointCloud2 Acceptance", StringComparison.Ordinal)
                  && script.Contains("RVIZ_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("RVIZ_MARKERARRAY_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("External Adapter", StringComparison.Ordinal)
                  && script.Contains("RVIZ_V1_SAMPLE", StringComparison.Ordinal),
                "129H-1: release validator accepts External Adapter and RViz2 v1 sample set");
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
                "129I-1: core SDK production surface has no hard R2FU or standard ROS2 message references"
                + (coreHits.Count == 0 ? string.Empty : " (" + string.Join(", ", coreHits) + ")"));

            var optionalRuntimeHits = ExistingTextFilesOrSingleFile(OptionalPackage + "/Runtime")
                .SelectMany(path => OptionalRuntimeForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(optionalRuntimeHits.Count == 0,
                "129I-2: optional package Runtime remains facade-only with no R2FU/message references"
                + (optionalRuntimeHits.Count == 0 ? string.Empty : " (" + string.Join(", ", optionalRuntimeHits) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase129\"", StringComparison.Ordinal)
                  && registry.Contains("Phase129Validation.Validate", StringComparison.Ordinal),
                "129J-1: validation registry wires --phase129");
            Check(project.Contains("Phase129Validation.cs", StringComparison.Ordinal),
                "129J-2: test project compiles Phase129Validation");
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

        private static bool AllR2fuReferencesAreGuarded(string text)
        {
            var tokens = new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "tf2_msgs",
                "sensor_msgs",
                "std_msgs",
                "geometry_msgs",
                "builtin_interfaces"
            };

            var stack = new Stack<bool>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
                {
                    stack.Push(trimmed.Contains(Define, StringComparison.Ordinal));
                    continue;
                }

                if (trimmed.StartsWith("#elif ", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    stack.Push(trimmed.Contains(Define, StringComparison.Ordinal));
                    continue;
                }

                if (trimmed.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    stack.Push(false);
                    continue;
                }

                if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    continue;
                }

                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (tokens.Any(token => line.Contains(token, StringComparison.Ordinal))
                    && !stack.Any(guarded => guarded))
                {
                    throw new InvalidOperationException("Unguarded Phase129 R2FU reference on line " + (i + 1) + ": " + trimmed);
                }
            }

            return true;
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
                throw new FileNotFoundException("Missing required Phase129 file: " + relativePath, path);
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
