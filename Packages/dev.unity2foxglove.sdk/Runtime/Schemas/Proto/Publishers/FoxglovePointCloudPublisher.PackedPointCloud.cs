// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: PackedPointCloud Native queueing, publishing, and adapter notification path.

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

        internal bool TryQueueVirtualLidarPackedPointCloudFrame(
            VirtualLidarPointData[] points,
            int pointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs,
            ref LidarScanBoundaryTimings boundaryTimings)
        {
            if (!CanQueueVirtualLidarPackedPointCloudFrame)
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
            var publishNativeFrame = ShouldPreparePackedPointCloudFrame();
            var motionRequestStart = boundaryTimings.Start();
            var motionSettings = ResolveMotionCompensationSettings();
            var publishRaw = motionSettings.PreserveRawOutput;
            var queueDeskewedOutput = motionSettings.EmitDeskewedOutput
                                      && publishNativeFrame
                                      && ShouldQueueDeskewedPackedPointCloudFrame(unixNs);
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
            QueueVirtualLidarPackedPointCloud(
                points,
                pointCount,
                unixNs,
                frameId,
                emitAbsoluteTimeNs,
                publishRaw && publishWebSocket,
                publishRaw && publishProvider,
                publishRaw && publishNativeFrame,
                EffectiveEncoding,
                publishRaw ? PackedPointCloudTopic : null,
                motionCompensation);
            boundaryTimings.EnqueueMs += boundaryTimings.ElapsedMs(enqueueStart);
            return true;
        }

        private void PublishPackedPointCloudFrame(
            PointCloudFrame frame,
            ulong unixNs,
            PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            if (!TryGetPreparedPublishDemand(out var publishWebSocket, out var publishProvider))
            {
                publishWebSocket = ShouldPreparePublishPayload();
                publishProvider = ShouldPrepareOrdinaryTransportPayload();
            }

            var publishNativeFrame = ShouldPreparePackedPointCloudFrame();
            if (!publishProvider && !publishNativeFrame)
                return;

            var nativeFrame = BuildPreparedPackedPointCloudFrame(
                frame,
                unixNs,
                packedLayout);
            if (nativeFrame == null)
                return;

            if (publishProvider)
            {
                PublishOrdinaryTransport(
                    nativeFrame,
                    typeof(PackedPointCloudFrame).FullName,
                    unixNs);
            }

            if (publishNativeFrame)
                PublishPackedPointCloudFrameReady(nativeFrame, "preparedNativeFrameReady");
        }

        private PackedPointCloudFrame BuildPreparedPackedPointCloudFrame(
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
                return new PackedPointCloudFrame(
                    unixNs,
                    string.IsNullOrEmpty(frame.FrameId) ? _frameId : frame.FrameId,
                    height: 1U,
                    width: checked((uint)frame.GetPointCount()),
                    fields: packed.Fields,
                    pointStep: packed.PointStride,
                    data: packed.Data,
                    isDense: true,
                    topic: PackedPointCloudTopic);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return null;
            }
        }

        private void QueueVirtualLidarPackedPointCloud(
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
            var request = new PackedPointCloudRequest(
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
            EnqueuePackedPointCloudRequest(request);
        }

        private void EnqueuePackedPointCloudRequest(PackedPointCloudRequest request)
        {
            EnsureEncodePipelines();
            _packedPointCloudPipeline.Queue(
                request,
                _logQosDrops,
                () => _diagnostics.RecordDrop(_logPerformanceDiagnostics));
        }

        private void PublishCompletedPackedPointCloudPayload(PackedPointCloudResult result)
        {
            _diagnostics.RecordPackedPointCloudResult(_logPerformanceDiagnostics, result);
            LogPackedPointCloudWorkerTiming(result);
            if (result.Request.PublishProvider && result.NativeFrame != null)
            {
                PublishOrdinaryTransport(
                    result.NativeFrame,
                    typeof(PackedPointCloudFrame).FullName,
                    result.Request.UnixNs);
            }

            if (result.Request.PublishNativeFrame && result.NativeFrame != null)
                PublishPackedPointCloudFrameReady(result.NativeFrame, "rawNativeFrameReady");

            if (result.MotionCompensatedNativeFrame != null)
                PublishPackedPointCloudFrameReady(result.MotionCompensatedNativeFrame, "deskewedNativeFrameReady");
            else if (result.Request.HasMotionCompensation && !string.IsNullOrWhiteSpace(result.Error))
                WarnMotionCompensation("skipped: " + result.Error);
        }

        private void PublishPackedPointCloudFrameReady(PackedPointCloudFrame frame)
            => PublishPackedPointCloudFrameReady(frame, "nativeFrameReady");

        private void PublishPackedPointCloudFrameReady(PackedPointCloudFrame frame, string stage)
        {
            if (frame == null)
                return;

            var handler = PackedPointCloudFrameReady;
            if (handler == null)
                return;

            var timingStart = BeginPackedPointCloudTiming();
            try
            {
                PointCloudFrameEventDispatcher.Invoke(handler, frame, Debug.LogException);
            }
            finally
            {
                LogPackedPointCloudTiming(timingStart, stage, frame);
            }
        }
    }
}
