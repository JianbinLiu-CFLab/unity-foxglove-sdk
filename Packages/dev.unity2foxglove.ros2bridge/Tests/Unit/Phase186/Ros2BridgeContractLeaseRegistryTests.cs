// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first dynamic subscription lease ownership checks.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed class Ros2BridgeContractLeaseRegistryTests
    {
        [Fact]
        public void FirstAcquireRegistersIdenticalAcquireSharesAndLastReleaseUnregisters()
        {
            var state = SessionState();
            var wire = new RecordingWireController();
            using var registry = new Ros2BridgeContractLeaseRegistry(
                generation: 7,
                capacity: 4,
                state,
                wire);
            var contract = Contract(11, "binding-a");

            Assert.True(registry.TryAcquire(
                contract,
                out var first,
                out var firstReason));
            Assert.Equal(string.Empty, firstReason);
            Assert.True(registry.TryAcquire(
                Contract(11, "binding-a"),
                out var second,
                out var secondReason));
            Assert.Equal(string.Empty, secondReason);
            Assert.Single(wire.Registered);
            Assert.Equal(2, registry.ActiveLeaseCount);

            Assert.True(registry.TryRelease(
                first,
                out var firstReleaseReason));
            Assert.Equal(string.Empty, firstReleaseReason);
            Assert.Empty(wire.Unregistered);
            Assert.True(state.IsLocallyActive(contract));

            Assert.True(registry.TryRelease(
                second,
                out var secondReleaseReason));
            Assert.Equal(string.Empty, secondReleaseReason);
            Assert.Single(wire.Unregistered);
            Assert.False(state.IsLocallyActive(contract));
            Assert.Equal(0, registry.ActiveLeaseCount);
        }

        [Fact]
        public void ConflictingBindingDuplicateReleaseAndForeignLeaseFailClosed()
        {
            var state = SessionState();
            var wire = new RecordingWireController();
            using var registry = new Ros2BridgeContractLeaseRegistry(
                generation: 7,
                capacity: 2,
                state,
                wire);
            Assert.True(registry.TryAcquire(
                Contract(11, "binding-a"),
                out var lease,
                out _));

            Assert.False(registry.TryAcquire(
                Contract(
                    12,
                    "binding-a",
                    topic: "/phase186/conflict"),
                out _,
                out var conflictReason));
            Assert.Contains("conflict", conflictReason);
            Assert.Single(wire.Registered);

            Assert.True(registry.TryRelease(lease, out _));
            Assert.False(registry.TryRelease(
                lease,
                out var duplicateReason));
            Assert.Contains("released", duplicateReason);

            using var foreignRegistry =
                new Ros2BridgeContractLeaseRegistry(
                    generation: 7,
                    capacity: 2,
                    SessionState(),
                    new RecordingWireController());
            Assert.False(foreignRegistry.TryRelease(
                lease,
                out var foreignReason));
            Assert.Contains("owner", foreignReason);
        }

        [Fact]
        public void SnapshotContainsOnlyCurrentLeasesInStableOrder()
        {
            var state = SessionState();
            var wire = new RecordingWireController();
            using var registry = new Ros2BridgeContractLeaseRegistry(
                generation: 7,
                capacity: 4,
                state,
                wire);
            Assert.True(registry.TryAcquire(
                Contract(12, "binding-b", "/phase186/b"),
                out var second,
                out _));
            Assert.True(registry.TryAcquire(
                Contract(11, "binding-a", "/phase186/a"),
                out var first,
                out _));
            Assert.True(registry.TryRelease(second, out _));

            var snapshot = registry.CaptureSnapshot();

            Assert.Equal(
                new[] { 11UL },
                snapshot.Contracts
                    .Select(contract => contract.ContractId)
                    .ToArray());
            Assert.Equal(7UL, snapshot.Generation);
            Assert.True(registry.TryRelease(first, out _));
            Assert.Single(snapshot.Contracts);
            Assert.Empty(registry.CaptureSnapshot().Contracts);
        }

        [Fact]
        public void RegistrationFailureRollsBackLeaseAndLocalAdmission()
        {
            var state = SessionState();
            var wire = new RecordingWireController
            {
                RejectRegister = true,
            };
            using var registry = new Ros2BridgeContractLeaseRegistry(
                generation: 7,
                capacity: 1,
                state,
                wire);
            var contract = Contract(11, "binding-a");

            Assert.False(registry.TryAcquire(
                contract,
                out var lease,
                out var reason));

            Assert.Null(lease);
            Assert.Contains("rejected", reason);
            Assert.False(state.IsLocallyActive(contract));
            Assert.Equal(0, registry.ActiveLeaseCount);
            Assert.Empty(registry.CaptureSnapshot().Contracts);
        }

        [Fact]
        public void ConcurrentAcquireReportsPendingRegistrationAsUnavailable()
        {
            var state = SessionState();
            using var wire = new BlockingWireController();
            using var registry = new Ros2BridgeContractLeaseRegistry(
                generation: 7,
                capacity: 2,
                state,
                wire);
            var contract = Contract(11, "binding-a");
            Ros2BridgeSessionResult firstResult = default;
            IRos2BridgeContractLease firstLease = null;
            var first = new Thread(() =>
            {
                firstResult = registry.TryAcquire(
                    contract,
                    out firstLease);
            });
            first.Start();
            Assert.True(
                wire.RegisterEntered.Wait(TimeSpan.FromSeconds(3)));

            var pending = registry.TryAcquire(
                contract,
                out var pendingLease);

            Assert.Equal(
                Ros2BridgeSessionResultState.Unavailable,
                pending.State);
            Assert.Null(pendingLease);
            wire.AllowRegister.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(3)));
            Assert.True(firstResult.IsAccepted, firstResult.Reason);
            Assert.NotNull(firstLease);
            firstLease.Dispose();
        }

        private static Ros2BridgeSessionState SessionState()
            => new Ros2BridgeSessionState(
                new Ros2BridgeSessionSettings(
                    "127.0.0.1",
                    port: 8765,
                    generation: 7,
                    Protocol.U2R2ProtocolLimits.Default));

        private static Ros2BridgeSessionContract Contract(
            ulong contractId,
            string bindingId,
            string topic = "/phase186/lease")
            => new Ros2BridgeSessionContract(
                new FoxRunTransportId(
                    "unity2foxglove.ros2bridge"),
                FoxRunTransportDirection.Subscribe,
                topic,
                "phase186_msgs/msg/Lease",
                FoxRunResolvedQos.Default,
                bindingId,
                contractId,
                generation: 7);

        private sealed class RecordingWireController :
            IRos2BridgeContractWireController
        {
            internal List<Ros2BridgeSessionContract> Registered { get; }
                = new List<Ros2BridgeSessionContract>();

            internal List<Ros2BridgeSessionContract> Unregistered { get; }
                = new List<Ros2BridgeSessionContract>();

            internal bool RejectRegister { get; set; }

            public Ros2BridgeSessionResult Register(
                Ros2BridgeSessionContract contract)
            {
                if (RejectRegister)
                {
                    return Ros2BridgeSessionResult.Reject(
                        "wire registration rejected");
                }
                Registered.Add(contract);
                return Ros2BridgeSessionResult.Accepted();
            }

            public Ros2BridgeSessionResult Unregister(
                Ros2BridgeSessionContract contract)
            {
                Unregistered.Add(contract);
                return Ros2BridgeSessionResult.Accepted();
            }
        }

        private sealed class BlockingWireController :
            IRos2BridgeContractWireController,
            IDisposable
        {
            internal ManualResetEventSlim RegisterEntered { get; }
                = new ManualResetEventSlim(false);

            internal ManualResetEventSlim AllowRegister { get; }
                = new ManualResetEventSlim(false);

            public Ros2BridgeSessionResult Register(
                Ros2BridgeSessionContract contract)
            {
                RegisterEntered.Set();
                if (!AllowRegister.Wait(TimeSpan.FromSeconds(3)))
                {
                    return Ros2BridgeSessionResult.Fault(
                        "registration release timed out");
                }
                return Ros2BridgeSessionResult.Accepted();
            }

            public Ros2BridgeSessionResult Unregister(
                Ros2BridgeSessionContract contract)
                => Ros2BridgeSessionResult.Accepted();

            public void Dispose()
            {
                RegisterEntered.Dispose();
                AllowRegister.Dispose();
            }
        }
    }
}
