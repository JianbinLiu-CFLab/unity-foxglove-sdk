// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers

using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Captures one background Draco encode request plus the publish routes that
    /// should receive its completed payload.
    /// </summary>
    internal sealed class DracoEncodeRequest : IBackgroundEncodeRequest
    {
        public DracoEncodeRequest(
            PointCloudFrame frame,
            ulong unixNs,
            bool publishWebSocket,
            bool publishBridge,
            PublisherEffectiveEncoding webSocketEncoding,
            double cloneMs)
        {
            Frame = frame;
            UnixNs = unixNs;
            PublishWebSocket = publishWebSocket;
            PublishBridge = publishBridge;
            WebSocketEncoding = webSocketEncoding;
            CloneMs = cloneMs;
        }

        public DracoEncodeRequest(
            VirtualLidarPointData[] lidarPoints,
            int lidarPointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs,
            bool publishWebSocket,
            bool publishBridge,
            PublisherEffectiveEncoding webSocketEncoding,
            double cloneMs)
        {
            LidarPoints = lidarPoints;
            LidarPointCount = lidarPointCount;
            UnixNs = unixNs;
            FrameId = frameId;
            EmitAbsoluteTimeNs = emitAbsoluteTimeNs;
            PublishWebSocket = publishWebSocket;
            PublishBridge = publishBridge;
            WebSocketEncoding = webSocketEncoding;
            CloneMs = cloneMs;
        }

        public PointCloudFrame Frame { get; }
        public VirtualLidarPointData[] LidarPoints { get; }
        public int LidarPointCount { get; }
        public bool HasVirtualLidarSnapshot => LidarPoints != null;
        public string FrameId { get; }
        public bool EmitAbsoluteTimeNs { get; }
        public ulong UnixNs { get; }
        public bool PublishWebSocket { get; }
        public bool PublishBridge { get; }
        public PublisherEffectiveEncoding WebSocketEncoding { get; }
        public double CloneMs { get; }
        public int Generation { get; set; }
    }

    /// <summary>
    /// Captures one background PointCloud2 pack request plus the publish routes
    /// that should receive its completed CDR payload.
    /// </summary>
    internal sealed class PointCloud2NativeRequest : IBackgroundEncodeRequest
    {
        public PointCloud2NativeRequest(
            VirtualLidarPointData[] lidarPoints,
            int lidarPointCount,
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
            LidarPoints = lidarPoints;
            LidarPointCount = lidarPointCount;
            UnixNs = unixNs;
            FrameId = frameId;
            EmitAbsoluteTimeNs = emitAbsoluteTimeNs;
            PublishWebSocket = publishWebSocket;
            PublishBridge = publishBridge;
            PublishNativeFrame = publishNativeFrame;
            WebSocketEncoding = webSocketEncoding;
            NativeTopic = nativeTopic;
            MotionCompensation = motionCompensation;
        }

        public VirtualLidarPointData[] LidarPoints { get; }
        public int LidarPointCount { get; }
        public ulong UnixNs { get; }
        public string FrameId { get; }
        public bool EmitAbsoluteTimeNs { get; }
        public bool PublishWebSocket { get; }
        public bool PublishBridge { get; }
        public bool PublishNativeFrame { get; }
        public PublisherEffectiveEncoding WebSocketEncoding { get; }
        public string NativeTopic { get; }
        public PointCloudMotionCompensationRequest MotionCompensation { get; }
        public bool HasMotionCompensation => MotionCompensation != null;
        public int Generation { get; set; }
    }

    /// <summary>
    /// Completed background Draco encode result, including prepared websocket and
    /// ROS2 bridge payload bytes for main-thread publish.
    /// </summary>
    internal sealed class DracoEncodeResult
    {
        public DracoEncodeResult(
            DracoEncodeRequest request,
            PointCloudFrame frame,
            bool success,
            byte[] webSocketPayload,
            byte[] bridgePayload,
            string error,
            double encodeMs)
        {
            Request = request;
            Frame = frame;
            Success = success;
            WebSocketPayload = webSocketPayload;
            BridgePayload = bridgePayload;
            Error = error;
            EncodeMs = encodeMs;
        }

        public DracoEncodeRequest Request { get; }
        public PointCloudFrame Frame { get; }
        public bool Success { get; }
        public byte[] WebSocketPayload { get; }
        public byte[] BridgePayload { get; }
        public string Error { get; }
        public double EncodeMs { get; }
    }

    /// <summary>
    /// Completed background PointCloud2 pack result with prepared CDR bytes for
    /// main-thread publish.
    /// </summary>
    internal sealed class PointCloud2NativeResult
    {
        public PointCloud2NativeResult(
            PointCloud2NativeRequest request,
            bool success,
            byte[] webSocketPayload,
            byte[] bridgePayload,
            PointCloud2NativeFrame nativeFrame,
            PointCloud2NativeFrame motionCompensatedNativeFrame,
            string error,
            int validCount,
            int payloadBytes,
            double encodeMs)
        {
            Request = request;
            Success = success;
            WebSocketPayload = webSocketPayload;
            BridgePayload = bridgePayload;
            NativeFrame = nativeFrame;
            MotionCompensatedNativeFrame = motionCompensatedNativeFrame;
            Error = error;
            ValidCount = validCount;
            PayloadBytes = payloadBytes;
            EncodeMs = encodeMs;
        }

        public PointCloud2NativeRequest Request { get; }
        public bool Success { get; }
        public byte[] WebSocketPayload { get; }
        public byte[] BridgePayload { get; }
        public PointCloud2NativeFrame NativeFrame { get; }
        public PointCloud2NativeFrame MotionCompensatedNativeFrame { get; }
        public string Error { get; }
        public int ValidCount { get; }
        public int PayloadBytes { get; }
        public double EncodeMs { get; }
    }
}
