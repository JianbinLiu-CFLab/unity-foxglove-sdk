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
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class Ros2BridgeTransientCapacitySerialCollection
    {
        public const string Name = "ROS 2 Bridge transient capacity serial";
    }

    [Collection(Ros2BridgeTransientCapacitySerialCollection.Name)]
    public sealed class Ros2BridgeConnectionTransientCapacityTests
    {
        [Fact]
        public void TransientCapacityWaitDoesNotStarveHeartbeat()
        {
            using var initialHeartbeatSeen = new ManualResetEventSlim(false);
            using var capacityFactoryStarted = new ManualResetEventSlim(false);
            using var capacityWait = new ManualResetEventSlim(false);
            using var heartbeatSeen = new ManualResetEventSlim(false);
            var factoryCalls = 0;
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
                        if (!initialHeartbeatSeen.IsSet)
                            initialHeartbeatSeen.Set();
                        else if (capacityFactoryStarted.IsSet)
                            heartbeatSeen.Set();
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
                ("readTimeoutMs", 100UL));
            var outbound = new U2R2BoundedOutboundScheduler(limits);
            Assert.True(
                outbound.TryReserveTransient(
                    limits.MaxTransientBytes,
                    out var capacityBlocker));
            U2R2ByteLease acceptedTransient = null;
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
            Assert.True(initialHeartbeatSeen.Wait(TimeSpan.FromSeconds(2)));

            var exchange = new Thread(() =>
            {
                try
                {
                    connection.Exchange(
                        (requestId, active) =>
                        {
                            if (Interlocked.Increment(ref factoryCalls) == 1)
                            {
                                Assert.False(
                                    outbound.TryReserveTransient(
                                        1,
                                        out _),
                                    "the transient budget should be occupied");
                                capacityFactoryStarted.Set();
                                Thread.Sleep(70);
                                return null;
                            }
                            Assert.True(
                                outbound.TryReserveTransient(
                                    1,
                                    out acceptedTransient),
                                "the transient budget should be available after retry");
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
                            Thread.Sleep(10);
                        });
                }
                catch (Exception exception)
                {
                    exchangeFailure = exception;
                }
            });
            exchange.Start();

            Assert.True(capacityFactoryStarted.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(capacityWait.Wait(TimeSpan.FromSeconds(2)));
            capacityBlocker.Dispose();
            Assert.True(
                heartbeatSeen.Wait(TimeSpan.FromMilliseconds(90)),
                "Transient capacity backpressure must not miss the heartbeat deadline.");

            Assert.True(exchange.Join(TimeSpan.FromSeconds(3)));
            peer.AssertCompleted();
            Assert.Null(exchangeFailure);
            acceptedTransient?.Dispose();
        }
    }
}
