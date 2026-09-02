// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Transport
// Purpose: Certificate distributor start rollback and client shutdown ownership.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Transport
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Transport")]
    public sealed class CertificateDistributorLifecycleTests
    {
        [Fact]
        public void BindFailureDoesNotPublishRunningStateAndCanRetry()
        {
            var certificatePath = CreateCertificateFixture();
            var blocker = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                blocker.Server.ExclusiveAddressUse = true;
                blocker.Start();
                var port = ((IPEndPoint)blocker.LocalEndpoint).Port;

                using var distributor = new FoxgloveCertificateDistributor(certificatePath);
                Assert.ThrowsAny<SocketException>(() => distributor.Start("127.0.0.1", port));
                Assert.False(distributor.IsRunning);

                blocker.Stop();
                distributor.Start("127.0.0.1", port);
                Assert.True(distributor.IsRunning);

                distributor.Stop();
                Assert.False(distributor.IsRunning);
            }
            finally
            {
                blocker.Stop();
                File.Delete(certificatePath);
            }
        }

        [Fact]
        public void FingerprintAndDownloadRemainBoundToStartedCertificateSnapshot()
        {
            var certificatePath = CreateCertificateFixture("certificate-A");
            var distributor = new FoxgloveCertificateDistributor(
                certificatePath,
                clientIoTimeoutMs: 5000);
            try
            {
                distributor.Start("127.0.0.1", 0);
                var firstEndpoint = GetListenerEndpoint(distributor);
                var fingerprintA = FoxgloveCertificateDistributor.ComputeSha256Fingerprint(certificatePath);
                var pageA = ReadHttpBody(firstEndpoint, "/");
                var downloadA = ReadHttpBody(firstEndpoint, "/rootCA.crt");

                Assert.Contains(fingerprintA, pageA, StringComparison.Ordinal);
                Assert.Equal("certificate-A", downloadA);

                File.WriteAllText(certificatePath, "certificate-B", Encoding.ASCII);
                var pageAfterMutation = ReadHttpBody(GetListenerEndpoint(distributor), "/");
                var downloadAfterMutation = ReadHttpBody(GetListenerEndpoint(distributor), "/rootCA.crt");

                Assert.Contains(fingerprintA, pageAfterMutation, StringComparison.Ordinal);
                Assert.Equal("certificate-A", downloadAfterMutation);

                distributor.Stop();
                distributor.Start("127.0.0.1", 0);
                var secondEndpoint = GetListenerEndpoint(distributor);
                var fingerprintB = FoxgloveCertificateDistributor.ComputeSha256Fingerprint(certificatePath);
                Assert.NotEqual(fingerprintA, fingerprintB);
                Assert.Contains(fingerprintB, ReadHttpBody(secondEndpoint, "/"), StringComparison.Ordinal);
                Assert.Equal("certificate-B", ReadHttpBody(secondEndpoint, "/rootCA.crt"));
            }
            finally
            {
                distributor.Dispose();
                File.Delete(certificatePath);
            }
        }

        [Fact]
        public void OptionalPemDownloadRemainsBoundToStartedSnapshot()
        {
            var certificatePath = CreateCertificateFixture("certificate-A");
            var pemPath = CreateCertificateFixture("pem-A");
            var distributor = new FoxgloveCertificateDistributor(certificatePath, pemPath);
            try
            {
                distributor.Start("127.0.0.1", 0);
                var endpoint = GetListenerEndpoint(distributor);
                Assert.Equal("pem-A", ReadHttpBody(endpoint, "/rootCA.pem"));

                File.WriteAllText(pemPath, "pem-B", Encoding.ASCII);
                Assert.Equal("pem-A", ReadHttpBody(GetListenerEndpoint(distributor), "/rootCA.pem"));
            }
            finally
            {
                distributor.Dispose();
                File.Delete(certificatePath);
                File.Delete(pemPath);
            }
        }

        [Fact]
        public void DisposeAbortsPartialClientAndWaitsForHandlerExit()
        {
            var certificatePath = CreateCertificateFixture();
            var distributor = new FoxgloveCertificateDistributor(
                certificatePath,
                clientIoTimeoutMs: 5000);
            try
            {
                distributor.Start("127.0.0.1", 0);
                var endpoint = GetListenerEndpoint(distributor);
                using var client = new TcpClient();
                client.Connect(endpoint.Address, endpoint.Port);
                var partialRequest = Encoding.ASCII.GetBytes("GET /partial");
                client.GetStream().Write(partialRequest, 0, partialRequest.Length);

                Assert.True(
                    SpinWait.SpinUntil(() => GetActiveClientHandlerCount(distributor) == 1, 2000),
                    "The partial client must be owned by a handler before Dispose starts.");

                var elapsed = Stopwatch.StartNew();
                distributor.Dispose();
                elapsed.Stop();

                Assert.False(distributor.IsRunning);
                Assert.Equal(0, GetActiveClientHandlerCount(distributor));
                Assert.True(
                    elapsed.Elapsed < TimeSpan.FromSeconds(3),
                    $"Closing an owned client should interrupt I/O promptly; elapsed={elapsed.Elapsed}.");
            }
            finally
            {
                distributor.Dispose();
                File.Delete(certificatePath);
            }
        }

        [Fact]
        public async Task DisposeReturnsBoundedlyWithoutDisposingActiveHandlerSignal()
        {
            var certificatePath = CreateCertificateFixture();
            var distributor = new FoxgloveCertificateDistributor(certificatePath);
            var handlersIdle = Assert.IsType<ManualResetEventSlim>(
                RequiredField("_clientHandlersIdle").GetValue(distributor));
            var simulatedClient = new TcpClient();
            Task disposeTask = null;
            try
            {
                RequiredField("_activeClientHandlers").SetValue(distributor, 1);
                handlersIdle.Reset();

                disposeTask = Task.Run(distributor.Dispose);
                var completed = await Task.WhenAny(
                    disposeTask,
                    Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.True(
                    ReferenceEquals(completed, disposeTask),
                    "Dispose waited without a bound for an active client handler.");
                await disposeTask;

                var complete = typeof(FoxgloveCertificateDistributor).GetMethod(
                    "CompleteClientHandler",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException(
                        "The certificate distributor handler completion boundary is missing.");
                complete.Invoke(distributor, new object[] { simulatedClient });
                Assert.Equal(0, GetActiveClientHandlerCount(distributor));
            }
            finally
            {
                if (disposeTask != null && !disposeTask.IsCompleted)
                {
                    RequiredField("_activeClientHandlers").SetValue(distributor, 0);
                    handlersIdle.Set();
                    var completed = await Task.WhenAny(
                        disposeTask,
                        Task.Delay(TimeSpan.FromSeconds(2)));
                    Assert.True(ReferenceEquals(completed, disposeTask));
                    await disposeTask;
                }
                simulatedClient.Dispose();
                distributor.Dispose();
                File.Delete(certificatePath);
            }
        }

        private static string CreateCertificateFixture(string contents = "phase187-certificate-fixture")
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, contents, Encoding.ASCII);
            return path;
        }

        private static string ReadHttpBody(IPEndPoint endpoint, string path)
        {
            using var client = new TcpClient();
            client.Connect(endpoint.Address, endpoint.Port);
            using var stream = client.GetStream();
            stream.ReadTimeout = 5000;
            stream.WriteTimeout = 5000;
            var request = Encoding.ASCII.GetBytes(
                $"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
            stream.Write(request, 0, request.Length);
            using var response = new MemoryStream();
            var buffer = new byte[1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                response.Write(buffer, 0, read);

            var text = Encoding.UTF8.GetString(response.ToArray());
            var separator = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return separator >= 0 ? text.Substring(separator + 4) : text;
        }

        private static IPEndPoint GetListenerEndpoint(FoxgloveCertificateDistributor distributor)
        {
            var field = typeof(FoxgloveCertificateDistributor).GetField(
                "_listener",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var listener = Assert.IsType<TcpListener>(field?.GetValue(distributor));
            return Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        }

        private static int GetActiveClientHandlerCount(FoxgloveCertificateDistributor distributor)
        {
            return Assert.IsType<int>(
                RequiredField("_activeClientHandlers").GetValue(distributor));
        }

        private static FieldInfo RequiredField(string name)
            => typeof(FoxgloveCertificateDistributor).GetField(
                   name,
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Required certificate distributor field is missing: " + name);
    }
}
