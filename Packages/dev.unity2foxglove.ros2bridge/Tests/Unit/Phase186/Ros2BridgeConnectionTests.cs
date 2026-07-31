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
