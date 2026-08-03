// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Main-thread dispatch over shared bounded Bridge subscription leases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal enum Ros2BridgeGeneratedSubscriptionState : byte
    {
        Pending = 1,
        Active = 2,
        Unavailable = 3,
        Rejected = 4,
        Faulted = 5,
        Stopped = 6,
    }

    internal readonly struct Ros2BridgeGeneratedSubscriptionSnapshot
    {
        internal Ros2BridgeGeneratedSubscriptionSnapshot(
            ulong contractId,
            Ros2BridgeGeneratedSubscriptionState state,
            long attempts,
            long acceptedLeases,
            long releasedLeases,
            int activeLeases,
            long receivedFrames,
            long appliedFrames,
            long rejectedAttempts,
            long unavailableAttempts,
            long failedFrames,
            long stateTransitions,
            string lastReason)
        {
            ContractId = contractId;
            State = state;
            Attempts = attempts;
            AcceptedLeases = acceptedLeases;
            ReleasedLeases = releasedLeases;
            ActiveLeases = activeLeases;
            ReceivedFrames = receivedFrames;
            AppliedFrames = appliedFrames;
            RejectedAttempts = rejectedAttempts;
            UnavailableAttempts = unavailableAttempts;
            FailedFrames = failedFrames;
            StateTransitions = stateTransitions;
            LastReason = lastReason ?? string.Empty;
        }

        internal ulong ContractId { get; }
        internal Ros2BridgeGeneratedSubscriptionState State { get; }
        internal long Attempts { get; }
        internal long AcceptedLeases { get; }
        internal long ReleasedLeases { get; }
        internal int ActiveLeases { get; }
        internal long ReceivedFrames { get; }
        internal long AppliedFrames { get; }
        internal long RejectedAttempts { get; }
        internal long UnavailableAttempts { get; }
        internal long FailedFrames { get; }
        internal long StateTransitions { get; }
        internal string LastReason { get; }
    }

    internal sealed class Ros2BridgeGeneratedSubscriptionRuntime :
        IDisposable
    {
        private sealed class ContractObservation
        {
            internal ContractObservation(ulong contractId, ulong touched)
            {
                ContractId = contractId;
                State = Ros2BridgeGeneratedSubscriptionState.Pending;
                LastTouched = touched;
            }

            internal ulong ContractId;
            internal Ros2BridgeGeneratedSubscriptionState State;
            internal long Attempts;
            internal long AcceptedLeases;
            internal long ReleasedLeases;
            internal int ActiveLeases;
            internal long ReceivedFrames;
            internal long AppliedFrames;
            internal long RejectedAttempts;
            internal long UnavailableAttempts;
            internal long FailedFrames;
            internal long StateTransitions;
            internal string LastReason = string.Empty;
            internal ulong LastTouched;

            internal Ros2BridgeGeneratedSubscriptionSnapshot Snapshot()
                => new Ros2BridgeGeneratedSubscriptionSnapshot(
                    ContractId,
                    State,
                    Attempts,
                    AcceptedLeases,
                    ReleasedLeases,
                    ActiveLeases,
                    ReceivedFrames,
                    AppliedFrames,
                    RejectedAttempts,
                    UnavailableAttempts,
                    FailedFrames,
                    StateTransitions,
                    LastReason);
        }

        private sealed class SubscriptionLease :
            IFoxRunTransportSubscriptionLease
        {
            private Ros2BridgeGeneratedSubscriptionRuntime _owner;
            private IRos2BridgeContractLease _physicalLease;
            private int _released;

            internal SubscriptionLease(
                Ros2BridgeGeneratedSubscriptionRuntime owner,
                Ros2BridgeSessionContract contract,
                FoxRunTransportSubscribeRoute route,
                IRos2BridgeContractLease physicalLease)
            {
                _owner = owner;
                Contract = contract;
                Route = route;
                _physicalLease = physicalLease;
            }

            public FoxRunTransportId Id => Contract.ProviderId;

            public ulong Generation => Contract.Generation;

            internal Ros2BridgeSessionContract Contract { get; }

            internal FoxRunTransportSubscribeRoute Route { get; }

            internal bool IsReleased
                => Volatile.Read(ref _released) != 0;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;
                var owner = Interlocked.Exchange(ref _owner, null);
                var physical = Interlocked.Exchange(
                    ref _physicalLease,
                    null);
                owner?.Release(this);
                physical?.Dispose();
            }
        }

        private readonly object _gate = new object();
        private readonly Ros2BridgeRuntime _runtime;
        private readonly FoxRunTransportId _providerId;
        private readonly ulong _generation;
        private readonly Dictionary<ulong, List<SubscriptionLease>>
            _byContractId =
                new Dictionary<ulong, List<SubscriptionLease>>();
        private readonly Dictionary<ulong, ContractObservation>
            _observations =
                new Dictionary<ulong, ContractObservation>();
        private readonly int _observationCapacity =
            checked((int)U2R2ProtocolLimits.Default.MaxContracts);
        private ulong _observationClock;
        private bool _disposed;

        internal Ros2BridgeGeneratedSubscriptionRuntime(
            Ros2BridgeRuntime runtime,
            FoxRunTransportId providerId,
            ulong generation)
        {
            _runtime = runtime
                ?? throw new ArgumentNullException(nameof(runtime));
            _providerId = providerId;
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            _generation = generation;
        }

        internal FoxRunTransportSubscribeResult Subscribe(
            in FoxRunTransportSubscribeRoute route)
        {
            var contractId = BuildContractId(route);
            if (!TryBeginObservation(
                    contractId,
                    out var observation,
                    out var observationReason))
            {
                return FoxRunTransportSubscribeResult.Rejected(
                    observationReason);
            }
            if (!string.Equals(
                    route.MessageEncoding,
                    "cdr",
                    StringComparison.Ordinal))
            {
                const string reason =
                    "ROS2 Bridge subscriptions require exact 'cdr' encoding.";
                CompleteAttempt(
                    observation,
                    Ros2BridgeGeneratedSubscriptionState.Rejected,
                    reason);
                return FoxRunTransportSubscribeResult.Rejected(reason);
            }
            if (string.IsNullOrWhiteSpace(route.LogicalSchemaName))
            {
                const string reason =
                    "ROS2 Bridge subscriptions require a canonical ROS message type.";
                CompleteAttempt(
                    observation,
                    Ros2BridgeGeneratedSubscriptionState.Rejected,
                    reason);
                return FoxRunTransportSubscribeResult.Rejected(reason);
            }
            if (route.MaxPayloadBytes <= 0
                || route.MaxPayloadBytes
                > Ros2BridgeFrameWriter.MaxPayloadBytes)
            {
                const string reason =
                    "ROS2 Bridge subscription payload bounds exceed the wire limit.";
                CompleteAttempt(
                    observation,
                    Ros2BridgeGeneratedSubscriptionState.Rejected,
                    reason);
                return FoxRunTransportSubscribeResult.Rejected(reason);
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    const string reason =
                        "ROS2 Bridge subscription runtime has ended.";
                    CompleteAttemptLocked(
                        observation,
                        Ros2BridgeGeneratedSubscriptionState.Stopped,
                        reason);
                    return FoxRunTransportSubscribeResult.Unavailable(reason);
                }
            }

            Ros2BridgeSessionContract contract;
            try
            {
                contract = new Ros2BridgeSessionContract(
                    _providerId,
                    FoxRunTransportDirection.Subscribe,
                    route.Topic,
                    route.LogicalSchemaName,
                    ResolveQos(route.DeliveryPolicy),
                    BuildBindingId(route),
                    contractId,
                    _generation);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException
                || exception is OverflowException)
            {
                var reason = Bound(exception.Message);
                CompleteAttempt(
                    observation,
                    Ros2BridgeGeneratedSubscriptionState.Rejected,
                    reason);
                return FoxRunTransportSubscribeResult.Rejected(reason);
            }

            var acquired = _runtime.TryAcquireSubscription(
                contract,
                out var physicalLease);
            if (!acquired.IsAccepted || physicalLease == null)
            {
                var reason = Bound(acquired.Reason);
                var state = acquired.State switch
                {
                    Ros2BridgeSessionResultState.Rejected =>
                        Ros2BridgeGeneratedSubscriptionState.Rejected,
                    Ros2BridgeSessionResultState.Faulted =>
                        Ros2BridgeGeneratedSubscriptionState.Faulted,
                    _ => Ros2BridgeGeneratedSubscriptionState.Unavailable
                };
                CompleteAttempt(observation, state, reason);
                return acquired.State switch
                {
                    Ros2BridgeSessionResultState.Rejected =>
                        FoxRunTransportSubscribeResult.Rejected(
                            reason),
                    Ros2BridgeSessionResultState.Faulted =>
                        FoxRunTransportSubscribeResult.Failed(
                            reason),
                    _ => FoxRunTransportSubscribeResult.Unavailable(
                        reason)
                };
            }

            var lease = new SubscriptionLease(
                this,
                contract,
                route,
                physicalLease);
            var stoppedDuringRegistration = false;
            List<SubscriptionLease> subscribers = null;
            lock (_gate)
            {
                if (_disposed)
                {
                    stoppedDuringRegistration = true;
                }
                else if (!_byContractId.TryGetValue(
                        contract.ContractId,
                        out subscribers))
                {
                    subscribers = new List<SubscriptionLease>();
                    _byContractId.Add(
                        contract.ContractId,
                        subscribers);
                }
                if (!stoppedDuringRegistration)
                    subscribers.Add(lease);
            }
            if (stoppedDuringRegistration)
            {
                lease.Dispose();
                const string reason =
                    "ROS2 Bridge subscription runtime ended during registration.";
                CompleteAttempt(
                    observation,
                    Ros2BridgeGeneratedSubscriptionState.Stopped,
                    reason);
                return FoxRunTransportSubscribeResult.Unavailable(reason);
            }
            CompleteAccepted(observation);
            return FoxRunTransportSubscribeResult.Accepted(lease);
        }

        internal int Pump(int maxFrames)
        {
            if (maxFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFrames));
            var processed = 0;
            while (processed < maxFrames
                   && _runtime.TryBeginInboundApply(out var apply))
            {
                using (apply)
                {
                    processed++;
                    if (!apply.CanApply)
                    {
                        apply.MarkDecodeFailure(
                            "The Bridge inbound apply lease became stale before dispatch.");
                        continue;
                    }

                    SubscriptionLease[] subscribers;
                    lock (_gate)
                    {
                        subscribers = _disposed
                            || !_byContractId.TryGetValue(
                                apply.Frame.Contract.ContractId,
                                out var active)
                            ? Array.Empty<SubscriptionLease>()
                            : active
                                .Where(value => !value.IsReleased)
                                .ToArray();
                    }
                    RecordReceived(apply.Frame.Contract.ContractId);
                    if (subscribers.Length == 0)
                    {
                        const string reason =
                            "The Bridge inbound contract has no active generated source.";
                        RecordFault(
                            apply.Frame.Contract.ContractId,
                            reason);
                        apply.MarkDecodeFailure(reason);
                        continue;
                    }

                    Exception first = null;
                    for (var index = 0;
                         index < subscribers.Length;
                         index++)
                    {
                        if (subscribers[index].IsReleased)
                            continue;
                        if (!apply.CanApply)
                        {
                            first ??= new InvalidOperationException(
                                "The Bridge inbound apply lease became stale during dispatch.");
                            break;
                        }
                        try
                        {
                            subscribers[index].Route.OnPayload(
                                apply.Frame.Payload,
                                apply.Frame.ReceiveTimeNs,
                                apply.Frame.Sequence);
                        }
                        catch (Exception exception)
                        {
                            first ??= exception;
                        }
                    }
                    if (first == null)
                    {
                        RecordApplied(apply.Frame.Contract.ContractId);
                        apply.MarkApplied();
                    }
                    else
                    {
                        var reason = Bound(first.Message);
                        RecordFault(
                            apply.Frame.Contract.ContractId,
                            reason);
                        apply.MarkDecodeFailure(reason);
                    }
                }
            }
            return processed;
        }

        internal Ros2BridgeInboundStatsSnapshot GetStatsSnapshot()
            => _runtime.GetInboundStatsSnapshot();

        internal int ObservedContractCount
        {
            get
            {
                lock (_gate)
                    return _observations.Count;
            }
        }

        internal Ros2BridgeSubscriptionObservationSnapshot
            GetObservationSnapshot()
        {
            lock (_gate)
            {
                var observed = 0;
                var active = 0;
                var pending = 0;
                var unavailable = 0;
                var rejected = 0;
                var faulted = 0;
                var selectedContractId = ulong.MaxValue;
                var selectedReason = string.Empty;
                foreach (var observation in _observations.Values)
                {
                    if (observation.State
                            == Ros2BridgeGeneratedSubscriptionState.Stopped
                        && observation.ActiveLeases == 0)
                    {
                        continue;
                    }
                    observed++;
                    switch (observation.State)
                    {
                        case Ros2BridgeGeneratedSubscriptionState.Active:
                            active++;
                            break;
                        case Ros2BridgeGeneratedSubscriptionState.Pending:
                        case Ros2BridgeGeneratedSubscriptionState.Stopped:
                            pending++;
                            break;
                        case Ros2BridgeGeneratedSubscriptionState.Unavailable:
                            unavailable++;
                            break;
                        case Ros2BridgeGeneratedSubscriptionState.Rejected:
                            rejected++;
                            break;
                        case Ros2BridgeGeneratedSubscriptionState.Faulted:
                            faulted++;
                            break;
                    }
                    if (!string.IsNullOrWhiteSpace(observation.LastReason)
                        && observation.ContractId < selectedContractId)
                    {
                        selectedContractId = observation.ContractId;
                        selectedReason = observation.LastReason;
                    }
                }
                return new Ros2BridgeSubscriptionObservationSnapshot(
                    observed,
                    active,
                    pending,
                    unavailable,
                    rejected,
                    faulted,
                    selectedReason);
            }
        }

        internal bool TryGetContractSnapshot(
            in FoxRunTransportSubscribeRoute route,
            out Ros2BridgeGeneratedSubscriptionSnapshot snapshot)
        {
            var contractId = BuildContractId(route);
            lock (_gate)
            {
                if (_observations.TryGetValue(
                        contractId,
                        out var observation))
                {
                    snapshot = observation.Snapshot();
                    return true;
                }
            }
            snapshot = default;
            return false;
        }

        private void Release(SubscriptionLease lease)
        {
            lock (_gate)
            {
                if (!_byContractId.TryGetValue(
                        lease.Contract.ContractId,
                        out var subscribers))
                {
                    return;
                }
                subscribers.Remove(lease);
                if (subscribers.Count == 0)
                {
                    _byContractId.Remove(
                        lease.Contract.ContractId);
                }
                if (_observations.TryGetValue(
                        lease.Contract.ContractId,
                        out var observation))
                {
                    if (observation.ActiveLeases > 0)
                        observation.ActiveLeases--;
                    observation.ReleasedLeases = SaturatingIncrement(
                        observation.ReleasedLeases);
                    TouchLocked(observation);
                    if (observation.ActiveLeases == 0)
                    {
                        SetStateLocked(
                            observation,
                            Ros2BridgeGeneratedSubscriptionState.Stopped,
                            string.Empty);
                    }
                }
            }
        }

        public void Dispose()
        {
            SubscriptionLease[] leases;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                leases = _byContractId.Values
                    .SelectMany(value => value)
                    .ToArray();
                _byContractId.Clear();
                foreach (var observation in _observations.Values)
                {
                    observation.ActiveLeases = 0;
                    SetStateLocked(
                        observation,
                        Ros2BridgeGeneratedSubscriptionState.Stopped,
                        string.Empty);
                }
            }
            Exception first = null;
            for (var index = leases.Length - 1;
                 index >= 0;
                 index--)
            {
                try
                {
                    leases[index].Dispose();
                }
                catch (Exception exception)
                {
                    first ??= exception;
                }
            }
            if (first != null)
                throw first;
        }

        private string BuildBindingId(
            in FoxRunTransportSubscribeRoute route)
            => _providerId.Value
               + "/subscribe/"
               + route.StableMemberId;

        private ulong BuildContractId(
            in FoxRunTransportSubscribeRoute route)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            AppendHash(ref hash, _providerId.Value, prime);
            AppendHash(
                ref hash,
                _generation.ToString(CultureInfo.InvariantCulture),
                prime);
            AppendHash(ref hash, route.StableMemberId, prime);
            AppendHash(ref hash, route.Topic, prime);
            AppendHash(ref hash, route.LogicalSchemaName, prime);
            AppendHash(ref hash, route.MessageEncoding, prime);
            AppendHash(
                ref hash,
                ((int)route.DeliveryPolicy.Reliability).ToString(
                    CultureInfo.InvariantCulture),
                prime);
            AppendHash(
                ref hash,
                ((int)route.DeliveryPolicy.Durability).ToString(
                    CultureInfo.InvariantCulture),
                prime);
            AppendHash(
                ref hash,
                ((int)route.DeliveryPolicy.History).ToString(
                    CultureInfo.InvariantCulture),
                prime);
            AppendHash(
                ref hash,
                route.DeliveryPolicy.Depth.ToString(
                    CultureInfo.InvariantCulture),
                prime);
            return hash == 0 ? 1UL : hash;
        }

        private bool TryBeginObservation(
            ulong contractId,
            out ContractObservation observation,
            out string reason)
        {
            lock (_gate)
            {
                if (!_observations.TryGetValue(
                        contractId,
                        out observation))
                {
                    if (_observations.Count >= _observationCapacity
                        && !TryEvictTerminalObservationLocked())
                    {
                        reason =
                            "ROS2 Bridge generated subscription observation capacity is exhausted.";
                        return false;
                    }
                    observation = new ContractObservation(
                        contractId,
                        NextObservationClockLocked());
                    _observations.Add(contractId, observation);
                }
                observation.Attempts = SaturatingIncrement(
                    observation.Attempts);
                TouchLocked(observation);
                if (observation.ActiveLeases == 0)
                {
                    SetStateLocked(
                        observation,
                        _disposed
                            ? Ros2BridgeGeneratedSubscriptionState.Stopped
                            : Ros2BridgeGeneratedSubscriptionState.Pending,
                        string.Empty);
                }
                reason = string.Empty;
                return true;
            }
        }

        private void CompleteAccepted(ContractObservation observation)
        {
            lock (_gate)
            {
                observation.AcceptedLeases = SaturatingIncrement(
                    observation.AcceptedLeases);
                if (observation.ActiveLeases < int.MaxValue)
                    observation.ActiveLeases++;
                SetStateLocked(
                    observation,
                    Ros2BridgeGeneratedSubscriptionState.Active,
                    string.Empty);
            }
        }

        private void CompleteAttempt(
            ContractObservation observation,
            Ros2BridgeGeneratedSubscriptionState state,
            string reason)
        {
            lock (_gate)
                CompleteAttemptLocked(observation, state, reason);
        }

        private void CompleteAttemptLocked(
            ContractObservation observation,
            Ros2BridgeGeneratedSubscriptionState state,
            string reason)
        {
            switch (state)
            {
                case Ros2BridgeGeneratedSubscriptionState.Rejected:
                    observation.RejectedAttempts = SaturatingIncrement(
                        observation.RejectedAttempts);
                    break;
                case Ros2BridgeGeneratedSubscriptionState.Unavailable:
                    observation.UnavailableAttempts = SaturatingIncrement(
                        observation.UnavailableAttempts);
                    break;
                case Ros2BridgeGeneratedSubscriptionState.Faulted:
                    observation.FailedFrames = SaturatingIncrement(
                        observation.FailedFrames);
                    break;
            }
            if (observation.ActiveLeases == 0)
                SetStateLocked(observation, state, reason);
            else
            {
                observation.LastReason = Bound(reason);
                TouchLocked(observation);
            }
        }

        private void RecordReceived(ulong contractId)
        {
            lock (_gate)
            {
                if (_observations.TryGetValue(
                        contractId,
                        out var observation))
                {
                    observation.ReceivedFrames = SaturatingIncrement(
                        observation.ReceivedFrames);
                    TouchLocked(observation);
                }
            }
        }

        private void RecordApplied(ulong contractId)
        {
            lock (_gate)
            {
                if (_observations.TryGetValue(
                        contractId,
                        out var observation))
                {
                    observation.AppliedFrames = SaturatingIncrement(
                        observation.AppliedFrames);
                    SetStateLocked(
                        observation,
                        observation.ActiveLeases == 0
                            ? Ros2BridgeGeneratedSubscriptionState.Stopped
                            : Ros2BridgeGeneratedSubscriptionState.Active,
                        string.Empty);
                }
            }
        }

        private void RecordFault(ulong contractId, string reason)
        {
            lock (_gate)
            {
                if (_observations.TryGetValue(
                        contractId,
                        out var observation))
                {
                    observation.FailedFrames = SaturatingIncrement(
                        observation.FailedFrames);
                    SetStateLocked(
                        observation,
                        observation.ActiveLeases == 0
                            ? Ros2BridgeGeneratedSubscriptionState.Stopped
                            : Ros2BridgeGeneratedSubscriptionState.Faulted,
                        reason);
                }
            }
        }

        private bool TryEvictTerminalObservationLocked()
        {
            ContractObservation oldest = null;
            foreach (var candidate in _observations.Values)
            {
                if (candidate.ActiveLeases != 0
                    || candidate.State
                    == Ros2BridgeGeneratedSubscriptionState.Pending)
                {
                    continue;
                }
                if (oldest == null
                    || candidate.LastTouched < oldest.LastTouched)
                {
                    oldest = candidate;
                }
            }
            return oldest != null
                   && _observations.Remove(oldest.ContractId);
        }

        private void SetStateLocked(
            ContractObservation observation,
            Ros2BridgeGeneratedSubscriptionState state,
            string reason)
        {
            if (observation.State != state)
            {
                observation.State = state;
                observation.StateTransitions = SaturatingIncrement(
                    observation.StateTransitions);
            }
            observation.LastReason = string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : Bound(reason);
            TouchLocked(observation);
        }

        private void TouchLocked(ContractObservation observation)
            => observation.LastTouched = NextObservationClockLocked();

        private ulong NextObservationClockLocked()
        {
            if (_observationClock != ulong.MaxValue)
                _observationClock++;
            return _observationClock;
        }

        private static long SaturatingIncrement(long value)
            => value == long.MaxValue ? value : value + 1;

        private static void AppendHash(
            ref ulong hash,
            string value,
            ulong prime)
        {
            value ??= string.Empty;
            unchecked
            {
                hash ^= checked((ulong)value.Length);
                hash *= prime;
                for (var index = 0; index < value.Length; index++)
                {
                    hash ^= value[index];
                    hash *= prime;
                }
            }
        }

        private static FoxRunResolvedQos ResolveQos(
            FoxRunDeliveryPolicy policy)
        {
            if (policy.Equals(FoxRunDeliveryPolicy.ProviderDefault))
                return FoxRunResolvedQos.Default;
            var baseline = FoxRunResolvedQos.Default;
            var reliability = policy.Reliability switch
            {
                FoxRunDeliveryReliability.Reliable =>
                    FoxRunQosReliability.Reliable,
                FoxRunDeliveryReliability.BestEffort =>
                    FoxRunQosReliability.BestEffort,
                FoxRunDeliveryReliability.SystemDefault =>
                    FoxRunQosReliability.SystemDefault,
                _ => baseline.Reliability
            };
            var durability = policy.Durability switch
            {
                FoxRunDeliveryDurability.TransientLocal =>
                    FoxRunQosDurability.TransientLocal,
                FoxRunDeliveryDurability.SystemDefault =>
                    FoxRunQosDurability.SystemDefault,
                _ => baseline.Durability
            };
            var history = policy.History switch
            {
                FoxRunDeliveryHistory.KeepAll =>
                    FoxRunQosHistory.KeepAll,
                FoxRunDeliveryHistory.SystemDefault =>
                    FoxRunQosHistory.SystemDefault,
                _ => baseline.History
            };
            var depth = history == FoxRunQosHistory.KeepLast
                ? policy.History == FoxRunDeliveryHistory.KeepLast
                    ? Math.Max(1, policy.Depth)
                    : baseline.Depth
                : 0;
            var profile =
                reliability == FoxRunQosReliability.SystemDefault
                && durability == FoxRunQosDurability.SystemDefault
                && history == FoxRunQosHistory.SystemDefault
                    ? FoxRunQosProfile.SystemDefault
                    : FoxRunQosProfile.Default;
            return new FoxRunResolvedQos(
                profile,
                reliability,
                durability,
                history,
                depth);
        }

        private static string Bound(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return "Unspecified ROS2 Bridge subscription failure.";
            var value = reason.Trim();
            return value.Length <= 512
                ? value
                : value.Substring(0, 512);
        }
    }

    internal static class Ros2BridgeCleanup
    {
        internal static Exception RunAll(
            int count,
            Action<int> cleanup,
            bool reverse = false)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (cleanup == null)
                throw new ArgumentNullException(nameof(cleanup));

            Exception first = null;
            for (var offset = 0; offset < count; offset++)
            {
                var index = reverse
                    ? count - offset - 1
                    : offset;
                try
                {
                    cleanup(index);
                }
                catch (Exception exception)
                {
                    first ??= exception;
                }
            }
            return first;
        }
    }
}
