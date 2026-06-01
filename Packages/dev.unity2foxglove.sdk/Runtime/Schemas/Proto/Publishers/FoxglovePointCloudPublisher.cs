// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes foxglove.PointCloud messages from decoded frames or Unity transforms.

using System;
using System.Collections.Generic;
using System.Threading;
using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Util;
using Stopwatch = System.Diagnostics.Stopwatch;
using UVector3 = UnityEngine.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes decoded point frames or child transforms as foxglove.PointCloud.
    /// Programmatic frames are intended for later Ouster/ROS input bridges.
    /// </summary>
    public class FoxglovePointCloudPublisher : FoxglovePublisherBase
    {
        private const int DracoFailureWarningIntervalFrames = 120;
        private const int MaxCompletedDracoEncodeResults = 8;
        private const int DracoWorkerStopWaitMs = 5000;
        private const int PointCloudDiagnosticsIntervalFrames = 60;

        [Header("Point Cloud Output")]
        [SerializeField] private PointCloudOutputMode _outputMode = PointCloudOutputMode.Draco;

        [Header("Point Cloud")]
        [SerializeField] private string _frameId = "unity_world";
        [SerializeField] private Transform[] _pointSources;
        [SerializeField] private bool _includeInactiveChildren;
        [SerializeField] private bool _useChildrenWhenSourcesEmpty = true;
        [SerializeField, Min(1)] private int _maxPoints = 4096;
        [SerializeField, Min(0)] private int _maxPackedBytes;
        [SerializeField] private PointCloudSamplingMode _samplingMode = PointCloudSamplingMode.FirstPoints;
        [SerializeField, Min(0f)] private float _voxelSizeMeters = 0.1f;
        [SerializeField] private bool _logQosDrops;
        [SerializeField] private bool _logPerformanceDiagnostics;
        [SerializeField] private bool _includeSyntheticIntensity;

        private PointCloudFrame _pendingFrame;
        private readonly object _pendingFrameGate = new object();
        private bool _warnedPointCloudBudget;
        private bool _warnedPendingDrop;
        private bool _warnedDracoFailure;
        private bool _hasPreparedPublishDemand;
        private bool _preparedPublishWebSocket;
        private bool _preparedPublishBridge;
        private int _dracoFailureCount;
        private bool _warnedDracoBacklog;
        private bool _warnedDracoWorkerShutdown;
        private readonly object _dracoEncodeGate = new object();
        private readonly Queue<DracoEncodeResult> _completedDracoEncodes = new Queue<DracoEncodeResult>();
        private readonly ManualResetEventSlim _dracoEncodeWorkerIdle = new ManualResetEventSlim(true);
        private DracoEncodeRequest _pendingDracoEncode;
        private int _droppedCompletedDracoEncodeCount;
        private bool _dracoEncodeWorkerRunning;
        private bool _stopDracoEncodeWorker;
        private int _diagnosticFrames;
        private long _diagnosticPreparedPoints;
        private int _diagnosticDrops;
        private double _diagnosticCloneMsTotal;
        private double _diagnosticCloneMsMax;
        private double _diagnosticEncodeMsTotal;
        private double _diagnosticEncodeMsMax;
        private int _diagnosticEncodeResults;

        private PointCloudOutputProfile ActiveProfile => PointCloudOutputProfile.ForMode(_outputMode);
        protected override string SchemaName => SchemaNameOverride;
        protected virtual string SchemaNameOverride => ActiveProfile.SchemaName;
        protected virtual string DefaultTopic => ActiveProfile.DefaultTopic;
        internal bool CanQueueVirtualLidarDracoFrame => _outputMode == PointCloudOutputMode.Draco;
        /// <summary>
        /// Public/member behavior description.
        /// </summary>

        public override bool SupportsJsonEncoding => ActiveProfile.SupportsJson;
        /// <summary>
        /// Public/member behavior description.
        /// </summary>

        public override bool SupportsProtobufEncoding => ActiveProfile.SupportsProtobuf;
        /// <summary>
        /// Public/member behavior description.
        /// </summary>

        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => _outputMode == PointCloudOutputMode.Draco
            ? Ros2PublisherSchemaNames.CompressedPointCloud
            : Ros2PublisherSchemaNames.PointCloud;

        protected virtual void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = DefaultTopic;
        }

        protected override void Reset()
        {
            base.Reset();
            _samplingMode = PointCloudSamplingMode.UniformStride;
        }

        protected override void OnDisable()
        {
            StopDracoEncodeWorker(clearCompleted: true);
            base.OnDisable();
        }

        private void OnDestroy()
        {
            StopDracoEncodeWorker(clearCompleted: true);
        }

        /// <summary>
        /// Queue a decoded frame for the next publish tick. This is a
        /// last-value-wins buffer: a new frame replaces stale pending data.
        /// </summary>
        /// <summary>
        /// Public/member behavior description.
        /// </summary>

/// <summary>Public/member behavior description.</summary>
        public void SetFrame(PointCloudFrame frame)
        {
            bool hadPendingFrame;
            lock (_pendingFrameGate)
            {
                hadPendingFrame = _pendingFrame != null;
                _pendingFrame = frame;
            }

            if (hadPendingFrame && frame != null && _logQosDrops && !_warnedPendingDrop)
            {
                Debug.LogWarning("[Foxglove] PointCloud pending frame replaced; stale pending frame dropped.");
                _warnedPendingDrop = true;
            }

            if (hadPendingFrame && frame != null)
                RecordPointCloudDrop();
        }

        /// <summary>
        /// Publish a decoded frame immediately, bypassing the regular Update cadence.
        /// </summary>
        /// <summary>
        /// Public/member behavior description.
        /// </summary>

/// <summary>Public/member behavior description.</summary>
        public void PublishFrame(PointCloudFrame frame, ulong logTimeNs)
        {
            ResolveManager();
            if (_manager == null || frame == null) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge) return;

            var prepared = PrepareFrameForQoS(frame, logTimeNs);
            if (prepared == null || prepared.GetPointCount() == 0) return;
            SetPreparedPublishDemand(publishWebSocket, publishBridge);
            try
            {
                PublishPreparedFrame(prepared, logTimeNs);
                LogPointCloudDiagnosticsIfReady();
            }
            finally
            {
                ClearPreparedPublishDemand();
            }
        }

        internal bool TryQueueVirtualLidarDracoFrame(
            VirtualLidarPointData[] points,
            int pointCount,
            ulong unixNs,
            string frameId,
            bool emitAbsoluteTimeNs)
        {
            if (!CanQueueVirtualLidarDracoFrame)
                return false;

            ResolveManager();
            if (_manager == null || _manager.Runtime?.ReplayEnabled == true)
                return true;

            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge)
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

        protected virtual void Update()
        {
            if (_manager == null) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            DrainCompletedDracoEncode();
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge) return;

            var unixNs = CurrentLogTimeNs;
            PointCloudFrame pendingFrame;
            lock (_pendingFrameGate)
            {
                pendingFrame = _pendingFrame;
                _pendingFrame = null;
            }

            // A source frame that carries its own timestamp (e.g. VirtualLidar's physics-time
            // scan start) drives the log time too, so payload time == log time (matching the
            // IMU path) and SLAM consumers see one consistent clock instead of a wall-clock vs
            // physics-clock skew that drifts under frame-rate stalls.
            if (pendingFrame != null && pendingFrame.UnixNs != 0)
                unixNs = pendingFrame.UnixNs;

            var frame = pendingFrame != null ? PrepareFrameForQoS(pendingFrame, unixNs) : PrepareFrameForQoS(CreateFrameFromTransforms(unixNs), unixNs);
            _warnedPendingDrop = false;
            if (frame == null || frame.GetPointCount() == 0) return;

            SetPreparedPublishDemand(publishWebSocket, publishBridge);
            try
            {
                PublishPreparedFrame(frame, unixNs);
                LogPointCloudDiagnosticsIfReady();
            }
            finally
            {
                ClearPreparedPublishDemand();
            }
        }

        protected virtual void PublishPreparedFrame(PointCloudFrame frame, ulong unixNs)
        {
            RecordPointCloudPrepared(frame);

            if (_outputMode == PointCloudOutputMode.Draco)
            {
                PublishDracoFrame(frame, unixNs);
                return;
            }

            PublishRawFrame(frame, unixNs);
        }

        private void PublishRawFrame(PointCloudFrame frame, ulong unixNs)
        {
            if (!TryGetPreparedPublishDemand(out var publishWebSocket, out var publishBridge))
            {
                publishWebSocket = ShouldPreparePublishPayload();
                publishBridge = ShouldPrepareRos2BridgePayload();
            }
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProto(PointCloudMessageBuilder.SerializeProtobuf(frame), unixNs);
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrPointCloudBuilder.Serialize(frame);
                PublishRos2(ros2Payload, unixNs);
            }
            else if (publishWebSocket)
            {
                Publish(PointCloudMessageBuilder.CreateJson(frame), unixNs);
            }

            if (publishBridge)
            {
                ros2Payload ??= Ros2CdrPointCloudBuilder.Serialize(frame);
                PublishRos2Bridge(ros2Payload, unixNs);
            }
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

            RecordPointCloudPrepared(pointCount);
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
            var startWorker = false;
            lock (_dracoEncodeGate)
            {
                if (_pendingDracoEncode != null && _logQosDrops && !_warnedDracoBacklog)
                {
                    Debug.LogWarning("[Foxglove] Draco point-cloud encode request replaced; stale pending encode dropped.");
                    _warnedDracoBacklog = true;
                }

                if (_pendingDracoEncode != null)
                    RecordPointCloudDrop();

                _pendingDracoEncode = request;
                if (!_dracoEncodeWorkerRunning)
                {
                    _stopDracoEncodeWorker = false;
                    _dracoEncodeWorkerRunning = true;
                    _dracoEncodeWorkerIdle.Reset();
                    startWorker = true;
                }
            }

            if (!startWorker)
                return;

            try
            {
                StartDracoEncodeWorker();
            }
            catch (Exception ex)
            {
                lock (_dracoEncodeGate)
                {
                    _dracoEncodeWorkerRunning = false;
                    _dracoEncodeWorkerIdle.Set();
                }
                LogDracoFailure("Unable to queue background Draco encode: " + ex.Message);
            }
        }

        private void StartDracoEncodeWorker()
        {
            var worker = new System.Threading.Thread(RunDracoEncodeWorker)
            {
                IsBackground = true,
                Name = "Foxglove Draco PointCloud Encode",
                Priority = System.Threading.ThreadPriority.BelowNormal
            };
            worker.Start();
        }

        private void RunDracoEncodeWorker()
        {
            try
            {
                while (true)
                {
                    DracoEncodeRequest request;
                    lock (_dracoEncodeGate)
                    {
                        if (_stopDracoEncodeWorker)
                        {
                            _dracoEncodeWorkerRunning = false;
                            return;
                        }

                        request = _pendingDracoEncode;
                        _pendingDracoEncode = null;
                        if (request == null)
                        {
                            _dracoEncodeWorkerRunning = false;
                            return;
                        }
                    }

                    var result = EncodeDracoRequest(request);
                    lock (_dracoEncodeGate)
                    {
                        if (_stopDracoEncodeWorker)
                            continue;

                        while (_completedDracoEncodes.Count >= MaxCompletedDracoEncodeResults)
                        {
                            _completedDracoEncodes.Dequeue();
                            _droppedCompletedDracoEncodeCount++;
                        }

                        _completedDracoEncodes.Enqueue(result);
                    }
                }
            }
            finally
            {
                lock (_dracoEncodeGate)
                {
                    _dracoEncodeWorkerRunning = false;
                }

                _dracoEncodeWorkerIdle.Set();
            }
        }

        private static DracoEncodeResult EncodeDracoRequest(DracoEncodeRequest request)
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
                (Stopwatch.GetTimestamp() - encodeStart) * 1000d / Stopwatch.Frequency);
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

        private void DrainCompletedDracoEncode()
        {
            List<DracoEncodeResult> results = null;
            int droppedCompletedResults;
            lock (_dracoEncodeGate)
            {
                droppedCompletedResults = _droppedCompletedDracoEncodeCount;
                _droppedCompletedDracoEncodeCount = 0;
                if (_completedDracoEncodes.Count > 0)
                {
                    results = new List<DracoEncodeResult>(_completedDracoEncodes);
                    _completedDracoEncodes.Clear();
                }
            }

            if (droppedCompletedResults > 0 && _logQosDrops)
                Debug.LogWarning($"[Foxglove] Draco point-cloud encode results dropped before main-thread drain: {droppedCompletedResults}.");
            if (droppedCompletedResults > 0)
                RecordPointCloudDrop(droppedCompletedResults);

            if (results == null || results.Count == 0)
                return;

            foreach (var result in results)
            {
                if (!result.Success)
                {
                    LogDracoFailure((string.IsNullOrWhiteSpace(result.Error) ? "Native Draco encode failed." : result.Error) + " Draco mode publishes nothing.");
                    continue;
                }

                _warnedDracoFailure = false;
                _dracoFailureCount = 0;
                _warnedDracoBacklog = false;
                RecordPointCloudEncodeResult(result);
                PublishCompletedDracoPayload(result);
            }

            LogPointCloudDiagnosticsIfReady();
        }

        private void StopDracoEncodeWorker(bool clearCompleted)
        {
            var shouldWait = false;
            lock (_dracoEncodeGate)
            {
                _stopDracoEncodeWorker = true;
                _pendingDracoEncode = null;
                shouldWait = _dracoEncodeWorkerRunning;
                if (clearCompleted)
                {
                    _completedDracoEncodes.Clear();
                    _droppedCompletedDracoEncodeCount = 0;
                }
            }

            if (!shouldWait)
                return;

            if (_dracoEncodeWorkerIdle.Wait(DracoWorkerStopWaitMs))
            {
                _warnedDracoWorkerShutdown = false;
                return;
            }

            if (!_warnedDracoWorkerShutdown)
            {
                Debug.LogWarning("[Foxglove] Draco point-cloud encode worker is still stopping; native encode will be ignored when it returns.");
                _warnedDracoWorkerShutdown = true;
            }
        }

        private void PublishCompletedDracoPayload(DracoEncodeResult result)
        {
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

        private void SetPreparedPublishDemand(bool publishWebSocket, bool publishBridge)
        {
            _preparedPublishWebSocket = publishWebSocket;
            _preparedPublishBridge = publishBridge;
            _hasPreparedPublishDemand = true;
        }

        private void ClearPreparedPublishDemand()
        {
            _hasPreparedPublishDemand = false;
            _preparedPublishWebSocket = false;
            _preparedPublishBridge = false;
        }

        private bool TryGetPreparedPublishDemand(out bool publishWebSocket, out bool publishBridge)
        {
            publishWebSocket = _preparedPublishWebSocket;
            publishBridge = _preparedPublishBridge;
            return _hasPreparedPublishDemand;
        }

        private void LogDracoFailure(string message)
        {
            _dracoFailureCount++;
            if (_warnedDracoFailure && _dracoFailureCount % DracoFailureWarningIntervalFrames != 0)
                return;

            _warnedDracoFailure = true;
            Debug.LogWarning("[Foxglove] Draco point-cloud mode disabled: " + message);
        }

        private sealed class DracoEncodeRequest
        {
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

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

            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public PointCloudFrame Frame { get; }
            public VirtualLidarPointData[] LidarPoints { get; }
            public int LidarPointCount { get; }
            public bool HasVirtualLidarSnapshot => LidarPoints != null;
            public string FrameId { get; }
            public bool EmitAbsoluteTimeNs { get; }
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public ulong UnixNs { get; }
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public bool PublishWebSocket { get; }
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public bool PublishBridge { get; }
            public PublisherEffectiveEncoding WebSocketEncoding { get; }
            /// <summary>Main-thread frame clone time before background Draco encode.</summary>
            public double CloneMs { get; }
        }

        private sealed class DracoEncodeResult
        {
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

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

            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public DracoEncodeRequest Request { get; }
            public PointCloudFrame Frame { get; }
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public bool Success { get; }
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public byte[] WebSocketPayload { get; }
            public byte[] BridgePayload { get; }
            /// <summary>
            /// Public/member behavior description.
            /// </summary>

            public string Error { get; }
            /// <summary>Worker-thread native encode time.</summary>
            public double EncodeMs { get; }
        }

        private long DiagnosticStart()
            => _logPerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0L;

        private double DiagnosticElapsedMs(long startTicks)
            => startTicks == 0L
                ? 0d
                : (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;

        private void RecordPointCloudPrepared(PointCloudFrame frame)
        {
            if (frame == null)
                return;

            RecordPointCloudPrepared(frame.GetPointCount());
        }

        private void RecordPointCloudPrepared(int pointCount)
        {
            if (!_logPerformanceDiagnostics)
                return;

            _diagnosticFrames++;
            _diagnosticPreparedPoints += Math.Max(0, pointCount);
        }

        private void RecordPointCloudDrop(int count = 1)
        {
            if (!_logPerformanceDiagnostics)
                return;

            _diagnosticDrops += Math.Max(1, count);
        }

        private void RecordPointCloudEncodeResult(DracoEncodeResult result)
        {
            if (!_logPerformanceDiagnostics || result == null)
                return;

            _diagnosticCloneMsTotal += result.Request.CloneMs;
            _diagnosticCloneMsMax = Math.Max(_diagnosticCloneMsMax, result.Request.CloneMs);
            _diagnosticEncodeMsTotal += result.EncodeMs;
            _diagnosticEncodeMsMax = Math.Max(_diagnosticEncodeMsMax, result.EncodeMs);
            _diagnosticEncodeResults++;
        }

        private void LogPointCloudDiagnosticsIfReady()
        {
            if (!_logPerformanceDiagnostics || _diagnosticFrames < PointCloudDiagnosticsIntervalFrames)
                return;

            var frameDivisor = Math.Max(1, _diagnosticFrames);
            var encodeDivisor = Math.Max(1, _diagnosticEncodeResults);
            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                this,
                "[PointCloudDiag] prepared={0} points={1} avgPoints={2:F0} cloneMs avg={3:F2} max={4:F2} encodeMs avg={5:F2} max={6:F2} drop={7}",
                _diagnosticFrames,
                _diagnosticPreparedPoints,
                (double)_diagnosticPreparedPoints / frameDivisor,
                _diagnosticCloneMsTotal / encodeDivisor,
                _diagnosticCloneMsMax,
                _diagnosticEncodeMsTotal / encodeDivisor,
                _diagnosticEncodeMsMax,
                _diagnosticDrops);

            _diagnosticFrames = 0;
            _diagnosticPreparedPoints = 0;
            _diagnosticDrops = 0;
            _diagnosticCloneMsTotal = 0d;
            _diagnosticCloneMsMax = 0d;
            _diagnosticEncodeMsTotal = 0d;
            _diagnosticEncodeMsMax = 0d;
            _diagnosticEncodeResults = 0;
        }

        protected virtual PointCloudFrame PrepareFrameForQoS(PointCloudFrame frame, ulong unixNs)
        {
            if (frame == null)
                return null;

            var pointCount = frame.GetPointCount();
            var stride = PointCloudQoS.ComputePackedStride(frame);
            var pointBudget = PointCloudQoS.ComputeEffectivePointBudget(
                pointCount,
                _maxPoints,
                Math.Max(0, _maxPackedBytes),
                stride);

            if (pointBudget <= 0)
            {
                WarnPointCloudReduced(pointCount, pointBudget);
                return null;
            }

            var useVoxelGrid = _samplingMode == PointCloudSamplingMode.VoxelGrid && _voxelSizeMeters > 0f;
            var forceUniformFallback = _samplingMode == PointCloudSamplingMode.VoxelGrid && _voxelSizeMeters <= 0f;

            if (!useVoxelGrid && !forceUniformFallback && frame.UnixNs != 0 && !string.IsNullOrEmpty(frame.FrameId) && pointCount <= pointBudget)
            {
                _warnedPointCloudBudget = false;
                return frame;
            }

            var copy = new PointCloudFrame
            {
                UnixNs = frame.UnixNs == 0 ? unixNs : frame.UnixNs,
                FrameId = string.IsNullOrEmpty(frame.FrameId) ? _frameId : frame.FrameId
            };

            if (useVoxelGrid)
            {
                var voxelIndices = PointCloudQoS.BuildVoxelSampleIndices(frame, _voxelSizeMeters);
                if (voxelIndices.Length <= pointBudget)
                {
                    foreach (var index in voxelIndices)
                        copy.Points.Add(frame.Points[index]);
                }
                else
                {
                    var indices = PointCloudQoS.BuildUniformSampleIndices(voxelIndices.Length, pointBudget);
                    foreach (var index in indices)
                        copy.Points.Add(frame.Points[voxelIndices[index]]);
                }
            }
            else if (pointCount <= pointBudget && !forceUniformFallback)
            {
                for (var i = 0; i < pointCount; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else if (_samplingMode == PointCloudSamplingMode.FirstPoints)
            {
                var count = Math.Min(pointCount, pointBudget);
                for (var i = 0; i < count; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else
            {
                var indices = PointCloudQoS.BuildUniformSampleIndices(pointCount, pointBudget);
                foreach (var index in indices)
                    copy.Points.Add(frame.Points[index]);
            }

            if (pointCount > pointBudget)
                WarnPointCloudReduced(pointCount, pointBudget);
            else
            {
                _warnedPointCloudBudget = false;
            }

            copy.ValidCount = copy.Points.Count;

            return copy;
        }

        private void WarnPointCloudReduced(int originalPoints, int outputPoints)
        {
            if (!_logQosDrops) return;
            if (_warnedPointCloudBudget) return;

            Debug.LogWarning(
                $"[Foxglove] PointCloud frame reduced from {originalPoints} to {Math.Max(0, outputPoints)} points.");
            _warnedPointCloudBudget = true;
        }

        private PointCloudFrame CreateFrameFromTransforms(ulong unixNs)
        {
            var frame = new PointCloudFrame
            {
                UnixNs = unixNs,
                FrameId = SanitizeFrameId(_frameId, "unity_world")
            };

            var added = 0;
            if (_pointSources != null && _pointSources.Length > 0)
            {
                foreach (var source in _pointSources)
                    AddPoint(frame, source, ref added);
            }
            else if (_useChildrenWhenSourcesEmpty)
            {
                for (var i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    if (!_includeInactiveChildren && !child.gameObject.activeInHierarchy)
                        continue;
                    AddPoint(frame, child, ref added);
                }
            }

            return frame;
        }

        private void AddPoint(PointCloudFrame frame, Transform source, ref int added)
        {
            if (source == null || added >= Math.Max(1, _maxPoints)) return;

            UVector3 pos = Manager != null && Manager.ActiveCoordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxglovePosition(source.position)
                : source.position;

            var point = new PointCloudPoint(pos.x, pos.y, pos.z);
            if (_includeSyntheticIntensity)
                point.Intensity = added;
            frame.Points.Add(point);
            added++;
        }
    }
}
