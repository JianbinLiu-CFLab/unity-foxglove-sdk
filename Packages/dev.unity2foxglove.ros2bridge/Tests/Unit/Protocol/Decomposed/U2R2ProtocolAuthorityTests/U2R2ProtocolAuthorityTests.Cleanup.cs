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

        private static void RemovalAbortRestoresReadyContract(
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
            var key = Key(scenario);
            var identity = Identity(key);
            var readyResponse = replay.Admit(
                scenario.Value<ulong>("registerRequestId"),
                RequestBytes("register_subscription", identity),
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
            var removedResponse = replay.Admit(
                scenario.Value<ulong>("unregisterRequestId"),
                RequestBytes("unregister_subscription", identity),
                1,
                scheduler);
            var removal = contracts.BeginUnregister(
                identity,
                scheduler,
                replay,
                removedResponse);

            contracts.AbortRemoval(
                removal,
                scheduler,
                replay,
                removedResponse,
                new U2R2ProtocolException(
                    scenario.Value<string>("abortErrorCode"),
                    "unregister backend timed out",
                    terminal: false));

            Assert.Equal(
                Bytes(scenario.Value<string>("abortResponseHex")),
                DrainOne(scheduler).Bytes.ToArray());
            Assert.Equal(
                scenario.Value<ulong>("expectedContractCount"),
                contracts.ContractCount);
            Assert.Equal(
                U2R2MessageAdmission.Accepted,
                contracts.AdmitMessage(identity, scenario.Value<ulong>("messageSequence")));
            Assert.Equal(
                U2R2EnqueueDisposition.Accepted,
                scheduler.EnqueueData(
                    U2R2OutboundFrame.Data("message", key, 1, Bytes("aa")),
                    U2R2QueueOverflowPolicy.Reject));
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

    }
}
