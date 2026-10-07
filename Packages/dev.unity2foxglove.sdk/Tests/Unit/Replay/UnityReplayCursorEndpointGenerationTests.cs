// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Replay
// Purpose: Locks replay cursor endpoint worker generations across restart.

using System;
using System.Collections.Generic;
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
    public sealed partial class UnityReplayCursorEndpointGenerationTests
    {
        private const string CursorJson =
            "{\"source\":\"phase187\",\"sequence\":1,\"mode\":\"seek\",\"time\":{\"sec\":2,\"nsec\":3}}";

        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        [Fact]
        public async Task DuplicateCursorResultUsesConflictStatus()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            var port = StartEndpointWithRetry(
                endpoint,
                "/duplicate",
                "duplicate-token",
                _ => new UnityReplayCursorEndpointQueueResult(true, "Duplicate cursor ignored."));

            Assert.Equal(
                HttpStatusCode.Conflict,
                await PostCursorAsync(port, "/duplicate", "duplicate-token"));
        }

        [Fact]
        public async Task RestartDoesNotPublishTheNextListenerBeforeTheOldWorkerRetires()
        {
            using var endpoint = new UnityReplayCursorEndpoint();
            using var oldQueueEntered = new ManualResetEventSlim();
            using var releaseOldQueue = new ManualResetEventSlim();
            var oldQueueCalls = 0;
            var newQueueCalls = 0;
            var newPort = 0;

            var oldPort = StartEndpointWithRetry(
                endpoint,
                "/old",
                "old-token",
                _ =>
                {
                    Interlocked.Increment(ref oldQueueCalls);
                    oldQueueEntered.Set();
                    releaseOldQueue.Wait(TimeSpan.FromSeconds(5));
                    return new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted.");
                });

            var oldRequest = PostCursorAsync(oldPort, "/old", "old-token");
            Assert.True(oldQueueEntered.Wait(TimeSpan.FromSeconds(5)), "Old worker never entered its queue callback.");

            var restart = Task.Run(() => newPort = StartEndpointWithRetry(
                endpoint,
                "/new",
                "new-token",
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
            var newPort = 0;
            Task<HttpStatusCode> oldRequest = null;

            try
            {
                var oldPort = StartEndpointWithRetry(
                    endpoint,
                    "/old-timeout",
                    "old-timeout-token",
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

                var restart = Task.Run(() => newPort = StartEndpointWithRetry(
                    endpoint,
                    "/new-timeout",
                    "new-timeout-token",
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
            Task<HttpStatusCode> firstRequest = null;
            Task<HttpStatusCode> secondRequest = null;

            try
            {
                var firstPort = StartEndpointWithRetry(
                    endpoint,
                    "/retire-first",
                    "retire-first-token",
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

                var secondPort = StartEndpointWithRetry(
                    endpoint,
                    "/retire-second",
                    "retire-second-token",
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

                var thirdPort = StartEndpointWithRetry(
                    endpoint,
                    "/retire-third",
                    "retire-third-token",
                    _ => new UnityReplayCursorEndpointQueueResult(true, "Cursor accepted."));

                Assert.Equal(1, endpoint.RetiringGenerationCount);

                releaseSecondQueue.Set();
                await ObserveRetiredRequestAsync(secondRequest);
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    await PostCursorAsync(thirdPort, "/retire-third", "retire-third-token"));

                releaseFirstQueue.Set();
                await ObserveRetiredRequestAsync(firstRequest);
                for (var i = 0; i < 200 && endpoint.RetiringGenerationCount != 0; i++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25));
                }
                Assert.Equal(0, endpoint.RetiringGenerationCount);
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
            Task<HttpStatusCode> oldRequest = null;

            try
            {
                var oldPort = StartEndpointWithRetry(
                    endpoint,
                    "/authority-old",
                    "authority-old-token",
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

                var newPort = StartEndpointWithRetry(
                    endpoint,
                    "/authority-new",
                    "authority-new-token",
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

    }
}
