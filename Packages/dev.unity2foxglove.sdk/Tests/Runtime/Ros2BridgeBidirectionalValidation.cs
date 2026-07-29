// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase186 structural gate for the frozen Bridge authority and portable loop primitive.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Ros2BridgeBidirectionalValidation
    {
        private const string ProtocolFixture =
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json";
        private const string PreMoveFixture =
            "Packages/dev.unity2foxglove.sdk/Tests/Unit/Phase186/Fixtures/pre_move_bridge_and_mcap_vectors.json";
        private const string InventoryFixture =
            "Packages/dev.unity2foxglove.sdk/Tests/Unit/Phase186/Fixtures/pre_move_sdk_ros_inventory.json";
        private const string Provenance =
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/PROVENANCE.json";
        private const string CanonicalType =
            "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope";
        private const string InterfaceDigest =
            "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 186: ROS-free core and bidirectional ROS2 Bridge ===");
            _passed = 0;

            VerifyProtocolAuthority();
            VerifyPreMoveBehaviorAuthority();
            VerifyProvenanceAuthority();
            VerifyPortableOriginProbe();

            Console.WriteLine($"Phase 186: {_passed} checks passed.");
        }

        private static void VerifyProtocolAuthority()
        {
            var fixture = LoadJson(ProtocolFixture);
            var limits = (JObject)fixture["limits"];
            var negativeVectors = (JArray)fixture["negativeVectors"];

            Check(
                (int?)fixture["fixtureVersion"] == 1
                && (string)fixture["protocol"] == "U2R2"
                && (int?)limits["fixedHeaderBytes"] == 16
                && (int?)limits["maxJsonHeaderBytes"] == 65_536
                && (int?)limits["maxPayloadBytes"] == 67_108_864
                && (int?)limits["defaultQueueCapacityFrames"] == 1_024
                && (long?)limits["maxQueuedPayloadBytes"] == 68_719_476_736L
                && (int?)limits["activeConnectionCount"] == 1
                && (int?)limits["partialFrameStallMs"] == 5_000,
                "186-A1: shared U2R2 v1 limits remain explicit and bounded");

            Check(
                (string)fixture["health"]?["request"]?["header"]?["op"] == "health_ping"
                && (string)fixture["health"]?["response"]?["header"]?["op"] == "health_pong"
                && (string)fixture["preparePublisher"]?["request"]?["header"]?["op"] == "prepare_publisher"
                && (string)fixture["preparePublisher"]?["response"]?["header"]?["op"] == "publisher_ready"
                && (string)fixture["publish"]?["frame"]?["header"]?["op"] == "publish",
                "186-A2: shared health, preparation, and publish operations are frozen");

            var negativeIds = negativeVectors
                .Select(vector => (string)vector["id"])
                .ToArray();
            Check(
                negativeVectors.Count == 19
                && negativeIds.All(id => !string.IsNullOrWhiteSpace(id))
                && negativeIds.Distinct(StringComparer.Ordinal).Count() == negativeIds.Length,
                "186-A3: nineteen unique fail-closed protocol vectors remain shared across C# and C++");
        }

        private static void VerifyPreMoveBehaviorAuthority()
        {
            var fixture = LoadJson(PreMoveFixture);
            var publishers = (JArray)fixture["ordinaryPublishers"];
            var publisherIds = publishers.Select(item => (string)item["id"]).ToArray();
            var expectedIds = new[]
            {
                "transform",
                "scene",
                "compressed_image",
                "camera_calibration",
                "sensor_compressed_image",
                "sensor_camera_info",
                "laser_scan",
                "point_cloud",
                "sensor_point_cloud2",
                "compressed_point_cloud"
            };

            Check(
                publishers.Count == expectedIds.Length
                && publisherIds.SequenceEqual(expectedIds, StringComparer.Ordinal)
                && publishers.All(item =>
                    (string)item["schemaEncoding"] == "ros2msg"
                    && (string)item["messageEncoding"] == "cdr"
                    && (int?)item["payloadLength"] > 0
                    && ((string)item["payloadSha256"])?.Length == 64),
                "186-A4: every ordinary Bridge publisher has exact ordered pre-move CDR authority");

            var mcap = (JObject)fixture["mcap"];
            Check(
                (bool?)mcap["typedFactory"]?["available"] == true
                && (string)mcap["packageAbsent"]?["decodedKind"] == "Unsupported"
                && (string)mcap["typedFailure"]?["decodedKind"] == "Ros2CdrDiagnostic",
                "186-A5: typed ROS MCAP factory, absence, and diagnostic fallback behavior are frozen");

            var inventory = LoadJson(InventoryFixture);
            Check(
                (int?)inventory["schemaVersion"] == 1
                && (string)inventory["capturedFromHead"]
                    == "b5388cb4051750939776d217208f467f37aa86c6"
                && (int?)inventory["totalPathCount"] == 156
                && (string)inventory["totalPathDigestSha256"]
                    == "72aa3286e017673725c8b62b25cf02acd6dc7f65466db13669623753da815517",
                "186-A6: the exact pre-extraction SDK ROS inventory remains immutable");
        }

        private static void VerifyProvenanceAuthority()
        {
            var provenance = LoadJson(Provenance);
            var reference = (JObject)provenance["reference"];
            var implementations = (JArray)provenance["implementations"];

            Check(
                (int?)provenance["schemaVersion"] == 1
                && (string)reference["repository"]
                    == "https://github.com/Unity-Technologies/ROS-TCP-Connector.git"
                && (string)reference["revision"]
                    == "c27f00c6cf750d2d0564349b3039d19aa3925e7c"
                && (string)reference["license"] == "Apache-2.0"
                && (bool?)reference["materialCopied"] == false,
                "186-A7: official reference revision, license, and clean-room status are explicit");

            Check(
                implementations.Count >= 5
                && implementations.All(item =>
                    (string)item["classification"] == "original"
                    && !Path.IsPathRooted((string)item["path"])),
                "186-A8: every current Phase186 implementation has original, repository-relative provenance");
        }

        private static void VerifyPortableOriginProbe()
        {
            var build = Read("Scripts/smoke/foxrun/phase186_bridge_build.py");
            var probe = Read("Scripts/smoke/foxrun/phase186_bridge_capability_probe.py");
            var nativeProbe = Read(
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_origin_suppression.cpp");
            var rows = new[]
            {
                "humble-fastrtps",
                "jazzy-fastrtps",
                "lyrical-fastrtps",
                "lyrical-zenoh"
            };

            Check(
                rows.All(row => build.Contains("\"" + row + "\"", StringComparison.Ordinal))
                && build.Contains(
                    "\"unity2foxglove_foxrun_interfaces_v1/msg/\"",
                    StringComparison.Ordinal)
                && build.Contains(
                    "\"Phase181State48D288ED82F1Envelope\"",
                    StringComparison.Ordinal)
                && build.Contains(InterfaceDigest, StringComparison.Ordinal)
                && build.Contains("\"NOT RUN\"", StringComparison.Ordinal),
                "186-A9: build evidence is exact-row, exact-interface, and honestly fail-closed");

            Check(
                probe.Contains("publisher_gid_take_serialized", StringComparison.Ordinal)
                && nativeProbe.Contains("take_serialized", StringComparison.Ordinal)
                && nativeProbe.Contains("publisher_gid", StringComparison.Ordinal)
                && nativeProbe.Contains("ignore_local_publications = true", StringComparison.Ordinal),
                "186-A10: one portable GID primitive distinguishes local and independent publishers");
        }

        private static JObject LoadJson(string relativePath)
            => JObject.Parse(Read(relativePath));

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(
                Root(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string Root()
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not find repository root.");

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
