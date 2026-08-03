// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Imu
// Purpose: Bounded queue for virtual IMU samples.

using System;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>One queued IMU sample in Foxglove coordinates.</summary>
    internal readonly struct ImuSample
    {
        /// <summary>Create one queued IMU sample.</summary>
        public ImuSample(ulong timestampNs, Vector3 linearAcceleration, Vector3 angularVelocity, Quaternion orientation)
        {
            TimestampNs = timestampNs;
            LinearAcceleration = linearAcceleration;
            AngularVelocity = angularVelocity;
            Orientation = orientation;
        }

        /// <summary>Sample timestamp in Unix nanoseconds.</summary>
        public ulong TimestampNs { get; }

        /// <summary>Linear acceleration in the IMU body frame.</summary>
        public Vector3 LinearAcceleration { get; }

        /// <summary>Angular velocity in the IMU body frame.</summary>
        public Vector3 AngularVelocity { get; }

        /// <summary>Orientation in the IMU body frame.</summary>
        public Quaternion Orientation { get; }
    }

    /// <summary>
    /// Bounded sample queue that drops oldest samples under back-pressure.
    /// </summary>
    internal sealed class ImuSampleQueue
    {
        internal const int MinCapacity = 8;

        private ImuSample[] _items = new ImuSample[MinCapacity];
        private int _head;
        private int _count;
        private long _droppedCount;

        /// <summary>Number of samples currently queued.</summary>
        public int Count => _count;

        /// <summary>Total number of oldest samples dropped since the last resize.</summary>
        public long DroppedCount => _droppedCount;

        /// <summary>Resize the bounded queue while preserving the oldest available samples.</summary>
        public void Resize(int capacity, int minCapacity)
        {
            if (capacity < minCapacity)
                capacity = minCapacity;
            if (_items.Length == capacity)
                return;

            var next = new ImuSample[capacity];
            var copyCount = Math.Min(_count, capacity);
            for (var i = 0; i < copyCount; i++)
            {
                TryDequeue(out next[i]);
            }

            _items = next;
            _count = copyCount;
            _head = 0;
            _droppedCount = 0;
        }

        /// <summary>Add a sample, dropping the oldest sample when the queue is full.</summary>
        public void Enqueue(ImuSample sample)
        {
            if (_count < _items.Length)
            {
                var tail = (_head + _count) % _items.Length;
                _items[tail] = sample;
                _count++;
                return;
            }

            _items[_head] = sample;
            _head = (_head + 1) % _items.Length;
            RecordDropped(1);
        }

        /// <summary>Account for samples omitted before admission, saturating at the counter limit.</summary>
        public void RecordDropped(long count)
        {
            if (count <= 0 || _droppedCount == long.MaxValue)
                return;

            _droppedCount = count >= long.MaxValue - _droppedCount
                ? long.MaxValue
                : _droppedCount + count;
        }

        /// <summary>Try to remove and return the oldest queued sample.</summary>
        public bool TryDequeue(out ImuSample sample)
        {
            if (_count == 0)
            {
                sample = default;
                return false;
            }

            var index = _head;
            sample = _items[index];
            _head = (_head + 1) % _items.Length;
            _count--;
            return true;
        }
    }
}
