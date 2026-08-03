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
    public sealed class U2R2ProtocolAuthorityTests
    {
        [Fact]
        public void SharedCommit2LedgerDrivesEveryBoundedAuthorityScenario()
        {
            var authority = LoadAuthority();
            var limitsJson = Assert.IsType<JObject>(authority["limits"]);
            var limits = LimitsFrom(limitsJson);
            var scenarios = Assert.IsType<JArray>(authority["scenarios"])
                .Values<JObject>()
                .ToArray();

            Assert.Equal(57, scenarios.Length);
            Assert.Equal(57, scenarios
                .Select(scenario => scenario.Value<string>("id"))
                .Distinct(StringComparer.Ordinal)
                .Count());

            foreach (var scenario in scenarios)
                RunScenario(scenario, limits, limitsJson);
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

        private static void RunScenario(
            JObject scenario,
            U2R2ProtocolLimits limits,
            JObject limitsJson)
        {
            switch (scenario.Value<string>("id"))
            {
                case "sender_starts_at_one":
                    SenderStartsAtOne(scenario);
                    return;
                case "receiver_accepts_higher_first":
                    ReceiverAcceptsHigherFirst(scenario, limits);
                    return;
                case "retained_identical_replay":
                    RetainedIdenticalReplay(scenario, limits);
                    return;
                case "retained_payload_conflict":
                    RetainedPayloadConflict(scenario, limits);
                    return;
                case "stale_after_replay_eviction":
                    StaleAfterReplayEviction(scenario, limits);
                    return;
                case "control_reserved_before_mutation":
                    ControlReservedBeforeMutation(scenario, limits);
                    return;
                case "replay_bytes_max_plus_one":
                    ReplayBytesMaxPlusOne(scenario, limits);
                    return;
                case "ready_precedes_message":
                    ReadyPrecedesMessage(scenario, limits);
                    return;
                case "unregister_fences_writer":
                    UnregisterFencesWriter(scenario, limits);
                    return;
                case "bounded_generation_tombstones":
                    BoundedGenerationTombstones(scenario, limits);
                    return;
                case "unknown_contract_faults":
                    UnknownContractFaults(scenario, limits);
                    return;
                case "sequence_starts_one_and_is_monotonic":
                    SequenceStartsOneAndIsMonotonic(scenario);
                    return;
                case "sequence_faults_before_wrap":
                    SequenceFaultsBeforeWrap(scenario);
                    return;
                case "drop_oldest_is_contract_local":
                    ContractLocalOverflow(scenario, limits, U2R2QueueOverflowPolicy.DropOldest);
                    return;
                case "replace_latest_is_contract_local":
                    ContractLocalOverflow(scenario, limits, U2R2QueueOverflowPolicy.ReplaceLatest);
                    return;
                case "zero_byte_replace_releases_depth":
                    ZeroByteReplaceReleasesDepth(scenario, limits);
                    return;
                case "per_contract_fifo_round_robin":
                    PerContractFifoRoundRobin(scenario, limits);
                    return;
                case "bounded_control_priority_allows_data":
                    BoundedControlPriorityAllowsData(scenario, limits);
                    return;
                case "fenced_control_yields_to_other_contract_data":
                    FencedControlYieldsToOtherContractData(scenario, limits);
                    return;
                case "reserved_control_survives_full_data_budget":
                    ReservedControlSurvivesFullDataBudget(scenario, limits);
                    return;
                case "queued_writer_accounting_exact":
                    QueuedWriterAccountingExact(scenario, limits);
                    return;
                case "byte_reservations_release_exactly_once":
                    ByteReservationsReleaseExactlyOnce(scenario, limits);
                    return;
                case "concurrent_lease_settlement_exactly_once":
                    ConcurrentLeaseSettlementExactlyOnce(scenario, limits);
                    return;
                case "terminal_close_cancels_pending_authorities":
                    TerminalCloseCancelsPendingAuthorities(scenario, limits);
                    return;
                case "terminal_close_rejects_wrong_authorities":
                    TerminalCloseRejectsWrongAuthorities(scenario, limits);
                    return;
                case "revoked_capacity_rejection_has_no_side_effects":
                    RevokedCapacityRejectionHasNoSideEffects(scenario, limits);
                    return;
                case "unregister_revoked_capacity_is_atomic":
                    UnregisterRevokedCapacityIsAtomic(scenario, limits);
                    return;
                case "one_reader_and_one_writer":
                    OneReaderAndOneWriter(scenario, limits);
                    return;
                case "capacity_counter_max_plus_one":
                    CapacityCounterMaxPlusOne(scenario);
                    return;
                case "checked_frame_size_bounds":
                    CheckedFrameSizeBounds(scenario, limits);
                    return;
                case "request_counter_exhausts_before_wrap":
                    RequestCounterExhaustsBeforeWrap(scenario);
                    return;
                case "request_high_water_faults_before_saturation":
                    RequestHighWaterFaultsBeforeSaturation(scenario, limits);
                    return;
                case "request_counter_is_thread_safe":
                    RequestCounterIsThreadSafe(scenario);
                    return;
                case "wrong_generation_is_not_a_tombstone":
                    WrongGenerationIsNotATombstone(scenario, limits);
                    return;
                case "failed_reservation_has_no_side_effects":
                    FailedReservationHasNoSideEffects(scenario, limits);
                    return;
                case "replay_advances_high_water_once":
                    ReplayAdvancesHighWaterOnce(scenario, limits);
                    return;
                case "pending_request_identity_is_atomic":
                    PendingRequestIdentityIsAtomic(scenario, limits);
                    return;
                case "replay_completion_abort_exactly_once":
                    ReplayCompletionAbortExactlyOnce(scenario, limits);
                    return;
                case "all_named_counters_are_bounded":
                    AllNamedCountersAreBounded(scenario, limits);
                    return;
                case "limits_diagnostic_snapshot_is_immutable":
                    LimitsDiagnosticSnapshotIsImmutable(scenario, limits, limitsJson);
                    return;
                case "limits_configuration_fails_closed":
                    LimitsConfigurationFailsClosed(scenario, limitsJson);
                    return;
                case "ready_unregister_full_ordering":
                    ReadyUnregisterFullOrdering(scenario, limits);
                    return;
                case "contract_identity_validation":
                    ContractIdentityValidation(scenario);
                    return;
                case "contract_identity_alias_and_replay":
                    ContractIdentityAliasAndReplay(scenario, limits);
                    return;
                case "fresh_registration_requires_subscribe_direction":
                    FreshRegistrationRequiresSubscribeDirection(scenario, limits);
                    return;
                case "message_requires_frozen_contract_identity":
                    MessageRequiresFrozenContractIdentity(scenario, limits);
                    return;
                case "composed_register_unregister_single_response":
                    ComposedRegisterUnregisterSingleResponse(scenario, limits);
                    return;
                case "fenced_response_fifo_and_transaction_binding":
                    FencedResponseFifoAndTransactionBinding(scenario, limits);
                    return;
                case "semantic_rejections_commit_exact_replay":
                    SemanticRejectionsCommitExactReplay(scenario, limits);
                    return;
                case "contract_claim_blocks_external_cancel":
                    ContractClaimBlocksExternalCancel(scenario, limits);
                    return;
                case "cached_replay_rejects_wrong_scheduler":
                    CachedReplayRejectsWrongScheduler(scenario, limits);
                    return;
                case "invalid_overflow_policy_has_no_side_effects":
                    InvalidOverflowPolicyHasNoSideEffects(scenario, limits);
                    return;
                case "replay_responses_respect_control_fairness":
                    ReplayResponsesRespectControlFairness(scenario, limits);
                    return;
                case "response_transaction_rejects_wrong_scheduler":
                    ResponseTransactionRejectsWrongScheduler(scenario, limits);
                    return;
                case "codec_consumes_session_limit_snapshot":
                    CodecConsumesSessionLimitSnapshot(scenario, limits);
                    return;
                case "pure_peer_close_transition":
                    PurePeerCloseTransition(scenario);
                    return;
                case "pure_timeout_transitions":
                    PureTimeoutTransitions(scenario);
                    return;
                default:
                    throw new InvalidOperationException(
                        "Unconsumed Commit2 fixture scenario: "
                        + scenario.Value<string>("id"));
            }
        }

        private static void SenderStartsAtOne(JObject scenario)
        {
            var counter = new U2R2RequestIdCounter();
            Assert.Equal(
                scenario["expectedIds"].Values<ulong>().ToArray(),
                new[] { counter.Next(), counter.Next() });
        }

        private static void ReceiverAcceptsHigherFirst(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var requestId = scenario.Value<ulong>("requestId");
            var admission = replay.Admit(
                requestId,
                Bytes("01"),
                1,
                scheduler);
            Assert.Equal(U2R2ReplayDecision.BeginMutation, admission.Decision);
            replay.Complete(admission, Bytes("aa"));
            Assert.Equal(scenario.Value<ulong>("expectedHighWater"), replay.HighWaterMark);
        }

        private static void RetainedIdenticalReplay(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var request = Bytes(scenario.Value<string>("canonicalRequestHex"));
            var response = Bytes(scenario.Value<string>("responseHex"));
            var first = replay.Admit(
                scenario.Value<ulong>("requestId"),
                request,
                (ulong)response.Length,
                scheduler);
            replay.Complete(first, response);
            DrainOne(scheduler);

            var repeated = replay.Admit(
                scenario.Value<ulong>("requestId"),
                request,
                (ulong)response.Length,
                scheduler);
            Assert.Equal(
                ParseReplayDecision(scenario.Value<string>("expectedDecision")),
                repeated.Decision);
            Assert.Equal(response, repeated.CachedResponse.ToArray());
            Assert.Equal(response, DrainOne(scheduler).Bytes.ToArray());
        }

        private static void RetainedPayloadConflict(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var requestId = scenario.Value<ulong>("requestId");
            var first = replay.Admit(
                requestId,
                Bytes(scenario.Value<string>("canonicalRequestHex")),
                1,
                scheduler);
            replay.Complete(first, Bytes("aa"));
            DrainOne(scheduler);
            AssertProtocolError(
                scenario,
                () => replay.Admit(
                    requestId,
                    Bytes(scenario.Value<string>("conflictingRequestHex")),
                    1,
                    scheduler));
        }

        private static void StaleAfterReplayEviction(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("maxOutstandingRequests", 2UL),
                ("maxReplayEntries", 2UL),
                ("maxReplayBytes", 64UL),
                ("reservedControlQueueDepth", 4UL),
                ("reservedControlQueueBytes", 64UL),
                ("maxQueuedBytes", limits.MaxQueuedBytes));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var replay = new U2R2RequestReplayAuthority(bounded);
            foreach (var requestId in scenario["requestIds"].Values<ulong>())
            {
                var admission = replay.Admit(
                    requestId,
                    new[] { (byte)requestId },
                    1,
                    scheduler);
                replay.Complete(admission, new[] { (byte)(requestId + 10) });
                DrainOne(scheduler);
            }
            Assert.Equal(3UL, replay.HighWaterMark);
            AssertProtocolError(
                scenario,
                () => replay.Admit(
                    scenario.Value<ulong>("staleRequestId"),
                    Bytes("01"),
                    1,
                    scheduler));
            var next = replay.Admit(4, Bytes("04"), 1, scheduler);
            Assert.Equal(U2R2ReplayDecision.BeginMutation, next.Decision);
        }

        private static void ControlReservedBeforeMutation(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("reservedControlQueueDepth", 1UL),
                ("reservedControlQueueBytes", 8UL),
                ("controlBurstLimit", 1UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            Assert.True(scheduler.TryReserveControl(8, out var reservation));
            reservation.Commit(U2R2OutboundFrame.Control("occupied", new byte[8]));
            var replay = new U2R2RequestReplayAuthority(bounded);
            AssertProtocolError(
                scenario,
                () => replay.Admit(
                    scenario.Value<ulong>("requestId"),
                    Bytes("01"),
                    1,
                    scheduler));
            Assert.Equal(0UL, replay.HighWaterMark);
            Assert.Equal(0UL, replay.OutstandingRequests);
        }

        private static void ReplayBytesMaxPlusOne(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(("maxReplayBytes", 8UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var replay = new U2R2RequestReplayAuthority(bounded);
            AssertProtocolError(
                scenario,
                () => replay.Admit(
                    scenario.Value<ulong>("requestId"),
                    new byte[4],
                    5,
                    scheduler));
            Assert.Equal(0UL, replay.HighWaterMark);
        }

        private static void ReadyPrecedesMessage(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var authority = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var replay = new U2R2RequestReplayAuthority(limits);
            var key = Key(scenario);
            var identity = Identity(key);
            var readyResponse = replay.Admit(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = authority.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            AssertProtocolError(
                scenario,
                () => authority.AdmitMessage(
                    identity,
                    scenario.Value<ulong>("firstSequence")));
            authority.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            Assert.Equal(
                U2R2MessageAdmission.Accepted,
                authority.AdmitMessage(identity, scenario.Value<ulong>("firstSequence")));
            scheduler.EnqueueData(
                U2R2OutboundFrame.Data("message", key, 1, Bytes("02")),
                U2R2QueueOverflowPolicy.Reject);
            Assert.Equal(
                scenario["expectedOrder"].Values<string>(),
                DrainAll(scheduler));
        }

        private static void UnregisterFencesWriter(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var key = Key(scenario);
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var authority = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var replay = new U2R2RequestReplayAuthority(limits);
            var identity = Identity(key);
            var readyResponse = replay.Admit(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = authority.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            authority.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            var order = new List<string> { DrainOne(scheduler).Token };
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data("message", key, 1, Bytes("aa")),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.True(scheduler.TryBeginWrite(out var writer));
            order.Add(writer.Frame.Token);
            var removedResponse = replay.Admit(
                2,
                RequestBytes("unregister_subscription", identity),
                1,
                scheduler);
            var removal = authority.BeginUnregister(
                identity,
                scheduler,
                replay,
                removedResponse);
            Assert.False(
                authority.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removedResponse,
                    U2R2OutboundFrame.Control("subscription_removed", Bytes("02"))));
            Assert.Equal(
                U2R2EnqueueDisposition.Rejected,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data("late", key, 2, Bytes("bb")),
                    U2R2QueueOverflowPolicy.Reject));
            writer.Dispose();
            Assert.True(
                authority.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removedResponse,
                    U2R2OutboundFrame.Control("subscription_removed", Bytes("02"))));
            order.Add(DrainOne(scheduler).Token);
            Assert.Equal(scenario["expectedOrder"].Values<string>(), order);
            Assert.Equal(
                ParseMessageAdmission(scenario.Value<string>("expectedAdmission")),
                authority.AdmitMessage(identity, 2));
        }

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

        private static void RaceTwo(Action first, Action second)
        {
            using var barrier = new Barrier(3);
            var firstTask = Task.Run(
                () =>
                {
                    barrier.SignalAndWait();
                    first();
                });
            var secondTask = Task.Run(
                () =>
                {
                    barrier.SignalAndWait();
                    second();
                });
            barrier.SignalAndWait();
            Task.WaitAll(firstTask, secondTask);
        }

        private static void TerminalCloseCancelsPendingAuthorities(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var pending = replay.Admit(
                scenario.Value<ulong>("requestId"),
                Bytes("01"),
                1,
                scheduler);
            var key = Key(scenario);
            var identity = Identity(key);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var registerResponse = replay.Admit(
                scenario.Value<ulong>("requestId") + 1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                registerResponse);
            var second = new U2R2ContractKey(key.ContractId + 1, key.Generation);
            var secondIdentity = Identity(second);
            var readyResponse = replay.Admit(
                scenario.Value<ulong>("requestId") + 2,
                RequestBytes("register_subscription", secondIdentity),
                1,
                scheduler);
            var ready = contracts.BeginRegistration(
                secondIdentity,
                scheduler,
                replay,
                readyResponse);
            contracts.CommitReady(
                ready,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            DrainOne(scheduler);
            var removedResponse = replay.Admit(
                scenario.Value<ulong>("requestId") + 3,
                RequestBytes("unregister_subscription", secondIdentity),
                1,
                scheduler);
            var removal = contracts.BeginUnregister(
                secondIdentity,
                scheduler,
                replay,
                removedResponse);

            Assert.Equal(3UL, replay.OutstandingRequests);
            Assert.Equal(2UL, contracts.ContractCount);
            Assert.Equal(1UL, scheduler.RevokedContractCount);
            for (var call = 0; call < scenario.Value<int>("closeCalls"); call++)
                contracts.Close(scheduler, replay);

            Assert.True(contracts.IsClosed);
            Assert.True(replay.IsClosed);
            Assert.True(scheduler.IsClosed);
            Assert.Equal(
                scenario.Value<ulong>("expectedContracts"),
                contracts.ContractCount);
            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingRequests"),
                replay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedRetainedEntries"),
                replay.RetainedEntries);
            Assert.Equal(
                scenario.Value<ulong>("expectedReplayBytes"),
                replay.ReplayBytes);
            Assert.Equal(
                scenario.Value<ulong>("expectedReservedDepth"),
                scheduler.TotalQueuedDepth);
            Assert.Equal(
                scenario.Value<ulong>("expectedReservedBytes"),
                scheduler.QueuedBytes);
            Assert.Equal(
                scenario.Value<ulong>("expectedRevokedContracts"),
                scheduler.RevokedContractCount);
        }

        private static void TerminalCloseRejectsWrongAuthorities(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var wrongScheduler = new U2R2BoundedOutboundScheduler(limits);
            var wrongReplay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var response = replay.Admit(
                scenario.Value<ulong>("requestId"),
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                response);
            var wrongResponse = wrongReplay.Admit(
                scenario.Value<ulong>("wrongRequestId"),
                Bytes("bb"),
                1,
                wrongScheduler);

            Assert.Throws<InvalidOperationException>(
                () => contracts.Close(wrongScheduler, wrongReplay));
            Assert.False(contracts.IsClosed);
            Assert.False(replay.IsClosed);
            Assert.False(scheduler.IsClosed);
            Assert.False(wrongReplay.IsClosed);
            Assert.False(wrongScheduler.IsClosed);
            Assert.Equal(1UL, contracts.ContractCount);
            Assert.Equal(
                scenario.Value<ulong>("expectedBoundOutstanding"),
                replay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedBoundReservedDepth"),
                scheduler.TotalQueuedDepth);
            Assert.Equal(
                scenario.Value<ulong>("expectedBoundOutstanding"),
                wrongReplay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedBoundReservedDepth"),
                wrongScheduler.TotalQueuedDepth);

            contracts.Close(scheduler, replay);
            Assert.True(contracts.IsClosed);
            Assert.True(replay.IsClosed);
            Assert.True(scheduler.IsClosed);
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(
                scenario.Value<ulong>("expectedClosedOutstanding"),
                replay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedClosedReservedDepth"),
                scheduler.TotalQueuedDepth);
            Assert.Throws<InvalidOperationException>(
                () => contracts.Close(wrongScheduler, wrongReplay));
            Assert.False(wrongReplay.IsClosed);
            Assert.False(wrongScheduler.IsClosed);

            wrongReplay.CancelPending(wrongResponse);
            Assert.Equal(
                scenario.Value<ulong>("expectedClosedOutstanding"),
                wrongReplay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedClosedReservedDepth"),
                wrongScheduler.TotalQueuedDepth);
        }

        private static void RevokedCapacityRejectionHasNoSideEffects(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("maxContracts", scenario.Value<ulong>("maxContracts")),
                ("maxTombstones", scenario.Value<ulong>("maxTombstones")));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var expectedBound = scenario.Value<ulong>("expectedBound");
            for (ulong contractId = 1; contractId <= expectedBound; contractId++)
                scheduler.RevokeContract(new U2R2ContractKey(contractId, 1));
            Assert.Equal(expectedBound, scheduler.RevokedContractCount);

            for (var attempt = 0;
                 attempt < scenario.Value<int>("attackAttempts");
                 attempt++)
            {
                var contractId = checked(expectedBound + 1UL + (ulong)attempt);
                Assert.Throws<InvalidOperationException>(
                    () => scheduler.RevokeContract(
                        new U2R2ContractKey(contractId, 1)));
                Assert.Equal(expectedBound, scheduler.RevokedContractCount);
            }

            scheduler.RevokeContract(new U2R2ContractKey(1, 1));
            Assert.Equal(expectedBound, scheduler.RevokedContractCount);
        }

        private static void UnregisterRevokedCapacityIsAtomic(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("maxContracts", scenario.Value<ulong>("maxContracts")),
                ("maxTombstones", scenario.Value<ulong>("maxTombstones")));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var replay = new U2R2RequestReplayAuthority(bounded);
            var contracts = new U2R2ContractAuthority(
                bounded,
                DefaultSemanticErrorFrame);
            var generation = scenario.Value<ulong>("generation");
            var target = Identity(
                new U2R2ContractKey(
                    scenario.Value<ulong>("targetContractId"),
                    generation));
            var filler = Identity(
                new U2R2ContractKey(
                    scenario.Value<ulong>("fillerContractId"),
                    generation));
            RegisterReady(
                contracts,
                scheduler,
                replay,
                target,
                scenario.Value<ulong>("targetRegisterRequestId"));
            RegisterReady(
                contracts,
                scheduler,
                replay,
                filler,
                scenario.Value<ulong>("fillerRegisterRequestId"));

            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "filler",
                        filler.Key,
                        1,
                        Bytes("01")),
                    U2R2QueueOverflowPolicy.Reject));
            Assert.True(scheduler.TryBeginWrite(out var writer));
            var fillerResponse = replay.Admit(
                scenario.Value<ulong>("fillerUnregisterRequestId"),
                RequestBytes("unregister_subscription", filler),
                1,
                scheduler);
            var fillerRemoval = contracts.BeginUnregister(
                filler,
                scheduler,
                replay,
                fillerResponse);
            contracts.CancelRemoval(
                fillerRemoval,
                scheduler,
                replay,
                fillerResponse);

            foreach (var contractId in scenario["revokedFillerIds"].Values<ulong>())
            {
                scheduler.RevokeContract(
                    new U2R2ContractKey(contractId, generation));
            }
            Assert.Equal(
                scenario.Value<ulong>("expectedRevokedAtCapacity"),
                scheduler.RevokedContractCount);

            var failedResponse = replay.Admit(
                scenario.Value<ulong>("failedUnregisterRequestId"),
                RequestBytes("unregister_subscription", target),
                1,
                scheduler);
            Assert.Throws<InvalidOperationException>(
                () => contracts.BeginUnregister(
                    target,
                    scheduler,
                    replay,
                    failedResponse));
            Assert.Equal(
                scenario.Value<ulong>("expectedContractsAfterFailure"),
                contracts.ContractCount);
            Assert.Equal(
                U2R2MessageAdmission.Accepted,
                contracts.AdmitMessage(target, 1));
            replay.CancelPending(failedResponse);
            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingAfterCancel"),
                replay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedReservedDepthAfterCancel"),
                scheduler.TotalQueuedDepth);

            writer.Dispose();
            Assert.Equal(
                scenario.Value<ulong>("expectedRevokedAfterRelease"),
                scheduler.RevokedContractCount);
            var retryResponse = replay.Admit(
                scenario.Value<ulong>("retryUnregisterRequestId"),
                RequestBytes("unregister_subscription", target),
                1,
                scheduler);
            var retryRemoval = contracts.BeginUnregister(
                target,
                scheduler,
                replay,
                retryResponse);
            Assert.True(
                contracts.TryCommitRemoved(
                    retryRemoval,
                    scheduler,
                    replay,
                    retryResponse,
                    U2R2OutboundFrame.Control(
                        "subscription_removed",
                        Bytes("02"))));
            DrainOne(scheduler);
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(0UL, replay.OutstandingRequests);
            contracts.Close(scheduler, replay);
        }

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

        private static void AllNamedCountersAreBounded(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var expected = new HashSet<string>(
                new[]
                {
                    "connections",
                    "dataSessions",
                    "probes",
                    "contracts",
                    "outstandingRequests",
                },
                StringComparer.Ordinal);
            Assert.Equal(
                scenario["counterNames"].Values<string>().OrderBy(value => value),
                expected.OrderBy(value => value));

            var resources = new U2R2SessionResourceAuthority(limits);
            var leases = new List<U2R2ResourceLease>();
            Assert.True(resources.TryAcquire(U2R2ConnectionRole.DataSession, out var data));
            leases.Add(data);
            for (ulong index = 0; index < limits.MaxProbes; index++)
            {
                Assert.True(resources.TryAcquire(U2R2ConnectionRole.Probe, out var probe));
                leases.Add(probe);
            }
            Assert.False(resources.TryAcquire(U2R2ConnectionRole.DataSession, out _));
            Assert.False(resources.TryAcquire(U2R2ConnectionRole.Probe, out _));
            Assert.Equal(limits.MaxConnections, resources.ConnectionCount);
            foreach (var lease in leases)
                lease.Dispose();
            Assert.Equal(0UL, resources.ConnectionCount);

            var contractLimits = limits.With(
                ("maxContracts", 2UL),
                ("reservedControlQueueDepth", 3UL));
            var contractScheduler = new U2R2BoundedOutboundScheduler(contractLimits);
            var contracts = new U2R2ContractAuthority(
                contractLimits,
                DefaultSemanticErrorFrame);
            var contractReplay = new U2R2RequestReplayAuthority(contractLimits);
            var firstIdentity = Identity(new U2R2ContractKey(1, 1));
            var firstResponse = contractReplay.Admit(
                1,
                RequestBytes("register_subscription", firstIdentity),
                1,
                contractScheduler);
            var firstRegistration = contracts.BeginRegistration(
                firstIdentity,
                contractScheduler,
                contractReplay,
                firstResponse);
            contracts.CommitReady(
                firstRegistration,
                contractReplay,
                firstResponse,
                U2R2OutboundFrame.Control("subscription_ready:1", Bytes("01")));
            DrainOne(contractScheduler);
            var secondIdentity = Identity(new U2R2ContractKey(2, 1));
            var secondResponse = contractReplay.Admit(
                2,
                RequestBytes("register_subscription", secondIdentity),
                1,
                contractScheduler);
            var secondRegistration = contracts.BeginRegistration(
                secondIdentity,
                contractScheduler,
                contractReplay,
                secondResponse);
            contracts.CommitReady(
                secondRegistration,
                contractReplay,
                secondResponse,
                U2R2OutboundFrame.Control("subscription_ready:2", Bytes("01")));
            DrainOne(contractScheduler);
            AssertProtocolError(
                scenario,
                () =>
                {
                    var thirdIdentity = Identity(new U2R2ContractKey(3, 1));
                    var thirdResponse = contractReplay.Admit(
                        3,
                        RequestBytes("register_subscription", thirdIdentity),
                        1,
                        contractScheduler);
                    contracts.BeginRegistration(
                        thirdIdentity,
                        contractScheduler,
                        contractReplay,
                        thirdResponse);
                });

            var replayLimits = limits.With(
                ("maxOutstandingRequests", 2UL),
                ("reservedControlQueueDepth", 3UL));
            var replayScheduler = new U2R2BoundedOutboundScheduler(replayLimits);
            var replay = new U2R2RequestReplayAuthority(replayLimits);
            replay.Admit(1, Bytes("01"), 1, replayScheduler);
            replay.Admit(2, Bytes("02"), 1, replayScheduler);
            AssertProtocolError(
                scenario,
                () => replay.Admit(3, Bytes("03"), 1, replayScheduler));
        }

        private static void LimitsDiagnosticSnapshotIsImmutable(
            JObject scenario,
            U2R2ProtocolLimits limits,
            JObject limitsJson)
        {
            var snapshot = limits.ToDiagnosticSnapshot();
            Assert.Equal(scenario.Value<int>("expectedLimitCount"), snapshot.Count);
            Assert.Equal(
                snapshot.OrderBy(pair => pair.Key),
                U2R2ProtocolLimits.Default
                    .ToDiagnosticSnapshot()
                    .OrderBy(pair => pair.Key));
            Assert.Equal(limitsJson.Value<ulong>("maxConnections"), snapshot["maxConnections"]);
            Assert.Throws<NotSupportedException>(
                () => ((IDictionary<string, ulong>)snapshot).Add("mutated", 1));
            var source = limitsJson.Properties().ToDictionary(
                property => property.Name,
                property => property.Value.Value<ulong>(),
                StringComparer.Ordinal);
            var independent = U2R2ProtocolLimits.FromDiagnosticSnapshot(source);
            source["maxConnections"] = 999;
            Assert.Equal(limits.MaxConnections, independent.MaxConnections);
        }

        private static void LimitsConfigurationFailsClosed(
            JObject scenario,
            JObject limitsJson)
        {
            foreach (var mutation in scenario["invalidMutations"].Values<string>())
            {
                var values = limitsJson.Properties().ToDictionary(
                    property => property.Name,
                    property => property.Value.Value<ulong>(),
                    StringComparer.Ordinal);
                Action action;
                switch (mutation)
                {
                    case "missing_field":
                        values.Remove("maxConnections");
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "unknown_field":
                        values.Add("unknownLimit", 1);
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "zero_value":
                        values["readTimeoutMs"] = 0;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "data_sessions_not_one":
                        values["maxDataSessions"] = 2;
                        values["maxConnections"] =
                            values["maxDataSessions"] + values["maxProbes"];
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "connections_below_roles":
                        values["maxConnections"] =
                            values["maxDataSessions"] + values["maxProbes"] - 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "per_contract_below_max_frame":
                        values["maxPerContractQueueBytes"] =
                            values["fixedFrameBytes"]
                            + values["maxHeaderBytes"]
                            + values["maxPayloadBytes"] - 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "queued_below_max_frame":
                        values["maxQueuedBytes"] =
                            values["fixedFrameBytes"]
                            + values["maxHeaderBytes"]
                            + values["maxPayloadBytes"] - 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "control_depth_exceeds_total":
                        values["reservedControlQueueDepth"] =
                            values["maxTotalQueueDepth"] + 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "control_burst_exceeds_depth":
                        values["controlBurstLimit"] =
                            values["reservedControlQueueDepth"] + 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "revoked_bound_overflow":
                        values["maxContracts"] = ulong.MaxValue;
                        values["maxTombstones"] = 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "header_above_uint32":
                        values["maxHeaderBytes"] = (ulong)uint.MaxValue + 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "payload_above_uint32":
                        values["maxPayloadBytes"] = (ulong)uint.MaxValue + 1;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "json_depth_above_protocol_max":
                        values["maxJsonDepth"] = 65;
                        action = () => U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        break;
                    case "unknown_with_field":
                        var limits = U2R2ProtocolLimits.FromDiagnosticSnapshot(values);
                        action = () => limits.With(("unknownLimit", 1UL));
                        break;
                    default:
                        throw new InvalidOperationException(mutation);
                }
                AssertProtocolError(scenario, action);
            }
        }

        private static void ReadyUnregisterFullOrdering(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var key = Key(scenario);
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var authority = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var replay = new U2R2RequestReplayAuthority(limits);
            var identity = Identity(key);
            var readyResponse = replay.Admit(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = authority.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            AssertProtocolError(
                scenario.Value<string>("expectedPreReadyErrorCode"),
                scenario.Value<bool>("terminal"),
                () => authority.AdmitMessage(
                    identity,
                    scenario.Value<ulong>("firstSequence")));
            authority.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            Assert.Equal(
                U2R2MessageAdmission.Accepted,
                authority.AdmitMessage(identity, 1));
            var order = new List<string> { DrainOne(scheduler).Token };
            scheduler.EnqueueData(U2R2OutboundFrame.Data("queued", key, 2, Bytes("01")), U2R2QueueOverflowPolicy.Reject);
            scheduler.EnqueueData(U2R2OutboundFrame.Data("writer", key, 3, Bytes("02")), U2R2QueueOverflowPolicy.Reject);
            Assert.True(scheduler.TryBeginWrite(out var writer));
            order.Add(writer.Frame.Token);
            var removedResponse = replay.Admit(
                2,
                RequestBytes("unregister_subscription", identity),
                1,
                scheduler);
            var removal = authority.BeginUnregister(
                identity,
                scheduler,
                replay,
                removedResponse);
            Assert.Equal(0UL, scheduler.DataQueuedDepth);
            Assert.False(
                authority.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removedResponse,
                    U2R2OutboundFrame.Control("subscription_removed", Bytes("03"))));
            writer.Dispose();
            Assert.True(
                authority.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removedResponse,
                    U2R2OutboundFrame.Control("subscription_removed", Bytes("03"))));
            order.Add(DrainOne(scheduler).Token);
            Assert.Equal(scenario["expectedOrder"].Values<string>(), order);
            Assert.Equal(
                ParseMessageAdmission(scenario.Value<string>("expectedAdmission")),
                authority.AdmitMessage(identity, 2));
        }

        private static void ContractIdentityValidation(JObject scenario)
        {
            foreach (var topic in scenario["validTopics"].Values<string>())
            {
                var parsed = ParseRegistration(
                    topic,
                    "sensor_msgs/msg/Image",
                    scenario["validQos"][0]);
                Assert.Equal(topic, parsed.Topic);
                Assert.Equal("sensor_msgs/msg/Image", parsed.SchemaName);
            }
            foreach (var topic in scenario["invalidTopics"].Values<string>())
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        topic,
                        "sensor_msgs/msg/Image",
                        scenario["validQos"][0]));
            foreach (var type in scenario["validTypes"].Values<string>())
            {
                var parsed = ParseRegistration(
                    "/camera/front",
                    type,
                    scenario["validQos"][0]);
                Assert.Equal(type, parsed.SchemaName);
            }
            foreach (var type in scenario["invalidTypes"].Values<string>())
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        "/camera/front",
                        type,
                        scenario["validQos"][0]));
            foreach (var qos in scenario["validQos"].Values<JObject>())
            {
                var parsed = ParseRegistration(
                    "/camera/front",
                    "sensor_msgs/msg/Image",
                    qos);
                Assert.NotNull(parsed.Qos);
                Assert.Equal(qos.Value<string>("profile"), parsed.Qos.Profile);
                Assert.Equal(qos.Value<string>("reliability"), parsed.Qos.Reliability);
                Assert.Equal(qos.Value<string>("durability"), parsed.Qos.Durability);
                Assert.Equal(qos.Value<string>("history"), parsed.Qos.History);
                Assert.Equal(qos.Value<uint>("depth"), parsed.Qos.Depth);
            }
            foreach (var qos in scenario["invalidQos"].Values<JObject>())
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        "/camera/front",
                        "sensor_msgs/msg/Image",
                        qos));
            foreach (var mutation in scenario["invalidQosShapes"].Values<string>())
            {
                var invalid = InvalidQosShape(
                    Assert.IsType<JObject>(scenario["validQos"][0]),
                    mutation);
                AssertProtocolError(
                    scenario,
                    () => ParseRegistration(
                        "/camera/front",
                        "sensor_msgs/msg/Image",
                        invalid));
            }
            var boundaries = Assert.IsType<JObject>(
                scenario["typeLengthBoundaries"]);
            var validPackage = new string(
                'a',
                boundaries.Value<int>("validPackageLength"));
            var invalidPackage = new string(
                'a',
                boundaries.Value<int>("invalidPackageLength"));
            var validType =
                "T"
                + new string(
                    'a',
                    boundaries.Value<int>("validTypeLength") - 1);
            var invalidType =
                "T"
                + new string(
                    'a',
                    boundaries.Value<int>("invalidTypeLength") - 1);
            ParseRegistration(
                "/camera/front",
                validPackage + "/msg/" + validType,
                scenario["validQos"][0]);
            AssertProtocolError(
                scenario,
                () => ParseRegistration(
                    "/camera/front",
                    invalidPackage + "/msg/Image",
                    scenario["validQos"][0]));
            AssertProtocolError(
                scenario,
                () => ParseRegistration(
                    "/camera/front",
                    "sensor_msgs/msg/" + invalidType,
                    scenario["validQos"][0]));
            foreach (var invalid in scenario["invalidDirections"].Values<uint>())
            {
                AssertProtocolError(
                    scenario,
                    () => new U2R2ContractIdentity(
                        new U2R2ContractKey(41, 7),
                        (U2R2ContractDirection)invalid,
                        "/camera/front",
                        "sensor_msgs/msg/Image",
                        new U2R2Qos(
                            "default",
                            "reliable",
                            "volatile",
                            "keep_last",
                            10)));
            }
        }

        private static JToken InvalidQosShape(
            JObject valid,
            string mutation)
        {
            if (mutation == "non_object")
                return new JValue("default");
            var value = (JObject)valid.DeepClone();
            switch (mutation)
            {
                case "missing_axis":
                    value.Remove("profile");
                    break;
                case "extra_axis":
                    value["deadline"] = 1;
                    break;
                case "profile_non_string":
                    value["profile"] = 1;
                    break;
                case "reliability_non_string":
                    value["reliability"] = 1;
                    break;
                case "durability_non_string":
                    value["durability"] = 1;
                    break;
                case "history_non_string":
                    value["history"] = 1;
                    break;
                case "depth_negative":
                    value["depth"] = -1;
                    break;
                case "depth_fraction":
                    value["depth"] = 1.5;
                    break;
                case "depth_above_uint32":
                    value["depth"] = 4294967296UL;
                    break;
                default:
                    throw new InvalidOperationException(mutation);
            }
            return value;
        }

        private static void ContractIdentityAliasAndReplay(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var request = RequestBytes("register_subscription", identity);
            var registerId = scenario.Value<ulong>("registerRequestId");
            var readyResponse = replay.Admit(
                registerId,
                request,
                1,
                scheduler);
            var registration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            contracts.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            DrainOne(scheduler);

            var repeated = replay.Admit(registerId, request, 1, scheduler);
            Assert.Equal(
                U2R2ReplayDecision.ReplayCached,
                repeated.Decision);
            var replayedRegistration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                repeated);
            Assert.True(replayedRegistration.Replayed);
            contracts.CommitReady(
                replayedRegistration,
                replay,
                repeated,
                U2R2OutboundFrame.Control("must-not-send", Bytes("01")));
            Assert.Equal(
                scenario.Value<ulong>("expectedResponseCount"),
                scheduler.TotalQueuedDepth);
            Assert.Equal("replay:" + registerId, DrainOne(scheduler).Token);

            var requestId = scenario.Value<ulong>("aliasStartRequestId");
            foreach (var mutation in scenario["aliasMutations"].Values<string>())
            {
                var alias = AliasIdentity(identity, mutation);
                var aliasResponse = replay.Admit(
                    requestId++,
                    RequestBytes("register_subscription", alias),
                    1,
                    scheduler);
                AssertProtocolError(
                    scenario,
                    () => contracts.BeginRegistration(
                        alias,
                        scheduler,
                        replay,
                        aliasResponse));
                Assert.Contains(
                    "subscription_ready",
                    DrainOne(scheduler).Token,
                    StringComparison.Ordinal);
            }
            Assert.Equal(1UL, contracts.ContractCount);
        }

        private static U2R2ContractIdentity AliasIdentity(
            U2R2ContractIdentity identity,
            string mutation)
        {
            var direction = identity.Direction;
            var topic = identity.Topic;
            var schemaName = identity.SchemaName;
            var profile = identity.Qos.Profile;
            var reliability = identity.Qos.Reliability;
            var durability = identity.Qos.Durability;
            var history = identity.Qos.History;
            var depth = identity.Qos.Depth;
            switch (mutation)
            {
                case "topic":
                    topic = "/camera/rear";
                    break;
                case "schemaName":
                    schemaName = "demo_interfaces/msg/Telemetry";
                    break;
                case "profile":
                    profile = "sensor_data";
                    break;
                case "reliability":
                    reliability = "best_effort";
                    break;
                case "durability":
                    durability = "transient_local";
                    break;
                case "history":
                    history = "keep_all";
                    depth = 0;
                    break;
                case "depth":
                    depth = 11;
                    break;
                case "direction":
                    direction = U2R2ContractDirection.Publish;
                    break;
                default:
                    throw new InvalidOperationException(mutation);
            }
            return new U2R2ContractIdentity(
                identity.Key,
                direction,
                topic,
                schemaName,
                new U2R2Qos(
                    profile,
                    reliability,
                    durability,
                    history,
                    depth));
        }

        private static void FreshRegistrationRequiresSubscribeDirection(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(
                Key(scenario),
                direction: U2R2ContractDirection.Publish);
            var request = RequestBytes("register_subscription", identity);
            var requestId = scenario.Value<ulong>("requestId");
            var response = replay.Admit(
                requestId,
                request,
                1,
                scheduler);

            AssertProtocolError(
                scenario,
                () => contracts.BeginRegistration(
                    identity,
                    scheduler,
                    replay,
                    response));
            var first = DrainOne(scheduler);
            Assert.Equal(
                scenario.Value<string>("expectedResponseToken"),
                first.Token);
            Assert.Equal(
                Bytes(scenario.Value<string>("expectedResponseHex")),
                first.Bytes);
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(0UL, replay.OutstandingRequests);

            var repeated = replay.Admit(
                requestId,
                request,
                1,
                scheduler);
            Assert.Equal(U2R2ReplayDecision.ReplayCached, repeated.Decision);
            Assert.Equal(first.Bytes, repeated.CachedResponse);
            Assert.Equal(first.Bytes, DrainOne(scheduler).Bytes);
        }

        private static void MessageRequiresFrozenContractIdentity(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var response = replay.Admit(
                1,
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
            DrainOne(scheduler);

            foreach (var mutation in scenario["aliasMutations"].Values<string>())
            {
                var alias = AliasIdentity(identity, mutation);
                AssertProtocolError(
                    scenario,
                    () => contracts.AdmitMessage(alias, 1));
            }
            Assert.Equal(
                U2R2MessageAdmission.Accepted,
                contracts.AdmitMessage(identity, 1));
        }

        private static void ComposedRegisterUnregisterSingleResponse(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var registerRequest =
                RequestBytes("register_subscription", identity);
            var registerId = scenario.Value<ulong>("registerRequestId");
            var order = new List<string>();
            var readyResponse = replay.Admit(
                registerId,
                registerRequest,
                1,
                scheduler);
            var registration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            contracts.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            Assert.Equal(
                scenario.Value<ulong>("expectedRegisterResponses"),
                scheduler.TotalQueuedDepth);
            order.Add(DrainOne(scheduler).Token);

            var readyReplay = replay.Admit(
                registerId,
                registerRequest,
                1,
                scheduler);
            var replayedRegistration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyReplay);
            contracts.CommitReady(
                replayedRegistration,
                replay,
                readyReplay,
                U2R2OutboundFrame.Control("must-not-send", Bytes("01")));
            Assert.Equal(
                scenario.Value<ulong>("expectedReplayResponses"),
                scheduler.TotalQueuedDepth);
            order.Add(DrainOne(scheduler).Token);

            Assert.Equal(
                U2R2MessageAdmission.Accepted,
                contracts.AdmitMessage(identity, 1));
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "message",
                        identity.Key,
                        1,
                        Bytes("01")),
                    U2R2QueueOverflowPolicy.Reject));
            order.Add(DrainOne(scheduler).Token);

            var unregisterRequest =
                RequestBytes("unregister_subscription", identity);
            var unregisterId = scenario.Value<ulong>("unregisterRequestId");
            var removedResponse = replay.Admit(
                unregisterId,
                unregisterRequest,
                1,
                scheduler);
            var removal = contracts.BeginUnregister(
                identity,
                scheduler,
                replay,
                removedResponse);
            Assert.True(
                contracts.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removedResponse,
                    U2R2OutboundFrame.Control(
                        "subscription_removed",
                        Bytes("02"))));
            Assert.Equal(
                scenario.Value<ulong>("expectedUnregisterResponses"),
                scheduler.TotalQueuedDepth);
            order.Add(DrainOne(scheduler).Token);

            var removedReplay = replay.Admit(
                unregisterId,
                unregisterRequest,
                1,
                scheduler);
            var replayedRemoval = contracts.BeginUnregister(
                identity,
                scheduler,
                replay,
                removedReplay);
            Assert.True(
                contracts.TryCommitRemoved(
                    replayedRemoval,
                    scheduler,
                    replay,
                    removedReplay,
                    U2R2OutboundFrame.Control("must-not-send", Bytes("02"))));
            Assert.Equal(
                scenario.Value<ulong>("expectedReplayResponses"),
                scheduler.TotalQueuedDepth);
            order.Add(DrainOne(scheduler).Token);
            Assert.Equal(scenario["expectedOrder"].Values<string>(), order);
            Assert.Equal(0UL, scheduler.TotalQueuedDepth);
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(1UL, contracts.TombstoneCount);
        }

        private static void InvalidOverflowPolicyHasNoSideEffects(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            AssertProtocolError(
                scenario,
                () => scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "invalid",
                        new U2R2ContractKey(1, 1),
                        1,
                        Bytes("01")),
                    (U2R2QueueOverflowPolicy)scenario.Value<int>(
                        "invalidPolicy")));
            Assert.Equal(
                scenario.Value<ulong>("expectedQueuedDepth"),
                scheduler.TotalQueuedDepth);
            Assert.Equal(
                scenario.Value<ulong>("expectedQueuedBytes"),
                scheduler.QueuedBytes);
        }

        private static void FencedResponseFifoAndTransactionBinding(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var registrationResponse = replay.Admit(
                scenario.Value<ulong>("registerRequestId"),
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                registrationResponse);
            var decoyResponse = replay.Admit(
                scenario.Value<ulong>("decoyRequestId"),
                Bytes("ee"),
                1,
                scheduler);
            Assert.Throws<InvalidOperationException>(
                () => contracts.CommitReady(
                    registration,
                    replay,
                    decoyResponse,
                    U2R2OutboundFrame.Control("wrong", Bytes("ff"))));
            replay.CancelPending(decoyResponse);
            contracts.CommitReady(
                registration,
                replay,
                registrationResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));

            var removalResponse = replay.Admit(
                scenario.Value<ulong>("unregisterRequestId"),
                RequestBytes("unregister_subscription", identity),
                1,
                scheduler);
            var removal = contracts.BeginUnregister(
                identity,
                scheduler,
                replay,
                removalResponse);
            Assert.True(
                contracts.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removalResponse,
                    U2R2OutboundFrame.Control(
                        "subscription_removed",
                        Bytes("02"))));

            Assert.Equal(
                scenario["expectedOrder"].Values<string>(),
                DrainAll(scheduler));
            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingRequests"),
                replay.OutstandingRequests);
            Assert.Equal(0UL, contracts.ContractCount);
            Assert.Equal(1UL, contracts.TombstoneCount);
        }

        private static void SemanticRejectionsCommitExactReplay(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(("maxContracts", 1UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var replay = new U2R2RequestReplayAuthority(bounded);
            var cases = scenario["cases"]
                .Values<JObject>()
                .ToDictionary(
                    item => item.Value<string>("kind"),
                    StringComparer.Ordinal);
            string KindForRequest(ulong requestId)
            {
                if (requestId == scenario.Value<ulong>("duplicateRequestId"))
                    return "duplicate";
                if (requestId == scenario.Value<ulong>("capacityRequestId"))
                    return "capacity";
                if (requestId == scenario.Value<ulong>("unknownRequestId"))
                    return "unknown";
                throw new InvalidOperationException(
                    "Unexpected semantic-rejection request ID.");
            }

            var contracts = new U2R2ContractAuthority(
                bounded,
                (operation, requestId, error) =>
                {
                    var item = cases[KindForRequest(requestId)];
                    Assert.Equal(
                        item.Value<string>("responseOperation"),
                        OperationToken(operation));
                    Assert.Equal(item.Value<string>("errorCode"), error.ErrorCode);
                    Assert.Equal(item.Value<bool>("terminal"), error.Terminal);
                    return U2R2OutboundFrame.Control(
                        OperationToken(operation)
                        + ":"
                        + requestId.ToString(CultureInfo.InvariantCulture)
                        + ":"
                        + error.ErrorCode,
                        Bytes(item.Value<string>("responseHex")));
                });

            var identity = Identity(Key(scenario));
            var initialResponse = replay.Admit(
                1,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var initialRegistration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                initialResponse);
            contracts.CommitReady(
                initialRegistration,
                replay,
                initialResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            DrainOne(scheduler);

            void AssertExactSemanticReplay(
                string kind,
                ulong requestId,
                string requestOperation,
                U2R2ContractIdentity rejectedIdentity,
                Action<U2R2ReplayAdmission> reject)
            {
                var item = cases[kind];
                var request = RequestBytes(requestOperation, rejectedIdentity);
                var response = replay.Admit(requestId, request, 1, scheduler);
                AssertProtocolError(
                    item.Value<string>("errorCode"),
                    item.Value<bool>("terminal"),
                    () => reject(response));
                var first = DrainOne(scheduler);
                Assert.Equal(
                    Bytes(item.Value<string>("responseHex")),
                    first.Bytes.ToArray());
                Assert.Contains(
                    item.Value<string>("responseOperation"),
                    first.Token,
                    StringComparison.Ordinal);

                var repeated = replay.Admit(requestId, request, 1, scheduler);
                Assert.Equal(U2R2ReplayDecision.ReplayCached, repeated.Decision);
                Assert.Equal(
                    Bytes(item.Value<string>("responseHex")),
                    repeated.CachedResponse.ToArray());
                Assert.Equal(
                    Bytes(item.Value<string>("responseHex")),
                    DrainOne(scheduler).Bytes.ToArray());
            }

            AssertExactSemanticReplay(
                "duplicate",
                scenario.Value<ulong>("duplicateRequestId"),
                "register_subscription",
                identity,
                response => contracts.BeginRegistration(
                    identity,
                    scheduler,
                    replay,
                    response));

            var capacityIdentity = Identity(
                new U2R2ContractKey(identity.Key.ContractId + 1, 1));
            AssertExactSemanticReplay(
                "capacity",
                scenario.Value<ulong>("capacityRequestId"),
                "register_subscription",
                capacityIdentity,
                response => contracts.BeginRegistration(
                    capacityIdentity,
                    scheduler,
                    replay,
                    response));

            var unknownIdentity = Identity(
                new U2R2ContractKey(identity.Key.ContractId + 2, 1));
            AssertExactSemanticReplay(
                "unknown",
                scenario.Value<ulong>("unknownRequestId"),
                "unregister_subscription",
                unknownIdentity,
                response => contracts.BeginUnregister(
                    unknownIdentity,
                    scheduler,
                    replay,
                    response));

            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingRequests"),
                replay.OutstandingRequests);
            Assert.Equal(1UL, contracts.ContractCount);
        }

        private static void ContractClaimBlocksExternalCancel(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var scheduler = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                (operation, requestId, error) =>
                {
                    Assert.Equal(
                        scenario.Value<string>("responseOperation"),
                        OperationToken(operation));
                    Assert.Equal(
                        scenario.Value<string>("abortErrorCode"),
                        error.ErrorCode);
                    return U2R2OutboundFrame.Control(
                        OperationToken(operation)
                        + ":"
                        + requestId.ToString(CultureInfo.InvariantCulture),
                        Bytes(scenario.Value<string>("abortResponseHex")));
                });
            var identity = Identity(Key(scenario));
            var request = RequestBytes("register_subscription", identity);
            var requestId = scenario.Value<ulong>("requestId");
            var response = replay.Admit(requestId, request, 1, scheduler);
            var registration = contracts.BeginRegistration(
                identity,
                scheduler,
                replay,
                response);

            Assert.Throws<InvalidOperationException>(
                () => replay.CancelPending(response));
            Assert.Throws<InvalidOperationException>(
                () => replay.Abort(
                    response,
                    Bytes(scenario.Value<string>("abortResponseHex"))));

            contracts.AbortRegistration(
                registration,
                scheduler,
                replay,
                response,
                new U2R2ProtocolException(
                    scenario.Value<string>("abortErrorCode"),
                    "registration backend rejected the contract",
                    terminal: false));
            Assert.Equal(
                Bytes(scenario.Value<string>("abortResponseHex")),
                DrainOne(scheduler).Bytes.ToArray());
            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingRequests"),
                replay.OutstandingRequests);
            Assert.Equal(0UL, contracts.ContractCount);

            var repeated = replay.Admit(requestId, request, 1, scheduler);
            Assert.Equal(U2R2ReplayDecision.ReplayCached, repeated.Decision);
            Assert.Equal(
                Bytes(scenario.Value<string>("abortResponseHex")),
                DrainOne(scheduler).Bytes.ToArray());
        }

        private static void CachedReplayRejectsWrongScheduler(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var original = new U2R2BoundedOutboundScheduler(limits);
            var wrong = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var request = Bytes(scenario.Value<string>("canonicalRequestHex"));
            var response = Bytes(scenario.Value<string>("responseHex"));
            var requestId = scenario.Value<ulong>("requestId");
            var first = replay.Admit(
                requestId,
                request,
                checked((ulong)response.Length),
                original);
            replay.Complete(first, response);
            DrainOne(original);

            Assert.Throws<InvalidOperationException>(
                () => replay.Admit(
                    requestId,
                    request,
                    checked((ulong)response.Length),
                    wrong));
            Assert.Equal(
                scenario.Value<ulong>("expectedWrongSchedulerDepth"),
                wrong.TotalQueuedDepth);

            var repeated = replay.Admit(
                requestId,
                request,
                checked((ulong)response.Length),
                original);
            Assert.Equal(U2R2ReplayDecision.ReplayCached, repeated.Decision);
            Assert.Equal(response, DrainOne(original).Bytes.ToArray());
        }

        private static void ReplayResponsesRespectControlFairness(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(("controlBurstLimit", 2UL));
            var scheduler = new U2R2BoundedOutboundScheduler(bounded);
            var replay = new U2R2RequestReplayAuthority(bounded);
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data(
                        "data",
                        new U2R2ContractKey(1, 1),
                        1,
                        Bytes("01")),
                    U2R2QueueOverflowPolicy.Reject));
            foreach (var requestId in scenario["requestIds"].Values<ulong>())
            {
                var response = replay.Admit(
                    requestId,
                    new[] { checked((byte)requestId) },
                    1,
                    scheduler);
                replay.Complete(
                    response,
                    new[] { checked((byte)(requestId + 1)) });
            }
            Assert.Equal(
                scenario["expectedOrder"].Values<string>(),
                DrainAll(scheduler));
        }

        private static void ResponseTransactionRejectsWrongScheduler(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var original = new U2R2BoundedOutboundScheduler(limits);
            var wrong = new U2R2BoundedOutboundScheduler(limits);
            var replay = new U2R2RequestReplayAuthority(limits);
            var contracts = new U2R2ContractAuthority(
                limits,
                DefaultSemanticErrorFrame);
            var identity = Identity(Key(scenario));
            var response = replay.Admit(
                scenario.Value<ulong>("requestId"),
                RequestBytes("register_subscription", identity),
                1,
                original);
            Assert.Throws<InvalidOperationException>(
                () => contracts.BeginRegistration(
                    identity,
                    wrong,
                    replay,
                    response));
            replay.CancelPending(response);
            Assert.Equal(
                scenario.Value<ulong>("expectedOutstandingRequests"),
                replay.OutstandingRequests);
            Assert.Equal(
                scenario.Value<ulong>("expectedReservedDepth"),
                original.TotalQueuedDepth);
        }

        private static void CodecConsumesSessionLimitSnapshot(
            JObject scenario,
            U2R2ProtocolLimits limits)
        {
            var bounded = limits.With(
                ("maxHeaderBytes", scenario.Value<ulong>("maxHeaderBytes")),
                ("maxPayloadBytes", scenario.Value<ulong>("maxPayloadBytes")),
                ("maxJsonDepth", scenario.Value<ulong>("maxJsonDepth")));
            var simple = new JObject { ["ok"] = 1 };
            U2R2ProtocolCodec.EncodeFrame(simple, Bytes("01"), bounded);
            AssertProtocolError(
                scenario.Value<string>("expectedWireErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    new JObject { ["padding"] = new string('x', 100) },
                    Array.Empty<byte>(),
                    bounded));
            AssertProtocolError(
                scenario.Value<string>("expectedWireErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    simple,
                    Bytes("0102"),
                    bounded));
            AssertProtocolError(
                scenario.Value<string>("expectedWireErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    new JObject
                    {
                        ["one"] = new JObject
                        {
                            ["two"] = new JObject
                            {
                                ["three"] = 1,
                            },
                        },
                    },
                    Array.Empty<byte>(),
                    bounded));
            var invalidFixed = limits.With(("fixedFrameBytes", 1UL));
            AssertProtocolError(
                scenario.Value<string>("expectedConfigurationErrorCode"),
                scenario.Value<bool>("terminal"),
                () => U2R2ProtocolCodec.EncodeFrame(
                    simple,
                    Array.Empty<byte>(),
                    invalidFixed));
        }

        private static U2R2Message ParseRegistration(
            string topic,
            string schemaName,
            JToken qos)
        {
            var header = RegisterSubscriptionHeader();
            header["topic"] = topic;
            header["schemaName"] = schemaName;
            header["qos"] = qos.DeepClone();
            return U2R2ProtocolCodec.ParseV2(
                new U2R2Frame(header, Array.Empty<byte>()));
        }

        private static JObject RegisterSubscriptionHeader()
            => (JObject)Assert.IsType<JArray>(
                    Assert.IsType<JObject>(LoadFixture()["v2"])["operations"])
                .Values<JObject>()
                .Single(vector => string.Equals(
                    vector.Value<string>("id"),
                    "register_subscription",
                    StringComparison.Ordinal))["header"]
                .DeepClone();

        private static void PurePeerCloseTransition(JObject scenario)
        {
            var lifecycle = new U2R2PureSessionLifecycle();
            AssertProtocolError(scenario, () => lifecycle.PeerClosed());
            Assert.Equal(
                ParseLifecycleState(scenario.Value<string>("expectedState")),
                lifecycle.State);
        }

        private static void PureTimeoutTransitions(JObject scenario)
        {
            var limits = LimitsFrom(
                Assert.IsType<JObject>(LoadAuthority()["limits"]));
            foreach (var kind in scenario["timeoutKinds"].Values<string>())
            {
                var lifecycle = new U2R2PureSessionLifecycle(limits);
                var parsedKind = ParseTimeoutKind(kind);
                var limit = lifecycle.LimitFor(parsedKind);
                Assert.False(lifecycle.HasTimedOut(parsedKind, limit - 1));
                AssertProtocolError(
                    scenario,
                    () => lifecycle.Timeout(
                        parsedKind,
                        limit));
                Assert.Equal(U2R2PureSessionState.Closed, lifecycle.State);
                var overLimit = new U2R2PureSessionLifecycle(limits);
                AssertProtocolError(
                    scenario,
                    () => overLimit.Timeout(parsedKind, limit + 1));
            }
        }

        private static void RegisterAndRemove(
            U2R2ContractAuthority authority,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ContractKey key,
            ulong firstRequestId)
        {
            var identity = Identity(key);
            var readyResponse = replay.Admit(
                firstRequestId,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = authority.BeginRegistration(
                identity,
                scheduler,
                replay,
                readyResponse);
            authority.CommitReady(
                registration,
                replay,
                readyResponse,
                U2R2OutboundFrame.Control("subscription_ready", Bytes("01")));
            DrainOne(scheduler);
            var removedResponse = replay.Admit(
                checked(firstRequestId + 1),
                RequestBytes("unregister_subscription", identity),
                1,
                scheduler);
            var removal = authority.BeginUnregister(
                identity,
                scheduler,
                replay,
                removedResponse);
            Assert.True(
                authority.TryCommitRemoved(
                    removal,
                    scheduler,
                    replay,
                    removedResponse,
                    U2R2OutboundFrame.Control("subscription_removed", Bytes("02"))));
            DrainOne(scheduler);
        }

        private static void RegisterReady(
            U2R2ContractAuthority authority,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ContractIdentity identity,
            ulong requestId)
        {
            var response = replay.Admit(
                requestId,
                RequestBytes("register_subscription", identity),
                1,
                scheduler);
            var registration = authority.BeginRegistration(
                identity,
                scheduler,
                replay,
                response);
            authority.CommitReady(
                registration,
                replay,
                response,
                U2R2OutboundFrame.Control(
                    "subscription_ready",
                    Bytes("01")));
            DrainOne(scheduler);
        }

        private static U2R2ContractIdentity Identity(
            U2R2ContractKey key,
            string topic = "/camera/front",
            string schemaName = "sensor_msgs/msg/Image",
            U2R2ContractDirection direction = U2R2ContractDirection.Subscribe)
            => new U2R2ContractIdentity(
                key,
                direction,
                topic,
                schemaName,
                new U2R2Qos(
                    "default",
                    "reliable",
                    "volatile",
                    "keep_last",
                    10));

        private static byte[] RequestBytes(
            string operation,
            U2R2ContractIdentity identity)
        {
            var value = new JObject
            {
                ["op"] = operation,
                ["contractId"] = identity.Key.ContractId,
                ["generation"] = identity.Key.Generation,
                ["direction"] =
                    identity.Direction == U2R2ContractDirection.Publish
                        ? "publish"
                        : "subscribe",
                ["topic"] = identity.Topic,
                ["schemaName"] = identity.SchemaName,
                ["qos"] = new JObject
                {
                    ["profile"] = identity.Qos.Profile,
                    ["reliability"] = identity.Qos.Reliability,
                    ["durability"] = identity.Qos.Durability,
                    ["history"] = identity.Qos.History,
                    ["depth"] = identity.Qos.Depth,
                },
            };
            return Encoding.UTF8.GetBytes(
                value.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static U2R2ContractKey Key(JObject scenario)
            => new U2R2ContractKey(
                scenario.Value<ulong>("contractId"),
                scenario.Value<ulong>("generation"));

        private static U2R2OutboundFrame DefaultSemanticErrorFrame(
            U2R2Operation operation,
            ulong requestId,
            U2R2ProtocolException error)
            => U2R2OutboundFrame.Control(
                OperationToken(operation)
                + ":"
                + requestId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + error.ErrorCode,
                Bytes("ee"));

        private static string OperationToken(U2R2Operation operation)
        {
            switch (operation)
            {
                case U2R2Operation.SubscriptionReady:
                    return "subscription_ready";
                case U2R2Operation.SubscriptionRemoved:
                    return "subscription_removed";
                default:
                    throw new InvalidOperationException(
                        "Unexpected contract response operation.");
            }
        }

        private static void EnqueueControl(
            U2R2BoundedOutboundScheduler scheduler,
            string token)
        {
            Assert.True(scheduler.TryReserveControl(1, out var reservation));
            reservation.Commit(U2R2OutboundFrame.Control(token, Bytes("01")));
        }

        private static U2R2OutboundFrame DrainOne(
            U2R2BoundedOutboundScheduler scheduler)
        {
            Assert.True(scheduler.TryBeginWrite(out var writer));
            var frame = writer.Frame;
            writer.Dispose();
            return frame;
        }

        private static string[] DrainAll(U2R2BoundedOutboundScheduler scheduler)
        {
            var tokens = new List<string>();
            while (scheduler.TryBeginWrite(out var writer))
            {
                tokens.Add(writer.Frame.Token);
                writer.Dispose();
            }
            return tokens.ToArray();
        }

        private static U2R2ProtocolLimits LimitsFrom(JObject source)
            => U2R2ProtocolLimits.FromDiagnosticSnapshot(
                source.Properties().ToDictionary(
                    property => property.Name,
                    property => property.Value.Value<ulong>(),
                    StringComparer.Ordinal));

        private static U2R2ReplayDecision ParseReplayDecision(string value)
            => value == "replay_cached"
                ? U2R2ReplayDecision.ReplayCached
                : throw new InvalidOperationException(value);

        private static U2R2MessageAdmission ParseMessageAdmission(string value)
            => value == "late_tombstone"
                ? U2R2MessageAdmission.LateTombstone
                : throw new InvalidOperationException(value);

        private static U2R2EnqueueDisposition ParseEnqueueResult(string value)
        {
            switch (value)
            {
                case "dropped_oldest":
                    return U2R2EnqueueDisposition.DroppedOldest;
                case "replaced_latest":
                    return U2R2EnqueueDisposition.ReplacedLatest;
                default:
                    throw new InvalidOperationException(value);
            }
        }

        private static U2R2PureSessionState ParseLifecycleState(string value)
            => value == "closed"
                ? U2R2PureSessionState.Closed
                : throw new InvalidOperationException(value);

        private static U2R2TimeoutKind ParseTimeoutKind(string value)
        {
            switch (value)
            {
                case "handshake":
                    return U2R2TimeoutKind.Handshake;
                case "partial_frame":
                    return U2R2TimeoutKind.PartialFrame;
                case "read":
                    return U2R2TimeoutKind.Read;
                case "write":
                    return U2R2TimeoutKind.Write;
                case "join":
                    return U2R2TimeoutKind.Join;
                case "shutdown":
                    return U2R2TimeoutKind.Shutdown;
                default:
                    throw new InvalidOperationException(value);
            }
        }

        private static void AssertProtocolError(JObject scenario, Action action)
            => AssertProtocolError(
                scenario.Value<string>("expectedErrorCode"),
                scenario.Value<bool>("terminal"),
                action);

        private static void AssertProtocolError(
            string code,
            bool terminal,
            Action action)
        {
            var exception = Assert.Throws<U2R2ProtocolException>(action);
            Assert.Equal(code, exception.ErrorCode);
            Assert.Equal(terminal, exception.Terminal);
        }

        private static ulong ParseUlong(JToken token)
            => ulong.Parse(token.Value<string>(), CultureInfo.InvariantCulture);

        private static byte[] Bytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            return bytes;
        }

        private static JObject LoadAuthority()
            => Assert.IsType<JObject>(
                JObject.Parse(File.ReadAllText(FindFixture()))["v2"]["commit2"]);

        private static JObject LoadFixture()
            => JObject.Parse(File.ReadAllText(FindFixture()));

        private static string FindFixture()
        {
            const string relative =
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/"
                + "u2r2_protocol_vectors.json";
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "Packages"))
                        && Directory.Exists(Path.Combine(current.FullName, "Tools")))
                    {
                        return Path.Combine(
                            current.FullName,
                            relative.Replace('/', Path.DirectorySeparatorChar));
                    }
                    current = current.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate the U2R2 authority fixture.");
        }
    }
}
