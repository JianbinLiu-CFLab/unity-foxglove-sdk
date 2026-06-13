// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Draco point-cloud queueing, encoding, and completion publishing path.

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

        internal bool TryQueueVirtualLidarDracoFrame(
            VirtualLidarPointData[] points,
            int pointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs)
        {
            if (!CanQueueVirtualLidarDracoFrame)
                return false;

            if (points != null && pointCount > 0)
                MarkSourceDrivenPointCloud();

            ResolveManager();
            if (_manager == null || _manager.Runtime?.ReplayEnabled == true)
                return true;

            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge)
                return true;

            if (!ShouldQueueVirtualLidarDracoFrame(unixNs))
                return true;

            QueueVirtualLidarDracoEncode(
                points,
                pointCount,
                unixNs,
                frameId,
                emitAbsoluteTimeNs,
                publishWebSocket,
                publishBridge,
                EffectiveEncoding);
            return true;
        }

        private bool ShouldQueueVirtualLidarDracoFrame(ulong unixNs)
        {
            var rateHz = _nativeDracoMaxPublishRateHz;
            if (rateHz <= 0f)
                return true;

            var intervalNs = ResolveNativeDracoPublishIntervalNs(rateHz);
            var timestampNs = unixNs == 0UL ? FoxgloveTimeUtil.NowUnixTimeNs() : unixNs;

            if (_lastNativeDracoPublishUnixNs != 0UL
                && timestampNs >= _lastNativeDracoPublishUnixNs
                && timestampNs - _lastNativeDracoPublishUnixNs < intervalNs)
            {
                _diagnostics.RecordDrop(_logPerformanceDiagnostics);
                return false;
            }

            // A backward clock jump, usually from replay seek or sensor clock reset,
            // intentionally resets the native Draco rate baseline and lets one frame through.
            _lastNativeDracoPublishUnixNs = timestampNs;
            return true;
        }

        private ulong ResolveNativeDracoPublishIntervalNs(float rateHz)
        {
            if (!rateHz.Equals(_cachedNativeDracoMaxPublishRateHz))
            {
                _cachedNativeDracoMaxPublishRateHz = rateHz;
                _cachedNativeDracoPublishIntervalNs = (ulong)Math.Max(1d, Math.Round(1_000_000_000d / rateHz));
            }

            return _cachedNativeDracoPublishIntervalNs;
        }

        private void PublishDracoFrame(PointCloudFrame frame, ulong unixNs)
        {
            if (frame == null || frame.GetPointCount() == 0)
                return;

            QueueDracoEncode(frame, unixNs);
        }

        private void QueueDracoEncode(PointCloudFrame frame, ulong unixNs)
        {
            if (!TryGetPreparedPublishDemand(out var publishWebSocket, out var publishBridge))
            {
                publishWebSocket = ShouldPreparePublishPayload();
                publishBridge = ShouldPrepareRos2BridgePayload();
            }

            // No main-thread clone. VirtualLidar allocates a fresh PointCloudFrame for every
            // scan (StartNewScan) and never mutates a frame after handing it to SetFrame, so
            // the background worker can read this frame directly. Cloning 262144 points on the
            // Update thread was the dominant per-frame main-thread spike that stalled the loop.
            var request = new DracoEncodeRequest(
                frame,
                unixNs,
                publishWebSocket,
                publishBridge,
                EffectiveEncoding,
                0d);
            EnqueueDracoEncodeRequest(request);
        }

        private void QueueVirtualLidarDracoEncode(
            VirtualLidarPointData[] points,
            int pointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs,
            bool publishWebSocket,
            bool publishBridge,
            PublisherEffectiveEncoding webSocketEncoding)
        {
            if (points == null || pointCount <= 0)
                return;

            _diagnostics.RecordPrepared(_logPerformanceDiagnostics, pointCount);
            var request = new DracoEncodeRequest(
                points,
                pointCount,
                unixNs,
                string.IsNullOrEmpty(frameId) ? _frameId : frameId,
                emitAbsoluteTimeNs,
                publishWebSocket,
                publishBridge,
                webSocketEncoding,
                0d);
            EnqueueDracoEncodeRequest(request);
        }

        private void EnqueueDracoEncodeRequest(DracoEncodeRequest request)
        {
            EnsureEncodePipelines();
            _dracoEncodePipeline.Queue(
                request,
                _logQosDrops,
                () => _diagnostics.RecordDrop(_logPerformanceDiagnostics));
        }

        private void PublishCompletedDracoPayload(DracoEncodeResult result)
        {
            _diagnostics.RecordEncodeResult(_logPerformanceDiagnostics, result);

            if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
            {
                PublishRos2(result.WebSocketPayload, result.Request.UnixNs);
            }
            else if (result.Request.PublishWebSocket)
            {
                PublishProto(result.WebSocketPayload, result.Request.UnixNs);
            }

            if (result.Request.PublishBridge)
                PublishRos2Bridge(result.BridgePayload, result.Request.UnixNs);
        }
    }
}
