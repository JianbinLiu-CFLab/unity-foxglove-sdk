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

        [Fact]
        public void GeneratedSubscribersShareOnePhysicalLeaseAndApplyOnPumpThread()
        {
            using var sendMessage = new ManualResetEventSlim(false);
            using var sendFault = new ManualResetEventSlim(false);
            using var registerSeen = new ManualResetEventSlim(false);
            using var allowRegistration = new ManualResetEventSlim(false);
            using var unregisterSeen = new ManualResetEventSlim(false);
            var connectionAttempt = 0;
            using var peer = LoopbackPeer.StartAcceptingReconnects(stream =>
            {
                if (Interlocked.Increment(ref connectionAttempt) == 1)
                {
                    ReadWireFrame(stream);
                    throw new IOException("intentional initial handshake disconnect");
                }

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
                registerSeen.Set();
                Assert.True(
                    allowRegistration.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    ContractResponse(
                        "subscription_ready",
                        register.RequestId,
                        register.ContractId));

                Assert.True(
                    sendMessage.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    new JObject
                    {
                        ["connectionGeneration"] = 19,
                        ["contractId"] = register.ContractId,
                        ["encoding"] = "cdr",
                        ["messageId"] = 1,
                        ["op"] = "message",
                        ["protocolVersion"] = 2,
                        ["receiveTimeNs"] = 2,
                        ["representation"] = "xcdr1-le",
                        ["schemaName"] = register.SchemaName,
                        ["sequence"] = 1,
                        ["sessionId"] = "phase186-session",
                        ["topic"] = register.Topic,
                    },
                    new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2a });
                Assert.True(
                    sendFault.Wait(TimeSpan.FromSeconds(3)));
                WriteFrame(
                    stream,
                    new JObject
                    {
                        ["connectionGeneration"] = 19,
                        ["contractId"] = register.ContractId,
                        ["encoding"] = "cdr",
                        ["messageId"] = 2,
                        ["op"] = "message",
                        ["protocolVersion"] = 2,
                        ["receiveTimeNs"] = 3,
                        ["representation"] = "xcdr1-le",
                        ["schemaName"] = register.SchemaName,
                        ["sequence"] = 2,
                        ["sessionId"] = "phase186-session",
                        ["topic"] = register.Topic,
                    },
                    new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2b });

                var unregister = Parse(ReadWireFrame(stream));
                Assert.Equal(
                    U2R2Operation.UnregisterSubscription,
                    unregister.Operation);
                Assert.Equal(register.ContractId, unregister.ContractId);
                unregisterSeen.Set();
                WriteFrame(
                    stream,
                    ContractResponse(
                        "subscription_removed",
                        unregister.RequestId,
                        unregister.ContractId));
            });

            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.generated-duplex");
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                peer.Port,
                queueCapacity: 8,
                reconnectIntervalMs: 100,
                sendTimeoutMs: 5000,
                sinkFactory: null,
                retirementOwner:
                    FoxRunTransportRetirementOwner.CreateForTests(3),
                providerId: providerId,
                direction: FoxRunTransportDirection.Publish,
                generation: 7,
                joinTimeoutMs: 5000,
                requiresSubscription: true);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                SpinWait.SpinUntil(
                    () => runtime.IsConnected,
                    TimeSpan.FromSeconds(30)),
                "the generated duplex runtime did not complete hello; connections="
                + peer.Connections
                + "; lastError="
                + runtime.GetStatsSnapshot().LastError);
            Assert.True(peer.Connections >= 2);
            Assert.True(runtime.HasInboundPipeline);

            using var subscriptions =
                new Ros2BridgeGeneratedSubscriptionRuntime(
                    runtime,
                    providerId,
                    generation: 7);
            var callbackThread = Environment.CurrentManagedThreadId;
            var callbackCount = 0;
            var throwOnPayload = false;
            byte[] firstPayload = null;
            var route = new FoxRunTransportSubscribeRoute(
                "phase186/generated/shared-binding",
                "/phase186/generated/shared",
                "phase186_msgs/msg/GeneratedShared",
                maxPayloadBytes: 32,
                FoxRunDeliveryPolicy.ProviderDefault,
                (payload, receiveTimeNs, sequence) =>
                {
                    if (throwOnPayload)
                        throw new InvalidDataException("generated apply failed");
                    Assert.Equal(
                        callbackThread,
                        Environment.CurrentManagedThreadId);
                    Assert.Equal(2UL, receiveTimeNs);
                    Assert.Equal(1UL, sequence);
                    firstPayload ??= payload.ToArray();
                    callbackCount++;
                },
                messageEncoding: "cdr");

            var first = default(FoxRunTransportSubscribeResult);
            Exception subscribeFailure = null;
            var subscribeThread = new Thread(() =>
            {
                try
                {
                    first = subscriptions.Subscribe(in route);
                }
                catch (Exception exception)
                {
                    subscribeFailure = exception;
                }
            })
            {
                IsBackground = true,
                Name = "phase186-generated-subscribe",
            };
            subscribeThread.Start();
            Assert.True(
                registerSeen.Wait(TimeSpan.FromSeconds(3)));
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var pending));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Pending,
                pending.State);
            allowRegistration.Set();
            Assert.True(subscribeThread.Join(TimeSpan.FromSeconds(3)));
            Assert.Null(subscribeFailure);
            Assert.Equal(
                FoxRunTransportRouteResultState.Accepted,
                first.State);
            var second = subscriptions.Subscribe(in route);
            Assert.Equal(
                FoxRunTransportRouteResultState.Accepted,
                second.State);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var active));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Active,
                active.State);
            Assert.Equal(2, active.Attempts);
            Assert.Equal(2, active.ActiveLeases);

            sendMessage.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        subscriptions.Pump(maxFrames: 8);
                        return callbackCount == 2;
                    },
                    TimeSpan.FromSeconds(3)),
                "the generated subscriptions were not applied on the pump thread");
            Assert.Equal(
                new byte[] { 0x00, 0x01, 0x00, 0x00, 0x2a },
                firstPayload);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var applied));
            Assert.Equal(1, applied.ReceivedFrames);
            Assert.Equal(1, applied.AppliedFrames);
            Assert.Equal(0, applied.FailedFrames);

            throwOnPayload = true;
            sendFault.Set();
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        subscriptions.Pump(maxFrames: 8);
                        return subscriptions.TryGetContractSnapshot(
                                   in route,
                                   out var snapshot)
                               && snapshot.FailedFrames == 1;
                    },
                    TimeSpan.FromSeconds(3)),
                "the generated apply failure was not observed");
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var faulted));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Faulted,
                faulted.State);
            Assert.Equal(2, faulted.ReceivedFrames);
            Assert.Equal(1, faulted.AppliedFrames);
            Assert.Equal(1, faulted.FailedFrames);

            first.Lease.Dispose();
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var shared));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Faulted,
                shared.State);
            Assert.Equal(1, shared.ActiveLeases);
            Assert.False(
                unregisterSeen.Wait(TimeSpan.FromMilliseconds(100)),
                "releasing one shared subscriber removed the physical lease");
            second.Lease.Dispose();
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in route,
                    out var stopped));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Stopped,
                stopped.State);
            Assert.Equal(0, stopped.ActiveLeases);
            var observed = subscriptions.GetObservationSnapshot();
            Assert.Equal(0, observed.ObservedContracts);
            Assert.Equal(0, observed.PendingContracts);
            Assert.True(
                unregisterSeen.Wait(TimeSpan.FromSeconds(3)));
            peer.AssertCompleted();
        }

        [Fact]
        public void GeneratedSubscriptionObservesRejectedAndUnavailableContracts()
        {
            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.generated-observation");
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                port: 1,
                queueCapacity: 2,
                reconnectIntervalMs: 10000,
                sendTimeoutMs: 1000,
                sinkFactory: null,
                retirementOwner:
                    FoxRunTransportRetirementOwner.CreateForTests(2),
                providerId: providerId,
                direction: FoxRunTransportDirection.Publish,
                generation: 11,
                joinTimeoutMs: 1000,
                requiresSubscription: true);
            using var subscriptions =
                new Ros2BridgeGeneratedSubscriptionRuntime(
                    runtime,
                    providerId,
                    generation: 11);
            var unavailableRoute = new FoxRunTransportSubscribeRoute(
                "phase186/generated/unavailable",
                "/phase186/generated/unavailable",
                "phase186_msgs/msg/Unavailable",
                maxPayloadBytes: 32,
                FoxRunDeliveryPolicy.ProviderDefault,
                (_, _, _) => { },
                messageEncoding: "cdr");
            var unavailable = subscriptions.Subscribe(in unavailableRoute);
            Assert.Equal(
                FoxRunTransportRouteResultState.Unavailable,
                unavailable.State);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in unavailableRoute,
                    out var unavailableSnapshot));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Unavailable,
                unavailableSnapshot.State);
            Assert.Equal(1, unavailableSnapshot.Attempts);
            Assert.NotEmpty(unavailableSnapshot.LastReason);

            var rejectedRoute = new FoxRunTransportSubscribeRoute(
                "phase186/generated/rejected",
                "/phase186/generated/rejected",
                "phase186_msgs/msg/Rejected",
                maxPayloadBytes: 32,
                FoxRunDeliveryPolicy.ProviderDefault,
                (_, _, _) => { },
                messageEncoding: "json");
            var rejected = subscriptions.Subscribe(in rejectedRoute);
            Assert.Equal(
                FoxRunTransportRouteResultState.Rejected,
                rejected.State);
            Assert.True(
                subscriptions.TryGetContractSnapshot(
                    in rejectedRoute,
                    out var rejectedSnapshot));
            Assert.Equal(
                Ros2BridgeGeneratedSubscriptionState.Rejected,
                rejectedSnapshot.State);
            Assert.Equal(1, rejectedSnapshot.RejectedAttempts);
            Assert.True(subscriptions.ObservedContractCount <= 64);
        }

        private static U2R2Message Parse(byte[] wireBytes)
            => U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(wireBytes));

        private static JObject HelloAck(
            ulong requestId,
            bool includeSubscribe = false)
            => new JObject
            {
                ["capabilities"] =
                    includeSubscribe
                        ? new JArray("publish", "subscribe")
                        : new JArray("publish"),
                ["connectionGeneration"] = 19,
                ["op"] = "hello_ack",
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["sessionId"] = "phase186-session",
                ["status"] = "ok",
            };

        private static JObject Response(
            string operation,
            ulong requestId,
            string sessionId,
            ulong connectionGeneration)
            => new JObject
            {
                ["connectionGeneration"] = connectionGeneration,
                ["op"] = operation,
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["sessionId"] = sessionId,
                ["status"] = "ok",
            };

        private static JObject MessageHeader()
            => new JObject
            {
                ["connectionGeneration"] = 19,
                ["contractId"] = 11,
                ["encoding"] = "cdr",
                ["messageId"] = 1,
                ["op"] = "message",
                ["protocolVersion"] = 2,
                ["receiveTimeNs"] = 2,
                ["representation"] = "xcdr1-le",
                ["schemaName"] = "phase186_msgs/msg/Inbound",
                ["sequence"] = 1,
                ["sessionId"] = "phase186-session",
                ["topic"] = "/phase186/inbound",
            };

        private static JObject ContractResponse(
            string operation,
            ulong requestId,
            ulong contractId)
            => new JObject
            {
                ["connectionGeneration"] = 19,
                ["contractId"] = contractId,
                ["op"] = operation,
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["sessionId"] = "phase186-session",
                ["status"] = "ok",
            };

        private static void WriteFrame(
            Stream stream,
            JObject header)
            => WriteFrame(
                stream,
                header,
                Array.Empty<byte>());

        private static void WriteFrame(
            Stream stream,
            JObject header,
            byte[] payload)
        {
            var wire = U2R2ProtocolCodec.EncodeFrame(
                header,
                payload);
            stream.Write(wire, 0, wire.Length);
            stream.Flush();
        }

        private static void WriteFragmented(
            Stream stream,
            JObject header,
            int fragmentBytes)
        {
            var wire = U2R2ProtocolCodec.EncodeFrame(
                header,
                Array.Empty<byte>());
            for (var offset = 0; offset < wire.Length;)
            {
                var count = Math.Min(
                    fragmentBytes,
                    wire.Length - offset);
                stream.Write(wire, offset, count);
                stream.Flush();
                offset += count;
            }
        }

        private static byte[] ReadWireFrame(Stream stream)
        {
            var fixedHeader = ReadExactly(stream, 16);
            var headerBytes = ReadUInt32(fixedHeader, 8);
            var payloadBytes = ReadUInt32(fixedHeader, 12);
            var remainder = ReadExactly(
                stream,
                checked((int)(headerBytes + payloadBytes)));
            var frame = new byte[
                checked(fixedHeader.Length + remainder.Length)];
            Buffer.BlockCopy(
                fixedHeader,
                0,
                frame,
                0,
                fixedHeader.Length);
            Buffer.BlockCopy(
                remainder,
                0,
                frame,
                fixedHeader.Length,
                remainder.Length);
            return frame;
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(bytes, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(
                bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));

        private static void WriteUInt32(
            byte[] bytes,
            int offset,
            uint value)
        {
            bytes[offset] = (byte)(value & 0xff);
            bytes[offset + 1] =
                (byte)((value >> 8) & 0xff);
            bytes[offset + 2] =
                (byte)((value >> 16) & 0xff);
            bytes[offset + 3] =
                (byte)((value >> 24) & 0xff);
        }

    }
}
