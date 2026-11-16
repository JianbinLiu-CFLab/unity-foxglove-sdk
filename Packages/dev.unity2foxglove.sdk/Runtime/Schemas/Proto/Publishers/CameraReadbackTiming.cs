// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Tracks AsyncGPUReadback latency timing for camera diagnostics.

using System;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Stores camera readback start ticks until a request is consumed by the main thread.
    /// </summary>
    internal sealed class CameraReadbackTiming
    {
        private const int DefaultMaxTrackedRequests = 8;
        private const int MaxTrackedRequests = 256;
        private ulong[] _requestKeys = new ulong[DefaultMaxTrackedRequests];
        private long[] _requestTicks = new long[DefaultMaxTrackedRequests];
        private long[] _requestSequences = new long[DefaultMaxTrackedRequests];
        private int _nextSlot;
        private int _requestCount;
        private long _nextSequence;

        public void Remember(ulong unixNs, long ticks)
            => Remember(unixNs, ticks, DefaultMaxTrackedRequests);

        public void Remember(ulong unixNs, long ticks, int maxTrackedRequests)
        {
            var capacity = Math.Min(MaxTrackedRequests, Math.Max(1, maxTrackedRequests));
            if (_requestKeys.Length != capacity)
            {
                Resize(capacity);
            }

            for (var i = 0; i < _requestKeys.Length; i++)
            {
                if (_requestSequences[i] == 0L || _requestKeys[i] != unixNs)
                    continue;

                _requestTicks[i] = ticks;
                return;
            }

            var slot = _nextSlot % _requestKeys.Length;
            if (_requestCount < _requestKeys.Length)
            {
                for (var offset = 0; offset < _requestKeys.Length; offset++)
                {
                    var candidate = (slot + offset) % _requestKeys.Length;
                    if (_requestSequences[candidate] == 0L)
                    {
                        slot = candidate;
                        break;
                    }
                }
            }

            _nextSlot = slot;
            _nextSlot = (_nextSlot + 1) % _requestKeys.Length;
            _requestKeys[slot] = unixNs;
            _requestTicks[slot] = ticks;
            _requestSequences[slot] = ++_nextSequence;
            if (_requestCount < _requestKeys.Length)
                _requestCount++;
        }

        public double TakeLatencyMs(ulong unixNs)
        {
            for (var i = 0; i < _requestKeys.Length; i++)
            {
                if (_requestSequences[i] == 0L || _requestKeys[i] != unixNs)
                    continue;

                var ticks = _requestTicks[i];
                _requestKeys[i] = 0UL;
                _requestTicks[i] = 0L;
                _requestSequences[i] = 0L;
                _requestCount--;
                return ElapsedMs(ticks);
            }

            return 0;
        }

        public void Clear()
        {
            Array.Clear(_requestKeys, 0, _requestKeys.Length);
            Array.Clear(_requestTicks, 0, _requestTicks.Length);
            Array.Clear(_requestSequences, 0, _requestSequences.Length);
            _nextSlot = 0;
            _requestCount = 0;
        }

        private void Resize(int capacity)
        {
            var entries = new System.Collections.Generic.List<RequestEntry>(_requestCount);
            for (var index = 0; index < _requestKeys.Length; index++)
            {
                if (_requestSequences[index] == 0L)
                    continue;

                entries.Add(new RequestEntry(
                    _requestKeys[index],
                    _requestTicks[index],
                    _requestSequences[index]));
            }

            entries.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            var keepCount = Math.Min(capacity, entries.Count);
            var keys = new ulong[capacity];
            var ticks = new long[capacity];
            var sequences = new long[capacity];
            for (var index = 0; index < keepCount; index++)
            {
                var entry = entries[entries.Count - keepCount + index];
                keys[index] = entry.Key;
                ticks[index] = entry.Ticks;
                sequences[index] = entry.Sequence;
            }

            _requestKeys = keys;
            _requestTicks = ticks;
            _requestSequences = sequences;
            _requestCount = keepCount;
            _nextSlot = keepCount == capacity ? 0 : keepCount;
        }

        private readonly struct RequestEntry
        {
            public RequestEntry(ulong key, long ticks, long sequence)
            {
                Key = key;
                Ticks = ticks;
                Sequence = sequence;
            }

            public ulong Key { get; }
            public long Ticks { get; }
            public long Sequence { get; }
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
    }
}
