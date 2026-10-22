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

    }
}
