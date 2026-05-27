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
using Foxglove.Schemas.PointCloud;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase98Validation
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

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 98: ROS2 Bridge Samples And Launch Kit ===");
            _passed = 0;

            VerifyPackageSample();
            VerifyDocsAndLayout();
            VerifyLaunchKit();
            VerifyAllSchemaFrameGeneration();
            VerifyProductSampleFrameGeneration();
            VerifyLegacySampleAndReleaseChecks();
            VerifyCliWiring();

            Console.WriteLine($"Phase 98: {_passed} checks passed.");
        }

        public static Phase98SendSummary SendAllSchemaSamples(string host, int port)
        {
            var frames = BuildAllSchemaFrames().ToList();
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

            var allSchemaFrames = BuildAllSchemaFrames().ToList();
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
            var packageJsonPath = RepoPath("Packages/dev.unity2foxglove.sdk/package.json");
            Check(File.Exists(packageJsonPath), "98A-0: package manifest exists");
            var packageJson = ParseJsonObject(ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json"),
                "98A-0b: package manifest is valid JSON");
            var samples = packageJson["samples"]?.Children<JObject>().ToList() ?? new List<JObject>();
            Check(samples.Count == 3, "98A-1: package manifest exposes three samples");
            Check(samples.Any(sample =>
                    sample["displayName"]?.ToString() == "ROS2 Bridge Sample"
                    && sample["path"]?.ToString() == "Samples~/Ros2BridgeSample"),
                "98A-2: package manifest includes ROS2 Bridge Sample");

            foreach (var relativePath in SampleFiles())
                Check(File.Exists(RepoPath(relativePath)), "98A-3: sample file exists " + relativePath);

            foreach (var relativePath in SampleMetaFiles())
                Check(File.Exists(RepoPath(relativePath)), "98A-4: sample meta file exists " + relativePath);

            var scene = ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity");
            var sceneCubePublisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveSceneCubePublisher.cs");
            Check(scene.Contains("_ros2BridgeEnabled: 1")
                  && scene.Contains("_ros2BridgeNamespace: /unity2foxglove")
                  && scene.Contains("_defaultRos2BridgeOutputEnabled: 1"),
                "98A-5: sample scene enables bridge namespace and default output");
            foreach (var topic in RequiredProductTopics.Concat(new[] { OptionalDracoTopic }))
                Check(scene.Contains(topic.Topic.Replace(NamespacePrefix, string.Empty)),
                    "98A-6: sample scene contains publisher topic " + topic.Topic);
            Check(scene.Contains("_frameId: Moving Cube")
                  && scene.Contains("_childFrameId: Moving Cube")
                  && sceneCubePublisher.Contains("return SanitizeFrameId(_frameId, gameObject.name);"),
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
            var sampleReadme = ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/README.md");
            var docs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/16_ROS2_Bridge_Sample.md");
            var samplesDoc = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/03_Samples_and_Demo_Project.md");

            Check(sampleReadme.Contains("optional Manager Inspector **ROS2 Bridge Health**")
                  && docs.Contains("not a required publish step")
                  && docs.Contains("ros2 launch"),
                "98B-1: docs document optional health diagnostics and sidecar launch");
            Check(samplesDoc.Contains("ROS2 Bridge Sample")
                  && (samplesDoc.Contains("three importable samples") || samplesDoc.Contains("three prepared samples")),
                "98B-2: sample overview documents third sample");
            Check(sampleReadme.Contains("direct Unity topics such as `/tf`")
                  && sampleReadme.Contains("mirrors it to ROS2 as `/unity2foxglove/tf`"),
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

            var layoutText = ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/FoxgloveRos2BridgeLayout.json");
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
                Check(bash.Contains("ros2 interface show", StringComparison.Ordinal)
                      && bash.Contains(schema, StringComparison.Ordinal),
                    "98C-5: bash preflight checks " + schema);
                Check((ps1.Contains("Invoke-Ros2Checked", StringComparison.Ordinal)
                       || ps1.Contains("ros2 interface show", StringComparison.Ordinal))
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

            var readme = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md");
            Check(readme.Contains("ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py")
                  && readme.Contains("ros2 topic list")
                  && readme.Contains("ros2 topic echo")
                  && readme.Contains("ros2 bag record"),
                "98C-8: sidecar README documents launch and topic verification commands");
        }

        private static void VerifyAllSchemaFrameGeneration()
        {
            var frames = BuildAllSchemaFrames().ToList();
            Check(frames.Count == 41, "98D-1: all-schema sender builds 41 frames");
            Check(frames.Select(f => f.SchemaName).Distinct(StringComparer.Ordinal).Count() == 41,
                "98D-2: all-schema sender covers unique schemas");

            foreach (var frame in frames)
            {
                Check(IsRos2TopicName(frame.Topic), "98D-3: all-schema topic is ROS2-safe " + frame.Topic);
                Check(frame.Topic.StartsWith("/unity2foxglove/samples/", StringComparison.Ordinal),
                    "98D-4: all-schema topic stays under sample namespace " + frame.Topic);
                Check(HasCdrHeader(frame.Payload), "98D-5: all-schema payload has CDR header " + frame.SchemaName);
                Check(Ros2BridgeFrameWriter.Write(frame).Length > frame.Payload.Length,
                    "98D-6: bridge frame writer accepts all-schema frame " + frame.SchemaName);
            }

            Check(ToSampleTopic("foxglove_msgs/msg/FrameTransform") == "/unity2foxglove/samples/frame_transform",
                "98D-7: schema-to-topic conversion uses snake_case");
        }

        private static void VerifyProductSampleFrameGeneration()
        {
            var frames = BuildRequiredProductFrames().ToList();
            Check(frames.Count == 6, "98E-1: product sender builds six required frames");
            foreach (var topic in RequiredProductTopics)
            {
                var frame = frames.SingleOrDefault(candidate => candidate.Topic == topic.Topic);
                Check(frame != null && frame.SchemaName == topic.SchemaName,
                    "98E-2: product frame exists " + topic.Topic);
                Check(frame != null && HasCdrHeader(frame.Payload),
                    "98E-3: product frame has CDR header " + topic.Topic);
            }

            Check(OptionalDracoTopic.Topic == "/unity2foxglove/point_cloud_draco",
                "98E-4: Draco topic is optional and separately named");
        }

        private static void VerifyLegacySampleAndReleaseChecks()
        {
            var phase17 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase17Validation.cs");
            var validatePackage = ReadRepoText("Scripts/release/validate_package.py");

            Check(phase17.Contains("samples.Count == 3") && phase17.Contains("ROS2 Bridge Sample"),
                "98F-1: Phase17 package validation accepts three samples");
            Check(validatePackage.Contains("EXPECTED_SAMPLE_COUNT = 3")
                  && validatePackage.Contains("\"ROS2 Bridge Sample\"")
                  && validatePackage.Contains("Samples~/Ros2BridgeSample"),
                "98F-2: release validation expects ROS2 Bridge Sample");
        }

        private static void VerifyCliWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var csproj = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("\"--phase98\"", StringComparison.Ordinal)
                  && registry.Contains("Phase98Validation.Validate", StringComparison.Ordinal),
                "98G-1: Program dispatches --phase98");
            Check(program.Contains("--phase98-sample-send-all") && program.Contains("RunPhase98SampleSendAll"),
                "98G-2: Program dispatches all-schema sample sender");
            Check(program.Contains("--phase98-live") && program.Contains("--json")
                  && program.Contains("--host") && program.Contains("--port") && program.Contains("--ros2"),
                "98G-3: Program dispatches live sample evidence with explicit overrides");
            Check(registry.Contains("Phase98Validation.Validate", StringComparison.Ordinal),
                "98G-4: full validation includes Phase98");
            Check(csproj.Contains("Phase98Validation.cs"),
                "98G-5: Phase98 validation is included in test project");
        }

        private static IEnumerable<Ros2BridgeFrame> BuildAllSchemaFrames()
        {
            return Ros2CdrSerializerRegistry.Entries.Select((entry, index) =>
            {
                var sample = entry.CreateSample();
                var payload = entry.Serialize(sample);
                return new Ros2BridgeFrame(
                    ToSampleTopic(entry.SchemaName),
                    entry.SchemaName,
                    Ros2BridgeFrame.CdrEncoding,
                    SampleTimeNs + (ulong)index,
                    (ulong)(index + 1),
                    payload,
                    Ros2BridgeQosProfile.ReliableDefault);
            });
        }

        private static IEnumerable<Ros2BridgeFrame> BuildRequiredProductFrames()
        {
            for (var i = 0; i < RequiredProductTopics.Length; ++i)
            {
                var topic = RequiredProductTopics[i];
                if (!Ros2CdrSerializerRegistry.TryGetBySchemaName(topic.SchemaName, out var entry))
                    throw new InvalidOperationException("Missing product sample serializer: " + topic.SchemaName);

                var sample = entry.CreateSample();
                yield return new Ros2BridgeFrame(
                    topic.Topic,
                    topic.SchemaName,
                    Ros2BridgeFrame.CdrEncoding,
                    SampleTimeNs + (ulong)i,
                    (ulong)(i + 1),
                    entry.Serialize(sample),
                    Ros2BridgeQosProfile.ReliableDefault);
            }
        }

        private static IEnumerable<Phase98TopicEvidence> SendProductFrames(Ros2BridgeTcpClient client, IReadOnlyList<Ros2BridgeFrame> frames)
        {
            foreach (var frame in frames)
            {
                client.Send(frame, timeoutMs: 1000);
                yield return new Phase98TopicEvidence(frame.Topic, frame.SchemaName, "sent", frame.Payload.Length);
            }
        }

        private static Phase98TopicEvidence TrySendDracoTopic(Ros2BridgeTcpClient client)
        {
            if (!DracoPointCloudNativeEncoder.TryGetAvailability(out var versionOrError))
                return new Phase98TopicEvidence(OptionalDracoTopic.Topic, OptionalDracoTopic.SchemaName, "skipped_unavailable: " + versionOrError, 0);

            var frame = CreatePointCloudFrame();
            if (!DracoPointCloudNativeEncoder.TryEncode(frame, out var dracoPayload, out var encodeError))
                return new Phase98TopicEvidence(OptionalDracoTopic.Topic, OptionalDracoTopic.SchemaName, "failed_encode: " + encodeError, 0);

            var cdrPayload = Ros2CdrCompressedPointCloudBuilder.Serialize(frame, dracoPayload);
            var bridgeFrame = new Ros2BridgeFrame(
                OptionalDracoTopic.Topic,
                OptionalDracoTopic.SchemaName,
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs + 100,
                100,
                cdrPayload,
                Ros2BridgeQosProfile.ReliableDefault);
            client.Send(bridgeFrame, timeoutMs: 1000);
            return new Phase98TopicEvidence(OptionalDracoTopic.Topic, OptionalDracoTopic.SchemaName, "sent: " + versionOrError, cdrPayload.Length);
        }

        private static PointCloudFrame CreatePointCloudFrame()
        {
            var frame = new PointCloudFrame
            {
                UnixNs = SampleTimeNs,
                FrameId = "unity_world"
            };
            frame.Points.Add(new PointCloudPoint(-0.5f, -0.5f, 0.0f));
            frame.Points.Add(new PointCloudPoint(0.5f, -0.5f, 0.25f));
            frame.Points.Add(new PointCloudPoint(-0.5f, 0.5f, 0.5f));
            frame.Points.Add(new PointCloudPoint(0.5f, 0.5f, 0.75f));
            return frame;
        }

        private static string ToSampleTopic(string schemaName)
        {
            var leaf = schemaName.Substring(schemaName.LastIndexOf('/') + 1);
            return NamespacePrefix + "/samples/" + ToSnakeCase(leaf);
        }

        private static string ToSnakeCase(string value)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < value.Length; ++i)
            {
                var c = value[i];
                if (char.IsUpper(c))
                {
                    if (builder.Length > 0
                        && builder[builder.Length - 1] != '_'
                        && (char.IsLower(value[i - 1])
                            || char.IsDigit(value[i - 1])
                            || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
                    {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
                else if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            return builder.ToString().Trim('_');
        }

        private static bool IsRos2TopicName(string topic)
            => Regex.IsMatch(topic, "^/[a-z0-9_]+(/[a-z0-9_]+)*$");

        private static bool HasCdrHeader(byte[] payload)
            => payload != null && payload.Length >= 4 && payload[0] == 0 && payload[1] == 1 && payload[2] == 0 && payload[3] == 0;

        private static bool ClaimsNativeRos2VisualizationSupport(string text, string token)
        {
            var index = text.IndexOf(token, StringComparison.Ordinal);
            if (index < 0)
                return false;

            var windowStart = Math.Max(0, index - 80);
            var windowLength = Math.Min(text.Length - windowStart, 180);
            var window = text.Substring(windowStart, windowLength);
            return !(window.Contains("outside", StringComparison.OrdinalIgnoreCase)
                     || window.Contains("defer", StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> SampleFiles()
        {
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/README.md";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/FoxgloveRos2BridgeLayout.json";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs";
        }

        private static IEnumerable<string> SampleMetaFiles()
        {
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/README.md.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/FoxgloveRos2BridgeLayout.json.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scenes.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs.meta";
            yield return "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs.meta";
        }

        private static IEnumerable<string> SampleScriptSources()
        {
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs");
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs");
            yield return ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs");
        }

        private static void WriteEvidence(string fullPath, Phase98LiveEvidence evidence)
            => File.WriteAllText(fullPath, JsonConvert.SerializeObject(evidence, Formatting.Indented), Encoding.UTF8);

        private static bool AnyLineContainsAll(string text, params string[] tokens)
        {
            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Any(line => tokens.All(token => line.Contains(token)));
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static JObject ParseJsonObject(string text, string name)
        {
            var token = ParseJson(text, name);
            if (token is JObject obj)
                return obj;

            throw new InvalidOperationException("[FAIL] " + name + " (expected object)");
        }

        private static JToken ParseJson(string text, string name)
        {
            try
            {
                return JToken.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("[FAIL] " + name, ex);
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private readonly struct ProductTopic
        {
            public ProductTopic(string topic, string schemaName)
            {
                Topic = topic;
                SchemaName = schemaName;
            }

            public string Topic { get; }
            public string SchemaName { get; }
        }
    }

    public sealed class Phase98SendSummary
    {
        public int SentFrames { get; set; }
        public long TotalWireBytes { get; set; }
        public string FirstSchema { get; set; }
        public string LastSchema { get; set; }
    }

    public sealed class Phase98LiveEvidence
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; }

        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("healthReportPath")]
        public string HealthReportPath { get; set; }

        [JsonProperty("healthSummary")]
        public string HealthSummary { get; set; }

        [JsonProperty("productTopics")]
        public Phase98TopicEvidence[] ProductTopics { get; set; }

        [JsonProperty("optionalDracoTopic")]
        public Phase98TopicEvidence OptionalDracoTopic { get; set; }

        [JsonProperty("allSchema")]
        public Phase98AllSchemaEvidence AllSchema { get; set; }

        [JsonProperty("ros2GraphObservation")]
        public string Ros2GraphObservation { get; set; }

        [JsonProperty("ros2Commands")]
        public string[] Ros2Commands { get; set; }
    }

    public sealed class Phase98TopicEvidence
    {
        public Phase98TopicEvidence(string topic, string schemaName, string status, int payloadBytes)
        {
            Topic = topic;
            SchemaName = schemaName;
            Status = status;
            PayloadBytes = payloadBytes;
        }

        [JsonProperty("topic")]
        public string Topic { get; }

        [JsonProperty("schemaName")]
        public string SchemaName { get; }

        [JsonProperty("status")]
        public string Status { get; }

        [JsonProperty("payloadBytes")]
        public int PayloadBytes { get; }
    }

    public sealed class Phase98AllSchemaEvidence
    {
        [JsonProperty("sentFrames")]
        public int SentFrames { get; set; }

        [JsonProperty("totalWireBytes")]
        public long TotalWireBytes { get; set; }

        [JsonProperty("firstSchema")]
        public string FirstSchema { get; set; }

        [JsonProperty("lastSchema")]
        public string LastSchema { get; set; }
    }
}
