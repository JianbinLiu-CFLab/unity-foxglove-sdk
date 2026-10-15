// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "184-D")]
    [Trait("Domain", "Ros2Bridge")]
    [Collection(
        Unity2Foxglove.Ros2Bridge.UnitTests
            .Ros2BridgeNetworkTimingSerialCollection.Name)]
    /// <remarks>Phase192 source sections: PublisherRuntimeBehaviorTests.cs, PublisherTransportLifecycle.cs, PublisherAuthorityFixtures.cs, PublisherFrameCodec.cs</remarks>
    public sealed partial class Ros2BridgePublisherPreparationTests
    {
        [Fact]
        public void TopicAdmissionEnforcesFinalRosNameLength()
        {
            var maximum = "/" + new string('a', 254);
            var oversized = "/" + new string('a', 255);
            Assert.True(Ros2BridgeTopicProfile.IsValidRos2TopicName(maximum));
            Assert.False(Ros2BridgeTopicProfile.IsValidRos2TopicName(oversized));

            Assert.False(
                Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic(
                    "/" + new string('n', 127),
                    "/" + new string('p', 127),
                    string.Empty,
                    out _,
                    out var error));
            Assert.Contains("255", error, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedPublisherCarriesDefaultQosForV2Preparation()
        {
            using var transport = new LegacyTransport();
            transport.Connect("127.0.0.1", 19484, 1000);
            var publisher = new Ros2BridgePublisher(transport);

            publisher.Publish(
                "/phase186/generated/default_qos",
                Ros2PublisherSchemaNames.FrameTransform,
                new global::Foxglove.FrameTransform
                {
                    Translation = new global::Foxglove.Vector3(),
                    Rotation = new global::Foxglove.Quaternion(),
                },
                logTimeNs: 186UL);

            var frame = Assert.Single(transport.SentFrames);
            Assert.Equal(FoxRunResolvedQos.Default, frame.Qos);
        }

        [Fact]
        public void FirstProbeIsPendingUntilCorrelatedExactContractAck()
        {
            var transport = new PreparationTransport("ok");
            using var runtime = Runtime(() => transport);
            runtime.Start(enabled: true, autoConnect: true);

            var initial = runtime.PreparePublisher(
                "/phase184/custom",
                "phase184_msgs/msg/CustomEnvelope",
                FoxRunResolvedQos.SensorData,
                out var initialReason);

            Assert.Equal(Ros2BridgePublisherReadiness.Pending, initial);
            Assert.Contains("pending", initialReason, StringComparison.OrdinalIgnoreCase);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/custom",
                          "phase184_msgs/msg/CustomEnvelope",
                          FoxRunResolvedQos.SensorData,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));

            var request = Ros2BridgePublisherPreparationCodec.ParseRequest(
                Assert.Single(transport.Requests));
            Assert.Equal(1, request.ProtocolVersion);
            Assert.False(string.IsNullOrWhiteSpace(request.RequestId));
            Assert.Equal("/phase184/custom", request.Topic);
            Assert.Equal("phase184_msgs/msg/CustomEnvelope", request.SchemaName);
            Assert.Equal("cdr", request.Encoding);
            Assert.Equal(FoxRunResolvedQos.SensorData, request.Qos);
            Assert.Empty(transport.SentFrames);
        }

        [Fact]
        public void RejectedAckAndLegacyTransportFailClosed()
        {
            var rejectedTransport = new PreparationTransport(
                "error",
                "publisher_unavailable",
                "typesupport unavailable");
            using (var rejectedRuntime = Runtime(() => rejectedTransport))
            {
                rejectedRuntime.Start(enabled: true, autoConnect: true);
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    rejectedRuntime.PreparePublisher(
                        "/phase184/rejected",
                        "phase184_msgs/msg/Rejected",
                        FoxRunResolvedQos.Default,
                        out _));
                Assert.True(SpinWait.SpinUntil(
                    () => rejectedRuntime.PreparePublisher(
                              "/phase184/rejected",
                              "phase184_msgs/msg/Rejected",
                              FoxRunResolvedQos.Default,
                              out _)
                          == Ros2BridgePublisherReadiness.Rejected,
                    TimeSpan.FromSeconds(3)));
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Rejected,
                    rejectedRuntime.PreparePublisher(
                        "/phase184/rejected",
                        "phase184_msgs/msg/Rejected",
                        FoxRunResolvedQos.Default,
                        out var reason));
                Assert.Contains("typesupport unavailable", reason, StringComparison.Ordinal);
            }

            var legacy = new LegacyTransport();
            using var legacyRuntime = Runtime(() => legacy);
            legacyRuntime.Start(enabled: true, autoConnect: true);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                legacyRuntime.PreparePublisher(
                    "/phase184/legacy",
                    "phase184_msgs/msg/Legacy",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => legacyRuntime.PreparePublisher(
                          "/phase184/legacy",
                          "phase184_msgs/msg/Legacy",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Rejected,
                TimeSpan.FromSeconds(3)));
        }

        [Fact]
        public void ReconnectInvalidatesReadyAndRequeuesEveryExactContract()
        {
            var firstSendGate = new ManualResetEventSlim(false);
            var first = new PreparationTransport("ok")
            {
                FailNextPublish = true,
                SendGate = firstSendGate
            };
            var responseGate = new ManualResetEventSlim(false);
            var second = new PreparationTransport("ok") { ResponseGate = responseGate };
            var transport = new ReconnectSequenceTransport(first, second);
            using var runtime = Runtime(() => transport);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/reconnect",
                    "phase184_msgs/msg/Reconnect",
                    FoxRunResolvedQos.SystemDefault,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/reconnect",
                          "phase184_msgs/msg/Reconnect",
                          FoxRunResolvedQos.SystemDefault,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));

            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/reconnect",
                    "phase184_msgs/msg/Reconnect",
                    FoxRunResolvedQos.SystemDefault,
                    sequence: 1UL),
                out var enqueueReason), enqueueReason);
            Assert.True(SpinWait.SpinUntil(
                () => first.SendStarted.IsSet,
                TimeSpan.FromSeconds(3)));
            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/reconnect",
                    "phase184_msgs/msg/Reconnect",
                    FoxRunResolvedQos.SystemDefault,
                    sequence: 2UL),
                out enqueueReason), enqueueReason);
            firstSendGate.Set();

            Assert.True(SpinWait.SpinUntil(
                () => second.RequestCount == 1,
                TimeSpan.FromSeconds(3)));
            Assert.Equal(0, second.SentFrameCount);
            responseGate.Set();
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/reconnect",
                          "phase184_msgs/msg/Reconnect",
                          FoxRunResolvedQos.SystemDefault,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => second.SentFrameCount == 1,
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => second.EventSnapshot.Length == 2,
                TimeSpan.FromSeconds(3)));
            Assert.Single(first.Requests);
            Assert.Single(second.Requests);
            Assert.Equal(new[] { "prepare", "send" }, second.EventSnapshot);
        }

        [Fact]
        public void RejectedReconnectDropsPreparedQueuedFrameWithoutSendingIt()
        {
            var firstSendGate = new ManualResetEventSlim(false);
            var first = new PreparationTransport("ok")
            {
                FailNextPublish = true,
                SendGate = firstSendGate
            };
            var rejected = new PreparationTransport(
                "error",
                "typesupport_unavailable",
                "typesupport unavailable");
            var transport = new ReconnectSequenceTransport(first, rejected);
            using var runtime = Runtime(() => transport);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/rejected_reconnect",
                    "phase184_msgs/msg/RejectedReconnect",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/rejected_reconnect",
                          "phase184_msgs/msg/RejectedReconnect",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));

            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/rejected_reconnect",
                    "phase184_msgs/msg/RejectedReconnect",
                    FoxRunResolvedQos.Default,
                    sequence: 1UL),
                out var reason), reason);
            Assert.True(SpinWait.SpinUntil(
                () => first.SendStarted.IsSet,
                TimeSpan.FromSeconds(3)));
            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/rejected_reconnect",
                    "phase184_msgs/msg/RejectedReconnect",
                    FoxRunResolvedQos.Default,
                    sequence: 2UL),
                out reason), reason);
            firstSendGate.Set();

            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/rejected_reconnect",
                          "phase184_msgs/msg/RejectedReconnect",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Rejected,
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.GetStatsSnapshot().FailedFrames >= 2,
                TimeSpan.FromSeconds(3)));
            Assert.Empty(rejected.SentFrames);
            Assert.True(runtime.GetStatsSnapshot().DroppedFrames >= 1);
        }

        [Fact]
        public void ExactCacheKeyIncludesTopicSchemaAndEveryQosAxis()
        {
            var transport = new PreparationTransport("ok");
            using var runtime = Runtime(() => transport);
            runtime.Start(enabled: true, autoConnect: true);
            var contracts = new[]
            {
                ("/phase184/key", "phase184_msgs/msg/Key", FoxRunResolvedQos.Default),
                ("/phase184/key2", "phase184_msgs/msg/Key", FoxRunResolvedQos.Default),
                ("/phase184/key", "phase184_msgs/msg/Key2", FoxRunResolvedQos.Default),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.SensorData, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepLast, 10)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.BestEffort,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepLast, 10)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.TransientLocal, FoxRunQosHistory.KeepLast, 10)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepAll, 0)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepLast, 11))
            };

            for (var index = 0; index < contracts.Length; index++)
            {
                var contract = contracts[index];
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    runtime.PreparePublisher(
                        contract.Item1,
                        contract.Item2,
                        contract.Item3,
                        out _));
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.PreparePublisher(
                              contract.Item1,
                              contract.Item2,
                              contract.Item3,
                              out _)
                          == Ros2BridgePublisherReadiness.Ready,
                    TimeSpan.FromSeconds(3)));
            }

            Assert.Equal(contracts.Length, transport.RequestCount);
        }

        [Fact]
        public void PreparationRegistryRejectsUniqueContractsBeyondQueueCapacity()
        {
            var transport = new PreparationTransport("ok")
            {
                ResponseGate = new ManualResetEventSlim(false)
            };
            using var runtime = Runtime(() => transport, queueCapacity: 2);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/capacity_1",
                    "phase184_msgs/msg/CapacityOne",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/capacity_2",
                    "phase184_msgs/msg/CapacityTwo",
                    FoxRunResolvedQos.Default,
                    out _));

            Assert.Equal(
                Ros2BridgePublisherReadiness.Rejected,
                runtime.PreparePublisher(
                    "/phase184/capacity_3",
                    "phase184_msgs/msg/CapacityThree",
                    FoxRunResolvedQos.Default,
                    out var firstReason));
            Assert.Contains("capacity", firstReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Rejected,
                runtime.PreparePublisher(
                    "/phase184/capacity_3",
                    "phase184_msgs/msg/CapacityThree",
                    FoxRunResolvedQos.Default,
                    out var secondReason));
            Assert.Equal(firstReason, secondReason);
        }

    }
}
