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
        public void MessageBeforeSubscriptionReadyIsDroppedWithoutDegradingSession()
        {
            using var sendMessage = new ManualResetEventSlim(false);
            using var releasePeer = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFrame(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true));
                Assert.True(sendMessage.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    MessageHeader(),
                    new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2a });
                Assert.True(releasePeer.Wait(TimeSpan.FromSeconds(3)));
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

            sendMessage.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () => queue.GetStatsSnapshot().ResolutionRejections == 1,
                    TimeSpan.FromSeconds(2)),
                "the pre-ready message rejection was not counted");
            var inbound = queue.GetStatsSnapshot();
            Assert.Equal(1, inbound.Received);
            Assert.False(inbound.HasSessionDeliveryFailure);
            Assert.Contains("subscription_ready", inbound.LastDiagnostic);
            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Ready,
                connection.LifecycleState);

            releasePeer.Set();
            peer.AssertCompleted();
        }

        [Fact]
        public void SubscriptionControlResponseUsesProtocolDeadlineNotSocketSendTimeout()
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
                Assert.Equal(
                    U2R2Operation.RegisterSubscription,
                    register.Operation);
                Thread.Sleep(250);
                try
                {
                    WriteFrame(
                        stream,
                        ContractResponse(
                            "subscription_ready",
                            register.RequestId,
                            register.ContractId));
                }
                catch (IOException)
                {
                    // The RED behavior closes the socket at the configured
                    // send timeout before the protocol response deadline.
                }
                Assert.True(
                    releasePeer.Wait(TimeSpan.FromSeconds(3)));
            });
            var contract = new Ros2BridgeSessionContract(
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge"),
                FoxRunTransportDirection.Subscribe,
                "/phase186/delayed_control",
                "phase186_msgs/msg/DelayedControl",
                FoxRunResolvedQos.Default,
                "binding-delayed-control",
                contractId: 12,
                generation: 7);
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 2,
                pendingCapacity: 2,
                timeoutMs: 100);
            connection.Start();
            var controller =
                (IRos2BridgeContractWireController)connection;

            var result = controller.Register(contract);

            releasePeer.Set();
            peer.AssertCompleted();
            Assert.True(result.IsAccepted, result.Reason);
        }

        [Fact]
        public void SubscriptionRegisterAndUnregisterUseCorrelatedWriterLane()
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
                Assert.Equal(
                    U2R2Operation.RegisterSubscription,
                    register.Operation);
                Assert.Equal(11UL, register.ContractId);
                Assert.Equal("/phase186/inbound", register.Topic);
                Assert.Equal(
                    "phase186_msgs/msg/Inbound",
                    register.SchemaName);
                WriteFrame(
                    stream,
                    ContractResponse(
                        "subscription_ready",
                        register.RequestId,
                        register.ContractId));

                var unregister = Parse(ReadWireFrame(stream));
                Assert.Equal(
                    U2R2Operation.UnregisterSubscription,
                    unregister.Operation);
                Assert.Equal(11UL, unregister.ContractId);
                WriteFrame(
                    stream,
                    ContractResponse(
                        "subscription_removed",
                        unregister.RequestId,
                        unregister.ContractId));
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
            using var transport = new Ros2BridgeTcpClient();
            transport.Connect("127.0.0.1", peer.Port, 5000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 2,
                pendingCapacity: 2,
                timeoutMs: 1000);
            connection.Start();
            var controller =
                (IRos2BridgeContractWireController)connection;

            Assert.True(controller.Register(contract).IsAccepted);
            Assert.True(controller.Unregister(contract).IsAccepted);

            releasePeer.Set();
            peer.AssertCompleted();
        }

    }
}
