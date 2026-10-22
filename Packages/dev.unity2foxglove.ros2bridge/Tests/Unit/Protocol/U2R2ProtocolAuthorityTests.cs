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
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class U2R2ProtocolAuthoritySerialCollection
    {
        public const string Name = "U2R2 protocol authority serial";
    }

    [Trait("Phase", "186-B")]
    [Trait("Domain", "U2R2ProtocolAuthority")]
    [Collection(U2R2ProtocolAuthoritySerialCollection.Name)]
    public sealed partial class U2R2ProtocolAuthorityTests
    {
        [Fact]
        public void ControlReservationsFailClosedWhenAccountingExceedsLimit()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            SetSchedulerCounter(
                scheduler,
                "_controlBytesUsed",
                checked(limits.ReservedControlQueueBytes + 1UL));

            Assert.False(scheduler.TryReserveControl(1UL, out var reservation));
            Assert.Null(reservation);
        }

        [Fact]
        public void TransientReservationsFailClosedWhenAccountingExceedsLimit()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            SetSchedulerCounter(
                scheduler,
                "_transientBytes",
                checked(limits.MaxTransientBytes + 1UL));

            Assert.False(scheduler.TryReserveTransient(1UL, out var lease));
            Assert.Null(lease);
        }

        [Fact]
        public void ReaderAdmissionsFailClosedWhenAccountingExceedsLimit()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            SetSchedulerCounter(
                scheduler,
                "_inFlightBytes",
                checked(limits.MaxInFlightBytes + 1UL));

            Assert.False(scheduler.TryBeginRead(1UL, out var lease));
            Assert.Null(lease);
        }

        [Fact]
        public void WriterAdmissionsFailClosedWhenAccountingExceedsLimit()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var key = new U2R2ContractKey(1UL, 1UL);
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data("writer", key, 1UL, Bytes("01")),
                    U2R2QueueOverflowPolicy.Reject));
            SetSchedulerCounter(
                scheduler,
                "_inFlightBytes",
                checked(limits.MaxInFlightBytes + 1UL));

            Assert.False(scheduler.TryBeginWrite(out var lease));
            Assert.Null(lease);
        }

        [Fact]
        public void SharedCommit2LedgerDrivesEveryBoundedAuthorityScenario()
        {
            var authority = LoadAuthority();
            var limitsJson = Assert.IsType<JObject>(authority["limits"]);
            var limits = LimitsFrom(limitsJson);
            var scenarios = Assert.IsType<JArray>(authority["scenarios"])
                .Values<JObject>()
                .ToArray();

            Assert.Equal(58, scenarios.Length);
            Assert.Equal(58, scenarios
                .Select(scenario => scenario.Value<string>("id"))
                .Distinct(StringComparer.Ordinal)
                .Count());

            foreach (var scenario in scenarios)
                RunScenario(scenario, limits, limitsJson);
        }

        [Fact]
        public void BoundReplayAdmissionRejectsContractIdentityMutation()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var requested = Identity(new U2R2ContractKey(41, 7));
            var mutated = Identity(new U2R2ContractKey(42, 7));
            var response = replay.AdmitContract(
                1,
                RequestBytes("register_subscription", requested),
                1,
                scheduler,
                U2R2Operation.RegisterSubscription,
                requested);

            Assert.Throws<InvalidOperationException>(
                () => contracts.BeginRegistration(
                    mutated,
                    scheduler,
                    replay,
                    response));
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(1UL, replay.OutstandingRequests);
            response.Dispose();
            Assert.Equal(0UL, replay.OutstandingRequests);
        }

        [Fact]
        public void CachedContractReplaySurvivesLegalEviction()
        {
            var limits = U2R2ProtocolLimits.Default.With(
                ("maxOutstandingRequests", 2UL),
                ("maxReplayEntries", 2UL),
                ("maxReplayBytes", 4096UL),
                ("reservedControlQueueDepth", 8UL),
                ("reservedControlQueueBytes", 4096UL));
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(new U2R2ContractKey(51, 1));

            var first = replay.AdmitContract(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler,
                U2R2Operation.RegisterSubscription,
                identity);
            replay.Complete(first, new byte[] { 0x11 });
            DrainOne(scheduler);

            var cached = replay.AdmitContract(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler,
                U2R2Operation.RegisterSubscription,
                identity);
            Assert.Equal(U2R2ReplayDecision.ReplayCached, cached.Decision);
            DrainOne(scheduler);

            for (ulong requestId = 2; requestId <= 3; requestId++)
            {
                var nextIdentity = Identity(new U2R2ContractKey(50 + requestId, 1));
                var next = replay.AdmitContract(
                    requestId,
                    RequestBytes("register_subscription", nextIdentity),
                    1,
                    scheduler,
                    U2R2Operation.RegisterSubscription,
                    nextIdentity);
                replay.Complete(next, new byte[] { (byte)(0x10 + requestId) });
                DrainOne(scheduler);
            }

            var replayedRegistration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                cached);
            Assert.True(replayedRegistration.Replayed);
            contracts.CommitReady(
                replayedRegistration,
                replay,
                cached,
                U2R2OutboundFrame.Control("subscription_ready:1", new byte[] { 0x11 }));
            cached.Dispose();
        }

        [Fact]
        public void CachedContractReplayIsRejectedForAnotherContractOperationOrScheduler()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var otherScheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(limits, DefaultSemanticErrorFrame);
            var otherContracts = new U2R2ContractAuthority(limits, DefaultSemanticErrorFrame);
            var identity = Identity(new U2R2ContractKey(51, 1));
            var otherIdentity = Identity(new U2R2ContractKey(52, 1));

            var first = replay.AdmitContract(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler,
                U2R2Operation.RegisterSubscription,
                identity);
            replay.Complete(first, new byte[] { 0x11 });
            DrainOne(scheduler);

            var cached = replay.AdmitContract(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler,
                U2R2Operation.RegisterSubscription,
                identity);
            Assert.Equal(U2R2ReplayDecision.ReplayCached, cached.Decision);
            DrainOne(scheduler);

            Assert.Throws<InvalidOperationException>(
                () => contracts.BeginRegistration(otherIdentity, scheduler, replay, cached));
            Assert.Throws<InvalidOperationException>(
                () => contracts.BeginUnregister(identity, scheduler, replay, cached));
            Assert.Throws<InvalidOperationException>(
                () => otherContracts.BeginRegistration(identity, otherScheduler, replay, cached));
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(0UL, otherContracts.ContractCount);

            var replayed = contracts.BeginRegistration(identity, scheduler, replay, cached);
            Assert.True(replayed.Replayed);
            contracts.CommitReady(
                replayed,
                replay,
                cached,
                U2R2OutboundFrame.Control("subscription_ready:1", new byte[] { 0x11 }));
            cached.Dispose();
        }

        [Fact]
        public void PendingContractClaimIsRejectedForAnotherOperation()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(limits, DefaultSemanticErrorFrame);
            var identity = Identity(new U2R2ContractKey(41, 7));
            var pending = replay.AdmitContract(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler,
                U2R2Operation.RegisterSubscription,
                identity);

            Assert.Throws<InvalidOperationException>(
                () => contracts.BeginUnregister(identity, scheduler, replay, pending));
            Assert.Equal(0UL, contracts.ContractCount);

            var registration = contracts.BeginRegistration(identity, scheduler, replay, pending);
            Assert.False(registration.Replayed);
            contracts.CommitReady(
                registration,
                replay,
                pending,
                U2R2OutboundFrame.Control("subscription_ready:1", new byte[] { 0x11 }));
            pending.Dispose();
        }

        [Fact]
        public void CancelledRequestIdStaysStaleAtTheHighWaterMark()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);

            var pending = replay.Admit(7, Bytes("06"), 1, scheduler);
            Assert.Equal(U2R2ReplayDecision.BeginMutation, pending.Decision);
            Assert.Equal(7UL, replay.HighWaterMark);
            replay.CancelPending(pending);

            var stale = Assert.Throws<U2R2ProtocolException>(
                () => replay.Admit(7, Bytes("06"), 1, scheduler));
            Assert.Equal("stale_request", stale.ErrorCode);
            Assert.Equal(0UL, replay.OutstandingRequests);
        }

        [Fact]
        public void DroppedAdmissionsRollbackEveryOwnedBoundedResource()
        {
            var limits = U2R2ProtocolLimits.Default;
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);

            using (replay.Admit(1, Bytes("01"), 1, scheduler))
            {
            }
            Assert.Equal(0UL, replay.OutstandingRequests);
            Assert.Equal(0UL, replay.ReplayBytes);
            Assert.Equal(0UL, scheduler.TotalQueuedDepth);

            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(new U2R2ContractKey(41, 7));
            var registerResponse = replay.Admit(
                2,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            using (contracts.BeginRegistration(
                       identity,
                       scheduler,
                       replay,
                       registerResponse))
            {
            }
            registerResponse.Dispose();
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(0UL, replay.OutstandingRequests);
            Assert.Equal(0UL, scheduler.TotalQueuedDepth);

            var readyResponse = replay.Admit(
                3,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            using (var registration = contracts.BeginRegistration(
                       identity,
                       scheduler,
                       replay,
                       readyResponse))
            {
                contracts.CommitReady(
                    registration,
                    replay,
                    readyResponse,
                    U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            }
            DrainOne(scheduler);

            var removeResponse = replay.Admit(
                4,
                RequestBytes("unregister_subscription", identity),
                1,
                scheduler);
            using (contracts.BeginUnregister(
                       identity,
                       scheduler,
                       replay,
                       removeResponse))
            {
            }
            removeResponse.Dispose();
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(0UL, replay.OutstandingRequests);
            Assert.Equal(0UL, scheduler.TotalQueuedDepth);
        }

    }
}
