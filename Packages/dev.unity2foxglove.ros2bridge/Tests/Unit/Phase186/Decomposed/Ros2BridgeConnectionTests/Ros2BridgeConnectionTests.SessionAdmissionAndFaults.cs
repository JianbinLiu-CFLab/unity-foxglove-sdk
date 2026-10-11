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

        [Fact]
        public void UnknownResponseAfterHandshakeIsTerminal()
        {
            using var releaseUnknown = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(stream, HelloAck(hello.RequestId));
                Assert.True(
                    releaseUnknown.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    Response(
                        "publisher_ready",
                        requestId: 999,
                        "phase186-session",
                        connectionGeneration: 19));
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

            connection.Start();
            releaseUnknown.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => connection.LifecycleState
                          == Ros2BridgeSessionLifecycleState.Faulted,
                    TimeSpan.FromSeconds(2)));

            var protocol = Assert.IsType<U2R2ProtocolException>(
                connection.LastFault);
            Assert.Equal("response_mismatch", protocol.ErrorCode);
            peer.AssertCompleted();
        }

        [Fact]
        public void DisposeClosesSocketAndWakesBlockedReader()
        {
            using var peerWaiting = new ManualResetEventSlim(false);
            using var peerClosed = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(stream, HelloAck(hello.RequestId));
                peerWaiting.Set();
                var value = stream.ReadByte();
                Assert.Equal(-1, value);
                peerClosed.Set();
            });
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: false,
                writerCapacity: 2,
                pendingCapacity: 2,
                timeoutMs: 1000);
            connection.Start();
            Assert.True(peerWaiting.Wait(TimeSpan.FromSeconds(2)));

            var stopwatch = Stopwatch.StartNew();
            connection.Dispose();
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 1500);
            Assert.True(peerClosed.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Stopped,
                connection.LifecycleState);
            peer.AssertCompleted();
        }

        [Fact]
        public void PrepareForReconnectAbortsReadyConnectionBeforeReset()
        {
            using var peerWaiting = new ManualResetEventSlim(false);
            using var peerClosed = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(stream, HelloAck(hello.RequestId));
                peerWaiting.Set();
                Assert.Equal(-1, stream.ReadByte());
                peerClosed.Set();
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
            connection.Start();
            Assert.True(peerWaiting.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Ready,
                connection.LifecycleState);

            connection.PrepareForReconnect();

            Assert.True(peerClosed.Wait(TimeSpan.FromSeconds(2)));
            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Stopped,
                connection.LifecycleState);
            Assert.Null(connection.LastFault);
            peer.AssertCompleted();
        }

        [Fact]
        public void OversizedHeaderFaultsBeforePeerSendsDeclaredBody()
        {
            using var releaseMalformed =
                new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(stream, HelloAck(hello.RequestId));
                Assert.True(
                    releaseMalformed.Wait(
                        TimeSpan.FromSeconds(3)));
                var fixedHeader = new byte[16];
                fixedHeader[0] = (byte)'U';
                fixedHeader[1] = (byte)'2';
                fixedHeader[2] = (byte)'R';
                fixedHeader[3] = (byte)'2';
                fixedHeader[4] =
                    U2R2ProtocolCodec.EnvelopeVersion;
                WriteUInt32(
                    fixedHeader,
                    8,
                    checked((uint)(
                        U2R2ProtocolLimits.Default
                            .MaxHeaderBytes
                        + 1)));
                stream.Write(
                    fixedHeader,
                    0,
                    fixedHeader.Length);
                stream.Flush();
                Thread.Sleep(500);
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

            connection.Start();
            var stopwatch = Stopwatch.StartNew();
            releaseMalformed.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => connection.LifecycleState
                          == Ros2BridgeSessionLifecycleState.Faulted,
                    TimeSpan.FromSeconds(1)));
            stopwatch.Stop();

            var protocol = Assert.IsType<U2R2ProtocolException>(
                connection.LastFault);
            Assert.Equal("invalid_frame", protocol.ErrorCode);
            Assert.True(stopwatch.ElapsedMilliseconds < 500);
            peer.AssertCompleted();
        }

    }
}
