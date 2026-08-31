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
        public async Task RetirementCapacityRefusesThirdGenerationWithoutStoppingSecond()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            using var firstQueueEntered = new ManualResetEventSlim();
            using var releaseFirstQueue = new ManualResetEventSlim();
            using var secondQueueEntered = new ManualResetEventSlim();
            using var releaseSecondQueue = new ManualResetEventSlim();
            var firstPort = ReserveFreeLoopbackPort();
            var secondPort = ReserveFreeLoopbackPort();
            var thirdPort = ReserveFreeLoopbackPort();
            Task<HttpStatusCode> firstRequest = null;
            Task<HttpStatusCode> secondRequest = null;

            try
            {
                endpoint.Start(
                    Options(firstPort, "/retire-first", "retire-first-token"),
                    _ =>
                    {
                        firstQueueEntered.Set();
                        releaseFirstQueue.Wait(TimeSpan.FromSeconds(10));
                        return new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.");
                    });
                firstRequest = PostCursorAsync(firstPort, "/retire-first", "retire-first-token");
                Assert.True(
                    firstQueueEntered.Wait(TimeSpan.FromSeconds(5)),
                    "First worker never entered its queue callback.");

                endpoint.Start(
                    Options(secondPort, "/retire-second", "retire-second-token"),
                    _ =>
                    {
                        secondQueueEntered.Set();
                        releaseSecondQueue.Wait(TimeSpan.FromSeconds(10));
                        return new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.");
                    });
                secondRequest = PostCursorAsync(secondPort, "/retire-second", "retire-second-token");
                Assert.True(
                    secondQueueEntered.Wait(TimeSpan.FromSeconds(5)),
                    "Second worker never entered its queue callback.");

                var error = Record.Exception(() => endpoint.Start(
                    Options(thirdPort, "/retire-third", "retire-third-token"),
                    _ => new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.")));

                var capacityError = Assert.IsType<InvalidOperationException>(error);
                Assert.Equal(
                    "Replay cursor endpoint retirement capacity is exhausted; the current generation remains active.",
                    capacityError.Message);
                Assert.Equal(1, endpoint.RetiringGenerationCount);

                releaseSecondQueue.Set();
                await ObserveRetiredRequestAsync(secondRequest);
                Assert.True(endpoint.IsRunning);
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    await PostCursorAsync(secondPort, "/retire-second", "retire-second-token"));

                releaseFirstQueue.Set();
                await ObserveRetiredRequestAsync(firstRequest);
                for (var i = 0; i < 200 && endpoint.RetiringGenerationCount != 0; i++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25));
                }
                Assert.Equal(0, endpoint.RetiringGenerationCount);
                endpoint.Start(
                    Options(thirdPort, "/retire-third", "retire-third-token"),
                    _ => new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted."));
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    await PostCursorAsync(thirdPort, "/retire-third", "retire-third-token"));
            }
            finally
            {
                releaseFirstQueue.Set();
                releaseSecondQueue.Set();
                if (firstRequest != null)
                {
                    await ObserveRetiredRequestAsync(firstRequest);
                }
                if (secondRequest != null)
                {
                    await ObserveRetiredRequestAsync(secondRequest);
                }
            }
        }

        [Fact]
        public async Task RetiredGenerationCannotOverwriteNewerCursor()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            var controller = new ExternalReplayCursorController { Enabled = true };
            using var oldQueueEntered = new ManualResetEventSlim();
            using var releaseOldQueue = new ManualResetEventSlim();
            using var newQueueEntered = new ManualResetEventSlim();
            var oldPort = ReserveFreeLoopbackPort();
            var newPort = ReserveFreeLoopbackPort();
            Task<HttpStatusCode> oldRequest = null;

            try
            {
                endpoint.Start(
                    Options(oldPort, "/authority-old", "authority-old-token"),
                    request =>
                    {
                        oldQueueEntered.Set();
                        releaseOldQueue.Wait(TimeSpan.FromSeconds(10));
                        var result = controller.TryEnqueue(
                            request,
                            replayEnabled: true,
                            startNs: 0,
                            endNs: 10_000_000_000UL,
                            out var message);
                        return new UnityReplayCursorEndpointQueueResult(
                            result == ExternalReplayCursorEnqueueResult.Accepted
                            || result == ExternalReplayCursorEnqueueResult.Duplicate,
                            message);
                    });
                oldRequest = PostCursorAsync(
                    oldPort,
                    "/authority-old",
                    "authority-old-token",
                    CursorJsonFor(sequence: 1, sec: 2, nsec: 3));
                Assert.True(oldQueueEntered.Wait(TimeSpan.FromSeconds(5)), "Old worker never entered its queue callback.");

                endpoint.Start(
                    Options(newPort, "/authority-new", "authority-new-token"),
                    request =>
                    {
                        var result = controller.TryEnqueue(
                            request,
                            replayEnabled: true,
                            startNs: 0,
                            endNs: 10_000_000_000UL,
                            out var message);
                        newQueueEntered.Set();
                        return new UnityReplayCursorEndpointQueueResult(
                            result == ExternalReplayCursorEnqueueResult.Accepted
                            || result == ExternalReplayCursorEnqueueResult.Duplicate,
                            message);
                    });
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    await PostCursorAsync(
                        newPort,
                        "/authority-new",
                        "authority-new-token",
                        CursorJsonFor(sequence: 2, sec: 3, nsec: 4)));
                Assert.True(newQueueEntered.Wait(TimeSpan.FromSeconds(5)), "New worker never entered its queue callback.");

                releaseOldQueue.Set();
                await ObserveRetiredRequestAsync(oldRequest);
                Assert.True(controller.TryDrainLatest(out var drained));
                Assert.Equal(2, drained.Sequence);
                Assert.Equal(3_000_000_004UL, drained.TimeNs);
            }
            finally
            {
                releaseOldQueue.Set();
                if (oldRequest != null)
                {
                    await ObserveRetiredRequestAsync(oldRequest);
                }
                controller.Clear();
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
        public void ExplicitSameTimeSeekIsAcceptedAfterAnAdvance()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var advance = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 1,
                didSeek: false);
            var seek = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 2,
                didSeek: true);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(advance, replayEnabled: true, startNs: 0, endNs: 10_000_000_000UL, out _));
            Assert.True(controller.TryDrainLatest(out var drainedAdvance));
            Assert.False(drainedAdvance.DidSeek);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(seek, replayEnabled: true, startNs: 0, endNs: 10_000_000_000UL, out _));
            Assert.True(controller.TryDrainLatest(out var drainedSeek));
            Assert.True(drainedSeek.DidSeek);
            Assert.Equal(2, drainedSeek.Sequence);
        }

        [Fact]
        public void DisablingControllerClearsAlreadyQueuedRequest()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var request = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 1,
                didSeek: false);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            controller.Enabled = false;

            Assert.False(controller.TryDrainLatest(out _));
        }

        [Fact]
        public async Task AtomicDrainAppliesBeforeDisableCanClearAuthority()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var request = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 1,
                didSeek: true);
            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            using var callbackEntered = new ManualResetEventSlim();
            using var releaseCallback = new ManualResetEventSlim();
            var appliedWhileEnabled = false;
            var drain = Task.Run(() => controller.TryDrainLatest(drained =>
            {
                appliedWhileEnabled = controller.Enabled;
                callbackEntered.Set();
                releaseCallback.Wait(TimeSpan.FromSeconds(5));
                Assert.Equal(1, drained.Sequence);
            }));

            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
            var disableStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var disable = Task.Run(() =>
            {
                disableStarted.SetResult(true);
                controller.Enabled = false;
            });
            await disableStarted.Task;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.False(disable.IsCompleted);
            releaseCallback.Set();
            Assert.True(await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(5))) == drain);
            await drain;
            await disable;
            Assert.True(appliedWhileEnabled);
            Assert.False(controller.TryDrainLatest(out _));
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

        private static Task<HttpStatusCode> PostCursorAsync(int port, string path, string bearerToken)
            => PostCursorAsync(port, path, bearerToken, CursorJson);

        private static async Task<HttpStatusCode> PostCursorAsync(
            int port,
            string path,
            string bearerToken,
            string json)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"http://127.0.0.1:{port}{path}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                bearerToken);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(request);
            return response.StatusCode;
        }

        private static string CursorJsonFor(long sequence, long sec, int nsec)
            => "{\"source\":\"phase187\",\"sequence\":"
               + sequence
               + ",\"mode\":\"seek\",\"time\":{\"sec\":"
               + sec
               + ",\"nsec\":"
               + nsec
               + "}}";

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
