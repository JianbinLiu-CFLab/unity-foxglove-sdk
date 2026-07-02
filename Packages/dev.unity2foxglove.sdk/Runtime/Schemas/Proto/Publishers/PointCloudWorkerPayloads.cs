// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Background point-cloud encode request and result payloads.

using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Point-cloud worker request that may own a pooled source snapshot.</summary>
    internal interface IPointCloudWorkerRequest : IBackgroundEncodeRequest
    {
        void RecycleSourceSnapshot();
    }

    /// <summary>Point-cloud worker result with access to its owning request.</summary>
    internal interface IPointCloudWorkerResult<out TRequest>
        where TRequest : class, IPointCloudWorkerRequest
    {
        TRequest Request { get; }
        void RecycleResultPayloads();
    }

    /// <summary>
    /// Captures one background Draco encode request plus the publish routes that
    /// should receive its completed payload.
    /// </summary>
    internal sealed class DracoEncodeRequest : IPointCloudWorkerRequest
    {
        private VirtualLidarPointData[] _lidarPoints;
        private int _lidarPointCount;

        /// <summary>Create a Draco request from a managed PointCloudFrame.</summary>
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

        /// <summary>Create a Draco request from a native VirtualLidar point snapshot.</summary>
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
            _lidarPoints = lidarPoints;
            _lidarPointCount = lidarPointCount;
            UnixNs = unixNs;
            FrameId = frameId;
            EmitAbsoluteTimeNs = emitAbsoluteTimeNs;
            PublishWebSocket = publishWebSocket;
            PublishBridge = publishBridge;
            WebSocketEncoding = webSocketEncoding;
            CloneMs = cloneMs;
        }

        /// <summary>Managed point-cloud frame used when no native LiDAR snapshot is present.</summary>
        public PointCloudFrame Frame { get; }

        /// <summary>Native LiDAR points cloned for worker-side Draco encoding.</summary>
        public VirtualLidarPointData[] LidarPoints => _lidarPoints;

        /// <summary>Number of native LiDAR point slots to encode.</summary>
        public int LidarPointCount => _lidarPointCount;

        /// <summary>True when this request carries a native VirtualLidar snapshot.</summary>
        public bool HasVirtualLidarSnapshot => _lidarPoints != null;

        /// <summary>Frame id used for metadata generated from native snapshots.</summary>
        public string FrameId { get; }

        /// <summary>True when per-point relative time should also be emitted as absolute nanoseconds.</summary>
        public bool EmitAbsoluteTimeNs { get; }

        /// <summary>Frame timestamp in Unix nanoseconds.</summary>
        public ulong UnixNs { get; }

        /// <summary>True when the websocket output path should receive the result.</summary>
        public bool PublishWebSocket { get; }

        /// <summary>True when the local ROS2 bridge output path should receive the result.</summary>
        public bool PublishBridge { get; }

        /// <summary>Effective websocket encoding selected when this request was queued.</summary>
        public PublisherEffectiveEncoding WebSocketEncoding { get; }

        /// <summary>Milliseconds spent cloning source data before enqueue.</summary>
        public double CloneMs { get; }

        /// <summary>Worker lifecycle generation used to orphan stale completed work.</summary>
        public int Generation { get; set; }

        public void RecycleSourceSnapshot()
        {
            var snapshot = _lidarPoints;
            _lidarPoints = null;
            _lidarPointCount = 0;
            VirtualLidarPointSnapshotPool.Return(snapshot);
        }
    }

    /// <summary>
    /// Captures one background PointCloud2 pack request plus the publish routes
    /// that should receive its completed CDR payload.
    /// </summary>
    internal sealed class PointCloud2NativeRequest : IPointCloudWorkerRequest
    {
        private VirtualLidarPointData[] _lidarPoints;
        private int _lidarPointCount;

        /// <summary>Create a PointCloud2 Native packing request from a VirtualLidar snapshot.</summary>
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
            bool logPerformanceDiagnostics,
            string nativeTopic = null,
            PointCloudMotionCompensationRequest motionCompensation = null)
        {
            _lidarPoints = lidarPoints;
            _lidarPointCount = lidarPointCount;
            UnixNs = unixNs;
            FrameId = frameId;
            EmitAbsoluteTimeNs = emitAbsoluteTimeNs;
            PublishWebSocket = publishWebSocket;
            PublishBridge = publishBridge;
            PublishNativeFrame = publishNativeFrame;
            WebSocketEncoding = webSocketEncoding;
            LogPerformanceDiagnostics = logPerformanceDiagnostics;
            NativeTopic = nativeTopic;
            MotionCompensation = motionCompensation;
        }

        /// <summary>Native LiDAR points cloned for worker-side PointCloud2 packing.</summary>
        public VirtualLidarPointData[] LidarPoints => _lidarPoints;

        /// <summary>Number of native LiDAR point slots to pack.</summary>
        public int LidarPointCount => _lidarPointCount;

        /// <summary>Frame timestamp in Unix nanoseconds.</summary>
        public ulong UnixNs { get; }

        /// <summary>Frame id written into PointCloud2 metadata.</summary>
        public string FrameId { get; }

        /// <summary>True when relative point time should also be emitted as absolute nanoseconds.</summary>
        public bool EmitAbsoluteTimeNs { get; }

        /// <summary>True when the websocket output path should receive the result.</summary>
        public bool PublishWebSocket { get; }

        /// <summary>True when the local ROS2 bridge output path should receive the result.</summary>
        public bool PublishBridge { get; }

        /// <summary>True when optional native DDS adapters should receive the frame handoff.</summary>
        public bool PublishNativeFrame { get; }

        /// <summary>Effective websocket encoding selected when this request was queued.</summary>
        public PublisherEffectiveEncoding WebSocketEncoding { get; }

        /// <summary>True when worker sub-stage timing diagnostics should be captured.</summary>
        public bool LogPerformanceDiagnostics { get; }

        /// <summary>Optional topic override for the raw native DDS frame.</summary>
        public string NativeTopic { get; }

        /// <summary>Optional request for a second motion-compensated visualization frame.</summary>
        public PointCloudMotionCompensationRequest MotionCompensation { get; }

        /// <summary>True when this request includes motion-compensation work.</summary>
        public bool HasMotionCompensation => MotionCompensation != null;

        /// <summary>Worker lifecycle generation used to orphan stale completed work.</summary>
        public int Generation { get; set; }

        public void RecycleSourceSnapshot()
        {
            var snapshot = _lidarPoints;
            _lidarPoints = null;
            _lidarPointCount = 0;
            VirtualLidarPointSnapshotPool.Return(snapshot);
        }
    }

    /// <summary>
    /// Completed background Draco encode result, including prepared websocket and
    /// ROS2 bridge payload bytes for main-thread publish.
    /// </summary>
    internal sealed class DracoEncodeResult : IPointCloudWorkerResult<DracoEncodeRequest>
    {
        /// <summary>Create a completed Draco worker result.</summary>
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

        /// <summary>Original worker request.</summary>
        public DracoEncodeRequest Request { get; }

        /// <summary>Metadata frame associated with the encoded Draco payload.</summary>
        public PointCloudFrame Frame { get; }

        /// <summary>True when encoding and payload construction succeeded.</summary>
        public bool Success { get; }

        /// <summary>Prepared websocket payload bytes, when requested.</summary>
        public byte[] WebSocketPayload { get; }

        /// <summary>Prepared ROS2 bridge payload bytes, when requested.</summary>
        public byte[] BridgePayload { get; }

        /// <summary>Failure reason when <see cref="Success"/> is false.</summary>
        public string Error { get; }

        /// <summary>Milliseconds spent on worker-side encoding.</summary>
        public double EncodeMs { get; }

        public void RecycleResultPayloads()
        {
        }
    }

    /// <summary>
    /// Fine-grained PointCloud2 Native encode diagnostics for stall investigation.
    /// Captures per-stage pack timings, packed-buffer pool behavior, and GC
    /// collection deltas observed across one worker encode.
    /// </summary>
    internal struct PointCloud2NativeEncodeDiagnostics
    {
        /// <summary>Raw pack valid-count scan milliseconds.</summary>
        public double RawCountValidMs;

        /// <summary>Raw packed buffer rent/allocation milliseconds.</summary>
        public double RawBufferRentMs;

        /// <summary>Raw pack write-loop milliseconds.</summary>
        public double RawWriteLoopMs;

        /// <summary>Raw packed buffer length in bytes.</summary>
        public int RawBufferLength;

        /// <summary>True when the raw packed buffer was reused from the pool.</summary>
        public bool RawBufferReused;

        /// <summary>Deskew pack valid-count scan milliseconds.</summary>
        public double DeskewCountValidMs;

        /// <summary>Deskew packed buffer rent/allocation milliseconds.</summary>
        public double DeskewBufferRentMs;

        /// <summary>Deskew pack write-loop milliseconds.</summary>
        public double DeskewWriteLoopMs;

        /// <summary>Deskew packed buffer length in bytes.</summary>
        public int DeskewBufferLength;

        /// <summary>True when the deskew packed buffer was reused from the pool.</summary>
        public bool DeskewBufferReused;

        /// <summary>GC gen0 collection count delta across the encode.</summary>
        public int GcGen0Delta;

        /// <summary>GC gen1 collection count delta across the encode.</summary>
        public int GcGen1Delta;

        /// <summary>GC gen2 collection count delta across the encode.</summary>
        public int GcGen2Delta;

        /// <summary>Packed buffers held by the pool after this encode; zero while rents miss means buffers are in flight.</summary>
        public int PoolRetainedBuffers;

        /// <summary>Bytes held by the packed buffer pool after this encode.</summary>
        public long PoolRetainedBytes;
    }

    /// <summary>
    /// Completed background PointCloud2 pack result with prepared CDR bytes for
    /// main-thread publish.
    /// </summary>
    internal sealed class PointCloud2NativeResult : IPointCloudWorkerResult<PointCloud2NativeRequest>
    {
        /// <summary>Create a completed PointCloud2 Native worker result.</summary>
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
            double encodeMs,
            double rawPackMs,
            double rawPayloadBuildMs,
            double motionCompensationMs,
            double deskewPackMs,
            PointCloud2NativeEncodeDiagnostics encodeDiagnostics = default)
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
            RawPackMs = rawPackMs;
            RawPayloadBuildMs = rawPayloadBuildMs;
            MotionCompensationMs = motionCompensationMs;
            DeskewPackMs = deskewPackMs;
            EncodeDiagnostics = encodeDiagnostics;
        }

        /// <summary>Original worker request.</summary>
        public PointCloud2NativeRequest Request { get; }

        /// <summary>True when packing and optional deskew frame construction succeeded.</summary>
        public bool Success { get; }

        /// <summary>Prepared websocket CDR payload bytes, when requested.</summary>
        public byte[] WebSocketPayload { get; }

        /// <summary>Prepared ROS2 bridge CDR payload bytes, when requested.</summary>
        public byte[] BridgePayload { get; }

        /// <summary>Raw PointCloud2 Native frame handoff for optional DDS adapters.</summary>
        public PointCloud2NativeFrame NativeFrame { get; }

        /// <summary>Deskewed visualization PointCloud2 Native frame handoff, when requested.</summary>
        public PointCloud2NativeFrame MotionCompensatedNativeFrame { get; }

        /// <summary>Failure reason when <see cref="Success"/> is false or deskew construction was skipped.</summary>
        public string Error { get; }

        /// <summary>Number of compacted valid points in the raw native frame.</summary>
        public int ValidCount { get; }

        /// <summary>Prepared payload byte count used for diagnostics.</summary>
        public int PayloadBytes { get; }

        /// <summary>Milliseconds spent on worker-side packing and optional deskew construction.</summary>
        public double EncodeMs { get; }

        /// <summary>Milliseconds spent compacting the raw VirtualLidar snapshot into PointCloud2 storage.</summary>
        public double RawPackMs { get; }

        /// <summary>Milliseconds spent building raw ROS2 CDR payload bytes for websocket or bridge output.</summary>
        public double RawPayloadBuildMs { get; }

        /// <summary>Milliseconds spent computing motion compensation before deskewed PointCloud2 packing.</summary>
        public double MotionCompensationMs { get; }

        /// <summary>Milliseconds spent packing the deskewed visualization PointCloud2 frame.</summary>
        public double DeskewPackMs { get; }

        /// <summary>Fine-grained pack/pool/GC diagnostics captured across this encode.</summary>
        public PointCloud2NativeEncodeDiagnostics EncodeDiagnostics { get; }

        public void RecycleResultPayloads()
        {
            NativeFrame?.RecycleData();
            MotionCompensatedNativeFrame?.RecycleData();
        }
    }
}
