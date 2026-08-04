// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Background utility lifecycle and invalid-cadence regression coverage.

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Utilities")]
    public sealed class BackgroundUtilityLifecycleTests
    {
        [Fact]
        public async Task EnqueueRejectsRequestOwnedByStopRequestedGeneration()
        {
            using var encodeEntered = new ManualResetEventSlim(false);
            using var releaseEncode = new ManualResetEventSlim(false);
            var droppedRequests = new ConcurrentDictionary<int, int>();
            using var pipeline = new BackgroundEncodePipeline<TestRequest, int>(
                "phase187-background-utility",
                completedCapacity: 1,
                stopWaitMs: 5000,
                encode: request =>
                {
                    encodeEntered.Set();
                    Assert.True(releaseEncode.Wait(TimeSpan.FromSeconds(5)));
                    return request.Id;
                },
                onDropRequest: request => droppedRequests.AddOrUpdate(request.Id, 1, (_, count) => count + 1));

            Assert.True(pipeline.Enqueue(new TestRequest(1), out _, out _));
            Assert.True(encodeEntered.Wait(TimeSpan.FromSeconds(2)));

            var stopTask = Task.Run(() =>
            {
                var stopped = pipeline.Stop(clearCompleted: true, out var waitedForWorker);
                return (stopped, waitedForWorker);
            });

            try
            {
                Assert.True(
                    SpinWait.SpinUntil(() => GetWorker(pipeline).StopRequested, TimeSpan.FromSeconds(2)),
                    "Stop must close the active worker generation before the racing enqueue.");

                var accepted = pipeline.Enqueue(
                    new TestRequest(2),
                    out var replacedPending,
                    out var startError);

                releaseEncode.Set();
                var completedTask = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(3)));
                Assert.Same(stopTask, completedTask);
                var stopResult = await stopTask;
                Assert.True(stopResult.stopped);
                Assert.True(stopResult.waitedForWorker);
                Assert.False(accepted);
                Assert.False(replacedPending);
                Assert.Contains("stopping", startError, StringComparison.OrdinalIgnoreCase);
                Assert.True(droppedRequests.TryGetValue(2, out var dropCount));
                Assert.Equal(1, dropCount);
            }
            finally
            {
                releaseEncode.Set();
                await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5)));
            }
        }

        [Fact]
        public void SchedulerRejectsNonFiniteRateAndTimeWithoutPersistingPoisonedState()
        {
            foreach (var rateHz in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var state = PoisonedSchedule();
                Assert.False(FixedRatePublishScheduler.ShouldPublish(
                    nowSec: 10d,
                    rateHz,
                    ref state,
                    nonPositivePublishesEveryFrame: true));
                AssertReset(state);
            }

            foreach (var nowSec in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var state = PoisonedSchedule();
                Assert.False(FixedRatePublishScheduler.ShouldPublish(
                    nowSec,
                    rateHz: 20f,
                    ref state,
                    nonPositivePublishesEveryFrame: true));
                AssertReset(state);
            }
        }

        private static BackgroundWorkerLifecycle GetWorker(
            BackgroundEncodePipeline<TestRequest, int> pipeline)
        {
            var field = typeof(BackgroundEncodePipeline<TestRequest, int>).GetField(
                "_worker",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return Assert.IsType<BackgroundWorkerLifecycle>(field?.GetValue(pipeline));
        }

        private static FixedRatePublishState PoisonedSchedule()
            => new FixedRatePublishState
            {
                HasSchedule = true,
                LastRateHz = 42f,
                NextDueSec = 123d
            };

        private static void AssertReset(FixedRatePublishState state)
        {
            Assert.False(state.HasSchedule);
            Assert.Equal(0f, state.LastRateHz);
            Assert.Equal(0d, state.NextDueSec);
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
