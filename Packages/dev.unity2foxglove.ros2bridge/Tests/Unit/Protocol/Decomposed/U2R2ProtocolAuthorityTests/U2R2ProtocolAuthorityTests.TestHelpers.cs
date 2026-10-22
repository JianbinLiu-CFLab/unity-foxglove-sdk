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
        private static void ReplayResponsesRespectControlFairness(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(("controlBurstLimit", 2UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var replay = new U2R2RequestReplayAuthority(bounded);
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "data",
                        new U2R2ContractKey(1, 1),
                        1,
                        Bytes("01")),
                    U2R2QueueOverflowPolicy.Reject));
            foreach (var requestId in scenario["requestIds"].Values<ulong>())
            {
                var response = replay.Admit(
                    requestId,
                    new[] { checked((byte)requestId) },
                    1,
                    scheduler);
                replay.Complete(
                    response,
                    new[] { checked((byte)(requestId + 1)) });
            }
            Assert.Equal(
                scenario["expectedOrder"].Values<string>(),
                DrainAll(scheduler));
        }

        private static void ResponseTransactionRejectsWrongScheduler(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var original = new U2R2BoundedOutboundScheduler(limits);
            var wrong = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var response = replay.Admit(
                scenario.Value<ulong>("requestId"),
                RequestBytes("register_subscription", identity),
                1,
                original);
            Assert.Throws<InvalidOperationException>(
                () => contracts.BeginRegistration(
                    identity,
                    wrong,
                    replay,
                    response));
            replay.CancelPending(response);
            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingRequests"),
                replay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedReservedDepth"),
                original.TotalQueuedDepth);
        }

        private static void CodecConsumesSessionLimitSnapshot(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("maxHeaderBytes", scenario.Value<ulong>("maxHeaderBytes")),
                ("maxPayloadBytes", scenario.Value<ulong>("maxPayloadBytes")),
                ("maxJsonDepth", scenario.Value<ulong>("maxJsonDepth")));
            var simple = new JObject { ["ok"] = 1 };
            U2R2ProtocolCodec.EncodeFrame(simple, Bytes("01"), bounded);
            AssertProtocolError(
                scenario.Value<string>("expectedWireErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    new JObject { ["padding"] = new string('x', 100) },
                    Array.Empty<byte>(),
                    bounded));
            AssertProtocolError(
                scenario.Value<string>("expectedWireErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    simple,
                    Bytes("0102"),
                    bounded));
            AssertProtocolError(
                scenario.Value<string>("expectedWireErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    new JObject
                    {
                        ["one"] = new JObject
                        {
                            ["two"] = new JObject
                            {
                                ["three"] = 1,
                            },
                        },
                    },
                    Array.Empty<byte>(),
                    bounded));
            var invalidFixed = limits.With(("fixedFrameBytes", 1UL));
            AssertProtocolError(
                scenario.Value<string>("expectedConfigurationErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    simple,
                    Array.Empty<byte>(),
                    invalidFixed));
        }

        private static U2R2Message ParseRegistration(
            string topic,
            string schemaName,
            JToken qos)
        {
            var header = RegisterSubscriptionHeader();
            header["topic"] = topic;
            header["schemaName"] = schemaName;
            header["qos"] = qos.DeepClone();
            return U2R2ProtocolCodec.ParseV2(
                new U2R2Frame(header, Array.Empty<byte>()));
        }

        private static JObject RegisterSubscriptionHeader()
            => (JObject)Assert.IsType<JArray>(
                    Assert.IsType<JObject>(LoadFixture()["v2"])["operations"])
                .Values<JObject>()
                .Single(vector => string.Equals(
                    vector.Value<string>("id"),
                    "register_subscription",
                    StringComparison.Ordinal))["header"]
                .DeepClone();

        private static void PurePeerCloseTransition(JObject scenario)
        {
            var lifecycle = new U2R2PureSessionLifecycle();
            AssertProtocolError(scenario, () => lifecycle.PeerClosed());
            Assert.Equal(
                ParseLifecycleState(scenario.Value<string>("expectedState")),
                lifecycle.State);
        }

        private static void PureTimeoutTransitions(JObject scenario)
        {
            var limits = LimitsFrom(
                Assert.IsType<JObject>(LoadAuthority()["limits"]));
            foreach (var kind in scenario["timeoutKinds"].Values<string>())
            {
                var lifecycle = new U2R2PureSessionLifecycle(limits);
                var parsedKind = ParseTimeoutKind(kind);
                var limit = lifecycle.LimitFor(parsedKind);
                Assert.False(lifecycle.HasTimedOut(parsedKind, limit - 1));
                AssertProtocolError(
                    scenario,
                    () => lifecycle.Timeout(
                        parsedKind,
                        limit));
                Assert.Equal(U2R2PureSessionState.Closed, lifecycle.State);
                var overLimit = new U2R2PureSessionLifecycle(limits);
                AssertProtocolError(
                    scenario,
                    () => overLimit.Timeout(parsedKind, limit + 1));
            }
        }

        private static void RegisterAndRemove(
            U2R2ContractAuthority authority,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ContractKey key,
            ulong firstRequestId)
        {
            var identity = Identity(key);
            var readyResponse = replay.Admit(
                firstRequestId,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = authority.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            authority.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            DrainOne(scheduler);
            var removedResponse = replay.Admit(
                checked(firstRequestId + 1),
                RequestBytes("unregister_subscription", identity),
                1,
                scheduler);
            var removal = authority.BeginUnregister(
                identity,
                scheduler,
                replay,
                removedResponse);
            Assert.True(
                authority.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removedResponse,
                    U2R2OutboundFrame.Control("subscription_removed", Bytes("02"))));
            DrainOne(scheduler);
        }

        private static void RegisterReady(
            U2R2ContractAuthority authority,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ContractIdentity identity,
            ulong requestId)
        {
            var response = replay.Admit(
                requestId,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = authority.BeginRegistration(
                identity,
                scheduler,
                replay,
                response);
            authority.CommitReady(
                registration,
                replay,
                response,
                U2R2OutboundFrame.Control(
                    "subscription_ready",
                    Bytes("01")));
            DrainOne(scheduler);
        }

        private static U2R2ContractIdentity Identity(
            U2R2ContractKey key,
            string topic = "/camera/front",
            string schemaName = "sensor_msgs/msg/Image",
            U2R2ContractDirection direction = U2R2ContractDirection.Subscribe)
            => new U2R2ContractIdentity(
                key,
                direction,
                topic,
                schemaName,
                new U2R2Qos(
                    "default",
                    "reliable",
                    "volatile",
                    "keep_last",
                    10));

        private static byte[] RequestBytes(
            string operation,
            U2R2ContractIdentity identity)
        {
            var value = new JObject
            {
                ["op"] = operation,
                ["contractId"] = identity.Key.ContractId,
                ["generation"] = identity.Key.Generation,
                ["direction"] =
                    identity.Direction == U2R2ContractDirection.Publish
                        ? "publish"
                        : "subscribe",
                ["topic"] = identity.Topic,
                ["schemaName"] = identity.SchemaName,
                ["qos"] = new JObject
                {
                    ["profile"] = identity.Qos.Profile,
                    ["reliability"] = identity.Qos.Reliability,
                    ["durability"] = identity.Qos.Durability,
                    ["history"] = identity.Qos.History,
                    ["depth"] = identity.Qos.Depth,
                },
            };
            return Encoding.UTF8.GetBytes(
                value.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static U2R2ContractKey Key(JObject scenario)
            => new U2R2ContractKey(
                scenario.Value<ulong>("contractId"),
                scenario.Value<ulong>("generation"));

        private static U2R2OutboundFrame DefaultSemanticErrorFrame(
            U2R2Operation operation,
            ulong requestId,
            U2R2ProtocolException error)
            => U2R2OutboundFrame.Control(
                OperationToken(operation)
                + ":"
                + requestId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + error.ErrorCode,
                Bytes("ee"));

        private static string OperationToken(U2R2Operation operation)
        {
            switch (operation)
            {
                case U2R2Operation.SubscriptionReady:
                    return "subscription_ready";
                case U2R2Operation.SubscriptionRemoved:
                    return "subscription_removed";
                default:
                    throw new InvalidOperationException(
                        "Unexpected contract response operation.");
            }
        }

        private static void EnqueueControl(
            U2R2BoundedOutboundScheduler scheduler,
            string token)
        {
            Assert.True(scheduler.TryReserveControl(1, out var reservation));
            reservation.Commit(U2R2OutboundFrame.Control(token, Bytes("01")));
        }

        private static U2R2OutboundFrame DrainOne(
            U2R2BoundedOutboundScheduler scheduler)
        {
            Assert.True(scheduler.TryBeginWrite(out var writer));
            var frame = writer.Frame;
            writer.Dispose();
            return frame;
        }

        private static string[] DrainAll(U2R2BoundedOutboundScheduler scheduler)
        {
            var tokens = new List<string>();
            while (scheduler.TryBeginWrite(out var writer))
            {
                tokens.Add(writer.Frame.Token);
                writer.Dispose();
            }
            return tokens.ToArray();
        }

        private static U2R2ProtocolLimits LimitsFrom(JObject source)
            => U2R2ProtocolLimits.FromDiagnosticSnapshot(
                source.Properties().ToDictionary(
                    property => property.Name,
                    property => property.Value.Value<ulong>(),
                    StringComparer.Ordinal));

        private static void SetSchedulerCounter(
            U2R2BoundedOutboundScheduler scheduler,
            string fieldName,
            ulong value)
        {
            var field = typeof(U2R2BoundedOutboundScheduler).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(scheduler, value);
        }

        private static U2R2ReplayDecision ParseReplayDecision(string value)
            => value == "replay_cached"
                ? U2R2ReplayDecision.ReplayCached
                : throw new InvalidOperationException(value);

        private static U2R2MessageAdmission ParseMessageAdmission(string value)
            => value == "late_tombstone"
                ? U2R2MessageAdmission.LateTombstone
                : throw new InvalidOperationException(value);

        private static U2R2EnqueueDisposition ParseEnqueueResult(string value)
        {
            switch (value)
            {
                case "dropped_oldest":
                    return U2R2EnqueueDisposition.DroppedOldest;
                case "replaced_latest":
                    return U2R2EnqueueDisposition.ReplacedLatest;
                default:
                    throw new InvalidOperationException(value);
            }
        }

        private static U2R2PureSessionState ParseLifecycleState(string value)
            => value == "closed"
                ? U2R2PureSessionState.Closed
                : throw new InvalidOperationException(value);

        private static U2R2TimeoutKind ParseTimeoutKind(string value)
        {
            switch (value)
            {
                case "handshake":
                    return U2R2TimeoutKind.Handshake;
                case "partial_frame":
                    return U2R2TimeoutKind.PartialFrame;
                case "read":
                    return U2R2TimeoutKind.Read;
                case "write":
                    return U2R2TimeoutKind.Write;
                case "join":
                    return U2R2TimeoutKind.Join;
                case "shutdown":
                    return U2R2TimeoutKind.Shutdown;
                default:
                    throw new InvalidOperationException(value);
            }
        }

        private static void AssertProtocolError(JObject scenario, Action action)
            => AssertProtocolError(
                scenario.Value<string>("expectedErrorCode"),
                scenario.Value<bool>("terminal"),
                action);

        private static void AssertProtocolError(
            string code,
            bool terminal,
            Action action)
        {
            var exception = Assert.Throws<U2R2ProtocolException>(action);
            Assert.Equal(code, exception.ErrorCode);
            Assert.Equal(terminal, exception.Terminal);
        }

        private static ulong ParseUlong(JToken token)
            => ulong.Parse(token.Value<string>(), CultureInfo.InvariantCulture);

        private static byte[] Bytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            return bytes;
        }

        private static JObject LoadAuthority()
            => Assert.IsType<JObject>(
                JObject.Parse(File.ReadAllText(FindFixture()))["v2"]["commit2"]);

        private static JObject LoadFixture()
            => JObject.Parse(File.ReadAllText(FindFixture()));

        private static string FindFixture()
        {
            const string relative =
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/"
                + "u2r2_protocol_vectors.json";
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "Packages"))
                        && Directory.Exists(Path.Combine(current.FullName, "Tools")))
                    {
                        return Path.Combine(
                            current.FullName,
                            relative.Replace('/', Path.DirectorySeparatorChar));
                    }
                    current = current.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate the U2R2 authority fixture.");
        }
    }
}
