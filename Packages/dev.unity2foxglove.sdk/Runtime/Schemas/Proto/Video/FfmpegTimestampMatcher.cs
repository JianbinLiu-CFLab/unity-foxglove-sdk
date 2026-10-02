// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Match FFmpeg MPEG-TS presentation timestamps to capture timestamps.

using System;
using System.Collections.Concurrent;

namespace Foxglove.Schemas.Video
{
    internal sealed class FfmpegTimestampMatcher
    {
        private const long PtsModulus = 1L << 33;
        private const long HalfPtsModulus = 1L << 32;
        private readonly ConcurrentDictionary<long, ulong> _captureTimestamps = new ConcurrentDictionary<long, ulong>();
        private long _nextFrameIndex;
        private long _lastUnwrappedPts90k = -1;
        private long _frameRate;

        public int PendingCount => _captureTimestamps.Count;

        public void Reset(int frameRate)
        {
            _captureTimestamps.Clear();
            _nextFrameIndex = 0;
            _lastUnwrappedPts90k = -1;
            _frameRate = Math.Max(1, frameRate);
        }

        public void Track(ulong timestampNs)
        {
            var index = _nextFrameIndex++;
            _captureTimestamps[index] = timestampNs;
        }

        public bool TryResolve(long pts90k, out ulong timestampNs)
        {
            timestampNs = 0;
            var unwrappedPts = UnwrapPts(pts90k);
            var frameIndex = (long)Math.Round(unwrappedPts * (double)_frameRate / 90000.0, MidpointRounding.AwayFromZero);
            if (frameIndex >= 0 && _captureTimestamps.TryRemove(frameIndex, out timestampNs))
            {
                RemoveOlder(frameIndex);
                return true;
            }

            if (TryResolveModulo(pts90k, out var moduloIndex, out timestampNs))
            {
                RemoveOlder(moduloIndex);
                return true;
            }

            return false;
        }

        public void Clear() => _captureTimestamps.Clear();

        private long UnwrapPts(long pts90k)
        {
            var normalized = pts90k & (PtsModulus - 1);
            if (_lastUnwrappedPts90k < 0)
            {
                _lastUnwrappedPts90k = normalized;
                return normalized;
            }

            var previousModulo = _lastUnwrappedPts90k & (PtsModulus - 1);
            var delta = normalized - previousModulo;
            if (delta > HalfPtsModulus)
                delta -= PtsModulus;
            else if (delta < -HalfPtsModulus)
                delta += PtsModulus;

            _lastUnwrappedPts90k += delta;
            return _lastUnwrappedPts90k;
        }

        private bool TryResolveModulo(long pts90k, out long frameIndex, out ulong timestampNs)
        {
            frameIndex = 0;
            timestampNs = 0;
            var normalized = pts90k & (PtsModulus - 1);
            var ticksPerFrame = 90000.0 / _frameRate;
            var bestDistance = double.MaxValue;
            var found = false;
            foreach (var pair in _captureTimestamps)
            {
                var expected = (pair.Key * ticksPerFrame) % PtsModulus;
                var distance = Math.Abs(normalized - expected);
                distance = Math.Min(distance, PtsModulus - distance);
                if (distance < bestDistance && distance <= ticksPerFrame / 2.0)
                {
                    bestDistance = distance;
                    frameIndex = pair.Key;
                    timestampNs = pair.Value;
                    found = true;
                }
            }

            return found && _captureTimestamps.TryRemove(frameIndex, out timestampNs);
        }

        private void RemoveOlder(long frameIndex)
        {
            foreach (var pair in _captureTimestamps)
            {
                if (pair.Key < frameIndex)
                    _captureTimestamps.TryRemove(pair.Key, out _);
            }
        }
    }
}
