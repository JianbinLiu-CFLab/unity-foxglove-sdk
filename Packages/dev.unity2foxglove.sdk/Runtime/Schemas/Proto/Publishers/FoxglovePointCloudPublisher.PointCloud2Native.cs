// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: PointCloud2 Native queueing, publishing, and adapter notification path.

using System;
using System.Threading;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxglovePointCloudPublisher
    {

        internal bool TryQueueVirtualLidarPointCloud2NativeFrame(
            VirtualLidarPointData[] points,
            int pointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs,
            ref LidarScanBoundaryTimings boundaryTimings)
        {
            if (!CanQueueVirtualLidarPointCloud2NativeFrame)
                return false;

            if (points != null && pointCount > 0)
                MarkSourceDrivenPointCloud();

            ResolveManager();
            if (_manager == null || _manager.Runtime?.ReplayEnabled == true)
            {
                VirtualLidarPointSnapshotPool.Return(points);
                return true;
            }

            var publishWebSocket = ShouldPreparePublishPayload();
            var publishProvider = ShouldPrepareOrdinaryTransportPayload();
            var publishNativeFrame = ShouldPreparePointCloud2NativeFrame();
            var motionRequestStart = boundaryTimings.Start();
            var motionSettings = ResolveMotionCompensationSettings();
            var publishRaw = motionSettings.PreserveRawOutput;
            var queueDeskewedOutput = motionSettings.EmitDeskewedOutput
                                      && publishNativeFrame
                                      && ShouldQueueDeskewedPointCloud2Frame(unixNs);
            var motionCompensation = queueDeskewedOutput
                ? TryCreateMotionCompensationRequest(
                    motionSettings,
                    publishNativeFrame)
                : null;
            boundaryTimings.MotionRequestMs += boundaryTimings.ElapsedMs(motionRequestStart);

            if (!publishRaw && motionCompensation == null)
            {
                VirtualLidarPointSnapshotPool.Return(points);
                return true;
            }

            if (publishRaw && !publishWebSocket && !publishProvider && !publishNativeFrame && motionCompensation == null)
            {
                VirtualLidarPointSnapshotPool.Return(points);
                return true;
            }

            var enqueueStart = boundaryTimings.Start();
            QueueVirtualLidarPointCloud2Native(
                points,
                pointCount,
                unixNs,
                frameId,
                emitAbsoluteTimeNs,
                publishRaw && publishWebSocket,
                publishRaw && publishProvider,
                publishRaw && publishNativeFrame,
                EffectiveEncoding,
                publishRaw ? PointCloud2NativeTopic : null,
                motionCompensation);
            boundaryTimings.EnqueueMs += boundaryTimings.ElapsedMs(enqueueStart);
            return true;
        }

        private void PublishPointCloud2NativeFrame(
            PointCloudFrame frame,
            ulong unixNs,
            PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            if (!TryGetPreparedPublishDemand(out var publishWebSocket, out var publishProvider))
            {
                publishWebSocket = ShouldPreparePublishPayload();
                publishProvider = ShouldPrepareOrdinaryTransportPayload();
            }

            var publishNativeFrame = ShouldPreparePointCloud2NativeFrame();
            if (!publishProvider && !publishNativeFrame)
                return;

            var nativeFrame = BuildPreparedPointCloud2NativeFrame(
                frame,
                unixNs,
                packedLayout);
            if (nativeFrame == null)
                return;

            if (publishProvider)
            {
                PublishOrdinaryTransport(
                    nativeFrame,
                    typeof(PointCloud2NativeFrame).FullName,
                    unixNs);
            }

            if (publishNativeFrame)
                PublishPointCloud2NativeFrameReady(nativeFrame, "preparedNativeFrameReady");
        }

        private PointCloud2NativeFrame BuildPreparedPointCloud2NativeFrame(
            PointCloudFrame frame,
            ulong unixNs,
            PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            if (frame == null)
                return null;

            try
            {
                var packed = packedLayout == null
                    ? PointCloudPackedDataBuilder.Build(frame)
                    : PointCloudPackedDataBuilder.Build(frame, packedLayout);
                return new PointCloud2NativeFrame(
                    unixNs,
                    string.IsNullOrEmpty(frame.FrameId) ? _frameId : frame.FrameId,
                    height: 1U,
                    width: checked((uint)frame.GetPointCount()),
                    fields: packed.Fields,
                    pointStep: packed.PointStride,
                    data: packed.Data,
                    isDense: true,
                    topic: PointCloud2NativeTopic);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return null;
            }
        }

        private void QueueVirtualLidarPointCloud2Native(
            VirtualLidarPointData[] points,
            int pointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs,
            bool publishWebSocket,
            bool publishProvider,
            bool publishNativeFrame,
            PublisherEffectiveEncoding webSocketEncoding,
            string nativeTopic = null,
            PointCloudMotionCompensationRequest motionCompensation = null)
        {
            if (points == null || pointCount <= 0)
                return;

            _diagnostics.RecordPrepared(_logPerformanceDiagnostics, pointCount);
            var request = new PointCloud2NativeRequest(
                points,
                pointCount,
                unixNs,
                string.IsNullOrEmpty(frameId) ? _frameId : frameId,
                emitAbsoluteTimeNs,
                publishWebSocket,
                publishProvider,
                publishNativeFrame,
                webSocketEncoding,
                _logPerformanceDiagnostics,
                nativeTopic,
                motionCompensation);
            EnqueuePointCloud2NativeRequest(request);
        }

        private void EnqueuePointCloud2NativeRequest(PointCloud2NativeRequest request)
        {
            EnsureEncodePipelines();
            _pointCloud2NativePipeline.Queue(
                request,
                _logQosDrops,
                () => _diagnostics.RecordDrop(_logPerformanceDiagnostics));
        }

        private void PublishCompletedPointCloud2NativePayload(PointCloud2NativeResult result)
        {
            _diagnostics.RecordPointCloud2NativeResult(_logPerformanceDiagnostics, result);
            LogPointCloud2NativeWorkerTiming(result);
            if (result.Request.PublishProvider && result.NativeFrame != null)
            {
                PublishOrdinaryTransport(
                    result.NativeFrame,
                    typeof(PointCloud2NativeFrame).FullName,
                    result.Request.UnixNs);
            }

            if (result.Request.PublishNativeFrame && result.NativeFrame != null)
                PublishPointCloud2NativeFrameReady(result.NativeFrame, "rawNativeFrameReady");

            if (result.MotionCompensatedNativeFrame != null)
                PublishPointCloud2NativeFrameReady(result.MotionCompensatedNativeFrame, "deskewedNativeFrameReady");
            else if (result.Request.HasMotionCompensation && !string.IsNullOrWhiteSpace(result.Error))
                WarnMotionCompensation("skipped: " + result.Error);
        }

        private void PublishPointCloud2NativeFrameReady(PointCloud2NativeFrame frame)
            => PublishPointCloud2NativeFrameReady(frame, "nativeFrameReady");

        private void PublishPointCloud2NativeFrameReady(PointCloud2NativeFrame frame, string stage)
        {
            if (frame == null)
                return;

            var handler = PointCloud2NativeFrameReady;
            if (handler == null)
                return;

            var timingStart = BeginPointCloud2NativeTiming();
            try
            {
                handler(frame);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                LogPointCloud2NativeTiming(timingStart, stage, frame);
            }
        }
    }
}
