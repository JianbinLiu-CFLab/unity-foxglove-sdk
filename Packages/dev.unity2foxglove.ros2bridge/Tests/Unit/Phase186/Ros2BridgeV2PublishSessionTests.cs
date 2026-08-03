// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first coverage for the live Bridge v2 publish session.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeV2PublishSessionTests
    {
        [Fact]
        public void HelloFreezesSidecarIdentityCapabilitiesAndLocalLimits()
        {
            var limits = U2R2ProtocolLimits.Default;
            var hello = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                limits);
            var parsedHello = Parse(hello.WireBytes);

            Assert.Equal(U2R2Operation.Hello, parsedHello.Operation);
            Assert.Equal(1UL, parsedHello.RequestId);
            Assert.Equal(
                new[] { U2R2Capability.Publish },
                parsedHello.Capabilities);
            Assert.Empty(parsedHello.SessionId);
            Assert.Equal(0UL, parsedHello.ConnectionGeneration);

            var response = U2R2ProtocolCodec.EncodeFrame(
                new JObject
                {
                    ["op"] = "hello_ack",
                    ["protocolVersion"] = 2,
                    ["requestId"] = 1,
                    ["status"] = "ok",
                    ["sessionId"] =
                        "5e7c4e90-b5b2-4db4-b27f-5a30e8086e1b",
                    ["connectionGeneration"] = 7,
                    ["capabilities"] = new JArray("publish")
                },
                Array.Empty<byte>(),
                limits);
            var snapshot = Ros2BridgeV2SessionCodec.AcceptHello(
                hello,
                response,
                limits);

            Assert.Equal(U2R2Dialect.V2, snapshot.Dialect);
            Assert.Equal(
                "5e7c4e90-b5b2-4db4-b27f-5a30e8086e1b",
                snapshot.SessionId);
            Assert.Equal(7UL, snapshot.ConnectionGeneration);
            Assert.Equal(
                new[] { U2R2Capability.Publish },
                snapshot.Capabilities);
            Assert.Same(limits, snapshot.Limits);
            Assert.Throws<NotSupportedException>(
                () => ((System.Collections.IList)snapshot.Capabilities)
                    .Add(U2R2Capability.Subscribe));
        }

        [Fact]
        public void HelloRejectsUnofferedOrMissingRequiredCapabilities()
        {
            var limits = U2R2ProtocolLimits.Default;
            var publishOnly = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                limits);
            var unoffered = HelloAck(requestId: 1, generation: 7);
            unoffered["capabilities"] =
                new JArray("publish", "subscribe");
            var unofferedError = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.AcceptHello(
                    publishOnly,
                    U2R2ProtocolCodec.EncodeFrame(
                        unoffered,
                        Array.Empty<byte>(),
                        limits),
                    limits));
            Assert.Equal(
                "response_mismatch",
                unofferedError.ErrorCode);
            Assert.True(unofferedError.Terminal);

            var duplex = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: true,
                limits);
            var missingPublish = HelloAck(
                requestId: 1,
                generation: 7);
            missingPublish["capabilities"] = new JArray("subscribe");
            var missingError = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.AcceptHello(
                    duplex,
                    U2R2ProtocolCodec.EncodeFrame(
                        missingPublish,
                        Array.Empty<byte>(),
                        limits),
                    limits));
            Assert.Equal(
                "missing_capability",
                missingError.ErrorCode);
            Assert.True(missingError.Terminal);
        }

        [Fact]
        public void TcpClientAcceptsOnlyIpv4LoopbackAuthority()
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost("localhost");
            Ros2BridgeTcpClient.ValidateLoopbackHost("127.0.0.1");
            Ros2BridgeTcpClient.ValidateLoopbackHost("127.44.55.66");

            Assert.Throws<ArgumentException>(
                () => Ros2BridgeTcpClient.ValidateLoopbackHost("::1"));
            Assert.Throws<ArgumentException>(
                () => Ros2BridgeTcpClient.ValidateLoopbackHost("0.0.0.0"));
            Assert.Throws<ArgumentException>(
                () => Ros2BridgeTcpClient.ValidateLoopbackHost("192.168.1.10"));
        }

        [Fact]
        public void TcpExchangeUsesOneAbsoluteWallClockDeadline()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverDone = new ManualResetEventSlim(false);
            var exchangeDone = new ManualResetEventSlim(false);
            Exception serverError = null;
            Exception exchangeError = null;
            var server = new Thread(() =>
            {
                try
                {
                    using var accepted = listener.AcceptTcpClient();
                    using var stream = accepted.GetStream();
                    var hello = Parse(ReadWireFrame(stream));
                    var response = U2R2ProtocolCodec.EncodeFrame(
                        HelloAck(hello.RequestId, generation: 7),
                        Array.Empty<byte>());
                    foreach (var value in response)
                    {
                        stream.WriteByte(value);
                        stream.Flush();
                        Thread.Sleep(75);
                    }
                }
                catch (Exception exception)
                {
                    serverError = exception;
                }
                finally
                {
                    serverDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 v2 drip-response server",
            };
            server.Start();

            using var client = new Ros2BridgeTcpClient();
            client.Connect("127.0.0.1", port, timeoutMs: 1000);
            var request = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                U2R2ProtocolLimits.Default);
            var stopwatch = Stopwatch.StartNew();
            var exchange = new Thread(() =>
            {
                try
                {
                    ((IRos2BridgeV2SessionTransport)client).ExchangeV2(
                        request.WireBytes,
                        U2R2ProtocolLimits.Default,
                        timeoutMs: 100);
                }
                catch (Exception exception)
                {
                    exchangeError = exception;
                }
                finally
                {
                    exchangeDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 v2 absolute-deadline client",
            };
            exchange.Start();

            var completedWithinBound = exchangeDone.Wait(
                TimeSpan.FromMilliseconds(700));
            stopwatch.Stop();
            client.Disconnect();
            serverDone.Wait(TimeSpan.FromSeconds(2));
            exchange.Join(TimeSpan.FromSeconds(1));
            server.Join(TimeSpan.FromSeconds(1));

            Assert.True(
                completedWithinBound,
                "positive one-byte reads reset the response deadline");
            var timeout = Assert.IsType<U2R2ProtocolException>(
                exchangeError);
            Assert.Equal("timeout", timeout.ErrorCode);
            Assert.True(timeout.Terminal);
            Assert.True(stopwatch.ElapsedMilliseconds < 700);
            Assert.True(
                serverError == null
                || serverError is IOException
                || serverError is SocketException
                || serverError is ObjectDisposedException,
                serverError?.ToString());
        }

        [Fact]
        public void LegacyPreparationUsesOneAbsoluteWallClockDeadline()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverDone = new ManualResetEventSlim(false);
            var exchangeDone = new ManualResetEventSlim(false);
            Exception serverError = null;
            Exception exchangeError = null;
            var server = new Thread(() =>
            {
                try
                {
                    using var accepted = listener.AcceptTcpClient();
                    using var stream = accepted.GetStream();
                    ReadWireFrame(stream);
                    var response =
                        Ros2BridgePublisherPreparationCodec
                            .WriteResponseForTests(
                                "phase186-v1-drip",
                                "ok");
                    foreach (var value in response)
                    {
                        stream.WriteByte(value);
                        stream.Flush();
                        Thread.Sleep(75);
                    }
                }
                catch (Exception exception)
                {
                    serverError = exception;
                }
                finally
                {
                    serverDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 v1 drip-response server",
            };
            server.Start();

            using var client = new Ros2BridgeTcpClient();
            client.Connect("127.0.0.1", port, timeoutMs: 1000);
            var request =
                Ros2BridgePublisherPreparationCodec.WriteRequest(
                    "phase186-v1-drip",
                    "/phase186/v1/drip",
                    "phase186_msgs/msg/Drip",
                    FoxRunResolvedQos.Default);
            var stopwatch = Stopwatch.StartNew();
            var exchange = new Thread(() =>
            {
                try
                {
                    client.ExchangePublisherPreparation(
                        request,
                        timeoutMs: 100);
                }
                catch (Exception exception)
                {
                    exchangeError = exception;
                }
                finally
                {
                    exchangeDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 v1 absolute-deadline client",
            };
            exchange.Start();

            var completedWithinBound = exchangeDone.Wait(
                TimeSpan.FromMilliseconds(700));
            stopwatch.Stop();
            client.Disconnect();
            serverDone.Wait(TimeSpan.FromSeconds(2));
            exchange.Join(TimeSpan.FromSeconds(1));
            server.Join(TimeSpan.FromSeconds(1));

            Assert.True(
                completedWithinBound,
                "positive one-byte reads reset the legacy preparation deadline");
            var timeout = Assert.IsType<U2R2ProtocolException>(
                exchangeError);
            Assert.Equal("timeout", timeout.ErrorCode);
            Assert.True(timeout.Terminal);
            Assert.True(stopwatch.ElapsedMilliseconds < 700);
            Assert.True(
                serverError == null
                || serverError is IOException
                || serverError is SocketException
                || serverError is ObjectDisposedException,
                serverError?.ToString());
        }

        [Fact]
        public void TcpConnectionSerializesIoAndLatchesOneDialect()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(1);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var helloRead = new ManualResetEventSlim(false);
            var releaseHello = new ManualResetEventSlim(false);
            var serverDone = new ManualResetEventSlim(false);
            var exchangeDone = new ManualResetEventSlim(false);
            var sendDone = new ManualResetEventSlim(false);
            Exception serverError = null;
            Exception exchangeError = null;
            Exception sendError = null;
            var server = new Thread(() =>
            {
                try
                {
                    using var accepted = listener.AcceptTcpClient();
                    using var stream = accepted.GetStream();
                    var hello = Parse(ReadWireFrame(stream));
                    helloRead.Set();
                    if (!releaseHello.Wait(TimeSpan.FromSeconds(3)))
                        throw new TimeoutException("hello release was not signaled");
                    var response = U2R2ProtocolCodec.EncodeFrame(
                        HelloAck(hello.RequestId, generation: 7),
                        Array.Empty<byte>());
                    stream.Write(response, 0, response.Length);
                    stream.Flush();
                }
                catch (Exception exception)
                {
                    serverError = exception;
                }
                finally
                {
                    serverDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 v2 dialect-latch server",
            };
            server.Start();

            using var client = new Ros2BridgeTcpClient();
            client.Connect("127.0.0.1", port, timeoutMs: 1000);
            var helloRequest = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                U2R2ProtocolLimits.Default);
            var exchange = new Thread(() =>
            {
                try
                {
                    ((IRos2BridgeV2SessionTransport)client).ExchangeV2(
                        helloRequest.WireBytes,
                        U2R2ProtocolLimits.Default,
                        timeoutMs: 1000);
                }
                catch (Exception exception)
                {
                    exchangeError = exception;
                }
                finally
                {
                    exchangeDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 v2 dialect-latch exchange",
            };
            exchange.Start();
            Assert.True(helloRead.Wait(TimeSpan.FromSeconds(2)));

            var send = new Thread(() =>
            {
                try
                {
                    client.Send(
                        Ros2BridgeFrame.CreateValidated(
                            "/phase186/v2/dialect",
                            "phase186_msgs/msg/Dialect",
                            Ros2BridgeFrame.CdrEncoding,
                            logTimeNs: 1,
                            sequence: 1,
                            payload: new byte[] { 0, 1, 0, 0 }),
                        timeoutMs: 1000);
                }
                catch (Exception exception)
                {
                    sendError = exception;
                }
                finally
                {
                    sendDone.Set();
                }
            })
            {
                IsBackground = true,
                Name = "Phase186 forbidden v1 concurrent writer",
            };
            send.Start();

            var wasSerialized = !sendDone.Wait(
                TimeSpan.FromMilliseconds(200));
            releaseHello.Set();
            Assert.True(exchangeDone.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(sendDone.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(serverDone.Wait(TimeSpan.FromSeconds(2)));
            exchange.Join(TimeSpan.FromSeconds(1));
            send.Join(TimeSpan.FromSeconds(1));
            server.Join(TimeSpan.FromSeconds(1));

            Assert.True(
                wasSerialized,
                "a concurrent legacy writer entered the active v2 socket");
            Assert.Null(exchangeError);
            var dialect = Assert.IsType<U2R2ProtocolException>(sendError);
            Assert.Equal("dialect_downgrade", dialect.ErrorCode);
            Assert.True(dialect.Terminal);
            Assert.Null(serverError);
        }

        [Fact]
        public void OversizedResponseLengthsAreTerminalFramingFaults()
        {
            var limits = U2R2ProtocolLimits.Default;
            var fixedHeader = new byte[16];
            fixedHeader[0] = (byte)'U';
            fixedHeader[1] = (byte)'2';
            fixedHeader[2] = (byte)'R';
            fixedHeader[3] = (byte)'2';
            fixedHeader[4] = U2R2ProtocolCodec.EnvelopeVersion;
            var oversizedHeader = checked(
                (uint)(limits.MaxHeaderBytes + 1));
            fixedHeader[8] = (byte)(oversizedHeader & 0xff);
            fixedHeader[9] = (byte)((oversizedHeader >> 8) & 0xff);
            fixedHeader[10] = (byte)((oversizedHeader >> 16) & 0xff);
            fixedHeader[11] = (byte)((oversizedHeader >> 24) & 0xff);

            var read = typeof(Ros2BridgeTcpClient).GetMethod(
                "ReadV2Frame",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(read);
            using var stream = new MemoryStream(fixedHeader);
            var invocation = Assert.Throws<TargetInvocationException>(
                () => read.Invoke(null, new object[] { stream, limits }));
            var error = Assert.IsType<U2R2ProtocolException>(
                invocation.InnerException);

            Assert.Equal("invalid_frame", error.ErrorCode);
            Assert.True(error.Terminal);
            Assert.Equal(fixedHeader.Length, stream.Position);
        }

        [Fact]
        public void PreparationAndPublishCarryCurrentSessionCorrelation()
        {
            var snapshot = Snapshot();
            var qos = FoxRunResolvedQos.Default;
            var preparation =
                Ros2BridgeV2SessionCodec.CreatePublisherPreparation(
                    snapshot,
                    requestId: 2,
                    "/phase186/v2/state",
                    "phase186_msgs/msg/State",
                    qos);
            var parsedPreparation = Parse(preparation.WireBytes);

            Assert.Equal(
                U2R2Operation.PreparePublisher,
                parsedPreparation.Operation);
            Assert.Equal(2UL, parsedPreparation.RequestId);
            Assert.Equal(snapshot.SessionId, parsedPreparation.SessionId);
            Assert.Equal(
                snapshot.ConnectionGeneration,
                parsedPreparation.ConnectionGeneration);
            Assert.Equal("/phase186/v2/state", parsedPreparation.Topic);
            Assert.Equal(
                "phase186_msgs/msg/State",
                parsedPreparation.SchemaName);
            Assert.Equal("cdr", parsedPreparation.Encoding);

            var frame = Ros2BridgeFrame.CreateOwned(
                "/phase186/v2/state",
                "phase186_msgs/msg/State",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: 123,
                sequence: 4,
                payload: new byte[] { 0, 1, 2, 3 },
                qos);
            var measurement = Ros2BridgeV2SessionCodec.MeasurePublish(
                frame,
                snapshot,
                requestId: 3,
                messageId: 1);
            var publish = Ros2BridgeV2SessionCodec.EncodePublish(
                frame,
                snapshot,
                requestId: 3,
                messageId: 1,
                measurement);
            var parsedPublish = Parse(publish.WireBytes);

            Assert.Equal(U2R2Operation.Publish, parsedPublish.Operation);
            Assert.Equal(3UL, parsedPublish.RequestId);
            Assert.Equal(1UL, parsedPublish.MessageId);
            Assert.Equal(snapshot.SessionId, parsedPublish.SessionId);
            Assert.Equal(
                snapshot.ConnectionGeneration,
                parsedPublish.ConnectionGeneration);
            Assert.Equal(frame.Topic, parsedPublish.Topic);
            Assert.Equal(frame.SchemaName, parsedPublish.SchemaName);
            Assert.Equal(frame.PayloadMemory.ToArray(), publish.Payload);
            Assert.Equal(
                publish.WireBytes.Length,
                measurement.TotalWireBytes);
        }

        [Fact]
        public void WrongCorrelationGenerationAndBusyAreTerminal()
        {
            var limits = U2R2ProtocolLimits.Default;
            var hello = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                limits);
            var wrongRequest = U2R2ProtocolCodec.EncodeFrame(
                HelloAck(requestId: 2, generation: 7),
                Array.Empty<byte>(),
                limits);
            var wrongHello = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.AcceptHello(
                    hello,
                    wrongRequest,
                    limits));
            Assert.Equal("response_mismatch", wrongHello.ErrorCode);
            Assert.True(wrongHello.Terminal);

            var busy = U2R2ProtocolCodec.EncodeFrame(
                new JObject
                {
                    ["op"] = "busy",
                    ["protocolVersion"] = 2,
                    ["requestId"] = 1,
                    ["status"] = "error",
                    ["errorCode"] = "busy",
                    ["message"] = "data session already leased",
                    ["terminal"] = true
                },
                Array.Empty<byte>(),
                limits);
            var busyError = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.AcceptHello(
                    hello,
                    busy,
                    limits));
            Assert.Equal("busy", busyError.ErrorCode);
            Assert.True(busyError.Terminal);

            var snapshot = Snapshot();
            var request =
                Ros2BridgeV2SessionCodec.CreatePublisherPreparation(
                    snapshot,
                    requestId: 2,
                    "/phase186/v2/state",
                    "phase186_msgs/msg/State",
                    FoxRunResolvedQos.Default);
            var wrongGeneration = U2R2ProtocolCodec.EncodeFrame(
                new JObject
                {
                    ["op"] = "publisher_ready",
                    ["protocolVersion"] = 2,
                    ["requestId"] = 2,
                    ["status"] = "ok",
                    ["sessionId"] = snapshot.SessionId,
                    ["connectionGeneration"] = 8
                },
                Array.Empty<byte>(),
                limits);
            var generationError = Assert.Throws<U2R2ProtocolException>(
                () => Ros2BridgeV2SessionCodec.ValidateResponse(
                    request,
                    wrongGeneration,
                    snapshot));
            Assert.Equal(
                "response_mismatch",
                generationError.ErrorCode);
            Assert.True(generationError.Terminal);
        }

        [Fact]
        public void WriteLeaseOwnsBoundedJitWireReservation()
        {
            using var scheduler = new Ros2BridgeOutboundScheduler(
                U2R2ProtocolLimits.Default,
                sessionGeneration: 12);
            var frame = Ros2BridgeFrame.CreateOwned(
                "/phase186/v2/reservation",
                "phase186_msgs/msg/State",
                Ros2BridgeFrame.CdrEncoding,
                logTimeNs: 1,
                sequence: 1,
                payload: new byte[] { 1 });
            Assert.Equal(
                Ros2BridgeOutboundEnqueueDisposition.Accepted,
                scheduler.Enqueue(
                    frame,
                    U2R2QueueOverflowPolicy.Reject));
            Assert.True(scheduler.TryBeginWrite(out var write));
            using (write)
            {
                Assert.True(
                    write.TryReserveTransient(
                        4096,
                        out var transient));
                using (transient)
                    Assert.Equal(4096UL, scheduler.TransientBytes);
                Assert.Equal(0UL, scheduler.TransientBytes);
                write.Complete();
            }
        }

        [Fact]
        public void PublishOnlyWorkerDoesNotOwnInboundQueueOrDispatcher()
        {
            var fields = typeof(Ros2BridgeWorkerLease)
                .GetFields(
                    BindingFlags.Instance
                    | BindingFlags.NonPublic
                    | BindingFlags.Public);
            Assert.DoesNotContain(
                fields,
                field =>
                    field.Name.Contains(
                        "inbound",
                        StringComparison.OrdinalIgnoreCase)
                    || field.FieldType.Name.Contains(
                        "Inbound",
                        StringComparison.OrdinalIgnoreCase)
                    || field.FieldType.Name.Contains(
                        "Dispatcher",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static Ros2BridgeV2SessionSnapshot Snapshot()
        {
            var limits = U2R2ProtocolLimits.Default;
            var hello = Ros2BridgeV2SessionCodec.CreateHello(
                requestId: 1,
                requiresSubscription: false,
                limits);
            return Ros2BridgeV2SessionCodec.AcceptHello(
                hello,
                U2R2ProtocolCodec.EncodeFrame(
                    HelloAck(requestId: 1, generation: 7),
                    Array.Empty<byte>(),
                    limits),
                limits);
        }

        private static JObject HelloAck(
            ulong requestId,
            ulong generation)
            => new JObject
            {
                ["op"] = "hello_ack",
                ["protocolVersion"] = 2,
                ["requestId"] = requestId,
                ["status"] = "ok",
                ["sessionId"] =
                    "5e7c4e90-b5b2-4db4-b27f-5a30e8086e1b",
                ["connectionGeneration"] = generation,
                ["capabilities"] = new JArray("publish")
            };

        private static U2R2Message Parse(byte[] wireBytes)
            => U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(wireBytes));

        private static byte[] ReadWireFrame(Stream stream)
        {
            var fixedHeader = ReadExact(stream, 16);
            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            var body = ReadExact(
                stream,
                checked((int)(headerLength + payloadLength)));
            var frame = new byte[checked(16 + body.Length)];
            Buffer.BlockCopy(fixedHeader, 0, frame, 0, fixedHeader.Length);
            Buffer.BlockCopy(body, 0, frame, 16, body.Length);
            return frame;
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }

        private static ulong ReadUInt32LE(byte[] buffer, int offset)
            => (ulong)buffer[offset]
               | ((ulong)buffer[offset + 1] << 8)
               | ((ulong)buffer[offset + 2] << 16)
               | ((ulong)buffer[offset + 3] << 24);
    }
}
