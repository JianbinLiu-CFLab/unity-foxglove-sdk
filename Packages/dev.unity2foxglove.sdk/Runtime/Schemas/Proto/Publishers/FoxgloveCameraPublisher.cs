// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Captures camera frames via AsyncGPUReadback and publishes them
// as foxglove.CompressedImage JPEG frames or FFmpeg-backed foxglove.CompressedVideo frames.

using System;
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
    public class FoxgloveCameraPublisher : FoxglovePublisherBase
    {
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
        [Tooltip("Log CameraDiag timing and queue counters for the JPEG path.")]
        [SerializeField] private bool _logCameraDiagnostics;
        [Tooltip("Minimum seconds between CameraDiag log lines.")]
        [SerializeField, Min(0.1f)] private float _cameraDiagnosticsIntervalSeconds = 2f;

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

        [Header("Sensor Camera")]
        [Tooltip("Optional shared LiDAR/IMU/camera unit profile that owns SLAM frame IDs and topics.")]
        [SerializeField] private MonoBehaviour _sensorUnitProfile;
        [Tooltip("Use the manager shared sensor clock so camera frames align with IMU/LiDAR timestamps.")]
        [SerializeField] private bool _useSharedSensorClock = true;
        [Tooltip("Publish JPEG as the standard ROS2 compressed camera image schema when ROS2 encoding is selected.")]
        [SerializeField] private bool _publishStandardRos2CompressedImage;

        private readonly CameraOutputModeRuntimeLock _outputModeRuntimeLock = new CameraOutputModeRuntimeLock();

        private CameraOutputMode ResolvedOutputMode
        {
            get
            {
                var mode = _outputModeRuntimeLock.Resolve(_outputMode, Application.isPlaying, out var warning);
                if (!string.IsNullOrEmpty(warning))
                    Debug.LogWarning(warning);
                return mode;
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

        /// <summary>Whether this component is configured for standard ROS2 compressed image output.</summary>
        public bool IsStandardRos2CompressedImageOutput
            => ActiveProfile.Mode == CameraOutputMode.Jpeg && _publishStandardRos2CompressedImage;

        /// <summary>Resolved topic for the standard camera image stream.</summary>
        public string SensorCameraImageTopic => ResolveSensorCameraImageTopic();

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

        /// <summary>Defaults the topic to the current mode default if not set.</summary>
        private void Awake()
        {
            ApplySensorProfileDefaults();
            if (string.IsNullOrEmpty(_topic))
                _topic = ActiveProfile.DefaultTopic;
            EnsureJpegPublishPipeline();
            EnsureVideoPublishPipeline();
        }

        private CameraJpegPublishPipeline EnsureJpegPublishPipeline()
        {
            if (_jpegPublishPipeline == null)
                _jpegPublishPipeline = new CameraJpegPublishPipeline(() => _captureGeneration, _diagnostics);
            return _jpegPublishPipeline;
        }

        private CameraVideoPublishPipeline EnsureVideoPublishPipeline()
        {
            if (_videoPublishPipeline == null)
                _videoPublishPipeline = new CameraVideoPublishPipeline(_diagnostics, Debug.LogWarning);
            return _videoPublishPipeline;
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
            _captureGeneration++;
            ResetBackpressureState();
            ResetJpegPipelineState();
            ResetVideoDiagnosticState();
            EnsureCaptureResources();
            if (_useAsyncJpeg && ActiveProfile.Mode == CameraOutputMode.Jpeg)
                EnsureJpegWorkerStarted();
        }

        /// <summary>
        /// Schedules a camera capture only when cadence, demand, replay state,
        /// and readback limits allow useful payload work.
        /// </summary>
        private void LateUpdate()
        {
            var profile = ActiveProfile;
            DrainCompletedJpegFrames();
            DrainEncodedAccessUnits();
            if (!EnsureSidecarMatchesMode(profile))
                return;

            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            if (!profile.IsVideo && !AllowJpegCaptureByBackpressure()) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            var publishNativeFrame = HasSensorCompressedImageDemand();
            if (!publishWebSocket && !publishBridge && !publishNativeFrame) return;
            if (profile.IsVideo && !EnsureVideoSidecarStarted(profile)) return;
            if (!profile.IsVideo && !AllowJpegCaptureByFrameBudget())
            {
                EmitCameraDiagnosticsIfNeeded();
                return;
            }

            EnsureCaptureResources();
            var renderUnixNs = CurrentLogTimeNs;
            if (_useSharedSensorClock)
                renderUnixNs = ResolveCameraCaptureUnixNs();
            var renderStart = Stopwatch.GetTimestamp();
            _captureResources.CaptureCamera.Render();
            _diagnostics.RecordRenderMs(ElapsedMs(renderStart));
            // Snapshot the concrete render target size with the readback request. Inspector
            // width/height can change while this callback is in flight.
            var captureRenderTexture = _captureResources.CaptureRenderTexture;
            var generation = _captureGeneration;
            var captureWidth = captureRenderTexture.width;
            var captureHeight = captureRenderTexture.height;
            RememberReadbackStart(renderUnixNs, Stopwatch.GetTimestamp());
            _pendingRequests++;
            AsyncGPUReadback.Request(captureRenderTexture, 0, TextureFormat.RGB24, req => OnReadbackComplete(req, generation, renderUnixNs, captureWidth, captureHeight));
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
                if (_destroyed || !isActiveAndEnabled || generation != _captureGeneration) return;
                if (req.hasError)
                {
                    Debug.LogWarning("[Foxglove] Camera AsyncGPUReadback failed.");
                    return;
                }
                if (_manager == null) return;

                var profile = ActiveProfile;
                if (profile.IsVideo)
                {
                    SubmitVideoFrame(req, renderUnixNs, captureWidth, captureHeight);
                    return;
                }

                var publishWebSocket = ShouldPreparePublishPayload();
                var publishBridge = ShouldPrepareRos2BridgePayload();
                var publishNativeFrame = HasSensorCompressedImageDemand();
                if (!publishWebSocket && !publishBridge && !publishNativeFrame)
                {
                    _diagnostics.RecordNoDemandJpegDrop();
                    return;
                }

                if (_useAsyncJpeg && EnsureJpegWorkerStarted())
                {
                    QueueJpegFrame(req, renderUnixNs, captureWidth, captureHeight, publishWebSocket, publishBridge, publishNativeFrame, EffectiveEncoding, readbackLatencyMs);
                    return;
                }

                PublishJpegFrame(req, renderUnixNs, captureWidth, captureHeight);
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
            _captureGeneration++;
            _cleanupWhenReadbacksDrain = _pendingRequests > 0;
            StopVideoSidecar();
            StopJpegWorker(clearQueues: true);
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
            _captureGeneration++;
            StopVideoSidecar();
            StopJpegWorker(clearQueues: true);
            _cleanupWhenReadbacksDrain = _pendingRequests > 0;
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

        /// <summary>
        /// Applies static resource caps before rendering so camera visualization cannot
        /// consume unbounded readback or worker queue capacity.
        /// </summary>
        private bool AllowJpegCaptureByFrameBudget()
        {
            EnsureJpegPublishPipeline();
            return _jpegPublishPipeline.AllowCaptureByFrameBudget(
                _useAsyncJpeg,
                _pendingRequests,
                Math.Max(1, _maxPendingReadbacks),
                _jpegPublishPipeline.EncodeQueueDepth,
                _maxJpegEncodeQueue,
                _jpegPublishPipeline.CompletedQueueDepth,
                _maxCompletedJpegQueue,
                _width,
                _height,
                _maxPixelsPerFrame);
        }

        /// <summary>
        /// Copies readback bytes on the main thread into an owned buffer before handing
        /// work to the JPEG worker; the worker never touches Unity objects.
        /// </summary>
        private void QueueJpegFrame(
            AsyncGPUReadbackRequest req,
            ulong unixNs,
            int captureWidth,
            int captureHeight,
            bool publishWebSocket,
            bool publishBridge,
            bool publishNativeFrame,
            PublisherEffectiveEncoding webSocketEncoding,
            double readbackLatencyMs)
        {
            EnsureJpegPublishPipeline();
            var copyStart = Stopwatch.GetTimestamp();
            var frameBytes = req.GetData<byte>().ToArray();
            _jpegPublishPipeline.TryQueueFrame(
                frameBytes,
                unixNs,
                captureWidth,
                captureHeight,
                publishWebSocket,
                publishBridge,
                publishNativeFrame,
                webSocketEncoding,
                readbackLatencyMs,
                _jpegQuality,
                ResolveFrameId(),
                _publishStandardRos2CompressedImage,
                _maxEncodedBytes,
                onReadbackCopy: (latency, _) => _diagnostics.RecordReadbackCopy(latency, ElapsedMs(copyStart)),
                onEncodeQueueDrop: () => _diagnostics.RecordEncodeQueueDrop());
        }

        /// <summary>
        /// Publishes a bounded number of completed worker results per frame to keep
        /// worker catch-up from monopolizing the main loop.
        /// </summary>
        private void DrainCompletedJpegFrames()
        {
            EnsureJpegPublishPipeline();
            var drained = _jpegPublishPipeline.DrainCompleted(
                _maxCompletedJpegPublishesPerFrame,
                PublishCompletedJpegFrame,
                out var droppedCompleted,
                out var elapsedMs);
            if (elapsedMs > 0)
                _diagnostics.RecordPublishDrainMs(elapsedMs);
            if (droppedCompleted > 0)
                _diagnostics.RecordCompletedJpegDrops(droppedCompleted);

            EmitCameraDiagnosticsIfNeeded();
        }

        /// <summary>
        /// Rejects stale or out-of-order worker results before publishing the freshest
        /// serialized JPEG payloads.
        /// </summary>
        private void PublishCompletedJpegFrame(JpegEncodeResult result)
        {
            if (result.Request.Generation != _captureGeneration)
                return;

            var captureUnixNs = result.Request.CaptureUnixNs;
            if (!CameraJpegPublishOrderPolicy.ShouldPublish(captureUnixNs, _lastPublishedCaptureUnixNs))
            {
                _diagnostics.RecordLateJpegDrop();
                return;
            }

            _diagnostics.RecordJpegEncodeResult(result.EncodeMs, result.SerializeMs, result.JpegBytes);

            if (result.DroppedByEncodedBudget)
            {
                _diagnostics.RecordEncodedBudgetDrop();
                EmitBackpressureWarning(
                    $"[Foxglove] Camera frame dropped: encoded size {result.JpegBytes} exceeds budget {result.Request.MaxEncodedBytes}.");
                return;
            }

            if (!result.Success)
            {
                LogJpegWorkerFailure(result.Error);
                return;
            }

            if (result.Request.PublishNativeFrame && result.SensorFrame != null)
            {
                SensorCompressedImageReady?.Invoke(result.SensorFrame);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProto(result.WebSocketPayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }
            else if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
            {
                PublishRos2(result.WebSocketPayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }
            else if (result.Request.PublishWebSocket)
            {
                Publish(result.JsonMessage, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            if (result.Request.PublishBridge)
            {
                PublishRos2Bridge(result.BridgePayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            EnsureJpegPublishPipeline().ResetWorkerFailure();
        }

        /// <summary>
        /// Synchronous JPEG fallback path; it still uses captured readback dimensions
        /// instead of mutable Inspector dimensions.
        /// </summary>
        private void PublishJpegFrame(AsyncGPUReadbackRequest req, ulong unixNs, int captureWidth, int captureHeight)
        {
            var jpeg = _captureResources.EncodeJpeg(req, captureWidth, captureHeight, _jpegQuality);
            if (jpeg == null || jpeg.Length == 0) return;

            if (CameraBackpressurePolicy.ExceedsBudget(jpeg, _maxEncodedBytes))
            {
                EmitBackpressureWarning(
                    $"[Foxglove] Camera frame dropped: encoded size {jpeg.Length} exceeds budget {_maxEncodedBytes}.");
                return;
            }

            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            var publishNativeFrame = HasSensorCompressedImageDemand();
            var frameId = ResolveFrameId();
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                var payload = CameraCompressedImageBuilder.Serialize(unixNs, frameId, jpeg, "jpeg");
                PublishProto(payload, unixNs);
                _backpressureGate.ResetSkipLogCount();
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = SerializeRos2CompressedImage(unixNs, frameId, jpeg);
                PublishRos2(ros2Payload, unixNs);
                _backpressureGate.ResetSkipLogCount();
            }
            else if (publishWebSocket)
            {
                var msg = new CompressedImageMessage
                {
                    Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(unixNs),
                    FrameId = frameId,
                    Data = Convert.ToBase64String(jpeg),
                    Format = "jpeg"
                };

                Publish(msg, unixNs);
                _backpressureGate.ResetSkipLogCount();
            }

            if (publishBridge)
            {
                ros2Payload ??= SerializeRos2CompressedImage(unixNs, frameId, jpeg);
                PublishRos2Bridge(ros2Payload, unixNs);
                _backpressureGate.ResetSkipLogCount();
            }

            if (publishNativeFrame)
            {
                SensorCompressedImageReady?.Invoke(new SensorCompressedImageFrame(unixNs, frameId, jpeg, "jpeg"));
                _lastPublishedCaptureUnixNs = unixNs;
                _backpressureGate.ResetSkipLogCount();
            }
        }

        private bool HasSensorCompressedImageDemand()
            => CameraSensorProfileResolver.HasCompressedImageDemand(
                IsStandardRos2CompressedImageOutput,
                SensorCompressedImageReady != null);

        private ulong ResolveCameraCaptureUnixNs()
            => _useSharedSensorClock && _manager != null
                ? _manager.GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)
                : CurrentLogTimeNs;

        private string ResolveFrameId()
            => CameraSensorProfileResolver.ResolveFrameId(_sensorUnitProfile, _frameId);

        private string ResolveSensorCameraImageTopic()
            => CameraSensorProfileResolver.ResolveImageTopic(_sensorUnitProfile, _topic);

        private ISensorCameraProfile ResolveSensorProfile()
            => CameraSensorProfileResolver.ResolveProfile(_sensorUnitProfile);

        private void ApplySensorProfileDefaults()
        {
            CameraSensorProfileResolver.ApplyDefaults(
                _sensorUnitProfile,
                _publishStandardRos2CompressedImage,
                ActiveProfile.DefaultTopic,
                ref _topic,
                ref _frameId);
        }

        private byte[] SerializeRos2CompressedImage(ulong unixNs, string frameId, byte[] jpeg)
            => CameraSensorProfileResolver.SerializeCompressedImage(
                _publishStandardRos2CompressedImage,
                unixNs,
                frameId,
                jpeg,
                "jpeg");

        /// <summary>
        /// Submits a rendered camera frame to the active video sidecar using the
        /// dimensions captured with the same readback request.
        /// </summary>
        private void SubmitVideoFrame(AsyncGPUReadbackRequest req, ulong renderUnixNs, int captureWidth, int captureHeight)
        {
            var readbackData = req.GetData<byte>();
            EnsureVideoPublishPipeline();
            var result = _videoPublishPipeline.SubmitVideoFrame(
                () => readbackData.ToArray(),
                readbackData.Length,
                renderUnixNs,
                captureWidth,
                captureHeight);

            if (result.Submitted)
            {
                DrainEncodedAccessUnits();
                return;
            }

            switch (result.Outcome)
            {
                case CameraVideoSubmitOutcome.DimensionMismatch:
                    RecordVideoDimensionMismatchDrop(result.Reason);
                    break;
                case CameraVideoSubmitOutcome.FrameDataMissing:
                    EmitVideoDiagnosticsIfNeeded();
                    LogVideoEncoderUnavailable(ActiveProfile, result.Reason);
                    break;
                default:
                    EmitVideoDiagnosticsIfNeeded();
                    LogVideoEncoderUnavailable(ActiveProfile, result.Reason);
                    break;
            }
        }

        /// <summary>
        /// Starts explicit video modes only; video setup failure never falls through into
        /// extra JPEG work during the same publish tick.
        /// </summary>
        private bool EnsureVideoSidecarStarted(CameraVideoOutputProfile profile)
        {
            if (!profile.IsVideo)
                return false;

            EnsureVideoPublishPipeline();
            if (_videoPublishPipeline.EnsureVideoSidecarStarted(
                profile,
                CameraVideoSidecarConfigFactory.Create(
                    _ffmpegPath,
                    _openH264HelperPath,
                    _openH264DllPath,
                    _width,
                    _height,
                    EffectivePublishRateHz,
                    _videoBitrateKbps,
                    _videoKeyframeInterval,
                    Math.Max(1, _maxPendingReadbacks),
                    _openH264MaxInputQueue,
                    _videoMaxOutputQueue),
                DrainEncodedAccessUnits,
                out var error))
            {
                _diagnostics.ResetVideoDimensionMismatchWarning();
                return true;
            }

            LogVideoEncoderUnavailable(profile, error);
            return false;
        }

        private int DesiredVideoWidth => CameraVideoSidecarConfigFactory.ResolveDimension(_width);

        private int DesiredVideoHeight => CameraVideoSidecarConfigFactory.ResolveDimension(_height);

        private void DrainEncodedAccessUnits()
        {
            EnsureVideoPublishPipeline();
            if (!_videoPublishPipeline.TryDrainEncodedAccessUnits(
                () => CurrentLogTimeNs,
                PublishVideoAccessUnit,
                sidecar => LogEncoderStderrIfNeeded(sidecar),
                out var elapsedMs))
            {
                return;
            }

            _diagnostics.RecordVideoDrainMs(elapsedMs);
            EmitVideoDiagnosticsIfNeeded();
        }

        private void PublishVideoAccessUnit(byte[] accessUnit, ulong unixNs, string videoFormat)
        {
            if (accessUnit == null || accessUnit.Length == 0)
                return;

            if (unixNs == 0UL)
                unixNs = CurrentLogTimeNs;
            var payload = CameraCompressedVideoBuilder.Serialize(
                unixNs,
                ResolveFrameId(),
                accessUnit,
                videoFormat);
            PublishProto(payload, unixNs);
            _diagnostics.RecordVideoAccessUnitPublished(accessUnit.Length);
        }

        private void StopVideoSidecar()
        {
            EnsureVideoPublishPipeline();
            _videoPublishPipeline.StopVideoSidecar(DrainEncodedAccessUnits);
        }

        /// <summary>
        /// Keeps the running sidecar aligned with the locked mode and requested
        /// dimensions, debouncing restarts while Inspector edits settle.
        /// </summary>
        private bool EnsureSidecarMatchesMode(CameraVideoOutputProfile profile)
        {
            EnsureVideoPublishPipeline();
            var result = _videoPublishPipeline.EnsureSidecarMatchesMode(
                profile,
                DesiredVideoWidth,
                DesiredVideoHeight,
                Time.unscaledTimeAsDouble,
                DrainEncodedAccessUnits);
            if (result.ResetEncoderWarning)
                _videoPublishPipeline.ResetVideoEncoderWarning();

            if (!string.IsNullOrEmpty(result.Diagnostic))
                _diagnostics.RecordVideoDiagnostic(result.Diagnostic);

            if (result.DroppedWhilePending)
            {
                _diagnostics.RecordVideoDimensionMismatchDrop(result.Diagnostic, warnOnce: false);
                EmitVideoDiagnosticsIfNeeded();
                return false;
            }

            if (result.Restarted)
            {
                _diagnostics.RecordVideoSidecarRestart();
                EmitVideoDiagnosticsIfNeeded();
            }

            return result.AllowCapture;
        }
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

        private void EnsureJpegQueues()
        {
            _jpegPublishPipeline?.EnsureQueues(_maxJpegEncodeQueue, _maxCompletedJpegQueue);
        }

        /// <summary>
        /// Lazily starts the background JPEG worker after demand and budget gates pass.
        /// </summary>
        private bool EnsureJpegWorkerStarted()
        {
            EnsureJpegPublishPipeline();
            return _jpegPublishPipeline.EnsureWorkerStarted(reason =>
                {
                    if (!string.IsNullOrEmpty(reason))
                        LogJpegWorkerFailure(reason);
                });
        }

        /// <summary>
        /// Requests worker shutdown without blocking Play Mode indefinitely; late output is
        /// discarded by queue clearing and generation checks.
        /// </summary>
        private void StopJpegWorker(bool clearQueues)
        {
            _jpegPublishPipeline?.StopWorker(
                clearQueues,
                reason => Debug.LogWarning(reason));
        }

        private void ClearJpegQueues()
        {
            _jpegPublishPipeline?.ClearQueues();
        }

        private void ClearReadbackTiming()
        {
            _jpegPublishPipeline?.ClearReadbackTiming();
        }

        private void ResetJpegPipelineState()
        {
            EnsureJpegPublishPipeline().ResetState();
            _lastPublishedCaptureUnixNs = 0;
            _diagnostics.ResetCameraState();
        }

        /// <summary>
        /// Tracks readback latency for diagnostics without making timing data part of the
        /// publish contract.
        /// </summary>
        private void RememberReadbackStart(ulong unixNs, long ticks)
        {
            EnsureJpegPublishPipeline();
            _jpegPublishPipeline.RememberReadbackStart(unixNs, ticks);
        }

        private double TakeReadbackLatencyMs(ulong unixNs)
        {
            EnsureJpegPublishPipeline();
            return _jpegPublishPipeline.TakeReadbackLatencyMs(unixNs);
        }

        private void LogJpegWorkerFailure(string reason)
        {
            EnsureJpegPublishPipeline();
            _jpegPublishPipeline.TryLogWorkerFailure(msg =>
            {
                if (!string.IsNullOrWhiteSpace(msg))
                    Debug.LogWarning(msg);
            }, reason);
        }

        /// <summary>
        /// Reports render, readback, encode, serialization and queue pressure separately
        /// so camera cost can be attributed before future pipeline changes.
        /// </summary>
        private void EmitCameraDiagnosticsIfNeeded()
        {
            _diagnostics.LogCameraIfNeeded(
                _logCameraDiagnostics,
                Time.unscaledTimeAsDouble,
                _cameraDiagnosticsIntervalSeconds,
                _pendingRequests,
                _jpegPublishPipeline?.EncodeQueueDepth ?? 0,
                _jpegPublishPipeline?.CompletedQueueDepth ?? 0,
                out var message);
            if (message != null)
                Debug.Log(message);
        }

        /// <summary>
        /// Drops one stale or mismatched video frame and records the reason for diagnostics.
        /// </summary>
        private void RecordVideoDimensionMismatchDrop(string reason)
        {
            if (_diagnostics.RecordVideoDimensionMismatchDrop(reason, warnOnce: true))
                Debug.LogWarning("[Foxglove] Camera video frame dropped: " + reason);

            EmitVideoDiagnosticsIfNeeded();
        }

        /// <summary>
        /// Reports video submission and drain evidence separately from JPEG diagnostics.
        /// </summary>
        private void EmitVideoDiagnosticsIfNeeded()
        {
            EnsureVideoPublishPipeline();
            var profile = CameraVideoOutputProfile.ForMode(_videoPublishPipeline.Mode);
            _diagnostics.LogVideoIfNeeded(
                _logVideoDiagnostics,
                Time.unscaledTimeAsDouble,
                _cameraDiagnosticsIntervalSeconds,
                profile.DisplayName,
                _videoPublishPipeline.SidecarWidth,
                _videoPublishPipeline.SidecarHeight,
                _pendingRequests,
                out var message);
            if (message != null)
                Debug.Log(message);
        }

        /// <summary>
        /// Clears video-specific diagnostics state on enable.
        /// </summary>
        private void ResetVideoDiagnosticState()
        {
            EnsureVideoPublishPipeline().ResetState();
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;

        /// <summary>
        /// Optional transport-drop cooldown for legacy behavior; the 138J path relies on
        /// static resource caps rather than frame-time feedback control.
        /// </summary>
        private bool AllowJpegCaptureByBackpressure()
        {
            if (!_enableBackpressureAdaptation)
                return _backpressureGate.AllowCapture(
                    enabled: false,
                    statsSupported: false,
                    totalDroppedDataFrames: 0,
                    currentTimeSec: 0,
                    cooldownSeconds: _backpressureCooldownSeconds,
                    logSkips: _logBackpressureSkips,
                    warning: out _);

            var stats = _manager.GetTransportStatsSnapshot();
            var allowCapture = _backpressureGate.AllowCapture(
                _enableBackpressureAdaptation,
                stats.Supported,
                stats.TotalDroppedDataFrames,
                Time.unscaledTimeAsDouble,
                _backpressureCooldownSeconds,
                _logBackpressureSkips,
                out var warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
            return allowCapture;
        }

        private void ResetBackpressureState()
            => _backpressureGate.Reset();

        private void EmitBackpressureWarning(string message)
        {
            if (_backpressureGate.TryRecordSkipWarning(_logBackpressureSkips, message, out var warning))
                Debug.LogWarning(warning);
        }

        private void LogVideoEncoderUnavailable(CameraVideoOutputProfile profile, string reason)
        {
            EnsureVideoPublishPipeline();
            _videoPublishPipeline.TryLogVideoEncoderUnavailable(profile, reason);
        }

        private void LockRuntimeOutputMode()
        {
            _outputModeRuntimeLock.Lock(_outputMode);
        }

        private void UnlockRuntimeOutputMode()
        {
            _outputModeRuntimeLock.Unlock();
        }

        private void LogEncoderStderrIfNeeded(ICameraVideoEncoderSidecar sidecar)
        {
            if (!_logEncoderStderr || sidecar == null)
                return;

            EnsureVideoPublishPipeline();
            _videoPublishPipeline.LogEncoderStderrIfNeeded(
                _logEncoderStderr,
                sidecar,
                CameraVideoOutputProfile.ForMode(_videoPublishPipeline.Mode).DisplayName);
        }

    }
}
