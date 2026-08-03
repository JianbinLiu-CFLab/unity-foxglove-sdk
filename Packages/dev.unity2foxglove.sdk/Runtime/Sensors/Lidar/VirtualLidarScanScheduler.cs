// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Owns pending scan scheduling and pending-batch completion for VirtualLidar.

using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
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
        private int[] _scanCrossings = new int[4];
        private int _scanCrossingCount;

        private static readonly ProfilerMarker ScheduleScanMarker = new ProfilerMarker("VirtualLidar.ScheduleScan");
        private static readonly ProfilerMarker BuildPointsScheduleMarker = new ProfilerMarker("VirtualLidar.BuildPoints.Schedule");

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

        /// <summary>Create a scheduler that logs diagnostics against the supplied Unity context.</summary>
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
            bool computeAcquisitionFrame,
            VirtualLidarScanBuffers scanBuffers)
        {
            using (ScheduleScanMarker.Auto())
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
                        profileInvalidation: false,
                        fixedDeltaTimeSeconds);
                    return;
                }

                // Rays are cast from the current tick pose. The build job always keeps
                // active scan-reference coordinates and only computes acquisition-time
                // coordinates for raw PackedPointCloud Native or deskew consumers.
                var queryParams = new QueryParameters(layerMask.value);
                var acquisitionWorldToLocal = computeAcquisitionFrame
                    ? CoordinateConverterFloat3.RigidWorldToLocal(worldPos, worldRot)
                    : float4x4.identity;

                // Build one batch for all columns this tick (cap at one revolution).
                _scanCrossingCount = 0;
                var batchCount = 0;
                var commands = scanBuffers.Commands;
                var results = scanBuffers.Results;
                var rayTimeOffsets = scanBuffers.RayTimeOffsets;
                var rayRings = scanBuffers.RayRings;
                for (var c = 0; c < columnsToEmit && batchCount < scanBuffers.EffectiveRayCount; c++)
                {
                    var rays = scanBuffers.ColumnRays[scanColumnCursor];
                    if (batchCount > 0 && batchCount + rays.Length > scanBuffers.EffectiveRayCount)
                        break;

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
                        EnsureScanCrossingCapacity(_scanCrossingCount + 1);
                        _scanCrossings[_scanCrossingCount++] = batchCount;
                        scanColumnCursor = 0;
                    }
                }

                if (batchCount <= 0)
                    return;

                var requiredCrossingCount = _scanCrossingCount;
                if (_pendingScanCrossings.Length < requiredCrossingCount)
                {
                    // grow-only: retain the peak crossing buffer to avoid per-tick churn;
                    // this is bounded by the maximum revolution crossings in one scheduled batch.
                    _pendingScanCrossings = new int[Math.Max(1, requiredCrossingCount)];
                }
                System.Diagnostics.Debug.Assert(_pendingScanCrossings.Length >= requiredCrossingCount);
                _pendingScanCrossingCount = requiredCrossingCount;
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
                    ComputeAcquisitionFrame = computeAcquisitionFrame,
                    MinRange = minRange,
                    MaxRange = maxRangeMeters,
                    SyntheticIntensity = syntheticIntensity,
                    SyntheticReflectivity = syntheticReflectivity,
                    Points = scanBuffers.PointData
                };
                using (BuildPointsScheduleMarker.Auto())
                    _pendingScanHandle = buildJob.Schedule(batchCount, 64, raycastHandle);
                _pendingScanState = PendingScanState.Scheduled;
            }
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
            LidarScanBoundaryHandler onScanBoundary)
        {
            if (_pendingScanState != PendingScanState.Scheduled || _pendingBatchCount <= 0)
                return;

            var completeStart = _scanDiagnostics.Start(logPerformanceDiagnostics);
            _pendingScanHandle.Complete();
            var completeMs = _scanDiagnostics.ElapsedMs(completeStart);
            _pendingScanState = PendingScanState.Consumed;

            try
            {
                if (_pendingProfileHash != scanBuffers.ComputeProfileHash())
                {
                    LogLidarBatchTiming(
                        logPerformanceDiagnostics,
                        _pendingScanId,
                        _pendingBatchCount,
                        0,
                        completeMs,
                        0d,
                        0d,
                        0d,
                        0d,
                        0d,
                        0d,
                        0d,
                        0d,
                        useNativeSnapshot,
                        _pendingScanCrossingCount,
                        fixedDeltaTime);
                    RecordLidarDiagnostics(logPerformanceDiagnostics, _pendingBatchCount, 0, completeMs, 0d, 0d, asyncOverrun: false, profileInvalidation: true, fixedDeltaTime);
                    return;
                }

                // BuildPointsJob is now chained behind RaycastCommand; any remaining wait is
                // included in completeMs, and there is no separate main-thread build phase here.
                var buildMs = 0d;

                var appendStart = _scanDiagnostics.Start(logPerformanceDiagnostics);
                var copyMs = 0d;
                var boundaryPublishMs = 0d;
                var publishActiveScanMs = 0d;
                var motionRequestMs = 0d;
                var enqueueMs = 0d;
                var startNewScanMs = 0d;
                var validPoints = 0;
                var ci = 0;
                var segmentStart = 0;
                for (var k = 0; k < _pendingBatchCount; k++)
                {
                    while (ci < _pendingScanCrossingCount && k == _pendingScanCrossings[ci])
                    {
                        var copyStart = _scanDiagnostics.Start(logPerformanceDiagnostics);
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
                        copyMs += _scanDiagnostics.ElapsedMs(copyStart);
                        var boundaryPublishStart = _scanDiagnostics.Start(logPerformanceDiagnostics);
                        var boundaryTimings = new LidarScanBoundaryTimings(logPerformanceDiagnostics);
                        onScanBoundary?.Invoke(ref boundaryTimings);
                        boundaryPublishMs += _scanDiagnostics.ElapsedMs(boundaryPublishStart);
                        publishActiveScanMs += boundaryTimings.PublishActiveScanMs;
                        motionRequestMs += boundaryTimings.MotionRequestMs;
                        enqueueMs += boundaryTimings.EnqueueMs;
                        startNewScanMs += boundaryTimings.StartNewScanMs;
                        _pendingScanState = PendingScanState.Published;
                        segmentStart = k;
                        ci++;
                    }
                }

                var finalCopyStart = _scanDiagnostics.Start(logPerformanceDiagnostics);
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
                copyMs += _scanDiagnostics.ElapsedMs(finalCopyStart);
                while (ci < _pendingScanCrossingCount && _pendingBatchCount == _pendingScanCrossings[ci])
                {
                    var boundaryPublishStart = _scanDiagnostics.Start(logPerformanceDiagnostics);
                    var boundaryTimings = new LidarScanBoundaryTimings(logPerformanceDiagnostics);
                    onScanBoundary?.Invoke(ref boundaryTimings);
                    boundaryPublishMs += _scanDiagnostics.ElapsedMs(boundaryPublishStart);
                    publishActiveScanMs += boundaryTimings.PublishActiveScanMs;
                    motionRequestMs += boundaryTimings.MotionRequestMs;
                    enqueueMs += boundaryTimings.EnqueueMs;
                    startNewScanMs += boundaryTimings.StartNewScanMs;
                    _pendingScanState = PendingScanState.Published;
                    ci++;
                }

                var appendMs = _scanDiagnostics.ElapsedMs(appendStart);
                LogLidarBatchTiming(
                    logPerformanceDiagnostics,
                    _pendingScanId,
                    _pendingBatchCount,
                    validPoints,
                    completeMs,
                    buildMs,
                    appendMs,
                    copyMs,
                    boundaryPublishMs,
                    publishActiveScanMs,
                    motionRequestMs,
                    enqueueMs,
                    startNewScanMs,
                    useNativeSnapshot,
                    _pendingScanCrossingCount,
                    fixedDeltaTime);
                RecordLidarDiagnostics(logPerformanceDiagnostics, _pendingBatchCount, validPoints, completeMs, buildMs, appendMs, asyncOverrun: false, profileInvalidation: false, fixedDeltaTime);
            }
            finally
            {
                ClearPendingScan();
            }
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

        private void EnsureScanCrossingCapacity(int requiredCrossingCount)
        {
            if (_scanCrossings.Length >= requiredCrossingCount)
                return;

            var nextLength = Math.Max(4, _scanCrossings.Length);
            while (nextLength < requiredCrossingCount)
                nextLength *= 2;
            Array.Resize(ref _scanCrossings, nextLength);
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
            {
                var nextSnapshot = VirtualLidarPointSnapshotPool.Rent(scanBuffers.EffectiveRayCount);
                if (activeScanPointSnapshot != null && activeScanPointSnapshotCount > 0)
                    Array.Copy(activeScanPointSnapshot, nextSnapshot, Math.Min(activeScanPointSnapshotCount, nextSnapshot.Length));

                VirtualLidarPointSnapshotPool.Return(activeScanPointSnapshot);
                activeScanPointSnapshot = nextSnapshot;
            }

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

        private void LogLidarBatchTiming(
            bool logPerformanceDiagnostics,
            int scanId,
            int rayCount,
            int validPointCount,
            double completeMs,
            double buildMs,
            double appendMs,
            double copyMs,
            double boundaryPublishMs,
            double publishActiveScanMs,
            double motionRequestMs,
            double enqueueMs,
            double startNewScanMs,
            bool nativeSnapshot,
            int crossings,
            float fixedDeltaTimeSeconds)
        {
            if (!logPerformanceDiagnostics)
                return;

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                _logContext,
                "[LidarDiag] batch timing: scanId={0} rays={1} valid={2} completeMs={3:F2} buildMs={4:F2} appendMs={5:F2} copyMs={6:F2} boundaryPublishMs={7:F2} publishActiveScanMs={8:F2} motionRequestMs={9:F2} enqueueMs={10:F2} startNewScanMs={11:F2} fixedDeltaMs={12:F2} nativeSnapshot={13} crossings={14}",
                scanId,
                rayCount,
                validPointCount,
                completeMs,
                buildMs,
                appendMs,
                copyMs,
                boundaryPublishMs,
                publishActiveScanMs,
                motionRequestMs,
                enqueueMs,
                startNewScanMs,
                fixedDeltaTimeSeconds * 1000f,
                nativeSnapshot,
                crossings);
        }

        private void RecordLidarDiagnostics(
            bool logPerformanceDiagnostics,
            int rayCount,
            int validPointCount,
            double completeMs,
            double buildMs,
            double appendMs,
            bool asyncOverrun,
            bool profileInvalidation,
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
                    profileInvalidation,
                    fixedDeltaTimeSeconds,
                    out var snapshot))
                return;

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                _logContext,
                "[LidarDiag] scanId={0} scans={1} rays={2} valid={3} completeMs avg={4:F2} max={5:F2} buildMs avg={6:F2} appendMs avg={7:F2} timingOverrun={8} profileInvalidation={9}",
                snapshot.ScanId,
                snapshot.Scans,
                snapshot.Rays,
                snapshot.ValidPoints,
                snapshot.CompleteMsAverage,
                snapshot.CompleteMsMax,
                snapshot.BuildMsAverage,
                snapshot.AppendMsAverage,
                snapshot.TimingOverruns,
                snapshot.ProfileInvalidations);
        }
    }
}
