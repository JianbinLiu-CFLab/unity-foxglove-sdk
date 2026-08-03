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
    internal enum Ros2BridgeOutboundEnqueueDisposition
    {
        Accepted = 1,
        DroppedOldest = 2,
        ReplacedLatest = 3,
        Oversize = 4,
        BackpressureRejected = 5,
        RejectedAfterStop = 6,
        Faulted = 7,
    }

    internal readonly struct Ros2BridgeOutboundCounters
    {
        internal Ros2BridgeOutboundCounters(
            ulong accepted,
            ulong sent,
            ulong dropped,
            ulong replaced,
            ulong oversize,
            ulong backpressureRejected,
            ulong rejectedAfterStop,
            ulong faulted,
            ulong disposalFailures)
        {
            Accepted = accepted;
            Sent = sent;
            Dropped = dropped;
            Replaced = replaced;
            Oversize = oversize;
            BackpressureRejected = backpressureRejected;
            RejectedAfterStop = rejectedAfterStop;
            Faulted = faulted;
            DisposalFailures = disposalFailures;
        }

        internal ulong Accepted { get; }
        internal ulong Sent { get; }
        internal ulong Dropped { get; }
        internal ulong Replaced { get; }
        internal ulong Oversize { get; }
        internal ulong BackpressureRejected { get; }
        internal ulong RejectedAfterStop { get; }
        internal ulong Faulted { get; }
        internal ulong DisposalFailures { get; }
    }

    internal readonly struct Ros2BridgeOutboundCloseResult :
        IEquatable<Ros2BridgeOutboundCloseResult>
    {
        internal Ros2BridgeOutboundCloseResult(
            ulong clearedDataDepth,
            ulong clearedDataBytes)
        {
            ClearedDataDepth = clearedDataDepth;
            ClearedDataBytes = clearedDataBytes;
        }

        internal ulong ClearedDataDepth { get; }
        internal ulong ClearedDataBytes { get; }

        public bool Equals(Ros2BridgeOutboundCloseResult other)
            => ClearedDataDepth == other.ClearedDataDepth
               && ClearedDataBytes == other.ClearedDataBytes;

        public override bool Equals(object obj)
            => obj is Ros2BridgeOutboundCloseResult other
               && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)ClearedDataDepth * 397)
                       ^ (int)ClearedDataBytes;
            }
        }
    }

    internal sealed class Ros2BridgeOutboundWriteLease : IDisposable
    {
        private readonly Ros2BridgeOutboundScheduler _owner;
        private readonly Ros2BridgeOutboundScheduler.QueueEntry _entry;
        private U2R2WriteLease _inner;
        private int _settled;

        internal Ros2BridgeOutboundWriteLease(
            Ros2BridgeOutboundScheduler owner,
            U2R2WriteLease inner,
            Ros2BridgeOutboundScheduler.QueueEntry entry)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _entry = entry;
        }

        internal string Token => _inner?.Frame.Token
            ?? _entry?.Token
            ?? string.Empty;

        internal bool IsControl => _entry == null;

        internal U2R2ContractKey ContractKey =>
            _entry == null ? default : _entry.Contract.Key;

        internal ReadOnlyMemory<byte> WireBytes =>
            _inner?.Frame.Bytes ?? ReadOnlyMemory<byte>.Empty;

        internal Ros2BridgeFrame SourceFrame => _entry?.Frame;

        internal bool RequiresPreparation =>
            _entry != null && _entry.RequiresPreparation;

        internal long EnqueueConnectionGeneration =>
            _entry?.EnqueueConnectionGeneration ?? 0;

        internal bool TryReserveTransient(
            ulong bytes,
            out U2R2ByteLease lease)
        {
            if (Volatile.Read(ref _settled) != 0)
            {
                lease = null;
                return false;
            }
            return _owner.TryReserveTransient(this, bytes, out lease);
        }

        internal bool IsActiveFor(Ros2BridgeOutboundScheduler owner)
            => ReferenceEquals(_owner, owner)
               && Volatile.Read(ref _settled) == 0
               && Volatile.Read(ref _inner) != null;

        internal void Complete()
        {
            var inner = TakeForSettlement();
            _owner.SettleWrite(
                _entry,
                inner,
                sent: true,
                error: null);
        }

        internal void Fault(Exception error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            var inner = TakeForSettlement();
            _owner.SettleWrite(
                _entry,
                inner,
                sent: false,
                error);
        }

        internal void Drop()
        {
            if (_entry == null)
            {
                throw new InvalidOperationException(
                    "A ROS 2 Bridge control frame cannot be dropped as publish data.");
            }
            var inner = TakeForSettlement();
            _owner.DropWrite(_entry, inner);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _settled, 1, 0) != 0)
                return;
            var inner = Interlocked.Exchange(ref _inner, null);
            if (inner != null)
                _owner.ReleaseUnsettled(_entry, inner);
        }

        private U2R2WriteLease TakeForSettlement()
        {
            if (Interlocked.CompareExchange(ref _settled, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge outbound write lease is already settled.");
            }
            return Interlocked.Exchange(ref _inner, null)
                   ?? throw new InvalidOperationException(
                       "The ROS 2 Bridge outbound write lease has no inner lease.");
        }
    }

    internal sealed class Ros2BridgePreparedOutboundEnqueue : IDisposable
    {
        private readonly Ros2BridgeOutboundScheduler _owner;
        private U2R2ByteLease _transientLease;
        private int _settled;

        internal Ros2BridgePreparedOutboundEnqueue(
            Ros2BridgeOutboundScheduler owner,
            Ros2BridgeFrame frame,
            Ros2BridgeFrameMeasurement measurement,
            byte[] encodedWire,
            U2R2ByteLease transientLease)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Measurement = measurement;
            EncodedWire = encodedWire
                          ?? throw new ArgumentNullException(nameof(encodedWire));
            _transientLease = transientLease
                              ?? throw new ArgumentNullException(nameof(transientLease));
        }

        internal Ros2BridgeFrame Frame { get; }
        internal Ros2BridgeFrameMeasurement Measurement { get; }
        internal byte[] EncodedWire { get; }

        internal U2R2ByteLease TakeForCommit(
            Ros2BridgeOutboundScheduler owner)
        {
            if (!ReferenceEquals(_owner, owner))
            {
                throw new InvalidOperationException(
                    "The prepared ROS 2 Bridge frame belongs to another scheduler.");
            }
            if (Interlocked.CompareExchange(ref _settled, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "The prepared ROS 2 Bridge frame is already settled.");
            }
            return Interlocked.Exchange(ref _transientLease, null)
                   ?? throw new InvalidOperationException(
                       "The prepared ROS 2 Bridge frame has no transient lease.");
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _settled, 1, 0) != 0)
                return;
            var transientLease =
                Interlocked.Exchange(ref _transientLease, null);
            if (transientLease != null)
                _owner.CancelPrepared(transientLease);
        }
    }

    internal sealed class Ros2BridgeOutboundScheduler : IDisposable
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

        internal Ros2BridgeOutboundEnqueueDisposition Enqueue(
            Ros2BridgeFrame frame,
            U2R2QueueOverflowPolicy overflowPolicy,
            bool requiresPreparation = false,
            long enqueueConnectionGeneration = 0)
        {
            ValidateOverflowPolicy(overflowPolicy);
            var disposition = PrepareEnqueue(
                frame,
                out var prepared);
            if (disposition != Ros2BridgeOutboundEnqueueDisposition.Accepted)
                return disposition;
            using (prepared)
            {
                return CommitPrepared(
                    prepared,
                    overflowPolicy,
                    requiresPreparation,
                    enqueueConnectionGeneration);
            }
        }

        internal Ros2BridgeOutboundEnqueueDisposition PrepareEnqueue(
            Ros2BridgeFrame frame,
            out Ros2BridgePreparedOutboundEnqueue prepared)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            prepared = null;
            var identity = new ContractIdentity(frame);

            lock (_gate)
            {
                var existingContract = FindContract(identity);
                if (_terminalFault != null)
                {
                    IncrementFaulted(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.Faulted;
                }
                if (_closed)
                {
                    IncrementRejectedAfterStop(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop;
                }
                if (existingContract == null
                    && (checked((ulong)_contracts.Count) >= _limits.MaxContracts
                        || _lastContractId == ulong.MaxValue))
                {
                    IncrementBackpressureRejected(null);
                    return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
                }
            }

            Ros2BridgeFrameMeasurement measurement;
            try
            {
                measurement = Ros2BridgeFrameWriter.Measure(frame);
                _ = U2R2FrameSize.Create(
                    _limits,
                    checked((ulong)measurement.HeaderBytes),
                    checked((ulong)measurement.PayloadBytes));
            }
            catch (ArgumentException)
            {
                lock (_gate)
                {
                    IncrementOversize(FindContract(identity));
                }
                return Ros2BridgeOutboundEnqueueDisposition.Oversize;
            }
            catch (U2R2ProtocolException error)
                when (string.Equals(
                    error.ErrorCode,
                    "capacity_exceeded",
                    StringComparison.Ordinal))
            {
                lock (_gate)
                {
                    IncrementOversize(FindContract(identity));
                }
                return Ros2BridgeOutboundEnqueueDisposition.Oversize;
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    var existingContract = FindContract(identity);
                    FaultCore(error);
                    IncrementContractFaulted(existingContract);
                }
                return Ros2BridgeOutboundEnqueueDisposition.Faulted;
            }

            U2R2ByteLease transientLease;
            lock (_gate)
            {
                var existingContract = FindContract(identity);
                if (_terminalFault != null)
                {
                    IncrementFaulted(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.Faulted;
                }
                if (_closed)
                {
                    IncrementRejectedAfterStop(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop;
                }
                if (existingContract == null
                    && (checked((ulong)_contracts.Count) >= _limits.MaxContracts
                        || _lastContractId == ulong.MaxValue))
                {
                    IncrementBackpressureRejected(null);
                    return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
                }
                if (!_inner.TryReserveTransient(
                        checked((ulong)measurement.TotalWireBytes),
                        out transientLease))
                {
                    IncrementBackpressureRejected(existingContract);
                    return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
                }
            }

            try
            {
                var encodedWire = Ros2BridgeFrameWriter.EncodeOwned(
                    frame,
                    measurement);
                prepared = new Ros2BridgePreparedOutboundEnqueue(
                    this,
                    frame,
                    measurement,
                    encodedWire,
                    transientLease);
                return Ros2BridgeOutboundEnqueueDisposition.Accepted;
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    var existingContract = FindContract(identity);
                    if (!TryReleaseTransient(
                            transientLease,
                            existingContract))
                    {
                        return Ros2BridgeOutboundEnqueueDisposition.Faulted;
                    }
                    FaultCore(error);
                    IncrementContractFaulted(existingContract);
                }
                return Ros2BridgeOutboundEnqueueDisposition.Faulted;
            }
        }

        internal Ros2BridgeOutboundEnqueueDisposition CommitPrepared(
            Ros2BridgePreparedOutboundEnqueue prepared,
            U2R2QueueOverflowPolicy overflowPolicy,
            bool requiresPreparation = false,
            long enqueueConnectionGeneration = 0)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));
            ValidateOverflowPolicy(overflowPolicy);

            lock (_gate)
            {
                var transientLease = prepared.TakeForCommit(this);
                var identity = new ContractIdentity(prepared.Frame);
                var existingContract = FindContract(identity);
                ContractState contract = null;
                var disposition =
                    Ros2BridgeOutboundEnqueueDisposition.Faulted;
                try
                {
                    if (_terminalFault != null)
                    {
                        IncrementFaulted(existingContract);
                        disposition =
                            Ros2BridgeOutboundEnqueueDisposition.Faulted;
                    }
                    else if (_closed)
                    {
                        IncrementRejectedAfterStop(existingContract);
                        disposition = Ros2BridgeOutboundEnqueueDisposition
                            .RejectedAfterStop;
                    }
                    else if (!TryGetOrCreateContract(
                                 identity,
                                 out contract,
                                 out var contractCreated))
                    {
                        IncrementBackpressureRejected(null);
                        disposition = Ros2BridgeOutboundEnqueueDisposition
                            .BackpressureRejected;
                    }
                    else
                    {
                        var admitted = false;
                        try
                        {
                            disposition = EnqueuePreparedCore(
                                contract,
                                prepared,
                                overflowPolicy,
                                requiresPreparation,
                                enqueueConnectionGeneration);
                            admitted = disposition
                                       != Ros2BridgeOutboundEnqueueDisposition
                                           .BackpressureRejected
                                       && disposition
                                       != Ros2BridgeOutboundEnqueueDisposition
                                           .Faulted;
                        }
                        catch (Exception error)
                        {
                            FaultCore(error);
                            IncrementContractFaulted(contract);
                            disposition = Ros2BridgeOutboundEnqueueDisposition
                                .Faulted;
                        }
                        if (!admitted)
                        {
                            RemoveProvisionalContract(
                                identity,
                                contract,
                                contractCreated);
                        }
                    }
                }
                finally
                {
                    if (!TryReleaseTransient(transientLease, contract))
                    {
                        disposition =
                            Ros2BridgeOutboundEnqueueDisposition.Faulted;
                    }
                }
                return disposition;
            }
        }

        internal void CancelPrepared(U2R2ByteLease transientLease)
        {
            if (transientLease == null)
                return;
            lock (_gate)
            {
                _ = TryReleaseTransient(transientLease, null);
            }
        }

        internal bool TryReserveControl(
            ulong bytes,
            out U2R2ControlReservation reservation)
        {
            lock (_gate)
            {
                reservation = null;
                return !_closed
                       && _terminalFault == null
                       && _inner.TryReserveControl(
                           bytes,
                           out reservation);
            }
        }

        internal bool TryReserveTransient(
            Ros2BridgeOutboundWriteLease write,
            ulong bytes,
            out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                return write != null
                       && write.IsActiveFor(this)
                       && !_closed
                       && _terminalFault == null
                       && _inner.TryReserveTransient(bytes, out lease);
            }
        }

        internal bool TryReserveSessionTransient(
            ulong bytes,
            out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                return !_closed
                       && _terminalFault == null
                       && _inner.TryReserveTransient(bytes, out lease);
            }
        }

        internal bool TryBeginWrite(
            out Ros2BridgeOutboundWriteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (!_inner.TryBeginWrite(out var innerLease))
                    return false;

                QueueEntry entry = null;
                if (!innerLease.Frame.IsControl)
                {
                    if (!_queuedByToken.TryGetValue(
                            innerLease.Frame.Token,
                            out entry)
                        || entry.Contract.Key
                        != innerLease.Frame.Contract
                        || entry.Contract.Queue.First == null
                        || !ReferenceEquals(
                            entry,
                            entry.Contract.Queue.First.Value))
                    {
                        ReleaseBrokenInnerLease(
                            innerLease,
                            new InvalidOperationException(
                                "The ROS 2 Bridge outbound queue mirror diverged."));
                        return false;
                    }

                    entry.Contract.Queue.RemoveFirst();
                    _queuedByToken.Remove(entry.Token);
                }

                lease = new Ros2BridgeOutboundWriteLease(
                    this,
                    innerLease,
                    entry);
                return true;
            }
        }

        internal bool TryGetContractCounters(
            Ros2BridgeFrame identityFrame,
            out Ros2BridgeOutboundCounters counters)
        {
            if (identityFrame == null)
                throw new ArgumentNullException(nameof(identityFrame));
            lock (_gate)
            {
                var contract = FindContract(
                    new ContractIdentity(identityFrame));
                if (contract == null)
                {
                    counters = default;
                    return false;
                }
                counters = contract.Counters.Snapshot();
                return true;
            }
        }

        internal void Fault(Exception error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            lock (_gate)
                FaultCore(error);
        }

        internal Ros2BridgeOutboundCloseResult Close()
        {
            lock (_gate)
                return CloseCore();
        }

        internal void RecordDisposalFailure(
            Ros2BridgeFrame frame = null)
        {
            lock (_gate)
            {
                IncrementDisposalFailure(
                    frame == null
                        ? null
                        : FindContract(new ContractIdentity(frame)));
            }
        }

        public void Dispose()
        {
            Close();
        }

        internal void SettleWrite(
            QueueEntry entry,
            U2R2WriteLease inner,
            bool sent,
            Exception error)
        {
            lock (_gate)
            {
                _ = error;
                if (sent)
                {
                    if (entry != null)
                    {
                        Increment(
                            ref _counters.Sent,
                            ref entry.Contract.Counters.Sent);
                    }
                }
                else
                {
                    IncrementFaulted(entry?.Contract);
                }

                DisposeInnerLease(entry, inner);
            }
        }

        internal void ReleaseUnsettled(
            QueueEntry entry,
            U2R2WriteLease inner)
        {
            lock (_gate)
            {
                IncrementFaulted(entry?.Contract);
                DisposeInnerLease(entry, inner);
            }
        }

        internal void DropWrite(
            QueueEntry entry,
            U2R2WriteLease inner)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            lock (_gate)
            {
                Increment(
                    ref _counters.Dropped,
                    ref entry.Contract.Counters.Dropped);
                DisposeInnerLease(entry, inner);
            }
        }

        private Ros2BridgeOutboundEnqueueDisposition EnqueuePreparedCore(
            ContractState contract,
            Ros2BridgePreparedOutboundEnqueue prepared,
            U2R2QueueOverflowPolicy overflowPolicy,
            bool requiresPreparation,
            long enqueueConnectionGeneration)
        {
            var frame = prepared.Frame;
            var measurement = prepared.Measurement;
            var token = NextToken();
            var sequence = NextSequence(contract);
            var outbound = U2R2OutboundFrame.DataOwned(
                token,
                contract.Key,
                sequence,
                prepared.EncodedWire);
            if (!MemoryMarshal.TryGetArray(
                    outbound.Bytes,
                    out ArraySegment<byte> ownedWire)
                || ownedWire.Array == null
                || ownedWire.Count != measurement.TotalWireBytes)
            {
                throw new InvalidOperationException(
                    "The U2R2 outbound authority did not retain an array-backed owned frame.");
            }
            var ownedFrame = Ros2BridgeFrame.CreateWireOwnedView(
                frame,
                ownedWire.Array,
                checked(
                    ownedWire.Offset
                    + 16
                    + measurement.HeaderBytes),
                measurement.PayloadBytes);
            var innerDisposition = _inner.EnqueueData(
                outbound,
                overflowPolicy);
            if (innerDisposition == U2R2EnqueueDisposition.Rejected)
            {
                IncrementBackpressureRejected(contract);
                return Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected;
            }

            var entry = new QueueEntry(
                token,
                contract,
                ownedFrame,
                outbound.ByteCount,
                requiresPreparation,
                enqueueConnectionGeneration);
            switch (innerDisposition)
            {
                case U2R2EnqueueDisposition.Accepted:
                    break;
                case U2R2EnqueueDisposition.DroppedOldest:
                    RemoveVictim(
                        contract,
                        contract.Queue.First,
                        replaced: false);
                    break;
                case U2R2EnqueueDisposition.ReplacedLatest:
                    RemoveVictim(
                        contract,
                        contract.Queue.Last,
                        replaced: true);
                    break;
                default:
                    throw new InvalidOperationException(
                        "The U2R2 outbound scheduler returned an unknown disposition.");
            }

            contract.Queue.AddLast(entry);
            _queuedByToken.Add(token, entry);
            Increment(
                ref _counters.Accepted,
                ref contract.Counters.Accepted);
            switch (innerDisposition)
            {
                case U2R2EnqueueDisposition.DroppedOldest:
                    return Ros2BridgeOutboundEnqueueDisposition.DroppedOldest;
                case U2R2EnqueueDisposition.ReplacedLatest:
                    return Ros2BridgeOutboundEnqueueDisposition.ReplacedLatest;
                default:
                    return Ros2BridgeOutboundEnqueueDisposition.Accepted;
            }
        }

        private bool TryReleaseTransient(
            U2R2ByteLease transientLease,
            ContractState contract)
        {
            try
            {
                transientLease.Dispose();
                return true;
            }
            catch (Exception error)
            {
                IncrementDisposalFailure(contract);
                FaultCore(error);
                IncrementContractFaulted(contract);
                return false;
            }
        }

        private bool TryGetOrCreateContract(
            ContractIdentity identity,
            out ContractState contract,
            out bool created)
        {
            if (_contracts.TryGetValue(identity, out contract))
            {
                created = false;
                return true;
            }
            if (checked((ulong)_contracts.Count) >= _limits.MaxContracts
                || _lastContractId == ulong.MaxValue)
            {
                contract = null;
                created = false;
                return false;
            }

            _lastContractId++;
            contract = new ContractState(
                identity,
                new U2R2ContractKey(
                    _lastContractId,
                    _sessionGeneration));
            _contracts.Add(identity, contract);
            created = true;
            return true;
        }

        private void RemoveProvisionalContract(
            ContractIdentity identity,
            ContractState contract,
            bool created)
        {
            if (!created)
                return;
            if (contract == null
                || contract.Queue.Count != 0
                || !_contracts.TryGetValue(identity, out var current)
                || !ReferenceEquals(current, contract))
            {
                throw new InvalidOperationException(
                    "A provisional ROS 2 Bridge contract could not be rolled back safely.");
            }
            _contracts.Remove(identity);
        }

        private ContractState FindContract(ContractIdentity identity)
            => _contracts.TryGetValue(identity, out var contract)
                ? contract
                : null;

        private string NextToken()
        {
            if (_lastToken == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge outbound token counter is exhausted.");
            }
            _lastToken++;
            return "bridge-data:"
                   + _lastToken.ToString(CultureInfo.InvariantCulture);
        }

        private static ulong NextSequence(ContractState contract)
        {
            if (contract.Sequence == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge contract sequence is exhausted.");
            }
            contract.Sequence++;
            return contract.Sequence;
        }

        private void RemoveVictim(
            ContractState contract,
            LinkedListNode<QueueEntry> victim,
            bool replaced)
        {
            if (victim == null)
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge outbound scheduler displaced a missing frame.");
            }
            contract.Queue.Remove(victim);
            if (!_queuedByToken.Remove(victim.Value.Token))
            {
                throw new InvalidOperationException(
                    "The ROS 2 Bridge outbound queue mirror lost a displaced frame.");
            }
            if (replaced)
            {
                Increment(
                    ref _counters.Replaced,
                    ref contract.Counters.Replaced);
            }
            else
            {
                Increment(
                    ref _counters.Dropped,
                    ref contract.Counters.Dropped);
            }
        }

        private Ros2BridgeOutboundCloseResult CloseCore()
        {
            if (_closed)
                return default;

            ulong depth = 0;
            ulong bytes = 0;
            foreach (var entry in _queuedByToken.Values)
            {
                depth = checked(depth + 1);
                bytes = checked(bytes + entry.WireBytes);
                Increment(
                    ref _counters.Dropped,
                    ref entry.Contract.Counters.Dropped);
            }
            foreach (var contract in _contracts.Values)
                contract.Queue.Clear();
            _queuedByToken.Clear();
            _inner.Close();
            _closed = true;
            _lastCloseResult = new Ros2BridgeOutboundCloseResult(
                depth,
                bytes);
            return _lastCloseResult;
        }

        private void FaultCore(Exception error)
        {
            if (_terminalFault != null || _closed)
                return;
            _terminalFault = error;
            checked
            {
                _counters.Faulted++;
            }
            CloseCore();
        }

        private void DisposeInnerLease(
            QueueEntry entry,
            U2R2WriteLease inner)
        {
            try
            {
                inner.Dispose();
            }
            catch (Exception error)
            {
                IncrementDisposalFailure(entry?.Contract);
                FaultCore(error);
                IncrementContractFaulted(entry?.Contract);
                throw;
            }
        }

        private void ReleaseBrokenInnerLease(
            U2R2WriteLease inner,
            Exception error)
        {
            try
            {
                inner.Dispose();
            }
            catch (Exception disposeError)
            {
                IncrementDisposalFailure(null);
                FaultCore(disposeError);
                return;
            }
            FaultCore(error);
        }

        private void IncrementOversize(ContractState contract)
        {
            checked
            {
                _counters.Oversize++;
                if (contract != null)
                    contract.Counters.Oversize++;
            }
        }

        private void IncrementBackpressureRejected(
            ContractState contract)
        {
            checked
            {
                _counters.BackpressureRejected++;
                if (contract != null)
                    contract.Counters.BackpressureRejected++;
            }
        }

        private void IncrementRejectedAfterStop(
            ContractState contract)
        {
            checked
            {
                _counters.RejectedAfterStop++;
                if (contract != null)
                    contract.Counters.RejectedAfterStop++;
            }
        }

        private void IncrementFaulted(ContractState contract)
        {
            checked
            {
                _counters.Faulted++;
                if (contract != null)
                    contract.Counters.Faulted++;
            }
        }

        private static void IncrementContractFaulted(
            ContractState contract)
        {
            if (contract == null)
                return;
            checked
            {
                contract.Counters.Faulted++;
            }
        }

        private void IncrementDisposalFailure(
            ContractState contract)
        {
            checked
            {
                _counters.DisposalFailures++;
                if (contract != null)
                    contract.Counters.DisposalFailures++;
            }
        }

        private static void Increment(
            ref ulong aggregate,
            ref ulong contract)
        {
            checked
            {
                aggregate++;
                contract++;
            }
        }

        private static void ValidateOverflowPolicy(
            U2R2QueueOverflowPolicy overflowPolicy)
        {
            if (overflowPolicy != U2R2QueueOverflowPolicy.Reject
                && overflowPolicy != U2R2QueueOverflowPolicy.DropOldest
                && overflowPolicy
                != U2R2QueueOverflowPolicy.ReplaceLatest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(overflowPolicy));
            }
        }
    }
}
