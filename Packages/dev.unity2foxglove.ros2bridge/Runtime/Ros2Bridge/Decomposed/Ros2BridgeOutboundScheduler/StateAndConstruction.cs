// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Bridge-local composition of the bounded U2R2 outbound authority.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed partial class Ros2BridgeOutboundScheduler
    {
        internal sealed class QueueEntry
        {
            internal QueueEntry(
                string token,
                ContractState contract,
                Ros2BridgeFrame frame,
                ulong wireBytes,
                bool requiresPreparation,
                long enqueueConnectionGeneration)
            {
                Token = token;
                Contract = contract;
                Frame = frame;
                WireBytes = wireBytes;
                RequiresPreparation = requiresPreparation;
                EnqueueConnectionGeneration =
                    enqueueConnectionGeneration;
            }

            internal string Token { get; }
            internal ContractState Contract { get; }
            internal Ros2BridgeFrame Frame { get; }
            internal ulong WireBytes { get; }
            internal bool RequiresPreparation { get; }
            internal long EnqueueConnectionGeneration { get; }
        }

        internal sealed class ContractState
        {
            internal ContractState(
                ContractIdentity identity,
                U2R2ContractKey key)
            {
                Identity = identity;
                Key = key;
            }

            internal ContractIdentity Identity { get; }
            internal U2R2ContractKey Key { get; }
            internal LinkedList<QueueEntry> Queue { get; } =
                new LinkedList<QueueEntry>();
            internal CounterState Counters { get; } = new CounterState();
            internal ulong Sequence { get; set; }
        }

        internal sealed class CounterState
        {
            internal ulong Accepted;
            internal ulong Sent;
            internal ulong Dropped;
            internal ulong Replaced;
            internal ulong Oversize;
            internal ulong BackpressureRejected;
            internal ulong RejectedAfterStop;
            internal ulong Faulted;
            internal ulong DisposalFailures;

            internal Ros2BridgeOutboundCounters Snapshot()
                => new Ros2BridgeOutboundCounters(
                    Accepted,
                    Sent,
                    Dropped,
                    Replaced,
                    Oversize,
                    BackpressureRejected,
                    RejectedAfterStop,
                    Faulted,
                    DisposalFailures);
        }

        internal readonly struct ContractIdentity :
            IEquatable<ContractIdentity>
        {
            internal ContractIdentity(Ros2BridgeFrame frame)
            {
                Topic = frame.Topic;
                SchemaName = frame.SchemaName;
                HasQos = frame.Qos.HasValue;
                Qos = frame.Qos.GetValueOrDefault();
            }

            private string Topic { get; }
            private string SchemaName { get; }
            private bool HasQos { get; }
            private FoxRunResolvedQos Qos { get; }

            public bool Equals(ContractIdentity other)
                => string.Equals(
                       Topic,
                       other.Topic,
                       StringComparison.Ordinal)
                   && string.Equals(
                       SchemaName,
                       other.SchemaName,
                       StringComparison.Ordinal)
                   && HasQos == other.HasQos
                   && (!HasQos || Qos.Equals(other.Qos));

            public override bool Equals(object obj)
                => obj is ContractIdentity other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(
                        Topic ?? string.Empty);
                    hash = (hash * 397)
                           ^ StringComparer.Ordinal.GetHashCode(
                               SchemaName ?? string.Empty);
                    hash = (hash * 397) ^ HasQos.GetHashCode();
                    return HasQos
                        ? (hash * 397) ^ Qos.GetHashCode()
                        : hash;
                }
            }
        }

        private readonly object _gate = new object();
        private readonly U2R2ProtocolLimits _limits;
        private readonly U2R2BoundedOutboundScheduler _inner;
        private readonly ulong _sessionGeneration;
        private readonly Dictionary<ContractIdentity, ContractState>
            _contracts =
                new Dictionary<ContractIdentity, ContractState>();
        private readonly Dictionary<string, QueueEntry> _queuedByToken =
            new Dictionary<string, QueueEntry>(StringComparer.Ordinal);
        private readonly CounterState _counters = new CounterState();
        private ulong _lastContractId;
        private ulong _lastToken;
        private bool _closed;
        private Exception _terminalFault;
        private Ros2BridgeOutboundCloseResult _lastCloseResult;

        internal Ros2BridgeOutboundScheduler(
            U2R2ProtocolLimits limits,
            ulong sessionGeneration)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            if (sessionGeneration == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sessionGeneration),
                    "The ROS 2 Bridge outbound session generation must be nonzero.");
            }
            if (_limits.FixedFrameBytes != 16)
            {
                throw new ArgumentException(
                    "The ROS 2 Bridge wire format requires a 16-byte fixed header.",
                    nameof(limits));
            }

            _sessionGeneration = sessionGeneration;
            _inner = new U2R2BoundedOutboundScheduler(_limits);
        }

        internal Ros2BridgeOutboundCounters Counters
        {
            get
            {
                lock (_gate)
                    return _counters.Snapshot();
            }
        }

        internal ulong QueuedBytes => _inner.QueuedBytes;
        internal ulong TotalQueuedDepth => _inner.TotalQueuedDepth;
        internal ulong DataQueuedDepth => _inner.DataQueuedDepth;
        internal ulong TransientBytes => _inner.TransientBytes;
        internal ulong InFlightBytes => _inner.InFlightBytes;

        internal bool IsClosed
        {
            get
            {
                lock (_gate)
                    return _closed;
            }
        }

        internal bool IsFaulted
        {
            get
            {
                lock (_gate)
                    return _terminalFault != null;
            }
        }

        internal Exception TerminalFault
        {
            get
            {
                lock (_gate)
                    return _terminalFault;
            }
        }

        internal bool TryGetTerminalState(
            out Exception terminalFault)
        {
            lock (_gate)
            {
                terminalFault = _terminalFault;
                return _closed;
            }
        }

        internal Ros2BridgeOutboundCloseResult LastCloseResult
        {
            get
            {
                lock (_gate)
                    return _lastCloseResult;
            }
        }

    }
}
