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
            public int DisposeCount { get; private set; }
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
            public void Dispose()
            {
                DisposeCount++;
                Disconnect();
            }
        }

        private sealed class ReconnectSequenceTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private readonly IRos2BridgeSink[] _transports;
            private int _index;

            internal ReconnectSequenceTransport(params IRos2BridgeSink[] transports)
            {
                _transports = transports;
            }

            private IRos2BridgeSink Current => _transports[_index];

            public bool IsConnected => Current.IsConnected;

            public void Connect(string host, int port, int timeoutMs)
                => Current.Connect(host, port, timeoutMs);

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
                => Current.Send(frame, timeoutMs);

            public byte[] ExchangePublisherPreparation(
                byte[] request,
                int timeoutMs)
                => ((IRos2BridgePublisherPreparationTransport)Current)
                    .ExchangePublisherPreparation(request, timeoutMs);

            public void Disconnect()
            {
                Current.Disconnect();
                if (_index + 1 < _transports.Length)
                    _index++;
            }

            public void Dispose()
            {
                for (var i = 0; i < _transports.Length; i++)
                    _transports[i].Dispose();
            }
        }

    }
}
