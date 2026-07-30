// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "184-D")]
    [Trait("Domain", "Ros2Bridge")]
    public sealed class Ros2BridgePublisherPreparationTests
    {
        [Fact]
        public void FirstProbeIsPendingUntilCorrelatedExactContractAck()
        {
            var transport = new PreparationTransport("ok");
            using var runtime = Runtime(() => transport);
            runtime.Start(enabled: true, autoConnect: true);

            var initial = runtime.PreparePublisher(
                "/phase184/custom",
                "phase184_msgs/msg/CustomEnvelope",
                FoxRunResolvedQos.SensorData,
                out var initialReason);

            Assert.Equal(Ros2BridgePublisherReadiness.Pending, initial);
            Assert.Contains("pending", initialReason, StringComparison.OrdinalIgnoreCase);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/custom",
                          "phase184_msgs/msg/CustomEnvelope",
                          FoxRunResolvedQos.SensorData,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));

            var request = Ros2BridgePublisherPreparationCodec.ParseRequest(
                Assert.Single(transport.Requests));
            Assert.Equal(1, request.ProtocolVersion);
            Assert.False(string.IsNullOrWhiteSpace(request.RequestId));
            Assert.Equal("/phase184/custom", request.Topic);
            Assert.Equal("phase184_msgs/msg/CustomEnvelope", request.SchemaName);
            Assert.Equal("cdr", request.Encoding);
            Assert.Equal(FoxRunResolvedQos.SensorData, request.Qos);
            Assert.Empty(transport.SentFrames);
        }

        [Fact]
        public void RejectedAckAndLegacyTransportFailClosed()
        {
            var rejectedTransport = new PreparationTransport(
                "error",
                "publisher_unavailable",
                "typesupport unavailable");
            using (var rejectedRuntime = Runtime(() => rejectedTransport))
            {
                rejectedRuntime.Start(enabled: true, autoConnect: true);
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    rejectedRuntime.PreparePublisher(
                        "/phase184/rejected",
                        "phase184_msgs/msg/Rejected",
                        FoxRunResolvedQos.Default,
                        out _));
                Assert.True(SpinWait.SpinUntil(
                    () => rejectedRuntime.PreparePublisher(
                              "/phase184/rejected",
                              "phase184_msgs/msg/Rejected",
                              FoxRunResolvedQos.Default,
                              out _)
                          == Ros2BridgePublisherReadiness.Rejected,
                    TimeSpan.FromSeconds(3)));
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Rejected,
                    rejectedRuntime.PreparePublisher(
                        "/phase184/rejected",
                        "phase184_msgs/msg/Rejected",
                        FoxRunResolvedQos.Default,
                        out var reason));
                Assert.Contains("typesupport unavailable", reason, StringComparison.Ordinal);
            }

            var legacy = new LegacyTransport();
            using var legacyRuntime = Runtime(() => legacy);
            legacyRuntime.Start(enabled: true, autoConnect: true);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                legacyRuntime.PreparePublisher(
                    "/phase184/legacy",
                    "phase184_msgs/msg/Legacy",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => legacyRuntime.PreparePublisher(
                          "/phase184/legacy",
                          "phase184_msgs/msg/Legacy",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Rejected,
                TimeSpan.FromSeconds(3)));
        }

        [Fact]
        public void ReconnectInvalidatesReadyAndRequeuesEveryExactContract()
        {
            var firstSendGate = new ManualResetEventSlim(false);
            var first = new PreparationTransport("ok")
            {
                FailNextPublish = true,
                SendGate = firstSendGate
            };
            var responseGate = new ManualResetEventSlim(false);
            var second = new PreparationTransport("ok") { ResponseGate = responseGate };
            var transports = new Queue<IRos2BridgeSink>(new IRos2BridgeSink[] { first, second });
            using var runtime = Runtime(() => transports.Dequeue());
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/reconnect",
                    "phase184_msgs/msg/Reconnect",
                    FoxRunResolvedQos.SystemDefault,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/reconnect",
                          "phase184_msgs/msg/Reconnect",
                          FoxRunResolvedQos.SystemDefault,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));

            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/reconnect",
                    "phase184_msgs/msg/Reconnect",
                    FoxRunResolvedQos.SystemDefault,
                    sequence: 1UL),
                out var enqueueReason), enqueueReason);
            Assert.True(SpinWait.SpinUntil(
                () => first.SendStarted.IsSet,
                TimeSpan.FromSeconds(3)));
            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/reconnect",
                    "phase184_msgs/msg/Reconnect",
                    FoxRunResolvedQos.SystemDefault,
                    sequence: 2UL),
                out enqueueReason), enqueueReason);
            firstSendGate.Set();

            Assert.True(SpinWait.SpinUntil(
                () => second.RequestCount == 1,
                TimeSpan.FromSeconds(3)));
            Assert.Equal(0, second.SentFrameCount);
            responseGate.Set();
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/reconnect",
                          "phase184_msgs/msg/Reconnect",
                          FoxRunResolvedQos.SystemDefault,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => second.SentFrameCount == 1,
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => second.EventSnapshot.Length == 2,
                TimeSpan.FromSeconds(3)));
            Assert.Single(first.Requests);
            Assert.Single(second.Requests);
            Assert.Equal(new[] { "prepare", "send" }, second.EventSnapshot);
        }

        [Fact]
        public void RejectedReconnectDropsPreparedQueuedFrameWithoutSendingIt()
        {
            var firstSendGate = new ManualResetEventSlim(false);
            var first = new PreparationTransport("ok")
            {
                FailNextPublish = true,
                SendGate = firstSendGate
            };
            var legacy = new LegacyTransport();
            var transports = new Queue<IRos2BridgeSink>(
                new IRos2BridgeSink[] { first, legacy });
            using var runtime = Runtime(() => transports.Dequeue());
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/rejected_reconnect",
                    "phase184_msgs/msg/RejectedReconnect",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/rejected_reconnect",
                          "phase184_msgs/msg/RejectedReconnect",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));

            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/rejected_reconnect",
                    "phase184_msgs/msg/RejectedReconnect",
                    FoxRunResolvedQos.Default,
                    sequence: 1UL),
                out var reason), reason);
            Assert.True(SpinWait.SpinUntil(
                () => first.SendStarted.IsSet,
                TimeSpan.FromSeconds(3)));
            Assert.True(runtime.TryEnqueuePrepared(
                Frame(
                    "/phase184/rejected_reconnect",
                    "phase184_msgs/msg/RejectedReconnect",
                    FoxRunResolvedQos.Default,
                    sequence: 2UL),
                out reason), reason);
            firstSendGate.Set();

            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/rejected_reconnect",
                          "phase184_msgs/msg/RejectedReconnect",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Rejected,
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.GetStatsSnapshot().FailedFrames >= 2,
                TimeSpan.FromSeconds(3)));
            Assert.Empty(legacy.SentFrames);
            Assert.True(runtime.GetStatsSnapshot().DroppedFrames >= 1);
        }

        [Fact]
        public void ExactCacheKeyIncludesTopicSchemaAndEveryQosAxis()
        {
            var transport = new PreparationTransport("ok");
            using var runtime = Runtime(() => transport);
            runtime.Start(enabled: true, autoConnect: true);
            var contracts = new[]
            {
                ("/phase184/key", "phase184_msgs/msg/Key", FoxRunResolvedQos.Default),
                ("/phase184/key2", "phase184_msgs/msg/Key", FoxRunResolvedQos.Default),
                ("/phase184/key", "phase184_msgs/msg/Key2", FoxRunResolvedQos.Default),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.SensorData, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepLast, 10)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.BestEffort,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepLast, 10)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.TransientLocal, FoxRunQosHistory.KeepLast, 10)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepAll, 0)),
                ("/phase184/key", "phase184_msgs/msg/Key",
                    new FoxRunResolvedQos(FoxRunQosProfile.Default, FoxRunQosReliability.Reliable,
                        FoxRunQosDurability.Volatile, FoxRunQosHistory.KeepLast, 11))
            };

            for (var index = 0; index < contracts.Length; index++)
            {
                var contract = contracts[index];
                Assert.Equal(
                    Ros2BridgePublisherReadiness.Pending,
                    runtime.PreparePublisher(
                        contract.Item1,
                        contract.Item2,
                        contract.Item3,
                        out _));
                Assert.True(SpinWait.SpinUntil(
                    () => runtime.PreparePublisher(
                              contract.Item1,
                              contract.Item2,
                              contract.Item3,
                              out _)
                          == Ros2BridgePublisherReadiness.Ready,
                    TimeSpan.FromSeconds(3)));
            }

            Assert.Equal(contracts.Length, transport.RequestCount);
        }

        [Fact]
        public void PreparationRegistryRejectsUniqueContractsBeyondQueueCapacity()
        {
            var transport = new PreparationTransport("ok")
            {
                ResponseGate = new ManualResetEventSlim(false)
            };
            using var runtime = Runtime(() => transport, queueCapacity: 2);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/capacity_1",
                    "phase184_msgs/msg/CapacityOne",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/capacity_2",
                    "phase184_msgs/msg/CapacityTwo",
                    FoxRunResolvedQos.Default,
                    out _));

            Assert.Equal(
                Ros2BridgePublisherReadiness.Rejected,
                runtime.PreparePublisher(
                    "/phase184/capacity_3",
                    "phase184_msgs/msg/CapacityThree",
                    FoxRunResolvedQos.Default,
                    out var firstReason));
            Assert.Contains("capacity", firstReason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Rejected,
                runtime.PreparePublisher(
                    "/phase184/capacity_3",
                    "phase184_msgs/msg/CapacityThree",
                    FoxRunResolvedQos.Default,
                    out var secondReason));
            Assert.Equal(firstReason, secondReason);
        }

        [Fact]
        public void OversizedPreparationHeaderIsRejectedBeforeItConsumesRegistryCapacity()
        {
            var transport = new PreparationTransport("ok");
            using var runtime = Runtime(() => transport, queueCapacity: 1);
            runtime.Start(enabled: true, autoConnect: true);
            var oversizedTopic = "/" + new string('a', Ros2BridgeFrameWriter.MaxHeaderBytes);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Rejected,
                runtime.PreparePublisher(
                    oversizedTopic,
                    "phase184_msgs/msg/Oversized",
                    FoxRunResolvedQos.Default,
                    out var rejectedReason));
            Assert.Contains("header", rejectedReason, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/after_oversized",
                    "phase184_msgs/msg/AfterOversized",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/after_oversized",
                          "phase184_msgs/msg/AfterOversized",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));
            Assert.Single(transport.Requests);
        }

        [Fact]
        public void MalformedPreparationResponseRejectsWithoutReconnectLoop()
        {
            var transport = new PreparationTransport("ok")
            {
                MutateResponse = response =>
                    MutateHeader(response, header => header["status"] = true)
            };
            using var runtime = Runtime(
                () => transport,
                reconnectIntervalMs: 100);
            runtime.Start(enabled: true, autoConnect: true);

            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/malformed_response",
                    "phase184_msgs/msg/MalformedResponse",
                    FoxRunResolvedQos.Default,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          "/phase184/malformed_response",
                          "phase184_msgs/msg/MalformedResponse",
                          FoxRunResolvedQos.Default,
                          out _)
                      == Ros2BridgePublisherReadiness.Rejected,
                TimeSpan.FromSeconds(3)));
            Thread.Sleep(250);

            Assert.Equal(1, transport.ConnectCount);
            Assert.Equal(1, transport.RequestCount);
        }

        [Fact]
        public void TransportPreparationFailureUsesReconnectBackoffAndBoundsRuntimeDiagnostic()
        {
            var transport = new PreparationTransport("ok")
            {
                PreparationFailure = new EndOfStreamException(new string('x', 4096))
            };
            using var runtime = Runtime(
                () => transport,
                reconnectIntervalMs: 100);
            runtime.Start(enabled: true, autoConnect: true);
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    "/phase184/closed_sidecar",
                    "phase184_msgs/msg/ClosedSidecar",
                    FoxRunResolvedQos.Default,
                    out _));

            string runtimeDiagnostic = null;
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    var value = runtime.GetStatsSnapshot().LastError;
                    if (string.IsNullOrEmpty(value))
                        return false;
                    runtimeDiagnostic = value;
                    return true;
                },
                TimeSpan.FromSeconds(3)));
            Assert.True(SpinWait.SpinUntil(
                () => transport.RequestCount >= 2,
                TimeSpan.FromSeconds(3)));
            var requestTimes = transport.RequestTimesSnapshot;

            Assert.True(
                requestTimes[1] - requestTimes[0] >= 75,
                "Preparation retries bypassed the configured reconnect backoff.");
            Assert.InRange(
                runtimeDiagnostic.Length,
                1,
                Ros2BridgeRuntime.MaxRuntimeDiagnosticChars);
        }

        [Fact]
        public void PreparationImplementationTypesStayInternalButTransportCapabilityIsPublic()
        {
            Assert.False(typeof(Ros2BridgePublisherReadiness).IsPublic);
            Assert.False(typeof(Ros2BridgePublisherPreparationRequest).IsPublic);
            Assert.False(typeof(Ros2BridgePublisherPreparationCodec).IsPublic);
            Assert.True(typeof(IRos2BridgePublisherPreparationTransport).IsPublic);
            Assert.True(typeof(Ros2BridgeRuntime)
                .GetMethod(
                    "PreparePublisher",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                ?.IsAssembly);
        }

        [Fact]
        public void FatalStopDuringDisposeStillDisposesOwnedSignalAndPreservesPrimary()
        {
            var sink = new FatalDisconnectSink();
            var runtime = Runtime(() => sink);
            var runtimeType = typeof(Ros2BridgeRuntime);
            var sinkField = runtimeType.GetField(
                "_sink",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            var signalField = runtimeType.GetField(
                "_signal",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(sinkField);
            Assert.NotNull(signalField);
            sinkField.SetValue(runtime, sink);
            var signal = Assert.IsType<AutoResetEvent>(signalField.GetValue(runtime));

            var thrown = Assert.Throws<OutOfMemoryException>(() => runtime.Dispose());

            Assert.Equal("stop-primary", thrown.Message);
            Assert.Equal(1, sink.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => signal.Set());
        }

        [Fact]
        public void DisposeIsIdempotentAfterAHealthyStop()
        {
            var runtime = Runtime(() => new TrackingLifecycleSink());

            runtime.Dispose();
            var secondDispose = Record.Exception(() => runtime.Dispose());
            var laterStop = Record.Exception(() => runtime.Stop());

            Assert.Null(secondDispose);
            Assert.Null(laterStop);
        }

        [Fact]
        public void FatalPreviousSinkCloseRollsBackConnectedReplacement()
        {
            var previous = new FatalDisconnectSink();
            var candidate = new TrackingLifecycleSink();
            var runtime = Runtime(() => candidate);
            var runtimeType = typeof(Ros2BridgeRuntime);
            SetPrivateField(runtimeType, runtime, "_enabled", true);
            SetPrivateField(runtimeType, runtime, "_workerGeneration", 1L);
            SetPrivateField(runtimeType, runtime, "_sink", previous);
            SetPrivateField(runtimeType, runtime, "_connected", false);
            var ensureConnected = runtimeType.GetMethod(
                "EnsureConnected",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(ensureConnected);

            var invocation = Assert.Throws<System.Reflection.TargetInvocationException>(
                () => ensureConnected.Invoke(runtime, new object[] { 1L }));
            var primary = Assert.IsType<OutOfMemoryException>(
                invocation.InnerException);

            Assert.Equal("stop-primary", primary.Message);
            Assert.Equal(1, previous.DisposeCount);
            Assert.Equal(1, candidate.ConnectCount);
            Assert.Equal(1, candidate.DisconnectCount);
            Assert.Equal(1, candidate.DisposeCount);
            Assert.False(runtime.GetStatsSnapshot().Connected);
            Assert.False(runtime.GetStatsSnapshot().Connecting);
            Assert.Null(runtimeType.GetField(
                    "_sink",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(runtime));
            runtime.Dispose();
        }

        [Fact]
        public void CodecRejectsCoercedJsonTypesAndBoundsSidecarDiagnostic()
        {
            var request = Ros2BridgePublisherPreparationCodec.WriteRequest(
                "strict-types",
                "/phase184/strict",
                "phase184_msgs/msg/Strict",
                FoxRunResolvedQos.Default);

            Assert.Throws<FormatException>(() =>
                Ros2BridgePublisherPreparationCodec.ParseRequest(
                    MutateHeader(request, header => header["protocolVersion"] = 1.0)));
            Assert.Throws<FormatException>(() =>
                Ros2BridgePublisherPreparationCodec.ParseRequest(
                    MutateHeader(request, header => header["qos"]["depth"] = true)));

            var response = Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                "strict-types",
                "error",
                "publisher_unavailable",
                new string('x', 4096));
            var parsed = Ros2BridgePublisherPreparationCodec.ParseResponse(
                response,
                "strict-types");
            Assert.Equal(
                Ros2BridgePublisherPreparationCodec.MaxDiagnosticChars,
                parsed.Message.Length);
            Assert.Throws<FormatException>(() =>
                Ros2BridgePublisherPreparationCodec.ParseResponse(
                    MutateHeader(response, header => header["status"] = true),
                    "strict-types"));
        }

        [Fact]
        [Trait("Phase", "186-A")]
        public void SharedV1AuthorityFixtureMatchesCurrentCSharpCodecs()
        {
            var fixture = JObject.Parse(File.ReadAllText(FindV1AuthorityFixture()));
            Assert.Equal(1, fixture.Value<int>("fixtureVersion"));

            var limits = Assert.IsType<JObject>(fixture["limits"]);
            Assert.Equal(16, limits.Value<int>("fixedHeaderBytes"));
            Assert.Equal(Ros2BridgeFrameWriter.MaxHeaderBytes, limits.Value<int>("maxJsonHeaderBytes"));
            Assert.Equal(Ros2BridgeFrameWriter.MaxPayloadBytes, limits.Value<int>("maxPayloadBytes"));
            Assert.Equal(1024, limits.Value<int>("defaultQueueCapacityFrames"));
            Assert.Equal(68719476736L, limits.Value<long>("maxQueuedPayloadBytes"));
            Assert.Equal(1, limits.Value<int>("activeConnectionCount"));
            Assert.Equal(4, limits.Value<int>("listenBacklog"));
            Assert.Equal(5000, limits.Value<int>("partialFrameStallMs"));

            var health = Assert.IsType<JObject>(fixture["health"]);
            var healthRequestId = health.Value<string>("requestId");
            var healthRequest = Ros2BridgeU2R2HealthCodec.WriteHealthPing(healthRequestId);
            AssertAuthorityFrame(Assert.IsType<JObject>(health["request"]), healthRequest);
            var healthResponse = Ros2BridgeU2R2HealthCodec.WriteHealthPongForTests(
                healthRequestId,
                sidecarName: health.Value<string>("sidecarName"),
                sidecarVersion: health.Value<string>("sidecarVersion"));
            AssertAuthorityFrame(Assert.IsType<JObject>(health["response"]), healthResponse);
            var pong = Ros2BridgeU2R2HealthCodec.ParseHealthPong(
                healthResponse,
                healthRequestId);
            Assert.Equal("ok", pong.Status);
            Assert.Equal(
                new[] { "disconnected", "request_sent", "healthy" },
                health["stateTransitions"]?.Values<string>().ToArray());

            var preparation = Assert.IsType<JObject>(fixture["preparePublisher"]);
            var qos = ReadAuthorityQos(Assert.IsType<JObject>(preparation["qos"]));
            var preparationRequest = Ros2BridgePublisherPreparationCodec.WriteRequest(
                preparation.Value<string>("requestId"),
                preparation.Value<string>("topic"),
                preparation.Value<string>("schemaName"),
                qos);
            AssertAuthorityFrame(
                Assert.IsType<JObject>(preparation["request"]),
                preparationRequest);
            var parsedRequest =
                Ros2BridgePublisherPreparationCodec.ParseRequest(preparationRequest);
            Assert.Equal(preparation.Value<string>("requestId"), parsedRequest.RequestId);
            Assert.Equal(preparation.Value<string>("topic"), parsedRequest.Topic);
            Assert.Equal(preparation.Value<string>("schemaName"), parsedRequest.SchemaName);
            Assert.Equal(qos, parsedRequest.Qos);

            var preparationResponse =
                Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                    preparation.Value<string>("requestId"),
                    "ok");
            AssertAuthorityFrame(
                Assert.IsType<JObject>(preparation["response"]),
                preparationResponse);
            var parsedResponse = Ros2BridgePublisherPreparationCodec.ParseResponse(
                preparationResponse,
                preparation.Value<string>("requestId"));
            Assert.Equal("ok", parsedResponse.Status);
            Assert.Equal(
                new[] { "unknown", "pending", "ready" },
                preparation["stateTransitions"]?.Values<string>().ToArray());

            var publish = Assert.IsType<JObject>(fixture["publish"]);
            var payload = HexToBytes(publish.Value<string>("payloadHex"));
            var publishFrame = Ros2BridgeFrame.CreateValidated(
                publish.Value<string>("topic"),
                publish.Value<string>("schemaName"),
                publish.Value<string>("encoding"),
                publish.Value<ulong>("logTimeNs"),
                publish.Value<ulong>("sequence"),
                payload,
                ReadAuthorityQos(Assert.IsType<JObject>(publish["qos"])));
            AssertAuthorityFrame(
                Assert.IsType<JObject>(publish["frame"]),
                Ros2BridgeFrameWriter.Write(publishFrame));
            Assert.Equal(
                new[] { "prepared", "queued", "sent" },
                publish["stateTransitions"]?.Values<string>().ToArray());

            var expectedNegativeIds = new[]
            {
                "bad_magic",
                "bad_version",
                "bad_flags",
                "oversized_header",
                "oversized_payload",
                "truncated_fixed",
                "truncated_header",
                "truncated_payload",
                "partial_payload_stall",
                "duplicate_operation",
                "unknown_operation",
                "illegal_sequence",
                "invalid_utf8",
                "trailing_json_root",
                "invalid_topic",
                "invalid_type",
                "invalid_delivery_policy",
                "correlation_mismatch",
                "peer_close"
            };
            var negativeVectors = Assert.IsType<JArray>(fixture["negativeVectors"]);
            Assert.Equal(
                expectedNegativeIds,
                negativeVectors
                    .Values<JObject>()
                    .Select(vector => vector.Value<string>("id"))
                    .ToArray());
            Assert.All(
                negativeVectors.Values<JObject>(),
                vector => Assert.Equal("reject", vector.Value<string>("expected")));
        }

        [Fact]
        public void RealTcpTransportPreparesAndPublishesOnOnePersistentSocket()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(2);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverDone = new ManualResetEventSlim(false);
            Exception serverFailure = null;
            byte[] preparationFrame = null;
            byte[] publishFrame = null;
            var secondConnectionPending = false;
            var server = new Thread(() =>
            {
                try
                {
                    using var accepted = listener.AcceptTcpClient();
                    accepted.ReceiveTimeout = 3000;
                    accepted.SendTimeout = 3000;
                    using var stream = accepted.GetStream();

                    preparationFrame = ReadWireFrame(stream);
                    var request =
                        Ros2BridgePublisherPreparationCodec.ParseRequest(preparationFrame);
                    var response =
                        Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                            request.RequestId,
                            "ok");
                    stream.Write(response, 0, response.Length);
                    stream.Flush();

                    // This second frame must arrive through the same accepted
                    // NetworkStream. A reconnect would leave this read at EOF
                    // or timeout and queue a second connection on the listener.
                    publishFrame = ReadWireFrame(stream);
                    Thread.Sleep(100);
                    secondConnectionPending = listener.Pending();
                }
                catch (Exception exception)
                {
                    serverFailure = exception;
                }
                finally
                {
                    serverDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase184 Bridge persistent-socket probe"
            };
            server.Start();

            using var runtime = new Ros2BridgeRuntime(
                "127.0.0.1",
                port,
                queueCapacity: 8,
                reconnectIntervalMs: 100,
                sendTimeoutMs: 1000,
                sinkFactory: () => new Ros2BridgeTcpClient());
            runtime.Start(enabled: true, autoConnect: true);

            const string topic = "/phase184/persistent_socket";
            const string schema = "phase184_msgs/msg/PersistentSocket";
            Assert.Equal(
                Ros2BridgePublisherReadiness.Pending,
                runtime.PreparePublisher(
                    topic,
                    schema,
                    FoxRunResolvedQos.SensorData,
                    out _));
            Assert.True(SpinWait.SpinUntil(
                () => runtime.PreparePublisher(
                          topic,
                          schema,
                          FoxRunResolvedQos.SensorData,
                          out _)
                      == Ros2BridgePublisherReadiness.Ready,
                TimeSpan.FromSeconds(3)));

            var frame = Frame(
                topic,
                schema,
                FoxRunResolvedQos.SensorData,
                sequence: 7UL);
            Assert.True(runtime.TryEnqueuePrepared(frame, out var reason), reason);
            Assert.True(serverDone.Wait(TimeSpan.FromSeconds(4)));
            server.Join(TimeSpan.FromSeconds(1));

            Assert.Null(serverFailure);
            Assert.False(secondConnectionPending);
            Assert.NotNull(preparationFrame);
            Assert.NotNull(publishFrame);
            Assert.Equal(1, runtime.GetStatsSnapshot().SentFrames);

            var headerLength = checked((int)ReadUInt32LE(publishFrame, 8));
            var payloadLength = checked((int)ReadUInt32LE(publishFrame, 12));
            var header = JObject.Parse(
                Encoding.UTF8.GetString(publishFrame, 16, headerLength));
            Assert.Equal("publish", (string)header["op"]);
            Assert.Equal(topic, (string)header["topic"]);
            Assert.Equal(schema, (string)header["schemaName"]);
            Assert.Equal("cdr", (string)header["encoding"]);
            Assert.Equal(7UL, (ulong)header["sequence"]);
            Assert.Equal(
                frame.PayloadMemory.ToArray(),
                publishFrame
                    .Skip(16 + headerLength)
                    .Take(payloadLength)
                    .ToArray());
        }

        private static Ros2BridgeRuntime Runtime(
            Func<IRos2BridgeSink> factory,
            int queueCapacity = 8,
            int reconnectIntervalMs = 10)
            => new Ros2BridgeRuntime(
                "127.0.0.1",
                19484,
                queueCapacity,
                reconnectIntervalMs,
                sendTimeoutMs: 250,
                factory);

        private sealed class PreparationTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private readonly string _status;
            private readonly string _errorCode;
            private readonly string _message;

            public PreparationTransport(
                string status,
                string errorCode = "",
                string message = "")
            {
                _status = status;
                _errorCode = errorCode;
                _message = message;
            }

            public List<byte[]> Requests { get; } = new List<byte[]>();
            public List<long> RequestTimes { get; } = new List<long>();
            public List<Ros2BridgeFrame> SentFrames { get; } = new List<Ros2BridgeFrame>();
            public List<string> Events { get; } = new List<string>();
            public ManualResetEventSlim ResponseGate { get; set; }
            public ManualResetEventSlim SendGate { get; set; }
            public ManualResetEventSlim SendStarted { get; } = new ManualResetEventSlim(false);
            public bool FailNextPublish { get; set; }
            public Exception PreparationFailure { get; set; }
            public Func<byte[], byte[]> MutateResponse { get; set; }
            public bool IsConnected { get; private set; }
            public int ConnectCount { get; private set; }
            public int RequestCount
            {
                get
                {
                    lock (Requests)
                        return Requests.Count;
                }
            }
            public int SentFrameCount
            {
                get
                {
                    lock (SentFrames)
                        return SentFrames.Count;
                }
            }
            public long[] RequestTimesSnapshot
            {
                get
                {
                    lock (Requests)
                        return RequestTimes.ToArray();
                }
            }
            public string[] EventSnapshot
            {
                get
                {
                    lock (Events)
                        return Events.ToArray();
                }
            }

            public void Connect(string host, int port, int timeoutMs)
            {
                ConnectCount++;
                IsConnected = true;
            }

            public byte[] ExchangePublisherPreparation(byte[] request, int timeoutMs)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                    RequestTimes.Add(Environment.TickCount64);
                }
                lock (Events)
                    Events.Add("prepare");
                if (PreparationFailure != null)
                    throw PreparationFailure;
                ResponseGate?.Wait(timeoutMs);
                var parsed = Ros2BridgePublisherPreparationCodec.ParseRequest(request);
                var response = Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                    parsed.RequestId,
                    _status,
                    _errorCode,
                    _message);
                return MutateResponse == null ? response : MutateResponse(response);
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
            {
                SendStarted.Set();
                SendGate?.Wait(timeoutMs);
                if (FailNextPublish)
                {
                    FailNextPublish = false;
                    throw new InvalidOperationException("forced reconnect");
                }
                lock (SentFrames)
                    SentFrames.Add(frame);
                lock (Events)
                    Events.Add("send");
            }

            public void Disconnect() => IsConnected = false;
            public void Dispose() => Disconnect();
        }

        private sealed class FatalDisconnectSink : IRos2BridgeSink
        {
            public bool IsConnected => true;
            public int DisposeCount { get; private set; }

            public void Connect(string host, int port, int timeoutMs) { }

            public void Send(Ros2BridgeFrame frame, int timeoutMs) { }

            public void Disconnect()
                => throw new OutOfMemoryException("stop-primary");

            public void Dispose()
                => DisposeCount++;
        }

        private sealed class TrackingLifecycleSink : IRos2BridgeSink
        {
            public bool IsConnected { get; private set; }
            public int ConnectCount { get; private set; }
            public int DisconnectCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void Connect(string host, int port, int timeoutMs)
            {
                ConnectCount++;
                IsConnected = true;
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs) { }

            public void Disconnect()
            {
                DisconnectCount++;
                IsConnected = false;
            }

            public void Dispose()
            {
                DisposeCount++;
                IsConnected = false;
            }
        }

        private static void SetPrivateField(
            Type runtimeType,
            object instance,
            string name,
            object value)
        {
            var field = runtimeType.GetField(
                name,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(instance, value);
        }

        private static byte[] MutateHeader(byte[] frame, Action<JObject> mutate)
        {
            var headerLength = (int)ReadUInt32LE(frame, 8);
            var header = JObject.Parse(Encoding.UTF8.GetString(frame, 16, headerLength));
            mutate(header);
            var headerBytes = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(header, Formatting.None));
            var result = new byte[16 + headerBytes.Length];
            Buffer.BlockCopy(frame, 0, result, 0, 16);
            WriteUInt32LE(result, 8, (uint)headerBytes.Length);
            Buffer.BlockCopy(headerBytes, 0, result, 16, headerBytes.Length);
            return result;
        }

        private static string FindV1AuthorityFixture()
        {
            const string relativePath =
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json";
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Packages"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Tools")))
                {
                    return Path.Combine(
                        directory.FullName,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the repository root for the shared U2R2 v1 authority fixture.");
        }

        private static void AssertAuthorityFrame(JObject vector, byte[] actual)
        {
            Assert.Equal(
                vector.Value<string>("frameHex"),
                BytesToHex(actual));
            var headerLength = checked((int)ReadUInt32LE(actual, 8));
            var headerJson = Encoding.UTF8.GetString(actual, 16, headerLength);
            Assert.Equal(vector.Value<string>("headerJson"), headerJson);
            Assert.True(
                JToken.DeepEquals(
                    Assert.IsType<JObject>(vector["header"]),
                    JObject.Parse(headerJson)));
            Assert.Equal(
                vector.Value<int>("payloadLength"),
                checked((int)ReadUInt32LE(actual, 12)));
        }

        private static FoxRunResolvedQos ReadAuthorityQos(JObject qos)
            => new(
                ParseAuthorityProfile(qos.Value<string>("profile")),
                ParseAuthorityReliability(qos.Value<string>("reliability")),
                ParseAuthorityDurability(qos.Value<string>("durability")),
                ParseAuthorityHistory(qos.Value<string>("history")),
                qos.Value<int>("depth"));

        private static FoxRunQosProfile ParseAuthorityProfile(string value)
            => value switch
            {
                "default" => FoxRunQosProfile.Default,
                "sensor_data" => FoxRunQosProfile.SensorData,
                "system_default" => FoxRunQosProfile.SystemDefault,
                _ => throw new FormatException("Unknown fixture QoS profile: " + value)
            };

        private static FoxRunQosReliability ParseAuthorityReliability(string value)
            => value switch
            {
                "system_default" => FoxRunQosReliability.SystemDefault,
                "reliable" => FoxRunQosReliability.Reliable,
                "best_effort" => FoxRunQosReliability.BestEffort,
                _ => throw new FormatException("Unknown fixture QoS reliability: " + value)
            };

        private static FoxRunQosDurability ParseAuthorityDurability(string value)
            => value switch
            {
                "system_default" => FoxRunQosDurability.SystemDefault,
                "volatile" => FoxRunQosDurability.Volatile,
                "transient_local" => FoxRunQosDurability.TransientLocal,
                _ => throw new FormatException("Unknown fixture QoS durability: " + value)
            };

        private static FoxRunQosHistory ParseAuthorityHistory(string value)
            => value switch
            {
                "system_default" => FoxRunQosHistory.SystemDefault,
                "keep_last" => FoxRunQosHistory.KeepLast,
                "keep_all" => FoxRunQosHistory.KeepAll,
                _ => throw new FormatException("Unknown fixture QoS history: " + value)
            };

        private static byte[] HexToBytes(string hex)
        {
            if (hex == null || (hex.Length & 1) != 0)
                throw new FormatException("Fixture hex must contain complete bytes.");
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static string BytesToHex(byte[] bytes)
            => string.Concat(bytes.Select(value => value.ToString("x2")));

        private static uint ReadUInt32LE(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));

        private static byte[] ReadWireFrame(Stream stream)
        {
            var fixedHeader = ReadExact(stream, 16);
            if (fixedHeader[0] != 'U'
                || fixedHeader[1] != '2'
                || fixedHeader[2] != 'R'
                || fixedHeader[3] != '2')
            {
                throw new FormatException("U2R2 magic is invalid.");
            }

            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            if (headerLength == 0
                || headerLength > Ros2BridgeFrameWriter.MaxHeaderBytes
                || payloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes)
            {
                throw new FormatException("U2R2 frame length is invalid.");
            }

            var bodyLength = checked((int)(headerLength + payloadLength));
            var frame = new byte[checked(16 + bodyLength)];
            Buffer.BlockCopy(fixedHeader, 0, frame, 0, fixedHeader.Length);
            var body = ReadExact(stream, bodyLength);
            Buffer.BlockCopy(body, 0, frame, 16, body.Length);
            return frame;
        }

        private static byte[] ReadExact(Stream stream, int length)
        {
            var data = new byte[length];
            var offset = 0;
            while (offset < data.Length)
            {
                var read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("U2R2 stream closed before the frame completed.");
                offset += read;
            }

            return data;
        }

        private static void WriteUInt32LE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xff);
            data[offset + 1] = (byte)((value >> 8) & 0xff);
            data[offset + 2] = (byte)((value >> 16) & 0xff);
            data[offset + 3] = (byte)((value >> 24) & 0xff);
        }

        private sealed class LegacyTransport : IRos2BridgeSink
        {
            public List<Ros2BridgeFrame> SentFrames { get; } = new List<Ros2BridgeFrame>();
            public bool IsConnected { get; private set; }
            public void Connect(string host, int port, int timeoutMs) => IsConnected = true;
            public void Send(Ros2BridgeFrame frame, int timeoutMs) => SentFrames.Add(frame);
            public void Disconnect() => IsConnected = false;
            public void Dispose() => Disconnect();
        }

        private static Ros2BridgeFrame Frame(
            string topic,
            string schema,
            FoxRunResolvedQos qos,
            ulong sequence)
            => Ros2BridgeFrame.CreateValidated(
                topic,
                schema,
                "cdr",
                184UL + sequence,
                sequence,
                new byte[] { 0, 1, 0, 0, (byte)sequence },
                qos);
    }
}
