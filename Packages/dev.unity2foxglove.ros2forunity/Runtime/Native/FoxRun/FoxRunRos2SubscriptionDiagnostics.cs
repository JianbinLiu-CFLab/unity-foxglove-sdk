// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Bounded per-contract native subscription diagnostics.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Immutable, managed-only acceptance view of one generated subscription.
    /// It contains counters only and never exposes a ROS2 message object.
    /// </summary>
    public readonly struct FoxRunRos2SubscriptionAcceptanceSnapshot
    {
        internal FoxRunRos2SubscriptionAcceptanceSnapshot(
            string topic,
            FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            Topic = topic ?? string.Empty;
            SessionGeneration = snapshot.SessionGeneration;
            State = snapshot.State;
            Received = snapshot.Received;
            Replaced = snapshot.Replaced;
            Applied = snapshot.Applied;
            Pending = snapshot.Pending;
            RejectedAfterStop = snapshot.RejectedAfterStop;
            CopyFailed = snapshot.CopyFailed;
            StaleCallbacks = snapshot.StaleCallbacks;
        }

        public string Topic { get; }
        public long SessionGeneration { get; }
        public FoxRunRos2SubscriptionBindingState State { get; }
        public long Received { get; }
        public long Replaced { get; }
        public long Applied { get; }
        public int Pending { get; }
        public long RejectedAfterStop { get; }
        public long CopyFailed { get; }
        public long StaleCallbacks { get; }

    }

    public enum FoxRunRos2AcceptanceArmStatus
    {
        Armed = 0,
        EndpointUnavailable = 1,
        AlreadyArmed = 2,
        PendingNotIdle = 3,
        CallbackInFlight = 4,
        ConcurrentCallbackRace = 5,
        Stopped = 6,
    }

    /// <summary>Managed-only counters for one explicitly armed acceptance epoch.</summary>
    public readonly struct FoxRunRos2AcceptanceAttemptSnapshot
    {
        internal FoxRunRos2AcceptanceAttemptSnapshot(
            long epoch,
            bool active,
            long received,
            long replaced,
            long applied,
            int pending,
            int callbacksInFlight)
        {
            Epoch = epoch;
            Active = active;
            Received = received;
            Replaced = replaced;
            Applied = applied;
            Pending = pending;
            CallbacksInFlight = callbacksInFlight;
        }

        public long Epoch { get; }
        public bool Active { get; }
        public long Received { get; }
        public long Replaced { get; }
        public long Applied { get; }
        public int Pending { get; }
        public int CallbacksInFlight { get; }

        public bool IsSingleApplyLatestWinsComplete
            => Active
               && Epoch > 0
               && Received > 1
               && Replaced > 0
               && Applied == 1
               && Pending == 0
               && CallbacksInFlight == 0
               && Received == Replaced + Applied;
    }

    /// <summary>
    /// Diagnostic-only, main-thread acceptance surface for generated native
    /// subscriptions. It is bounded to an exact source component and topic and
    /// returns only immutable counters.
    /// </summary>
    public static class FoxRunRos2SubscriptionAcceptanceDiagnostics
    {
        public static bool TryGet(
            MonoBehaviour source,
            string topic,
            out FoxRunRos2SubscriptionAcceptanceSnapshot snapshot)
        {
            if (source == null || string.IsNullOrEmpty(topic))
            {
                snapshot = default;
                return false;
            }
            return FoxRunRos2SubscriptionHub.TryGetAcceptanceSnapshot(source, topic, out snapshot);
        }

        public static FoxRunRos2AcceptanceArmStatus ArmAttempt(
            MonoBehaviour source,
            string topic,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            if (source == null || string.IsNullOrEmpty(topic))
            {
                snapshot = default;
                return FoxRunRos2AcceptanceArmStatus.EndpointUnavailable;
            }
            return FoxRunRos2SubscriptionHub.ArmAcceptanceAttempt(source, topic, out snapshot);
        }

        public static bool TryGetAttempt(
            MonoBehaviour source,
            string topic,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            if (source == null || string.IsNullOrEmpty(topic))
            {
                snapshot = default;
                return false;
            }
            return FoxRunRos2SubscriptionHub.TryGetAcceptanceAttempt(source, topic, out snapshot);
        }

        public static bool EndAttempt(MonoBehaviour source, string topic, long epoch)
            => source != null
               && !string.IsNullOrEmpty(topic)
               && FoxRunRos2SubscriptionHub.EndAcceptanceAttempt(source, topic, epoch);

        public static bool TryCompleteAcceptanceAttempt(
            MonoBehaviour source,
            string topic,
            long epoch,
            out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
        {
            if (source == null || string.IsNullOrEmpty(topic) || epoch <= 0)
            {
                snapshot = default;
                return false;
            }
            return FoxRunRos2SubscriptionHub.TryCompleteAcceptanceAttempt(
                source,
                topic,
                epoch,
                out snapshot);
        }
    }

    /// <summary>
    /// Main-thread diagnostic registry. Callback threads update only binding
    /// counters; the hub samples those counters here during scan/drain.
    /// </summary>
    internal sealed class FoxRunRos2SubscriptionDiagnostics
    {
        private const int MaximumContracts = 4096;
        private readonly object _sync = new object();
        private readonly Dictionary<string, FoxRunRos2SubscriptionBindingSnapshot> _snapshots =
            new Dictionary<string, FoxRunRos2SubscriptionBindingSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoggedDiagnostic> _lastLogged =
            new Dictionary<string, LoggedDiagnostic>(StringComparer.Ordinal);

        internal int Count
        {
            get
            {
                lock (_sync)
                    return _snapshots.Count;
            }
        }

        internal void Update(
            string endpointIdentity,
            FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(endpointIdentity)
                || string.IsNullOrEmpty(snapshot.ContractId))
                return;
            lock (_sync)
            {
                if (!_snapshots.ContainsKey(endpointIdentity)
                    && _snapshots.Count >= MaximumContracts)
                    return;
                _snapshots[endpointIdentity] = snapshot;
            }
        }

        internal bool TryGet(
            string endpointIdentity,
            out FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            lock (_sync)
                return _snapshots.TryGetValue(endpointIdentity ?? string.Empty, out snapshot);
        }

        internal bool ShouldLog(
            string endpointIdentity,
            FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(endpointIdentity)
                || string.IsNullOrEmpty(snapshot.ContractId)
                || snapshot.Error == FoxRunRos2RegistrationError.None)
                return false;
            var signature = new LoggedDiagnostic(snapshot.State, snapshot.Error, snapshot.Diagnostic);
            lock (_sync)
            {
                if (_lastLogged.TryGetValue(endpointIdentity, out var previous)
                    && previous.Equals(signature))
                    return false;
                _lastLogged[endpointIdentity] = signature;
                return true;
            }
        }

        internal void Remove(string endpointIdentity)
        {
            if (string.IsNullOrEmpty(endpointIdentity))
                return;
            lock (_sync)
            {
                _snapshots.Remove(endpointIdentity);
                _lastLogged.Remove(endpointIdentity);
            }
        }

        internal void RemoveExcept(ISet<string> liveEndpointIdentities)
        {
            if (liveEndpointIdentities == null)
                throw new ArgumentNullException(nameof(liveEndpointIdentities));
            lock (_sync)
            {
                var stale = new List<string>();
                foreach (var endpointIdentity in _snapshots.Keys)
                {
                    if (!liveEndpointIdentities.Contains(endpointIdentity))
                        stale.Add(endpointIdentity);
                }
                for (var i = 0; i < stale.Count; i++)
                {
                    _snapshots.Remove(stale[i]);
                    _lastLogged.Remove(stale[i]);
                }
            }
        }

        internal void Clear()
        {
            lock (_sync)
            {
                _snapshots.Clear();
                _lastLogged.Clear();
            }
        }

        private readonly struct LoggedDiagnostic : IEquatable<LoggedDiagnostic>
        {
            internal LoggedDiagnostic(
                FoxRunRos2SubscriptionBindingState state,
                FoxRunRos2RegistrationError error,
                string diagnostic)
            {
                State = state;
                Error = error;
                Diagnostic = diagnostic ?? string.Empty;
            }

            private FoxRunRos2SubscriptionBindingState State { get; }
            private FoxRunRos2RegistrationError Error { get; }
            private string Diagnostic { get; }

            public bool Equals(LoggedDiagnostic other)
                => State == other.State
                   && Error == other.Error
                   && string.Equals(Diagnostic, other.Diagnostic, StringComparison.Ordinal);
        }
    }
}
#endif
