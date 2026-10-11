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

    internal sealed partial class Ros2BridgeOutboundScheduler : IDisposable
    {
    }
}
