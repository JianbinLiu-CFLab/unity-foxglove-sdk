// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: Certify publish compatibility on the owned duplex writer lane.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeDuplexPublishRuntimeTests
    {
        private const string Topic =
            "/phase186/duplex/publish";
        private const string Schema =
            "phase186_msgs/msg/DuplexPublish";

        [Fact]
        public void PublishUsesOneDuplexWriterWithoutInboundPipeline()
        {
            var transport = new DuplexTransport(
                DuplexMode.Happy);
            var owner =
                FoxRunTransportRetirementOwner.CreateForTests(3);
            using var runtime = CreateRuntime(
                transport,
                owner,
                FoxRunTransportDirection.Publish);

            runtime.Start(enabled: true, autoConnect: true);
            WaitForPublisherReady(runtime);
            Assert.False(runtime.HasInboundPipeline);
            Assert.True(
                runtime.TryEnqueuePrepared(
                    Frame(sequence: 1),
                    out var reason),
                reason);
            WaitUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the duplex publish did not complete");

            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                transport.Requests
                    .Select(item => item.Message.Operation)
                    .ToArray());
            Assert.Equal(0, transport.LegacyV2ExchangeCount);
            Assert.Equal(0, transport.LegacyRawWriteCount);
        }

        [Fact]
        public void PublishOnlyIncompatibilityUsesFreshLegacySocket()
        {
            var transport = new DuplexTransport(
                DuplexMode.V2Incompatible);
            using var runtime = CreateRuntime(
                transport,
                FoxRunTransportRetirementOwner
                    .CreateForTests(3),
                FoxRunTransportDirection.Publish);

            runtime.Start(enabled: true, autoConnect: true);
            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(
                    Frame(sequence: 2),
                    out var reason),
                reason);
            WaitUntil(
                () => transport.LegacyRawWriteCount == 1,
                "the legacy publish did not complete");

            Assert.Equal(2, transport.ConnectCount);
            var hello = Assert.Single(transport.Requests);
            Assert.Equal(U2R2Operation.Hello, hello.Message.Operation);
            Assert.Equal(1, hello.Connection);
            Assert.Equal(
                new[] { 2 },
                transport.LegacyPreparationConnections);
            Assert.Equal(
                new[] { 2 },
                transport.LegacyRawWriteConnections);
            Assert.Equal(0, transport.LegacyV2ExchangeCount);
        }

        [Fact]
        public void SubscriptionNeverFallsBackToV1()
        {
            var transport = new DuplexTransport(
                DuplexMode.V2Incompatible);
            using var runtime = CreateRuntime(
                transport,
                FoxRunTransportRetirementOwner
                    .CreateForTests(3),
                FoxRunTransportDirection.Subscribe,
                reconnectIntervalMs: 10);

            runtime.Start(enabled: true, autoConnect: true);
            WaitUntil(
                () => transport.ConnectCount >= 2,
                "the subscription runtime did not retry v2");
            runtime.Stop();

            Assert.All(
                transport.Requests,
                request => Assert.Equal(
                    U2R2Operation.Hello,
                    request.Message.Operation));
            Assert.Empty(
                transport.LegacyPreparationConnections);
            Assert.Empty(
                transport.LegacyRawWriteConnections);
            Assert.Equal(0, transport.LegacyV2ExchangeCount);
        }

        [Fact]
        public void ReconnectReusesReservedWorkersAndReplaysPreparation()
        {
            var providerId = new FoxRunTransportId(
                "unity2foxglove.ros2bridge.duplex-reconnect");
            var owner =
                FoxRunTransportRetirementOwner.CreateForTests(3);
            var transport = new DuplexTransport(
                DuplexMode.DisconnectAfterFirstPreparation);
            using var runtime = CreateRuntime(
                transport,
                owner,
                FoxRunTransportDirection.Publish,
                providerId: providerId,
                reconnectIntervalMs: 10);

            runtime.Start(enabled: true, autoConnect: true);
            _ = runtime.PreparePublisher(
                Topic,
                Schema,
                Qos(),
                out _);
            WaitUntil(
                () => transport.ConnectCount >= 2,
                "the duplex runtime did not reconnect");
            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(
                    Frame(sequence: 3),
                    out var reason),
                reason);
            WaitUntil(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the post-reconnect publish did not complete");

            var requests = transport.Requests;
            Assert.Equal(
                new[] { 1, 2 },
                requests
                    .Where(item =>
                        item.Message.Operation
                        == U2R2Operation.Hello)
                    .Select(item => item.Connection)
                    .ToArray());
            Assert.Equal(
                new[] { 1, 2 },
                requests
                    .Where(item =>
                        item.Message.Operation
                        == U2R2Operation.PreparePublisher)
                    .Select(item => item.Connection)
                    .ToArray());
            Assert.Equal(
                2,
                Assert.Single(
                    requests,
                    item =>
                        item.Message.Operation
                        == U2R2Operation.Publish)
                    .Connection);

            runtime.Stop();
            Assert.True(
                owner.TryReserveExclusive(
                    providerId,
                    FoxRunTransportDirection.Publish,
                    generation: 2,
                    workerCount: 3,
                    out var replacement));
            replacement.Dispose();
        }

        [Fact]
        public void DuplexStartFailsBeforeConnectWithoutThreeWorkerSlots()
        {
            var transport = new DuplexTransport(
                DuplexMode.Happy);
            using var runtime = CreateRuntime(
                transport,
                FoxRunTransportRetirementOwner
                    .CreateForTests(2),
                FoxRunTransportDirection.Publish);

            Assert.False(
                runtime.TryStart(
                    enabled: true,
                    autoConnect: true,
                    out var reason));
            Assert.Contains(
                "retirement ownership",
                reason,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, transport.ConnectCount);
            Assert.Equal(
                Ros2BridgeRuntimeLifecycleState.Stopped,
                runtime.LifecycleState);
        }

        private static Ros2BridgeRuntime CreateRuntime(
            DuplexTransport transport,
            FoxRunTransportRetirementOwner owner,
            FoxRunTransportDirection direction,
            FoxRunTransportId? providerId = null,
            int reconnectIntervalMs = 20)
            => new Ros2BridgeRuntime(
                "127.0.0.1",
                8765,
                queueCapacity: 8,
                reconnectIntervalMs,
                sendTimeoutMs: 1000,
                () => transport,
                owner,
                providerId
                ?? new FoxRunTransportId(
                    "unity2foxglove.ros2bridge.duplex-publish"),
                direction,
                generation: 1,
                joinTimeoutMs: 1500,
                enableDuplexSession: true);

        private static void WaitForPublisherReady(
            Ros2BridgeRuntime runtime)
        {
            WaitUntil(
                () => runtime.PreparePublisher(
                          Topic,
                          Schema,
                          Qos(),
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                "the duplex publisher did not become ready");
        }

        private static Ros2BridgeFrame Frame(ulong sequence)
            => Ros2BridgeFrame.CreateOwned(
                Topic,
                Schema,
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: sequence,
                sequence,
                payload: new byte[] { 0, 1, 0, 0, 42 },
                Qos());

        private static FoxRunResolvedQos Qos()
            => new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepLast,
                depth: 10);

        private static void WaitUntil(
            Func<bool> predicate,
            string failure)
        {
            Assert.True(
                SpinWait.SpinUntil(
                    predicate,
                    TimeSpan.FromSeconds(5)),
                failure);
        }

        private enum DuplexMode : byte
        {
            Happy = 1,
            V2Incompatible = 2,
            DisconnectAfterFirstPreparation = 3,
        }

        private sealed class CapturedRequest
        {
            internal CapturedRequest(
                int connection,
                U2R2Message message)
            {
                Connection = connection;
                Message = message;
            }

            internal int Connection { get; }

            internal U2R2Message Message { get; }
        }

        private sealed class ReadResult
        {
            internal ReadResult(byte[] bytes, Exception error)
            {
                Bytes = bytes;
                Error = error;
            }

            internal byte[] Bytes { get; }

            internal Exception Error { get; }
        }

        private sealed class SessionChannel
        {
            internal BlockingCollection<ReadResult> Responses { get; } =
                new BlockingCollection<ReadResult>();
        }

        private sealed class DuplexTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport,
            IRos2BridgeRawWireSink,
            IRos2BridgeV2SessionTransport,
            IRos2BridgeSessionTransport
        {
            private readonly object _gate = new object();
            private readonly DuplexMode _mode;
            private readonly List<CapturedRequest> _requests =
                new List<CapturedRequest>();
            private readonly List<int>
                _legacyPreparationConnections =
                    new List<int>();
            private readonly List<int> _legacyRawWriteConnections =
                new List<int>();
            private SessionChannel _channel;
            private int _connected;
            private int _connectCount;
            private int _legacyV2ExchangeCount;

            internal DuplexTransport(DuplexMode mode)
            {
                _mode = mode;
            }

            public bool IsConnected
                => Volatile.Read(ref _connected) != 0;

            internal int ConnectCount
                => Volatile.Read(ref _connectCount);

            internal int LegacyV2ExchangeCount
                => Volatile.Read(
                    ref _legacyV2ExchangeCount);

            internal int LegacyRawWriteCount
            {
                get
                {
                    lock (_gate)
                        return _legacyRawWriteConnections.Count;
                }
            }

            internal CapturedRequest[] Requests
            {
                get
                {
                    lock (_gate)
                        return _requests.ToArray();
                }
            }

            internal int[] LegacyPreparationConnections
            {
                get
                {
                    lock (_gate)
                    {
                        return _legacyPreparationConnections
                            .ToArray();
                    }
                }
            }

            internal int[] LegacyRawWriteConnections
            {
                get
                {
                    lock (_gate)
                        return _legacyRawWriteConnections.ToArray();
                }
            }

            public void Connect(
                string host,
                int port,
                int timeoutMs)
            {
                _ = host;
                _ = port;
                _ = timeoutMs;
                var channel = new SessionChannel();
                lock (_gate)
                    _channel = channel;
                Interlocked.Increment(ref _connectCount);
                Volatile.Write(ref _connected, 1);
            }

            public void Send(
                Ros2BridgeFrame frame,
                int timeoutMs)
            {
                _ = frame;
                _ = timeoutMs;
                lock (_gate)
                {
                    _legacyRawWriteConnections.Add(
                        ConnectCount);
                }
            }

            public byte[] ExchangePublisherPreparation(
                byte[] request,
                int timeoutMs)
            {
                _ = timeoutMs;
                var parsed =
                    Ros2BridgePublisherPreparationCodec
                        .ParseRequest(request);
                lock (_gate)
                {
                    _legacyPreparationConnections.Add(
                        ConnectCount);
                }
                return Ros2BridgePublisherPreparationCodec
                    .WriteResponseForTests(
                        parsed.RequestId,
                        "ok");
            }

            public void SendWire(
                ReadOnlyMemory<byte> wireBytes,
                int timeoutMs)
            {
                _ = wireBytes;
                _ = timeoutMs;
                lock (_gate)
                {
                    _legacyRawWriteConnections.Add(
                        ConnectCount);
                }
            }

            public byte[] ExchangeV2(
                ReadOnlyMemory<byte> request,
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
                _ = request;
                _ = limits;
                _ = timeoutMs;
                Interlocked.Increment(
                    ref _legacyV2ExchangeCount);
                throw new InvalidOperationException(
                    "The duplex runtime bypassed its owned writer.");
            }

            public void BeginV2(
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
                _ = limits;
                _ = timeoutMs;
                if (!IsConnected)
                {
                    throw new InvalidOperationException(
                        "The duplex transport is disconnected.");
                }
            }

            public void WriteV2(
                ReadOnlyMemory<byte> wireBytes,
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
                _ = timeoutMs;
                var request = U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(
                        wireBytes.ToArray(),
                        limits));
                SessionChannel channel;
                var connection = ConnectCount;
                lock (_gate)
                {
                    channel = _channel;
                    _requests.Add(
                        new CapturedRequest(
                            connection,
                            request));
                }

                if (_mode == DuplexMode.V2Incompatible
                    && request.Operation
                    == U2R2Operation.Hello)
                {
                    channel.Responses.Add(
                        new ReadResult(
                            null,
                            new Ros2BridgeV2IncompatibilityException(
                                "The peer closed before returning v2 bytes.")));
                    return;
                }

                channel.Responses.Add(
                    new ReadResult(
                        Response(
                            request,
                            connection,
                            limits),
                        null));
                if (_mode
                        == DuplexMode
                            .DisconnectAfterFirstPreparation
                    && connection == 1
                    && request.Operation
                    == U2R2Operation.PreparePublisher)
                {
                    Volatile.Write(ref _connected, 0);
                    channel.Responses.CompleteAdding();
                }
            }

            public byte[] ReadV2(
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
                _ = limits;
                _ = timeoutMs;
                SessionChannel channel;
                lock (_gate)
                    channel = _channel;
                ReadResult result;
                try
                {
                    result = channel.Responses.Take();
                }
                catch (InvalidOperationException exception)
                {
                    throw new System.IO.EndOfStreamException(
                        "The scripted duplex connection closed.",
                        exception);
                }
                if (result.Error != null)
                    throw result.Error;
                return result.Bytes;
            }

            public void Close() => Disconnect();

            public void Disconnect()
            {
                SessionChannel channel;
                lock (_gate)
                    channel = _channel;
                Volatile.Write(ref _connected, 0);
                if (channel != null
                    && !channel.Responses.IsAddingCompleted)
                {
                    channel.Responses.CompleteAdding();
                }
            }

            public void Dispose() => Disconnect();

            private static byte[] Response(
                U2R2Message request,
                int connection,
                U2R2ProtocolLimits limits)
            {
                JObject header;
                switch (request.Operation)
                {
                    case U2R2Operation.Hello:
                        header = new JObject
                        {
                            ["capabilities"] = new JArray(
                                request.Capabilities.Select(
                                    CapabilityWireValue)),
                            ["connectionGeneration"] =
                                checked((ulong)(100 + connection)),
                            ["op"] = "hello_ack",
                            ["protocolVersion"] = 2,
                            ["requestId"] = request.RequestId,
                            ["sessionId"] =
                                "phase186-duplex-" + connection,
                            ["status"] = "ok",
                        };
                        break;
                    case U2R2Operation.PreparePublisher:
                        header = Correlated(
                            request,
                            "publisher_ready");
                        break;
                    case U2R2Operation.Publish:
                        header = Correlated(
                            request,
                            "publish_result");
                        header["messageId"] =
                            request.MessageId;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unexpected duplex request: "
                            + request.Operation);
                }
                return U2R2ProtocolCodec.EncodeFrame(
                    header,
                    Array.Empty<byte>(),
                    limits);
            }

            private static JObject Correlated(
                U2R2Message request,
                string operation)
                => new JObject
                {
                    ["connectionGeneration"] =
                        request.ConnectionGeneration,
                    ["op"] = operation,
                    ["protocolVersion"] = 2,
                    ["requestId"] = request.RequestId,
                    ["sessionId"] = request.SessionId,
                    ["status"] = "ok",
                };

            private static string CapabilityWireValue(
                U2R2Capability capability)
                => capability == U2R2Capability.Publish
                    ? "publish"
                    : capability == U2R2Capability.Subscribe
                        ? "subscribe"
                        : throw new ArgumentOutOfRangeException(
                            nameof(capability));
        }
    }
}
