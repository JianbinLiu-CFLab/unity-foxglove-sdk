// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Tests/Protocol
// Purpose: Cross-language authority for bounded U2R2 replay, ordering, and budgets.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests.Unit.Protocol
{
    public sealed partial class U2R2ProtocolAuthorityTests
    {
        private static void Race(int workerCount, Action action)
        {
            using var barrier = new Barrier(workerCount + 1);
            var tasks = Enumerable.Range(0, workerCount)
                .Select(
                    _ => Task.Run(
                        () =>
                        {
                            barrier.SignalAndWait();
                            action();
                        }))
                .ToArray();
            barrier.SignalAndWait();
            Task.WaitAll(tasks);
        }

        private static void RaceOnDedicatedThreads(int workerCount, Action action)
        {
            using var barrier = new Barrier(workerCount + 1);
            var tasks = Enumerable.Range(0, workerCount)
                .Select(
                    _ => Task.Factory.StartNew(
                        () =>
                        {
                            barrier.SignalAndWait();
                            action();
                        },
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default))
                .ToArray();
            barrier.SignalAndWait();
            Task.WaitAll(tasks);
        }

        private static void OneReaderAndOneWriter(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            Assert.True(scheduler.TryBeginRead(1, out var reader));
            Assert.False(scheduler.TryBeginRead(1, out _));
            EnqueueControl(scheduler, "control");
            Assert.True(scheduler.TryBeginWrite(out var writer));
            Assert.False(scheduler.TryBeginWrite(out _));
            Assert.Equal(1, scenario.Value<int>("expectedConcurrentReaders"));
            Assert.Equal(1, scenario.Value<int>("expectedConcurrentWriters"));
            reader.Dispose();
            writer.Dispose();
        }

        private static void CapacityCounterMaxPlusOne(JObject scenario)
        {
            var counter = new U2R2CapacityCounter(scenario.Value<ulong>("capacity"));
            Assert.True(counter.TryAcquire());
            Assert.True(counter.TryAcquire());
            Assert.False(counter.TryAcquire());
            counter.Release();
            counter.Release();
            Assert.Equal(scenario.Value<ulong>("expectedFinalCount"), counter.Count);
            Assert.Throws<InvalidOperationException>(() => counter.Release());
        }

        private static void CheckedFrameSizeBounds(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var maximum = U2R2FrameSize.Create(
                limits,
                limits.MaxHeaderBytes,
                limits.MaxPayloadBytes);
            Assert.Equal(
                limits.FixedFrameBytes
                + limits.MaxHeaderBytes
                + limits.MaxPayloadBytes,
                maximum.TotalBytes);
            Assert.True(limits.MaxPerContractQueueBytes >= maximum.TotalBytes);
            Assert.True(limits.MaxQueuedBytes >= maximum.TotalBytes);
            Assert.True(limits.MaxTransientBytes >= maximum.TotalBytes);
            Assert.True(limits.MaxInFlightBytes >= maximum.TotalBytes);
            AssertProtocolError(
                scenario,
                () => U2R2FrameSize.Create(
                    limits,
                    limits.MaxHeaderBytes,
                    limits.MaxPayloadBytes + 1));
            AssertProtocolError(
                scenario,
                () => U2R2CheckedArithmetic.Add(
                    ulong.MaxValue,
                    1,
                    ulong.MaxValue,
                    "overflow"));
        }

        private static void RequestCounterExhaustsBeforeWrap(JObject scenario)
        {
            var counter = new U2R2RequestIdCounter(
                ParseUlong(scenario["startingRequestId"]));
            Assert.Equal(ParseUlong(scenario["lastRequestId"]), counter.Next());
            AssertProtocolError(scenario, () => counter.Next());
            Assert.True(counter.IsFaulted);
        }

        private static void RequestHighWaterFaultsBeforeSaturation(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            AssertProtocolError(
                scenario,
                () => replay.Admit(
                    ParseUlong(scenario["requestId"]),
                    Bytes("01"),
                    1,
                    scheduler));
            Assert.Equal(
                scenario.Value<ulong>("expectedHighWater"),
                replay.HighWaterMark);
            Assert.Equal(0UL, replay.OutstandingRequests);
        }

        private static void RequestCounterIsThreadSafe(JObject scenario)
        {
            var workers = scenario.Value<int>("workerCount");
            var iterations = scenario.Value<int>("iterationsPerWorker");
            var expectedUnique = scenario.Value<int>("expectedUniqueIds");
            var counter = new U2R2RequestIdCounter();
            var ids = new ulong[expectedUnique];
            var nextIndex = -1;

            RaceOnDedicatedThreads(
                workers,
                () =>
                {
                    for (var iteration = 0; iteration < iterations; iteration++)
                    {
                        var index = Interlocked.Increment(ref nextIndex);
                        ids[index] = counter.Next();
                    }
                });

            Assert.Equal(expectedUnique, Volatile.Read(ref nextIndex) + 1);
            Assert.Equal(expectedUnique, ids.Distinct().Count());
            Assert.Equal(
                scenario.Value<ulong>("expectedFirstId"),
                ids.Min());
            Assert.Equal(
                scenario.Value<ulong>("expectedLastId"),
                ids.Max());
        }

        private static void WrongGenerationIsNotATombstone(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var authority = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var replay = new U2R2RequestReplayAuthority(limits);
            var removed = new U2R2ContractKey(
                scenario.Value<ulong>("contractId"),
                scenario.Value<ulong>("removedGeneration"));
            RegisterAndRemove(authority, scheduler, replay, removed, 1);
            var wrong = new U2R2ContractKey(
                scenario.Value<ulong>("contractId"),
                scenario.Value<ulong>("wrongGeneration"));
            AssertProtocolError(
                scenario,
                () => authority.AdmitMessage(Identity(wrong), 1));
        }

        private static void FailedReservationHasNoSideEffects(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("reservedControlQueueDepth", 1UL),
                ("reservedControlQueueBytes", 1UL),
                ("controlBurstLimit", 1UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            EnqueueControl(scheduler, "occupied");
            var replay = new U2R2RequestReplayAuthority(bounded);
            var mutations = 0;
            AssertProtocolError(
                scenario,
                () =>
                {
                    var admission = replay.Admit(
                        scenario.Value<ulong>("requestId"),
                        Bytes("01"),
                        1,
                        scheduler);
                    if (admission.Decision == U2R2ReplayDecision.BeginMutation)
                        mutations++;
                });
            Assert.Equal(scenario.Value<ulong>("expectedHighWater"), replay.HighWaterMark);
            Assert.Equal(scenario.Value<int>("expectedMutationCount"), mutations);
        }

        private static void ReplayAdvancesHighWaterOnce(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var request = Bytes(scenario.Value<string>("canonicalRequestHex"));
            var response = Bytes(scenario.Value<string>("responseHex"));
            var mutations = 0;
            var admission = replay.Admit(
                scenario.Value<ulong>("requestId"),
                request,
                (ulong)response.Length,
                scheduler);
            if (admission.Decision == U2R2ReplayDecision.BeginMutation)
                mutations++;
            replay.Complete(admission, response);
            DrainOne(scheduler);
            var repeated = replay.Admit(
                scenario.Value<ulong>("requestId"),
                request,
                (ulong)response.Length,
                scheduler);
            Assert.Equal(ParseReplayDecision(scenario.Value<string>("expectedDecision")), repeated.Decision);
            Assert.Equal(response, repeated.CachedResponse.ToArray());
            Assert.Equal(scenario.Value<ulong>("expectedHighWater"), replay.HighWaterMark);
            Assert.Equal(scenario.Value<int>("expectedMutationCount"), mutations);
        }

        private static void PendingRequestIdentityIsAtomic(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var request = Bytes(scenario.Value<string>("canonicalRequestHex"));
            var pending = replay.Admit(
                scenario.Value<ulong>("requestId"),
                request,
                3,
                scheduler);
            Assert.Equal(U2R2ReplayDecision.BeginMutation, pending.Decision);
            Assert.Equal(scenario.Value<ulong>("expectedHighWater"), replay.HighWaterMark);
            AssertProtocolError(
                Assert.IsType<JObject>(scenario["identicalPending"]),
                () => replay.Admit(
                    scenario.Value<ulong>("requestId"),
                    request,
                    3,
                    scheduler));
            AssertProtocolError(
                Assert.IsType<JObject>(scenario["conflictingPending"]),
                () => replay.Admit(
                    scenario.Value<ulong>("requestId"),
                    Bytes(scenario.Value<string>("conflictingRequestHex")),
                    3,
                    scheduler));
            AssertProtocolError(
                Assert.IsType<JObject>(scenario["lowerPending"]),
                () => replay.Admit(
                    scenario.Value<ulong>("lowerRequestId"),
                    Bytes("06"),
                    1,
                    scheduler));
            var higher = replay.Admit(
                scenario.Value<ulong>("higherRequestId"),
                Bytes("08"),
                1,
                scheduler);
            Assert.Equal(U2R2ReplayDecision.BeginMutation, higher.Decision);
        }

        private static void ReplayCompletionAbortExactlyOnce(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var request = Bytes(scenario.Value<string>("canonicalRequestHex"));
            var completeResponse = Bytes(scenario.Value<string>("completeResponseHex"));
            var abortResponse = Bytes(scenario.Value<string>("abortResponseHex"));
            var mutationCount = 0;
            var completed = replay.Admit(
                scenario.Value<ulong>("completeRequestId"),
                request,
                (ulong)completeResponse.Length,
                scheduler);
            mutationCount++;
            replay.Complete(completed, completeResponse);
            Assert.Throws<InvalidOperationException>(
                () => replay.Complete(completed, completeResponse));
            DrainOne(scheduler);
            var aborted = replay.Admit(
                scenario.Value<ulong>("abortRequestId"),
                request,
                (ulong)abortResponse.Length,
                scheduler);
            mutationCount++;
            replay.Abort(aborted, abortResponse);
            Assert.Throws<InvalidOperationException>(
                () => replay.Abort(aborted, abortResponse));
            DrainOne(scheduler);
            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingRequests"),
                replay.OutstandingRequests);
            Assert.Equal(scenario.Value<int>("expectedMutationCount"), mutationCount);
            var replayedAbort = replay.Admit(
                scenario.Value<ulong>("abortRequestId"),
                request,
                (ulong)abortResponse.Length,
                scheduler);
            Assert.Equal(U2R2ReplayDecision.ReplayCached, replayedAbort.Decision);
            Assert.Equal(abortResponse, replayedAbort.CachedResponse.ToArray());
        }

    }
}
