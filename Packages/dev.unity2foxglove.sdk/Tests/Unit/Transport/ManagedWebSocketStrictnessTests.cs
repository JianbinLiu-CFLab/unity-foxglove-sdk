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
    [Trait("Phase", "187")]
    [Trait("Domain", "Transport")]
    public sealed class ManagedWebSocketStrictnessTests
    {
        [Fact]
        public void PendingHandshakesAreBoundedAndClosedOnStop()
        {
            var options = new ManagedWebSocketOptions { MaxClients = 1 };
            using var backend = new ManagedWsBackend(options);
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);

            using var pending = new TcpClient();
            pending.Connect("127.0.0.1", port);
            Assert.True(
                SpinWait.SpinUntil(() => PendingClientCount(backend) == 1, TimeSpan.FromSeconds(2)),
                "A connected client that has not completed the handshake must consume a bounded pending slot.");

            using var rejected = new TcpClient();
            rejected.Connect("127.0.0.1", port);
            Assert.True(
                SpinWait.SpinUntil(() => backend.GetStatsSnapshot().TotalRejectedClients >= 1, TimeSpan.FromSeconds(2)),
                "A second pending client must be rejected while the single admission slot is occupied.");

            backend.Stop();
            Assert.True(
                SpinWait.SpinUntil(() => PendingClientCount(backend) == 0, TimeSpan.FromSeconds(2)),
                "Stop must retire every pending handshake instead of leaving an owned socket behind.");
            Assert.False(backend.IsRunning);
        }

        [Fact]
        public void EstablishedClientsConsumeOneCapacitySlot()
        {
            var options = new ManagedWebSocketOptions { MaxClients = 4 };
            using var backend = new ManagedWsBackend(options);
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            var clients = new List<TcpClient>();

            try
            {
                var responses = new List<string>();
                for (var index = 0; index < options.MaxClients; index++)
                {
                    var client = new TcpClient();
                    client.Connect("127.0.0.1", port);
                    clients.Add(client);
                    var stream = client.GetStream();
                    stream.ReadTimeout = 2000;
                    stream.WriteTimeout = 2000;
                    var request =
                        "GET / HTTP/1.1\r\n" +
                        "Host: 127.0.0.1\r\n" +
                        "Connection: Upgrade\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Sec-WebSocket-Version: 13\r\n" +
                        "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
                        "Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n\r\n";
                    var bytes = Encoding.ASCII.GetBytes(request);
                    stream.Write(bytes, 0, bytes.Length);
                    responses.Add(ReadHttpHeaders(stream));
                    var expectedActive = index + 1;
                    Assert.True(
                        SpinWait.SpinUntil(
                            () =>
                            {
                                var stats = backend.GetStatsSnapshot();
                                return stats.ActiveClientCount == expectedActive
                                    && stats.PendingClientCount == 0;
                            },
                            TimeSpan.FromSeconds(2)),
                        $"Client {expectedActive} did not transfer its reservation to the active set.");
                }

                Assert.All(responses, response => Assert.StartsWith("HTTP/1.1 101", response));
                Assert.True(
                    SpinWait.SpinUntil(
                        () => backend.GetStatsSnapshot().ActiveClientCount == options.MaxClients,
                        TimeSpan.FromSeconds(2)),
                    "Every configured client slot must remain available to an established connection.");
                Assert.Equal(0, backend.GetStatsSnapshot().PendingClientCount);
            }
            finally
            {
                backend.Stop();
                foreach (var client in clients)
                    client.Dispose();
            }
        }

        [Fact]
        public void SlowCapacityRejectionDoesNotStopAcceptLoop()
        {
            var options = new ManagedWebSocketOptions { MaxClients = 1 };
            using var backend = new BlockingRejectBackend(options);
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            var clients = new List<TcpClient>();

            try
            {
                var first = ConnectAndWriteHandshake(port);
                clients.Add(first);
                Assert.StartsWith("HTTP/1.1 101", ReadHttpHeaders(first.GetStream()));
                Assert.True(
                    SpinWait.SpinUntil(
                        () => backend.GetStatsSnapshot().ActiveClientCount == 1,
                        TimeSpan.FromSeconds(2)));

                var second = ConnectAndWriteHandshake(port);
                clients.Add(second);
                Assert.True(
                    backend.RejectionEntered.Wait(TimeSpan.FromSeconds(2)),
                    "The first capacity rejection did not reach the response worker.");

                var third = ConnectAndWriteHandshake(port);
                clients.Add(third);
                Assert.True(
                    SpinWait.SpinUntil(
                        () => backend.GetStatsSnapshot().TotalRejectedClients >= 2,
                        TimeSpan.FromSeconds(2)),
                    $"A blocked rejection response must not stall subsequent accepts (stats={backend.GetStatsSnapshot().TotalRejectedClients}, active={backend.GetStatsSnapshot().ActiveClientCount}, pending={backend.GetStatsSnapshot().PendingClientCount}).");
            }
            finally
            {
                backend.ReleaseRejection.Set();
                backend.Stop();
                foreach (var client in clients)
                    client.Dispose();
            }
        }

        [Fact]
        public void SecureBackendDoesNotWritePlaintextBeforeTlsHandshake()
        {
            using var backend = new ProbeWssBackend();
            Assert.False(backend.PlaintextCapacityResponseSupported);
        }

        [Fact]
        public void SecureBackendStopHookReleasesDeferredCertificate()
        {
            using var backend = new ProbeWssBackend();
            var field = typeof(ManagedWssBackend).GetField(
                "_serverCertificate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            using var certificate = new X509Certificate2();
            field.SetValue(backend, certificate);

            var hook = typeof(ManagedWssBackend).GetMethod(
                "OnStopCompletedUnderLifecycleLock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(hook);
            hook.Invoke(backend, null);

            Assert.Null(field.GetValue(backend));
        }

        [Fact]
        public void SecureBackendDuplicateStartLeavesTheActiveCertificateUsable()
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "foxglove-wss-start-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var pfxPath = Path.Combine(directory, "server.pfx");
            try
            {
                using (var rsa = RSA.Create(2048))
                {
                    var request = new CertificateRequest(
                        "CN=localhost",
                        rsa,
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                    using var certificate = request.CreateSelfSigned(
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow.AddHours(1));
                    File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx));
                }

                using var backend = new ManagedWssBackend(new FoxgloveTlsOptions
                {
                    CertificatePfxPath = pfxPath
                });
                var port = GetFreeTcpPort();
                backend.Start("127.0.0.1", port);
                Assert.Throws<InvalidOperationException>(() => backend.Start("127.0.0.1", port));
                Assert.True(backend.IsRunning);
                backend.Stop();
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void HandshakeAdmissionRequiresExactUpgradeTokenAndSixteenByteKey()
        {
            var handlerType = typeof(ManagedWsBackend).Assembly.GetType(
                "Unity.FoxgloveSDK.Transport.WsHandshakeHandler");
            Assert.NotNull(handlerType);
            var tokenMethod = handlerType.GetMethod(
                "ContainsUpgradeToken", BindingFlags.Static | BindingFlags.NonPublic);
            var keyMethod = handlerType.GetMethod(
                "IsValidWebSocketKey", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(tokenMethod);
            Assert.NotNull(keyMethod);

            Assert.False((bool)tokenMethod.Invoke(null, new object[] { "NotUpgrade" }));
            Assert.True((bool)tokenMethod.Invoke(null, new object[] { "keep-alive, Upgrade" }));
            Assert.True((bool)keyMethod.Invoke(null, new object[] { "dGhlIHNhbXBsZSBub25jZQ==" }));
            Assert.False((bool)keyMethod.Invoke(null, new object[] { "dGhlIHNhbXBsZSBub25jZQ" }));
            Assert.False((bool)keyMethod.Invoke(null, new object[] { "not-base64" }));
        }

        [Fact]
        public void FailedListenerStartDoesNotPublishRunningStateAndCanRetry()
        {
            using var blocker = new TcpListener(System.Net.IPAddress.Loopback, 0);
            blocker.Start();
            var port = ((System.Net.IPEndPoint)blocker.LocalEndpoint).Port;
            using var backend = new ManagedWsBackend();

            Assert.ThrowsAny<SocketException>(() => backend.Start("127.0.0.1", port));
            Assert.False(backend.IsRunning);

            blocker.Stop();
            backend.Start("127.0.0.1", port);
            Assert.True(backend.IsRunning);
            backend.Stop();
            Assert.False(backend.IsRunning);
        }

        [Fact]
        public async Task StopDefersDisconnectUntilConnectCallbackReturns()
        {
            using var backend = new ManagedWsBackend(new ManagedWebSocketOptions { MaxClients = 2 });
            var port = GetFreeTcpPort();
            var connectEntered = new ManualResetEventSlim(false);
            var releaseConnect = new ManualResetEventSlim(false);
            var disconnectObserved = new ManualResetEventSlim(false);
            var order = new ConcurrentQueue<string>();
            backend.OnClientConnected += _ =>
            {
                order.Enqueue("connect");
                connectEntered.Set();
                releaseConnect.Wait(TimeSpan.FromSeconds(5));
            };
            backend.OnClientDisconnected += _ =>
            {
                order.Enqueue("disconnect");
                disconnectObserved.Set();
            };
            backend.Start("127.0.0.1", port);
            using var client = ConnectAndWriteHandshake(port);

            try
            {
                Assert.True(
                    connectEntered.Wait(TimeSpan.FromSeconds(2)),
                    "The connection callback did not enter its controlled pause.");

                var stopTask = Task.Run(() => backend.Stop());
                Assert.True(
                    SpinWait.SpinUntil(() => !backend.IsRunning, TimeSpan.FromSeconds(2)),
                    "Stop did not retire the listener before waiting for callbacks.");
                Assert.False(
                    disconnectObserved.Wait(TimeSpan.FromMilliseconds(300)),
                    "Disconnect must not overtake an in-flight connect callback.");

                releaseConnect.Set();
                Assert.True(
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(4))) == stopTask,
                    "Stop did not complete after the connect callback was released.");
                await stopTask;
                Assert.Equal(new[] { "connect", "disconnect" }, order.ToArray());
            }
            finally
            {
                releaseConnect.Set();
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

        [Fact]
        public void MalformedCloseSendsProtocolErrorCloseBeforeDisconnect()
        {
            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(
                BuildMaskedFrame(WsOpcode.Close, new byte[] { 0x03 }));
            var connection = new WsConnection(tcpClient, stream, 8, 1024);
            var clients = Clients(backend);
            clients[2] = connection;
            connection.StartSendLoop(null, CancellationToken.None);

            InvokeReceiveLoop(backend, 2, connection);

            var written = stream.WrittenBytes;
            Assert.True(written.Length >= 4);
            Assert.Equal((byte)(0x80 | WsOpcode.Close), written[0]);
            Assert.Equal(2, written[1] & 0x7F);
            Assert.Equal(1002, (written[2] << 8) | written[3]);
            Assert.Empty(clients);
        }

        [Fact]
        public void OversizedFragmentedMessageSendsMessageTooBigClose()
        {
            var frames = new List<byte[]>
            {
                BuildMaskedFrame(WsOpcode.Binary, new byte[ushort.MaxValue], fin: false)
            };
            for (var index = 0; index < 64; index++)
            {
                frames.Add(
                    BuildMaskedFrame(
                        WsOpcode.Continuation,
                        new byte[ushort.MaxValue],
                        fin: index == 63));
            }

            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(JoinFrames(frames));
            var connection = new WsConnection(tcpClient, stream, 8, 1024);
            var clients = Clients(backend);
            clients[4] = connection;
            connection.StartSendLoop(null, CancellationToken.None);

            InvokeReceiveLoop(backend, 4, connection);

            var written = stream.WrittenBytes;
            Assert.True(written.Length >= 4);
            Assert.Equal((byte)(0x80 | WsOpcode.Close), written[0]);
            Assert.Equal(2, written[1] & 0x7F);
            Assert.Equal(1009, (written[2] << 8) | written[3]);
            Assert.Empty(clients);
        }

        [Fact]
        public void StrictUtf8DecodeAllowsCodePointAcrossFragmentBoundary()
        {
            var first = BuildMaskedFrame(WsOpcode.Text, new byte[] { 0xE2 }, fin: false);
            var second = BuildMaskedFrame(WsOpcode.Continuation, new byte[] { 0x82, 0xAC });
            var input = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, input, 0, first.Length);
            Buffer.BlockCopy(second, 0, input, first.Length, second.Length);
            using var backend = new ManagedWsBackend();
            using var tcpClient = new TcpClient();
            var stream = new DuplexBufferStream(input);
            var connection = new WsConnection(tcpClient, stream, 8, 1024);
            var clients = Clients(backend);
            clients[3] = connection;
            string receivedText = null;
            backend.OnTextReceived += (_, text) => receivedText = text;

            InvokeReceiveLoop(backend, 3, connection);

            Assert.Equal("€", receivedText);
            Assert.Empty(clients);
        }

        [Fact]
        public void ClientIdsRemainMonotonicAcrossStop()
        {
            using var backend = new ManagedWsBackend();
            var allocate = typeof(ManagedWsBackend).GetMethod(
                "AllocateClientId",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(allocate);
            Assert.Equal(1u, (uint)allocate.Invoke(backend, null));

            backend.Stop();

            Assert.Equal(2u, (uint)allocate.Invoke(backend, null));
        }

        [Fact]
        public void StaleDisconnectCannotRemoveAReplacementConnection()
        {
            using var backend = new ManagedWsBackend();
            using var oldTcpClient = new TcpClient();
            using var newTcpClient = new TcpClient();
            var oldStream = new DuplexBufferStream(Array.Empty<byte>());
            var newStream = new DuplexBufferStream(Array.Empty<byte>());
            var oldConnection = new WsConnection(oldTcpClient, oldStream, 8, 1024);
            var newConnection = new WsConnection(newTcpClient, newStream, 8, 1024);
            var clients = Clients(backend);
            clients[7] = newConnection;
            var disconnectCallbacks = 0;
            backend.OnClientDisconnected += _ => disconnectCallbacks++;

            InvokeDisconnect(backend, 7, oldConnection);

            Assert.True(clients.TryGetValue(7, out var retained));
            Assert.Same(newConnection, retained);
            Assert.Equal(0, disconnectCallbacks);
            Assert.True(oldStream.IsDisposed);
            Assert.False(newStream.IsDisposed);

            InvokeDisconnect(backend, 7, newConnection);
            Assert.Empty(clients);
            Assert.Equal(1, disconnectCallbacks);
            Assert.True(newStream.IsDisposed);
        }

        private static WsFrameReadResult ReadResult(byte[] bytes, out WsFrame frame)
        {
            using var stream = new MemoryStream(bytes);
            return WsFrameCodec.ReadFrame(stream, out frame);
        }

        private static ConcurrentDictionary<uint, WsConnection> Clients(ManagedWsBackend backend)
        {
            var field = typeof(ManagedWsBackend).GetField(
                "_clients",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<ConcurrentDictionary<uint, WsConnection>>(field.GetValue(backend));
        }

        private static int PendingClientCount(ManagedWsBackend backend)
        {
            var field = typeof(ManagedWsBackend).GetField(
                "_pendingClients",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return 0;

            var value = field.GetValue(backend) as System.Collections.ICollection;
            return value?.Count ?? 0;
        }

        private static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static string ReadHttpHeaders(NetworkStream stream)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 8192)
            {
                int value;
                try
                {
                    value = stream.ReadByte();
                }
                catch (IOException)
                {
                    return "";
                }
                if (value < 0)
                    break;
                bytes.Add((byte)value);
                var count = bytes.Count;
                if (count >= 4
                    && bytes[count - 4] == '\r'
                    && bytes[count - 3] == '\n'
                    && bytes[count - 2] == '\r'
                    && bytes[count - 1] == '\n')
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static TcpClient ConnectAndWriteHandshake(int port)
        {
            var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;
            var request =
                "GET / HTTP/1.1\r\n" +
                "Host: 127.0.0.1\r\n" +
                "Connection: Upgrade\r\n" +
                "Upgrade: websocket\r\n" +
                "Sec-WebSocket-Version: 13\r\n" +
                "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
                "Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
            return client;
        }

        private static string BuildHandshakeRequest(
            string connection = "Upgrade",
            string upgrade = "websocket",
            bool includeKey = true)
        {
            var request = new StringBuilder()
                .Append("GET / HTTP/1.1\r\n")
                .Append("Host: 127.0.0.1\r\n");
            if (connection != null)
                request.Append("Connection: ").Append(connection).Append("\r\n");
            if (upgrade != null)
                request.Append("Upgrade: ").Append(upgrade).Append("\r\n");
            if (includeKey)
                request.Append("Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n");
            request
                .Append("Sec-WebSocket-Version: 13\r\n")
                .Append("Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n\r\n");
            return request.ToString();
        }

        private sealed class BlockingRejectBackend : ManagedWsBackend
        {
            internal readonly ManualResetEventSlim RejectionEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim ReleaseRejection = new ManualResetEventSlim();

            internal BlockingRejectBackend(ManagedWebSocketOptions options)
                : base(options) { }

            protected override void RejectPendingClient(TcpClient tcpClient)
            {
                RejectionEntered.Set();
                ReleaseRejection.Wait(TimeSpan.FromSeconds(5));
                base.RejectPendingClient(tcpClient);
            }

            public override void Dispose()
            {
                ReleaseRejection.Set();
                base.Dispose();
                RejectionEntered.Dispose();
                ReleaseRejection.Dispose();
            }
        }

        private sealed class ProbeWssBackend : ManagedWssBackend
        {
            internal ProbeWssBackend()
                : base(new FoxgloveTlsOptions()) { }

            internal bool PlaintextCapacityResponseSupported
                => SupportsPlaintextCapacityResponse;
        }

        private sealed class BlockingHandshakeBackend : ManagedWsBackend
        {
            internal readonly BlockingHandshakeStream HandshakeStream = new BlockingHandshakeStream();

            protected override Stream CreateClientStream(TcpClient tcpClient)
                => HandshakeStream;
        }

        private sealed class BlockingHandshakeStream : Stream
        {
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim();

            internal bool IsDisposed { get; private set; }

            internal void Release() => _release.Set();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override bool CanTimeout => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _release.Wait(TimeSpan.FromSeconds(30));
                return 0;
            }

            public override int ReadByte()
            {
                _release.Wait(TimeSpan.FromSeconds(30));
                return -1;
            }

            public override void Write(byte[] buffer, int offset, int count) { }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                _release.Set();
                _release.Dispose();
                base.Dispose(disposing);
            }
        }

        private static void InvokeReceiveLoop(
            ManagedWsBackend backend,
            uint clientId,
            WsConnection connection)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "ReceiveLoop",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { clientId, connection, CancellationToken.None });
        }

        private static void InvokeHandleClient(
            ManagedWsBackend backend,
            TcpClient tcpClient,
            CancellationToken cancellationToken)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "HandleClient",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { tcpClient, cancellationToken });
        }

        private static void InvokeDisconnect(
            ManagedWsBackend backend,
            uint clientId,
            WsConnection connection)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "DisconnectClient",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { clientId, connection });
        }

        private static byte[] BuildMaskedFrame(byte opcode, byte[] payload, bool fin = true)
        {
            payload ??= Array.Empty<byte>();
            if (payload.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(payload));

            var extendedLengthBytes = payload.Length <= 125 ? 0 : 2;
            var frame = new byte[2 + extendedLengthBytes + 4 + payload.Length];
            frame[0] = (byte)((fin ? 0x80 : 0x00) | opcode);
            if (extendedLengthBytes == 0)
            {
                frame[1] = (byte)(0x80 | payload.Length);
            }
            else
            {
                frame[1] = 0xFE;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)payload.Length;
            }
            WriteMaskedPayload(frame, 2 + extendedLengthBytes, payload);
            return frame;
        }

        private static byte[] JoinFrames(IReadOnlyCollection<byte[]> frames)
        {
            var totalLength = 0;
            foreach (var frame in frames)
                totalLength = checked(totalLength + frame.Length);

            var joined = new byte[totalLength];
            var offset = 0;
            foreach (var frame in frames)
            {
                Buffer.BlockCopy(frame, 0, joined, offset, frame.Length);
                offset += frame.Length;
            }
            return joined;
        }

        private static byte[] BuildNonMinimalMaskedFrame(byte opcode, byte marker, ulong payloadLength)
        {
            var extendedLengthBytes = marker == 126 ? 2 : 8;
            var payload = new byte[checked((int)payloadLength)];
            var frame = new byte[2 + extendedLengthBytes + 4 + payload.Length];
            frame[0] = (byte)(0x80 | opcode);
            frame[1] = (byte)(0x80 | marker);
            for (var i = 0; i < extendedLengthBytes; i++)
            {
                var shift = (extendedLengthBytes - i - 1) * 8;
                frame[2 + i] = (byte)(payloadLength >> shift);
            }

            WriteMaskedPayload(frame, 2 + extendedLengthBytes, payload);
            return frame;
        }

        private static void WriteMaskedPayload(byte[] frame, int maskOffset, byte[] payload)
        {
            var mask = new byte[] { 0x12, 0x34, 0x56, 0x78 };
            Buffer.BlockCopy(mask, 0, frame, maskOffset, mask.Length);
            for (var i = 0; i < payload.Length; i++)
                frame[maskOffset + mask.Length + i] = (byte)(payload[i] ^ mask[i % mask.Length]);
        }

        private sealed class DuplexBufferStream : Stream
        {
            private readonly MemoryStream _input;
            private readonly MemoryStream _output = new MemoryStream();
            private readonly object _outputLock = new object();

            public DuplexBufferStream(byte[] input)
            {
                _input = new MemoryStream(input ?? Array.Empty<byte>(), writable: false);
            }

            public byte[] WrittenBytes
            {
                get
                {
                    lock (_outputLock)
                    {
                        return _output.ToArray();
                    }
                }
            }

            public bool IsDisposed { get; private set; }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
                => _input.Read(buffer, offset, count);

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_outputLock)
                {
                    _output.Write(buffer, offset, count);
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
