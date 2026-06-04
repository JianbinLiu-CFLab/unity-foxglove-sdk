// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Owns pending scan scheduling and pending-batch completion for VirtualLidar.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Sensors;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Decouples VirtualLidar pending scan scheduling and batch consumption
    /// from the MonoBehaviour to keep FixedUpdate and publish flow compact.
    /// </summary>
    internal sealed class VirtualLidarScanScheduler
    {
        private readonly LidarScanDiagnostics _scanDiagnostics = new LidarScanDiagnostics();
        private readonly UnityEngine.Object _logContext;
        private readonly List<int> _scanCrossings = new();

        // Async scan batches advance through this small state machine so FixedUpdate
        // schedules raycast/build work without waiting on it in the same tick.
        private enum PendingScanState
        {
            Idle,
            Scheduled,
            Consumed,
            Published
        }

        private PendingScanState _pendingScanState;
        private JobHandle _pendingScanHandle;
        private int _pendingBatchCount;
        private int[] _pendingScanCrossings = Array.Empty<int>();
        private int _pendingScanCrossingCount;
        private int _pendingProfileHash;
        private int _nextPendingScanId;
        private int _pendingScanId;

        public VirtualLidarScanScheduler(UnityEngine.Object logContext)
        {
            _logContext = logContext;
        }

        /// <summary>
        /// Schedule a pending raycast+build batch for the current fixed-tick column progress.
        /// </summary>
        public void SchedulePendingScan(
            int columnsToEmit,
            bool logPerformanceDiagnostics,
            float fixedDeltaTimeSeconds,
            int frameCounter,
            ref int scanColumnCursor,
            Vector3 worldPos,
            Quaternion worldRot,
            LayerMask layerMask,
            float maxRangeMeters,
            float syntheticIntensity,
            float syntheticReflectivity,
            ILidarScanPattern scanPattern,
            float4x4 activeScanWorldToLocal,
            VirtualLidarScanBuffers scanBuffers)
        {
            if (_pendingScanState == PendingScanState.Scheduled)
            {
                RecordLidarDiagnostics(
                    logPerformanceDiagnostics,
                    0,
                    0,
                    0d,
                    0d,
                    0d,
                    asyncOverrun: true,
                    fixedDeltaTimeSeconds);
                return;
            }

            // Rays are cast from the current tick pose. The build job keeps both the
            // active scan-reference coordinates for legacy visualization and the
            // acquisition-time coordinates needed by raw PointCloud2 Native streams.
            var queryParams = new QueryParameters(layerMask.value);
            var acquisitionWorldToLocal = Matrix4x4
                .TRS(worldPos, worldRot, Vector3.one)
                .inverse
                .ToFloat4x4();

            // Build one batch for all columns this tick (cap at one revolution).
            _scanCrossings.Clear();
            var batchCount = 0;
            var commands = scanBuffers.Commands;
            var results = scanBuffers.Results;
            var rayTimeOffsets = scanBuffers.RayTimeOffsets;
            var rayRings = scanBuffers.RayRings;
            for (var c = 0; c < columnsToEmit && batchCount < scanBuffers.EffectiveRayCount; c++)
            {
                var rays = scanBuffers.ColumnRays[scanColumnCursor];
                for (var r = 0; r < rays.Length && batchCount < scanBuffers.EffectiveRayCount; r++)
                {
                    var k = rays[r];
                    var index = k * scanBuffers.RayStride;
                    if (index >= scanBuffers.RawRayCount)
                        index = scanBuffers.RawRayCount - 1;

                    if (!scanPattern.TryGetRay(index, frameCounter, out var localDir, out var timeOffset))
                    {
                        commands[batchCount] = new RaycastCommand(worldPos, Vector3.forward, queryParams, 0f);
                        rayTimeOffsets[batchCount] = 0f;
                        rayRings[batchCount] = 0;
                    }
                    else
                    {
                        var worldDir = worldRot * new Vector3(localDir.X, localDir.Y, localDir.Z);
                        commands[batchCount] = new RaycastCommand(worldPos, worldDir, queryParams, maxRangeMeters);
                        rayTimeOffsets[batchCount] = LidarScanTiming.NormalizedOffsetToSeconds(timeOffset, scanPattern.ScanRateHz);
                        rayRings[batchCount] = scanBuffers.SpinEffectiveColumns > 0
                            ? (ushort)(index / scanBuffers.SpinEffectiveColumns)
                            : (ushort)0;
                    }

                    batchCount++;
                }

                scanColumnCursor++;
                if (scanColumnCursor >= scanBuffers.ScanColumnCount)
                {
                    _scanCrossings.Add(batchCount);
                    scanColumnCursor = 0;
                }
            }

            if (batchCount <= 0)
                return;

            var requiredCrossingCount = _scanCrossings.Count;
            if (_pendingScanCrossings.Length < requiredCrossingCount)
            {
                _pendingScanCrossings = new int[Math.Max(1, requiredCrossingCount)];
            }
            _pendingScanCrossingCount = Math.Min(requiredCrossingCount, _pendingScanCrossings.Length);
            for (var i = 0; i < _pendingScanCrossingCount; i++)
                _pendingScanCrossings[i] = _scanCrossings[i];

            _pendingBatchCount = batchCount;
            _pendingProfileHash = scanBuffers.ComputeProfileHash();
            _pendingScanId = ++_nextPendingScanId;
            var raycastHandle = RaycastCommand.ScheduleBatch(
                commands.GetSubArray(0, batchCount),
                results.GetSubArray(0, batchCount),
                64);

            var minRange = (float)scanPattern.MinRangeMeters;
            var buildJob = new VirtualLidarBuildPointsJob
            {
                Hits = results,
                RayTimeOffsets = rayTimeOffsets,
                RayRings = rayRings,
                WorldToLocal = activeScanWorldToLocal,
                AcquisitionWorldToLocal = acquisitionWorldToLocal,
                MinRange = minRange,
                MaxRange = maxRangeMeters,
                SyntheticIntensity = syntheticIntensity,
                SyntheticReflectivity = syntheticReflectivity,
                Points = scanBuffers.PointData
            };
            _pendingScanHandle = buildJob.Schedule(batchCount, 64, raycastHandle);
            _pendingScanState = PendingScanState.Scheduled;
        }

        /// <summary>
        /// Completes any scheduled pending batch, appends points, and emits scan boundaries.
        /// </summary>
        public void ConsumePendingScan(
            bool logPerformanceDiagnostics,
            float fixedDeltaTime,
            bool useNativeSnapshot,
            VirtualLidarScanBuffers scanBuffers,
            ref PointCloudFrame activeScanFrame,
            ref VirtualLidarPointData[] activeScanPointSnapshot,
            ref int activeScanPointSnapshotCount,
            ref int activeScanValidPoints,
            Action onScanBoundary)
        {
            if (_pendingScanState != PendingScanState.Scheduled || _pendingBatchCount <= 0)
                return;

            var completeStart = DiagnosticStart(logPerformanceDiagnostics);
            _pendingScanHandle.Complete();
            var completeMs = DiagnosticElapsedMs(completeStart);
            _pendingScanState = PendingScanState.Consumed;

            if (_pendingProfileHash != scanBuffers.ComputeProfileHash())
            {
                RecordLidarDiagnostics(logPerformanceDiagnostics, _pendingBatchCount, 0, completeMs, 0d, 0d, asyncOverrun: true, fixedDeltaTime);
                ClearPendingScan();
                return;
            }

            // BuildPointsJob is now chained behind RaycastCommand; any remaining wait is
            // included in completeMs, and there is no separate main-thread build phase here.
            var buildMs = 0d;

            var appendStart = DiagnosticStart(logPerformanceDiagnostics);
            var validPoints = 0;
            var ci = 0;
            var segmentStart = 0;
            for (var k = 0; k < _pendingBatchCount; k++)
            {
                while (ci < _pendingScanCrossingCount && k == _pendingScanCrossings[ci])
                {
                    AppendOrCopyPendingPointDataSegment(
                        scanBuffers,
                        segmentStart,
                        k - segmentStart,
                        useNativeSnapshot,
                        ref activeScanFrame,
                        ref activeScanPointSnapshot,
                        ref activeScanPointSnapshotCount,
                        ref activeScanValidPoints,
                        ref validPoints);
                    onScanBoundary?.Invoke();
                    _pendingScanState = PendingScanState.Published;
                    segmentStart = k;
                    ci++;
                }
            }

            AppendOrCopyPendingPointDataSegment(
                scanBuffers,
                segmentStart,
                _pendingBatchCount - segmentStart,
                useNativeSnapshot,
                ref activeScanFrame,
                ref activeScanPointSnapshot,
                ref activeScanPointSnapshotCount,
                ref activeScanValidPoints,
                ref validPoints);
            while (ci < _pendingScanCrossingCount && _pendingBatchCount == _pendingScanCrossings[ci])
            {
                onScanBoundary?.Invoke();
                _pendingScanState = PendingScanState.Published;
                ci++;
            }

            var appendMs = DiagnosticElapsedMs(appendStart);
            RecordLidarDiagnostics(logPerformanceDiagnostics, _pendingBatchCount, validPoints, completeMs, buildMs, appendMs, asyncOverrun: false, fixedDeltaTime);
            ClearPendingScan();
        }

        /// <summary>Shutdown/reset path: complete outstanding jobs before native buffers can be reused.</summary>
        public void DrainPendingScan()
        {
            if (_pendingScanState == PendingScanState.Scheduled)
                _pendingScanHandle.Complete();

            ClearPendingScan();
        }

        /// <summary>Clear only pending-batch state; active scan buffers may still hold partial revolution.</summary>
        public void ClearPendingScan()
        {
            _pendingScanHandle = default;
            _pendingScanState = PendingScanState.Idle;
            _pendingBatchCount = 0;
            _pendingScanCrossingCount = 0;
            _pendingProfileHash = 0;
            _pendingScanId = 0;
        }

        private void AppendOrCopyPendingPointDataSegment(
            VirtualLidarScanBuffers scanBuffers,
            int sourceStart,
            int length,
            bool useNativeSnapshot,
            ref PointCloudFrame activeScanFrame,
            ref VirtualLidarPointData[] activeScanPointSnapshot,
            ref int activeScanPointSnapshotCount,
            ref int activeScanValidPoints,
            ref int validPoints)
        {
            if (length <= 0)
                return;

            if (useNativeSnapshot)
            {
                CopyPendingPointDataSegment(
                    scanBuffers,
                    sourceStart,
                    length,
                    ref activeScanPointSnapshot,
                    ref activeScanPointSnapshotCount);
                return;
            }

            AppendPendingPointDataSegment(
                scanBuffers,
                sourceStart,
                length,
                ref activeScanFrame,
                ref activeScanValidPoints,
                ref validPoints);
        }

        private void CopyPendingPointDataSegment(
            VirtualLidarScanBuffers scanBuffers,
            int sourceStart,
            int length,
            ref VirtualLidarPointData[] activeScanPointSnapshot,
            ref int activeScanPointSnapshotCount)
        {
            if (length <= 0)
                return;

            if (activeScanPointSnapshot == null || activeScanPointSnapshot.Length < scanBuffers.EffectiveRayCount)
                activeScanPointSnapshot = new VirtualLidarPointData[scanBuffers.EffectiveRayCount];

            var writableLength = Math.Min(length, activeScanPointSnapshot.Length - activeScanPointSnapshotCount);
            if (writableLength <= 0)
                return;

            NativeArray<VirtualLidarPointData>.Copy(
                scanBuffers.PointData,
                sourceStart,
                activeScanPointSnapshot,
                activeScanPointSnapshotCount,
                writableLength);
            activeScanPointSnapshotCount += writableLength;
        }

        private void AppendPendingPointDataSegment(
            VirtualLidarScanBuffers scanBuffers,
            int sourceStart,
            int length,
            ref PointCloudFrame activeScanFrame,
            ref int activeScanValidPoints,
            ref int validPoints)
        {
            var end = Math.Min(_pendingBatchCount, sourceStart + length);
            for (var k = sourceStart; k < end; k++)
            {
                var point = scanBuffers.PointData[k];
                if (point.IsValid == 0)
                    continue;

                activeScanFrame.Points.Add(new PointCloudPoint(point.X, point.Y, point.Z)
                {
                    Intensity = point.Intensity,
                    Reflectivity = point.Reflectivity,
                    TimeOffsetSeconds = point.TimeOffsetSeconds,
                    Ring = point.Ring
                });
                activeScanValidPoints++;
                validPoints++;
            }
        }

        private static long DiagnosticStart(bool logPerformanceDiagnostics)
            => logPerformanceDiagnostics ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

        private static double DiagnosticElapsedMs(long startTicks)
            => startTicks == 0L ? 0d : (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency;

        private void RecordLidarDiagnostics(
            bool logPerformanceDiagnostics,
            int rayCount,
            int validPointCount,
            double completeMs,
            double buildMs,
            double appendMs,
            bool asyncOverrun,
            float fixedDeltaTimeSeconds)
        {
            if (!_scanDiagnostics.Record(
                    logPerformanceDiagnostics,
                    _pendingScanId,
                    rayCount,
                    validPointCount,
                    completeMs,
                    buildMs,
                    appendMs,
                    asyncOverrun,
                    fixedDeltaTimeSeconds,
                    out var snapshot))
                return;

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                _logContext,
                "[LidarDiag] scanId={0} scans={1} rays={2} valid={3} completeMs avg={4:F2} max={5:F2} buildMs avg={6:F2} appendMs avg={7:F2} overrun={8}",
                snapshot.ScanId,
                snapshot.Scans,
                snapshot.Rays,
                snapshot.ValidPoints,
                snapshot.CompleteMsAverage,
                snapshot.CompleteMsMax,
                snapshot.BuildMsAverage,
                snapshot.AppendMsAverage,
                snapshot.Overruns);
        }
    }
}
