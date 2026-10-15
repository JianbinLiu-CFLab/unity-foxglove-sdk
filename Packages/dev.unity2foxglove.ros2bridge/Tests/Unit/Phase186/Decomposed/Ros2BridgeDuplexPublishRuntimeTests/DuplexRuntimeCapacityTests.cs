// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: Certify publish compatibility on the owned duplex writer lane.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeDuplexPublishRuntimeTests
    {
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

        private static U2R2ByteLease ReserveAllRuntimeTransient(
            Ros2BridgeRuntime runtime)
        {
            var worker = RequiredPrivateField(
                typeof(Ros2BridgeRuntime),
                "_run").GetValue(runtime);
            Assert.NotNull(worker);
            var outbound = RequiredPrivateField(
                worker.GetType(),
                "_outbound").GetValue(worker);
            Assert.NotNull(outbound);
            var authority = RequiredPrivateField(
                outbound.GetType(),
                "_inner").GetValue(outbound)
                as U2R2BoundedOutboundScheduler;
            Assert.NotNull(authority);
            Assert.True(
                authority.TryReserveTransient(
                    U2R2ProtocolLimits.Default.MaxTransientBytes,
                    out var lease),
                "the test could not occupy the runtime transient budget");
            return lease;
        }

        private static object RequestAdmissionGate(
            Ros2BridgeRuntime runtime)
        {
            var worker = RequiredPrivateField(
                typeof(Ros2BridgeRuntime),
                "_run").GetValue(runtime);
            Assert.NotNull(worker);
            var connection = RequiredPrivateField(
                worker.GetType(),
                "_duplexConnection").GetValue(worker);
            Assert.NotNull(connection);
            return RequiredPrivateField(
                connection.GetType(),
                "_requestAdmissionGate").GetValue(connection);
        }

        private static object OutboundSchedulerGate(
            Ros2BridgeRuntime runtime)
        {
            var worker = RequiredPrivateField(
                typeof(Ros2BridgeRuntime),
                "_run").GetValue(runtime);
            Assert.NotNull(worker);
            var outbound = RequiredPrivateField(
                worker.GetType(),
                "_outbound").GetValue(worker);
            Assert.NotNull(outbound);
            return RequiredPrivateField(
                outbound.GetType(),
                "_gate").GetValue(outbound);
        }

        private static FieldInfo RequiredPrivateField(
            Type type,
            string name)
            => type.GetField(
                   name,
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Required Bridge field is missing: "
                   + type.FullName
                   + "."
                   + name);

        private static void SetPrivateField<T>(
            object owner,
            string name,
            T value)
            => RequiredPrivateField(owner.GetType(), name)
                .SetValue(owner, value);

        private enum DuplexMode : byte
        {
            Happy = 1,
            V2Incompatible = 2,
            DisconnectAfterFirstPreparation = 3,
            FalseNegativeHealthProbeAfterHello = 4,
            HoldHelloResponse = 5,
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
            private int _healthProbeUnavailable;
            private int _disposed;

            internal DuplexTransport(DuplexMode mode)
            {
                _mode = mode;
            }

            public bool IsConnected
                => Volatile.Read(ref _connected) != 0
                   && Volatile.Read(
                       ref _healthProbeUnavailable) == 0;

            internal int ConnectCount
                => Volatile.Read(ref _connectCount);

            internal bool HealthProbeUnavailable
                => Volatile.Read(ref _healthProbeUnavailable) != 0;

            internal bool IsDisposed
                => Volatile.Read(ref _disposed) != 0;

            internal ManualResetEventSlim HelloWritten { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim AllowHelloResponse { get; } =
                new ManualResetEventSlim(false);

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
                if (IsDisposed)
                    throw new ObjectDisposedException(
                        nameof(DuplexTransport));
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

                if (_mode == DuplexMode.HoldHelloResponse
                    && request.Operation == U2R2Operation.Hello)
                {
                    HelloWritten.Set();
                    if (!AllowHelloResponse.Wait(
                            TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException(
                            "The test did not release the duplex hello response.");
                    }
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
                            .FalseNegativeHealthProbeAfterHello
                    && request.Operation
                    == U2R2Operation.Hello)
                {
                    Volatile.Write(
                        ref _healthProbeUnavailable,
                        1);
                }
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

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                AllowHelloResponse.Set();
                Disconnect();
                HelloWritten.Dispose();
                AllowHelloResponse.Dispose();
            }

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
