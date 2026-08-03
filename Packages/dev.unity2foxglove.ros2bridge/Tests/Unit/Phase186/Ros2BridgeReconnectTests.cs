// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first reconnect snapshot and non-resurrection checks.

using System;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeReconnectTests
    {
        [Fact]
        public void ReleaseDuringReconnectCannotBecomeReadyOrResolveInbound()
        {
            var state = SessionState();
            var wire = new AcceptingWireController();
            using var registry = new Ros2BridgeContractLeaseRegistry(
                generation: 7,
                capacity: 4,
                state,
                wire);
            var retained = Contract(11, "binding-a", "/phase186/a");
            var released = Contract(12, "binding-b", "/phase186/b");
            Assert.True(registry.TryAcquire(
                retained,
                out var retainedLease,
                out _));
            Assert.True(registry.TryAcquire(
                released,
                out var releasedLease,
                out _));

            var reconnect = state.BeginReconnect(
                registry.CaptureSnapshot());
            var wireSession = WireSession(
                sessionId: "phase186-session",
                connectionGeneration: 19);
            Assert.True(state.TryCompleteHandshake(
                reconnect.AttemptGeneration,
                wireSession,
                out _));
            Assert.True(registry.TryRelease(releasedLease, out _));

            Assert.False(state.TryMarkSubscriptionReady(
                reconnect.AttemptGeneration,
                released,
                out var releasedReason));
            Assert.Contains("released", releasedReason);
            Assert.True(state.TryMarkSubscriptionReady(
                reconnect.AttemptGeneration,
                retained,
                out _));

            Assert.Equal(
                Ros2BridgeSessionResultState.Accepted,
                state.TryResolveInbound(
                    Message(
                        retained,
                        "phase186-session",
                        connectionGeneration: 19),
                    out var resolved).State);
            Assert.Same(retained, resolved);
            Assert.Equal(
                Ros2BridgeSessionResultState.Rejected,
                state.TryResolveInbound(
                    Message(
                        released,
                        "phase186-session",
                        connectionGeneration: 19),
                    out _).State);

            Assert.True(registry.TryRelease(retainedLease, out _));
        }

        [Fact]
        public void NewReconnectInvalidatesOldAttemptAndOldWireGeneration()
        {
            var state = SessionState();
            var contract = Contract(11, "binding-a", "/phase186/a");
            Assert.True(state.TryActivateLocal(contract, out _));
            var contracts = new Ros2BridgeSessionContractSnapshot(
                generation: 7,
                new[] { contract });
            var first = state.BeginReconnect(contracts);
            Assert.True(state.TryCompleteHandshake(
                first.AttemptGeneration,
                WireSession("phase186-first", 19),
                out _));
            Assert.True(state.TryMarkSubscriptionReady(
                first.AttemptGeneration,
                contract,
                out _));

            var second = state.BeginReconnect(contracts);
            Assert.True(state.TryCompleteHandshake(
                second.AttemptGeneration,
                WireSession("phase186-second", 20),
                out _));
            Assert.False(state.TryMarkSubscriptionReady(
                first.AttemptGeneration,
                contract,
                out var oldAttemptReason));
            Assert.Contains("attempt", oldAttemptReason);
            Assert.True(state.TryMarkSubscriptionReady(
                second.AttemptGeneration,
                contract,
                out _));

            Assert.Equal(
                Ros2BridgeSessionResultState.Faulted,
                state.TryResolveInbound(
                    Message(contract, "phase186-first", 19),
                    out _).State);
            Assert.Equal(
                Ros2BridgeSessionResultState.Accepted,
                state.TryResolveInbound(
                    Message(contract, "phase186-second", 20),
                    out _).State);
        }

        [Fact]
        public void PublisherAndSubscriptionReplayStateAreIndependent()
        {
            var state = SessionState();
            var subscription = Contract(
                11,
                "binding-sub",
                "/phase186/sub");
            Assert.True(state.TryActivateLocal(subscription, out _));
            var reconnect = state.BeginReconnect(
                new Ros2BridgeSessionContractSnapshot(
                    generation: 7,
                    new[] { subscription }));
            Assert.True(state.TryCompleteHandshake(
                reconnect.AttemptGeneration,
                WireSession("phase186-session", 19),
                out _));

            Assert.True(state.TryMarkPublisherReady(
                reconnect.AttemptGeneration,
                "binding-publish",
                out _));
            Assert.True(state.TryMarkSubscriptionReady(
                reconnect.AttemptGeneration,
                subscription,
                out _));
            Assert.True(state.TryRevokeLocal(subscription, out _));

            Assert.True(state.IsPublisherReady("binding-publish"));
            Assert.False(state.IsSubscriptionReady(
                subscription.ContractId));
        }

        [Fact]
        public void SessionSettingsAndReconnectSnapshotsAreImmutable()
        {
            var settings = new Ros2BridgeSessionSettings(
                "localhost",
                port: 8765,
                generation: 7,
                U2R2ProtocolLimits.Default);
            var state = new Ros2BridgeSessionState(settings);
            var contract = Contract(11, "binding-a", "/phase186/a");
            Assert.True(state.TryActivateLocal(contract, out _));

            var reconnect = state.BeginReconnect(
                new Ros2BridgeSessionContractSnapshot(
                    generation: 7,
                    new[] { contract }));
            Assert.Equal("127.0.0.1", reconnect.Settings.Host);
            Assert.Equal(8765, reconnect.Settings.Port);
            Assert.Equal(7UL, reconnect.Settings.Generation);
            Assert.Same(
                U2R2ProtocolLimits.Default,
                reconnect.Settings.Limits);
            Assert.Single(reconnect.Contracts.Contracts);

            Assert.True(state.TryRevokeLocal(contract, out _));
            Assert.Single(reconnect.Contracts.Contracts);
            Assert.Empty(
                state.BeginReconnect(
                        new Ros2BridgeSessionContractSnapshot(
                            generation: 7,
                            Array.Empty<Ros2BridgeSessionContract>()))
                    .Contracts.Contracts);
        }

        [Fact]
        public void ReplaySkipsReleasedContractAndIsolatesRejectedContract()
        {
            var state = SessionState();
            var initialWire = new AcceptingWireController();
            using var registry = new Ros2BridgeContractLeaseRegistry(
                generation: 7,
                capacity: 4,
                state,
                initialWire);
            var rejected = Contract(
                11,
                "binding-rejected",
                "/phase186/rejected");
            var ready = Contract(
                12,
                "binding-ready",
                "/phase186/ready");
            var released = Contract(
                13,
                "binding-released",
                "/phase186/released");
            Assert.True(registry.TryAcquire(
                rejected,
                out var rejectedLease,
                out _));
            Assert.True(registry.TryAcquire(
                ready,
                out var readyLease,
                out _));
            Assert.True(registry.TryAcquire(
                released,
                out var releasedLease,
                out _));
            var reconnect = state.BeginReconnect(
                registry.CaptureSnapshot());
            Assert.True(state.TryCompleteHandshake(
                reconnect.AttemptGeneration,
                WireSession("phase186-replay", 21),
                out _));
            Assert.True(registry.TryRelease(
                releasedLease,
                out _));
            var replayWire = new SelectiveWireController(
                rejected.ContractId);

            var result = registry.ReplayCurrent(
                reconnect,
                replayWire);

            Assert.Equal(2, result.Attempted);
            Assert.Equal(1, result.Ready);
            Assert.Equal(1, result.SkippedReleased);
            Assert.Equal(1, result.Rejected);
            Assert.False(state.IsSubscriptionReady(
                rejected.ContractId));
            Assert.True(state.IsSubscriptionReady(
                ready.ContractId));
            Assert.False(state.IsSubscriptionReady(
                released.ContractId));
            Assert.True(registry.TryRelease(
                rejectedLease,
                out _));
            Assert.True(registry.TryRelease(
                readyLease,
                out _));
        }

        private static Ros2BridgeSessionState SessionState()
            => new Ros2BridgeSessionState(
                new Ros2BridgeSessionSettings(
                    "127.0.0.1",
                    port: 8765,
                    generation: 7,
                    U2R2ProtocolLimits.Default));

        private static Ros2BridgeV2SessionSnapshot WireSession(
            string sessionId,
            ulong connectionGeneration)
            => new Ros2BridgeV2SessionSnapshot(
                sessionId,
                connectionGeneration,
                new[]
                {
                    U2R2Capability.Publish,
                    U2R2Capability.Subscribe,
                },
                U2R2ProtocolLimits.Default);

        private static U2R2Message Message(
            Ros2BridgeSessionContract contract,
            string sessionId,
            ulong connectionGeneration)
        {
            var header = new Newtonsoft.Json.Linq.JObject
            {
                ["connectionGeneration"] = connectionGeneration,
                ["contractId"] = contract.ContractId,
                ["encoding"] = "cdr",
                ["messageId"] = 1,
                ["op"] = "message",
                ["protocolVersion"] = 2,
                ["receiveTimeNs"] = 1,
                ["representation"] = "xcdr1-le",
                ["schemaName"] = contract.CanonicalRosType,
                ["sequence"] = 1,
                ["sessionId"] = sessionId,
                ["topic"] = contract.Topic,
            };
            return U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(
                    U2R2ProtocolCodec.EncodeFrame(
                        header,
                        new byte[] { 0, 1, 0, 0 })));
        }

        private static Ros2BridgeSessionContract Contract(
            ulong contractId,
            string bindingId,
            string topic)
            => new Ros2BridgeSessionContract(
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge"),
                FoxRunTransportDirection.Subscribe,
                topic,
                "phase186_msgs/msg/Reconnect",
                FoxRunResolvedQos.Default,
                bindingId,
                contractId,
                generation: 7);

        private sealed class AcceptingWireController :
            IRos2BridgeContractWireController
        {
            public Ros2BridgeSessionResult Register(
                Ros2BridgeSessionContract contract)
                => Ros2BridgeSessionResult.Accepted();

            public Ros2BridgeSessionResult Unregister(
                Ros2BridgeSessionContract contract)
                => Ros2BridgeSessionResult.Accepted();
        }

        private sealed class SelectiveWireController :
            IRos2BridgeContractWireController
        {
            private readonly ulong _rejectedContractId;

            internal SelectiveWireController(
                ulong rejectedContractId)
            {
                _rejectedContractId = rejectedContractId;
            }

            public Ros2BridgeSessionResult Register(
                Ros2BridgeSessionContract contract)
                => contract.ContractId
                   == _rejectedContractId
                    ? Ros2BridgeSessionResult.Reject(
                        "isolated rejection")
                    : Ros2BridgeSessionResult.Accepted();

            public Ros2BridgeSessionResult Unregister(
                Ros2BridgeSessionContract contract)
                => Ros2BridgeSessionResult.Accepted();
        }
    }
}
