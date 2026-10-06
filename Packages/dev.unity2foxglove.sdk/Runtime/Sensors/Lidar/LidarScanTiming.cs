// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    internal readonly struct LidarRuntimeConfiguration
    {
        public LidarRuntimeConfiguration(
            string frameId,
            float maxRangeMeters,
            int layerMaskValue,
            bool publishEmptyFrames,
            bool logPerformanceDiagnostics,
            int maxRaycastCommandsPerFixedUpdate,
            float syntheticReflectivity,
            float syntheticIntensity)
        {
            FrameId = frameId;
            MaxRangeMeters = maxRangeMeters;
            LayerMaskValue = layerMaskValue;
            PublishEmptyFrames = publishEmptyFrames;
            LogPerformanceDiagnostics = logPerformanceDiagnostics;
            MaxRaycastCommandsPerFixedUpdate = maxRaycastCommandsPerFixedUpdate;
            SyntheticReflectivity = syntheticReflectivity;
            SyntheticIntensity = syntheticIntensity;
        }

        public string FrameId { get; }
        public float MaxRangeMeters { get; }
        public int LayerMaskValue { get; }
        public bool PublishEmptyFrames { get; }
        public bool LogPerformanceDiagnostics { get; }
        public int MaxRaycastCommandsPerFixedUpdate { get; }
        public float SyntheticReflectivity { get; }
        public float SyntheticIntensity { get; }
    }

    internal static class LidarPendingScanCompletionPolicy
    {
        internal static bool IsReady(bool scheduled, bool jobCompleted, int batchCount)
            => scheduled && jobCompleted && batchCount > 0;
    }

    /// <summary>
    /// Shared scan timing helpers for LiDAR patterns and point-cloud payloads.
    /// </summary>
    public static class LidarScanTiming
    {
        /// <summary>
        /// Convert a normalized offset inside one scan period into seconds.
        /// </summary>
        public static float NormalizedOffsetToSeconds(float normalizedOffset, double scanRateHz)
        {
            if (float.IsNaN(normalizedOffset) || float.IsInfinity(normalizedOffset) || normalizedOffset <= 0f)
                return 0f;
            if (double.IsNaN(scanRateHz) || double.IsInfinity(scanRateHz) || scanRateHz <= 0d)
                return 0f;

            return (float)(Math.Min(normalizedOffset, 1f) / scanRateHz);
        }

        /// <summary>
        /// Returns the point acquisition offset when a scan spans multiple physics ticks.
        /// The elapsed physics time is authoritative; normalized phase only refines the
        /// position inside the current tick and never compresses a budget-stretched scan.
        /// </summary>
        public static float AcquisitionOffsetSeconds(
            double acquisitionPhysSeconds,
            double scanStartPhysSeconds,
            float normalizedOffset,
            float fixedDeltaTimeSeconds)
        {
            var elapsed = acquisitionPhysSeconds - scanStartPhysSeconds;
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0d)
                elapsed = 0d;
            var tick = float.IsNaN(fixedDeltaTimeSeconds) || float.IsInfinity(fixedDeltaTimeSeconds)
                ? 0f
                : Math.Max(0f, fixedDeltaTimeSeconds);
            var phase = float.IsNaN(normalizedOffset) || float.IsInfinity(normalizedOffset)
                ? 0f
                : Math.Clamp(normalizedOffset, 0f, 1f);
            return (float)elapsed + phase * tick;
        }

        /// <summary>
        /// Reserve whole scan columns only when no asynchronous batch is in flight.
        /// </summary>
        internal static bool TryReserveColumns(
            bool hasPendingScan,
            double progress,
            int budgetColumns,
            int remainingColumns,
            out int columnsToEmit,
            out double remainingProgress)
        {
            columnsToEmit = 0;
            remainingProgress = progress;
            if (hasPendingScan
                || double.IsNaN(progress)
                || double.IsInfinity(progress)
                || progress <= 0d
                || budgetColumns <= 0
                || remainingColumns <= 0)
                return false;

            var wholeColumns = progress >= int.MaxValue
                ? int.MaxValue
                : (int)Math.Floor(progress);
            columnsToEmit = Math.Min(wholeColumns, Math.Min(budgetColumns, remainingColumns));
            if (columnsToEmit <= 0)
            {
                columnsToEmit = 0;
                return false;
            }

            remainingProgress = progress - columnsToEmit;
            return true;
        }

        /// <summary>
        /// Advance a scan boundary from the sensor timeline rather than completion time.
        /// </summary>
        internal static double NextScanStartPhysSeconds(double scanStartPhysSeconds, double scanPeriodSeconds)
        {
            if (double.IsNaN(scanStartPhysSeconds)
                || double.IsInfinity(scanStartPhysSeconds)
                || double.IsNaN(scanPeriodSeconds)
                || double.IsInfinity(scanPeriodSeconds)
                || scanPeriodSeconds <= 0d)
                return scanStartPhysSeconds;

            var next = scanStartPhysSeconds + scanPeriodSeconds;
            return double.IsNaN(next) || double.IsInfinity(next) ? scanStartPhysSeconds : next;
        }
    }
}
