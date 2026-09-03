// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Atomic admission barrier for transport-thread client events.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Counts client-event items removed or rejected while a session epoch is
    /// being retired.  The count is kept with the originating generation so a
    /// late transport callback cannot be reported as a drop from a new session.
    /// </summary>
    internal readonly struct ClientEventDropCounts
    {
        internal ClientEventDropCounts(long events, long payloadBytes)
        {
            Events = events;
            PayloadBytes = payloadBytes;
        }

        internal long Events { get; }
        internal long PayloadBytes { get; }
    }

    internal readonly struct ClientEventDropSnapshot
    {
        internal ClientEventDropSnapshot(ulong generation, long events, long payloadBytes)
        {
            Generation = generation;
            Events = events;
            PayloadBytes = payloadBytes;
        }

        internal ulong Generation { get; }
        internal long Events { get; }
        internal long PayloadBytes { get; }
    }

    /// <summary>
    /// Serializes event admission with retirement clearing.  A transport
    /// callback can outlive delegate removal, so checking a generation before
    /// taking the queue lock is not sufficient: retirement must close the
    /// admission gate and clear the queues while holding the same lock.
    /// </summary>
    internal sealed class ClientEventAdmissionState
    {
        private readonly object _gate = new();
        private ulong _generation;
        private bool _accepting;
        private readonly Dictionary<ulong, DropAccumulator> _retirementDrops = new();
        private long _totalRetirementDropCount;
        private long _totalRetirementDropBytes;

        internal void Activate(ulong generation)
        {
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));

            lock (_gate)
            {
                _generation = generation;
                _accepting = true;
            }
        }

        /// <summary>
        /// Attempts to enqueue while atomically checking the active epoch.
        /// The queue operation runs under the admission lock, so an in-flight
        /// callback cannot slip between retirement invalidation and clearing.
        /// </summary>
        internal ClientEventAdmissionResult TryEnqueue<T>(
            BoundedEventQueue<T> queue,
            ulong eventGeneration,
            T item,
            out BoundedEventQueueOverflow overflow)
            => TryEnqueue(queue, eventGeneration, item, null, out overflow);

        /// <summary>
        /// Attempts an enqueue and records rejected items while the admission
        /// lock is held.  Recording in this transaction prevents a retirement
        /// flush from racing the accounting performed by a late callback.
        /// </summary>
        internal ClientEventAdmissionResult TryEnqueue<T>(
            BoundedEventQueue<T> queue,
            ulong eventGeneration,
            T item,
            Func<T, int> measureBytes,
            out BoundedEventQueueOverflow overflow)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));

            lock (_gate)
            {
                if (!_accepting || eventGeneration == 0 || eventGeneration != _generation)
                {
                    var itemBytes = measureBytes == null
                        ? 0
                        : Math.Max(0, measureBytes(item));
                    RecordDropLocked(eventGeneration, 1, itemBytes);
                    overflow = default;
                    return ClientEventAdmissionResult.Retired;
                }

                return queue.TryEnqueue(item, out overflow)
                    ? ClientEventAdmissionResult.Enqueued
                    : ClientEventAdmissionResult.QueueFull;
            }
        }

        /// <summary>
        /// Closes admission and clears all queues as one transaction.  The
        /// callback is invoked while the gate is held and must only clear the
        /// queues supplied by this state owner.
        /// </summary>
        internal bool InvalidateAndClear(Action clearQueues)
        {
            if (clearQueues == null)
                throw new ArgumentNullException(nameof(clearQueues));

            return InvalidateAndClear(() =>
            {
                clearQueues();
                return default(ClientEventDropCounts);
            });
        }

        /// <summary>
        /// Closes admission and clears queues while retaining the removed-item
        /// counts under the same lock.  The owner can subsequently call
        /// <see cref="TakeRetirementDrops"/> outside the lock to emit diagnostics.
        /// </summary>
        internal bool InvalidateAndClear(Func<ClientEventDropCounts> clearQueues)
        {
            if (clearQueues == null)
                throw new ArgumentNullException(nameof(clearQueues));

            lock (_gate)
            {
                var wasAccepting = _accepting;
                _accepting = false;
                var cleared = clearQueues();
                RecordDropLocked(_generation, cleared.Events, cleared.PayloadBytes);
                return wasAccepting;
            }
        }

        internal ClientEventDropSnapshot[] TakeRetirementDrops()
        {
            lock (_gate)
            {
                if (_retirementDrops.Count == 0)
                    return Array.Empty<ClientEventDropSnapshot>();

                var snapshots = new ClientEventDropSnapshot[_retirementDrops.Count];
                var index = 0;
                foreach (var pair in _retirementDrops)
                {
                    snapshots[index++] = new ClientEventDropSnapshot(
                        pair.Key,
                        pair.Value.Events,
                        pair.Value.PayloadBytes);
                }

                _retirementDrops.Clear();
                return snapshots;
            }
        }

        internal long TotalRetirementDropCount
        {
            get
            {
                lock (_gate)
                    return _totalRetirementDropCount;
            }
        }

        internal long TotalRetirementDropBytes
        {
            get
            {
                lock (_gate)
                    return _totalRetirementDropBytes;
            }
        }

        internal bool IsAccepting(ulong generation)
        {
            lock (_gate)
                return _accepting && generation != 0 && generation == _generation;
        }

        private void RecordDropLocked(ulong generation, long events, long payloadBytes)
        {
            if (events <= 0)
                return;

            if (!_retirementDrops.TryGetValue(generation, out var accumulator))
            {
                accumulator = new DropAccumulator();
                _retirementDrops.Add(generation, accumulator);
            }

            accumulator.Events += events;
            accumulator.PayloadBytes += Math.Max(0, payloadBytes);
            _totalRetirementDropCount += events;
            _totalRetirementDropBytes += Math.Max(0, payloadBytes);
        }

        private sealed class DropAccumulator
        {
            internal long Events;
            internal long PayloadBytes;
        }
    }

    internal enum ClientEventAdmissionResult
    {
        Enqueued,
        QueueFull,
        Retired
    }
}
