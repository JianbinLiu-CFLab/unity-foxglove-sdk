// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Provides a small thread-safe queue with frame and byte budgets for
// cross-thread runtime event bridges.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Thread-safe FIFO queue that rejects new items when frame or byte budgets
    /// would be exceeded.
    /// </summary>
    internal sealed class BoundedEventQueue<T>
    {
        private readonly object _lock = new();
        private readonly Queue<QueuedItem> _queue = new();
        private readonly Func<T, int> _measureBytes;
        private readonly int _maxFrames;
        private readonly long _maxBytes;
        private readonly bool _hasByteBudget;
        private long _queuedBytes;
        private long _droppedCount;
        private long _droppedBytes;

        public BoundedEventQueue(int maxFrames, long maxBytes, Func<T, int> measureBytes)
        {
            _maxFrames = Math.Max(1, maxFrames);
            _maxBytes = Math.Max(0, maxBytes);
            _hasByteBudget = maxBytes > 0;
            _measureBytes = measureBytes ?? (_ => 0);
        }

        public bool TryEnqueue(T item, out BoundedEventQueueOverflow overflow)
        {
            var itemBytes = Math.Max(0, _measureBytes(item));
            lock (_lock)
            {
                if (_queue.Count + 1 > _maxFrames || (_hasByteBudget && _queuedBytes + itemBytes > _maxBytes))
                {
                    _droppedCount++;
                    _droppedBytes += itemBytes;
                    overflow = new BoundedEventQueueOverflow(
                        _queue.Count,
                        _queuedBytes,
                        itemBytes,
                        _droppedCount,
                        _droppedBytes);
                    return false;
                }

                _queue.Enqueue(new QueuedItem(item, itemBytes));
                _queuedBytes += itemBytes;
                overflow = default;
                return true;
            }
        }

        public bool TryDequeue(out T item)
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                {
                    item = default;
                    return false;
                }

                var queued = _queue.Dequeue();
                item = queued.Item;
                _queuedBytes = Math.Max(0, _queuedBytes - queued.SizeBytes);
                return true;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
                _queuedBytes = 0;
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return _queue.Count;
            }
        }

        public long QueuedBytes
        {
            get
            {
                lock (_lock)
                    return _queuedBytes;
            }
        }

        public long DroppedCount
        {
            get
            {
                lock (_lock)
                    return _droppedCount;
            }
        }

        public long DroppedBytes
        {
            get
            {
                lock (_lock)
                    return _droppedBytes;
            }
        }

        private readonly struct QueuedItem
        {
            public QueuedItem(T item, int sizeBytes)
            {
                Item = item;
                SizeBytes = sizeBytes;
            }

            public T Item { get; }
            public int SizeBytes { get; }
        }
    }

    /// <summary>
    /// Snapshot describing a rejected bounded-queue enqueue attempt.
    /// </summary>
    internal readonly struct BoundedEventQueueOverflow
    {
        public BoundedEventQueueOverflow(
            int queuedFrames,
            long queuedBytes,
            long rejectedBytes,
            long droppedCount,
            long droppedBytes)
        {
            QueuedFrames = queuedFrames;
            QueuedBytes = queuedBytes;
            RejectedBytes = rejectedBytes;
            DroppedCount = droppedCount;
            DroppedBytes = droppedBytes;
        }

        public int QueuedFrames { get; }
        public long QueuedBytes { get; }
        public long RejectedBytes { get; }
        public long DroppedCount { get; }
        public long DroppedBytes { get; }
    }
}
