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
    public sealed partial class ManagedWebSocketStrictnessTests
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
        public void StopWaitsForCapacityResponseWorkersToFinish()
        {
            var options = new ManagedWebSocketOptions { MaxClients = 1 };
            using var backend = new BlockingRejectBackend(options);
            var port = GetFreeTcpPort();
            backend.Start("127.0.0.1", port);
            using var first = ConnectAndWriteHandshake(port);
            using var second = ConnectAndWriteHandshake(port);

            try
            {
                Assert.StartsWith("HTTP/1.1 101", ReadHttpHeaders(first.GetStream()));
                Assert.True(
                    backend.RejectionEntered.Wait(TimeSpan.FromSeconds(2)),
                    "The capacity response worker did not start.");

                var stopTask = Task.Run(() => backend.Stop());
                Assert.False(
                    stopTask.Wait(TimeSpan.FromMilliseconds(100)),
                    "Stop returned while a capacity response worker was still running.");

                backend.ReleaseRejection.Set();
                Assert.True(stopTask.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                backend.ReleaseRejection.Set();
                if (backend.IsRunning)
                    backend.Stop();
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
                releaseConnect.Wait();
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

                Assert.True(
                    stopTask.Wait(TimeSpan.FromSeconds(9)),
                    "Stop must return after the bounded client-handler wait even when the callback is still blocked.");
                Assert.Throws<InvalidOperationException>(
                    () => backend.Start("127.0.0.1", port));

                releaseConnect.Set();
                Assert.True(
                    await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(4))) == stopTask,
                    "Stop did not complete after the connect callback was released.");
                await stopTask;
                Assert.True(
                    SpinWait.SpinUntil(() => ClientHandlerCount(backend) == 0, TimeSpan.FromSeconds(4)),
                    "The retained client handler did not leave the tracking set after its callback returned.");
                Assert.Equal(new[] { "connect", "disconnect" }, order.ToArray());
            }
            finally
            {
                releaseConnect.Set();
            }
        }

    }
}
