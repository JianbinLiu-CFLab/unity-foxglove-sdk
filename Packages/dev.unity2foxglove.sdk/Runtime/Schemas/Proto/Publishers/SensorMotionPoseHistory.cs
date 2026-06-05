// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Bounded Unity-free pose history for point-cloud visualization deskew.

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>One timestamped world-from-sensor pose sample.</summary>
    internal readonly struct SensorMotionPoseSample
    {
        /// <summary>Create a normalized timestamped world-from-sensor pose sample.</summary>
        public SensorMotionPoseSample(ulong unixNs, Vector3 translation, Quaternion rotation)
        {
            UnixNs = unixNs;
            Translation = translation;
            Rotation = Quaternion.Normalize(rotation);
        }

        /// <summary>Pose timestamp in Unix nanoseconds.</summary>
        public ulong UnixNs { get; }

        /// <summary>World-space sensor translation at the sample timestamp.</summary>
        public Vector3 Translation { get; }

        /// <summary>World-space sensor rotation at the sample timestamp.</summary>
        public Quaternion Rotation { get; }
    }

    /// <summary>
    /// Bounded ring buffer of sensor poses sampled from the Unity main thread.
    /// Consumers clone snapshots before background workers use the data.
    /// </summary>
    internal sealed class SensorMotionPoseHistory
    {
        private const int DefaultCapacity = 256;
        private const ulong DefaultMaxAgeNs = 5_000_000_000UL;

        private readonly SensorMotionPoseSample[] _samples;
        private readonly ulong _maxAgeNs;
        private int _start;
        private int _count;

        /// <summary>Create a bounded pose history ring buffer.</summary>
        public SensorMotionPoseHistory(int capacity = DefaultCapacity, ulong maxAgeNs = DefaultMaxAgeNs)
        {
            if (capacity < 2)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Pose history requires at least two samples.");

            _samples = new SensorMotionPoseSample[capacity];
            _maxAgeNs = maxAgeNs;
        }

        /// <summary>Number of pose samples currently retained.</summary>
        public int Count => _count;

        /// <summary>Remove all retained pose samples.</summary>
        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        /// <summary>Add or replace a pose sample, trimming samples older than the configured horizon.</summary>
        public void Add(ulong unixNs, Vector3 translation, Quaternion rotation)
        {
            var sample = new SensorMotionPoseSample(unixNs, translation, rotation);
            if (_count > 0)
            {
                var lastIndex = PhysicalIndex(_count - 1);
                if (unixNs <= _samples[lastIndex].UnixNs)
                {
                    _samples[lastIndex] = sample;
                    return;
                }
            }

            if (_count < _samples.Length)
            {
                _samples[PhysicalIndex(_count)] = sample;
                _count++;
            }
            else
            {
                _samples[_start] = sample;
                _start = (_start + 1) % _samples.Length;
            }

            TrimOldSamples(unixNs);
        }

        /// <summary>Clone retained samples in chronological order for background-worker use.</summary>
        public SensorMotionPoseSample[] Snapshot()
        {
            var snapshot = new SensorMotionPoseSample[_count];
            for (var i = 0; i < _count; i++)
                snapshot[i] = _samples[PhysicalIndex(i)];
            return snapshot;
        }

        /// <summary>True when the retained sample range covers an inclusive scan time interval.</summary>
        public bool Covers(ulong startUnixNs, ulong endUnixNs)
        {
            if (_count < 2 || startUnixNs > endUnixNs)
                return false;

            return _samples[_start].UnixNs <= startUnixNs
                   && _samples[PhysicalIndex(_count - 1)].UnixNs >= endUnixNs;
        }

        private void TrimOldSamples(ulong latestUnixNs)
        {
            while (_count > 2)
            {
                var first = _samples[_start].UnixNs;
                if (latestUnixNs <= first || latestUnixNs - first <= _maxAgeNs)
                    return;

                _start = (_start + 1) % _samples.Length;
                _count--;
            }
        }

        private int PhysicalIndex(int logicalIndex)
            => (_start + logicalIndex) % _samples.Length;
    }

    /// <summary>Interpolation helpers for cloned pose history snapshots.</summary>
    internal static class SensorMotionPoseHistoryMath
    {
        /// <summary>Interpolate a pose sample at the requested timestamp.</summary>
        public static bool TryInterpolate(
            SensorMotionPoseSample[] samples,
            ulong unixNs,
            out SensorMotionPoseSample pose)
        {
            pose = default;
            if (samples == null || samples.Length == 0)
                return false;

            if (samples.Length == 1)
            {
                if (samples[0].UnixNs != unixNs)
                    return false;

                pose = samples[0];
                return true;
            }

            if (unixNs < samples[0].UnixNs || unixNs > samples[samples.Length - 1].UnixNs)
                return false;

            for (var i = 1; i < samples.Length; i++)
            {
                var right = samples[i];
                if (unixNs > right.UnixNs)
                    continue;

                var left = samples[i - 1];
                if (right.UnixNs == left.UnixNs)
                {
                    pose = right;
                    return true;
                }

                var t = (double)(unixNs - left.UnixNs) / (right.UnixNs - left.UnixNs);
                pose = new SensorMotionPoseSample(
                    unixNs,
                    Vector3.Lerp(left.Translation, right.Translation, (float)t),
                    Quaternion.Slerp(left.Rotation, right.Rotation, (float)t));
                return true;
            }

            pose = samples[samples.Length - 1];
            return true;
        }
    }
}
