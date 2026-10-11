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

                    var helloFrame = ReadWireFrame(stream);
                    var hello = U2R2ProtocolCodec.ParseV2(
                        U2R2ProtocolCodec.DecodeFrame(helloFrame));
                    Assert.Equal(U2R2Operation.Hello, hello.Operation);
                    const string sessionId =
                        "5e7c4e90-b5b2-4db4-b27f-5a30e8086e1b";
                    const ulong connectionGeneration = 7;
                    var response = U2R2ProtocolCodec.EncodeFrame(
                        new JObject
                        {
                            ["op"] = "hello_ack",
                            ["protocolVersion"] = 2,
                            ["requestId"] = hello.RequestId,
                            ["status"] = "ok",
                            ["sessionId"] = sessionId,
                            ["connectionGeneration"] =
                                connectionGeneration,
                            ["capabilities"] = new JArray("publish"),
                        },
                        Array.Empty<byte>());
                    stream.Write(response, 0, response.Length);
                    stream.Flush();

                    preparationFrame = ReadWireFrame(stream);
                    var request = U2R2ProtocolCodec.ParseV2(
                        U2R2ProtocolCodec.DecodeFrame(preparationFrame));
                    Assert.Equal(
                        U2R2Operation.PreparePublisher,
                        request.Operation);
                    response = U2R2ProtocolCodec.EncodeFrame(
                        new JObject
                        {
                            ["op"] = "publisher_ready",
                            ["protocolVersion"] = 2,
                            ["requestId"] = request.RequestId,
                            ["status"] = "ok",
                            ["sessionId"] = sessionId,
                            ["connectionGeneration"] =
                                connectionGeneration,
                        },
                        Array.Empty<byte>());
                    stream.Write(response, 0, response.Length);
                    stream.Flush();

                    // The publish request must arrive through the same accepted
                    // NetworkStream. A reconnect would leave this read at EOF
                    // or timeout and queue a second connection on the listener.
                    publishFrame = ReadWireFrame(stream);
                    var publish = U2R2ProtocolCodec.ParseV2(
                        U2R2ProtocolCodec.DecodeFrame(publishFrame));
                    Assert.Equal(U2R2Operation.Publish, publish.Operation);
                    response = U2R2ProtocolCodec.EncodeFrame(
                        new JObject
                        {
                            ["op"] = "publish_result",
                            ["protocolVersion"] = 2,
                            ["requestId"] = publish.RequestId,
                            ["status"] = "ok",
                            ["sessionId"] = sessionId,
                            ["connectionGeneration"] =
                                connectionGeneration,
                            ["messageId"] = publish.MessageId,
                        },
                        Array.Empty<byte>());
                    stream.Write(response, 0, response.Length);
                    stream.Flush();
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

    }
}
