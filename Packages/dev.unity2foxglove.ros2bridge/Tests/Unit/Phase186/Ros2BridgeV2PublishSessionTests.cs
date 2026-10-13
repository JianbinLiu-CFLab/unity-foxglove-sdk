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
    public sealed partial class Ros2BridgeV2PublishSessionTests
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
            client.Connect("127.0.0.1", port, timeoutMs: 5000);
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
            client.Connect("127.0.0.1", port, timeoutMs: 5000);
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
            client.Connect("127.0.0.1", port, timeoutMs: 5000);
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

    }
}
