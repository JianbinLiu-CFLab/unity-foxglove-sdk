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
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    


public sealed partial class Ros2BridgePublisherPreparationTests
    {
        [Fact]
        public void OversizedTopicIsRejectedBeforeItConsumesRegistryCapacity()
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
            Assert.Contains(
                "topic is invalid",
                rejectedReason,
                StringComparison.OrdinalIgnoreCase);

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
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.IsConnected,
                TimeSpan.FromSeconds(1)));

            var thrown = Assert.Throws<OutOfMemoryException>(() => runtime.Dispose());

            Assert.Equal("stop-primary", thrown.Message);
            Assert.Equal(1, sink.DisposeCount);
            Assert.Equal(
                Ros2BridgeRuntimeLifecycleState.Stopped,
                runtime.LifecycleState);
            Assert.Throws<ObjectDisposedException>(
                () => runtime.Start(enabled: true, autoConnect: true));
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
        public void ReconnectReusesOneDetachedSinkAndInvokesFactoryOncePerRun()
        {
            var transport = new PreparationTransport("ok")
            {
                FailNextPublish = true
            };
            var factoryCalls = 0;
            using var runtime = Runtime(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                return transport;
            });
            runtime.Start(enabled: true, autoConnect: true);
            Assert.True(SpinWait.SpinUntil(
                () => runtime.IsConnected,
                TimeSpan.FromSeconds(1)));
            Assert.True(runtime.TryEnqueue(
                Frame(
                    "/phase184/reusable",
                    "phase184_msgs/msg/Reusable",
                    FoxRunResolvedQos.Default,
                    sequence: 1),
                out var reason), reason);
            Assert.True(SpinWait.SpinUntil(
                () => transport.ConnectCount >= 2,
                TimeSpan.FromSeconds(3)));

            Assert.Equal(1, Volatile.Read(ref factoryCalls));
            runtime.Stop();
            Assert.Equal(1, transport.DisposeCount);
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

            var surrogateBoundary = Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                "strict-types",
                "error",
                "publisher_unavailable",
                new string('x', Ros2BridgePublisherPreparationCodec.MaxDiagnosticChars - 1)
                + "\ud83d\ude00");
            var boundedSurrogate = Ros2BridgePublisherPreparationCodec.ParseResponse(
                surrogateBoundary,
                "strict-types").Message;
            Assert.True(boundedSurrogate.Length <= Ros2BridgePublisherPreparationCodec.MaxDiagnosticChars);
            Assert.False(char.IsSurrogate(boundedSurrogate[boundedSurrogate.Length - 1]));

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

            var execution = Assert.IsType<JObject>(
                fixture["v2"]?["legacyV1NegativeExecution"]);
            Assert.Equal(1, execution.Value<int>("schemaVersion"));
            Assert.Equal("negativeVectors", execution.Value<string>("catalog"));
            var executableVectors = Assert.IsType<JArray>(execution["vectors"])
                .Values<JObject>()
                .ToArray();
            Assert.Equal(
                expectedNegativeIds,
                executableVectors
                    .Select(vector => vector.Value<string>("id"))
                    .ToArray());
            Assert.All(
                executableVectors,
                vector =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(
                        vector.Value<string>("action")));
                    Assert.False(string.IsNullOrWhiteSpace(
                        vector.Value<string>("expectedFailure")));
                    Assert.NotEmpty(
                        Assert.IsType<JArray>(vector["consumers"])
                            .Values<string>());
                });
            foreach (var vector in executableVectors.Where(
                         vector => Assert.IsType<JArray>(vector["consumers"])
                             .Values<string>()
                             .Contains("csharp", StringComparer.Ordinal)))
            {
                AssertExecutableV1NegativeRejected(fixture, vector);
            }
        }

    }
}
