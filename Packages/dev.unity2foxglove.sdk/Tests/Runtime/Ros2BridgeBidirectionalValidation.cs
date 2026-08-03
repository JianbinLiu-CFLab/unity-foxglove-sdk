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
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Ros2BridgeBidirectionalValidation
    {
        private const string ProtocolFixture =
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json";
        private const string PreMoveFixture =
            "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Fixtures/pre_move_bridge_and_mcap_vectors.json";
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
            VerifyProviderGenerationBoundary();
            VerifyGeneratedStandardCdrBehavior();
            VerifyPhysicalLeaseSharingBehavior();
            VerifyGeneratedDuplexProbeAuthority();

            Console.WriteLine($"Phase 186: {_passed} checks passed.");
        }

        private static void VerifyProtocolAuthority()
        {
            var fixture = LoadJson(ProtocolFixture);
            var limits = (JObject)fixture["limits"];
            var negativeVectors = (JArray)fixture["negativeVectors"];
            var executableV2Negatives =
                (JArray)fixture["v2"]?["negativeVectors"];
            var executableV1Authority =
                (JObject)fixture["v2"]?["legacyV1NegativeExecution"];
            var executableV1Negatives =
                (JArray)executableV1Authority?["vectors"];

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
                && negativeIds.Distinct(StringComparer.Ordinal).Count()
                    == negativeIds.Length
                && negativeVectors.All(
                    vector => (string)vector["expected"] == "reject")
                && (int?)executableV1Authority?["schemaVersion"] == 1
                && (string)executableV1Authority?["catalog"]
                    == "negativeVectors"
                && executableV1Negatives != null
                && executableV1Negatives.Count == negativeVectors.Count
                && executableV1Negatives
                    .Select(vector => (string)vector["id"])
                    .SequenceEqual(negativeIds, StringComparer.Ordinal)
                && executableV1Negatives.All(
                    vector => !string.IsNullOrWhiteSpace(
                                  (string)vector["action"])
                              && !string.IsNullOrWhiteSpace(
                                  (string)vector["expectedFailure"])
                              && vector["consumers"] is JArray consumers
                              && consumers.Count > 0
                              && consumers.All(
                                  consumer => (string)consumer == "csharp"
                                              || (string)consumer == "cpp"))
                && executableV2Negatives != null
                && executableV2Negatives.Count == 51
                && executableV2Negatives.All(
                    vector => !string.IsNullOrWhiteSpace(
                                  (string)vector["action"])
                              && !string.IsNullOrWhiteSpace(
                                  (string)vector["expectedErrorCode"])
                              && vector["terminal"]?.Type
                                  == JTokenType.Boolean),
                "186-A3: all 19 frozen v1 negative IDs bind to versioned role-aware executable actions, while all 51 v2 negatives carry cross-language actions and exact errors");
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
                && (string)mcap["typedFailure"]?["decodedKind"] == "Provider"
                && (string)mcap["typedFailure"]?["decoderId"]
                    == "unity2foxglove.ros2bridge/cdr-diagnostic",
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
            var notices = Read("THIRD_PARTY_NOTICES.md");

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

            Check(
                notices.Contains(
                    "Unity-Technologies/ROS-TCP-Connector (reference-only review)",
                    StringComparison.Ordinal)
                && notices.Contains(
                    "no implementation code or comments were copied",
                    StringComparison.Ordinal)
                && notices.Contains(
                    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/PROVENANCE.json",
                    StringComparison.Ordinal),
                "186-A8N: third-party notices preserve the reference-only clean-room boundary");
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

        private static void VerifyProviderGenerationBoundary()
        {
            var core = Read(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var fanout = Read(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportContributions.cs");
            var bridge = Read(
                "Packages/dev.unity2foxglove.ros2bridge/Editor/FoxRun/Ros2CustomCdrEmitter.cs");
            var provider = Read(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs");

            Check(
                fanout.Contains(
                    "internal static class FoxRunGeneratedTransportFanout",
                    StringComparison.Ordinal)
                && fanout.Contains(
                    "internal static FoxRunGeneratedTransportFanoutResult Publish(",
                    StringComparison.Ordinal)
                && core.Contains(
                    "IFoxRunRemoteOwnershipSource",
                    StringComparison.Ordinal)
                && !core.Contains("Ros2Bridge", StringComparison.Ordinal)
                && !core.Contains("Ros2Cdr", StringComparison.Ordinal)
                && !core.Contains("U2R2", StringComparison.Ordinal)
                && !fanout.Contains("Ros2Bridge", StringComparison.Ordinal)
                && !fanout.Contains("Ros2Cdr", StringComparison.Ordinal)
                && !fanout.Contains("U2R2", StringComparison.Ordinal),
                "186-F1: core generation owns neutral fanout/origin and no Bridge, CDR, or U2R2 branch");

            Check(
                bridge.Contains(
                    "IFoxRunBridgeGeneratedSubscribeSource",
                    StringComparison.Ordinal)
                && bridge.Contains(
                    "Ros2CdrDeserializerRegistry.TryGetByClrType",
                    StringComparison.Ordinal)
                && bridge.Contains(
                    "EnsureFullyConsumed",
                    StringComparison.Ordinal)
                && !bridge.Contains(
                    "System.Reflection",
                    StringComparison.Ordinal)
                && provider.Contains(
                    "Ros2BridgeGeneratedSubscriptionRuntime",
                    StringComparison.Ordinal),
                "186-F2: Bridge generation owns direct standard/custom CDR input with no reflection fallback");
        }

        private static void VerifyGeneratedStandardCdrBehavior()
        {
            var expected = Ros2CdrSampleFactory.CreateLogSample();
            var payload = Ros2CdrGeneratedSerializers.Serialize(expected);
            Check(
                Ros2CdrDeserializerRegistry.TryGetByClrType(
                    typeof(global::Foxglove.Log),
                    out var entry)
                && string.Equals(
                    entry.SchemaName,
                    "foxglove_msgs/msg/Log",
                    StringComparison.Ordinal)
                && entry.Deserialize(payload).Equals(expected),
                "186-F3: generated standard CDR writer and direct reader round-trip the same typed value");

            var trailing = payload.Concat(new byte[] { 0xff }).ToArray();
            var rejectedTrailing = false;
            try
            {
                entry.Deserialize(trailing);
            }
            catch (InvalidDataException)
            {
                rejectedTrailing = true;
            }
            Check(
                rejectedTrailing,
                "186-F4: generated standard CDR input rejects trailing root bytes");
        }

        private static void VerifyPhysicalLeaseSharingBehavior()
        {
            var state = new Ros2BridgeSessionState(
                new Ros2BridgeSessionSettings(
                    "127.0.0.1",
                    8765,
                    186,
                    U2R2ProtocolLimits.Default));
            var wire = new RecordingWireController();
            using var registry = new Ros2BridgeContractLeaseRegistry(
                186,
                4,
                state,
                wire);
            var contract = new Ros2BridgeSessionContract(
                new FoxRunTransportId("unity2foxglove.ros2bridge"),
                FoxRunTransportDirection.Subscribe,
                "/phase186/f/lease",
                CanonicalType,
                FoxRunResolvedQos.Default,
                "phase186-f-shared-binding",
                18601,
                186);

            if (!registry.TryAcquire(
                    contract,
                    out var first,
                    out var firstReason))
            {
                throw new InvalidOperationException(firstReason);
            }
            if (!registry.TryAcquire(
                    contract,
                    out var second,
                    out var secondReason))
            {
                throw new InvalidOperationException(secondReason);
            }
            if (!registry.TryRelease(
                    first,
                    out var firstReleaseReason))
            {
                throw new InvalidOperationException(firstReleaseReason);
            }
            var firstReleaseKeptWire = wire.Unregistered.Count == 0
                                       && state.IsLocallyActive(contract);
            if (!registry.TryRelease(
                    second,
                    out var secondReleaseReason))
            {
                throw new InvalidOperationException(secondReleaseReason);
            }

            Check(
                wire.Registered.Count == 1
                && firstReleaseKeptWire
                && wire.Unregistered.Count == 1
                && registry.ActiveLeaseCount == 0
                && !state.IsLocallyActive(contract),
                "186-F5: identical logical subscriptions share first-acquire/last-release wire ownership");
        }

        private static void VerifyGeneratedDuplexProbeAuthority()
        {
            var build = Read(
                "Scripts/smoke/foxrun/phase186_bridge_build.py");
            var cmake = Read(
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/CMakeLists.txt");
            var probe = Read(
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_generated_duplex.cpp");

            Check(
                build.Contains(
                    "STANDARD_SCHEMA_TYPE = \"foxglove_msgs/msg/Log\"",
                    StringComparison.Ordinal)
                && build.Contains(
                    "generatedDuplexProbe",
                    StringComparison.Ordinal)
                && cmake.Contains(
                    "test_generated_duplex",
                    StringComparison.Ordinal)
                && probe.Contains(
                    "GeneratedFoxgloveLogIsSuppressedLocallyAndForwardedExternally",
                    StringComparison.Ordinal)
                && probe.Contains(
                    "Phase181EnvelopeIsSuppressedLocallyAndForwardedExternally",
                    StringComparison.Ordinal)
                && probe.Contains(
                    "unity2foxglove_foxrun_interfaces_v1/msg/",
                    StringComparison.Ordinal)
                && probe.Contains(
                    "Phase181State48D288ED82F1Envelope",
                    StringComparison.Ordinal),
                "186-F6: Jazzy/FastDDS live certification requires generated-standard and exact Phase181 duplex probes");
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

        private sealed class RecordingWireController :
            IRos2BridgeContractWireController
        {
            internal List<Ros2BridgeSessionContract> Registered { get; }
                = new List<Ros2BridgeSessionContract>();

            internal List<Ros2BridgeSessionContract> Unregistered { get; }
                = new List<Ros2BridgeSessionContract>();

            public Ros2BridgeSessionResult Register(
                Ros2BridgeSessionContract contract)
            {
                Registered.Add(contract);
                return Ros2BridgeSessionResult.Accepted();
            }

            public Ros2BridgeSessionResult Unregister(
                Ros2BridgeSessionContract contract)
            {
                Unregistered.Add(contract);
                return Ros2BridgeSessionResult.Accepted();
            }
        }
    }
}
