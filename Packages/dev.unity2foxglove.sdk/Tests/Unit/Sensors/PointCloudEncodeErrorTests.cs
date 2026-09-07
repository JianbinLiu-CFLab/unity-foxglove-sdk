// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "187-R4-G03")]
    [Trait("Domain", "Sensors")]
    public sealed class PointCloudEncodeErrorTests
    {
        [Fact]
        public void WorkerEncodeErrorsAreReportedOnceOnTheOwningDrainThread()
        {
            using var recycled = new ManualResetEventSlim(false);
            var warnings = new ConcurrentQueue<WarningRecord>();
            var ownerThread = Environment.CurrentManagedThreadId;

            using var pipeline = new PointCloudEncodePipeline<TestRequest, TestResult>(
                "phase187-point-cloud-error",
                completedCapacity: 1,
                workerStopWaitMs: 5000,
                encode: _ => throw new InvalidOperationException("encode boom"),
                isSuccess: _ => true,
                failureMessage: _ => "result failure",
                formatFailureWarning: message => message,
                publishCompleted: _ => { },
                logWarning: message => warnings.Enqueue(new WarningRecord(
                    Environment.CurrentManagedThreadId,
                    message)),
                logDropDiagnostic: _ => { },
                replacedPendingWarning: "replaced",
                queueFailureMessagePrefix: "queue: ",
                droppedCompletedWarning: count => "dropped: " + count,
                workerShutdownWarning: "shutdown",
                failureWarningIntervalFrames: 1);

            pipeline.Queue(new TestRequest(recycled), logQosDrops: false, onPendingDrop: null);

            Assert.True(recycled.Wait(TimeSpan.FromSeconds(2)), "The throwing worker must recycle its request.");
            Assert.Empty(warnings);

            pipeline.Drain(logQosDrops: false, onDroppedCompleted: null, onResultsProcessed: null);
            Assert.Single(warnings);
            var warning = Assert.Single(warnings);
            Assert.Equal(ownerThread, warning.ThreadId);
            Assert.Equal("queue: encode boom", warning.Message);

            pipeline.Drain(logQosDrops: false, onDroppedCompleted: null, onResultsProcessed: null);
            Assert.Single(warnings);
        }

        private sealed class TestRequest : IPointCloudWorkerRequest
        {
            private readonly ManualResetEventSlim _recycled;

            public TestRequest(ManualResetEventSlim recycled)
            {
                _recycled = recycled;
            }

            public int Generation { get; set; }

            public void RecycleSourceSnapshot()
                => _recycled.Set();
        }

        private sealed class TestResult : IPointCloudWorkerResult<TestRequest>
        {
            public TestRequest Request { get; } = null;

            public void RecycleResultPayloads()
            {
            }
        }

        private readonly struct WarningRecord
        {
            public WarningRecord(int threadId, string message)
            {
                ThreadId = threadId;
                Message = message;
            }

            public int ThreadId { get; }
            public string Message { get; }
        }
    }
}
