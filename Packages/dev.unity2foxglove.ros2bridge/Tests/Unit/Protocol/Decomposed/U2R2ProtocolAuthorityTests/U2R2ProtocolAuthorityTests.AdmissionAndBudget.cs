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
        private static void BoundedGenerationTombstones(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(("maxTombstones", 1UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var authority = new U2R2ContractAuthority(
                bounded,
                DefaultSemanticErrorFrame);
            var replay = new U2R2RequestReplayAuthority(bounded);
            var first = new U2R2ContractKey(
                scenario.Value<ulong>("firstContractId"),
                scenario.Value<ulong>("generation"));
            var second = new U2R2ContractKey(
                scenario.Value<ulong>("secondContractId"),
                scenario.Value<ulong>("generation"));
            RegisterAndRemove(authority, scheduler, replay, first, 1);
            RegisterAndRemove(authority, scheduler, replay, second, 3);
            Assert.Equal(1UL, authority.TombstoneCount);
            Assert.Equal(
                scenario.Value<ulong>("expectedRevokedContracts"),
                scheduler.RevokedContractCount);
            AssertProtocolError(scenario, () => authority.AdmitMessage(Identity(first), 1));
            Assert.Equal(
                U2R2MessageAdmission.LateTombstone,
                authority.AdmitMessage(Identity(second), 1));
        }

        private static void UnknownContractFaults(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var authority = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            AssertProtocolError(
                scenario,
                () => authority.AdmitMessage(Identity(Key(scenario)), 1));
        }

        private static void SequenceStartsOneAndIsMonotonic(JObject scenario)
        {
            var sequence = new U2R2ContractSequence();
            foreach (var accepted in scenario["acceptedSequences"].Values<ulong>())
                sequence.Admit(accepted);
            Assert.Equal(2UL, sequence.LastAccepted);
            AssertProtocolError(
                scenario,
                () => sequence.Admit(scenario.Value<ulong>("rejectedSequence")));
        }

        private static void SequenceFaultsBeforeWrap(JObject scenario)
        {
            var sequence = new U2R2ContractSequence(
                ParseUlong(scenario["startingSequence"]));
            sequence.Admit(ParseUlong(scenario["lastAcceptedSequence"]));
            AssertProtocolError(scenario, () => sequence.Admit(0));
            Assert.True(sequence.IsFaulted);
        }

        private static void ContractLocalOverflow(
            JObject scenario,
            U2R2ProtocolLimits limits,
            U2R2QueueOverflowPolicy policy)
        {
            var bounded = limits.With(
                ("fixedFrameBytes", 1UL),
                ("maxHeaderBytes", 1UL),
                ("maxPayloadBytes", 8UL),
                ("maxTransientBytes", 16UL),
                ("maxInFlightBytes", 16UL),
                ("maxPerContractQueueDepth", 2UL),
                ("maxPerContractQueueBytes", 16UL),
                ("maxTotalQueueDepth", 8UL),
                ("maxQueuedBytes", 128UL),
                ("reservedControlQueueDepth", 2UL),
                ("reservedControlQueueBytes", 16UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var cold = new U2R2ContractKey(1, 1);
            var hot = new U2R2ContractKey(2, 1);
            scheduler.EnqueueData(
                U2R2OutboundFrame.Data("cold-1", cold, 1, Bytes("01")),
                U2R2QueueOverflowPolicy.Reject);
            scheduler.EnqueueData(
                U2R2OutboundFrame.Data("hot-1", hot, 1, Bytes("02")),
                policy);
            scheduler.EnqueueData(
                U2R2OutboundFrame.Data("hot-2", hot, 2, Bytes("03")),
                policy);
            var result = scheduler.EnqueueData(
                U2R2OutboundFrame.Data("hot-3", hot, 3, Bytes("04")),
                policy);
            Assert.Equal(ParseEnqueueResult(scenario.Value<string>("expectedResult")), result);
            var drained = DrainAll(scheduler);
            Assert.Contains("cold-1", drained);
            var expectedHot = scenario["retainedHotTokens"].Values<string>().ToArray();
            Assert.Equal(expectedHot, drained.Where(value => value.StartsWith("hot-", StringComparison.Ordinal)));
        }

        private static void PerContractFifoRoundRobin(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var a = new U2R2ContractKey(1, 1);
            var b = new U2R2ContractKey(2, 1);
            scheduler.EnqueueData(U2R2OutboundFrame.Data("a-1", a, 1, Bytes("01")), U2R2QueueOverflowPolicy.Reject);
            scheduler.EnqueueData(U2R2OutboundFrame.Data("a-2", a, 2, Bytes("02")), U2R2QueueOverflowPolicy.Reject);
            scheduler.EnqueueData(U2R2OutboundFrame.Data("b-1", b, 1, Bytes("03")), U2R2QueueOverflowPolicy.Reject);
            scheduler.EnqueueData(U2R2OutboundFrame.Data("b-2", b, 2, Bytes("04")), U2R2QueueOverflowPolicy.Reject);
            Assert.Equal(scenario["expectedOrder"].Values<string>(), DrainAll(scheduler));
        }

        private static void ZeroByteReplaceReleasesDepth(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(
                limits.With(("maxPerContractQueueDepth", 1UL)));
            var key = new U2R2ContractKey(1, 1);
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "zero-byte-victim",
                        key,
                        1,
                        Array.Empty<byte>()),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(
                ParseEnqueueResult(scenario.Value<string>("expectedResult")),
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "replacement",
                        key,
                        2,
                        Bytes("01")),
                    U2R2QueueOverflowPolicy.ReplaceLatest));
            Assert.Equal(
                scenario.Value<ulong>("expectedQueuedDepth"),
                scheduler.DataQueuedDepth);
            Assert.Equal(
                scenario.Value<ulong>("expectedQueuedBytes"),
                scheduler.QueuedBytes);
            Assert.Equal(
                scenario["expectedOrder"].Values<string>(),
                DrainAll(scheduler));
        }

        private static void BoundedControlPriorityAllowsData(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(
                limits.With(("controlBurstLimit", 2UL)));
            var key = new U2R2ContractKey(1, 1);
            scheduler.EnqueueData(U2R2OutboundFrame.Data("data-1", key, 1, Bytes("01")), U2R2QueueOverflowPolicy.Reject);
            EnqueueControl(scheduler, "control-1");
            EnqueueControl(scheduler, "control-2");
            EnqueueControl(scheduler, "control-3");
            Assert.Equal(scenario["expectedOrder"].Values<string>(), DrainAll(scheduler));
        }

        private static void FencedControlYieldsToOtherContractData(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(("controlBurstLimit", 1UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            EnqueueControl(scheduler, "control-prime");
            Assert.Equal("control-prime", DrainOne(scheduler).Token);

            var replay = new U2R2RequestReplayAuthority(bounded);
            var contracts = new U2R2ContractAuthority(
                bounded,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var response = replay.Admit(
                scenario.Value<ulong>("requestId"),
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                response);
            contracts.CommitReady(
                registration,
                replay,
                response,
                U2R2OutboundFrame.Control(
                    "subscription_ready",
                    Bytes("01")));

            var other = new U2R2ContractKey(
                identity.Key.ContractId + 1,
                identity.Key.Generation);
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "same-data",
                        identity.Key,
                        1,
                        Bytes("01")),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "other-data",
                        other,
                        1,
                        Bytes("02")),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(
                scenario["expectedOrder"].Values<string>(),
                DrainAll(scheduler));
        }

        private static void ReservedControlSurvivesFullDataBudget(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("fixedFrameBytes", 1UL),
                ("maxHeaderBytes", 1UL),
                ("maxPayloadBytes", 8UL),
                ("maxTransientBytes", 16UL),
                ("maxInFlightBytes", 16UL),
                ("maxPerContractQueueDepth", 2UL),
                ("maxPerContractQueueBytes", 16UL),
                ("maxTotalQueueDepth", 3UL),
                ("maxQueuedBytes", 24UL),
                ("reservedControlQueueDepth", 1UL),
                ("reservedControlQueueBytes", 8UL),
                ("controlBurstLimit", 1UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var key = new U2R2ContractKey(1, 1);
            var tokens = scenario["dataTokens"].Values<string>().ToArray();
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(tokens[0], key, 1, new byte[8]),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(tokens[1], key, 2, new byte[8]),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(
                U2R2EnqueueDisposition.Rejected,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data("data-overflow", key, 3, new byte[1]),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.Equal(
                scenario.Value<bool>("expectedControlReserved"),
                scheduler.TryReserveControl(8, out var reservation));
            reservation.Commit(
                U2R2OutboundFrame.Control(
                    scenario.Value<string>("controlToken"),
                    new byte[8]));
        }

        private static void QueuedWriterAccountingExact(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var key = new U2R2ContractKey(1, 1);
            var frameBytes = scenario.Value<int>("frameBytes");
            scheduler.EnqueueData(
                U2R2OutboundFrame.Data("frame", key, 1, new byte[frameBytes]),
                U2R2QueueOverflowPolicy.Reject);
            Assert.Equal(
                scenario.Value<ulong>("expectedQueuedBeforeWrite"),
                scheduler.QueuedBytes);
            Assert.True(scheduler.TryBeginWrite(out var writer));
            Assert.Equal(
                scenario.Value<ulong>("expectedQueuedDuringWrite"),
                scheduler.QueuedBytes);
            Assert.Equal(
                scenario.Value<ulong>("expectedInFlightDuringWrite"),
                scheduler.InFlightBytes);
            writer.Dispose();
            writer.Dispose();
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.QueuedBytes);
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.InFlightBytes);
        }

        private static void ByteReservationsReleaseExactlyOnce(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            Assert.True(scheduler.TryReserveTransient(8, out var transient));
            Assert.True(scheduler.TryBeginRead(16, out var reader));
            transient.Dispose();
            transient.Dispose();
            reader.Dispose();
            reader.Dispose();
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.TransientBytes);
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.InFlightBytes);
        }

        private static void ConcurrentLeaseSettlementExactlyOnce(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var workers = scenario.Value<int>("workerCount");
            var iterations = scenario.Value<int>("iterations");
            Assert.True(scheduler.TryReserveTransient(3, out var transientSentinel));
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                Assert.True(scheduler.TryReserveTransient(1, out var transient));
                Race(workers, transient.Dispose);
                Assert.Equal(3UL, scheduler.TransientBytes);
            }
            transientSentinel.Dispose();
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.TransientBytes);

            Assert.True(scheduler.TryBeginRead(8, out var reader));
            Race(workers, reader.Dispose);
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.InFlightBytes);

            var key = new U2R2ContractKey(1, 1);
            Assert.True(scheduler.TryBeginRead(3, out var inFlightSentinel));
            scheduler.EnqueueData(
                U2R2OutboundFrame.Data("writer", key, 1, new byte[8]),
                U2R2QueueOverflowPolicy.Reject);
            Assert.True(scheduler.TryBeginWrite(out var writer));
            Race(workers, writer.Dispose);
            Assert.Equal(3UL, scheduler.InFlightBytes);
            inFlightSentinel.Dispose();
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.InFlightBytes);

            var resources = new U2R2SessionResourceAuthority(limits);
            Assert.True(resources.TryAcquire(U2R2ConnectionRole.Probe, out var resourceSentinel));
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                Assert.True(resources.TryAcquire(U2R2ConnectionRole.DataSession, out var resource));
                Race(workers, resource.Dispose);
                Assert.Equal(1UL, resources.ConnectionCount);
            }
            resourceSentinel.Dispose();
            Assert.Equal(
                scenario.Value<ulong>("expectedFinalConnections"),
                resources.ConnectionCount);

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                Assert.True(scheduler.TryReserveControl(1, out var sentinel));
                sentinel.Commit(
                    U2R2OutboundFrame.Control("sentinel", Bytes("01")));
                Assert.True(scheduler.TryReserveControl(1, out var control));
                var commitWins = 0;
                var cancelWins = 0;
                RaceTwo(
                    () =>
                    {
                        if (control.TryCommit(
                                U2R2OutboundFrame.Control("race", Bytes("01"))))
                        {
                            Interlocked.Increment(ref commitWins);
                        }
                    },
                    () =>
                    {
                        if (control.TryCancel())
                            Interlocked.Increment(ref cancelWins);
                    });
                Assert.Equal(1, commitWins + cancelWins);
                var drained = DrainAll(scheduler);
                Assert.Equal(1, drained.Count(token => token == "sentinel"));
                Assert.Equal(commitWins, drained.Count(token => token == "race"));
                Assert.Equal(0UL, scheduler.QueuedBytes);
                Assert.Equal(0UL, scheduler.InFlightBytes);
            }

            Assert.True(scheduler.TryReserveControl(1, out var invalid));
            Assert.Throws<ArgumentException>(
                () => invalid.Commit(
                    U2R2OutboundFrame.Data("not-control", key, 2, Bytes("01"))));
            Assert.True(invalid.TryCancel());
            Assert.Equal(scenario.Value<ulong>("expectedFinalBytes"), scheduler.QueuedBytes);
        }

    }
}
