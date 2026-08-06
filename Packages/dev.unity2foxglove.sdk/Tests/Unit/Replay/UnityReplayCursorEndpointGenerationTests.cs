// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Replay
// Purpose: Locks replay cursor endpoint worker generations across restart.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Replay
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Replay")]
    public sealed class UnityReplayCursorEndpointGenerationTests
    {
        private const string CursorJson =
            "{\"source\":\"phase187\",\"sequence\":1,\"mode\":\"seek\",\"time\":{\"sec\":2,\"nsec\":3}}";

        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        [Fact]
        public async Task RestartDoesNotPublishTheNextListenerBeforeTheOldWorkerRetires()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            using var oldQueueEntered = new ManualResetEventSlim();
            using var releaseOldQueue = new ManualResetEventSlim();
            var oldQueueCalls = 0;
            var newQueueCalls = 0;
            var oldPort = ReserveFreeLoopbackPort();
            var newPort = ReserveFreeLoopbackPort();

            endpoint.Start(
                Options(oldPort, "/old", "old-token"),
                _ =>
                {
                    Interlocked.Increment(ref oldQueueCalls);
                    oldQueueEntered.Set();
                    releaseOldQueue.Wait(TimeSpan.FromSeconds(5));
                    return new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.");
                });

            var oldRequest = PostCursorAsync(oldPort, "/old", "old-token");
            Assert.True(oldQueueEntered.Wait(TimeSpan.FromSeconds(5)), "Old worker never entered its queue callback.");

            var restart = Task.Run(() => endpoint.Start(
                Options(newPort, "/new", "new-token"),
                _ =>
                {
                    Interlocked.Increment(ref newQueueCalls);
                    return new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.");
                }));

            var publishedWhileOldWorkerWasBlocked = ReferenceEquals(
                await Task.WhenAny(restart, Task.Delay(TimeSpan.FromMilliseconds(100))),
                restart);
            releaseOldQueue.Set();

            var completedRestart = await Task.WhenAny(restart, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(restart, completedRestart);
            await restart;
            await ObserveRetiredRequestAsync(oldRequest);

            var newStatus = await PostCursorAsync(newPort, "/new", "new-token");
            Assert.Equal(HttpStatusCode.Accepted, newStatus);
            Assert.Equal(1, Volatile.Read(ref oldQueueCalls));
            Assert.Equal(1, Volatile.Read(ref newQueueCalls));
            Assert.False(
                publishedWhileOldWorkerWasBlocked,
                "The replacement listener became observable before the prior worker retired.");
        }

        [Fact]
        public async Task TimedOutRetirementCannotClearOrConsumeTheReplacementGeneration()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            using var oldQueueEntered = new ManualResetEventSlim();
            using var releaseOldQueue = new ManualResetEventSlim();
            var oldQueueCalls = 0;
            var newQueueCalls = 0;
            var oldPort = ReserveFreeLoopbackPort();
            var newPort = ReserveFreeLoopbackPort();
            Task<HttpStatusCode> oldRequest = null;

            try
            {
                endpoint.Start(
                    Options(oldPort, "/old-timeout", "old-timeout-token"),
                    _ =>
                    {
                        Interlocked.Increment(ref oldQueueCalls);
                        oldQueueEntered.Set();
                        releaseOldQueue.Wait(TimeSpan.FromSeconds(5));
                        return new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.");
                    });

                oldRequest = PostCursorAsync(oldPort, "/old-timeout", "old-timeout-token");
                Assert.True(
                    oldQueueEntered.Wait(TimeSpan.FromSeconds(5)),
                    "Old worker never entered its queue callback.");

                var restart = Task.Run(() => endpoint.Start(
                    Options(newPort, "/new-timeout", "new-timeout-token"),
                    _ =>
                    {
                        Interlocked.Increment(ref newQueueCalls);
                        return new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.");
                    }));

                var completedRestart = await Task.WhenAny(restart, Task.Delay(TimeSpan.FromSeconds(3)));
                Assert.Same(restart, completedRestart);
                await restart;
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    await PostCursorAsync(newPort, "/new-timeout", "new-timeout-token"));

                releaseOldQueue.Set();
                await ObserveRetiredRequestAsync(oldRequest);
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                Assert.True(endpoint.IsRunning);
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    await PostCursorAsync(newPort, "/new-timeout", "new-timeout-token"));
                Assert.Equal(1, Volatile.Read(ref oldQueueCalls));
                Assert.Equal(2, Volatile.Read(ref newQueueCalls));
            }
            finally
            {
                releaseOldQueue.Set();
                if (oldRequest != null)
                {
                    await ObserveRetiredRequestAsync(oldRequest);
                }
            }
        }

        [Fact]
        public void WorkerLoopAndHandlersUseOnlyTheirCapturedGeneration()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var listenLoop = TestSources.ExtractMethod(source, "private void ListenLoop(WorkerGeneration generation)");
            var handle = TestSources.ExtractMethod(
                source,
                "private void Handle(WorkerGeneration generation, HttpListenerContext context)");
            var stop = TestSources.ExtractMethod(source, "private void StopNoLock()");

            Assert.Contains("private sealed class WorkerGeneration", source, StringComparison.Ordinal);
            Assert.Contains("generation.Listener.GetContext()", listenLoop, StringComparison.Ordinal);
            Assert.Contains("generation.Options", handle, StringComparison.Ordinal);
            Assert.Contains("generation.Queue", handle, StringComparison.Ordinal);
            Assert.Contains("generation.StateProvider", handle, StringComparison.Ordinal);
            Assert.DoesNotContain("_listener", listenLoop, StringComparison.Ordinal);
            Assert.DoesNotContain("_options", handle, StringComparison.Ordinal);
            Assert.DoesNotContain("_queue", handle, StringComparison.Ordinal);
            Assert.Contains("generation.Worker.Join", stop, StringComparison.Ordinal);
            Assert.Contains("ManagedWebSocketOptions.FixedTimeEqualsUtf8", source, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AbortedResponseDoesNotRetireTheListenerWorker()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            using var queueEntered = new ManualResetEventSlim();
            using var releaseQueue = new ManualResetEventSlim();
            var port = ReserveFreeLoopbackPort();

            endpoint.Start(
                Options(port, "/abort", "abort-token"),
                _ =>
                {
                    queueEntered.Set();
                    releaseQueue.Wait(TimeSpan.FromSeconds(5));
                    return new UnityReplayCursorEndpointQueueResult(
                        true,
                        new string('x', 2 * 1024 * 1024));
                });

            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, port);
                var request = Encoding.ASCII.GetBytes(
                    "POST /abort HTTP/1.1\r\n"
                    + "Host: 127.0.0.1\r\n"
                    + "Authorization: Bearer abort-token\r\n"
                    + "Content-Type: application/json\r\n"
                    + "Content-Length: " + Encoding.UTF8.GetByteCount(CursorJson) + "\r\n"
                    + "Connection: close\r\n\r\n"
                    + CursorJson);
                await client.GetStream().WriteAsync(request, 0, request.Length);
                Assert.True(queueEntered.Wait(TimeSpan.FromSeconds(5)), "Worker never reached the queue callback.");

                client.Client.LingerState = new LingerOption(true, 0);
                client.Close();
                releaseQueue.Set();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.Equal(
                HttpStatusCode.Accepted,
                await PostCursorAsync(port, "/abort", "abort-token"));
        }

        private static UnityReplayCursorEndpointOptions Options(int port, string path, string bearerToken)
            => new UnityReplayCursorEndpointOptions(
                enabled: true,
                host: "127.0.0.1",
                port: port,
                path: path,
                bearerToken: bearerToken,
                maxBodyBytes: UnityReplayCursorEndpointOptions.Default.MaxBodyBytes);

        private static async Task<HttpStatusCode> PostCursorAsync(int port, string path, string bearerToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{port}{path}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                bearerToken);
            request.Content = new StringContent(CursorJson, Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(request);
            return response.StatusCode;
        }

        private static async Task ObserveRetiredRequestAsync(Task<HttpStatusCode> request)
        {
            try
            {
                await request;
            }
            catch (HttpRequestException)
            {
                // Closing the retired listener may abort its in-flight response.
            }
            catch (TaskCanceledException)
            {
                // A closed listener can surface as a bounded client timeout.
            }
        }

        private static int ReserveFreeLoopbackPort()
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
    }
}
