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
    public static partial class Phase98Validation
    {
        private static void VerifyAllSchemaFrameGeneration(IReadOnlyList<Ros2BridgeFrame> frames)
        {
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
            Check(ToSampleTopic("foxglove_msgs/msg/IMU") == "/unity2foxglove/samples/imu",
                "98D-8: schema-to-topic conversion preserves IMU acronym as one snake_case token");
            Check(ToSampleTopic("sensor_msgs/msg/PointCloud2") == "/unity2foxglove/samples/point_cloud2",
                "98D-9: schema-to-topic conversion handles trailing numeric schema names");
            Check(ToSampleTopic("foxglove_msgs/msg/CompressedPointCloud") == "/unity2foxglove/samples/compressed_point_cloud",
                "98D-10: schema-to-topic conversion handles compressed point cloud names");
            Check(ToSampleTopic("foxglove_msgs/msg/CameraCalibration") == "/unity2foxglove/samples/camera_calibration",
                "98D-11: schema-to-topic conversion handles camera calibration names");
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
            var validatePackage = ReadRepoText("Scripts/package/validate_unity_package.py");

            Check(phase17.Contains("samples.Count == 3", StringComparison.Ordinal)
                  && phase17.Contains("dev.unity2foxglove.ros2bridge", StringComparison.Ordinal)
                  && phase17.Contains("bridgeSamples.Count == 1", StringComparison.Ordinal),
                "98F-1: Phase17 validates the core and Bridge sample manifests separately");
            Check(validatePackage.Contains("EXPECTED_SAMPLE_COUNT = len(EXPECTED_SAMPLES)")
                  && validatePackage.Contains("check_ros2_bridge_package", StringComparison.Ordinal)
                  && validatePackage.Contains("\"ROS2 Bridge Sample\"")
                  && validatePackage.Contains("Samples~/Ros2BridgeSample"),
                "98F-2: release validation checks the Bridge package sample separately");
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

        private static List<Ros2BridgeFrame> BuildAllSchemaFrames()
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
                    FoxRunResolvedQos.Default);
            }).ToList();
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
                    FoxRunResolvedQos.Default);
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
                FoxRunResolvedQos.Default);
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
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/README.md";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/FoxgloveRos2BridgeLayout.json";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs";
        }

        private static IEnumerable<string> SampleMetaFiles()
        {
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/README.md.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/FoxgloveRos2BridgeLayout.json.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scenes.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs.meta";
            yield return "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs.meta";
        }

        private static IEnumerable<string> SampleScriptSources()
        {
            yield return ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs");
            yield return ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs");
            yield return ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs");
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




}
