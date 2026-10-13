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
}
