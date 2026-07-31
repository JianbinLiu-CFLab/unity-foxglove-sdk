// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Bounded durable ownership for detached provider workers that outlive shutdown.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// A detached lease contains only worker-reachable resources. It must not
    /// retain a Manager, provider component, user component, or callback.
    /// </summary>
    public interface IFoxRunDetachedRetirementLease : IDisposable
    {
    }

    public readonly struct FoxRunTransportRetirementInfo
    {
        internal FoxRunTransportRetirementInfo(
            FoxRunTransportId providerId,
            FoxRunTransportDirection direction,
            ulong generation,
            string workerIdentity,
            long retainedBytes,
            int retainedResources,
            DateTime retiredAtUtc)
        {
            ProviderId = providerId;
            Direction = direction;
            Generation = generation;
            WorkerIdentity = workerIdentity ?? string.Empty;
            RetainedBytes = retainedBytes;
            RetainedResources = retainedResources;
            RetiredAtUtc = retiredAtUtc;
        }

        public FoxRunTransportId ProviderId { get; }
        public FoxRunTransportDirection Direction { get; }
        public ulong Generation { get; }
        public string WorkerIdentity { get; }
        public long RetainedBytes { get; }
        public int RetainedResources { get; }
        public DateTime RetiredAtUtc { get; }
    }

    /// <summary>
    /// Process-wide bounded exception to Manager-local ownership. Capacity is
    /// reserved before workers start; timeout converts an existing slot in
    /// place and therefore cannot request or allocate another slot.
    /// </summary>
    public sealed class FoxRunTransportRetirementOwner
    {
        public const int DefaultCapacity = 16;
        private static readonly FoxRunTransportRetirementOwner SharedInstance =
            new FoxRunTransportRetirementOwner(DefaultCapacity);

        private readonly object _gate = new object();
        private readonly Slot[] _slots;
        private ulong _nextReservation;

        private FoxRunTransportRetirementOwner(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _slots = new Slot[capacity];
        }

        public static FoxRunTransportRetirementOwner Shared => SharedInstance;
        public int Capacity => _slots.Length;

        public int OccupiedCount
        {
            get
            {
                lock (_gate)
                {
                    var count = 0;
                    for (var i = 0; i < _slots.Length; i++)
                        if (_slots[i].State != SlotState.Free)
                            count++;
                    return count;
                }
            }
        }

        public int RetiredCount
        {
            get
            {
                lock (_gate)
                {
                    var count = 0;
                    for (var i = 0; i < _slots.Length; i++)
                        if (_slots[i].State == SlotState.Retired
                            || _slots[i].State == SlotState.Completing)
                            count++;
                    return count;
                }
            }
        }

        public bool TryReserve(
            FoxRunTransportId providerId,
            FoxRunTransportDirection direction,
            ulong generation,
            int workerCount,
            out FoxRunTransportRetirementReservation reservation)
            => TryReserveCore(
                providerId,
                direction,
                generation,
                workerCount,
                exclusive: false,
                out reservation);

        /// <summary>
        /// Atomically reserves worker slots and rejects a second reservation
        /// for the same Provider direction while either reservation is active
        /// or retired. This is for transports whose detached worker may still
        /// touch exclusive process or socket state.
        /// </summary>
        public bool TryReserveExclusive(
            FoxRunTransportId providerId,
            FoxRunTransportDirection direction,
            ulong generation,
            int workerCount,
            out FoxRunTransportRetirementReservation reservation)
            => TryReserveCore(
                providerId,
                direction,
                generation,
                workerCount,
                exclusive: true,
                out reservation);

        private bool TryReserveCore(
            FoxRunTransportId providerId,
            FoxRunTransportDirection direction,
            ulong generation,
            int workerCount,
            bool exclusive,
            out FoxRunTransportRetirementReservation reservation)
        {
            if (workerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            if (direction != FoxRunTransportDirection.Publish
                && direction != FoxRunTransportDirection.Subscribe)
                throw new ArgumentOutOfRangeException(nameof(direction));

            lock (_gate)
            {
                var freeCount = 0;
                for (var i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].State != SlotState.Free
                        && _slots[i].ProviderId == providerId
                        && _slots[i].Direction == direction
                        && (exclusive || _slots[i].Exclusive))
                    {
                        reservation = null;
                        return false;
                    }
                    if (_slots[i].State == SlotState.Free)
                        freeCount++;
                }
                if (freeCount < workerCount)
                {
                    reservation = null;
                    return false;
                }

                if (_nextReservation == ulong.MaxValue)
                    throw new InvalidOperationException(
                        "FoxRun transport retirement reservation generation is exhausted.");
                var reservationId = ++_nextReservation;
                var indexes = new int[workerCount];
                var cursor = 0;
                for (var i = 0; i < _slots.Length && cursor < indexes.Length; i++)
                {
                    if (_slots[i].State != SlotState.Free)
                        continue;
                    _slots[i] = Slot.Active(
                        reservationId,
                        providerId,
                        direction,
                        generation,
                        exclusive);
                    indexes[cursor++] = i;
                }

                reservation = new FoxRunTransportRetirementReservation(
                    this,
                    reservationId,
                    indexes);
                return true;
            }
        }

        public IReadOnlyList<FoxRunTransportRetirementInfo> CaptureRetired()
        {
            lock (_gate)
            {
                var result = new List<FoxRunTransportRetirementInfo>();
                for (var i = 0; i < _slots.Length; i++)
                {
                    ref var slot = ref _slots[i];
                    if (slot.State != SlotState.Retired
                        && slot.State != SlotState.Completing)
                        continue;
                    result.Add(new FoxRunTransportRetirementInfo(
                        slot.ProviderId,
                        slot.Direction,
                        slot.Generation,
                        slot.WorkerIdentity,
                        slot.RetainedBytes,
                        slot.RetainedResources,
                        slot.RetiredAtUtc));
                }

                return result.AsReadOnly();
            }
        }

        internal static FoxRunTransportRetirementOwner CreateForTests(int capacity)
            => new FoxRunTransportRetirementOwner(capacity);

        internal void WarmUp()
        {
            lock (_gate)
            {
                _ = _slots.Length;
            }
        }

        internal bool TryReturn(
            ulong reservationId,
            int slotIndex)
        {
            lock (_gate)
            {
                if (!Owns(slotIndex, reservationId, SlotState.Active))
                    return false;
                _slots[slotIndex] = default;
                return true;
            }
        }

        internal bool TryConvertToRetired(
            ulong reservationId,
            int slotIndex,
            IFoxRunDetachedRetirementLease lease,
            string workerIdentity,
            long retainedBytes,
            int retainedResources)
        {
            if (lease == null)
                throw new ArgumentNullException(nameof(lease));
            if (string.IsNullOrWhiteSpace(workerIdentity))
                throw new ArgumentException("Worker identity cannot be empty.", nameof(workerIdentity));
            if (retainedBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(retainedBytes));
            if (retainedResources < 0)
                throw new ArgumentOutOfRangeException(nameof(retainedResources));

            lock (_gate)
            {
                if (!Owns(slotIndex, reservationId, SlotState.Active))
                    return false;
                ref var slot = ref _slots[slotIndex];
                slot.State = SlotState.Retired;
                slot.Lease = lease;
                slot.WorkerIdentity = workerIdentity;
                slot.RetainedBytes = retainedBytes;
                slot.RetainedResources = retainedResources;
                slot.RetiredAtUtc = DateTime.UtcNow;
                return true;
            }
        }

        internal bool TryCompleteRetired(
            ulong reservationId,
            int slotIndex)
        {
            IFoxRunDetachedRetirementLease lease;
            lock (_gate)
            {
                if (!Owns(slotIndex, reservationId, SlotState.Retired))
                    return false;
                lease = _slots[slotIndex].Lease;
                _slots[slotIndex].State = SlotState.Completing;
            }

            try
            {
                lease.Dispose();
                return true;
            }
            finally
            {
                lock (_gate)
                {
                    if (Owns(slotIndex, reservationId, SlotState.Completing))
                        _slots[slotIndex] = default;
                }
            }
        }

        private bool Owns(
            int slotIndex,
            ulong reservationId,
            SlotState requiredState)
            => slotIndex >= 0
               && slotIndex < _slots.Length
               && _slots[slotIndex].ReservationId == reservationId
               && _slots[slotIndex].State == requiredState;

        private enum SlotState : byte
        {
            Free = 0,
            Active = 1,
            Retired = 2,
            Completing = 3
        }

        private struct Slot
        {
            internal SlotState State;
            internal ulong ReservationId;
            internal FoxRunTransportId ProviderId;
            internal FoxRunTransportDirection Direction;
            internal ulong Generation;
            internal IFoxRunDetachedRetirementLease Lease;
            internal string WorkerIdentity;
            internal long RetainedBytes;
            internal int RetainedResources;
            internal DateTime RetiredAtUtc;
            internal bool Exclusive;

            internal static Slot Active(
                ulong reservationId,
                FoxRunTransportId providerId,
                FoxRunTransportDirection direction,
                ulong generation,
                bool exclusive)
                => new Slot
                {
                    State = SlotState.Active,
                    ReservationId = reservationId,
                    ProviderId = providerId,
                    Direction = direction,
                    Generation = generation,
                    Exclusive = exclusive
                };
        }
    }

    /// <summary>Preallocated group of atomic worker-retirement reservations.</summary>
    public sealed class FoxRunTransportRetirementReservation : IDisposable
    {
        private readonly FoxRunTransportRetirementOwner _owner;
        private readonly ulong _reservationId;
        private readonly int[] _slotIndexes;

        internal FoxRunTransportRetirementReservation(
            FoxRunTransportRetirementOwner owner,
            ulong reservationId,
            int[] slotIndexes)
        {
            _owner = owner;
            _reservationId = reservationId;
            _slotIndexes = slotIndexes;
        }

        public int WorkerCount => _slotIndexes.Length;

        public bool TryReturn(int workerIndex)
            => _owner.TryReturn(_reservationId, Slot(workerIndex));

        public bool TryConvertToRetired(
            int workerIndex,
            IFoxRunDetachedRetirementLease lease,
            string workerIdentity,
            long retainedBytes,
            int retainedResources)
            => _owner.TryConvertToRetired(
                _reservationId,
                Slot(workerIndex),
                lease,
                workerIdentity,
                retainedBytes,
                retainedResources);

        public bool TryCompleteRetired(int workerIndex)
            => _owner.TryCompleteRetired(_reservationId, Slot(workerIndex));

        /// <summary>
        /// Primes thread-local monitor infrastructure before an allocation
        /// assertion; it never changes reservation state.
        /// </summary>
        public void WarmUpTimeoutConversionForCurrentThread() => _owner.WarmUp();

        public void Dispose()
        {
            for (var i = 0; i < _slotIndexes.Length; i++)
                _owner.TryReturn(_reservationId, _slotIndexes[i]);
        }

        private int Slot(int workerIndex)
        {
            if ((uint)workerIndex >= (uint)_slotIndexes.Length)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            return _slotIndexes[workerIndex];
        }
    }
}
