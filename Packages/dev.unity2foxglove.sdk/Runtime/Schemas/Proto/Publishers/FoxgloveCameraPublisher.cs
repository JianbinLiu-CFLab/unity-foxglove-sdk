// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Captures camera frames via AsyncGPUReadback and publishes them
// as foxglove.CompressedImage JPEG frames or FFmpeg-backed foxglove.CompressedVideo frames.

using System;
using System.Threading;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Util;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Captures camera frames and publishes either dependency-free JPEG images
    /// or optional FFmpeg-backed H.264/H.265 compressed video.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public partial class FoxgloveCameraPublisher : FoxglovePublisherBase
    {
        private const float DefaultMaxCaptureRateHz = 10f;

        [Header("Camera Output")]
        [SerializeField] private CameraOutputMode _outputMode = CameraOutputMode.Jpeg;

        /// <summary>Identifier for the Foxglove frame, e.g. <c>"unity_camera"</c>.</summary>
        [SerializeField] private string _frameId = "unity_camera";
        /// <summary>Capture resolution width in pixels.</summary>
        [SerializeField, Min(1)] private int _width = 640;
        /// <summary>Capture resolution height in pixels.</summary>
        [SerializeField, Min(1)] private int _height = 480;
        /// <summary>JPEG quality 10-100.</summary>
        [Range(10, 100)]
        [SerializeField] private int _jpegQuality = 70;
        /// <summary>Max number of concurrent AsyncGPUReadback requests.</summary>
        [SerializeField, Min(1)] private int _maxPendingReadbacks = 1;
        [Tooltip("Maximum source capture/render rate for heavy camera visualization. Use 0 to capture every eligible publisher tick.")]
        [SerializeField, Min(0f)] private float _maxCaptureRateHz = DefaultMaxCaptureRateHz;
        [SerializeField] private CameraPipelineHealthMode _cameraHealthMode = CameraPipelineHealthMode.Balanced;

        [Header("Async JPEG")]
        [Tooltip("Encode JPEG camera frames on a background worker using Unity-free buffers.")]
        [SerializeField] private bool _useAsyncJpeg = true;
        [Tooltip("Maximum number of raw readback frames waiting for JPEG encode.")]
        [SerializeField, Min(1)] private int _maxJpegEncodeQueue = 2;
        [Tooltip("Maximum number of encoded JPEG frames waiting for main-thread publish.")]
        [SerializeField, Min(1)] private int _maxCompletedJpegQueue = 2;
        [Tooltip("Maximum completed JPEG frames published from LateUpdate per frame.")]
        [SerializeField, Min(1)] private int _maxCompletedJpegPublishesPerFrame = 1;
        [Tooltip("Maximum pixels in a single JPEG capture; 0 means unlimited.")]
        [SerializeField, Min(0)] private int _maxPixelsPerFrame;
        [Tooltip("When enabled, JPEG capture only schedules a new frame when readback, encode, and completed-publish queues are idle.")]
        [SerializeField] private bool _requireIdleJpegPipeline = true;
        [Tooltip("Camera pipeline stage duration in milliseconds that triggers capture cooldown; 0 disables the cooldown trigger.")]
        [SerializeField, Min(0f)] private float _pipelineCooldownThresholdMs = 50f;
        [Tooltip("Base milliseconds to wait before scheduling another JPEG capture after a slow camera pipeline stage; 0 disables cooldown waiting.")]
        [SerializeField, Min(0f)] private float _pipelineCooldownMs = 1000f;
        [Tooltip("Frame delta in milliseconds above which JPEG capture waits for several healthy main-loop frames; 0 disables this gate.")]
        [SerializeField, Min(0f)] private float _mainLoopCaptureCooldownThresholdMs = 100f;
        [Tooltip("Number of healthy main-loop frames required before JPEG capture resumes after a slow frame; 0 disables this gate.")]
        [SerializeField, Min(0)] private int _mainLoopStableFramesBeforeCapture = 2;
        [Tooltip("Log CameraDiag timing and queue counters for the JPEG path.")]
        [SerializeField] private bool _logCameraDiagnostics;
        [Tooltip("Minimum seconds between CameraDiag log lines.")]
        [SerializeField, Min(0.1f)] private float _cameraDiagnosticsIntervalSeconds = 2f;
        [Tooltip("Camera stage duration in milliseconds before a CameraSlow diagnostic is emitted.")]
        [SerializeField, Min(1f)] private float _cameraSlowStageThresholdMs = 50f;

        [Header("FFmpeg Video")]
        [SerializeField] private string _ffmpegPath = "";
        [SerializeField, Min(1)] private int _videoBitrateKbps = 4000;
        [SerializeField, Min(1)] private int _videoKeyframeInterval = 30;
        [SerializeField, Min(1)] private int _videoMaxOutputQueue = 4;
        [Tooltip("Log VideoDiag timing and drop counters for H.264/H.265 paths.")]
        [SerializeField] private bool _logVideoDiagnostics;
        [SerializeField] private bool _logEncoderStderr;

        [Header("OpenH264 Video")]
        [SerializeField] private string _openH264HelperPath = "";
        [SerializeField] private string _openH264DllPath = "";
        [SerializeField, Min(1)] private int _openH264MaxInputQueue = 2;

        [Header("Backpressure")]
        [Tooltip("When enabled, transport queue pressure suppresses camera capture to reduce work.")]
        [SerializeField] private bool _enableBackpressureAdaptation;
        [Tooltip("Seconds to wait before resuming capture after backpressure is observed.")]
        [Min(0)]
        [SerializeField] private float _backpressureCooldownSeconds = 0.5f;
        [Tooltip("Maximum encoded JPEG size in bytes; 0 means unlimited.")]
        [Min(0)]
        [SerializeField] private int _maxEncodedBytes;
        [Tooltip("Log a warning each time a capture is skipped by backpressure.")]
        [SerializeField] private bool _logBackpressureSkips;

        [Tooltip("Optional shared LiDAR/IMU/camera unit profile that owns SLAM frame IDs and topics.")]
        [SerializeField] private MonoBehaviour _sensorUnitProfile;
        [Tooltip("Use the manager shared sensor clock so camera frames align with IMU/LiDAR timestamps.")]
        [SerializeField] private bool _useSharedSensorClock = true;
        [Tooltip("Publish JPEG as the standard ROS2 compressed camera image schema when ROS2 encoding is selected.")]
        [SerializeField] private bool _publishStandardRos2CompressedImage;
        [Tooltip("Publish raw standard ROS2 Image frames when enabled and an optional R2FU/native ROS2 adapter subscribes to the raw image event.")]
        [SerializeField] private bool _publishStandardRos2RawImage;
        [Tooltip("Default raw topic when no override profile topic is set.")]
        [SerializeField] private string _sensorCameraRawImageTopic = "/unity/sensor/camera/image";

        private bool _rawBandwidthWarningIssued;

        private readonly CameraOutputModeRuntimeLock _outputModeRuntimeLock = new CameraOutputModeRuntimeLock();

        private CameraOutputMode ResolvedOutputMode
        {
            get
            {
                return _outputModeRuntimeLock.Resolve(_outputMode, Application.isPlaying);
            }
        }

        private CameraVideoOutputProfile ActiveProfile => CameraVideoOutputProfile.ForMode(ResolvedOutputMode);

        protected override string SchemaName => ActiveProfile.SchemaName;
        public override bool SupportsJsonEncoding => ActiveProfile.SupportsJson;
        public override bool SupportsProtobufEncoding => ActiveProfile.SupportsProtobuf;
        public override bool SupportsRos2Encoding => ActiveProfile.Mode == CameraOutputMode.Jpeg;
        protected override string Ros2SchemaName => ActiveProfile.Mode == CameraOutputMode.Jpeg
            ? (_publishStandardRos2CompressedImage
                ? Ros2PublisherSchemaNames.SensorCompressedImage
                : Ros2PublisherSchemaNames.CompressedImage)
            : "";

        /// <summary>
        /// Raised after the JPEG path produces a standard compressed image frame.
        /// Optional ROS2 adapters translate this core-SDK DTO into native ROS messages.
        /// </summary>
        public event Action<SensorCompressedImageFrame> SensorCompressedImageReady;
        /// <summary>Raised after a ROS2 raw image frame is built from readback data.</summary>
        public event Action<SensorRawImageFrame> SensorRawImageReady;

        /// <summary>Whether this component is configured for standard ROS2 compressed image output.</summary>
        public bool IsStandardRos2CompressedImageOutput
            => ActiveProfile.Mode == CameraOutputMode.Jpeg && _publishStandardRos2CompressedImage;
        /// <summary>Whether this component is configured for standard ROS2 raw image output.</summary>
        public bool IsStandardRos2RawImageOutput
            => _publishStandardRos2RawImage;

        /// <summary>Resolved topic for the standard camera image stream.</summary>
        public string SensorCameraImageTopic => ResolveSensorCameraImageTopic();
        /// <summary>Resolved topic for the standard raw camera image stream.</summary>
        public string SensorCameraRawImageTopic => ResolveSensorCameraRawImageTopic();

        /// <summary>Resolved frame ID for this camera stream.</summary>
        public string SensorCameraFrameId => ResolveFrameId();

        /// <summary>Resolved capture width used by the camera image stream.</summary>
        public int SensorCameraCaptureWidth => Math.Max(1, _width);

        /// <summary>Resolved capture height used by the camera image stream.</summary>
        public int SensorCameraCaptureHeight => Math.Max(1, _height);

        // Capture state
        private int _pendingRequests;
        private bool _destroyed;
        private int _captureGeneration;
        private bool _cleanupWhenReadbacksDrain;
        private readonly CameraCaptureResources _captureResources = new CameraCaptureResources();

        // Video sidecar state
        private readonly CameraPublishDiagnostics _diagnostics = new CameraPublishDiagnostics();
        private CameraVideoPublishPipeline _videoPublishPipeline;
        private readonly CameraBackpressureGate _backpressureGate = new CameraBackpressureGate();

        // Async JPEG state
        private CameraJpegPublishPipeline _jpegPublishPipeline;
        private ulong _lastPublishedCaptureUnixNs;
        private ulong _lastSourceCaptureUnixNs;
        private double _pipelineCooldownUntilSeconds;
        private int _mainLoopStableFramesRemaining;
        private float _cachedMaxCaptureRateHz = float.NaN;
        private ulong _cachedMaxCaptureIntervalNs;

        /// <summary>Defaults the topic to the current mode default if not set.</summary>
        private void Awake()
        {
            ApplySensorProfileDefaults();
            if (string.IsNullOrEmpty(_topic))
                _topic = ActiveProfile.DefaultTopic;
            EnsureJpegPublishPipeline();
            EnsureVideoPublishPipeline();
        }

        /// <summary>
        /// Locks schema-affecting camera mode before registration so Play Mode does not
        /// advertise one topic/schema and publish another after an Inspector change.
        /// </summary>
        protected override void OnEnable()
        {
            ApplySensorProfileDefaults();
            LockRuntimeOutputMode();
            base.OnEnable();
            _destroyed = false;
            _cleanupWhenReadbacksDrain = false;
            Interlocked.Increment(ref _captureGeneration);
            _lastSourceCaptureUnixNs = 0UL;
            ResetBackpressureState();
            ResetJpegPipelineState();
            ResetVideoDiagnosticState();
            EnsureCaptureResources();
            if (_useAsyncJpeg && ActiveProfile.Mode == CameraOutputMode.Jpeg)
                EnsureJpegWorkerStarted();
            _rawBandwidthWarningIssued = false;
        }

        /// <summary>
        /// Schedules a camera capture only when cadence, demand, replay state,
        /// and readback limits allow useful payload work.
        /// </summary>
        private void LateUpdate()
        {
            WarnIfRuntimeOutputModeSwitchIgnored();
            var profile = ActiveProfile;
            DrainCompletedJpegFrames();
            DrainEncodedAccessUnits();
            if (!EnsureSidecarMatchesMode(profile))
                return;

            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            var publishNativeFrame = HasSensorCompressedImageDemand(profile);
            var publishRawFrame = HasSensorRawImageDemand();
            if (!publishWebSocket && !publishBridge && !publishNativeFrame && !publishRawFrame) return;
            var publishJpegOutput = !profile.IsVideo && (publishWebSocket || publishBridge || publishNativeFrame);
            if (publishJpegOutput && !AllowJpegCaptureByBackpressure()) return;
            LogRawBandwidthWarningIfNeeded();

            var requestVideoOutput = profile.IsVideo && (publishWebSocket || publishBridge);
            if (requestVideoOutput && !EnsureVideoSidecarStarted(profile)) return;
            if (!profile.IsVideo && !AllowJpegCaptureByMainLoopHealth())
            {
                EmitCameraDiagnosticsIfNeeded();
                return;
            }
            if (!profile.IsVideo && !AllowJpegCaptureByFrameBudget())
            {
                EmitCameraDiagnosticsIfNeeded();
                return;
            }
            if (!profile.IsVideo && !AllowJpegCaptureByPipelineHealth())
            {
                EmitCameraDiagnosticsIfNeeded();
                return;
            }

            var renderUnixNs = CurrentLogTimeNs;
            if (_useSharedSensorClock)
                renderUnixNs = ResolveCameraCaptureUnixNs();
            if (!AllowCameraCaptureBySourceRate(renderUnixNs))
            {
                EmitCameraDiagnosticsIfNeeded();
                return;
            }
            if (!AllowCameraCaptureByHealthPolicy(profile))
            {
                EmitCameraDiagnosticsIfNeeded();
                return;
            }

            EnsureCaptureResources();
            var pendingBeforeSchedule = _pendingRequests;
            var renderStart = Stopwatch.GetTimestamp();
            _captureResources.CaptureCamera.Render();
            var renderMs = ElapsedMs(renderStart);
            RecordPipelineCooldownIfNeeded(renderMs);
            _diagnostics.RecordRenderMs(
                renderMs,
                Time.realtimeSinceStartupAsDouble,
                _jpegPublishPipeline?.EncodeQueueDepth ?? 0,
                _jpegPublishPipeline?.CompletedQueueDepth ?? 0);
            // Snapshot the concrete render target size with the readback request. Inspector
            // width/height can change while this callback is in flight.
            var captureRenderTexture = _captureResources.CaptureRenderTexture;
            var generation = _captureGeneration;
            var captureWidth = captureRenderTexture.width;
            var captureHeight = captureRenderTexture.height;
            RememberReadbackStart(renderUnixNs, Stopwatch.GetTimestamp());
            _pendingRequests++;
            AsyncGPUReadback.Request(captureRenderTexture, 0, TextureFormat.RGB24, req => OnReadbackComplete(req, generation, renderUnixNs, captureWidth, captureHeight));
            _diagnostics.RecordReadbackScheduled(
                pendingBeforeSchedule,
                _pendingRequests,
                Time.realtimeSinceStartupAsDouble,
                _jpegPublishPipeline?.EncodeQueueDepth ?? 0,
                _jpegPublishPipeline?.CompletedQueueDepth ?? 0);
            EmitCameraSlowStageIfNeeded(
                "render",
                renderMs,
                pendingBeforeSchedule,
                _pendingRequests);
        }

        /// <summary>
        /// Completes one local readback request and routes it using the generation and
        /// dimensions captured when the request was issued.
        /// </summary>
        private void OnReadbackComplete(AsyncGPUReadbackRequest req, int generation, ulong renderUnixNs, int captureWidth, int captureHeight)
        {
            var readbackLatencyMs = TakeReadbackLatencyMs(renderUnixNs);
            try
            {
                // Equivalent to generation != _captureGeneration, but with a cross-thread visible read.
                if (_destroyed || !isActiveAndEnabled || generation != Volatile.Read(ref _captureGeneration)) return;
                if (req.hasError)
                {
                    Debug.LogWarning("[Foxglove] Camera AsyncGPUReadback failed.");
                    return;
                }
                if (_manager == null) return;

                var profile = ActiveProfile;
                var publishRawFrame = HasSensorRawImageDemand();

                var publishWebSocket = ShouldPreparePublishPayload();
                var publishBridge = ShouldPrepareRos2BridgePayload();
                var publishNativeFrame = HasSensorCompressedImageDemand(profile);
                var publishJpegFrame = publishWebSocket || publishBridge || publishNativeFrame;
                var publishVideo = profile.IsVideo && (publishWebSocket || publishBridge);
                if (publishVideo)
                {
                    SubmitVideoFrame(req, profile, renderUnixNs, captureWidth, captureHeight);
                    if (publishRawFrame)
                    {
                        var rawBytes = req.GetData<byte>().ToArray();
                        PublishRawFrame(rawBytes, renderUnixNs, captureWidth, captureHeight);
                    }
                    return;
                }

                if (!publishJpegFrame && !publishRawFrame)
                {
                    _diagnostics.RecordNoDemandJpegDrop();
                    return;
                }

                var frameBytes = publishRawFrame || (_useAsyncJpeg && publishJpegFrame)
                    ? req.GetData<byte>().ToArray()
                    : null;
                if (!publishJpegFrame)
                {
                    if (frameBytes != null)
                        PublishRawFrame(frameBytes, renderUnixNs, captureWidth, captureHeight);
                    return;
                }

                if (_useAsyncJpeg && EnsureJpegWorkerStarted())
                {
                    QueueJpegFrame(
                        req,
                        renderUnixNs,
                        captureWidth,
                        captureHeight,
                        publishWebSocket,
                        publishBridge,
                        publishNativeFrame,
                        EffectiveEncoding,
                        readbackLatencyMs,
                        frameBytes);
                    if (publishRawFrame && frameBytes != null)
                        PublishRawFrame(frameBytes, renderUnixNs, captureWidth, captureHeight);
                    return;
                }

                PublishJpegFrame(req, renderUnixNs, captureWidth, captureHeight, frameBytes);
                if (publishRawFrame && frameBytes != null)
                    PublishRawFrame(frameBytes, renderUnixNs, captureWidth, captureHeight);
            }
            finally
            {
                CompletePendingReadback();
            }
        }

        /// <summary>
        /// Invalidates stale callbacks and lets local readbacks drain without globally
        /// waiting on unrelated AsyncGPUReadback work.
        /// </summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            Interlocked.Increment(ref _captureGeneration);
            _cleanupWhenReadbacksDrain = _pendingRequests > 0;
            StopVideoSidecar();
            StopJpegWorker(clearQueues: true);
            _jpegPublishPipeline?.Dispose();
            _jpegPublishPipeline = null;
            if (_pendingRequests == 0)
                CleanupResources();
            UnlockRuntimeOutputMode();
        }

        /// <summary>
        /// Mirrors disable-time cleanup during object destruction while stale readback and
        /// worker outputs are rejected by generation checks.
        /// </summary>
        private void OnDestroy()
        {
            _destroyed = true;
            Interlocked.Increment(ref _captureGeneration);
            _cleanupWhenReadbacksDrain = _pendingRequests > 0;
            StopVideoSidecar();
            StopJpegWorker(clearQueues: true);
            _jpegPublishPipeline?.Dispose();
            _jpegPublishPipeline = null;
            if (_pendingRequests == 0)
                CleanupResources();
            UnlockRuntimeOutputMode();
        }

        private void CompletePendingReadback()
        {
            _pendingRequests = Mathf.Max(0, _pendingRequests - 1);
            if (_pendingRequests == 0 && _cleanupWhenReadbacksDrain)
            {
                _cleanupWhenReadbacksDrain = false;
                CleanupResources();
            }
        }

        private bool HasSensorCompressedImageDemand()
            => HasSensorCompressedImageDemand(ActiveProfile);

        private bool HasSensorCompressedImageDemand(CameraVideoOutputProfile profile)
            => CameraSensorProfileResolver.HasCompressedImageDemand(
                profile.Mode == CameraOutputMode.Jpeg && _publishStandardRos2CompressedImage,
                SensorCompressedImageReady != null);

        private bool HasSensorRawImageDemand()
            => CameraSensorProfileResolver.HasRawImageDemand(
                IsStandardRos2RawImageOutput,
                SensorRawImageReady != null);

        private ulong ResolveCameraCaptureUnixNs()
            => _useSharedSensorClock && _manager != null
                ? _manager.GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)
                : CurrentLogTimeNs;

        private void WarnIfRuntimeOutputModeSwitchIgnored()
        {
            _outputModeRuntimeLock.ResolveWarning(_outputMode, Application.isPlaying, out var warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
        }

        private string ResolveFrameId()
            => CameraSensorProfileResolver.ResolveFrameId(_sensorUnitProfile, _frameId);

        private string ResolveSensorCameraImageTopic()
            => CameraSensorProfileResolver.ResolveImageTopic(_sensorUnitProfile, _topic);

        private string ResolveSensorCameraRawImageTopic()
            => CameraSensorProfileResolver.ResolveRawImageTopic(_sensorUnitProfile, _sensorCameraRawImageTopic);

        private ISensorCameraProfile ResolveSensorProfile()
            => CameraSensorProfileResolver.ResolveProfile(_sensorUnitProfile);

        private void ApplySensorProfileDefaults()
        {
            CameraSensorProfileResolver.ApplyDefaults(
                _sensorUnitProfile,
                _publishStandardRos2CompressedImage,
                _publishStandardRos2RawImage,
                ActiveProfile.DefaultTopic,
                _sensorCameraRawImageTopic,
                ref _topic,
                ref _sensorCameraRawImageTopic,
                ref _frameId);
        }

        private byte[] SerializeRos2CompressedImage(ulong unixNs, string frameId, byte[] jpeg)
            => CameraSensorProfileResolver.SerializeCompressedImage(
                _publishStandardRos2CompressedImage,
                unixNs,
                frameId,
                jpeg,
                "jpeg");

        private int DesiredVideoWidth => CameraVideoSidecarConfigFactory.ResolveDimension(_width);

        private int DesiredVideoHeight => CameraVideoSidecarConfigFactory.ResolveDimension(_height);

        /// <summary>
        /// Allocates Unity capture resources on the main thread using the current
        /// Inspector-requested dimensions before each readback snapshots the actual RT size.
        /// </summary>
        private void EnsureCaptureResources()
        {
            _captureResources.Ensure(this, transform, Math.Max(1, _width), Math.Max(1, _height));
        }

        /// <summary>
        /// Destroys Unity-owned capture resources only after local pending readbacks are
        /// drained or invalidated.
        /// </summary>
        private void CleanupResources()
        {
            _captureResources.Cleanup();
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;

        private void ResetBackpressureState()
            => _backpressureGate.Reset();

        private void LockRuntimeOutputMode()
        {
            _outputModeRuntimeLock.Lock(_outputMode);
        }

        private void UnlockRuntimeOutputMode()
        {
            _outputModeRuntimeLock.Unlock();
        }
    }
}
