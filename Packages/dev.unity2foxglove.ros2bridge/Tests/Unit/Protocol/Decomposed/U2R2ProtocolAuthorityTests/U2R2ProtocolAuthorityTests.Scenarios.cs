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
                case "removal_abort_restores_ready_contract":
                    RemovalAbortRestoresReadyContract(scenario, limits);
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

    }
}
