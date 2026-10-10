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

    }
}
