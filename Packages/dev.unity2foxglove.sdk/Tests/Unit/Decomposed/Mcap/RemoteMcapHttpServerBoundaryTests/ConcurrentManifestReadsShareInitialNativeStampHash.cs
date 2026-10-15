// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed partial class RemoteMcapHttpServerBoundaryTests
    {
        [Fact]
        public async Task ConcurrentManifestReadsShareInitialNativeStampHash()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-source-stamp-concurrent-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildRemoteMcap(1, 0, 64 * 1024));
                var source = new RemoteMcapDataSourcePrototype(path, "concurrent", "Concurrent", string.Empty);
                var counter = typeof(RemoteMcapDataSourcePrototype).GetField(
                    "_fullContentHashComputations",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var observedToken = typeof(RemoteMcapDataSourcePrototype).GetField(
                    "_hasObservedStamp",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(counter);
                Assert.NotNull(observedToken);

                using var start = new ManualResetEventSlim(false);
                var tasks = new List<Task>();
                for (var i = 0; i < 8; i++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        start.Wait();
                        return source.GetManifest(new RemoteMcapRequest());
                    }));
                }

                start.Set();
                await Task.WhenAll(tasks);

                var hashCount = (int)counter.GetValue(source);
                Assert.True(hashCount > 0);
                if ((bool)observedToken.GetValue(source))
                    Assert.Equal(1, hashCount);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void SourceIdentityUsesTrustedGenerationVersionAuthority()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-generation-authority-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildRemoteMcap(1));
                var generation = "generation-1";
                var source = new RemoteMcapDataSourcePrototype(
                    path,
                    "authority",
                    "Authority",
                    string.Empty,
                    generationVersionProvider: () => generation);
                var first = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                generation = "generation-2";
                var second = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                Assert.NotEqual(first, second);
                Assert.EndsWith("generation:generation-1", first);
                Assert.EndsWith("generation:generation-2", second);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void SourceIdentityIncludesContentGenerationAcrossPrototypeRestart()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-generation-restart-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildRemoteMcap(1));
                var first = new RemoteMcapDataSourcePrototype(path, "restart", "Restart", string.Empty)
                    .GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;
                var firstRestart = new RemoteMcapDataSourcePrototype(path, "restart", "Restart", string.Empty)
                    .GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                File.WriteAllBytes(path, BuildRemoteMcap(2));
                var secondRestart = new RemoteMcapDataSourcePrototype(path, "restart", "Restart", string.Empty)
                    .GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                Assert.StartsWith("restart@", first);
                Assert.Equal(first, firstRestart);
                Assert.StartsWith("restart@", secondRestart);
                Assert.NotEqual(first, secondRestart);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void ManifestDataRoutePreservesCustomQueryParameters()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-route-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildRemoteMcap(1));
                var source = new RemoteMcapDataSourcePrototype(
                    path,
                    "route",
                    "Route",
                    string.Empty,
                    dataRoute: "/v1/data?recordingId=route&startTime=1");

                var manifest = source.GetManifest(new RemoteMcapRequest()).Manifest;
                Assert.Equal(
                    "/v1/data?recordingId="
                    + Uri.EscapeDataString(manifest.Sources[0].Id)
                    + "&startTime=1",
                    manifest.Sources[0].DataUrl);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public async Task SlowFileResponseDoesNotBlockNextAcceptedRequest()
        {
            var path = Path.GetTempFileName();
            RemoteMcapHttpServer server = null;
            TcpClient slowClient = null;
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
                    file.SetLength(SlowResponseLength);

                var options = new RemoteMcapHttpOptions
                {
                    Host = "127.0.0.1",
                    McapPath = path,
                    SourceId = "slow"
                };
                server = StartLoopbackServerWithRetry(options);

                slowClient = new TcpClient { ReceiveBufferSize = 256 };
                await slowClient.ConnectAsync(IPAddress.Loopback, options.Port);
                var slowRequest = Encoding.ASCII.GetBytes(
                    "GET /v1/files/slow.mcap HTTP/1.1\r\n"
                    + "Host: 127.0.0.1:" + options.Port + "\r\n"
                    + "Connection: close\r\n\r\n");
                var slowStream = slowClient.GetStream();
                await slowStream.WriteAsync(slowRequest, 0, slowRequest.Length);
                await ReadResponseHeadersAsync(slowStream, TimeSpan.FromSeconds(2));
                await Task.Delay(100);

                using (var client = new HttpClient())
                using (var request = new HttpRequestMessage(
                           HttpMethod.Options,
                           server.BaseUrl + "/v1/manifest"))
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                using (var response = await client.SendAsync(request, timeout.Token))
                {
                    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                }
            }
            finally
            {
                slowClient?.Dispose();
                server?.Dispose();
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public async Task ScheduledRequestIsTrackedBeforeASynchronousHandlerCanBlock()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = RemoteMcapHttpServer.ScheduleRequest(async () =>
            {
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
            });

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(first.IsCompleted);

            var second = RemoteMcapHttpServer.ScheduleRequest(() =>
            {
                secondEntered.TrySetResult(true);
                return Task.CompletedTask;
            });
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await second.WaitAsync(TimeSpan.FromSeconds(2));

            release.TrySetResult(true);
            await first.WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void CancelledRangeConstructionStopsBeforeOpeningTheSource()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var source = new RemoteMcapDataSourcePrototype(
                Path.Combine(Path.GetTempPath(), "r4-f03-003-missing.mcap"),
                "r4",
                "r4",
                string.Empty);

            Assert.Throws<OperationCanceledException>(() => source.GetDataStream(
                new RemoteMcapRequest { SourceId = "r4" },
                cancellation.Token));
        }

        [Fact]
        public void ConcurrentDisposeClaimsShutdownExactlyOnce()
        {
            var path = Path.Combine(Path.GetTempPath(), "dispose-race-" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, new byte[] { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P' });
            RemoteMcapHttpServer server = null;
            try
            {
                server = StartLoopbackServerWithRetry(new RemoteMcapHttpOptions
                {
                    Host = "127.0.0.1",
                    McapPath = path,
                    SourceId = "dispose-race"
                });

                // The hook fires inside the first Dispose, before the shutdown is claimed, and runs a
                // second Dispose to completion on another thread. That pins the contract callers rely
                // on: a concurrent Dispose must not throw, and exactly one disposal must happen.
                // It does NOT prove the claim is atomic. The hook runs before the read, so a
                // non-atomic claim would observe the second call's write here and return just as the
                // atomic one does. The unsafe window is two instructions wide and has no
                // deterministic oracle - see build/phase191-followup-20260921/RESIDUAL_FINDINGS.md.
                var armed = 0;
                Exception concurrentError = null;
                var target = server;
                target.TestHook = point =>
                {
                    if (point != "DisposeBeforeClaim" || Interlocked.Exchange(ref armed, 1) != 0)
                        return;
                    var concurrent = Task.Run(() =>
                    {
                        try
                        {
                            target.Dispose();
                        }
                        catch (Exception error)
                        {
                            Volatile.Write(ref concurrentError, error);
                        }
                    });
                    Assert.True(concurrent.Wait(TimeSpan.FromSeconds(10)), "the concurrent dispose must finish");
                };

                var firstError = Record.Exception(() => target.Dispose());

                Assert.Null(firstError);
                Assert.Null(Volatile.Read(ref concurrentError));
                Assert.False(target.IsRunning);
                Assert.Equal(1, target.StopDisposeCountForTests);

                // A later dispose stays a no-op.
                Assert.Null(Record.Exception(() => target.Dispose()));
                Assert.Equal(1, target.StopDisposeCountForTests);
                server = null;
            }
            finally
            {
                server?.Dispose();
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void DisposeStormFromManyThreadsDisposesExactlyOnce()
        {
            var path = Path.Combine(Path.GetTempPath(), "dispose-storm-" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, new byte[] { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P' });
            RemoteMcapHttpServer server = null;
            try
            {
                server = StartLoopbackServerWithRetry(new RemoteMcapHttpOptions
                {
                    Host = "127.0.0.1",
                    McapPath = path,
                    SourceId = "dispose-storm"
                });

                const int threads = 16;
                var target = server;
                using var start = new ManualResetEventSlim(false);
                var errors = new ConcurrentBag<Exception>();
                var workers = new Task[threads];
                for (var index = 0; index < threads; index++)
                {
                    workers[index] = Task.Factory.StartNew(
                        () =>
                        {
                            start.Wait();
                            try
                            {
                                target.Dispose();
                            }
                            catch (Exception error)
                            {
                                errors.Add(error);
                            }
                        },
                        TaskCreationOptions.LongRunning);
                }

                start.Set();
                Assert.True(Task.WaitAll(workers, TimeSpan.FromSeconds(30)), "every dispose must finish");

                Assert.Empty(errors);
                Assert.False(target.IsRunning);
                Assert.Equal(1, target.StopDisposeCountForTests);
                server = null;
            }
            finally
            {
                server?.Dispose();
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }

        private static RemoteMcapHttpServer StartLoopbackServerWithRetry(
            RemoteMcapHttpOptions options)
        {
            Exception lastError = null;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                options.Port = FindFreeLoopbackPort();
                try
                {
                    return RemoteMcapHttpServer.Start(options);
                }
                catch (Exception error) when (IsAddressAlreadyInUse(error))
                {
                    lastError = error;
                }
            }

            throw new InvalidOperationException(
                "Could not bind a loopback Remote MCAP test server.",
                lastError);
        }

        private static int FindFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static byte[] BuildRemoteMcap(int messageCount, byte payloadSeed = 0, int payloadSize = 1)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "remote-generation");
                writer.WriteChannel(1, 0, "/generation", "json", new Dictionary<string, string>());
                for (var i = 0; i < messageCount; i++)
                    writer.WriteMessage(1, (uint)(i + 1), (ulong)(i + 1), (ulong)(i + 1), BuildPayload(payloadSize, (byte)(i + payloadSeed)));
                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics
                    {
                        MessageCount = (ulong)messageCount,
                        ChannelCount = 1,
                        MessageStartTime = 1,
                        MessageEndTime = (ulong)messageCount,
                        ChannelMessageCounts = new Dictionary<ushort, ulong> { [1] = (ulong)messageCount }
                    }
                };
                summary.Channels.Add(new McapChannel { Id = 1, Topic = "/generation", MessageEncoding = "json" });
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            return stream.ToArray();
        }

        private static byte[] BuildPayload(int size, byte value)
        {
            var payload = new byte[size];
            Array.Fill(payload, value);
            return payload;
        }

        private static bool IsAddressAlreadyInUse(Exception error)
            => error is SocketException socket
               && socket.SocketErrorCode == SocketError.AddressAlreadyInUse
               || error is HttpListenerException listener
               && (listener.ErrorCode == 183 || listener.ErrorCode == 10_048);

        private static async Task ReadResponseHeadersAsync(
            Stream stream,
            TimeSpan timeout)
        {
            var marker = new byte[] { 13, 10, 13, 10 };
            var matched = 0;
            var buffer = new byte[1];
            using (var cancellation = new CancellationTokenSource(timeout))
            {
                while (matched < marker.Length)
                {
                    var read = await stream.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        cancellation.Token);
                    if (read == 0)
                        throw new EndOfStreamException("Remote MCAP response ended before its HTTP headers.");

                    matched = buffer[0] == marker[matched]
                        ? matched + 1
                        : buffer[0] == marker[0] ? 1 : 0;
                }
            }
        }

        private static void DeleteTempFileWithRetry(string path)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(25);
                }
            }
        }
    }
}
