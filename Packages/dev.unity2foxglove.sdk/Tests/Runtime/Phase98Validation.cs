// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 98 validation for ROS2 Bridge sample and launch kit evidence.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase98Validation.
    /// </summary>
    /// <remarks>Phase192 source sections: VerifyAllSchemaFrameGeneration.cs, SendSummary.cs, LiveEvidence.cs, TopicEvidence.cs, AllSchemaEvidence.cs</remarks>
    public static partial class Phase98Validation
    {
        private const ulong SampleTimeNs = 1_700_098_000_000_000_000UL;
        private const string NamespacePrefix = "/unity2foxglove";

        private static readonly ProductTopic[] RequiredProductTopics =
        {
            new ProductTopic("/unity2foxglove/tf", "foxglove_msgs/msg/FrameTransform"),
            new ProductTopic("/unity2foxglove/scene", "foxglove_msgs/msg/SceneUpdate"),
            new ProductTopic("/unity2foxglove/camera", "foxglove_msgs/msg/CompressedImage"),
            new ProductTopic("/unity2foxglove/camera_calibration", "foxglove_msgs/msg/CameraCalibration"),
            new ProductTopic("/unity2foxglove/laser_scan", "foxglove_msgs/msg/LaserScan"),
            new ProductTopic("/unity2foxglove/point_cloud", "foxglove_msgs/msg/PointCloud")
        };

        private static readonly ProductTopic OptionalDracoTopic =
            new ProductTopic("/unity2foxglove/point_cloud_draco", "foxglove_msgs/msg/CompressedPointCloud");

        private static readonly ProductTopic[] LayoutTopics =
        {
            new ProductTopic("/tf", "foxglove.FrameTransform"),
            new ProductTopic("/scene", "foxglove.SceneUpdate"),
            new ProductTopic("/camera", "foxglove.CompressedImage"),
            new ProductTopic("/camera_calibration", "foxglove.CameraCalibration"),
            new ProductTopic("/laser_scan", "foxglove.LaserScan"),
            new ProductTopic("/point_cloud", "foxglove.PointCloud"),
            new ProductTopic("/point_cloud_draco", "foxglove.CompressedPointCloud")
        };

        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 98: ROS2 Bridge Samples And Launch Kit ===");
            _passed = 0;
            var allSchemaFrames = BuildAllSchemaFrames();

            VerifyPackageSample();
            VerifyDocsAndLayout();
            VerifyLaunchKit();
            VerifyAllSchemaFrameGeneration(allSchemaFrames);
            VerifyProductSampleFrameGeneration();
            VerifyLegacySampleAndReleaseChecks();
            VerifyCliWiring();

            Console.WriteLine($"Phase 98: {_passed} checks passed.");
        }

        /// <summary>
        /// Validation method for SendAllSchemaSamples.
        /// </summary>
        /// <param name="host">Host address used by the validation client or listener.</param>
        /// <param name="port">TCP port used by the validation client or listener.</param>
        /// <returns>The value produced by the validation helper.</returns>
        public static Phase98SendSummary SendAllSchemaSamples(string host, int port)
        {
            var frames = BuildAllSchemaFrames();
            using var client = new Ros2BridgeTcpClient();
            client.Connect(host, port, timeoutMs: 5000);

            long totalWireBytes = 0;
            foreach (var frame in frames)
            {
                totalWireBytes += Ros2BridgeFrameWriter.Write(frame).LongLength;
                client.Send(frame, timeoutMs: 1000);
            }

            return new Phase98SendSummary
            {
                SentFrames = frames.Count,
                TotalWireBytes = totalWireBytes,
                FirstSchema = frames.FirstOrDefault()?.SchemaName ?? string.Empty,
                LastSchema = frames.LastOrDefault()?.SchemaName ?? string.Empty
            };
        }

        /// <summary>
        /// Validation method for GenerateLiveEvidence.
        /// </summary>
        /// <param name="jsonPath">Path where live-evidence JSON is written.</param>
        /// <param name="host">Host address used by the validation client or listener.</param>
        /// <param name="port">TCP port used by the validation client or listener.</param>
        /// <param name="ros2Path">Path to the ROS 2 CLI used by the live-evidence helper.</param>
        /// <returns>The value produced by the validation helper.</returns>
        public static Phase98LiveEvidence GenerateLiveEvidence(string jsonPath, string host, int port, string ros2Path)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                throw new ArgumentException("Phase 98 live evidence requires an output JSON path.", nameof(jsonPath));

            var fullPath = Path.GetFullPath(jsonPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");

            var healthPath = Path.Combine(Path.GetDirectoryName(fullPath) ?? ".", "ros2_bridge_health.phase98.json");
            var health = Phase97Validation.GenerateHealthReport(
                healthPath,
                liveMode: true,
                ros2Path: ros2Path,
                host: host,
                port: port);

            var evidence = new Phase98LiveEvidence
            {
                SchemaVersion = 1,
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host,
                Port = port <= 0 ? 8767 : port,
                HealthReportPath = healthPath,
                HealthSummary = health.Summary.ToString(),
                Ros2GraphObservation = "deferred_to_phase99_manual_gate",
                Ros2Commands = new[]
                {
                    "ros2 topic list | grep unity2foxglove",
                    "ros2 topic info /unity2foxglove/tf --verbose",
                    "ros2 topic echo --once /unity2foxglove/tf",
                    "ros2 topic echo --once /unity2foxglove/laser_scan",
                    "ros2 topic echo --once /unity2foxglove/point_cloud",
                    "ros2 topic hz /unity2foxglove/tf"
                }
            };

            if (health.Summary != Ros2BridgeHealthSummary.Ready)
            {
                evidence.ProductTopics = RequiredProductTopics
                    .Select(topic => new Phase98TopicEvidence(topic.Topic, topic.SchemaName, "not_sent_health_not_ready", 0))
                    .ToArray();
                evidence.OptionalDracoTopic = new Phase98TopicEvidence(OptionalDracoTopic.Topic, OptionalDracoTopic.SchemaName, "not_sent_health_not_ready", 0);
                evidence.AllSchema = new Phase98AllSchemaEvidence { SentFrames = 0, TotalWireBytes = 0 };
                WriteEvidence(fullPath, evidence);
                return evidence;
            }

            using var client = new Ros2BridgeTcpClient();
            client.Connect(evidence.Host, evidence.Port, timeoutMs: 5000);

            var productFrames = BuildRequiredProductFrames().ToList();
            evidence.ProductTopics = SendProductFrames(client, productFrames).ToArray();
            evidence.OptionalDracoTopic = TrySendDracoTopic(client);

            var allSchemaFrames = BuildAllSchemaFrames();
            var totalWireBytes = 0L;
            foreach (var frame in allSchemaFrames)
            {
                totalWireBytes += Ros2BridgeFrameWriter.Write(frame).LongLength;
                client.Send(frame, timeoutMs: 1000);
            }

            evidence.AllSchema = new Phase98AllSchemaEvidence
            {
                SentFrames = allSchemaFrames.Count,
                TotalWireBytes = totalWireBytes,
                FirstSchema = allSchemaFrames.FirstOrDefault()?.SchemaName ?? string.Empty,
                LastSchema = allSchemaFrames.LastOrDefault()?.SchemaName ?? string.Empty
            };

            WriteEvidence(fullPath, evidence);
            return evidence;
        }

        private static void VerifyPackageSample()
        {
            var packageJsonPath = RepoPath("Packages/dev.unity2foxglove.ros2bridge/package.json");
            Check(File.Exists(packageJsonPath), "98A-0: Bridge package manifest exists");
            var packageJson = ParseJsonObject(ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/package.json"),
                "98A-0b: package manifest is valid JSON");
            var samples = packageJson["samples"]?.Children<JObject>().ToList() ?? new List<JObject>();
            Check(samples.Count == 1, "98A-1: Bridge package manifest exposes one sample");
            Check(samples.Any(sample =>
                    sample["displayName"]?.ToString() == "ROS2 Bridge Sample"
                    && sample["path"]?.ToString() == "Samples~/Ros2BridgeSample"),
                "98A-2: Bridge package manifest includes ROS2 Bridge Sample");

            foreach (var relativePath in SampleFiles())
                Check(File.Exists(RepoPath(relativePath)), "98A-3: sample file exists " + relativePath);

            foreach (var relativePath in SampleMetaFiles())
                Check(File.Exists(RepoPath(relativePath)), "98A-4: sample meta file exists " + relativePath);

            var sampleAsmdef = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Unity2Foxglove.Ros2Bridge.Sample.asmdef");
            Check(sampleAsmdef.Contains(
                      "\"Unity2Foxglove.Ros2Bridge.Generated\"",
                      StringComparison.Ordinal),
                "98A-4b: sample assembly references the Bridge assembly that owns the Provider");

            var scene = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity");
            var sceneCubePublisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveSceneCubePublisher.cs");
            const string controllerMarker =
                "m_EditorClassIdentifier: Assembly-CSharp::Ros2BridgeSampleController";
            Check(scene.StartsWith("%YAML", StringComparison.Ordinal),
                "98A-5a: sample scene is serialized as readable Unity YAML");
            var controllerStart = scene.IndexOf(controllerMarker, StringComparison.Ordinal);
            Check(controllerStart >= 0, "98A-5b: sample scene serializes its Bridge controller");
            var controllerEnd = scene.IndexOf("--- !u!", controllerStart, StringComparison.Ordinal);
            Check(controllerEnd > controllerStart, "98A-5c: Bridge controller block is bounded");
            var controllerBlock = scene.Substring(controllerStart, controllerEnd - controllerStart);
            Check(scene.Contains("guid: 6e1c973bf0174cf5ae0ec73f69d8a242", StringComparison.Ordinal)
                  && scene.Contains("_foxRunPublishTransportIds:", StringComparison.Ordinal)
                  && scene.Contains("- unity2foxglove.ros2bridge", StringComparison.Ordinal)
                  && controllerBlock.Contains("_provider: {fileID:", StringComparison.Ordinal)
                  && !controllerBlock.Contains("_manager: {fileID:", StringComparison.Ordinal)
                  && !scene.Contains("_ros2BridgeEnabled:", StringComparison.Ordinal)
                  && !scene.Contains("_ros2BridgeOutput:", StringComparison.Ordinal),
                "98A-5: sample scene uses the Bridge Provider and neutral Manager selection");
            foreach (var topic in RequiredProductTopics.Concat(new[] { OptionalDracoTopic }))
                Check(scene.Contains(topic.Topic.Replace(NamespacePrefix, string.Empty)),
                    "98A-6: sample scene contains publisher topic " + topic.Topic);
            Check(scene.Contains("_frameId: Moving Cube")
                  && scene.Contains("_childFrameId: Moving Cube")
                  && sceneCubePublisher.Contains("return _cachedExplicitFrameId ?? SanitizeFrameId(_frameId, gameObject.name);", StringComparison.Ordinal),
                "98A-8: sample scene cube frame resolves to the transform child frame id");

            var scripts = SampleScriptSources();
            var forbidden = new[]
            {
                "Process.Start",
                "System.Diagnostics.Process",
                "ros2 launch",
                "ros2 run",
                "colcon build",
                "cmd.exe",
                "powershell.exe"
            };
            foreach (var script in scripts)
            foreach (var token in forbidden)
                Check(script.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0,
                    "98A-7: sample scripts do not launch external tools " + token);
        }

        private static void VerifyDocsAndLayout()
        {
            var sampleReadme = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/README.md");
            var docs = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Documentation~/en/16_ROS2_Bridge_Sample.md");
            var samplesDoc = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/03_Samples_and_Demo_Project.md");

            Check(sampleReadme.Contains("ros2 launch")
                  && docs.Contains("## Lifecycle and diagnostics")
                  && docs.Contains("health/provenance probes"),
                "98B-1: docs document runtime diagnostics and explicit sidecar launch");
            Check(samplesDoc.Contains("dev.unity2foxglove.ros2bridge", StringComparison.Ordinal)
                  && samplesDoc.Contains("companion package", StringComparison.OrdinalIgnoreCase),
                "98B-2: core sample overview routes Bridge users to the companion package");
            Check(sampleReadme.Contains("## Ordinary Publisher Topics", StringComparison.Ordinal)
                  && sampleReadme.Contains("Foxglove WebSocket topics", StringComparison.Ordinal)
                  && sampleReadme.Contains("Bridge CDR", StringComparison.Ordinal),
                "98B-3: sample README distinguishes direct Foxglove topics from ROS2 bridge topics");
            Check(docs.Contains("RViz2-native") && docs.Contains("outside this sample"),
                "98B-4: docs defer native RViz2 message compatibility");

            foreach (var text in new[] { sampleReadme, docs })
            {
                Check(!ClaimsNativeRos2VisualizationSupport(text, "sensor_msgs")
                      && !ClaimsNativeRos2VisualizationSupport(text, "tf2_msgs")
                      && !ClaimsNativeRos2VisualizationSupport(text, "visualization_msgs"),
                    "98B-5: docs do not claim native ROS2 visualization message support");
            }

            var layoutText = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/FoxgloveRos2BridgeLayout.json");
            Check(ParseJson(layoutText, "98B-6: Foxglove layout is valid JSON") != null,
                "98B-6: Foxglove layout is valid JSON");
            foreach (var topic in LayoutTopics)
            {
                Check(layoutText.Contains(topic.Topic) && layoutText.Contains(topic.SchemaName),
                    "98B-7: layout references direct Foxglove sample topic and schema " + topic.Topic);
            }

            Check(!layoutText.Contains("/unity2foxglove/")
                  && !layoutText.Contains("foxglove_msgs/msg/"),
                "98B-8: layout avoids ROS2 bridge namespace and ros2msg schema names");
        }

        private static void VerifyLaunchKit()
        {
            foreach (var relativePath in new[]
            {
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/launch/unity2foxglove_bridge.launch.py",
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.sh",
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.ps1"
            })
                Check(File.Exists(RepoPath(relativePath)), "98C-1: launch kit file exists " + relativePath);

            var cmake = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/CMakeLists.txt");
            var packageXml = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/package.xml");
            Check(cmake.Contains("install(DIRECTORY launch") && cmake.Contains("install(DIRECTORY scripts"),
                "98C-2: CMake installs launch and scripts folders");
            Check(packageXml.Contains("<exec_depend>launch</exec_depend>")
                  && packageXml.Contains("<exec_depend>launch_ros</exec_depend>"),
                "98C-3: package.xml declares launch runtime dependencies");

            var sidecar = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");
            Check(sidecar.Contains("init_and_remove_ros_arguments") && sidecar.Contains("parse_args(non_ros_args)"),
                "98C-4: sidecar accepts ROS launch-injected --ros-args");

            var bash = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.sh");
            var ps1 = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.ps1");
            foreach (var schema in RequiredProductTopics.Select(t => t.SchemaName).Concat(new[] { OptionalDracoTopic.SchemaName }))
            {
                Check(bash.Contains("ros2 interface package foxglove_msgs", StringComparison.Ordinal)
                      && bash.Contains(schema, StringComparison.Ordinal),
                    "98C-5: bash preflight checks " + schema);
                Check(ps1.Contains("\"interface\", \"package\", \"foxglove_msgs\"", StringComparison.Ordinal)
                      && ps1.Contains(schema, StringComparison.Ordinal),
                    "98C-6: PowerShell preflight checks " + schema);
            }

            foreach (var script in new[] { bash, ps1 })
            {
                Check(!script.Contains("apt install") && !script.Contains("sudo ")
                      && !script.Contains("setx ") && !script.Contains(">> ~/.")
                      && !script.Contains("PATH="),
                    "98C-7: launch helper does not install packages or mutate shell profile");
            }
            Check(bash.Contains("PORT must be a number between 1 and 65535.", StringComparison.Ordinal)
                  && bash.Contains("10#$PORT", StringComparison.Ordinal)
                  && bash.Contains("PAYLOAD_FORMAT must be one of", StringComparison.Ordinal)
                  && bash.Contains("cdr-with-encapsulation|cdr-body-only", StringComparison.Ordinal),
                "98C-7b: bash launch helper validates port and payload format before ros2 launch");

            var readme = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md");
            Check(readme.Contains("ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py")
                  && readme.Contains("ros2 topic list")
                  && readme.Contains("ros2 topic echo")
                  && readme.Contains("ros2 bag record"),
                "98C-8: sidecar README documents launch and topic verification commands");
        }
    }




}
