// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first live-worker coverage for the publish-only U2R2 v2 session.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeV2LiveRuntimeTests
    {
        private const string Topic = "/phase186/v2/live_runtime";
        private const string Schema = "phase186_msgs/msg/LiveRuntime";
        private static readonly FoxRunResolvedQos Qos =
            FoxRunResolvedQos.Default;

        [Fact]
        public void HelloGateKeepsConnectionAndPublisherPending()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.HoldHello);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.True(
                transport.HelloEntered.Wait(TimeSpan.FromSeconds(2)),
                "the worker did not send its v2 hello");
            try
            {
                Assert.False(runtime.IsConnected);
                Assert.True(
                    runtime.GetStatsSnapshot().TransientBytes > 0,
                    "the held hello exchange is outside the runtime byte authority");
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    runtime.PreparePublisher(
                        Topic,
                        Schema,
                        Qos,
                        out _));
                Thread.Sleep(75);

                var requests = transport.V2Requests;
                Assert.Single(requests);
                Assert.Equal(U2R2Operation.Hello, requests[0].Message.Operation);
                Assert.Equal(0, transport.LegacyPreparationCount);
                Assert.Equal(0, transport.RawWriteCount);
            }
            finally
            {
                transport.AllowHelloAck.Set();
            }
        }

        [Fact]
        public void PublisherPreparationRemainsInsideTransientAuthority()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.HoldPreparation);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);
            WaitFor(
                () => runtime.IsConnected,
                "the v2 session did not complete hello");

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    Topic,
                    Schema,
                    Qos,
                    out _));
            Assert.True(
                transport.PreparationEntered.Wait(
                    TimeSpan.FromSeconds(2)));
            try
            {
                Assert.True(
                    runtime.GetStatsSnapshot().TransientBytes > 0,
                    "the held preparation exchange is outside the runtime byte authority");
            }
            finally
            {
                transport.AllowPreparationAck.Set();
            }
            WaitForPublisherReady(runtime);
            Assert.Equal(
                0,
                runtime.GetStatsSnapshot().TransientBytes);
        }

        [Fact]
        public void OneLiveSocketCorrelatesHelloPreparationAndPublish()
        {
            using var transport = new ScriptedTransport(ScriptMode.Happy);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            WaitFor(
                () => runtime.IsConnected,
                "the runtime did not complete v2 hello");
            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(Frame(sequence: 8), out var reason),
                reason);
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the runtime did not receive a correlated publish_result");

            var requests = transport.V2Requests;
            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                requests.Select(item => item.Message.Operation).ToArray());
            Assert.All(
                requests,
                request => Assert.Equal(1, request.Connection));
            Assert.Equal(
                new ulong[] { 1, 2, 3 },
                requests.Select(item => item.Message.RequestId).ToArray());
            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(0, transport.LegacyPreparationCount);
            Assert.Equal(0, transport.RawWriteCount);

            var preparation = requests[1].Message;
            var publish = requests[2].Message;
            Assert.Equal("phase186-live-session-1", preparation.SessionId);
            Assert.Equal(7UL, preparation.ConnectionGeneration);
            Assert.Equal(preparation.SessionId, publish.SessionId);
            Assert.Equal(
                preparation.ConnectionGeneration,
                publish.ConnectionGeneration);
            Assert.Equal(1UL, publish.MessageId);
            Assert.Equal(8UL, publish.LogTimeNs);
        }

        [Fact]
        public void PublicEnqueueCannotBypassV2PublisherPreparation()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.Happy);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);
            WaitFor(
                () => runtime.IsConnected,
                "the v2 session did not become ready");

            var withoutQos = Ros2BridgeFrame.CreateOwned(
                Topic,
                Schema,
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: 1,
                sequence: 1,
                payload: new byte[] { 0x00, 0x01, 0x00, 0x00 });
            Assert.False(
                runtime.TryEnqueue(withoutQos, out var missingQosReason));
            Assert.Contains(
                "QoS",
                missingQosReason,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, runtime.GetStatsSnapshot().AcceptedFrames);

            var frame = Frame(sequence: 2);
            Assert.True(
                runtime.TryEnqueue(frame, out var enqueueReason),
                enqueueReason);
            WaitFor(
                () => transport.V2Requests.Any(
                    request => request.Message.Operation
                               == U2R2Operation.PreparePublisher),
                "public enqueue did not schedule publisher preparation");
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the prepared public frame was not published");

            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                transport.V2Requests
                    .Select(request => request.Message.Operation)
                    .ToArray());
        }

        [Fact]
        public void SynchronousSendQueuesPreparationBeforeV2Publish()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.Happy);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            runtime.Send(Frame(sequence: 3), timeoutMs: 1000);

            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the synchronous public adapter did not publish");
            Assert.Equal(
                new[]
                {
                    U2R2Operation.Hello,
                    U2R2Operation.PreparePublisher,
                    U2R2Operation.Publish,
                },
                transport.V2Requests
                    .Select(request => request.Message.Operation)
                    .ToArray());
        }

        [Theory]
        [InlineData(ScriptMode.BusyAtHello)]
        [InlineData(ScriptMode.MalformedHelloResponse)]
        [InlineData(ScriptMode.WrongHelloCorrelation)]
        [InlineData(ScriptMode.WrongPreparationGeneration)]
        public void TerminalProtocolFaultNeverDowngradesCurrentAttempt(
            ScriptMode mode)
        {
            using var transport = new ScriptedTransport(mode);
            using var runtime = Runtime(
                transport,
                reconnectIntervalMs: 10000);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.True(
                transport.HelloEntered.Wait(TimeSpan.FromSeconds(2)),
                "the worker did not send its v2 hello");
            if (mode == ScriptMode.WrongPreparationGeneration)
            {
                WaitFor(
                    () => runtime.IsConnected,
                    "the runtime did not complete the initial hello");
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    runtime.PreparePublisher(
                        Topic,
                        Schema,
                        Qos,
                        out _));
            }

            WaitFor(
                () =>
                {
                    var stats = runtime.GetStatsSnapshot();
                    return transport.DisconnectCount > 0
                           && !stats.Connected
                           && !string.IsNullOrWhiteSpace(stats.LastError);
                },
                "the terminal protocol fault was not surfaced");
            Thread.Sleep(75);

            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(0, transport.LegacyPreparationCount);
            Assert.Equal(0, transport.RawWriteCount);
            Assert.False(runtime.IsConnected);
            Assert.NotEmpty(runtime.GetStatsSnapshot().LastError);
        }

        [Theory]
        [InlineData(ScriptMode.UnsupportedProtocol)]
        [InlineData(ScriptMode.CleanEofBeforeHelloResponse)]
        public void ExplicitPublishOnlyIncompatibilityUsesFreshLegacySocket(
            ScriptMode mode)
        {
            using var transport = new ScriptedTransport(mode);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);

            WaitFor(
                () => runtime.IsConnected && transport.ConnectCount == 2,
                "the publish-only runtime did not establish its fresh v1 socket");
            WaitForPublisherReady(runtime);
            Assert.True(
                runtime.TryEnqueuePrepared(Frame(sequence: 9), out var reason),
                reason);
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the v1 fallback socket did not publish");

            var hello = Assert.Single(transport.V2Requests);
            Assert.Equal(U2R2Operation.Hello, hello.Message.Operation);
            Assert.Equal(1, hello.Connection);
            Assert.Equal(new[] { 2 }, transport.LegacyPreparationConnections);
            var rawWrite = Assert.Single(transport.RawWrites);
            Assert.Equal(2, rawWrite.Connection);
            Assert.Equal(
                "publish",
                U2R2ProtocolCodec.DecodeFrame(rawWrite.WireBytes)
                    .Header
                    .Value<string>("op"));
            Assert.Equal(2, transport.ConnectCount);
            Assert.True(transport.DisconnectCount >= 1);
        }

        [Fact]
        public void SubscriptionRequiredSessionNeverFallsBackToV1()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.UnsupportedProtocol);
            var owner = FoxRunTransportRetirementOwner.CreateForTests(1);
            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                19484,
                queueCapacity: 8,
                reconnectIntervalMs: 10000,
                sendTimeoutMs: 500,
                sinkFactory: () => transport,
                owner,
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge.v2-live-subscribe-red"),
                FoxRunTransportDirection.Subscribe,
                generation: 1,
                joinTimeoutMs: 1000);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.True(
                transport.HelloEntered.Wait(TimeSpan.FromSeconds(2)),
                "the subscription-required session did not send hello");
            Thread.Sleep(100);

            var hello = Assert.Single(transport.V2Requests);
            Assert.Contains(
                U2R2Capability.Subscribe,
                hello.Message.Capabilities);
            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(0, transport.LegacyPreparationCount);
            Assert.False(runtime.IsConnected);
        }

        [Fact]
        public void QueuedFrameAfterReconnectUsesNewSidecarIdentity()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.ReconnectBeforePublish);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    Topic,
                    Schema,
                    Qos,
                    out _));

            Assert.True(
                transport.SecondConnectEntered.Wait(TimeSpan.FromSeconds(3)),
                "the first prepared connection did not enter reconnect");
            try
            {
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Ready,
                    runtime.PreparePublisher(
                        Topic,
                        Schema,
                        Qos,
                        out _));
                Assert.True(
                    runtime.TryEnqueuePrepared(
                        Frame(sequence: 10),
                        out var reason),
                    reason);
            }
            finally
            {
                transport.AllowSecondConnect.Set();
            }

            Assert.True(
                transport.SecondPreparationObserved.Wait(
                    TimeSpan.FromSeconds(3)),
                "the second session did not replay publisher preparation");
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the queued frame did not publish after reconnect");

            var publishes = transport.V2Requests
                .Where(item => item.Message.Operation == U2R2Operation.Publish)
                .ToArray();
            var publish = Assert.Single(publishes);
            Assert.Equal(2, publish.Connection);
            Assert.Equal("phase186-live-session-2", publish.Message.SessionId);
            Assert.Equal(8UL, publish.Message.ConnectionGeneration);
            Assert.DoesNotContain(
                publishes,
                item => item.Connection == 1);
        }

        [Fact]
        public void PreparationDisconnectAfterWriteLeaseReconnectsWithoutLosingFrame()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.DisconnectDuringLeasedPreparation);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);
            WaitFor(
                () => runtime.IsConnected,
                "the initial v2 session did not connect");
            WaitForPublisherReady(runtime);

            var worker = RequiredField(
                typeof(Ros2BridgeRuntime),
                "_run").GetValue(runtime);
            Assert.NotNull(worker);
            var workerGate = RequiredField(
                worker.GetType(),
                "_gate").GetValue(worker);
            Assert.NotNull(workerGate);
            var invalidate = worker.GetType().GetMethod(
                "InvalidatePreparationsLocked",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "The worker preparation invalidation seam is missing.");

            lock (workerGate)
            {
                Assert.True(
                    runtime.TryEnqueuePrepared(
                        Frame(sequence: 12),
                        out var reason),
                    reason);
                invalidate.Invoke(worker, null);
            }

            WaitFor(
                () => transport.ConnectCount == 2,
                "the leased preparation failure did not return to reconnect");
            Assert.True(
                transport.SecondPreparationObserved.Wait(
                    TimeSpan.FromSeconds(3)),
                "the replacement session did not replay publisher preparation");
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the leased frame was lost across preparation reconnect");

            var publish = Assert.Single(
                transport.V2Requests,
                request => request.Message.Operation
                           == U2R2Operation.Publish);
            Assert.Equal(2, publish.Connection);
            var stats = runtime.GetStatsSnapshot();
            Assert.Equal(0, stats.DroppedFrames);
            Assert.Equal(0, stats.FailedFrames);
            Assert.Equal(0, stats.FaultedFrames);
        }

        [Fact]
        public void TransientContentionRetriesWithoutLosingAcceptedFrame()
        {
            using var transport = new ScriptedTransport(
                ScriptMode.ReconnectBeforePublish);
            using var runtime = Runtime(transport);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    Topic,
                    Schema,
                    Qos,
                    out _));
            Assert.True(
                transport.SecondConnectEntered.Wait(TimeSpan.FromSeconds(3)),
                "the first prepared connection did not enter reconnect");

            U2R2ByteLease blocker = null;
            try
            {
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Ready,
                    runtime.PreparePublisher(
                        Topic,
                        Schema,
                        Qos,
                        out _));
                Assert.True(
                    runtime.TryEnqueuePrepared(
                        Frame(sequence: 11),
                        out var reason),
                    reason);
                blocker = ReserveAllRuntimeTransient(runtime);
                transport.AllowSecondConnect.Set();
                Thread.Sleep(150);
                Assert.Equal(
                    0,
                    runtime.GetStatsSnapshot().SentFrames);
            }
            finally
            {
                transport.AllowSecondConnect.Set();
                blocker?.Dispose();
            }

            Assert.True(
                transport.SecondPreparationObserved.Wait(
                    TimeSpan.FromSeconds(3)),
                "the second session did not replay publisher preparation");
            WaitFor(
                () => runtime.GetStatsSnapshot().SentFrames == 1,
                "the accepted frame was lost when transient capacity returned");
            var stats = runtime.GetStatsSnapshot();
            Assert.Equal(0, stats.DroppedFrames);
            Assert.Equal(0, stats.FailedFrames);
            Assert.Equal(0, stats.FaultedFrames);
            Assert.Equal(0, stats.TransientBytes);
            Assert.Equal(0, stats.InFlightBytes);
        }

        private static Ros2BridgeRuntime Runtime(
            ScriptedTransport transport,
            int reconnectIntervalMs = 50)
            => new Ros2BridgeRuntime(
                "127.0.0.1",
                19484,
                queueCapacity: 8,
                reconnectIntervalMs,
                sendTimeoutMs: 500,
                sinkFactory: () => transport);

        private static void WaitForPublisherReady(
            Ros2BridgeRuntime runtime)
        {
            _ = runtime.PreparePublisher(
                Topic,
                Schema,
                Qos,
                out _);
            WaitFor(
                () => runtime.PreparePublisher(
                          Topic,
                          Schema,
                          Qos,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                "the exact publisher contract did not become ready");
        }

        private static Ros2BridgeFrame Frame(ulong sequence)
            => Ros2BridgeFrame.CreateOwned(
                Topic,
                Schema,
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: sequence,
                sequence,
                payload: new byte[] { 0x00, 0x01, 0x00, 0x00 },
                Qos);

        private static U2R2ByteLease ReserveAllRuntimeTransient(
            Ros2BridgeRuntime runtime)
        {
            var worker = RequiredField(
                typeof(Ros2BridgeRuntime),
                "_run").GetValue(runtime);
            Assert.NotNull(worker);
            var outbound = RequiredField(
                worker.GetType(),
                "_outbound").GetValue(worker);
            Assert.NotNull(outbound);
            var authority = RequiredField(
                outbound.GetType(),
                "_inner").GetValue(outbound)
                as U2R2BoundedOutboundScheduler;
            Assert.NotNull(authority);
            Assert.True(
                authority.TryReserveTransient(
                    U2R2ProtocolLimits.Default.MaxTransientBytes,
                    out var lease),
                "the test could not occupy the real runtime transient budget");
            return lease;
        }

        private static FieldInfo RequiredField(Type type, string name)
            => type.GetField(
                   name,
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Required live-runtime field is missing: "
                   + type.FullName
                   + "."
                   + name);

        private static void WaitFor(
            Func<bool> condition,
            string message,
            int timeoutMs = 3000)
            => Assert.True(
                SpinWait.SpinUntil(
                    condition,
                    TimeSpan.FromMilliseconds(timeoutMs)),
                message);

        public enum ScriptMode
        {
            Happy = 1,
            HoldHello = 2,
            BusyAtHello = 3,
            MalformedHelloResponse = 4,
            WrongHelloCorrelation = 5,
            WrongPreparationGeneration = 6,
            UnsupportedProtocol = 7,
            CleanEofBeforeHelloResponse = 8,
            ReconnectBeforePublish = 9,
            HoldPreparation = 10,
            DisconnectDuringLeasedPreparation = 11,
        }

        private sealed class CapturedV2Request
        {
            internal CapturedV2Request(
                int connection,
                U2R2Message message,
                byte[] wireBytes)
            {
                Connection = connection;
                Message = message;
                WireBytes = wireBytes;
            }

            internal int Connection { get; }
            internal U2R2Message Message { get; }
            internal byte[] WireBytes { get; }
        }

        private sealed class CapturedRawWrite
        {
            internal CapturedRawWrite(
                int connection,
                byte[] wireBytes)
            {
                Connection = connection;
                WireBytes = wireBytes;
            }

            internal int Connection { get; }
            internal byte[] WireBytes { get; }
        }

        private sealed class ScriptedTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport,
            IRos2BridgeRawWireSink,
            IRos2BridgeV2SessionTransport
        {
            private readonly object _gate = new object();
            private readonly ScriptMode _mode;
            private readonly List<CapturedV2Request> _v2Requests =
                new List<CapturedV2Request>();
            private readonly List<int> _legacyPreparationConnections =
                new List<int>();
            private readonly List<CapturedRawWrite> _rawWrites =
                new List<CapturedRawWrite>();
            private int _connected;
            private int _connectCount;
            private int _disconnectCount;
            private int _currentConnection;
            private int _legacySendCount;
            private int _preparationCount;

            internal ScriptedTransport(ScriptMode mode)
            {
                _mode = mode;
                if (mode != ScriptMode.HoldHello)
                    AllowHelloAck.Set();
                if (mode != ScriptMode.ReconnectBeforePublish)
                    AllowSecondConnect.Set();
                if (mode != ScriptMode.HoldPreparation)
                    AllowPreparationAck.Set();
            }

            internal ManualResetEventSlim HelloEntered { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim AllowHelloAck { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim SecondConnectEntered { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim AllowSecondConnect { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim SecondPreparationObserved { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim PreparationEntered { get; } =
                new ManualResetEventSlim(false);

            internal ManualResetEventSlim AllowPreparationAck { get; } =
                new ManualResetEventSlim(false);

            public bool IsConnected => Volatile.Read(ref _connected) != 0;

            internal int ConnectCount => Volatile.Read(ref _connectCount);

            internal int DisconnectCount =>
                Volatile.Read(ref _disconnectCount);

            internal int LegacyPreparationCount
            {
                get
                {
                    lock (_gate)
                        return _legacyPreparationConnections.Count;
                }
            }

            internal int RawWriteCount
            {
                get
                {
                    lock (_gate)
                        return _rawWrites.Count;
                }
            }

            internal CapturedV2Request[] V2Requests
            {
                get
                {
                    lock (_gate)
                        return _v2Requests.ToArray();
                }
            }

            internal int[] LegacyPreparationConnections
            {
                get
                {
                    lock (_gate)
                        return _legacyPreparationConnections.ToArray();
                }
            }

            internal CapturedRawWrite[] RawWrites
            {
                get
                {
                    lock (_gate)
                        return _rawWrites.ToArray();
                }
            }

            public void Connect(string host, int port, int timeoutMs)
            {
                _ = host;
                _ = port;
                _ = timeoutMs;
                var connection = Interlocked.Increment(ref _connectCount);
                Volatile.Write(ref _currentConnection, connection);
                Volatile.Write(ref _connected, 1);
                if (_mode == ScriptMode.ReconnectBeforePublish
                    && connection == 2)
                {
                    SecondConnectEntered.Set();
                    if (!AllowSecondConnect.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException(
                            "The test did not release the second connection.");
                    }
                }
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
            {
                _ = frame;
                _ = timeoutMs;
                Interlocked.Increment(ref _legacySendCount);
            }

            public byte[] ExchangePublisherPreparation(
                byte[] request,
                int timeoutMs)
            {
                _ = timeoutMs;
                var parsed =
                    Ros2BridgePublisherPreparationCodec.ParseRequest(request);
                lock (_gate)
                {
                    _legacyPreparationConnections.Add(
                        Volatile.Read(ref _currentConnection));
                }
                return Ros2BridgePublisherPreparationCodec
                    .WriteResponseForTests(parsed.RequestId, "ok");
            }

            public void SendWire(
                ReadOnlyMemory<byte> wireBytes,
                int timeoutMs)
            {
                _ = timeoutMs;
                lock (_gate)
                {
                    _rawWrites.Add(
                        new CapturedRawWrite(
                            Volatile.Read(ref _currentConnection),
                            wireBytes.ToArray()));
                }
            }

            public byte[] ExchangeV2(
                ReadOnlyMemory<byte> request,
                U2R2ProtocolLimits limits,
                int timeoutMs)
            {
                _ = timeoutMs;
                var wireBytes = request.ToArray();
                var message = U2R2ProtocolCodec.ParseV2(
                    U2R2ProtocolCodec.DecodeFrame(wireBytes, limits));
                var connection = Volatile.Read(ref _currentConnection);
                lock (_gate)
                {
                    _v2Requests.Add(
                        new CapturedV2Request(
                            connection,
                            message,
                            wireBytes));
                }

                switch (message.Operation)
                {
                    case U2R2Operation.Hello:
                        HelloEntered.Set();
                        return RespondToHello(
                            message,
                            limits,
                            connection);
                    case U2R2Operation.PreparePublisher:
                        return RespondToPreparation(
                            message,
                            limits,
                            connection);
                    case U2R2Operation.Publish:
                        return PublishResult(message, limits);
                    default:
                        throw new InvalidOperationException(
                            "Unexpected live-runtime operation: "
                            + message.Operation);
                }
            }

            public void Disconnect()
            {
                Interlocked.Increment(ref _disconnectCount);
                Volatile.Write(ref _connected, 0);
            }

            public void Dispose()
            {
                AllowHelloAck.Set();
                AllowSecondConnect.Set();
                AllowPreparationAck.Set();
                Disconnect();
                HelloEntered.Dispose();
                AllowHelloAck.Dispose();
                SecondConnectEntered.Dispose();
                AllowSecondConnect.Dispose();
                SecondPreparationObserved.Dispose();
                PreparationEntered.Dispose();
                AllowPreparationAck.Dispose();
            }

            private byte[] RespondToHello(
                U2R2Message request,
                U2R2ProtocolLimits limits,
                int connection)
            {
                if (!AllowHelloAck.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "The test did not release hello.");
                }

                switch (_mode)
                {
                    case ScriptMode.BusyAtHello:
                        return ErrorResponse(
                            "busy",
                            "busy",
                            "data session already leased",
                            request.RequestId,
                            limits);
                    case ScriptMode.MalformedHelloResponse:
                        return new byte[] { (byte)'U' };
                    case ScriptMode.WrongHelloCorrelation:
                        return HelloAck(
                            checked(request.RequestId + 1),
                            connection,
                            request.Capabilities,
                            limits);
                    case ScriptMode.UnsupportedProtocol:
                        return ErrorResponse(
                            "fault",
                            "unsupported_protocol",
                            "protocolVersion 2 is unavailable",
                            request.RequestId,
                            limits);
                    case ScriptMode.CleanEofBeforeHelloResponse:
                        throw new Ros2BridgeV2IncompatibilityException(
                            "The peer closed before returning any v2 bytes.");
                    default:
                        return HelloAck(
                            request.RequestId,
                            connection,
                            request.Capabilities,
                            limits);
                }
            }

            private byte[] RespondToPreparation(
                U2R2Message request,
                U2R2ProtocolLimits limits,
                int connection)
            {
                var preparation = Interlocked.Increment(
                    ref _preparationCount);
                PreparationEntered.Set();
                if (!AllowPreparationAck.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "The test did not release publisher preparation.");
                }
                if (_mode == ScriptMode.WrongPreparationGeneration)
                {
                    return PublisherReady(
                        request,
                        checked(request.ConnectionGeneration + 1),
                        limits);
                }
                if (_mode == ScriptMode.DisconnectDuringLeasedPreparation
                    && preparation == 2)
                {
                    Volatile.Write(ref _connected, 0);
                    throw new IOException(
                        "The scripted publisher preparation connection closed.");
                }

                var response = PublisherReady(
                    request,
                    request.ConnectionGeneration,
                    limits);
                if (_mode == ScriptMode.ReconnectBeforePublish)
                {
                    if (connection == 1)
                        Volatile.Write(ref _connected, 0);
                    else if (connection == 2)
                        SecondPreparationObserved.Set();
                }
                else if (
                    _mode
                    == ScriptMode.DisconnectDuringLeasedPreparation
                    && connection == 2)
                {
                    SecondPreparationObserved.Set();
                }
                return response;
            }

            private static byte[] HelloAck(
                ulong requestId,
                int connection,
                IReadOnlyList<U2R2Capability> capabilities,
                U2R2ProtocolLimits limits)
                => U2R2ProtocolCodec.EncodeFrame(
                    new JObject
                    {
                        ["op"] = "hello_ack",
                        ["protocolVersion"] = 2,
                        ["requestId"] = requestId,
                        ["status"] = "ok",
                        ["sessionId"] =
                            "phase186-live-session-" + connection,
                        ["connectionGeneration"] =
                            checked((ulong)(6 + connection)),
                        ["capabilities"] = new JArray(
                            capabilities.Select(CapabilityWireValue)),
                    },
                    Array.Empty<byte>(),
                    limits);

            private static byte[] PublisherReady(
                U2R2Message request,
                ulong generation,
                U2R2ProtocolLimits limits)
                => U2R2ProtocolCodec.EncodeFrame(
                    new JObject
                    {
                        ["op"] = "publisher_ready",
                        ["protocolVersion"] = 2,
                        ["requestId"] = request.RequestId,
                        ["status"] = "ok",
                        ["sessionId"] = request.SessionId,
                        ["connectionGeneration"] = generation,
                    },
                    Array.Empty<byte>(),
                    limits);

            private static byte[] PublishResult(
                U2R2Message request,
                U2R2ProtocolLimits limits)
                => U2R2ProtocolCodec.EncodeFrame(
                    new JObject
                    {
                        ["op"] = "publish_result",
                        ["protocolVersion"] = 2,
                        ["requestId"] = request.RequestId,
                        ["status"] = "ok",
                        ["sessionId"] = request.SessionId,
                        ["connectionGeneration"] =
                            request.ConnectionGeneration,
                        ["messageId"] = request.MessageId,
                    },
                    Array.Empty<byte>(),
                    limits);

            private static byte[] ErrorResponse(
                string operation,
                string errorCode,
                string message,
                ulong requestId,
                U2R2ProtocolLimits limits)
                => U2R2ProtocolCodec.EncodeFrame(
                    new JObject
                    {
                        ["op"] = operation,
                        ["protocolVersion"] = 2,
                        ["requestId"] = requestId,
                        ["status"] = "error",
                        ["errorCode"] = errorCode,
                        ["message"] = message,
                        ["terminal"] = true,
                    },
                    Array.Empty<byte>(),
                    limits);

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
