// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Pure bounded U2R2 outbound scheduling and byte accounting.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public readonly struct U2R2ContractKey : IEquatable<U2R2ContractKey>
    {
        public U2R2ContractKey(ulong contractId, ulong generation)
        {
            if (contractId == 0 || generation == 0)
            {
                throw new U2R2ProtocolException(
                    "invalid_contract",
                    "A U2R2 contract identity must be nonzero.",
                    terminal: false);
            }
            ContractId = contractId;
            Generation = generation;
        }

        public ulong ContractId { get; }
        public ulong Generation { get; }

        public bool Equals(U2R2ContractKey other)
            => ContractId == other.ContractId
               && Generation == other.Generation;

        public override bool Equals(object obj)
            => obj is U2R2ContractKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)ContractId * 397) ^ (int)Generation;
            }
        }

        public static bool operator ==(U2R2ContractKey left, U2R2ContractKey right)
            => left.Equals(right);

        public static bool operator !=(U2R2ContractKey left, U2R2ContractKey right)
            => !left.Equals(right);
    }

    public enum U2R2QueueOverflowPolicy
    {
        Reject = 1,
        DropOldest = 2,
        ReplaceLatest = 3,
    }

    public enum U2R2EnqueueDisposition
    {
        Accepted = 1,
        Rejected = 2,
        DroppedOldest = 3,
        ReplacedLatest = 4,
    }

    public sealed class U2R2OutboundFrame
    {
        private readonly byte[] _bytes;

        private U2R2OutboundFrame(
            string token,
            bool isControl,
            U2R2ContractKey contract,
            ulong sequence,
            byte[] bytes,
            bool cloneBytes)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            IsControl = isControl;
            Contract = contract;
            Sequence = sequence;
            _bytes = bytes == null
                ? Array.Empty<byte>()
                : cloneBytes
                    ? (byte[])bytes.Clone()
                    : bytes;
        }

        public string Token { get; }
        public bool IsControl { get; }
        public U2R2ContractKey Contract { get; }
        public ulong Sequence { get; }
        public ReadOnlyMemory<byte> Bytes => new(_bytes);
        public ulong ByteCount => checked((ulong)_bytes.LongLength);

        public static U2R2OutboundFrame Control(string token, byte[] bytes)
            => new U2R2OutboundFrame(
                token,
                isControl: true,
                default,
                0,
                bytes,
                cloneBytes: true);

        public static U2R2OutboundFrame Data(
            string token,
            U2R2ContractKey contract,
            ulong sequence,
            byte[] bytes)
            => CreateData(
                token,
                contract,
                sequence,
                bytes,
                cloneBytes: true);

        internal static U2R2OutboundFrame DataOwned(
            string token,
            U2R2ContractKey contract,
            ulong sequence,
            byte[] bytes)
            => CreateData(
                token,
                contract,
                sequence,
                bytes,
                cloneBytes: false);

        private static U2R2OutboundFrame CreateData(
            string token,
            U2R2ContractKey contract,
            ulong sequence,
            byte[] bytes,
            bool cloneBytes)
        {
            if (sequence == 0)
            {
                throw new U2R2ProtocolException(
                    "contract_sequence_fault",
                    "A U2R2 data sequence must be nonzero.",
                    terminal: false);
            }
            return new U2R2OutboundFrame(
                token,
                isControl: false,
                contract,
                sequence,
                bytes,
                cloneBytes);
        }
    }

    public sealed class U2R2ControlReservation : IDisposable
    {
        private readonly object _gate = new();
        private U2R2BoundedOutboundScheduler _owner;
        private readonly ulong _reservedBytes;
        private bool _settled;

        internal U2R2ControlReservation(
            U2R2BoundedOutboundScheduler owner,
            ulong reservedBytes)
        {
            _owner = owner;
            _reservedBytes = reservedBytes;
        }

        public void Commit(U2R2OutboundFrame frame)
        {
            if (!TryCommitCore(frame, fenceContract: null))
                throw new InvalidOperationException("The control reservation is settled.");
        }

        internal void CommitFenced(
            U2R2OutboundFrame frame,
            U2R2ContractKey fenceContract)
        {
            if (!TryCommitCore(frame, fenceContract))
                throw new InvalidOperationException("The control reservation is settled.");
        }

        public bool TryCommit(U2R2OutboundFrame frame)
            => TryCommitCore(frame, fenceContract: null);

        private bool TryCommitCore(
            U2R2OutboundFrame frame,
            U2R2ContractKey? fenceContract)
        {
            lock (_gate)
            {
                if (_settled || _owner == null)
                    return false;
                _owner.CommitControl(
                    this,
                    frame,
                    _reservedBytes,
                    fenceContract);
                _settled = true;
                _owner = null;
                return true;
            }
        }

        public void Dispose()
            => TryCancel();

        public bool TryCancel()
        {
            lock (_gate)
            {
                if (_settled || _owner == null)
                    return false;
                var owner = _owner;
                owner.CancelControl(_reservedBytes);
                _owner = null;
                _settled = true;
                return true;
            }
        }
    }

    public sealed class U2R2ByteLease : IDisposable
    {
        private Action _release;

        internal U2R2ByteLease(Action release)
        {
            _release = release;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    public sealed class U2R2WriteLease : IDisposable
    {
        private Action _release;

        internal U2R2WriteLease(
            U2R2OutboundFrame frame,
            Action release)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _release = release;
        }

        public U2R2OutboundFrame Frame { get; }

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    public sealed class U2R2BoundedOutboundScheduler
    {
        private sealed class ControlEntry
        {
            public ControlEntry(
                U2R2OutboundFrame frame,
                U2R2ContractKey? fenceContract)
            {
                Frame = frame;
                FenceContract = fenceContract;
            }

            public U2R2OutboundFrame Frame { get; }
            public U2R2ContractKey? FenceContract { get; }
            public bool PriorityFence => FenceContract.HasValue;
        }

        private readonly object _gate = new();
        private readonly U2R2ProtocolLimits _limits;
        private readonly LinkedList<ControlEntry> _control = new();
        private readonly Dictionary<U2R2ContractKey, LinkedList<U2R2OutboundFrame>>
            _data = new();
        private readonly Queue<U2R2ContractKey> _roundRobin = new();
        private readonly HashSet<U2R2ContractKey> _active = new();
        private readonly HashSet<U2R2ContractKey> _revoked = new();
        private readonly HashSet<U2R2ContractKey> _retireWhenDrained = new();
        private ulong _controlDepthUsed;
        private ulong _controlBytesUsed;
        private ulong _dataQueuedDepth;
        private ulong _dataQueuedBytes;
        private ulong _transientBytes;
        private ulong _inFlightBytes;
        private ulong _controlBurst;
        private bool _readerActive;
        private bool _writerActive;
        private U2R2ContractKey _writerContract;
        private bool _writerHasContract;
        private bool _closed;

        public U2R2BoundedOutboundScheduler(U2R2ProtocolLimits limits)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        public ulong QueuedBytes
        {
            get
            {
                lock (_gate)
                    return checked(_controlBytesUsed + _dataQueuedBytes);
            }
        }

        public ulong TotalQueuedDepth
        {
            get
            {
                lock (_gate)
                    return checked(_controlDepthUsed + _dataQueuedDepth);
            }
        }

        public ulong DataQueuedDepth
        {
            get
            {
                lock (_gate)
                    return _dataQueuedDepth;
            }
        }

        public ulong TransientBytes
        {
            get
            {
                lock (_gate)
                    return _transientBytes;
            }
        }

        public ulong InFlightBytes
        {
            get
            {
                lock (_gate)
                    return _inFlightBytes;
            }
        }

        public ulong RevokedContractCount
        {
            get
            {
                lock (_gate)
                    return checked((ulong)_revoked.Count);
            }
        }

        public bool IsClosed
        {
            get
            {
                lock (_gate)
                    return _closed;
            }
        }

        public bool TryReserveControl(
            ulong bytes,
            out U2R2ControlReservation reservation)
        {
            lock (_gate)
            {
                reservation = null;
                if (_closed
                    || _controlDepthUsed >= _limits.ReservedControlQueueDepth
                    || _controlBytesUsed > _limits.ReservedControlQueueBytes
                    || bytes > _limits.ReservedControlQueueBytes - _controlBytesUsed)
                {
                    return false;
                }
                _controlDepthUsed++;
                _controlBytesUsed += bytes;
                reservation = new U2R2ControlReservation(this, bytes);
                return true;
            }
        }

        public U2R2EnqueueDisposition EnqueueData(
            U2R2OutboundFrame frame,
            U2R2QueueOverflowPolicy policy)
        {
            if (policy != U2R2QueueOverflowPolicy.Reject
                && policy != U2R2QueueOverflowPolicy.DropOldest
                && policy != U2R2QueueOverflowPolicy.ReplaceLatest)
            {
                throw new U2R2ProtocolException(
                    "invalid_contract",
                    "The U2R2 queue overflow policy is invalid.",
                    terminal: false);
            }
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.IsControl)
                throw new ArgumentException("A data queue requires a data frame.", nameof(frame));

            lock (_gate)
            {
                if (_closed)
                    return U2R2EnqueueDisposition.Rejected;
                if (_revoked.Contains(frame.Contract))
                    return U2R2EnqueueDisposition.Rejected;

                if (!_data.TryGetValue(frame.Contract, out var queue))
                {
                    queue = new LinkedList<U2R2OutboundFrame>();
                    _data.Add(frame.Contract, queue);
                }

                if (CanFitData(
                        queue,
                        frame.ByteCount,
                        removingDepth: 0,
                        removingBytes: 0))
                {
                    AddData(frame.Contract, queue, frame);
                    return U2R2EnqueueDisposition.Accepted;
                }
                if (policy == U2R2QueueOverflowPolicy.Reject || queue.Count == 0)
                {
                    RemoveEmptyQueue(frame.Contract, queue);
                    return U2R2EnqueueDisposition.Rejected;
                }

                var victim = policy == U2R2QueueOverflowPolicy.DropOldest
                    ? queue.First
                    : queue.Last;
                if (!CanFitData(
                        queue,
                        frame.ByteCount,
                        removingDepth: 1,
                        removingBytes: victim.Value.ByteCount))
                    return U2R2EnqueueDisposition.Rejected;

                queue.Remove(victim);
                _dataQueuedDepth--;
                _dataQueuedBytes -= victim.Value.ByteCount;
                AddData(frame.Contract, queue, frame, activate: false);
                return policy == U2R2QueueOverflowPolicy.DropOldest
                    ? U2R2EnqueueDisposition.DroppedOldest
                    : U2R2EnqueueDisposition.ReplacedLatest;
            }
        }

        public bool TryReserveTransient(
            ulong bytes,
            out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (_closed
                    || _transientBytes > _limits.MaxTransientBytes
                    || bytes > _limits.MaxTransientBytes - _transientBytes)
                    return false;
                _transientBytes += bytes;
                lease = new U2R2ByteLease(() => ReleaseTransient(bytes));
                return true;
            }
        }

        public bool TryBeginRead(ulong bytes, out U2R2ByteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (_closed
                    || _readerActive
                    || _inFlightBytes > _limits.MaxInFlightBytes
                    || bytes > _limits.MaxInFlightBytes - _inFlightBytes)
                {
                    return false;
                }
                _readerActive = true;
                _inFlightBytes += bytes;
                lease = new U2R2ByteLease(() => EndRead(bytes));
                return true;
            }
        }

        public bool TryBeginWrite(out U2R2WriteLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (_closed || _writerActive)
                    return false;

                var chooseControl = ShouldChooseControl(
                    out var selectedDataContract);
                U2R2OutboundFrame frame;
                if (chooseControl)
                {
                    frame = _control.First.Value.Frame;
                }
                else if (selectedDataContract.HasValue)
                {
                    var key = selectedDataContract.Value;
                    frame = _data[key].First.Value;
                }
                else if (_control.Count > 0)
                {
                    chooseControl = true;
                    frame = _control.First.Value.Frame;
                }
                else
                {
                    return false;
                }

                if (_inFlightBytes > _limits.MaxInFlightBytes
                    || frame.ByteCount > _limits.MaxInFlightBytes - _inFlightBytes)
                    return false;

                if (chooseControl)
                {
                    _control.RemoveFirst();
                    _controlDepthUsed--;
                    _controlBytesUsed -= frame.ByteCount;
                    _controlBurst++;
                    _writerHasContract = false;
                }
                else
                {
                    var key = selectedDataContract.Value;
                    RemoveActiveContract(key);
                    var queue = _data[key];
                    queue.RemoveFirst();
                    _dataQueuedDepth--;
                    _dataQueuedBytes -= frame.ByteCount;
                    if (queue.Count == 0)
                        _data.Remove(key);
                    else
                        Activate(key);
                    _controlBurst = 0;
                    _writerContract = key;
                    _writerHasContract = true;
                }

                _writerActive = true;
                _inFlightBytes += frame.ByteCount;
                lease = new U2R2WriteLease(
                    frame,
                    () => EndWrite(frame.ByteCount));
                return true;
            }
        }

        public void RevokeContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                if (!_revoked.Contains(key))
                {
                    var limit = checked(
                        _limits.MaxContracts + _limits.MaxTombstones);
                    if (checked((ulong)_revoked.Count) >= limit)
                    {
                        throw new InvalidOperationException(
                            "The U2R2 revoked-contract lifecycle exceeded its bound.");
                    }
                    _revoked.Add(key);
                }
                if (_data.TryGetValue(key, out var queue))
                {
                    foreach (var frame in queue)
                    {
                        _dataQueuedDepth--;
                        _dataQueuedBytes -= frame.ByteCount;
                    }
                    _data.Remove(key);
                }
                if (_active.Remove(key))
                {
                    var retained = new Queue<U2R2ContractKey>();
                    while (_roundRobin.Count > 0)
                    {
                        var candidate = _roundRobin.Dequeue();
                        if (candidate != key)
                            retained.Enqueue(candidate);
                    }
                    while (retained.Count > 0)
                        _roundRobin.Enqueue(retained.Dequeue());
                }
            }
        }

        internal void RetireContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                RevokeContract(key);
                _retireWhenDrained.Add(key);
                TryForgetRetired(key);
            }
        }

        internal void ForgetContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                if (_data.ContainsKey(key)
                    || _writerActive
                    && _writerHasContract
                    && _writerContract == key)
                {
                    _retireWhenDrained.Add(key);
                    return;
                }
                _revoked.Remove(key);
                _retireWhenDrained.Remove(key);
            }
        }

        public bool IsContractRevokedAndDrained(U2R2ContractKey key)
        {
            lock (_gate)
            {
                return _revoked.Contains(key)
                       && !_data.ContainsKey(key)
                       && !(_writerActive
                            && _writerHasContract
                            && _writerContract == key);
            }
        }

        internal void ActivateContract(U2R2ContractKey key)
        {
            lock (_gate)
            {
                if (_closed)
                {
                    throw new InvalidOperationException(
                        "The U2R2 outbound scheduler is closed.");
                }
                _revoked.Remove(key);
                _retireWhenDrained.Remove(key);
            }
        }

        internal void CommitControl(
            U2R2ControlReservation reservation,
            U2R2OutboundFrame frame,
            ulong reservedBytes,
            U2R2ContractKey? fenceContract)
        {
            if (reservation == null)
                throw new ArgumentNullException(nameof(reservation));
            if (frame == null || !frame.IsControl)
                throw new ArgumentException("A control reservation requires a control frame.");
            if (frame.ByteCount > reservedBytes)
            {
                throw new U2R2ProtocolException(
                    "capacity_exceeded",
                    "The U2R2 control response exceeds its reservation.",
                    terminal: false);
            }
            lock (_gate)
            {
                if (_closed)
                {
                    throw new InvalidOperationException(
                        "The U2R2 outbound scheduler is closed.");
                }
                _controlBytesUsed -= reservedBytes - frame.ByteCount;
                var entry = new ControlEntry(frame, fenceContract);
                if (fenceContract.HasValue)
                {
                    var insertion = _control.First;
                    while (insertion != null && insertion.Value.PriorityFence)
                        insertion = insertion.Next;
                    if (insertion == null)
                        _control.AddLast(entry);
                    else
                        _control.AddBefore(insertion, entry);
                }
                else
                    _control.AddLast(entry);
            }
        }

        internal void CancelControl(ulong reservedBytes)
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                if (_controlDepthUsed == 0 || _controlBytesUsed < reservedBytes)
                {
                    throw new InvalidOperationException(
                        "The U2R2 control reservation is not active.");
                }
                _controlDepthUsed--;
                _controlBytesUsed -= reservedBytes;
            }
        }

        private bool CanFitData(
            LinkedList<U2R2OutboundFrame> queue,
            ulong incomingBytes,
            ulong removingDepth,
            ulong removingBytes)
        {
            var queueDepth = checked((ulong)queue.Count);
            if (removingDepth > queueDepth)
                throw new InvalidOperationException("Data queue depth accounting underflow.");
            var contractDepth = queueDepth - removingDepth;
            var contractBytes = 0UL;
            foreach (var queued in queue)
                contractBytes = checked(contractBytes + queued.ByteCount);
            if (removingBytes > contractBytes
                || removingDepth > _dataQueuedDepth
                || removingBytes > _dataQueuedBytes)
            {
                throw new InvalidOperationException(
                    "Data queue byte accounting underflow.");
            }
            contractBytes -= removingBytes;

            var dataDepthLimit =
                _limits.MaxTotalQueueDepth - _limits.ReservedControlQueueDepth;
            var dataByteLimit =
                _limits.MaxQueuedBytes - _limits.ReservedControlQueueBytes;
            return contractDepth < _limits.MaxPerContractQueueDepth
                   && incomingBytes
                   <= _limits.MaxPerContractQueueBytes - contractBytes
                   && _dataQueuedDepth - removingDepth < dataDepthLimit
                   && incomingBytes
                   <= dataByteLimit - (_dataQueuedBytes - removingBytes);
        }

        private void AddData(
            U2R2ContractKey key,
            LinkedList<U2R2OutboundFrame> queue,
            U2R2OutboundFrame frame,
            bool activate = true)
        {
            queue.AddLast(frame);
            _dataQueuedDepth = checked(_dataQueuedDepth + 1);
            _dataQueuedBytes = checked(_dataQueuedBytes + frame.ByteCount);
            if (activate)
                Activate(key);
        }

        private void Activate(U2R2ContractKey key)
        {
            if (_active.Add(key))
                _roundRobin.Enqueue(key);
        }

        private void RemoveEmptyQueue(
            U2R2ContractKey key,
            LinkedList<U2R2OutboundFrame> queue)
        {
            if (queue.Count == 0)
                _data.Remove(key);
        }

        private bool ShouldChooseControl(
            out U2R2ContractKey? selectedDataContract)
        {
            selectedDataContract = null;
            if (_control.Count == 0)
            {
                if (_roundRobin.Count > 0)
                    selectedDataContract = _roundRobin.Peek();
                return false;
            }
            if (_roundRobin.Count == 0
                || _controlBurst < _limits.ControlBurstLimit)
            {
                return true;
            }

            foreach (var candidate in _roundRobin)
            {
                var blocked = false;
                foreach (var control in _control)
                {
                    if (control.FenceContract.HasValue
                        && control.FenceContract.Value == candidate)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (!blocked)
                {
                    selectedDataContract = candidate;
                    return false;
                }
            }

            return true;
        }

        private void RemoveActiveContract(U2R2ContractKey key)
        {
            if (!_active.Remove(key))
                throw new InvalidOperationException(
                    "The selected U2R2 contract is not active.");
            var retained = new Queue<U2R2ContractKey>();
            var removed = false;
            while (_roundRobin.Count > 0)
            {
                var candidate = _roundRobin.Dequeue();
                if (!removed && candidate == key)
                    removed = true;
                else
                    retained.Enqueue(candidate);
            }
            while (retained.Count > 0)
                _roundRobin.Enqueue(retained.Dequeue());
            if (!removed)
                throw new InvalidOperationException(
                    "The selected U2R2 contract is not scheduled.");
        }

        private void ReleaseTransient(ulong bytes)
        {
            lock (_gate)
            {
                if (_transientBytes < bytes)
                    throw new InvalidOperationException("Transient byte accounting underflow.");
                _transientBytes -= bytes;
            }
        }

        private void EndRead(ulong bytes)
        {
            lock (_gate)
            {
                if (!_readerActive || _inFlightBytes < bytes)
                    throw new InvalidOperationException("Reader accounting underflow.");
                _readerActive = false;
                _inFlightBytes -= bytes;
            }
        }

        private void EndWrite(ulong bytes)
        {
            lock (_gate)
            {
                if (!_writerActive || _inFlightBytes < bytes)
                    throw new InvalidOperationException("Writer accounting underflow.");
                var completedContract = _writerContract;
                var completedHasContract = _writerHasContract;
                _writerActive = false;
                _writerHasContract = false;
                _inFlightBytes -= bytes;
                if (completedHasContract)
                    TryForgetRetired(completedContract);
            }
        }

        internal void Close()
        {
            lock (_gate)
            {
                if (_closed)
                    return;
                _closed = true;
                _control.Clear();
                _data.Clear();
                _roundRobin.Clear();
                _active.Clear();
                _revoked.Clear();
                _retireWhenDrained.Clear();
                _controlDepthUsed = 0;
                _controlBytesUsed = 0;
                _dataQueuedDepth = 0;
                _dataQueuedBytes = 0;
                _controlBurst = 0;
            }
        }

        private void TryForgetRetired(U2R2ContractKey key)
        {
            if (!_retireWhenDrained.Contains(key)
                || _data.ContainsKey(key)
                || _writerActive
                && _writerHasContract
                && _writerContract == key)
            {
                return;
            }
            _retireWhenDrained.Remove(key);
            _revoked.Remove(key);
        }

    }
}
