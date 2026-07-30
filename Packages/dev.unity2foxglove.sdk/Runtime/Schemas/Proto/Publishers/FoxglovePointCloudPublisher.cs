// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes foxglove.PointCloud messages from decoded frames or Unity transforms.

using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Publishes decoded point frames or child transforms as foxglove.PointCloud.
    /// Programmatic frames are intended for later Ouster/ROS input bridges.
    /// </summary>
    public partial class FoxglovePointCloudPublisher : FoxglovePublisherBase
    {
        private const int DracoFailureWarningIntervalFrames = 120;
        // Keep the latest completed Draco frame only. Publishing several stale
        // completed point clouds in one Update creates visible Foxglove bursts
        // when Unity is unfocused or a client render stalls.
        private const int MaxCompletedDracoEncodeResults = 1;
        private const int PackedPointCloudFailureWarningIntervalFrames = 120;
        // PackedPointCloud Native frames are large enough that draining stale completed
        // results in a burst can hitch the main loop. Keep the latest completed
        // frame only; the pending side is already last-value-wins.
        private const int MaxCompletedPackedPointCloudResults = 1;
        private const int DracoWorkerStopWaitMs = 5000;
        private const int PackedPointCloudWorkerStopWaitMs = 5000;
        private const float DefaultNativeDracoMaxPublishRateHz = 6f;

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

        [Tooltip("Publish a lightweight frame anchor when no scene, robot, or SLAM tree owns the point-cloud frame.")]
        [SerializeField] private bool _publishPackedPointCloudTfAnchor;
        [Tooltip("Parent frame used when publishing the PackedPointCloud Native TF anchor.")]
        [SerializeField] private string _packedPointCloudTfParentFrame = "map";
        [Tooltip("Child frame used when publishing the PackedPointCloud Native TF anchor. Leave empty to follow Frame Id.")]
        [SerializeField] private string _packedPointCloudTfChildFrame;
        [Tooltip("TF anchor translation in ROS coordinates.")]
        [SerializeField] private Vector3 _packedPointCloudTfTranslation;
        [Tooltip("TF anchor rotation in ROS roll/pitch/yaw degrees.")]
        [SerializeField] private Vector3 _packedPointCloudTfRotationEuler;

        [Header("Motion Compensation")]
        [Tooltip("Emit an optional deskewed PackedPointCloud visualization stream. Leave disabled for raw SLAM input.")]
        [SerializeField] private bool _enableMotionCompensation;
        [SerializeField] private PointCloudMotionCompensationOutputPolicy _motionCompensationOutputPolicy = PointCloudMotionCompensationOutputPolicy.RawAndDeskewedTopic;
        [SerializeField] private string _deskewedPackedPointCloudTopic = PointCloudMotionCompensationOptions.DefaultDeskewedTopic;
        [Tooltip("Optional cap for deskewed PackedPointCloud visualization output. Set 0 to publish a deskewed frame for every eligible raw scan.")]
        [SerializeField, Min(0f)] private float _deskewedPackedPointCloudMaxPublishRateHz = 2f;
        [SerializeField] private PointCloudMotionCompensationReferenceTime _motionCompensationReferenceTime = PointCloudMotionCompensationReferenceTime.ScanStart;
        [SerializeField] private PointCloudMotionCompensationSource _motionCompensationSource = PointCloudMotionCompensationSource.SensorTransform;

        [Header("Draco")]
        [Tooltip("Caps source-driven VirtualLidar native Draco visualization snapshots. The default 6 Hz keeps Foxglove responsive; set 0 only when you explicitly want every completed source scan.")]
        [SerializeField, Min(0f)] private float _nativeDracoMaxPublishRateHz = DefaultNativeDracoMaxPublishRateHz;
        [Tooltip("When source-driven frames arrive through SetFrame/PublishFrame/VirtualLidar native Draco, suppress the transform fallback generated by this publisher's Update loop so sparse child-transform frames cannot overwrite real LiDAR clouds.")]
        [SerializeField] private bool _suppressTransformFallbackAfterSourceFrames = true;

        private readonly PointCloudPendingFrameSlot _pendingFrameSlot = new PointCloudPendingFrameSlot();
        private readonly PointCloudPublishState _publishState = new PointCloudPublishState();
        private readonly PointCloudQoSReducer _qosReducer = new PointCloudQoSReducer(Debug.LogWarning);
        private readonly TransformPointCloudSource _transformPointCloudSource = new TransformPointCloudSource();
        private PointCloudEncodePipeline<DracoEncodeRequest, DracoEncodeResult> _dracoEncodePipeline;
        private PointCloudEncodePipeline<PackedPointCloudRequest, PackedPointCloudResult> _packedPointCloudPipeline;
        private readonly PointCloudPublishDiagnostics _diagnostics = new PointCloudPublishDiagnostics();
        private readonly SensorMotionPoseHistory _motionPoseHistory = new SensorMotionPoseHistory();
        private ulong _lastNativeDracoPublishUnixNs;
        private ulong _lastDeskewedPackedPointCloudPublishUnixNs;
        private float _cachedNativeDracoMaxPublishRateHz = float.NaN;
        private ulong _cachedNativeDracoPublishIntervalNs;
        private float _cachedDeskewedPackedPointCloudMaxPublishRateHz = float.NaN;
        private ulong _cachedDeskewedPackedPointCloudPublishIntervalNs;
        private int _unityThreadId;
        private int _motionCompensationWarningCount;

        private PointCloudOutputProfile ActiveProfile => PointCloudOutputProfile.ForMode(_outputMode);
        protected override string SchemaName => SchemaNameOverride;
        protected virtual string SchemaNameOverride => ActiveProfile.SchemaName;
        protected virtual string DefaultTopic => ActiveProfile.DefaultTopic;
        /// <summary>True when VirtualLidar may use the low-allocation Draco queue.</summary>
        internal bool CanQueueVirtualLidarDracoFrame => _outputMode == PointCloudOutputMode.Draco;
        /// <summary>True when VirtualLidar may use the low-allocation PackedPointCloud Native queue.</summary>
        internal bool CanQueueVirtualLidarPackedPointCloudFrame => _outputMode == PointCloudOutputMode.PackedPointCloud;
        /// <summary>True when any VirtualLidar native queue is active for this mode.</summary>
        internal bool CanQueueVirtualLidarNativeFrame => CanQueueVirtualLidarDracoFrame || CanQueueVirtualLidarPackedPointCloudFrame;
        /// <summary>True when VirtualLidar must compute acquisition-time point coordinates.</summary>
        internal bool RequiresVirtualLidarAcquisitionFrame => CanQueueVirtualLidarPackedPointCloudFrame || EnableMotionCompensatedPackedPointCloud;
        /// <summary>Whether the selected output mode supports JSON payloads.</summary>
        public override bool SupportsJsonEncoding => ActiveProfile.SupportsJson;

        /// <summary>
        /// Raised on the Unity main thread after the PackedPointCloud native worker has
        /// prepared packed data. Optional DDS adapters can publish the frame without
        /// doing per-point work on the main thread.
        /// </summary>
        public event Action<PackedPointCloudFrame> PackedPointCloudFrameReady;

        /// <summary>Whether the selected output mode supports protobuf payloads.</summary>
        public override bool SupportsProtobufEncoding => ActiveProfile.SupportsProtobuf;

        /// <summary>Current user-selected point-cloud output mode.</summary>
        public PointCloudOutputMode OutputMode => _outputMode;

        /// <summary>True when this publisher is configured for standard PackedPointCloud native output.</summary>
        public bool IsPackedPointCloudOutput => _outputMode == PointCloudOutputMode.PackedPointCloud;

        /// <summary>True when opt-in point-cloud performance diagnostics should emit detailed timing logs.</summary>
        public bool PerformanceDiagnosticsEnabled => _logPerformanceDiagnostics;

        /// <summary>Resolved publisher topic for optional point-cloud Providers.</summary>
        public string PackedPointCloudTopic => string.IsNullOrWhiteSpace(_topic) ? DefaultTopic : _topic;

        /// <summary>True when a deskewed visualization PackedPointCloud stream is enabled.</summary>
        public bool EnableMotionCompensatedPackedPointCloud => IsPackedPointCloudOutput && _enableMotionCompensation;

        /// <summary>Resolved deskewed visualization topic for optional Providers.</summary>
        public string MotionCompensatedPackedPointCloudTopic
            => PointCloudMotionCompensationOptions.NormalizeTopic(
                _deskewedPackedPointCloudTopic,
                PointCloudMotionCompensationOptions.DefaultDeskewedTopic);

        /// <summary>Resolved motion-compensation output policy.</summary>
        public PointCloudMotionCompensationOutputPolicy MotionCompensationOutputPolicy => _motionCompensationOutputPolicy;

        /// <summary>Resolved frame id for optional point-cloud Providers.</summary>
        public string PointCloudFrameId => SanitizeNonEmptyFrameId(_frameId, "unity_world");

        /// <summary>True when PackedPointCloud Native output should also publish a TF anchor.</summary>
        public bool PublishPackedPointCloudTfAnchor => IsPackedPointCloudOutput && _publishPackedPointCloudTfAnchor;

        /// <summary>Resolved parent frame for the optional PackedPointCloud Native TF anchor.</summary>
        public string PackedPointCloudTfParentFrame => SanitizeNonEmptyFrameId(_packedPointCloudTfParentFrame, "map");

        /// <summary>Resolved child frame for the optional PackedPointCloud Native TF anchor.</summary>
        public string PackedPointCloudTfChildFrame => SanitizeNonEmptyFrameId(_packedPointCloudTfChildFrame, PointCloudFrameId);

        /// <summary>Translation for the optional PackedPointCloud Native TF anchor.</summary>
        public Vector3 PackedPointCloudTfTranslation => _packedPointCloudTfTranslation;

        /// <summary>Rotation for the optional PackedPointCloud Native TF anchor.</summary>
        public Quaternion PackedPointCloudTfRotation => PackedPointCloudTfRotationRos;

        /// <summary>Rotation for the optional PackedPointCloud Native TF anchor from ROS roll/pitch/yaw degrees.</summary>
        public Quaternion PackedPointCloudTfRotationRos
        {
            get
            {
                var q = RosTransformMath.RollPitchYawDegreesToQuaternion(
                    _packedPointCloudTfRotationEuler.x,
                    _packedPointCloudTfRotationEuler.y,
                    _packedPointCloudTfRotationEuler.z);
                return new Quaternion(q.X, q.Y, q.Z, q.W);
            }
        }

        protected virtual void Awake()
        {
            EnsureEncodePipelines();
            if (string.IsNullOrEmpty(_topic)) _topic = DefaultTopic;
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private static string SanitizeNonEmptyFrameId(string raw, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
            return SanitizeFrameId(value, fallback);
        }

        protected override void Reset()
        {
            base.Reset();
            _samplingMode = PointCloudSamplingMode.UniformStride;
        }

        protected override void OnDisable()
        {
            _lastNativeDracoPublishUnixNs = 0UL;
            _lastDeskewedPackedPointCloudPublishUnixNs = 0UL;
            _motionCompensationWarningCount = 0;
            _motionPoseHistory.Clear();
            _pendingFrameSlot.Take();
            _publishState.ResetSourceDriven();
            _publishState.ClearPreparedDemand();
            _dracoEncodePipeline?.Stop(clearCompleted: true);
            _packedPointCloudPipeline?.Stop(clearCompleted: true);
            base.OnDisable();
        }

        private void OnDestroy()
        {
            _dracoEncodePipeline?.Dispose();
            _dracoEncodePipeline = null;
            _packedPointCloudPipeline?.Dispose();
            _packedPointCloudPipeline = null;
        }

        protected virtual void FixedUpdate()
        {
            if (!IsPackedPointCloudOutput || !_enableMotionCompensation)
                return;

            EnsureManagerAvailable();
            var unixNs = _manager == null
                ? CurrentLogTimeNs
                : _manager.GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble);
            var foxglovePosition = CoordinateConverter.UnityToFoxglovePosition(transform.position);
            var foxgloveRotation = CoordinateConverter.UnityToFoxgloveRotation(transform.rotation);

            _motionPoseHistory.Add(
                unixNs,
                new NumericsVector3(foxglovePosition.x, foxglovePosition.y, foxglovePosition.z),
                new NumericsQuaternion(foxgloveRotation.x, foxgloveRotation.y, foxgloveRotation.z, foxgloveRotation.w));
        }

        /// <summary>
        /// Queue a decoded frame for the next publish tick.
        /// This is a last-value-wins buffer: a new frame replaces stale pending data.
        /// </summary>
        public void SetFrame(PointCloudFrame frame)
        {
            if (frame != null)
                MarkSourceDrivenPointCloud();

            var droppedPendingFrame = _pendingFrameSlot.SetFrame(frame, Volatile.Read(ref _logQosDrops), out var warning);

            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);

            if (droppedPendingFrame)
                _diagnostics.RecordDrop(Volatile.Read(ref _logPerformanceDiagnostics));
        }

        /// <summary>
        /// Publish a decoded frame immediately, bypassing the regular Update cadence.
        /// </summary>
        /// <remarks>
        /// Use the supplied timestamp to keep LiDAR timing aligned with IMU/TF inputs.
        /// This immediate publish path must run on the Unity main thread. Use
        /// <see cref="SetFrame(PointCloudFrame)"/> when handing off frames from workers.
        /// </remarks>
        public void PublishFrame(PointCloudFrame frame, ulong logTimeNs)
        {
            if (!IsUnityMainThread())
                throw new InvalidOperationException("PointCloud PublishFrame must run on the Unity main thread. Use SetFrame for worker-thread handoff.");

            if (frame != null)
                MarkSourceDrivenPointCloud();

            ResolveManager();
            if (_manager == null || frame == null) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishProvider = ShouldPrepareOrdinaryTransportPayload();
            var publishNativeFrame = ShouldPreparePackedPointCloudFrame();
            if (!publishWebSocket && !publishProvider && !publishNativeFrame) return;

            var prepared = PrepareFrameForQoS(frame, logTimeNs, out var packedLayout);
            if (prepared == null || prepared.GetPointCount() == 0) return;
            SetPreparedPublishDemand(publishWebSocket, publishProvider);
            try
            {
                PublishPreparedFrame(prepared, logTimeNs, packedLayout);
                _diagnostics.LogIfReady(_logPerformanceDiagnostics, LogPointCloudDiagnosticMessage);
            }
            finally
            {
                ClearPreparedPublishDemand();
            }
        }

        /// <summary>
        /// Queues a source VirtualLidar snapshot into the Draco worker when the
        /// selected mode is compatible.
        /// </summary>

        /// <summary>
        /// Queues a source VirtualLidar snapshot into the PackedPointCloud Native
        /// worker when the selected mode is compatible.
        /// </summary>


        protected virtual void Update()
        {
            if (_manager == null) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            EnsureEncodePipelines();
            _dracoEncodePipeline.Drain(
                _logQosDrops,
                dropped => _diagnostics.RecordDrop(_logPerformanceDiagnostics, dropped),
                () => _diagnostics.LogIfReady(_logPerformanceDiagnostics, LogPointCloudDiagnosticMessage));
            var packedPointCloudDrainStart = BeginPackedPointCloudTiming();
            _packedPointCloudPipeline.Drain(
                _logQosDrops,
                dropped => _diagnostics.RecordDrop(_logPerformanceDiagnostics, dropped),
                () =>
                {
                    _diagnostics.LogIfReady(_logPerformanceDiagnostics, LogPointCloudDiagnosticMessage);
                    LogPackedPointCloudTiming(packedPointCloudDrainStart, "pipelineDrain", PackedPointCloudTopic, 0, 0);
                });
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishProvider = ShouldPrepareOrdinaryTransportPayload();
            var publishNativeFrame = ShouldPreparePackedPointCloudFrame();
            if (!publishWebSocket && !publishProvider && !publishNativeFrame) return;

            var unixNs = CurrentLogTimeNs;
            var pendingFrame = _pendingFrameSlot.Take();

            // A source frame that carries its own timestamp (e.g. VirtualLidar's physics-time
            // scan start) drives the log time too, so payload time == log time (matching the
            // IMU path) and SLAM consumers see one consistent clock instead of a wall-clock vs
            // physics-clock skew that drifts under frame-rate stalls.
            if (pendingFrame != null && pendingFrame.UnixNs != 0)
                unixNs = pendingFrame.UnixNs;

            if (pendingFrame == null && ShouldSuppressTransformFallback())
            {
                if (_logQosDrops && _publishState.ShouldLogTransformFallbackSuppressedWarning())
                {
                    Debug.LogWarning("[Foxglove] PointCloud transform fallback suppressed after source-driven frames; real LiDAR/source frames will own this topic.");
                }
                return;
            }

            PointCloudPackedDataBuilder.PointCloudLayout packedLayout;
            var frame = pendingFrame != null
                ? PrepareFrameForQoS(pendingFrame, unixNs, out packedLayout)
                : PrepareFrameForQoS(_transformPointCloudSource.CreateFrameFromTransforms(
                    unixNs,
                    SanitizeFrameId(_frameId, "unity_world"),
                    transform,
                    _pointSources,
                    _useChildrenWhenSourcesEmpty,
                    _includeInactiveChildren,
                    _includeSyntheticIntensity,
                    _maxPoints,
                    Manager != null ? Manager.ActiveOutputCoordinateMode : CoordinateMode.LeftHand),
                    unixNs,
                    out packedLayout);
            _pendingFrameSlot.ResetReplacementWarning();
            if (frame == null || frame.GetPointCount() == 0) return;

            SetPreparedPublishDemand(publishWebSocket, publishProvider);
            try
            {
                PublishPreparedFrame(frame, unixNs, packedLayout);
                _diagnostics.LogIfReady(_logPerformanceDiagnostics, LogPointCloudDiagnosticMessage);
            }
            finally
            {
                ClearPreparedPublishDemand();
            }
        }

        protected virtual void PublishPreparedFrame(PointCloudFrame frame, ulong unixNs)
        {
            PublishPreparedFrame(frame, unixNs, null);
        }

        protected virtual void PublishPreparedFrame(
            PointCloudFrame frame,
            ulong unixNs,
            PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            _diagnostics.RecordPrepared(_logPerformanceDiagnostics, frame);

            if (_outputMode == PointCloudOutputMode.Draco)
            {
                PublishDracoFrame(frame, unixNs);
                return;
            }

            if (_outputMode == PointCloudOutputMode.PackedPointCloud)
            {
                PublishPackedPointCloudFrame(frame, unixNs, packedLayout);
                return;
            }

            PublishRawFrame(frame, unixNs, packedLayout);
        }






        private bool IsUnityMainThread()
        {
            return _unityThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _unityThreadId;
        }

        /// <summary>
        /// Marks this publisher as source-driven so transform fallback frames do
        /// not overwrite real LiDAR output.
        /// </summary>
        internal void MarkSourceDrivenPointCloud()
        {
            _publishState.MarkSourceDriven();
        }

        private bool ShouldSuppressTransformFallback()
            => _publishState.ShouldSuppressTransformFallback(_suppressTransformFallbackAfterSourceFrames);

        private bool ShouldPreparePackedPointCloudFrame()
            => _outputMode == PointCloudOutputMode.PackedPointCloud
               && PackedPointCloudFrameReady != null;

        private void EnsureEncodePipelines()
        {
            if (_dracoEncodePipeline == null)
            {
                _dracoEncodePipeline = new PointCloudEncodePipeline<DracoEncodeRequest, DracoEncodeResult>(
                    "Foxglove Draco PointCloud Encode",
                    MaxCompletedDracoEncodeResults,
                    DracoWorkerStopWaitMs,
                    PointCloudWorkerEncoders.EncodeDracoRequest,
                    result => result.Success,
                    result => (string.IsNullOrWhiteSpace(result.Error) ? "Native Draco encode failed." : result.Error) + " Draco mode publishes nothing.",
                    message => "[Foxglove] Draco point-cloud mode disabled: " + message,
                    PublishCompletedDracoPayload,
                    Debug.LogWarning,
                    LogPointCloudDropDiagnostic,
                    "[Foxglove] Draco point-cloud encode request replaced; stale pending encode dropped.",
                    "Unable to queue background Draco encode: ",
                    dropped => $"[Foxglove] Draco point-cloud encode results dropped before main-thread drain: {dropped}.",
                    "[Foxglove] Draco point-cloud encode worker is still stopping; native encode will be ignored when it returns.",
                    DracoFailureWarningIntervalFrames);
            }

            if (_packedPointCloudPipeline == null)
            {
                _packedPointCloudPipeline = new PointCloudEncodePipeline<PackedPointCloudRequest, PackedPointCloudResult>(
                    "Foxglove PackedPointCloud Native Pack",
                    MaxCompletedPackedPointCloudResults,
                    PackedPointCloudWorkerStopWaitMs,
                    PointCloudWorkerEncoders.EncodePackedPointCloudRequest,
                    result => result.Success,
                    result => (string.IsNullOrWhiteSpace(result.Error) ? "Native PackedPointCloud pack failed." : result.Error) + " PackedPointCloud mode publishes nothing.",
                    message => "[Foxglove] PackedPointCloud native mode disabled: " + message,
                    PublishCompletedPackedPointCloudPayload,
                    Debug.LogWarning,
                    LogPointCloudDropDiagnostic,
                    "[Foxglove] PackedPointCloud native request replaced; stale pending payload dropped.",
                    "Unable to queue background PackedPointCloud pack: ",
                    dropped => $"[Foxglove] PackedPointCloud native payloads dropped before main-thread drain: {dropped}.",
                    "[Foxglove] PackedPointCloud native worker is still stopping; native payload will be ignored when it returns.",
                    PackedPointCloudFailureWarningIntervalFrames);
            }
        }

        private static void LogPointCloudDropDiagnostic(string message)
            => Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message ?? string.Empty);



        private void SetPreparedPublishDemand(bool publishWebSocket, bool publishProvider)
        {
            _publishState.SetPreparedDemand(publishWebSocket, publishProvider);
        }

        private void ClearPreparedPublishDemand()
        {
            _publishState.ClearPreparedDemand();
        }

        private bool TryGetPreparedPublishDemand(out bool publishWebSocket, out bool publishProvider)
        {
            return _publishState.TryGetPreparedDemand(out publishWebSocket, out publishProvider);
        }


        protected virtual PointCloudFrame PrepareFrameForQoS(PointCloudFrame frame, ulong unixNs)
        {
            return PrepareFrameForQoS(frame, unixNs, out _);
        }

        private PointCloudFrame PrepareFrameForQoS(
            PointCloudFrame frame,
            ulong unixNs,
            out PointCloudPackedDataBuilder.PointCloudLayout packedLayout)
        {
            return _qosReducer.PrepareFrameForQoS(
                frame,
                unixNs,
                string.IsNullOrWhiteSpace(_frameId) ? "unity_world" : _frameId,
                _maxPoints,
                _maxPackedBytes,
                _samplingMode,
                _voxelSizeMeters,
                _logQosDrops,
                out packedLayout);
        }
    }
}
