// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: First-acquire/last-release subscription wire ownership.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Unity2Foxglove.Ros2Bridge
{
    internal readonly struct Ros2BridgeReconnectReplayResult
    {
        internal Ros2BridgeReconnectReplayResult(
            int attempted,
            int ready,
            int skippedReleased,
            int rejected)
        {
            Attempted = attempted;
            Ready = ready;
            SkippedReleased = skippedReleased;
            Rejected = rejected;
        }

        internal int Attempted { get; }

        internal int Ready { get; }

        internal int SkippedReleased { get; }

        internal int Rejected { get; }
    }

    internal sealed class Ros2BridgeContractLeaseRegistry :
        IDisposable
    {
        private enum EntryState : byte
        {
            Registering = 1,
            Ready = 2,
        }

        private sealed class Entry
        {
            internal Ros2BridgeSessionContract Contract;
            internal EntryState State;
            internal bool CleanupAfterRegistration;
            internal Dictionary<long, Lease> Leases =
                new Dictionary<long, Lease>();
        }

        private sealed class Lease :
            IRos2BridgeContractLease
        {
            private readonly Ros2BridgeContractLeaseRegistry _owner;
            private int _released;

            internal Lease(
                Ros2BridgeContractLeaseRegistry owner,
                Ros2BridgeSessionContract contract,
                long identity)
            {
                _owner = owner;
                Contract = contract;
                LeaseIdentity = identity;
            }

            public Ros2BridgeSessionContract Contract { get; }

            public long LeaseIdentity { get; }

            public bool IsReleased
                => Volatile.Read(ref _released) != 0;

            internal bool TryMarkReleased()
                => Interlocked.Exchange(ref _released, 1) == 0;

            internal bool BelongsTo(
                Ros2BridgeContractLeaseRegistry owner)
                => ReferenceEquals(_owner, owner);

            public void Dispose()
                => _owner.TryRelease(this, out _);
        }

        private readonly object _gate = new object();
        private readonly ulong _generation;
        private readonly int _capacity;
        private readonly Ros2BridgeSessionState _sessionState;
        private readonly IRos2BridgeContractWireController _wire;
        private readonly Dictionary<string, Entry> _byBinding =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, Entry> _byContractId =
            new Dictionary<ulong, Entry>();
        private long _nextLeaseIdentity;
        private int _activeLeaseCount;
        private bool _disposed;

        internal Ros2BridgeContractLeaseRegistry(
            ulong generation,
            int capacity,
            Ros2BridgeSessionState sessionState,
            IRos2BridgeContractWireController wire)
        {
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _generation = generation;
            _capacity = capacity;
            _sessionState = sessionState
                ?? throw new ArgumentNullException(nameof(sessionState));
            _wire = wire
                ?? throw new ArgumentNullException(nameof(wire));
            if (_sessionState.Settings.Generation != generation)
            {
                throw new ArgumentException(
                    "The lease registry and session state generations differ.",
                    nameof(sessionState));
            }
        }

        internal int ActiveLeaseCount
        {
            get
            {
                lock (_gate)
                    return _activeLeaseCount;
            }
        }

        internal bool TryAcquire(
            Ros2BridgeSessionContract contract,
            out IRos2BridgeContractLease lease,
            out string reason)
        {
            lease = null;
            if (!ValidateContract(contract, out reason))
                return false;

            Entry entry;
            Lease created;
            var first = false;
            lock (_gate)
            {
                if (_disposed)
                {
                    reason = "The Bridge lease registry is disposed.";
                    return false;
                }
                if (_byBinding.TryGetValue(
                        contract.BindingId,
                        out entry))
                {
                    if (!entry.Contract.Equals(contract))
                    {
                        reason =
                            "The Bridge binding identity conflicts with an active contract.";
                        return false;
                    }
                    if (entry.State != EntryState.Ready)
                    {
                        reason =
                            "The Bridge contract registration is still pending.";
                        return false;
                    }
                }
                else
                {
                    if (_byBinding.Count >= _capacity)
                    {
                        reason =
                            "The Bridge contract lease capacity is exhausted.";
                        return false;
                    }
                    if (_byContractId.ContainsKey(
                            contract.ContractId))
                    {
                        reason =
                            "The Bridge contract ID conflicts with an active binding.";
                        return false;
                    }
                    entry = new Entry
                    {
                        Contract = contract,
                        State = EntryState.Registering,
                    };
                    _byBinding.Add(contract.BindingId, entry);
                    _byContractId.Add(contract.ContractId, entry);
                    first = true;
                }

                if (_nextLeaseIdentity == long.MaxValue)
                {
                    if (first)
                        RemoveEntryLocked(entry);
                    reason =
                        "The Bridge lease identity counter is exhausted.";
                    return false;
                }
                var identity = ++_nextLeaseIdentity;
                created = new Lease(this, entry.Contract, identity);
                entry.Leases.Add(identity, created);
                _activeLeaseCount++;
            }

            if (first)
            {
                if (!_sessionState.TryActivateLocal(
                        contract,
                        out reason))
                {
                    RollBackFirst(entry, created);
                    return false;
                }

                Ros2BridgeSessionResult registration;
                try
                {
                    registration = _wire.Register(contract);
                }
                catch (Exception exception)
                {
                    registration = Ros2BridgeSessionResult.Fault(
                        exception.Message);
                }
                if (!registration.IsAccepted)
                {
                    _sessionState.TryRevokeLocal(
                        contract,
                        out _);
                    RollBackFirst(entry, created);
                    reason = string.IsNullOrWhiteSpace(
                        registration.Reason)
                        ? "The Bridge wire registration was rejected."
                        : registration.Reason;
                    return false;
                }

                var stoppedDuringRegistration = false;
                var cleanupAfterRegistration = false;
                lock (_gate)
                {
                    if (_disposed
                        || !_byBinding.TryGetValue(
                            contract.BindingId,
                            out var current)
                        || !ReferenceEquals(current, entry))
                    {
                        stoppedDuringRegistration = true;
                        cleanupAfterRegistration =
                            entry.CleanupAfterRegistration;
                    }
                    else
                    {
                        entry.State = EntryState.Ready;
                    }
                }
                if (stoppedDuringRegistration)
                {
                    var cleanupFailure = string.Empty;
                    if (cleanupAfterRegistration)
                    {
                        _sessionState.TryRevokeLocal(
                            contract,
                            out _);
                        try
                        {
                            var cleanup =
                                _wire.Unregister(contract);
                            if (!cleanup.IsAccepted)
                            {
                                cleanupFailure =
                                    cleanup.Reason;
                            }
                        }
                        catch (Exception exception)
                        {
                            cleanupFailure =
                                exception.Message;
                        }
                    }
                    created.TryMarkReleased();
                    reason =
                        "The Bridge lease registry stopped during registration."
                        + (string.IsNullOrWhiteSpace(cleanupFailure)
                            ? string.Empty
                            : " Cleanup failed: "
                              + cleanupFailure);
                    return false;
                }
            }

            lease = created;
            reason = string.Empty;
            return true;
        }

        internal bool TryRelease(
            IRos2BridgeContractLease lease,
            out string reason)
        {
            if (!(lease is Lease owned)
                || !owned.BelongsTo(this))
            {
                reason =
                    "The Bridge lease belongs to another owner.";
                return false;
            }

            Ros2BridgeSessionContract unregister = null;
            lock (_gate)
            {
                if (owned.IsReleased)
                {
                    reason =
                        "The Bridge lease is already released.";
                    return false;
                }
                if (!_byBinding.TryGetValue(
                        owned.Contract.BindingId,
                        out var entry)
                    || !ReferenceEquals(
                        entry.Contract,
                        owned.Contract)
                    || !entry.Leases.Remove(
                        owned.LeaseIdentity))
                {
                    reason =
                        "The Bridge lease registry has no matching active lease.";
                    return false;
                }
                if (!owned.TryMarkReleased())
                {
                    reason =
                        "The Bridge lease is already released.";
                    return false;
                }
                _activeLeaseCount--;
                if (entry.Leases.Count == 0)
                {
                    unregister = entry.Contract;
                    RemoveEntryLocked(entry);
                }
            }

            if (unregister == null)
            {
                reason = string.Empty;
                return true;
            }

            if (!_sessionState.TryRevokeLocal(
                    unregister,
                    out var revokeReason))
            {
                reason = revokeReason;
                return false;
            }

            Ros2BridgeSessionResult result;
            try
            {
                result = _wire.Unregister(unregister);
            }
            catch (Exception exception)
            {
                result = Ros2BridgeSessionResult.Fault(
                    exception.Message);
            }
            reason = result.IsAccepted
                ? string.Empty
                : string.IsNullOrWhiteSpace(result.Reason)
                    ? "The Bridge wire unregister was rejected."
                    : result.Reason;
            return result.IsAccepted;
        }

        internal Ros2BridgeSessionContractSnapshot
            CaptureSnapshot()
        {
            Ros2BridgeSessionContract[] contracts;
            lock (_gate)
            {
                contracts = _byBinding.Values
                    .Where(entry => entry.State == EntryState.Ready)
                    .Select(entry => entry.Contract)
                    .ToArray();
            }
            return new Ros2BridgeSessionContractSnapshot(
                _generation,
                contracts);
        }

        internal Ros2BridgeReconnectReplayResult ReplayCurrent(
            Ros2BridgeReconnectSnapshot reconnect,
            IRos2BridgeContractWireController wire)
        {
            if (reconnect == null)
                throw new ArgumentNullException(nameof(reconnect));
            if (wire == null)
                throw new ArgumentNullException(nameof(wire));
            if (reconnect.Settings.Generation != _generation
                || reconnect.Contracts.Generation != _generation)
            {
                throw new ArgumentException(
                    "The reconnect snapshot belongs to another lease registry.",
                    nameof(reconnect));
            }

            var attempted = 0;
            var ready = 0;
            var skipped = 0;
            var rejected = 0;
            foreach (var contract in reconnect.Contracts.Contracts)
            {
                if (!IsCurrentReadyContract(contract))
                {
                    skipped++;
                    continue;
                }

                attempted++;
                Ros2BridgeSessionResult registration;
                try
                {
                    registration = wire.Register(contract);
                }
                catch (Exception exception)
                {
                    registration = Ros2BridgeSessionResult.Fault(
                        exception.Message);
                }
                if (!registration.IsAccepted)
                {
                    rejected++;
                    continue;
                }

                if (!IsCurrentReadyContract(contract)
                    || !_sessionState.TryMarkSubscriptionReady(
                        reconnect.AttemptGeneration,
                        contract,
                        out _))
                {
                    skipped++;
                    Ros2BridgeSessionResult cleanup;
                    try
                    {
                        cleanup = wire.Unregister(contract);
                    }
                    catch (Exception exception)
                    {
                        cleanup = Ros2BridgeSessionResult.Fault(
                            exception.Message);
                    }
                    if (!cleanup.IsAccepted)
                        rejected++;
                    continue;
                }
                ready++;
            }

            return new Ros2BridgeReconnectReplayResult(
                attempted,
                ready,
                skipped,
                rejected);
        }

        public void Dispose()
        {
            Entry[] entries;
            Entry[] readyEntries;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                entries = _byBinding.Values.ToArray();
                foreach (var entry in entries)
                {
                    foreach (var lease in entry.Leases.Values)
                        lease.TryMarkReleased();
                    if (entry.State == EntryState.Registering)
                        entry.CleanupAfterRegistration = true;
                }
                readyEntries = entries
                    .Where(entry => entry.State == EntryState.Ready)
                    .ToArray();
                _byBinding.Clear();
                _byContractId.Clear();
                _activeLeaseCount = 0;
            }

            Exception firstFailure = null;
            foreach (var entry in entries)
            {
                _sessionState.TryRevokeLocal(
                    entry.Contract,
                    out _);
            }
            foreach (var entry in readyEntries)
            {
                try
                {
                    var result =
                        _wire.Unregister(entry.Contract);
                    if (!result.IsAccepted)
                    {
                        firstFailure ??=
                            new InvalidOperationException(
                                string.IsNullOrWhiteSpace(
                                    result.Reason)
                                    ? "The Bridge wire unregister was rejected during disposal."
                                    : result.Reason);
                    }
                }
                catch (Exception exception)
                {
                    firstFailure ??= exception;
                }
            }
            if (firstFailure != null)
                throw firstFailure;
        }

        private bool ValidateContract(
            Ros2BridgeSessionContract contract,
            out string reason)
        {
            if (contract == null)
            {
                reason = "The Bridge contract is null.";
                return false;
            }
            if (contract.Generation != _generation)
            {
                reason =
                    "The Bridge contract belongs to another session generation.";
                return false;
            }
            if (contract.Direction
                != Unity.FoxgloveSDK.Components
                    .FoxRunTransportDirection.Subscribe)
            {
                reason =
                    "The Bridge lease registry accepts subscription contracts only.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void RollBackFirst(
            Entry entry,
            Lease lease)
        {
            lock (_gate)
            {
                if (_byBinding.TryGetValue(
                        entry.Contract.BindingId,
                        out var current)
                    && ReferenceEquals(current, entry))
                {
                    RemoveEntryLocked(entry);
                }
                if (entry.Leases.Remove(lease.LeaseIdentity))
                    _activeLeaseCount--;
                lease.TryMarkReleased();
            }
        }

        private void RemoveEntryLocked(Entry entry)
        {
            _byBinding.Remove(entry.Contract.BindingId);
            _byContractId.Remove(entry.Contract.ContractId);
        }

        private bool IsCurrentReadyContract(
            Ros2BridgeSessionContract contract)
        {
            lock (_gate)
            {
                return !_disposed
                       && _byBinding.TryGetValue(
                           contract.BindingId,
                           out var entry)
                       && entry.State == EntryState.Ready
                       && entry.Contract.Equals(contract);
            }
        }
    }
}
