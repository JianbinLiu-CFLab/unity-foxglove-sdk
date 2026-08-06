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

        private static string CreateCertificateFixture()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "phase187-certificate-fixture", Encoding.ASCII);
            return path;
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
