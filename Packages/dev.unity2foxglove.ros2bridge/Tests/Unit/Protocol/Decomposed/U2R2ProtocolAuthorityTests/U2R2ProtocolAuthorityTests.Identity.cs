// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Tests/Protocol
// Purpose: Cross-language authority for bounded U2R2 replay, ordering, and budgets.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests.Unit.Protocol
{
    public sealed partial class U2R2ProtocolAuthorityTests
    {
        private static void ContractIdentityValidation(JObject scenario)
        {
            foreach (var topic in scenario["validTopics"].Values<string>())
            {
                var parsed = ParseRegistration(
                    topic,
                    "sensor_msgs/msg/Image",
                    scenario["validQos"][0]);
                Assert.Equal(topic, parsed.Topic);
                Assert.Equal("sensor_msgs/msg/Image", parsed.SchemaName);
            }
            foreach (var topic in scenario["invalidTopics"].Values<string>())
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        topic,
                        "sensor_msgs/msg/Image",
                        scenario["validQos"][0]));
            foreach (var type in scenario["validTypes"].Values<string>())
            {
                var parsed = ParseRegistration(
                    "/camera/front",
                    type,
                    scenario["validQos"][0]);
                Assert.Equal(type, parsed.SchemaName);
            }
            foreach (var type in scenario["invalidTypes"].Values<string>())
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        "/camera/front",
                        type,
                        scenario["validQos"][0]));
            foreach (var qos in scenario["validQos"].Values<JObject>())
            {
                var parsed = ParseRegistration(
                    "/camera/front",
                    "sensor_msgs/msg/Image",
                    qos);
                Assert.NotNull(parsed.Qos);
                Assert.Equal(qos.Value<string>("profile"), parsed.Qos.Profile);
                Assert.Equal(qos.Value<string>("reliability"), parsed.Qos.Reliability);
                Assert.Equal(qos.Value<string>("durability"), parsed.Qos.Durability);
                Assert.Equal(qos.Value<string>("history"), parsed.Qos.History);
                Assert.Equal(qos.Value<uint>("depth"), parsed.Qos.Depth);
            }
            foreach (var qos in scenario["invalidQos"].Values<JObject>())
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        "/camera/front",
                        "sensor_msgs/msg/Image",
                        qos));
            foreach (var mutation in scenario["invalidQosShapes"].Values<string>())
            {
                var invalid = InvalidQosShape(
                    Assert.IsType<JObject>(scenario["validQos"][0]),
                    mutation);
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        "/camera/front",
                        "sensor_msgs/msg/Image",
                        invalid));
            }
            var boundaries = Assert.IsType<JObject>(
                scenario["typeLengthBoundaries"]);
            var validPackage = new string(
                'a',
                boundaries.Value<int>("validPackageLength"));
            var invalidPackage = new string(
                'a',
                boundaries.Value<int>("invalidPackageLength"));
            var validType =
                "T"
                + new string(
                    'a',
                    boundaries.Value<int>("validTypeLength") - 1);
            var invalidType =
                "T"
                + new string(
                    'a',
                    boundaries.Value<int>("invalidTypeLength") - 1);
            ParseRegistration(
                "/camera/front",
                validPackage + "/msg/" + validType,
                scenario["validQos"][0]);
            AssertProtocolError(
                scenario,
                () => ParseRegistration(
                    "/camera/front",
                    invalidPackage + "/msg/Image",
                    scenario["validQos"][0]));
            AssertProtocolError(
                scenario,
                () => ParseRegistration(
                    "/camera/front",
                    "sensor_msgs/msg/" + invalidType,
                    scenario["validQos"][0]));
            foreach (var invalid in scenario["invalidDirections"].Values<uint>())
            {
                AssertProtocolError(
                    scenario,
                    () => new U2R2ContractIdentity(
                        new U2R2ContractKey(41, 7),
                        (U2R2ContractDirection)invalid,
                        "/camera/front",
                        "sensor_msgs/msg/Image",
                        new U2R2Qos(
                            "default",
                            "reliable",
                            "volatile",
                            "keep_last",
                            10)));
            }
        }

        private static JToken InvalidQosShape(
            JObject valid,
            string mutation)
        {
            if (mutation == "non_object")
                return new JValue("default");
            var value = (JObject)valid.DeepClone();
            switch (mutation)
            {
                case "missing_axis":
                    value.Remove("profile");
                    break;
                case "extra_axis":
                    value["deadline"] = 1;
                    break;
                case "profile_non_string":
                    value["profile"] = 1;
                    break;
                case "reliability_non_string":
                    value["reliability"] = 1;
                    break;
                case "durability_non_string":
                    value["durability"] = 1;
                    break;
                case "history_non_string":
                    value["history"] = 1;
                    break;
                case "depth_negative":
                    value["depth"] = -1;
                    break;
                case "depth_fraction":
                    value["depth"] = 1.5;
                    break;
                case "depth_above_uint32":
                    value["depth"] = 4294967296UL;
                    break;
                default:
                    throw new InvalidOperationException(mutation);
            }
            return value;
        }

        private static void ContractIdentityAliasAndReplay(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var request = RequestBytes("register_subscription", identity);
            var registerId = scenario.Value<ulong>("registerRequestId");
            var readyResponse = replay.Admit(
                registerId,
                request,
                1,
                scheduler);
            var registration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            contracts.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            DrainOne(scheduler);

            var repeated = replay.Admit(registerId, request, 1, scheduler);
            Assert.Equal(
                U2R2ReplayDecision.ReplayCached,
                repeated.Decision);
            var replayedRegistration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                repeated);
            Assert.True(replayedRegistration.Replayed);
            contracts.CommitReady(
                replayedRegistration,
                replay,
                repeated,
                U2R2OutboundFrame.Control("must-not-send", Bytes("01")));
            Assert.Equal(
                scenario.Value<ulong>("expectedResponseCount"),
                scheduler.TotalQueuedDepth);
            Assert.Equal("replay:" + registerId, DrainOne(scheduler).Token);

            var requestId = scenario.Value<ulong>("aliasStartRequestId");
            foreach (var mutation in scenario["aliasMutations"].Values<string>())
            {
                var alias = AliasIdentity(identity, mutation);
                var aliasResponse = replay.Admit(
                    requestId++,
                    RequestBytes("register_subscription", alias),
                    1,
                    scheduler);
                AssertProtocolError(
                    scenario,
                    () => contracts.BeginRegistration(
                        alias,
                        scheduler,
                        replay,
                        aliasResponse));
                Assert.Contains(
                    "subscription_ready",
                    DrainOne(scheduler).Token,
                    StringComparison.Ordinal);
            }
            Assert.Equal(1UL, contracts.ContractCount);
        }

        private static U2R2ContractIdentity AliasIdentity(
            U2R2ContractIdentity identity,
            string mutation)
        {
            var direction = identity.Direction;
            var topic = identity.Topic;
            var schemaName = identity.SchemaName;
            var profile = identity.Qos.Profile;
            var reliability = identity.Qos.Reliability;
            var durability = identity.Qos.Durability;
            var history = identity.Qos.History;
            var depth = identity.Qos.Depth;
            switch (mutation)
            {
                case "topic":
                    topic = "/camera/rear";
                    break;
                case "schemaName":
                    schemaName = "demo_interfaces/msg/Telemetry";
                    break;
                case "profile":
                    profile = "sensor_data";
                    break;
                case "reliability":
                    reliability = "best_effort";
                    break;
                case "durability":
                    durability = "transient_local";
                    break;
                case "history":
                    history = "keep_all";
                    depth = 0;
                    break;
                case "depth":
                    depth = 11;
                    break;
                case "direction":
                    direction = U2R2ContractDirection.Publish;
                    break;
                default:
                    throw new InvalidOperationException(mutation);
            }
            return new U2R2ContractIdentity(
                identity.Key,
                direction,
                topic,
                schemaName,
                new U2R2Qos(
                    profile,
                    reliability,
                    durability,
                    history,
                    depth));
        }

    }
}
