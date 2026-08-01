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
    public sealed class Ros2BridgeConnectionTests
    {
        [Fact]
        public void ConnectedSocketIsNotSubscribeDecodeReadiness()
        {
            var stats = new Ros2BridgeStatsSnapshot(
                enabled: true,
                connected: true,
                connecting: false,
                queuedFrames: 0,
                sentFrames: 0,
                droppedFrames: 0,
                failedFrames: 0,
                lastError: string.Empty,
                lastConnectedUnixMs: 186,
                lastDisconnectedUnixMs: 0);

            var status = Ros2BridgeTransportStatusMapper.Create(
                generation: 186,
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                hasInboundPipeline: true,
                Ros2BridgePublisherObservationSnapshot.Empty,
                new Ros2BridgeSubscriptionObservationSnapshot(
                    observedContracts: 1,
                    activeContracts: 0,
                    pendingContracts: 1,
                    unavailableContracts: 0,
                    rejectedContracts: 0,
                    faultedContracts: 0,
                    lastReason: "awaiting decoded binding"));

            Assert.Equal(FoxRunTransportObservedState.Ready, status.Publish.State);
            Assert.Equal(FoxRunTransportObservedState.Starting, status.Subscribe.State);
            Assert.Equal(FoxRunTransportObservedState.Degraded, status.State);
            Assert.False(status.Subscribe.IsReady);
            Assert.Contains(
                status.Diagnostics,
                diagnostic => diagnostic.Code == "ROS2BRIDGE005");
        }

        [Fact]
        public void ObservedDisconnectMapsToBoundedStableReconnectingStatus()
        {
            var stats = new Ros2BridgeStatsSnapshot(
                enabled: true,
                connected: false,
                connecting: false,
                queuedFrames: 0,
                sentFrames: 0,
                droppedFrames: 0,
                failedFrames: 1,
                lastError: new string('x', 700),
                lastConnectedUnixMs: 185,
                lastDisconnectedUnixMs: 186);

            var status = Ros2BridgeTransportStatusMapper.Create(
                generation: 187,
                FoxRunTransportCapabilities.Publish,
                Ros2BridgeRuntimeLifecycleState.Ready,
                stats,
                hasInboundPipeline: false,
                Ros2BridgePublisherObservationSnapshot.Empty,
                Ros2BridgeSubscriptionObservationSnapshot.Empty);

            Assert.Equal(
                FoxRunTransportObservedState.Reconnecting,
                status.Publish.State);
            Assert.Equal(
                FoxRunTransportObservedState.Reconnecting,
                status.State);
            var diagnostic = Assert.Single(status.Diagnostics);
            Assert.Equal("ROS2BRIDGE002", diagnostic.Code);
            Assert.Equal(
                FoxRunTransportDiagnostic.MaximumMessageChars,
                diagnostic.Message.Length);
        }

        [Fact]
        public void CleanupAttemptsEveryActionAndPreservesFirstFailure()
        {
            var order = new System.Collections.Generic.List<int>();
            var first = new InvalidOperationException("first");
            var second = new InvalidDataException("second");

            var actual = Ros2BridgeCleanup.RunAll(
                count: 3,
                index =>
                {
                    order.Add(index);
                    if (index == 2)
                        throw first;
                    if (index == 0)
                        throw second;
                },
                reverse: true);

            Assert.Same(first, actual);
            Assert.Equal(new[] { 2, 1, 0 }, order);
        }

        [Fact]
        public void DedicatedReaderAndWriterCompleteFragmentedHandshakeAndRequest()
        {
            using var releasePeer = new ManualResetEventSlim(false);
            using var peer = LoopbackPeer.Start(stream =>
            {
                var hello = Parse(ReadWireFrame(stream));
                WriteFragmented(
                    stream,
                    HelloAck(
                        hello.RequestId,
                        includeSubscribe: true),
                    fragmentBytes: 3);

                var prepare = Parse(ReadWireFrame(stream));
                Assert.Equal(
                    U2R2Operation.PreparePublisher,
                    prepare.Operation);
                WriteFragmented(
                    stream,
                    Response(
                        "publisher_ready",
                        prepare.RequestId,
                        prepare.SessionId,
                        prepare.ConnectionGeneration),
                    fragmentBytes: 5);
                Assert.True(
                    releasePeer.Wait(TimeSpan.FromSeconds(3)));
            });

            using var transport = new Ros2BridgeTcpClient();
            transport.Connect(
                "127.0.0.1",
                peer.Port,
                timeoutMs: 1000);
            using var connection = new Ros2BridgeConnection(
                (IRos2BridgeSessionTransport)transport,
                U2R2ProtocolLimits.Default,
                requiresSubscription: true,
                writerCapacity: 4,
                pendingCapacity: 4,
                timeoutMs: 1000);

            var snapshot = connection.Start();
            var response = connection.Exchange(
                (requestId, active) =>
                    Ros2BridgeV2SessionCodec.CreatePublisherPreparation(
                        active,
                        requestId,
                        "/phase186/connection",
                        "phase186_msgs/msg/Connection",
                        FoxRunResolvedQos.Default),
                timeoutMs: 1000);

            Assert.Equal(
                Ros2BridgeSessionLifecycleState.Ready,
                connection.LifecycleState);
            Assert.Equal("phase186-session", snapshot.SessionId);
            Assert.Equal(19UL, snapshot.ConnectionGeneration);
            Assert.True(
                snapshot.HasCapability(U2R2Capability.Subscribe));
            Assert.Equal(
                U2R2Operation.PublisherReady,
                response.Operation);
            Assert.NotEqual(0, connection.ReaderManagedThreadId);
            Assert.NotEqual(0, connection.WriterManagedThreadId);
            Assert.NotEqual(
                connection.ReaderManagedThreadId,
                connection.WriterManagedThreadId);
            releasePeer.Set();
            peer.AssertCompleted();
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
                timeoutMs: 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
            transport.Connect("127.0.0.1", peer.Port, 1000);
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
                reconnectIntervalMs: 10000,
                sendTimeoutMs: 1000,
                sinkFactory: null,
                retirementOwner:
                    FoxRunTransportRetirementOwner.CreateForTests(3),
                providerId: providerId,
                direction: FoxRunTransportDirection.Publish,
                generation: 7,
                joinTimeoutMs: 1500,
                requiresSubscription: true);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(
                SpinWait.SpinUntil(
                    () => runtime.IsConnected,
                    TimeSpan.FromSeconds(3)),
                "the generated duplex runtime did not complete hello");
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

        private sealed class LoopbackPeer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _done =
                new ManualResetEventSlim(false);
            private Exception _error;

            private LoopbackPeer(Action<Stream> behavior)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start(1);
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _thread = new Thread(() =>
                {
                    try
                    {
                        using var client = _listener.AcceptTcpClient();
                        using var stream = client.GetStream();
                        behavior(stream);
                    }
                    catch (Exception exception)
                    {
                        _error = exception;
                    }
                    finally
                    {
                        _done.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "Phase186E loopback peer",
                };
                _thread.Start();
            }

            internal int Port { get; }

            internal static LoopbackPeer Start(
                Action<Stream> behavior)
                => new LoopbackPeer(behavior);

            internal void AssertCompleted()
            {
                Assert.True(_done.Wait(TimeSpan.FromSeconds(3)));
                _thread.Join(TimeSpan.FromSeconds(1));
                Assert.Null(_error);
            }

            public void Dispose()
            {
                _listener.Stop();
                _done.Wait(TimeSpan.FromSeconds(1));
                _thread.Join(TimeSpan.FromSeconds(1));
                _done.Dispose();
            }
        }
    }
}
