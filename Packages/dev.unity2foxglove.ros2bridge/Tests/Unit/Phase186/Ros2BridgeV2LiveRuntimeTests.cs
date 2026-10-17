// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first live-worker coverage for the publish-only U2R2 v2 session.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    /// <remarks>Phase192 source sections: V2ReconnectRuntimeTests.cs</remarks>
    public sealed partial class Ros2BridgeV2LiveRuntimeTests
    {
        private const string Topic = "/phase186/v2/live_runtime";
        private const string Schema = "phase186_msgs/msg/LiveRuntime";
        private static readonly FoxRunResolvedQos Qos =
            FoxRunResolvedQos.Default;

        [Fact]
        public void HelloGateKeepsConnectionAndPublisherPending()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.HoldHello);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.True(
                transport.HelloEntered.Wait(TimeSpan.FromSeconds(2)),
                "the worker did not send its v2 hello");
            try
            {
                Assert.False(runtime.IsConnected);
                Assert.True(
                    runtime.GetStatsSnapshot().TransientBytes > 0,
                    "the held hello exchange is outside the runtime byte authority");
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    runtime.PreparePublisher(
                        Topic,
                        Schema,
                        Qos,
                        out _));
                Thread.Sleep(75);

                var requests = transport.V2Requests;
                Assert.Single(requests);
                Assert.Equal(U2R2Operation.Hello, requests[0].Message.Operation);
                Assert.Equal(0, transport.LegacyPreparationCount);
                Assert.Equal(0, transport.RawWriteCount);
            }
            finally
            {
                transport.AllowHelloAck.Set();
            }
        }

        [Fact]
        public void PublisherPreparationRemainsInsideTransientAuthority()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.HoldPreparation);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);
            WaitFor(
                () => runtime.IsConnected,
                "the v2 session did not complete hello");

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    Topic,
                    Schema,
                    Qos,
                    out _));
            Assert.True(
                transport.PreparationEntered.Wait(
                    TimeSpan.FromSeconds(2)));
            try
            {
                Assert.True(
                    runtime.GetStatsSnapshot().TransientBytes > 0,
                    "the held preparation exchange is outside the runtime byte authority");
            }
            finally
            {
                transport.AllowPreparationAck.Set();
            }
            WaitForPublisherReady(runtime);
            Assert.True(
                SpinWait.SpinUntil(
                    () => runtime.GetStatsSnapshot().TransientBytes == 0,
                    TimeSpan.FromSeconds(5)),
                $"transient bytes remained: {runtime.GetStatsSnapshot().TransientBytes}; "
                + $"connects={transport.ConnectCount}; "
                + $"disconnects={transport.DisconnectCount}; "
                + $"requests={transport.V2Requests.Length}; "
                + $"lastError={runtime.GetStatsSnapshot().LastError}");
        }

        [Fact]
        public void OneLiveSocketCorrelatesHelloPreparationAndPublish()
        {
            using var transport = new ScriptedTransport(ScriptMode.Happy);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            WaitFor(
                () => runtime.IsConnected,
                "the runtime did not complete v2 hello");
            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(Frame(sequence: 8), out var reason),
                reason);
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the runtime did not receive a correlated publish_result");

            var requests = transport.V2Requests;
            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                requests.Select(item => item.Message.Operation).ToArray());
            Assert.All(
                requests,
                request => Assert.Equal(1, request.Connection));
            Assert.Equal(
                new ulong[] { 1, 2, 3 },
                requests.Select(item => item.Message.RequestId).ToArray());
            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(0, transport.LegacyPreparationCount);
            Assert.Equal(0, transport.RawWriteCount);

            var preparation = requests[1].Message;
            var publish = requests[2].Message;
            Assert.Equal("phase186-live-session-1", preparation.SessionId);
            Assert.Equal(7UL, preparation.ConnectionGeneration);
            Assert.Equal(preparation.SessionId, publish.SessionId);
            Assert.Equal(
                preparation.ConnectionGeneration,
                publish.ConnectionGeneration);
            Assert.Equal(1UL, publish.MessageId);
            Assert.Equal(8UL, publish.LogTimeNs);
        }

        [Fact]
        public void PublicEnqueueCannotBypassV2PublisherPreparation()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.Happy);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);
            WaitFor(
                () => runtime.IsConnected,
                "the v2 session did not become ready");

            var withoutQos = Ros2BridgeFrame.CreateOwned(
                Topic,
                Schema,
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: 1,
                sequence: 1,
                payload: new byte[] { 0x00, 0x01, 0x00, 0x00 });
            Assert.False(
                runtime.TryEnqueue(withoutQos, out var missingQosReason));
            Assert.Contains(
                "QoS",
                missingQosReason,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, runtime.GetStatsSnapshot().AcceptedFrames);

            var frame = Frame(sequence: 2);
            Assert.True(
                runtime.TryEnqueue(frame, out var enqueueReason),
                enqueueReason);
            WaitFor(
                () => transport.V2Requests.Any(
                    request => request.Message.Operation
                               == U2R2Operation.PreparePublisher),
                "public enqueue did not schedule publisher preparation");
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the prepared public frame was not published");

            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                transport.V2Requests
                    .Select(request => request.Message.Operation)
                    .ToArray());
        }

        [Fact]
        public void SynchronousSendQueuesPreparationBeforeV2Publish()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.Happy);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            runtime.Send(Frame(sequence: 3), timeoutMs: 1000);

            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the synchronous public adapter did not publish");
            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                transport.V2Requests
                    .Select(request => request.Message.Operation)
                    .ToArray());
        }

        [Theory]
        [InlineData(ScriptMode.BusyAtHello)]
        [InlineData(ScriptMode.MalformedHelloResponse)]
        [InlineData(ScriptMode.WrongHelloCorrelation)]
        [InlineData(ScriptMode.WrongPreparationGeneration)]
        public void TerminalProtocolFaultNeverDowngradesCurrentAttempt(
            ScriptMode mode)
        {
            using var transport = new ScriptedTransport(mode);
            using var runtime = Runtime(
                transport,
                reconnectIntervalMs: 10000);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.True(
                transport.HelloEntered.Wait(TimeSpan.FromSeconds(2)),
                "the worker did not send its v2 hello");
            if (mode == ScriptMode.WrongPreparationGeneration)
            {
                WaitFor(
                    () => runtime.IsConnected,
                    "the runtime did not complete the initial hello");
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    runtime.PreparePublisher(
                        Topic,
                        Schema,
                        Qos,
                        out _));
            }

            WaitFor(
                () =>
                {
                    var stats = runtime.GetStatsSnapshot();
                    return transport.DisconnectCount > 0
                           && !stats.Connected
                           && !string.IsNullOrWhiteSpace(stats.LastError);
                },
                "the terminal protocol fault was not surfaced");
            Thread.Sleep(75);

            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(0, transport.LegacyPreparationCount);
            Assert.Equal(0, transport.RawWriteCount);
            Assert.False(runtime.IsConnected);
            Assert.NotEmpty(runtime.GetStatsSnapshot().LastError);
        }

        [Theory]
        [InlineData(ScriptMode.UnsupportedProtocol)]
        [InlineData(ScriptMode.CleanEofBeforeHelloResponse)]
        public void ExplicitPublishOnlyIncompatibilityUsesFreshLegacySocket(
            ScriptMode mode)
        {
            using var transport = new ScriptedTransport(mode);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            WaitFor(
                () => runtime.IsConnected && transport.ConnectCount == 2,
                "the publish-only runtime did not establish its fresh v1 socket");
            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(Frame(sequence: 9), out var reason),
                reason);
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the v1 fallback socket did not publish");

            var hello = Assert.Single(transport.V2Requests);
            Assert.Equal(U2R2Operation.Hello, hello.Message.Operation);
            Assert.Equal(1, hello.Connection);
            Assert.Equal(new[] { 2 }, transport.LegacyPreparationConnections);
            var rawWrite = Assert.Single(transport.RawWrites);
            Assert.Equal(2, rawWrite.Connection);
            Assert.Equal(
                "publish",
                U2R2ProtocolCodec.DecodeFrame(rawWrite.WireBytes)
                    .Header
                    .Value<string>("op"));
            Assert.Equal(2, transport.ConnectCount);
            Assert.True(transport.DisconnectCount >= 1);
        }

        [Fact]
        public void SubscriptionRequiredSessionNeverFallsBackToV1()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.UnsupportedProtocol);
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                19484,
                queueCapacity: 8,
                reconnectIntervalMs: 10000,
                sendTimeoutMs: 500,
                sinkFactory: () => transport,
                owner,
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge.v2-live-subscribe-red"),
                FoxRunTransportDirection.Subscribe,
                generation: 1,
                joinTimeoutMs: 1000);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.True(
                transport.HelloEntered.Wait(TimeSpan.FromSeconds(2)),
                "the subscription-required session did not send hello");
            Thread.Sleep(100);

            var hello = Assert.Single(transport.V2Requests);
            Assert.Contains(
                U2R2Capability.Subscribe,
                hello.Message.Capabilities);
            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(0, transport.LegacyPreparationCount);
            Assert.False(runtime.IsConnected);
        }

    }
}
