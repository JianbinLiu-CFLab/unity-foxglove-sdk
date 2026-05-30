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
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Util;
using UVector3 = UnityEngine.Vector3;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes decoded point frames or child transforms as foxglove.PointCloud.
    /// Programmatic frames are intended for later Ouster/ROS input bridges.
    /// </summary>
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public class FoxglovePointCloudPublisher : FoxglovePublisherBase
    {
        private const int DracoFailureWarningIntervalFrames = 120;
        private const int MaxCompletedDracoEncodeResults = 8;
        private const int DracoWorkerStopWaitMs = 5000;

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

        private PointCloudOutputProfile ActiveProfile => PointCloudOutputProfile.ForMode(_outputMode);
        protected override string SchemaName => SchemaNameOverride;
        protected virtual string SchemaNameOverride => ActiveProfile.SchemaName;
        protected virtual string DefaultTopic => ActiveProfile.DefaultTopic;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public override bool SupportsJsonEncoding => ActiveProfile.SupportsJson;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public override bool SupportsProtobufEncoding => ActiveProfile.SupportsProtobuf;
        /// <summary>
        /// Summary text for this member.
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
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
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
        }

        /// <summary>
        /// Publish a decoded frame immediately, bypassing the regular Update cadence.
        /// </summary>
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public void PublishFrame(PointCloudFrame frame, ulong logTimeNs)
        {
            ResolveManager();
            if (_manager == null || frame == null) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge) return;

            var prepared = PrepareFrameForQoS(frame, logTimeNs);
            if (prepared == null || prepared.Points.Count == 0) return;
            SetPreparedPublishDemand(publishWebSocket, publishBridge);
            try
            {
                PublishPreparedFrame(prepared, logTimeNs);
            }
            finally
            {
                ClearPreparedPublishDemand();
            }
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

            var frame = pendingFrame != null ? PrepareFrameForQoS(pendingFrame, unixNs) : PrepareFrameForQoS(CreateFrameFromTransforms(unixNs), unixNs);
            _warnedPendingDrop = false;
            if (frame == null || frame.Points.Count == 0) return;

            SetPreparedPublishDemand(publishWebSocket, publishBridge);
            try
            {
                PublishPreparedFrame(frame, unixNs);
            }
            finally
            {
                ClearPreparedPublishDemand();
            }
        }

        protected virtual void PublishPreparedFrame(PointCloudFrame frame, ulong unixNs)
        {
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
            if (frame == null || frame.Points.Count == 0)
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

            var request = new DracoEncodeRequest(CloneFrameForBackgroundEncode(frame), unixNs, publishWebSocket, publishBridge);
            var startWorker = false;
            lock (_dracoEncodeGate)
            {
                if (_pendingDracoEncode != null && _logQosDrops && !_warnedDracoBacklog)
                {
                    Debug.LogWarning("[Foxglove] Draco point-cloud encode request replaced; stale pending encode dropped.");
                    _warnedDracoBacklog = true;
                }

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
                ThreadPool.QueueUserWorkItem(_ => RunDracoEncodeWorker());
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

                    var success = DracoPointCloudNativeEncoder.TryEncode(request.Frame, out var dracoPayload, out var encodeError);
                    var result = new DracoEncodeResult(request, success, dracoPayload, encodeError);
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
                PublishDracoPayload(result.Request.Frame, result.Request.UnixNs, result.Payload, result.Request.PublishWebSocket, result.Request.PublishBridge);
            }
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

        private void PublishDracoPayload(PointCloudFrame frame, ulong unixNs, byte[] dracoPayload, bool publishWebSocket, bool publishBridge)
        {
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrCompressedPointCloudBuilder.Serialize(frame, dracoPayload);
                PublishRos2(ros2Payload, unixNs);
            }
            else if (publishWebSocket)
            {
                var payload = CompressedPointCloudMessageBuilder.SerializeProtobuf(frame, dracoPayload);
                PublishProto(payload, unixNs);
            }

            if (publishBridge)
            {
                ros2Payload ??= Ros2CdrCompressedPointCloudBuilder.Serialize(frame, dracoPayload);
                PublishRos2Bridge(ros2Payload, unixNs);
            }
        }

        private static PointCloudFrame CloneFrameForBackgroundEncode(PointCloudFrame frame)
        {
            var copy = new PointCloudFrame
            {
                UnixNs = frame.UnixNs,
                FrameId = frame.FrameId
            };

            foreach (var point in frame.Points)
            {
                copy.Points.Add(new PointCloudPoint(point.X, point.Y, point.Z)
                {
                    Intensity = point.Intensity,
                    Reflectivity = point.Reflectivity,
                    Ring = point.Ring,
                    TimeOffsetSeconds = point.TimeOffsetSeconds
                });
            }

            return copy;
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
            /// Summary text for this member.
            /// </summary>

            public DracoEncodeRequest(PointCloudFrame frame, ulong unixNs, bool publishWebSocket, bool publishBridge)
            {
                Frame = frame;
                UnixNs = unixNs;
                PublishWebSocket = publishWebSocket;
                PublishBridge = publishBridge;
            }

            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public PointCloudFrame Frame { get; }
            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public ulong UnixNs { get; }
            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public bool PublishWebSocket { get; }
            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public bool PublishBridge { get; }
        }

        private sealed class DracoEncodeResult
        {
            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public DracoEncodeResult(DracoEncodeRequest request, bool success, byte[] payload, string error)
            {
                Request = request;
                Success = success;
                Payload = payload;
                Error = error;
            }

            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public DracoEncodeRequest Request { get; }
            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public bool Success { get; }
            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public byte[] Payload { get; }
            /// <summary>
            /// Summary text for this member.
            /// </summary>

            public string Error { get; }
        }

        protected virtual PointCloudFrame PrepareFrameForQoS(PointCloudFrame frame, ulong unixNs)
        {
            if (frame == null)
                return null;

            var stride = PointCloudQoS.ComputePackedStride(frame);
            var pointBudget = PointCloudQoS.ComputeEffectivePointBudget(
                frame.Points.Count,
                _maxPoints,
                Math.Max(0, _maxPackedBytes),
                stride);

            if (pointBudget <= 0)
            {
                WarnPointCloudReduced(frame.Points.Count, pointBudget);
                return null;
            }

            var useVoxelGrid = _samplingMode == PointCloudSamplingMode.VoxelGrid && _voxelSizeMeters > 0f;
            var forceUniformFallback = _samplingMode == PointCloudSamplingMode.VoxelGrid && _voxelSizeMeters <= 0f;

            if (!useVoxelGrid && !forceUniformFallback && frame.UnixNs != 0 && !string.IsNullOrEmpty(frame.FrameId) && frame.Points.Count <= pointBudget)
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
            else if (frame.Points.Count <= pointBudget && !forceUniformFallback)
            {
                for (var i = 0; i < frame.Points.Count; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else if (_samplingMode == PointCloudSamplingMode.FirstPoints)
            {
                var count = Math.Min(frame.Points.Count, pointBudget);
                for (var i = 0; i < count; i++)
                    copy.Points.Add(frame.Points[i]);
            }
            else
            {
                var indices = PointCloudQoS.BuildUniformSampleIndices(frame.Points.Count, pointBudget);
                foreach (var index in indices)
                    copy.Points.Add(frame.Points[index]);
            }

            if (frame.Points.Count > pointBudget)
                WarnPointCloudReduced(frame.Points.Count, pointBudget);
            else
            {
                _warnedPointCloudBudget = false;
            }

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

