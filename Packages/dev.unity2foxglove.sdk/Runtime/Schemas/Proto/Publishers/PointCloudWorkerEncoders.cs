// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Unity-free worker encoders for point-cloud publish payloads.

using System;
using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Pure worker-side point-cloud encoders and payload builders.
    /// </summary>
    internal static class PointCloudWorkerEncoders
    {
        /// <summary>Encode one Draco point-cloud request into publish-ready payloads.</summary>
        public static DracoEncodeResult EncodeDracoRequest(DracoEncodeRequest request)
        {
            FoxgloveProfiler.Global.BeginSample("PointCloudWorker.EncodeDraco");
            try
            {
                var encodeStart = Stopwatch.GetTimestamp();
                var success = false;
                var error = "";
                byte[] dracoPayload = null;
                var metadataFrame = request.Frame;
                var validCount = 0;

                if (request.HasVirtualLidarSnapshot)
                {
                    success = DracoPointCloudNativeEncoder.TryEncodeVirtualLidarPoints(
                        request.LidarPoints,
                        request.LidarPointCount,
                        out dracoPayload,
                        out error,
                        out validCount);
                    metadataFrame = new PointCloudFrame
                    {
                        UnixNs = request.UnixNs,
                        FrameId = request.FrameId,
                        ValidCount = validCount,
                        EmitAbsoluteTimeNs = request.EmitAbsoluteTimeNs
                    };
                }
                else
                {
                    success = DracoPointCloudNativeEncoder.TryEncode(request.Frame, out dracoPayload, out error);
                }

                byte[] webSocketPayload = null;
                byte[] bridgePayload = null;
                if (success)
                {
                    try
                    {
                        BuildDracoPublishPayloads(
                            request,
                            metadataFrame,
                            dracoPayload,
                            out webSocketPayload,
                            out bridgePayload);
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        error = "Unable to serialize compressed point-cloud payload off thread: " + ex.Message;
                    }
                }

                return new DracoEncodeResult(
                    request,
                    metadataFrame,
                    success,
                    webSocketPayload,
                    bridgePayload,
                    error,
                    ElapsedMs(encodeStart));
            }
            finally
            {
                FoxgloveProfiler.Global.EndSample();
            }
        }

        /// <summary>Pack one PointCloud2 Native request into publish-ready raw and optional deskewed frames.</summary>
        public static PointCloud2NativeResult EncodePointCloud2NativeRequest(PointCloud2NativeRequest request)
        {
            FoxgloveProfiler.Global.BeginSample("PointCloudWorker.EncodePointCloud2Native");
            try
            {
                var encodeStart = Stopwatch.GetTimestamp();
                var success = false;
                var error = "";
                byte[] webSocketPayload = null;
                byte[] bridgePayload = null;
                PointCloud2NativeFrame nativeFrame = null;
                PointCloud2NativeFrame motionCompensatedNativeFrame = null;
                var validCount = 0;
                var payloadBytes = 0;
                var rawPackMs = 0d;
                var rawPayloadBuildMs = 0d;
                var motionCompensationMs = 0d;
                var deskewPackMs = 0d;

                try
                {
                    var rawPackStart = DiagnosticStart(request.LogPerformanceDiagnostics);
                    var packed = PointCloud2PackedDataBuilder.BuildVirtualLidarFullStride(
                        request.LidarPoints,
                        request.LidarPointCount,
                        request.EmitAbsoluteTimeNs,
                        useAcquisitionFrameCoordinates: true);
                    rawPackMs = DiagnosticElapsedMs(rawPackStart);
                    validCount = packed.PointStride == 0U ? 0 : checked((int)(packed.Data.Length / packed.PointStride));
                    nativeFrame = BuildPointCloud2NativeFrame(request, packed, validCount);

                    byte[] ros2Payload = null;
                    if (request.PublishWebSocket && request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
                    {
                        var rawPayloadStart = DiagnosticStart(request.LogPerformanceDiagnostics);
                        ros2Payload = BuildPointCloud2NativePayload(nativeFrame);
                        rawPayloadBuildMs += DiagnosticElapsedMs(rawPayloadStart);
                        webSocketPayload = ros2Payload;
                    }

                    if (request.PublishBridge)
                    {
                        if (ros2Payload == null)
                        {
                            var rawPayloadStart = DiagnosticStart(request.LogPerformanceDiagnostics);
                            ros2Payload = BuildPointCloud2NativePayload(nativeFrame);
                            rawPayloadBuildMs += DiagnosticElapsedMs(rawPayloadStart);
                        }
                        bridgePayload = ros2Payload;
                    }

                    payloadBytes = ros2Payload?.Length ?? nativeFrame.Data.Length;

                    if (request.HasMotionCompensation)
                    {
                        var motionCompensationStart = DiagnosticStart(request.LogPerformanceDiagnostics);
                        if (!PointCloudMotionCompensator.TryCompensateVirtualLidar(
                                request.LidarPoints,
                                request.LidarPointCount,
                                request.UnixNs,
                                request.MotionCompensation,
                                out var compensated,
                                out var compensationError))
                        {
                            motionCompensationMs = DiagnosticElapsedMs(motionCompensationStart);
                            error = "Unable to build motion-compensated PointCloud2 frame: " + compensationError;
                        }
                        else
                        {
                            motionCompensationMs = DiagnosticElapsedMs(motionCompensationStart);
                            var deskewPackStart = DiagnosticStart(request.LogPerformanceDiagnostics);
                            var compensatedPacked = PointCloud2PackedDataBuilder.BuildVirtualLidarFullStride(
                                compensated.Points,
                                compensated.PointCount,
                                request.EmitAbsoluteTimeNs);
                            var compensatedValidCount = compensatedPacked.PointStride == 0U
                                ? 0
                                : checked((int)(compensatedPacked.Data.Length / compensatedPacked.PointStride));
                            motionCompensatedNativeFrame = BuildPointCloud2NativeFrame(
                                request,
                                compensatedPacked,
                                compensatedValidCount,
                                compensated.ReferenceUnixNs,
                                request.MotionCompensation.Topic,
                                isMotionCompensatedVisualization: true);
                            deskewPackMs = DiagnosticElapsedMs(deskewPackStart);
                        }
                    }

                    success = true;
                }
                catch (Exception ex)
                {
                    error = "Unable to serialize native PointCloud2 payload off thread: " + ex.Message;
                }

                return new PointCloud2NativeResult(
                    request,
                    success,
                    webSocketPayload,
                    bridgePayload,
                    nativeFrame,
                    motionCompensatedNativeFrame,
                    error,
                    validCount,
                    payloadBytes,
                    ElapsedMs(encodeStart),
                    rawPackMs,
                    rawPayloadBuildMs,
                    motionCompensationMs,
                    deskewPackMs);
            }
            finally
            {
                FoxgloveProfiler.Global.EndSample();
            }
        }

        private static PointCloud2NativeFrame BuildPointCloud2NativeFrame(
            PointCloud2NativeRequest request,
            PointCloudPackedData packed,
            int validCount)
            => BuildPointCloud2NativeFrame(
                request,
                packed,
                validCount,
                request.UnixNs,
                request.NativeTopic,
                isMotionCompensatedVisualization: false);

        private static PointCloud2NativeFrame BuildPointCloud2NativeFrame(
            PointCloud2NativeRequest request,
            PointCloudPackedData packed,
            int validCount,
            ulong unixNs,
            string topic,
            bool isMotionCompensatedVisualization)
        {
            return new PointCloud2NativeFrame(
                unixNs,
                request.FrameId,
                height: 1U,
                width: checked((uint)validCount),
                fields: packed.Fields,
                pointStep: packed.PointStride,
                data: packed.Data,
                isDense: true,
                topic: topic,
                isMotionCompensatedVisualization: isMotionCompensatedVisualization);
        }

        private static byte[] BuildPointCloud2NativePayload(PointCloud2NativeFrame frame)
        {
            return Ros2CdrSensorPointCloud2Builder.Serialize(
                frame.UnixNs,
                frame.FrameId,
                frame.Height,
                frame.Width,
                frame.Fields,
                frame.PointStep,
                frame.Data,
                frame.IsDense);
        }

        private static void BuildDracoPublishPayloads(
            DracoEncodeRequest request,
            PointCloudFrame frame,
            byte[] dracoPayload,
            out byte[] webSocketPayload,
            out byte[] bridgePayload)
        {
            webSocketPayload = null;
            bridgePayload = null;
            byte[] ros2Payload = null;

            if (request.PublishWebSocket && request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrCompressedPointCloudBuilder.Serialize(frame, dracoPayload);
                webSocketPayload = ros2Payload;
            }
            else if (request.PublishWebSocket)
            {
                webSocketPayload = CompressedPointCloudMessageBuilder.SerializeProtobuf(frame, dracoPayload);
            }

            if (request.PublishBridge)
            {
                ros2Payload ??= Ros2CdrCompressedPointCloudBuilder.Serialize(frame, dracoPayload);
                bridgePayload = ros2Payload;
            }
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;

        private static long DiagnosticStart(bool enabled)
            => enabled ? Stopwatch.GetTimestamp() : 0L;

        private static double DiagnosticElapsedMs(long startTicks)
            => startTicks == 0L ? 0d : ElapsedMs(startTicks);
    }
}
