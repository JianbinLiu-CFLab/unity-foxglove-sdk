// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Bounded per-contract native subscription diagnostics.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
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
            SameOriginDrops = snapshot.SameOriginDrops;
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
        /// <summary>Inbound envelopes suppressed because they originated from this Unity process.</summary>
        public long SameOriginDrops { get; }

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
    /// Immutable, managed-only runtime diagnostic for one generated native ROS2
    /// subscription. It deliberately contains no message object, node, token,
    /// or middleware credential.
    /// </summary>
    public readonly struct FoxRunRos2SubscriptionDiagnosticSnapshot
    {
        internal FoxRunRos2SubscriptionDiagnosticSnapshot(
            FoxRunRos2SubscriptionBindingSnapshot binding,
            FoxRunRos2RuntimeDiagnosticContext runtime)
        {
            ContractId = binding.ContractId ?? string.Empty;
            Topic = binding.Topic ?? string.Empty;
            DeclaringType = binding.DeclaringType ?? string.Empty;
            MemberName = binding.MemberName ?? string.Empty;
            CanonicalRosType = binding.CanonicalRosType ?? string.Empty;
            QosPreset = binding.QosPreset;
            State = binding.State;
            SessionGeneration = binding.SessionGeneration;
            Received = binding.Received;
            Replaced = binding.Replaced;
            Applied = binding.Applied;
            Pending = binding.Pending;
            RejectedAfterStop = binding.RejectedAfterStop;
            CopyFailed = binding.CopyFailed;
            StaleCallbacks = binding.StaleCallbacks;
            SameOriginDrops = binding.SameOriginDrops;
            LastReceiveStopwatchTimestamp = binding.LastReceiveStopwatchTimestamp;
            LastApplyStopwatchTimestamp = binding.LastApplyStopwatchTimestamp;
            LastErrorCode = ErrorCode(binding.Error);
            LastErrorMessage = FoxRunRos2PublicDiagnostic.Describe(binding.Error);
            RosDistro = runtime.RosDistro ?? string.Empty;
            RmwImplementation = runtime.RmwImplementation ?? string.Empty;
            CommunicationMode = runtime.CommunicationMode ?? "unknown";
            TransportLabel = runtime.TransportLabel ?? "ROS2 Native / Unknown RMW";
        }

        public string ContractId { get; }
        public string Topic { get; }
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string CanonicalRosType { get; }
        public FoxRunRos2QosPreset QosPreset { get; }
        public FoxRunRos2SubscriptionBindingState State { get; }
        public long SessionGeneration { get; }
        public long Received { get; }
        public long Replaced { get; }
        public long Applied { get; }
        public int Pending { get; }
        public long RejectedAfterStop { get; }
        public long CopyFailed { get; }
        public long StaleCallbacks { get; }
        public long SameOriginDrops { get; }
        public long LastReceiveStopwatchTimestamp { get; }
        public long LastApplyStopwatchTimestamp { get; }
        public string LastErrorCode { get; }
        public string LastErrorMessage { get; }
        public string RosDistro { get; }
        public string RmwImplementation { get; }
        public string CommunicationMode { get; }
        public string TransportLabel { get; }

        private static string ErrorCode(FoxRunRos2RegistrationError error)
        {
            switch (error)
            {
                case FoxRunRos2RegistrationError.None: return string.Empty;
                case FoxRunRos2RegistrationError.RuntimeUnavailable: return "RuntimeUnavailable";
                case FoxRunRos2RegistrationError.UnsupportedMessageType: return "UnsupportedMessageType";
                case FoxRunRos2RegistrationError.UnsupportedQos: return "UnsupportedQos";
                case FoxRunRos2RegistrationError.RegistrationRejected: return "RegistrationRejected";
                case FoxRunRos2RegistrationError.InvalidSubscriptionToken: return "InvalidSubscriptionToken";
                case FoxRunRos2RegistrationError.BackendFailure: return "BackendFailure";
                case FoxRunRos2RegistrationError.StaleGeneration: return "StaleGeneration";
                case FoxRunRos2RegistrationError.Stopped: return "Stopped";
                case FoxRunRos2RegistrationError.TeardownFailure: return "TeardownFailure";
                case FoxRunRos2RegistrationError.ApplyFailure: return "ApplyFailure";
                default: return "Unknown";
            }
        }
    }

    /// <summary>
    /// Reflection-safe optional-package boundary for native subscription
    /// diagnostics. A call returns an immutable, deterministically sorted copy.
    /// </summary>
    public static class FoxRunRos2SubscriptionRuntimeDiagnostics
    {
        public static FoxRunRos2SubscriptionDiagnosticSnapshot[] GetSnapshots()
            => FoxRunRos2SubscriptionHub.GetDiagnosticSnapshots();
    }

    internal readonly struct FoxRunRos2RuntimeDiagnosticContext
    {
        internal FoxRunRos2RuntimeDiagnosticContext(string rosDistro, string rmwImplementation)
        {
            RosDistro = Normalize(rosDistro);
            RmwImplementation = Normalize(rmwImplementation);
            if (string.Equals(RmwImplementation, "rmw_fastrtps_cpp", StringComparison.Ordinal))
            {
                CommunicationMode = "fastdds";
                TransportLabel = "ROS2 Native / FastDDS (DDS)";
            }
            else if (string.Equals(RmwImplementation, "rmw_zenoh_cpp", StringComparison.Ordinal))
            {
                CommunicationMode = "zenoh";
                TransportLabel = "ROS2 Native / Zenoh";
            }
            else
            {
                CommunicationMode = "unknown";
                TransportLabel = "ROS2 Native / "
                    + (string.IsNullOrEmpty(RmwImplementation)
                        ? "Unknown RMW"
                        : RmwImplementation);
            }
        }

        internal string RosDistro { get; }
        internal string RmwImplementation { get; }
        internal string CommunicationMode { get; }
        internal string TransportLabel { get; }

        internal static FoxRunRos2RuntimeDiagnosticContext Unknown
            => new FoxRunRos2RuntimeDiagnosticContext(string.Empty, string.Empty);

        internal static FoxRunRos2RuntimeDiagnosticContext CaptureAfterRuntimeReady(
            string rosDistro,
            string rmwImplementation)
            => new FoxRunRos2RuntimeDiagnosticContext(rosDistro, rmwImplementation);

        private static string Normalize(string value)
            => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// Main-thread diagnostic registry. Callback threads update only binding
    /// counters; the hub samples those counters here during scan/drain.
    /// </summary>
    internal sealed class FoxRunRos2SubscriptionDiagnostics
    {
        private const int MaximumContracts = 4096;
        private readonly object _sync = new object();
        private readonly Dictionary<string, SnapshotEntry> _snapshots =
            new Dictionary<string, SnapshotEntry>(StringComparer.Ordinal);
        private readonly HashSet<LoggedDiagnostic> _lastLogged =
            new HashSet<LoggedDiagnostic>();

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
            => Update(endpointIdentity, snapshot, FoxRunRos2RuntimeDiagnosticContext.Unknown);

        internal void Update(
            string endpointIdentity,
            FoxRunRos2SubscriptionBindingSnapshot snapshot,
            FoxRunRos2RuntimeDiagnosticContext runtime)
        {
            if (string.IsNullOrEmpty(endpointIdentity)
                || string.IsNullOrEmpty(snapshot.ContractId))
                return;
            lock (_sync)
            {
                if (!_snapshots.ContainsKey(endpointIdentity)
                    && _snapshots.Count >= MaximumContracts)
                    return;
                _snapshots[endpointIdentity] = new SnapshotEntry(
                    endpointIdentity,
                    snapshot,
                    runtime);
            }
        }

        internal bool TryGet(
            string endpointIdentity,
            out FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            lock (_sync)
            {
                if (_snapshots.TryGetValue(endpointIdentity ?? string.Empty, out var entry))
                {
                    snapshot = entry.Binding;
                    return true;
                }
                snapshot = default;
                return false;
            }
        }

        internal FoxRunRos2SubscriptionDiagnosticSnapshot[] GetSnapshots()
        {
            lock (_sync)
            {
                if (_snapshots.Count == 0)
                    return Array.Empty<FoxRunRos2SubscriptionDiagnosticSnapshot>();
                var entries = new SnapshotEntry[_snapshots.Count];
                var index = 0;
                foreach (var entry in _snapshots.Values)
                    entries[index++] = entry;
                Array.Sort(entries, CompareEntries);
                var snapshots = new FoxRunRos2SubscriptionDiagnosticSnapshot[entries.Length];
                for (var i = 0; i < entries.Length; i++)
                    snapshots[i] = entries[i].Diagnostic;
                return snapshots;
            }
        }

        internal bool ShouldLog(
            string endpointIdentity,
            FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(endpointIdentity)
                || string.IsNullOrEmpty(snapshot.ContractId))
                return false;
            lock (_sync)
            {
                ReconcileLoggedDiagnosticsForContract(snapshot.ContractId);
                if (snapshot.Error == FoxRunRos2RegistrationError.None)
                    return false;
                var signature = new LoggedDiagnostic(snapshot.ContractId, snapshot.Error);
                return _lastLogged.Add(signature);
            }
        }

        internal void Remove(string endpointIdentity)
        {
            if (string.IsNullOrEmpty(endpointIdentity))
                return;
            lock (_sync)
            {
                if (_snapshots.Remove(endpointIdentity))
                {
                    RemoveLoggedDiagnosticsWithoutMatchingError();
                }
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
                    _snapshots.Remove(stale[i]);
                RemoveLoggedDiagnosticsWithoutMatchingError();
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

        private bool HasSnapshotForDiagnostic(LoggedDiagnostic signature)
        {
            foreach (var entry in _snapshots.Values)
            {
                if (string.Equals(
                        entry.Binding.ContractId,
                        signature.ContractId,
                        StringComparison.Ordinal)
                    && entry.Binding.Error == signature.Error)
                {
                    return true;
                }
            }
            return false;
        }

        private void ReconcileLoggedDiagnosticsForContract(string contractId)
        {
            if (string.IsNullOrEmpty(contractId))
                return;
            _lastLogged.RemoveWhere(signature =>
                signature.MatchesContract(contractId)
                && !HasSnapshotForDiagnostic(signature));
        }

        private void RemoveLoggedDiagnosticsWithoutMatchingError()
            => _lastLogged.RemoveWhere(signature => !HasSnapshotForDiagnostic(signature));

        private readonly struct LoggedDiagnostic : IEquatable<LoggedDiagnostic>
        {
            internal LoggedDiagnostic(
                string contractId,
                FoxRunRos2RegistrationError error)
            {
                ContractId = contractId ?? string.Empty;
                Error = error;
            }

            internal string ContractId { get; }
            internal FoxRunRos2RegistrationError Error { get; }

            public bool Equals(LoggedDiagnostic other)
                => Error == other.Error
                   && string.Equals(ContractId, other.ContractId, StringComparison.Ordinal);

            public override bool Equals(object obj)
                => obj is LoggedDiagnostic other && Equals(other);

            public override int GetHashCode()
                => (StringComparer.Ordinal.GetHashCode(ContractId) * 397) ^ (int)Error;

            internal bool MatchesContract(string contractId)
                => string.Equals(ContractId, contractId, StringComparison.Ordinal);
        }

        private readonly struct SnapshotEntry
        {
            internal SnapshotEntry(
                string endpointIdentity,
                FoxRunRos2SubscriptionBindingSnapshot binding,
                FoxRunRos2RuntimeDiagnosticContext runtime)
            {
                EndpointIdentity = endpointIdentity ?? string.Empty;
                Binding = binding;
                Diagnostic = new FoxRunRos2SubscriptionDiagnosticSnapshot(binding, runtime);
            }

            internal string EndpointIdentity { get; }
            internal FoxRunRos2SubscriptionBindingSnapshot Binding { get; }
            internal FoxRunRos2SubscriptionDiagnosticSnapshot Diagnostic { get; }
        }

        private static int CompareEntries(SnapshotEntry left, SnapshotEntry right)
        {
            var compare = CompareSnapshots(left.Diagnostic, right.Diagnostic);
            return compare != 0
                ? compare
                : string.CompareOrdinal(left.EndpointIdentity, right.EndpointIdentity);
        }

        private static int CompareSnapshots(
            FoxRunRos2SubscriptionDiagnosticSnapshot left,
            FoxRunRos2SubscriptionDiagnosticSnapshot right)
        {
            var compare = string.CompareOrdinal(left.ContractId, right.ContractId);
            if (compare != 0)
                return compare;
            compare = string.CompareOrdinal(left.Topic, right.Topic);
            if (compare != 0)
                return compare;
            compare = string.CompareOrdinal(left.DeclaringType, right.DeclaringType);
            if (compare != 0)
                return compare;
            compare = string.CompareOrdinal(left.MemberName, right.MemberName);
            if (compare != 0)
                return compare;
            return left.SessionGeneration.CompareTo(right.SessionGeneration);
        }
    }
}
#endif
