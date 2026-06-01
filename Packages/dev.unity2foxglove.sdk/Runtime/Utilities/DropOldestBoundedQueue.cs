// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Small thread-safe bounded queue with last-value-wins behavior.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// A small thread-safe bounded queue that drops the oldest element when over capacity.
    /// Useful for keeping the freshest async work items during overload.
    /// </summary>
    public sealed class DropOldestBoundedQueue<T>
    {
        private readonly object _gate = new object();
        private readonly Queue<T> _queue = new Queue<T>();

        /// <summary>
        /// Initializes a bounded queue with a positive capacity.
        /// </summary>
        public DropOldestBoundedQueue(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            Capacity = capacity;
        }

        /// <summary>
        /// Maximum number of items kept in the queue.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Current queue size.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_gate)
                    return _queue.Count;
            }
        }

        /// <summary>
        /// Enqueues an item. If the queue is full, drops the oldest item and returns <c>true</c>.
        /// </summary>
        public bool Enqueue(T item)
        {
            lock (_gate)
            {
                var dropped = false;
                if (_queue.Count >= Capacity)
                {
                    _queue.Dequeue();
                    dropped = true;
                }

                _queue.Enqueue(item);
                return dropped;
            }
        }

        /// <summary>
        /// Attempts to dequeue one item.
        /// </summary>
        public bool TryDequeue(out T item)
        {
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    item = default;
                    return false;
                }

                item = _queue.Dequeue();
                return true;
            }
        }

        /// <summary>
        /// Clears all queued items.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
                _queue.Clear();
        }
    }
}
