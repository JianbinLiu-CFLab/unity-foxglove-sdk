// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 96 validation for ROS2 Bridge topic profiles and QoS metadata.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase96Validation
    {
        private const ulong SampleTimeNs = 1_700_096_000_000_000_000UL;
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 96: ROS2 Bridge Topic Profiles And QoS ===");
            _passed = 0;

            VerifyTopicHelpers();
            VerifyQosHelpers();
            VerifyFrameHeaderCompatibility();
            VerifyRuntimeSourceIntegration();
            VerifySidecarSourceExpectations();
            VerifyInspectorSourceExpectations();

            Console.WriteLine($"Phase 96: {_passed} checks passed.");
        }

        private static void VerifyTopicHelpers()
        {
            Check(Resolve("", "/tf", "") == "/tf", "96A-1: empty namespace preserves topic");
            Check(Resolve("/robot1", "/tf", "") == "/robot1/tf", "96A-2: namespace prefixes publisher topic");
            Check(Resolve("/robot1/", "/unity//point_cloud", "") == "/robot1/unity/point_cloud",
                "96A-3: namespace and topic collapse duplicate slashes");
            Check(Resolve("/", "/tf", "") == "/tf", "96A-4: root namespace behaves like no prefix");
            Check(Resolve("/robot1", "/unity/point_cloud", "/lidar/front") == "/lidar/front",
                "96A-5: absolute override wins over manager namespace");

            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeNamespace("robot1", out _, out _),
                "96A-6: namespace without leading slash is rejected");
            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeTopic("lidar/front", out _, out _),
                "96A-7: override without leading slash is rejected");
            Check(!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic("/robot1", "tf", "", out _, out _),
                "96A-8: publisher topic without leading slash is rejected");
            Check(!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic("", "/", "", out _, out _),
                "96A-9: root publisher topic is rejected");
            Check(!Ros2BridgeTopicProfile.TryNormalizeRos2BridgeTopic("/", out _, out _),
                "96A-10: root override topic is rejected");
        }

        private static void VerifyQosHelpers()
        {
            Check((int)Ros2BridgeQosPreset.ReliableDefault == 0, "96B-1: ReliableDefault enum value is stable");
            Check((int)Ros2BridgeQosPreset.SensorData == 1, "96B-2: SensorData enum value is stable");
            Check((int)Ros2BridgeQosPreset.TransientLocal == 2, "96B-3: TransientLocal enum value is stable");
            Check((int)Ros2BridgeQosPreset.Custom == 3, "96B-4: Custom enum value is stable");

            var reliable = Ros2BridgeQosProfile.Resolve(
                Ros2BridgeQosPreset.ReliableDefault,
                Ros2BridgeReliability.BestEffort,
                Ros2BridgeDurability.TransientLocal,
                99);
            Check(reliable.ReliabilityWireValue == "reliable" && reliable.DurabilityWireValue == "volatile" && reliable.Depth == 10,
                "96B-5: Reliable Default maps to reliable volatile depth10");

            var sensor = Ros2BridgeQosProfile.Resolve(
                Ros2BridgeQosPreset.SensorData,
                Ros2BridgeReliability.Reliable,
                Ros2BridgeDurability.TransientLocal,
                99);
            Check(sensor.ReliabilityWireValue == "best_effort" && sensor.DurabilityWireValue == "volatile" && sensor.Depth == 5,
                "96B-6: Sensor Data maps to best-effort volatile depth5");

            var transientLocal = Ros2BridgeQosProfile.Resolve(
                Ros2BridgeQosPreset.TransientLocal,
                Ros2BridgeReliability.BestEffort,
                Ros2BridgeDurability.Volatile,
                99);
            Check(transientLocal.ReliabilityWireValue == "reliable" && transientLocal.DurabilityWireValue == "transient_local" && transientLocal.Depth == 1,
                "96B-7: Transient Local maps to reliable transient-local depth1");

            var custom = Ros2BridgeQosProfile.Resolve(
                Ros2BridgeQosPreset.Custom,
                Ros2BridgeReliability.BestEffort,
                Ros2BridgeDurability.TransientLocal,
                0);
            Check(custom.ReliabilityWireValue == "best_effort" && custom.DurabilityWireValue == "transient_local" && custom.Depth == 1,
                "96B-8: Custom normalizes depth to at least 1");
            Check(custom.DisplaySummary == "Best Effort / Transient Local / Depth 1",
                "96B-9: QoS display summary uses product labels");
        }

        private static void VerifyFrameHeaderCompatibility()
        {
            var legacy = new Ros2BridgeFrame(
                "/unity/tf",
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs,
                1,
                new byte[] { 0, 1, 0, 0, 1 });
            var legacyHeader = ReadHeader(Ros2BridgeFrameWriter.Write(legacy));
            Check(legacyHeader["profileName"] == null && legacyHeader["qos"] == null,
                "96C-1: legacy frame constructor omits QoS metadata");
            Check(!legacy.Qos.HasValue && legacy.ProfileName == null,
                "96C-2: legacy frame keeps QoS optional");

            var qos = Ros2BridgeQosProfile.Resolve(
                Ros2BridgeQosPreset.SensorData,
                Ros2BridgeReliability.Reliable,
                Ros2BridgeDurability.Volatile,
                10);
            var profiled = new Ros2BridgeFrame(
                "/lidar/front",
                "foxglove_msgs/msg/PointCloud",
                Ros2BridgeFrame.CdrEncoding,
                SampleTimeNs,
                2,
                new byte[] { 0, 1, 0, 0, 2 },
                qos);
            var profiledHeader = ReadHeader(Ros2BridgeFrameWriter.Write(profiled));
            Check(profiledHeader["topic"]?.ToString() == "/lidar/front", "96C-3: profiled frame uses effective bridge topic");
            Check(profiledHeader["profileName"]?.ToString() == "Sensor Data", "96C-4: header contains profileName");
            Check(profiledHeader["qos"]?["reliability"]?.ToString() == "best_effort", "96C-5: header contains QoS reliability");
            Check(profiledHeader["qos"]?["durability"]?.ToString() == "volatile", "96C-6: header contains QoS durability");
            Check(profiledHeader["qos"]?["depth"]?.Value<int>() == 5, "96C-7: header contains QoS depth");
        }

        private static void VerifyRuntimeSourceIntegration()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var publishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var publisherBase = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var wrapper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Ros2Bridge/Ros2BridgePublisher.cs");

            Check(manager.Contains("_ros2BridgeNamespace") && manager.Contains("_ros2BridgeQosPreset"),
                "96D-1: Manager owns bridge namespace and QoS settings");
            Check(manager.Contains("ResolveRos2BridgeQos") && manager.Contains("TryResolveRos2BridgeTopic"),
                "96D-2: Manager exposes topic and QoS resolvers");
            Check(publishing.Contains("effectiveTopic") && publishing.Contains("Ros2BridgeQosProfile"),
                "96D-3: Manager bridge publish path resolves effective topic and QoS");
            Check(publishing.Contains("new Ros2BridgeFrame(") && publishing.Contains("effectiveTopic") && publishing.Contains("qos"),
                "96D-4: Manager enqueues QoS-profiled bridge frames");
            Check(publisherBase.Contains("_ros2BridgeTopicOverride") && publisherBase.Contains("EffectiveRos2BridgeTopic"),
                "96D-5: Publisher base exposes bridge topic override and effective topic");
            Check(publisherBase.Contains("PublishRos2BridgeCdr(_topic, _ros2BridgeTopicOverride"),
                "96D-6: Publisher base passes override to Manager bridge publish path");
            Check(wrapper.Contains("new Ros2BridgeFrame(topic, schemaName, Ros2BridgeFrame.CdrEncoding"),
                "96D-7: Phase94/95 wrapper remains on legacy frame constructor");
        }

        private static void VerifySidecarSourceExpectations()
        {
            var sidecar = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");

            Check(sidecar.Contains("profileName") && sidecar.Contains("qos"),
                "96E-1: sidecar parses optional QoS header fields");
            Check(sidecar.Contains("qos.reliability must be reliable or best_effort"),
                "96E-2: sidecar rejects invalid reliability strings");
            Check(sidecar.Contains("qos.durability must be volatile or transient_local"),
                "96E-3: sidecar rejects invalid durability strings");
            Check(sidecar.Contains("qos.depth must be >= 1"),
                "96E-4: sidecar rejects invalid depth");
            Check(sidecar.Contains("create_generic_publisher(frame.topic, frame.schema_name, qos)"),
                "96E-5: sidecar applies requested QoS when creating publisher");
            Check(sidecar.Contains("topic reused with different schemaName or QoS"),
                "96E-6: sidecar rejects same-topic schema/QoS conflicts");
            Check(sidecar.Contains("reliability=%s durability=%s depth=%d"),
                "96E-7: sidecar logs publisher QoS details");
        }

        private static void VerifyInspectorSourceExpectations()
        {
            var managerEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var ros2BridgeEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Ros2Bridge.cs");
            var publishDataEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");
            var cameraEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var pointCloudEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            var normalizedManagerEditor = NormalizeLineEndings(managerEditor);

            Check(normalizedManagerEditor.Contains("DrawSection(\"ROS2 Bridge\"")
                  && !normalizedManagerEditor.Contains("Subheader(\"ROS2 Bridge\");\n            DrawRos2BridgeSection();"),
                "96F-1: Manager Inspector promotes ROS2 Bridge to top-level section");
            Check(ros2BridgeEditor.Contains("\"Bridge Namespace\"") && ros2BridgeEditor.Contains("\"QoS Preset\"") && ros2BridgeEditor.Contains("\"Effective QoS\""),
                "96F-2: Manager Inspector exposes topic namespace and QoS preset");
            Check(ros2BridgeEditor.Contains("\"Host\"") && ros2BridgeEditor.Contains("\"Default Output\"") && ros2BridgeEditor.Contains("\"Allow Publisher Override\""),
                "96F-3: Manager bridge labels are compact product labels");
            Check(cameraEditor.Contains("Bridge Topic Override") && pointCloudEditor.Contains("Bridge Topic Override"),
                "96F-4: custom publisher Inspectors expose topic override");
            Check(cameraEditor.Contains("Effective Bridge Topic") && pointCloudEditor.Contains("Effective Bridge Topic"),
                "96F-5: custom publisher Inspectors show effective bridge topic");
            Check(cameraEditor.Contains("Effective Bridge QoS") && pointCloudEditor.Contains("Effective Bridge QoS"),
                "96F-6: custom publisher Inspectors show effective bridge QoS");
        }

        private static string NormalizeLineEndings(string text)
            => text.Replace("\r\n", "\n").Replace('\r', '\n');

        private static string Resolve(string bridgeNamespace, string publisherTopic, string overrideTopic)
        {
            if (!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic(
                bridgeNamespace,
                publisherTopic,
                overrideTopic,
                out var effective,
                out var error))
            {
                throw new Exception(error);
            }

            return effective;
        }

        private static JObject ReadHeader(byte[] bytes)
        {
            var headerLength = ReadUInt32(bytes, 8);
            var headerJson = Encoding.UTF8.GetString(bytes, 16, checked((int)headerLength));
            return JObject.Parse(headerJson);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }
    }
}
