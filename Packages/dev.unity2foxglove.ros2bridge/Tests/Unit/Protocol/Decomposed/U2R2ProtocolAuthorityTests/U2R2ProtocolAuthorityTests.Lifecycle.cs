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

    }
}
