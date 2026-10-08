// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Strict RFC 6455 frame validation and restart-generation ownership.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Transport
{


public sealed partial class ManagedWebSocketStrictnessTests
    {
        [Fact]
        public void StopSendsGoingAwayCloseBeforeDisposingEstablishedClient()
        {
            using var backend = new ManagedWsBackend();
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            using var client = ConnectAndWriteHandshake(port);

            try
            {
                var stream = client.GetStream();
                Assert.StartsWith("HTTP/1.1 101", ReadHttpHeaders(stream));
                Assert.True(SpinWait.SpinUntil(
                    () => backend.GetStatsSnapshot().ActiveClientCount == 1,
                    TimeSpan.FromSeconds(2)));

                backend.Stop();

                var close = ReadServerFrame(stream);
                Assert.Equal(WsOpcode.Close, close.Opcode);
                Assert.Equal(new byte[] { 0x03, 0xE9 }, close.Payload);
                Assert.False(backend.IsRunning);
            }
            finally
            {
                if (backend.IsRunning)
                    backend.Stop();
            }
        }

        [Fact]
        public void StopWaitsForPeerCloseAndSuppressesApplicationFrames()
        {
            using var backend = new ManagedWsBackend();
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            using var client = ConnectAndWriteHandshake(port);
            var textReceived = 0;
            backend.OnTextReceived += (_, _) => Interlocked.Increment(ref textReceived);

            try
            {
                var stream = client.GetStream();
                Assert.StartsWith("HTTP/1.1 101", ReadHttpHeaders(stream));
                Assert.True(SpinWait.SpinUntil(
                    () => backend.GetStatsSnapshot().ActiveClientCount == 1,
                    TimeSpan.FromSeconds(2)));

                stream.ReadTimeout = 5000;
                var stopTask = Task.Run(() => backend.Stop());
                var close = ReadServerFrame(stream);
                Assert.Equal(WsOpcode.Close, close.Opcode);
                Assert.Equal(new byte[] { 0x03, 0xE9 }, close.Payload);

                WriteClientFrame(stream, (byte)WsOpcode.Text, Encoding.UTF8.GetBytes("{}"));
                Assert.False(SpinWait.SpinUntil(
                    () => Volatile.Read(ref textReceived) != 0,
                    TimeSpan.FromMilliseconds(200)));
                Assert.False(stopTask.Wait(TimeSpan.FromMilliseconds(100)));

                WriteClientFrame(stream, (byte)WsOpcode.Close, new byte[] { 0x03, 0xE9 });
                Assert.True(stopTask.Wait(TimeSpan.FromSeconds(3)));
                Assert.False(backend.IsRunning);
            }
            finally
            {
                if (backend.IsRunning)
                    backend.Stop();
            }
        }

        [Fact]
        public void MalformedRequiredHandshakeHeadersReturnConsistentBadRequest()
        {
            var requests = new[]
            {
                BuildHandshakeRequest(connection: null),
                BuildHandshakeRequest(connection: "NotUpgrade"),
                BuildHandshakeRequest(upgrade: null),
                BuildHandshakeRequest(upgrade: "http"),
                BuildHandshakeRequest(includeKey: false)
            };

            foreach (var request in requests)
            {
                var stream = new DuplexBufferStream(Encoding.ASCII.GetBytes(request));
                var handler = new WsHandshakeHandler(
                    new ManagedWebSocketOptions(),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new object(),
                    null);

                var result = handler.Handshake(stream);

                Assert.False(result.accepted);
                Assert.StartsWith(
                    "HTTP/1.1 400 Bad Request",
                    Encoding.ASCII.GetString(stream.WrittenBytes));
            }
        }

        [Fact]
        public void NetworkHandshakeRejectsConnectionSubstringThatOnlyContainsUpgrade()
        {
            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions { MaxClients = 2 });
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            try
            {
                using var client = new TcpClient();
                client.Connect("127.0.0.1", port);
                var stream = client.GetStream();
                stream.ReadTimeout = 2000;
                stream.WriteTimeout = 2000;
                var request = Encoding.ASCII.GetBytes(BuildHandshakeRequest(connection: "NotUpgrade"));
                stream.Write(request, 0, request.Length);

                var response = ReadHttpHeaders(stream);
                Assert.StartsWith("HTTP/1.1 400 Bad Request", response);
                Assert.True(
                    SpinWait.SpinUntil(() => backend.GetStatsSnapshot().ActiveClientCount == 0,
                        TimeSpan.FromSeconds(2)));
            }
            finally
            {
                backend.Stop();
            }
        }

        [Fact]
        public async Task HandshakeHasAnAbsoluteDeadlineAcrossBlockingReads()
        {
            using var backend = new BlockingHandshakeBackend();
            using var client = new TcpClient();
            var stopwatch = Stopwatch.StartNew();
            var task = Task.Run(() => InvokeHandleClient(backend, client, CancellationToken.None));

            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(6500))) == task;
            backend.HandshakeStream.Release();
            if (!completed)
                await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(1)));

            Assert.True(
                completed,
                $"A handshake that never produces a byte must be closed by the absolute deadline (elapsed={stopwatch.ElapsedMilliseconds}ms).");
            Assert.True(backend.HandshakeStream.IsDisposed);
        }

        [Fact]
        public void EstablishedConnectionClosesWhenFrameProgressStalls()
        {
            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions
            {
                EstablishedIdleTimeoutMs = 150
            });
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            using var client = ConnectAndWriteHandshake(port);

            try
            {
                Assert.StartsWith("HTTP/1.1 101", ReadHttpHeaders(client.GetStream()));
                Assert.True(
                    SpinWait.SpinUntil(
                        () => backend.GetStatsSnapshot().ActiveClientCount == 1,
                        TimeSpan.FromSeconds(2)),
                    "The established client did not enter the active set.");

                var partialFrame = new byte[] { 0x82, 0x84, 1, 2, 3, 4, 0xAA };
                client.GetStream().Write(partialFrame, 0, partialFrame.Length);
                Assert.True(
                    SpinWait.SpinUntil(
                        () => backend.GetStatsSnapshot().ActiveClientCount == 0,
                        TimeSpan.FromSeconds(3)),
                    "A client that never completes a frame must be retired by the established deadline.");
            }
            finally
            {
                if (backend.IsRunning)
                    backend.Stop();
            }
        }

        [Fact]
        public void EstablishedIdleConnectionReceivesServerDataAndPongStaysAlive()
        {
            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions
            {
                EstablishedIdleTimeoutMs = 120
            });
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            using var client = ConnectAndWriteHandshake(port);

            try
            {
                var stream = client.GetStream();
                Assert.StartsWith("HTTP/1.1 101", ReadHttpHeaders(stream));
                Assert.True(SpinWait.SpinUntil(
                    () => backend.GetStatsSnapshot().ActiveClientCount == 1,
                    TimeSpan.FromSeconds(2)));

                backend.SendText(1, "{\"op\":\"serverData\"}");
                var dataReceived = false;
                while (!dataReceived)
                {
                    var frame = ReadServerFrame(stream);
                    if (frame.Opcode == WsOpcode.Ping)
                        WriteClientFrame(stream, WsOpcode.Pong, frame.Payload);
                    else
                    {
                        Assert.Equal(WsOpcode.Text, frame.Opcode);
                        dataReceived = true;
                    }
                }

                for (var i = 0; i < 2; i++)
                {
                    var ping = ReadServerFrame(stream);
                    Assert.Equal(WsOpcode.Ping, ping.Opcode);
                    WriteClientFrame(stream, WsOpcode.Pong, ping.Payload);
                }

                Assert.True(
                    SpinWait.SpinUntil(
                        () => backend.GetStatsSnapshot().ActiveClientCount == 1,
                        TimeSpan.FromMilliseconds(350)),
                    "A peer that responds to server pings must remain connected while idle.");
            }
            finally
            {
                if (backend.IsRunning)
                    backend.Stop();
            }
        }

        [Fact]
        public void EstablishedIdleConnectionWithoutPongIsRetired()
        {
            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions
            {
                EstablishedIdleTimeoutMs = 120
            });
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            using var client = ConnectAndWriteHandshake(port);

            try
            {
                var stream = client.GetStream();
                Assert.StartsWith("HTTP/1.1 101", ReadHttpHeaders(stream));
                Assert.True(SpinWait.SpinUntil(
                    () => backend.GetStatsSnapshot().ActiveClientCount == 1,
                    TimeSpan.FromSeconds(2)));

                var ping = ReadServerFrame(stream);
                Assert.Equal(WsOpcode.Ping, ping.Opcode);
                Assert.True(
                    SpinWait.SpinUntil(
                        () => backend.GetStatsSnapshot().ActiveClientCount == 0,
                        TimeSpan.FromSeconds(2)),
                    "A peer that does not answer server pings must be retired.");
            }
            finally
            {
                if (backend.IsRunning)
                    backend.Stop();
            }
        }

        [Fact]
        public void NullTextIsSentAsAnEmptyTextFrame()
        {
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(Array.Empty<byte>());
            using var connection = new WsConnection(tcpClient, stream, 8, 1024);
            connection.StartSendLoop(null, CancellationToken.None);

            var result = connection.SendText(null, FramePriority.Control);

            Assert.True(result.Accepted);
            Assert.True(
                SpinWait.SpinUntil(() => stream.WrittenBytes.Length >= 2, TimeSpan.FromSeconds(1)),
                "The empty text frame was not written before the test deadline.");
            Assert.Equal(new byte[] { 0x81, 0x00 }, stream.WrittenBytes);
        }

        [Fact]
        public void CodecClassifiesNonMinimalLengthsAndMalformedClosePayloads()
        {
            Assert.Equal(
                WsFrameReadResult.ProtocolError,
                ReadResult(BuildNonMinimalMaskedFrame(WsOpcode.Binary, 126, 1), out _));
            Assert.Equal(
                WsFrameReadResult.ProtocolError,
                ReadResult(BuildNonMinimalMaskedFrame(WsOpcode.Binary, 127, 126), out _));
            Assert.Equal(
                WsFrameReadResult.ProtocolError,
                ReadResult(BuildMaskedFrame(WsOpcode.Close, new byte[] { 0x03 }), out _));
            Assert.Equal(
                WsFrameReadResult.InvalidPayloadData,
                ReadResult(
                    BuildMaskedFrame(
                        WsOpcode.Close,
                        new byte[] { 0x03, 0xE8, 0xC3, 0x28 }),
                    out _));
        }

        [Fact]
        public void InvalidTextSendsInvalidPayloadCloseWithoutApplicationCallback()
        {
            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(
                BuildMaskedFrame(WsOpcode.Text, new byte[] { 0xC3, 0x28 }));
            var connection = new WsConnection(
                tcpClient,
                stream,
                ManagedWebSocketOptions.DefaultMaxQueuedFrames,
                ManagedWebSocketOptions.DefaultMaxQueuedBytes);
            var clients = Clients(backend);
            clients[1] = connection;
            var textCallbacks = 0;
            backend.OnTextReceived += (_, _) => textCallbacks++;
            connection.StartSendLoop(null, CancellationToken.None);

            InvokeReceiveLoop(backend, 1, connection);

            var written = stream.WrittenBytes;
            Assert.Equal(0, textCallbacks);
            Assert.True(written.Length >= 4);
            Assert.Equal((byte)(0x80 | WsOpcode.Close), written[0]);
            Assert.Equal(2, written[1] & 0x7F);
            Assert.Equal(1007, (written[2] << 8) | written[3]);
            Assert.Empty(clients);
        }

    }
}
