// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Round4 G04 terminal admission and handle-lifecycle regressions.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "187-R2-G04")]
    [Trait("Domain", "BackgroundUtilities")]
    public sealed class G04BackgroundTerminalLifecycleTests
    {
        [Fact]
        public async Task DisposeRejectsSubmitThatPassedTheInitialGuard()
        {
            using var encodeEntered = new ManualResetEventSlim(false);
            using var releaseEncode = new ManualResetEventSlim(false);
            using var enqueueGuardReached = new ManualResetEventSlim(false);
            using var releaseEnqueueGuard = new ManualResetEventSlim(false);
            var encodedIds = new System.Collections.Concurrent.ConcurrentBag<int>();
            var enqueueCallCount = 0;
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "phase187-g04-terminal-admission",
                completedCapacity: 1,
                stopWaitMs: 0,
                encode: request =>
                {
                    encodedIds.Add(request.Id);
                    if (request.Id == 1)
                    {
                        encodeEntered.Set();
                        Assert.True(releaseEncode.Wait(TimeSpan.FromSeconds(5)));
                    }

                    return request.Id;
                });
            pipeline.TestHook = point =>
            {
                if (point != "EnqueueAfterDisposedCheck"
                    || Interlocked.Increment(ref enqueueCallCount) != 2)
                    return;

                enqueueGuardReached.Set();
                releaseEnqueueGuard.Wait(TimeSpan.FromSeconds(5));
            };

            Task<Exception> race = null;
            try
            {
                Assert.True(pipeline.Enqueue(new TestRequest(1), out _, out _));
                Assert.True(encodeEntered.Wait(TimeSpan.FromSeconds(5)));
                race = RunOnDedicatedThread(() => Record.Exception(() =>
                    pipeline.Enqueue(new TestRequest(2), out _, out _)));
                Assert.True(enqueueGuardReached.Wait(TimeSpan.FromSeconds(5)));

                pipeline.Dispose();
                releaseEnqueueGuard.Set();
                var raceException = await AwaitTask(race);
                releaseEncode.Set();

                Assert.True(
                    SpinWait.SpinUntil(() => !GetWorker(pipeline).IsRunning, TimeSpan.FromSeconds(3)),
                    "The original worker must retire after terminal disposal.");
                Assert.IsType<ObjectDisposedException>(raceException);
                Assert.DoesNotContain(2, encodedIds);
                Assert.Null(GetPending(pipeline));
                Assert.Equal(0, GetActiveWorkerCount(pipeline));
            }
            finally
            {
                releaseEnqueueGuard.Set();
                releaseEncode.Set();
                if (race != null)
                    await Task.WhenAny(race, Task.Delay(TimeSpan.FromSeconds(5)));
                ForceRetireForTest(pipeline);
                pipeline.Dispose();
            }
        }

        [Fact]
        public async Task DisposeDoesNotDisposeSignalWhileSubmitIsBeingAdmitted()
        {
            using var enqueueGuardReached = new ManualResetEventSlim(false);
            using var releaseEnqueueGuard = new ManualResetEventSlim(false);
            using var beforeSignalReached = new ManualResetEventSlim(false);
            using var releaseBeforeSignal = new ManualResetEventSlim(false);
            using var disposeBeforeHandleReached = new ManualResetEventSlim(false);
            using var releaseDisposeBeforeHandle = new ManualResetEventSlim(false);
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "phase187-g04-terminal-signal",
                completedCapacity: 1,
                stopWaitMs: 0,
                encode: request => request.Id);
            pipeline.TestHook = point =>
            {
                switch (point)
                {
                    case "EnqueueAfterDisposedCheck":
                        enqueueGuardReached.Set();
                        releaseEnqueueGuard.Wait(TimeSpan.FromSeconds(5));
                        break;
                    case "EnqueueBeforeSignal":
                        beforeSignalReached.Set();
                        releaseBeforeSignal.Wait(TimeSpan.FromSeconds(5));
                        break;
                    case "DisposeBeforeHandleDisposal":
                        disposeBeforeHandleReached.Set();
                        releaseDisposeBeforeHandle.Wait(TimeSpan.FromSeconds(5));
                        break;
                }
            };

            var enqueue = RunOnDedicatedThread(() => Record.Exception(() =>
                pipeline.Enqueue(new TestRequest(1), out _, out _)));
            Task<Exception> dispose = null;
            try
            {
                Assert.True(enqueueGuardReached.Wait(TimeSpan.FromSeconds(10)));
                dispose = RunOnDedicatedThread(() => Record.Exception(pipeline.Dispose));
                Assert.True(disposeBeforeHandleReached.Wait(TimeSpan.FromSeconds(10)));
                releaseEnqueueGuard.Set();

                var reachedSignal = beforeSignalReached.Wait(TimeSpan.FromSeconds(10));
                if (reachedSignal)
                {
                    Assert.True(
                        SpinWait.SpinUntil(() => IsDisposed(GetWorkerSignal(pipeline)), TimeSpan.FromSeconds(5)),
                        "Dispose must finish handle release before the admitted submit is allowed to signal.");
                }
                releaseDisposeBeforeHandle.Set();
                if (reachedSignal)
                    releaseBeforeSignal.Set();

                var enqueueException = await AwaitTask(enqueue);
                var disposeException = await AwaitTask(dispose);
                Assert.Null(disposeException);
                Assert.IsType<ObjectDisposedException>(enqueueException);
                Assert.Null(GetPending(pipeline));
                Assert.False(GetWorker(pipeline).IsRunning);
                Assert.Equal(0, GetActiveWorkerCount(pipeline));
            }
            finally
            {
                releaseEnqueueGuard.Set();
                releaseBeforeSignal.Set();
                releaseDisposeBeforeHandle.Set();
                await Task.WhenAll(
                    Task.WhenAny(enqueue, Task.Delay(TimeSpan.FromSeconds(5))),
                    dispose == null
                        ? Task.CompletedTask
                        : Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5))));
                ForceRetireForTest(pipeline);
                pipeline.Dispose();
            }
        }

        [Fact]
        public async Task ConcurrentDisposeCallsRemainIdempotent()
        {
            using var firstStopGuardReached = new ManualResetEventSlim(false);
            using var secondStopGuardReached = new ManualResetEventSlim(false);
            using var releaseFirstStopGuard = new ManualResetEventSlim(false);
            using var releaseSecondStopGuard = new ManualResetEventSlim(false);
            var stopGuardCount = 0;
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "phase187-g04-concurrent-dispose",
                completedCapacity: 1,
                stopWaitMs: 0,
                encode: request => request.Id);
            pipeline.TestHook = point =>
            {
                if (point != "StopAfterDisposedCheck")
                    return;

                switch (Interlocked.Increment(ref stopGuardCount))
                {
                    case 1:
                        firstStopGuardReached.Set();
                        releaseFirstStopGuard.Wait(TimeSpan.FromSeconds(5));
                        break;
                    case 2:
                        secondStopGuardReached.Set();
                        releaseSecondStopGuard.Wait(TimeSpan.FromSeconds(5));
                        break;
                }
            };

            var first = RunOnDedicatedThread(() => Record.Exception(pipeline.Dispose));
            Task<Exception> second = null;
            try
            {
                Assert.True(firstStopGuardReached.Wait(TimeSpan.FromSeconds(5)));
                second = RunOnDedicatedThread(() => Record.Exception(pipeline.Dispose));
                Assert.True(secondStopGuardReached.Wait(TimeSpan.FromSeconds(5)));
                releaseFirstStopGuard.Set();
                Assert.True(
                    SpinWait.SpinUntil(() => IsDisposed(GetWorkerSignal(pipeline)), TimeSpan.FromSeconds(5)),
                    "One dispose caller must be able to complete handle release while the other is parked.");
                releaseSecondStopGuard.Set();

                var firstException = await AwaitTask(first);
                var secondException = await AwaitTask(second);
                Assert.Null(firstException);
                Assert.Null(secondException);
            }
            finally
            {
                releaseFirstStopGuard.Set();
                releaseSecondStopGuard.Set();
                await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(5)));
                if (second != null)
                    await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));
                pipeline.Dispose();
            }
        }

        [Fact]
        public async Task EncodeErrorDiagnosticsRemainBounded()
        {
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "phase187-g04-error-bound",
                completedCapacity: 2,
                stopWaitMs: 5000,
                encode: _ => throw new InvalidOperationException("encode boom"));
            try
            {
                for (var i = 0; i < 8; i++)
                {
                    pipeline.Enqueue(new TestRequest(i), out _, out _);
                    var expected = Math.Min(i + 1, 2);
                    Assert.True(
                        SpinWait.SpinUntil(() => GetEncodeErrorCount(pipeline) >= expected, TimeSpan.FromSeconds(3)),
                        "The throwing worker must process each diagnostic request.");
                }

                Assert.InRange(GetEncodeErrorCount(pipeline), 0, 2);
            }
            finally
            {
                pipeline.Dispose();
            }
        }

        [Fact]
        public void EncodeErrorDiagnosticsStayExactlyAtTheConfiguredBound()
        {
            var observed = 0;
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "mseries-m3-b1-error-bound",
                completedCapacity: 2,
                stopWaitMs: 5000,
                encode: _ => throw new InvalidOperationException("encode boom"),
                onEncodeError: _ => Interlocked.Increment(ref observed));
            try
            {
                for (var i = 1; i <= 6; i++)
                {
                    pipeline.Enqueue(new TestRequest(i), out _, out _);
                    Assert.True(
                        SpinWait.SpinUntil(() => Volatile.Read(ref observed) >= i, TimeSpan.FromSeconds(5)),
                        "The throwing worker must report every encode failure.");
                }

                Assert.Equal(2, GetEncodeErrorCount(pipeline));

                var results = new List<int>();
                pipeline.Drain(results, out var dropped, out var encodeErrors);
                Assert.Empty(results);
                Assert.Equal(0, dropped);
                Assert.Equal(2, encodeErrors);
            }
            finally
            {
                pipeline.Dispose();
            }
        }

        [Fact]
        public void DroppedCompletedResultsAreCountedForTheDrainingConsumer()
        {
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "mseries-m3-b2-dropped-results",
                completedCapacity: 2,
                stopWaitMs: 5000,
                encode: request => request.Id);
            try
            {
                for (var i = 1; i <= 5; i++)
                {
                    var expectedDropped = Math.Max(0, i - 2);
                    pipeline.Enqueue(new TestRequest(i), out _, out _);
                    Assert.True(
                        SpinWait.SpinUntil(
                            () => GetCompletedCount(pipeline) == Math.Min(i, 2)
                                  && GetDroppedCompletedCount(pipeline) == expectedDropped,
                            TimeSpan.FromSeconds(5)),
                        "The worker must complete each request before the next one is admitted.");
                }

                var results = new List<int>();
                pipeline.Drain(results, out var dropped, out var encodeErrors);
                Assert.Equal(new[] { 4, 5 }, results);
                Assert.Equal(3, dropped);
                Assert.Equal(0, encodeErrors);
            }
            finally
            {
                pipeline.Dispose();
            }
        }

        [Fact]
        public void EncodeFailuresAfterStopAreNotReportedToTheNextDrain()
        {
            using var encodeEntered = new ManualResetEventSlim(false);
            using var releaseEncode = new ManualResetEventSlim(false);
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "mseries-m3-b5-stop-guard",
                completedCapacity: 2,
                stopWaitMs: 0,
                encode: _ =>
                {
                    encodeEntered.Set();
                    releaseEncode.Wait();
                    throw new InvalidOperationException("teardown boom");
                });
            try
            {
                Assert.True(pipeline.Enqueue(new TestRequest(1), out _, out _));
                Assert.True(encodeEntered.Wait(TimeSpan.FromSeconds(5)));

                var stop = Task.Run(() => pipeline.Stop(clearCompleted: true, out _));
                Assert.True(
                    SpinWait.SpinUntil(() => stop.IsCompleted, TimeSpan.FromSeconds(5)),
                    "Stop must not wait for the abandoned worker when stopWaitMs is zero.");

                releaseEncode.Set();
                Assert.True(
                    SpinWait.SpinUntil(() => GetActiveWorkerCount(pipeline) == 0, TimeSpan.FromSeconds(5)),
                    "The abandoned worker must exit after its encode throws.");
                Assert.Equal(0, GetEncodeErrorCount(pipeline));
            }
            finally
            {
                releaseEncode.Set();
                pipeline.Dispose();
            }
        }

        [Fact]
        public void StopTimeoutRejectsReplacementWorkerUntilOrphanRetires()
        {
            using var encodeEntered = new ManualResetEventSlim(false);
            using var releaseEncode = new ManualResetEventSlim(false);
            var encoded = 0;
            var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "phase191-single-worker-timeout",
                completedCapacity: 1,
                stopWaitMs: 0,
                encode: request =>
                {
                    Interlocked.Increment(ref encoded);
                    if (request.Id == 1)
                    {
                        encodeEntered.Set();
                        releaseEncode.Wait(TimeSpan.FromSeconds(5));
                    }

                    return request.Id;
                });
            try
            {
                Assert.True(pipeline.Enqueue(new TestRequest(1), out _, out _));
                Assert.True(encodeEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.False(pipeline.Stop(clearCompleted: true, out _));

                Assert.False(
                    pipeline.Enqueue(new TestRequest(2), out _, out var startError));
                Assert.Equal("Background encode worker is stopping.", startError);
                Assert.Equal(1, Volatile.Read(ref encoded));

                releaseEncode.Set();
                Assert.True(
                    SpinWait.SpinUntil(() => GetActiveWorkerCount(pipeline) == 0, TimeSpan.FromSeconds(3)));
                Assert.True(pipeline.Enqueue(new TestRequest(3), out _, out _));
            }
            finally
            {
                releaseEncode.Set();
                pipeline.Dispose();
            }
        }

        private static async Task<T> AwaitTask<T>(Task<T> task)
        {
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(task, completed);
            return await task;
        }

        private static Task<Exception> RunOnDedicatedThread(Func<Exception> action)
            => Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

        private static BackgroundWorkerLifecycle GetWorker(
            BackgroundEncodePipeline<TestRequest, int> pipeline)
            => (BackgroundWorkerLifecycle)typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(pipeline);

        private static System.Threading.AutoResetEvent GetWorkerSignal(
            BackgroundEncodePipeline<TestRequest, int> pipeline)
            => (System.Threading.AutoResetEvent)typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_workerSignal", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(pipeline);

        private static object GetPending(BackgroundEncodePipeline<TestRequest, int> pipeline)
            => typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(pipeline);

        private static int GetActiveWorkerCount(BackgroundEncodePipeline<TestRequest, int> pipeline)
            => (int)typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_activeWorkerCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(pipeline);

        private static int GetEncodeErrorCount(BackgroundEncodePipeline<TestRequest, int> pipeline)
            => ((System.Collections.Generic.Queue<string>)typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_encodeErrors", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(pipeline)).Count;

        private static int GetCompletedCount(BackgroundEncodePipeline<TestRequest, int> pipeline)
            => ((System.Collections.Generic.Queue<int>)typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_completed", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(pipeline)).Count;

        private static int GetDroppedCompletedCount(BackgroundEncodePipeline<TestRequest, int> pipeline)
            => (int)typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_droppedCompletedCount", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(pipeline);

        private static bool IsDisposed(WaitHandle handle)
            => Record.Exception(() => handle.WaitOne(0)) is ObjectDisposedException;

        private static void ForceRetireForTest(BackgroundEncodePipeline<TestRequest, int> pipeline)
        {
            var worker = GetWorker(pipeline);
            var pending = typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic);
            var active = typeof(BackgroundEncodePipeline<TestRequest, int>)
                .GetField("_activeWorkerCount", BindingFlags.Instance | BindingFlags.NonPublic);
            lock (worker.Gate)
            {
                worker.RequestStopLocked();
                pending.SetValue(pipeline, null);
                if (!worker.IsRunning)
                    active.SetValue(pipeline, 0);
            }

            try
            {
                GetWorkerSignal(pipeline).Set();
            }
            catch (ObjectDisposedException)
            {
            }

            SpinWait.SpinUntil(() => !worker.IsRunning, TimeSpan.FromSeconds(5));
        }

        private sealed class TestRequest : IBackgroundEncodeRequest
        {
            public TestRequest(int id)
            {
                Id = id;
            }

            public int Id { get; }
            public int Generation { get; set; }
        }
    }
}
