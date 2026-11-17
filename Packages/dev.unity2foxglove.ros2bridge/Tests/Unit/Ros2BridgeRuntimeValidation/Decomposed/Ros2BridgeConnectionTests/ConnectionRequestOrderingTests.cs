// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2BridgeRuntimeValidation
// Purpose: Real-stream checks for request construction ordering and capacity waits.

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;
using static Unity2Foxglove.Ros2Bridge.Tests.Ros2BridgeConnectionTests;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeConnectionRequestOrderingTests
    {
        [Fact]
        public void ConcurrentExchangesReachThePeerInRequestIdOrder()
        {
            var observed = new List<ulong>();
            using var peer = StartPeer(observed);
            using var transport = new Ros2BridgeTcpClient();
            using var connection = StartConnection(transport, peer);
            using var firstEntered = new ManualResetEventSlim(false);
            using var releaseFirst = new ManualResetEventSlim(false);
            Exception firstFailure = null;
            var first = new Thread(() =>
            {
                try
                {
                    connection.Exchange(
                        (requestId, active) =>
                        {
                            firstEntered.Set();
                            Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(3)));
                            return Prepare(active, requestId, "/phase309/first");
                        },
                        2000);
                }
                catch (Exception exception)
                {
                    firstFailure = exception;
                }
            });
            first.Start();
            Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(2)));

            Exception secondFailure = null;
            var second = new Thread(() =>
            {
                try
                {
                    connection.Exchange(
                        (requestId, active) => Prepare(active, requestId, "/phase309/second"),
                        2000);
                }
                catch (Exception exception)
                {
                    secondFailure = exception;
                }
            });
            second.Start();
            Thread.Sleep(200);
            releaseFirst.Set();

            Assert.True(first.Join(TimeSpan.FromSeconds(3)));
            Assert.True(second.Join(TimeSpan.FromSeconds(3)));
            peer.AssertCompleted();
            Assert.Null(firstFailure);
            Assert.Null(secondFailure);
            Assert.Equal(2, observed.Count);
            Assert.True(observed[0] < observed[1], string.Join(",", observed));
        }

        [Fact]
        public void CapacityWaitReleasesRequestConstructionForOtherExchanges()
        {
            var observed = new List<ulong>();
            using var peer = StartPeer(observed);
            using var transport = new Ros2BridgeTcpClient();
            using var connection = StartConnection(transport, peer);
            using var waiting = new ManualResetEventSlim(false);
            using var otherDone = new ManualResetEventSlim(false);
            var attempts = 0;
            Exception delayedFailure = null;
            var delayed = new Thread(() =>
            {
                try
                {
                    connection.Exchange(
                        (requestId, active) => Interlocked.Increment(ref attempts) == 1
                            ? null
                            : Prepare(active, requestId, "/phase309/delayed"),
                        2000,
                        () =>
                        {
                            waiting.Set();
                            Assert.True(otherDone.Wait(TimeSpan.FromSeconds(3)));
                        });
                }
                catch (Exception exception)
                {
                    delayedFailure = exception;
                }
            });
            delayed.Start();
            Assert.True(waiting.Wait(TimeSpan.FromSeconds(2)));

            connection.Exchange(
                (requestId, active) => Prepare(active, requestId, "/phase309/other"),
                2000);
            otherDone.Set();

            Assert.True(delayed.Join(TimeSpan.FromSeconds(3)));
            peer.AssertCompleted();
            Assert.Null(delayedFailure);
            Assert.Equal(2, attempts);
            Assert.Equal(2, observed.Count);
            Assert.True(observed[0] < observed[1], string.Join(",", observed));
        }

        private static LoopbackPeer StartPeer(List<ulong> observed)
            => LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(stream, HelloAck(hello.RequestId, includeSubscribe: true));
                for (var index = 0; index < 2; index++)
                {
                    var request = Parse(ReadWireFrame(stream));
                    observed.Add(request.RequestId);
                    WriteFrame(
                        stream,
                        Response(
                            "publisher_ready",
                            request.RequestId,
                            request.SessionId,
                            request.ConnectionGeneration));
                }
            });

        private static Ros2BridgeConnection StartConnection(
            Ros2BridgeTcpClient transport,
            LoopbackPeer peer)
        {
            transport.Connect("127.0.0.1", peer.Port, 5000);
            var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 4,
                pendingCapacity: 4,
                timeoutMs: 2000);
            connection.Start();
            return connection;
        }

        private static Ros2BridgeV2Request Prepare(
            Ros2BridgeV2SessionSnapshot active,
            ulong requestId,
            string topic)
            => Ros2BridgeV2SessionCodec.CreatePublisherPreparation(
                active,
                requestId,
                topic,
                "phase309_msgs/msg/Ordered",
                FoxRunResolvedQos.Default);
    }
}
