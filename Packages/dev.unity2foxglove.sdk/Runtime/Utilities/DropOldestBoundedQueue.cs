// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Small thread-safe bounded queue with last-value-wins behavior.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Util
{
    public sealed class DropOldestBoundedQueue<T>
    {
        private readonly object _gate = new object();
        private readonly Queue<T> _queue = new Queue<T>();

        public DropOldestBoundedQueue(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            Capacity = capacity;
        }

        public int Capacity { get; }

        public int Count
        {
            get
            {
                lock (_gate)
                    return _queue.Count;
            }
        }

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

        public void Clear()
        {
            lock (_gate)
                _queue.Clear();
        }
    }
}
