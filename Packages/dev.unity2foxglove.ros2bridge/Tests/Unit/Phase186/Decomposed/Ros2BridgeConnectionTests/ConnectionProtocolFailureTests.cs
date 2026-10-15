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

        [Fact]
        public void ReadyMessageTransfersOwnedLogicalPayloadToInboundQueue()
        {
            using var sendMessage = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));
                Assert.True(
                    sendMessage.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    MessageHeader(),
                    new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2a });
                Assert.Equal(-1, stream.ReadByte());
            });
            var contract = new Ros2BridgeSessionContract(
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge"),
                FoxRunTransportDirection.Subscribe,
                "/phase186/inbound",
                "phase186_msgs/msg/Inbound",
                FoxRunResolvedQos.Default,
                "binding-inbound",
                contractId: 11,
                generation: 7);
            var contracts = new Ros2BridgeSessionContractSnapshot(
                generation: 7,
                new[] { contract });
            var state = new Ros2BridgeSessionState(
                new Ros2BridgeSessionSettings(
                    "127.0.0.1",
                    peer.Port,
                    generation: 7,
                    U2R2ProtocolLimits.Default));
            Assert.True(state.TryActivateLocal(contract, out _));
            var reconnect = state.BeginReconnect(contracts);
            using var queue = new Ros2BridgeInboundQueue(
                new Ros2BridgeInboundQueueLimits(
                    maxPayloadBytes: 32,
                    maxTotalBytes: 64,
                    maxPerContractDepth: 2,
                    maxPerContractBytes: 64));
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 2,
                pendingCapacity: 2,
                timeoutMs: 1000,
                inboundResolver: state,
                inboundReceiver: queue);

            var wireSession = connection.Start();
            Assert.True(state.TryCompleteHandshake(
                reconnect.AttemptGeneration,
                wireSession,
                out _));
            Assert.True(state.TryMarkSubscriptionReady(
                reconnect.AttemptGeneration,
                contract,
                out _));
            queue.BeginSession(
                wireSession.SessionId,
                wireSession.ConnectionGeneration,
                contracts);
            sendMessage.Set();

            Assert.True(
                SpinWait.SpinUntil(
                    () => queue.GetStatsSnapshot().QueuedFrames == 1,
                    TimeSpan.FromSeconds(2)));
            Assert.True(queue.TryBeginApply(out var apply));
            using (apply)
            {
                Assert.Equal(
                    new byte[] { 0, 1, 0, 0, 0x2a },
                    apply.Frame.Payload.ToArray());
                apply.MarkApplied();
            }
            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Ready,
                connection.LifecycleState);

            connection.Dispose();
            peer.AssertCompleted();
        }

        [Fact]
        public void SubscriptionReadyMakesBackToBackMessageVisibleBeforeRegisterReturns()
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
                var register = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    ContractResponse(
                        "subscription_ready",
                        register.RequestId,
                        register.ContractId));
                WriteFrame(
                    stream,
                    MessageHeader(),
                    new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2a });
                Assert.True(
                    releasePeer.Wait(TimeSpan.FromSeconds(3)));
            });
            var contract = new Ros2BridgeSessionContract(
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge"),
                FoxRunTransportDirection.Subscribe,
                "/phase186/inbound",
                "phase186_msgs/msg/Inbound",
                FoxRunResolvedQos.Default,
                "binding-inbound",
                contractId: 11,
                generation: 7);
            var contracts = new Ros2BridgeSessionContractSnapshot(
                generation: 7,
                new[] { contract });
            var state = new Ros2BridgeSessionState(
                new Ros2BridgeSessionSettings(
                    "127.0.0.1",
                    peer.Port,
                    generation: 7,
                    U2R2ProtocolLimits.Default));
            Assert.True(state.TryActivateLocal(contract, out _));
            var reconnect = state.BeginReconnect(contracts);
            using var queue = new Ros2BridgeInboundQueue(
                new Ros2BridgeInboundQueueLimits(
                    maxPayloadBytes: 32,
                    maxTotalBytes: 64,
                    maxPerContractDepth: 2,
                    maxPerContractBytes: 64));
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 2,
                pendingCapacity: 2,
                timeoutMs: 1000,
                inboundResolver: state,
                inboundReceiver: queue);
            var wireSession = connection.Start();
            Assert.True(state.TryCompleteHandshake(
                reconnect.AttemptGeneration,
                wireSession,
                out _));
            queue.BeginSession(
                wireSession.SessionId,
                wireSession.ConnectionGeneration,
                contracts);

            var registration =
                ((IRos2BridgeContractWireController)connection)
                .Register(contract);

            Assert.True(registration.IsAccepted, registration.Reason);
            Assert.True(state.IsSubscriptionReady(contract.ContractId));
            Assert.True(
                SpinWait.SpinUntil(
                    () => queue.GetStatsSnapshot().QueuedFrames == 1,
                    TimeSpan.FromSeconds(2)),
                "the first message after subscription_ready was dropped");
            releasePeer.Set();
            peer.AssertCompleted();
        }

    }
}
