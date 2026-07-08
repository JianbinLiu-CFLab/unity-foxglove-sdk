// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Accumulates and logs VirtualLidar scan performance counters.
    /// </summary>
    internal sealed class LidarScanDiagnostics
    {
        private const int LogIntervalTicks = 60;

        private int _scans;
        private long _rays;
        private long _validPoints;
        private int _timingOverruns;
        private int _profileInvalidations;
        private double _completeMsTotal;
        private double _completeMsMax;
        private double _buildMsTotal;
        private double _appendMsTotal;

        /// <summary>Returns a stopwatch timestamp when diagnostics are enabled.</summary>
        public long Start(bool enabled)
            => enabled ? Stopwatch.GetTimestamp() : 0L;

        /// <summary>Returns elapsed milliseconds from a diagnostics timestamp.</summary>
        public double ElapsedMs(long startTicks)
            => startTicks == 0L
                ? 0d
                : (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;

        /// <summary>Records one scan batch and returns an interval snapshot when due.</summary>
        public bool Record(
            bool enabled,
            int scanId,
            int rayCount,
            int validPointCount,
            double completeMs,
            double buildMs,
            double appendMs,
            bool asyncOverrun,
            bool profileInvalidation,
            double fixedDeltaTimeSeconds,
            out LidarScanDiagnosticSnapshot snapshot)
        {
            snapshot = default;
            if (!enabled)
                return false;

            _scans++;
            _rays += Math.Max(0, rayCount);
            _validPoints += Math.Max(0, validPointCount);
            _completeMsTotal += completeMs;
            _completeMsMax = Math.Max(_completeMsMax, completeMs);
            _buildMsTotal += buildMs;
            _appendMsTotal += appendMs;
            if (asyncOverrun || completeMs > fixedDeltaTimeSeconds * 1000d)
                _timingOverruns++;
            if (profileInvalidation)
                _profileInvalidations++;

            if (_scans < LogIntervalTicks)
                return false;

            var divisor = Math.Max(1, _scans);
            snapshot = new LidarScanDiagnosticSnapshot(
                scanId,
                _scans,
                _rays,
                _validPoints,
                _completeMsTotal / divisor,
                _completeMsMax,
                _buildMsTotal / divisor,
                _appendMsTotal / divisor,
                _timingOverruns,
                _profileInvalidations);

            Reset();
            return true;
        }

        /// <summary>Resets all accumulated interval counters.</summary>
        public void Reset()
        {
            _scans = 0;
            _rays = 0;
            _validPoints = 0;
            _timingOverruns = 0;
            _profileInvalidations = 0;
            _completeMsTotal = 0d;
            _completeMsMax = 0d;
            _buildMsTotal = 0d;
            _appendMsTotal = 0d;
        }
    }

    /// <summary>One interval of LiDAR scan diagnostic counters.</summary>
    internal readonly struct LidarScanDiagnosticSnapshot
    {
        public LidarScanDiagnosticSnapshot(
            int scanId,
            int scans,
            long rays,
            long validPoints,
            double completeMsAverage,
            double completeMsMax,
            double buildMsAverage,
            double appendMsAverage,
            int timingOverruns,
            int profileInvalidations)
        {
            ScanId = scanId;
            Scans = scans;
            Rays = rays;
            ValidPoints = validPoints;
            CompleteMsAverage = completeMsAverage;
            CompleteMsMax = completeMsMax;
            BuildMsAverage = buildMsAverage;
            AppendMsAverage = appendMsAverage;
            TimingOverruns = timingOverruns;
            ProfileInvalidations = profileInvalidations;
        }

        public int ScanId { get; }
        public int Scans { get; }
        public long Rays { get; }
        public long ValidPoints { get; }
        public double CompleteMsAverage { get; }
        public double CompleteMsMax { get; }
        public double BuildMsAverage { get; }
        public double AppendMsAverage { get; }
        public int TimingOverruns { get; }
        public int ProfileInvalidations { get; }
    }
}
