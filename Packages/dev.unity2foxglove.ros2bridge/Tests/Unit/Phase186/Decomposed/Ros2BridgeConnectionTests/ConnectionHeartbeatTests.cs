// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first real-stream checks for the bounded duplex connection.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeConnectionTests
    {
        [Fact]
        public void ReadyConnectionSendsSessionBoundHeartbeatBeforeReadLeaseExpires()
        {
            using var heartbeatSeen = new ManualResetEventSlim(false);
            using var releasePeer = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));

                var heartbeat = Parse(ReadWireFrame(stream));
                Assert.Equal(
                    U2R2Operation.HealthPing,
                    heartbeat.Operation);
                Assert.Equal(
                    "phase186-session",
                    heartbeat.SessionId);
                Assert.Equal(
                    19UL,
                    heartbeat.ConnectionGeneration);
                WriteFrame(
                    stream,
                    Response(
                        "health_pong",
                        heartbeat.RequestId,
                        heartbeat.SessionId,
                        heartbeat.ConnectionGeneration));
                heartbeatSeen.Set();
                Assert.True(
                    releasePeer.Wait(TimeSpan.FromSeconds(3)));
            });

            var limits = U2R2ProtocolLimits.Default.With(
                ("readTimeoutMs", 1000UL));
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect(
                "127.0.0.1",
                peer.Port,
                timeoutMs: 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                limits,
                requiresSubscription: true,
                writerCapacity: 4,
                pendingCapacity: 4,
                timeoutMs: 1000);

            connection.Start();

            Assert.True(
                heartbeatSeen.Wait(
                    TimeSpan.FromMilliseconds(750)));
            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Ready,
                connection.LifecycleState);
            releasePeer.Set();
            peer.AssertCompleted();
        }

        [Fact]
        public void HeartbeatWaitDoesNotBlockAQueuedCorrelatedRequest()
        {
            using var heartbeatSeen = new ManualResetEventSlim(false);
            using var releasePeer = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));

                var heartbeat = Parse(ReadWireFrame(stream));
                Assert.Equal(U2R2Operation.HealthPing, heartbeat.Operation);
                heartbeatSeen.Set();

                var prepare = Parse(ReadWireFrame(stream));
                Assert.Equal(U2R2Operation.PreparePublisher, prepare.Operation);
                WriteFrame(
                    stream,
                    Response(
                        "health_pong",
                        heartbeat.RequestId,
                        heartbeat.SessionId,
                        heartbeat.ConnectionGeneration));
                WriteFrame(
                    stream,
                    Response(
                        "publisher_ready",
                        prepare.RequestId,
                        prepare.SessionId,
                        prepare.ConnectionGeneration));
                Assert.True(releasePeer.Wait(TimeSpan.FromSeconds(3)));
            });

            var limits = U2R2ProtocolLimits.Default.With(
                ("readTimeoutMs", 1000UL));
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                limits,
                requiresSubscription: true,
                writerCapacity: 4,
                pendingCapacity: 4,
                timeoutMs: 2000);
            connection.Start();
            Assert.True(heartbeatSeen.Wait(TimeSpan.FromMilliseconds(750)));

            try
            {
                var response = connection.Exchange(
                    (requestId, active) =>
                        Ros2BridgeV2SessionCodec.CreatePublisherPreparation(
                            active,
                            requestId,
                            "/phase187/heartbeat_progress",
                            "phase187_msgs/msg/HeartbeatProgress",
                            FoxRunResolvedQos.Default),
                    timeoutMs: 1500);
                Assert.Equal(
                    U2R2Operation.PublisherReady,
                    response.Operation);
                Assert.Equal(
                    Ros2BridgeSessionLifecycleState.Ready,
                    connection.LifecycleState);
            }
            finally
            {
                releasePeer.Set();
            }
            peer.AssertCompleted();
        }

        [Fact]
        public void RequestConstructionCannotBeOvertakenByIdleHeartbeat()
        {
            using var factoryEntered = new ManualResetEventSlim(false);
            using var releaseFactory = new ManualResetEventSlim(false);
            using var releasePeer = new ManualResetEventSlim(false);
            U2R2Operation? firstOperation = null;
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));

                for (var index = 0; index < 2; index++)
                {
                    var request = Parse(ReadWireFrame(stream));
                    firstOperation ??= request.Operation;
                    if (request.Operation == U2R2Operation.HealthPing)
                    {
                        WriteFrame(
                            stream,
                            Response(
                                "health_pong",
                                request.RequestId,
                                request.SessionId,
                                request.ConnectionGeneration));
                        continue;
                    }

                    Assert.Equal(
                        U2R2Operation.PreparePublisher,
                        request.Operation);
                    WriteFrame(
                        stream,
                        Response(
                            "publisher_ready",
                            request.RequestId,
                            request.SessionId,
                            request.ConnectionGeneration));
                    break;
                }
                Assert.True(
                    releasePeer.Wait(TimeSpan.FromSeconds(3)));
            });

            var limits = U2R2ProtocolLimits.Default.With(
                ("readTimeoutMs", 1000UL));
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                limits,
                requiresSubscription: true,
                writerCapacity: 4,
                pendingCapacity: 4,
                timeoutMs: 2000);
            connection.Start();

            U2R2Message response = null;
            Exception failure = null;
            var exchange = new Thread(() =>
            {
                try
                {
                    response = connection.Exchange(
                        (requestId, active) =>
                        {
                            factoryEntered.Set();
                            Assert.True(
                                releaseFactory.Wait(
                                    TimeSpan.FromSeconds(3)));
                            return Ros2BridgeV2SessionCodec
                                .CreatePublisherPreparation(
                                    active,
                                    requestId,
                                    "/phase187/ordered_request",
                                    "phase187_msgs/msg/OrderedRequest",
                                    FoxRunResolvedQos.Default);
                        },
                        timeoutMs: 2000);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            exchange.Start();
            Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(2)));
            Thread.Sleep(650);
            releaseFactory.Set();
            Assert.True(exchange.Join(TimeSpan.FromSeconds(3)));
            releasePeer.Set();
            peer.AssertCompleted();

            Assert.Null(failure);
            Assert.Equal(U2R2Operation.PublisherReady, response?.Operation);
            Assert.Equal(U2R2Operation.PreparePublisher, firstOperation);
        }

        [Fact]
        public void ProtocolReadDeadlineSurvivesIdleGapBeyondSendTimeout()
        {
            using var releasePeer = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));

                var prepare = Parse(ReadWireFrame(stream));
                Assert.Equal(
                    U2R2Operation.PreparePublisher,
                    prepare.Operation);
                WriteFrame(
                    stream,
                    Response(
                        "publisher_ready",
                        prepare.RequestId,
                        prepare.SessionId,
                        prepare.ConnectionGeneration));
                Assert.True(
                    releasePeer.Wait(TimeSpan.FromSeconds(3)));
            });

            using var transport = new Ros2BridgeTcpClient();
            transport.Connect(
                "127.0.0.1",
                peer.Port,
                timeoutMs: 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 4,
                pendingCapacity: 4,
                timeoutMs: 100);

            connection.Start();
            Thread.Sleep(250);

            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Ready,
                connection.LifecycleState);

            Exception exchangeFailure = null;
            U2R2Operation? responseOperation = null;
            try
            {
                var response = connection.Exchange(
                    (requestId, active) =>
                        Ros2BridgeV2SessionCodec
                            .CreatePublisherPreparation(
                                active,
                                requestId,
                                "/phase186/idle_gap",
                                "phase186_msgs/msg/IdleGap",
                                FoxRunResolvedQos.Default),
                    timeoutMs: 1000);
                responseOperation = response.Operation;
            }
            catch (Exception exception)
            {
                exchangeFailure = exception;
            }
            finally
            {
                releasePeer.Set();
                if (exchangeFailure != null)
                    connection.Dispose();
            }

            peer.AssertCompleted();
            Assert.Null(exchangeFailure);
            Assert.Equal(
                U2R2Operation.PublisherReady,
                responseOperation);
        }

        [Fact]
        public void NormalWorkIsRejectedUntilCorrelatedHelloAck()
        {
            using var helloSeen = new ManualResetEventSlim(false);
            using var releasePeer = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                helloSeen.Set();
                Assert.True(
                    releasePeer.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId == ulong.MaxValue
                            ? 1
                            : hello.RequestId + 1));
            });
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: false,
                writerCapacity: 2,
                pendingCapacity: 2,
                timeoutMs: 1000);
            Exception startError = null;
            using var startDone = new ManualResetEventSlim(false);
            var startThread = new Thread(() =>
            {
                try
                {
                    connection.Start();
                }
                catch (Exception exception)
                {
                    startError = exception;
                }
                finally
                {
                    startDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186E connection handshake",
            };
            startThread.Start();

            Assert.True(helloSeen.Wait(TimeSpan.FromSeconds(2)));
            Assert.Throws<InvalidOperationException>(
                () => connection.Exchange(
                    (requestId, active) =>
                        Ros2BridgeV2SessionCodec
                            .CreatePublisherPreparation(
                                active,
                                requestId,
                                "/phase186/early",
                                "phase186_msgs/msg/Early",
                                FoxRunResolvedQos.Default),
                    timeoutMs: 100));
            releasePeer.Set();
            Assert.True(startDone.Wait(TimeSpan.FromSeconds(2)));
            startThread.Join(TimeSpan.FromSeconds(1));

            var protocol = Assert.IsType<U2R2ProtocolException>(
                startError);
            Assert.Equal("response_mismatch", protocol.ErrorCode);
            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Faulted,
                connection.LifecycleState);
            peer.AssertCompleted();
        }

    }
}
