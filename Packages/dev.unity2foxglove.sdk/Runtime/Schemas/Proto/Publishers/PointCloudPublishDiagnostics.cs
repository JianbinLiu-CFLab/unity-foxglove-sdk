// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Owns point-cloud publish diagnostic counters and log aggregation.

using System;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Aggregates point-cloud publish diagnostics so publisher code can stay
    /// focused on queueing and payload routing.
    /// </summary>
    internal sealed class PointCloudPublishDiagnostics
    {
        private const int DiagnosticsIntervalFrames = 60;

        private int _frames;
        private long _preparedPoints;
        private int _drops;
        private double _cloneMsTotal;
        private double _cloneMsMax;
        private double _encodeMsTotal;
        private double _encodeMsMax;
        private int _encodeResults;

        public void RecordPrepared(bool enabled, PointCloudFrame frame)
        {
            if (frame == null)
                return;

            RecordPrepared(enabled, frame.GetPointCount());
        }

        public void RecordPrepared(bool enabled, int pointCount)
        {
            if (!enabled)
                return;

            _frames++;
            _preparedPoints += Math.Max(0, pointCount);
        }

        public void RecordDrop(bool enabled, int count = 1)
        {
            if (!enabled)
                return;

            _drops += Math.Max(1, count);
        }

        public void RecordEncodeResult(bool enabled, DracoEncodeResult result)
        {
            if (!enabled || result == null)
                return;

            _cloneMsTotal += result.Request.CloneMs;
            _cloneMsMax = Math.Max(_cloneMsMax, result.Request.CloneMs);
            _encodeMsTotal += result.EncodeMs;
            _encodeMsMax = Math.Max(_encodeMsMax, result.EncodeMs);
            _encodeResults++;
        }

        public void RecordPointCloud2NativeResult(bool enabled, PointCloud2NativeResult result)
        {
            if (!enabled || result == null)
                return;

            _encodeMsTotal += result.EncodeMs;
            _encodeMsMax = Math.Max(_encodeMsMax, result.EncodeMs);
            _encodeResults++;
        }

        public void LogIfReady(bool enabled, Action<string, object[]> log)
        {
            if (!enabled || _frames < DiagnosticsIntervalFrames)
                return;
            if (log == null)
                throw new ArgumentNullException(nameof(log));

            var frameDivisor = Math.Max(1, _frames);
            var encodeDivisor = Math.Max(1, _encodeResults);
            log(
                "[PointCloudDiag] prepared={0} points={1} avgPoints={2:F0} cloneMs avg={3:F2} max={4:F2} encodeMs avg={5:F2} max={6:F2} drop={7}",
                new object[]
                {
                    _frames,
                    _preparedPoints,
                    (double)_preparedPoints / frameDivisor,
                    _cloneMsTotal / encodeDivisor,
                    _cloneMsMax,
                    _encodeMsTotal / encodeDivisor,
                    _encodeMsMax,
                    _drops
                });

            Reset();
        }

        public void Reset()
        {
            _frames = 0;
            _preparedPoints = 0;
            _drops = 0;
            _cloneMsTotal = 0d;
            _cloneMsMax = 0d;
            _encodeMsTotal = 0d;
            _encodeMsMax = 0d;
            _encodeResults = 0;
        }
    }
}
