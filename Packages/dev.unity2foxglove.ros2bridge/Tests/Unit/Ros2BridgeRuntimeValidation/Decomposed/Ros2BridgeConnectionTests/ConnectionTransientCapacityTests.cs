// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2BridgeRuntimeValidation
// Purpose: Real-stream checks for heartbeat progress while publish capacity waits.

using System;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeConnectionTransientCapacityTests
    {
        [Fact]
        public void TransientCapacityWaitDoesNotStarveHeartbeat()
        {
            using var capacityWait = new ManualResetEventSlim(false);
            using var releaseCapacity = new ManualResetEventSlim(false);
            using var heartbeatSeen = new ManualResetEventSlim(false);
            var allowRequest = 0;
            Exception exchangeFailure = null;
            using var peer = Ros2BridgeConnectionTests.LoopbackPeer.Start(stream =>
            {
                var hello = Ros2BridgeConnectionTests.Parse(
                    Ros2BridgeConnectionTests.ReadWireFrame(stream));
                Ros2BridgeConnectionTests.WriteFrame(
                    stream,
                    Ros2BridgeConnectionTests.HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));

                var heartbeat = Ros2BridgeConnectionTests.Parse(
                    Ros2BridgeConnectionTests.ReadWireFrame(stream));
                Assert.Equal(U2R2Operation.HealthPing, heartbeat.Operation);
                Ros2BridgeConnectionTests.WriteFrame(
                    stream,
                    Ros2BridgeConnectionTests.Response(
                        "health_pong",
                        heartbeat.RequestId,
                        heartbeat.SessionId,
                        heartbeat.ConnectionGeneration));
                heartbeatSeen.Set();

                while (true)
                {
                    var request = Ros2BridgeConnectionTests.Parse(
                        Ros2BridgeConnectionTests.ReadWireFrame(stream));
                    if (request.Operation == U2R2Operation.HealthPing)
                    {
                        Ros2BridgeConnectionTests.WriteFrame(
                            stream,
                            Ros2BridgeConnectionTests.Response(
                                "health_pong",
                                request.RequestId,
                                request.SessionId,
                                request.ConnectionGeneration));
                        continue;
                    }

                    Assert.Equal(U2R2Operation.PreparePublisher, request.Operation);
                    Ros2BridgeConnectionTests.WriteFrame(
                        stream,
                        Ros2BridgeConnectionTests.Response(
                            "publisher_ready",
                            request.RequestId,
                            request.SessionId,
                            request.ConnectionGeneration));
                    break;
                }
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

            var exchange = new Thread(() =>
            {
                try
                {
                    connection.Exchange(
                        (requestId, active) =>
                        {
                            if (Volatile.Read(ref allowRequest) == 0)
                                return null;
                            return Ros2BridgeV2SessionCodec
                                .CreatePublisherPreparation(
                                    active,
                                    requestId,
                                    "/phase187/transient_capacity",
                                    "phase187_msgs/msg/TransientCapacity",
                                    FoxRunResolvedQos.Default);
                        },
                        timeoutMs: 2000,
                        waitForRequestCapacity: () =>
                        {
                            capacityWait.Set();
                            Assert.True(releaseCapacity.Wait(TimeSpan.FromSeconds(3)));
                        });
                }
                catch (Exception exception)
                {
                    exchangeFailure = exception;
                }
            });
            exchange.Start();

            Assert.True(capacityWait.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(
                heartbeatSeen.Wait(TimeSpan.FromMilliseconds(750)),
                "Transient capacity backpressure must not miss the heartbeat deadline.");

            Volatile.Write(ref allowRequest, 1);
            releaseCapacity.Set();
            Assert.True(exchange.Join(TimeSpan.FromSeconds(3)));
            peer.AssertCompleted();
            Assert.Null(exchangeFailure);
        }
    }
}
