// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: PointCloud2 Native queueing, publishing, and adapter notification path.

using System;
using System.Threading;
using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
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
            bool emitAbsoluteTimeNs)
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
            var publishBridge = ShouldPrepareRos2BridgePayload();
            var publishNativeFrame = ShouldPreparePointCloud2NativeFrame();
            var motionSettings = ResolveMotionCompensationSettings();
            var publishRaw = motionSettings.PreserveRawOutput;
            var motionCompensation = TryCreateMotionCompensationRequest(
                points,
                pointCount,
                unixNs,
                motionSettings,
                publishNativeFrame);

            if (!publishRaw && motionCompensation == null)
            {
                VirtualLidarPointSnapshotPool.Return(points);
                return true;
            }

            if (publishRaw && !publishWebSocket && !publishBridge && !publishNativeFrame && motionCompensation == null)
            {
                VirtualLidarPointSnapshotPool.Return(points);
                return true;
            }

            QueueVirtualLidarPointCloud2Native(
                points,
                pointCount,
                unixNs,
                frameId,
                emitAbsoluteTimeNs,
                publishRaw && publishWebSocket,
                publishRaw && publishBridge,
                publishRaw && publishNativeFrame,
                EffectiveEncoding,
                publishRaw ? PointCloud2NativeTopic : null,
                motionCompensation);
            return true;
        }

        private void PublishPointCloud2NativeFrame(
            PointCloudFrame frame,
            ulong unixNs,
            PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            if (!TryGetPreparedPublishDemand(out var publishWebSocket, out var publishBridge))
            {
                publishWebSocket = ShouldPreparePublishPayload();
                publishBridge = ShouldPrepareRos2BridgePayload();
            }

            byte[] ros2Payload = null;
            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = packedLayout == null
                    ? Ros2CdrSensorPointCloud2Builder.Serialize(frame)
                    : Ros2CdrSensorPointCloud2Builder.Serialize(frame, packedLayout);
                PublishRos2(ros2Payload, unixNs);
            }

            if (publishBridge)
            {
                ros2Payload ??= packedLayout == null
                    ? Ros2CdrSensorPointCloud2Builder.Serialize(frame)
                    : Ros2CdrSensorPointCloud2Builder.Serialize(frame, packedLayout);
                PublishRos2Bridge(ros2Payload, unixNs);
            }

            if (ShouldPreparePointCloud2NativeFrame())
                PublishPreparedPointCloud2NativeFrame(frame, unixNs, packedLayout);
        }

        private void PublishPreparedPointCloud2NativeFrame(
            PointCloudFrame frame,
            ulong unixNs,
            PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            var handler = PointCloud2NativeFrameReady;
            if (handler == null || frame == null)
                return;

            try
            {
                var packed = packedLayout == null
                    ? PointCloudPackedDataBuilder.Build(frame)
                    : PointCloudPackedDataBuilder.Build(frame, packedLayout);
                var nativeFrame = new PointCloud2NativeFrame(
                    unixNs,
                    string.IsNullOrEmpty(frame.FrameId) ? _frameId : frame.FrameId,
                    height: 1U,
                    width: checked((uint)frame.GetPointCount()),
                    fields: packed.Fields,
                    pointStep: packed.PointStride,
                    data: packed.Data,
                    isDense: true,
                    topic: PointCloud2NativeTopic);
                PublishPointCloud2NativeFrameReady(nativeFrame, "preparedNativeFrameReady");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Foxglove] PointCloud2 native frame subscriber failed: " + ex.Message);
            }
        }

        private void QueueVirtualLidarPointCloud2Native(
            VirtualLidarPointData[] points,
            int pointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs,
            bool publishWebSocket,
            bool publishBridge,
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
                publishBridge,
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
            if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
                PublishRos2(result.WebSocketPayload, result.Request.UnixNs);

            if (result.Request.PublishBridge)
                PublishRos2Bridge(result.BridgePayload, result.Request.UnixNs);

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
                Debug.LogWarning("[Foxglove] PointCloud2 native frame subscriber failed: " + ex.Message);
            }
            finally
            {
                LogPointCloud2NativeTiming(timingStart, stage, frame);
            }
        }
    }
}
