// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Captures camera frames via AsyncGPUReadback and publishes them
// as foxglove.CompressedImage JPEG frames or FFmpeg-backed foxglove.CompressedVideo frames.

using System;
using System.Collections.Generic;
using System.Threading;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Schemas;
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

        private CameraOutputMode _runtimeOutputMode;
        private bool _runtimeOutputModeInitialized;
        private bool _warnedRuntimeOutputModeSwitch;

        private CameraOutputMode ResolvedOutputMode
        {
            get
            {
                if (Application.isPlaying && _runtimeOutputModeInitialized)
                {
                    if (_outputMode != _runtimeOutputMode)
                        WarnRuntimeOutputModeSwitch();
                    return _runtimeOutputMode;
                }

                return _outputMode;
            }
        }

        private CameraVideoOutputProfile ActiveProfile => CameraVideoOutputProfile.ForMode(ResolvedOutputMode);

        protected override string SchemaName => ActiveProfile.SchemaName;
        public override bool SupportsJsonEncoding => ActiveProfile.SupportsJson;
        public override bool SupportsProtobufEncoding => ActiveProfile.SupportsProtobuf;
        public override bool SupportsRos2Encoding => ActiveProfile.Mode == CameraOutputMode.Jpeg;
        protected override string Ros2SchemaName => ActiveProfile.Mode == CameraOutputMode.Jpeg
            ? Ros2PublisherSchemaNames.CompressedImage
            : "";

        // Capture state
        private Camera _sourceCam;
        private Camera _captureCam;
        private RenderTexture _captureRT;
        private Texture2D _texture2D;
        private int _pendingRequests;
        private bool _destroyed;
        private int _captureGeneration;
        private bool _cleanupWhenReadbacksDrain;
        private readonly object _readbackTimingGate = new object();
        private readonly Dictionary<ulong, long> _readbackRequestTicks = new Dictionary<ulong, long>();

        // Video sidecar state
        private ICameraVideoEncoderSidecar _videoSidecar;
        private CameraOutputMode _videoSidecarMode = CameraOutputMode.Jpeg;
        private bool _warnedVideoEncoderUnavailable;
        private string _lastLoggedStderr;

        // JPEG backpressure state
        private long _lastDropCount;
        private double _cooldownUntilSec;
        private int _backpressureSkipLogCount;
        private bool _backpressureBaselineInitialized;

        // Async JPEG state
        private const int JpegWorkerStopWaitMs = 500;
        private DropOldestBoundedQueue<JpegEncodeRequest> _jpegEncodeQueue;
        private DropOldestBoundedQueue<JpegEncodeResult> _completedJpegQueue;
        private AutoResetEvent _jpegWorkerSignal;
        private Thread _jpegWorker;
        private volatile bool _jpegWorkerStopping;
        private ulong _lastPublishedCaptureUnixNs;
        private bool _warnedJpegWorkerFailure;
        private bool _warnedJpegWorkerShutdown;
        private double _nextCameraDiagLogSec;
        private double _lastRenderMs;
        private double _lastReadbackLatencyMs;
        private double _lastReadbackCopyMs;
        private double _lastJpegEncodeMs;
        private double _lastSerializeMs;
        private double _lastPublishDrainMs;
        private int _lastJpegBytes;
        private int _readbackBudgetSkipCount;
        private int _encodeBudgetSkipCount;
        private int _completedBudgetSkipCount;
        private int _pixelBudgetSkipCount;
        private int _noDemandJpegDropCount;
        private int _droppedEncodeQueueCount;
        private int _droppedCompletedJpegCount;
        private int _droppedEncodedBudgetCount;
        private int _droppedLateJpegCount;

        /// <summary>Defaults the topic to the current mode default if not set.</summary>
        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic))
                _topic = ActiveProfile.DefaultTopic;
        }

        /// <summary>
        /// Locks schema-affecting camera mode before registration so Play Mode does not
        /// advertise one topic/schema and publish another after an Inspector change.
        /// </summary>
        protected override void OnEnable()
        {
            LockRuntimeOutputMode();
            base.OnEnable();
            _destroyed = false;
            _cleanupWhenReadbacksDrain = false;
            _captureGeneration++;
            _warnedVideoEncoderUnavailable = false;
            _lastLoggedStderr = null;
            ResetBackpressureState();
            ResetJpegPipelineState();
            _sourceCam = GetComponent<Camera>();
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
            EnsureSidecarMatchesMode(profile);
            DrainCompletedJpegFrames();
            DrainEncodedAccessUnits();

            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            if (!profile.IsVideo && !AllowJpegCaptureByBackpressure()) return;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge) return;
            if (profile.IsVideo && !EnsureVideoSidecarStarted(profile)) return;
            if (!profile.IsVideo && !AllowJpegCaptureByFrameBudget())
            {
                LogCameraDiagnosticsIfNeeded();
                return;
            }

            EnsureCaptureResources();
            var renderUnixNs = CurrentLogTimeNs;
            var renderStart = Stopwatch.GetTimestamp();
            _captureCam.Render();
            _lastRenderMs = ElapsedMs(renderStart);
            // Snapshot the concrete render target size with the readback request. Inspector
            // width/height can change while this callback is in flight.
            var generation = _captureGeneration;
            var captureWidth = _captureRT.width;
            var captureHeight = _captureRT.height;
            RememberReadbackStart(renderUnixNs, Stopwatch.GetTimestamp());
            _pendingRequests++;
            AsyncGPUReadback.Request(_captureRT, 0, TextureFormat.RGB24, req => OnReadbackComplete(req, generation, renderUnixNs, captureWidth, captureHeight));
        }

        /// <summary>
        /// Completes one local readback request and routes it using the generation and
        /// dimensions captured when the request was issued.
        /// </summary>
        private void OnReadbackComplete(AsyncGPUReadbackRequest req, int generation, ulong renderUnixNs, int captureWidth, int captureHeight)
        {
            var readbackLatencyMs = TakeReadbackLatencyMs(renderUnixNs);
            CompletePendingReadback();

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
                SubmitVideoFrame(req, renderUnixNs);
                return;
            }

            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            if (!publishWebSocket && !publishBridge)
            {
                _noDemandJpegDropCount++;
                return;
            }

            if (_useAsyncJpeg && EnsureJpegWorkerStarted())
            {
                QueueJpegFrame(req, renderUnixNs, captureWidth, captureHeight, publishWebSocket, publishBridge, EffectiveEncoding, readbackLatencyMs);
                return;
            }

            PublishJpegFrame(req, renderUnixNs, captureWidth, captureHeight);
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
            EnsureJpegQueues();
            var result = CameraFrameBudgetPolicy.Evaluate(new CameraFrameBudgetInput
            {
                PendingReadbacks = _pendingRequests,
                MaxPendingReadbacks = Math.Max(1, _maxPendingReadbacks),
                EncodeQueueDepth = _useAsyncJpeg ? (_jpegEncodeQueue?.Count ?? 0) : 0,
                MaxEncodeQueueDepth = _useAsyncJpeg ? Math.Max(1, _maxJpegEncodeQueue) : int.MaxValue,
                CompletedQueueDepth = _useAsyncJpeg ? (_completedJpegQueue?.Count ?? 0) : 0,
                MaxCompletedQueueDepth = _useAsyncJpeg ? Math.Max(1, _maxCompletedJpegQueue) : int.MaxValue,
                Width = Math.Max(1, _width),
                Height = Math.Max(1, _height),
                MaxPixelsPerFrame = Math.Max(0, _maxPixelsPerFrame)
            });

            if (result.AllowCapture)
                return true;

            RecordCameraBudgetSkip(result.SkipReason);
            return false;
        }

        private void RecordCameraBudgetSkip(CameraFrameBudgetSkipReason reason)
        {
            switch (reason)
            {
                case CameraFrameBudgetSkipReason.ReadbackQueueFull:
                    _readbackBudgetSkipCount++;
                    break;
                case CameraFrameBudgetSkipReason.EncodeQueueFull:
                    _encodeBudgetSkipCount++;
                    break;
                case CameraFrameBudgetSkipReason.CompletedQueueFull:
                    _completedBudgetSkipCount++;
                    break;
                case CameraFrameBudgetSkipReason.PixelBudgetExceeded:
                    _pixelBudgetSkipCount++;
                    break;
            }
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
            PublisherEffectiveEncoding webSocketEncoding,
            double readbackLatencyMs)
        {
            EnsureJpegQueues();
            var copyStart = Stopwatch.GetTimestamp();
            var frameBytes = req.GetData<byte>().ToArray();
            _lastReadbackLatencyMs = readbackLatencyMs;
            _lastReadbackCopyMs = ElapsedMs(copyStart);

            var request = new JpegEncodeRequest(
                frameBytes,
                Math.Max(1, captureWidth),
                Math.Max(1, captureHeight),
                Mathf.Clamp(_jpegQuality, 10, 100),
                unixNs,
                _frameId,
                publishWebSocket,
                publishBridge,
                webSocketEncoding,
                Math.Max(0, _maxEncodedBytes),
                _captureGeneration);

            if (_jpegEncodeQueue.Enqueue(request))
                _droppedEncodeQueueCount++;

            _jpegWorkerSignal?.Set();
        }

        /// <summary>
        /// Publishes a bounded number of completed worker results per frame to keep
        /// worker catch-up from monopolizing the main loop.
        /// </summary>
        private void DrainCompletedJpegFrames()
        {
            var queue = _completedJpegQueue;
            if (queue == null)
                return;

            var drainStart = Stopwatch.GetTimestamp();
            var maxDrain = Math.Max(1, _maxCompletedJpegPublishesPerFrame);
            var drained = 0;
            while (drained < maxDrain && queue.TryDequeue(out var result))
            {
                drained++;
                PublishCompletedJpegFrame(result);
            }

            if (drained > 0)
                _lastPublishDrainMs = ElapsedMs(drainStart);

            LogCameraDiagnosticsIfNeeded();
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
                _droppedLateJpegCount++;
                return;
            }

            _lastJpegEncodeMs = result.EncodeMs;
            _lastSerializeMs = result.SerializeMs;
            _lastJpegBytes = result.JpegBytes;

            if (result.DroppedByEncodedBudget)
            {
                _droppedEncodedBudgetCount++;
                LogBackpressureSkip(
                    $"[Foxglove] Camera frame dropped: encoded size {result.JpegBytes} exceeds budget {result.Request.MaxEncodedBytes}.");
                return;
            }

            if (!result.Success)
            {
                LogJpegWorkerFailure(result.Error);
                return;
            }

            if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProto(result.WebSocketPayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureSkipLogCount = 0;
            }
            else if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
            {
                PublishRos2(result.WebSocketPayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureSkipLogCount = 0;
            }
            else if (result.Request.PublishWebSocket)
            {
                Publish(result.JsonMessage, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureSkipLogCount = 0;
            }

            if (result.Request.PublishBridge)
            {
                PublishRos2Bridge(result.BridgePayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureSkipLogCount = 0;
            }

            _warnedJpegWorkerFailure = false;
        }

        /// <summary>
        /// Synchronous JPEG fallback path; it still uses captured readback dimensions
        /// instead of mutable Inspector dimensions.
        /// </summary>
        private void PublishJpegFrame(AsyncGPUReadbackRequest req, ulong unixNs, int captureWidth, int captureHeight)
        {
            captureWidth = Math.Max(1, captureWidth);
            captureHeight = Math.Max(1, captureHeight);
            if (_texture2D == null || _texture2D.width != captureWidth || _texture2D.height != captureHeight)
            {
                if (_texture2D != null)
                    Destroy(_texture2D);

                _texture2D = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
            }

            if (_texture2D == null)
                return;

            var data = req.GetData<byte>();
            _texture2D.LoadRawTextureData(data);
            _texture2D.Apply(false);

            var jpeg = _texture2D.EncodeToJPG(_jpegQuality);
            if (jpeg == null || jpeg.Length == 0) return;

            if (CameraBackpressurePolicy.ExceedsBudget(jpeg, _maxEncodedBytes))
            {
                LogBackpressureSkip(
                    $"[Foxglove] Camera frame dropped: encoded size {jpeg.Length} exceeds budget {_maxEncodedBytes}.");
                return;
            }

            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                var payload = CameraCompressedImageBuilder.Serialize(unixNs, _frameId, jpeg, "jpeg");
                PublishProto(payload, unixNs);
                _backpressureSkipLogCount = 0;
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrCompressedImageBuilder.Serialize(unixNs, _frameId, jpeg, "jpeg");
                PublishRos2(ros2Payload, unixNs);
                _backpressureSkipLogCount = 0;
            }
            else if (publishWebSocket)
            {
                var msg = new CompressedImageMessage
                {
                    Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(unixNs),
                    FrameId = _frameId,
                    Data = Convert.ToBase64String(jpeg),
                    Format = "jpeg"
                };

                Publish(msg, unixNs);
                _backpressureSkipLogCount = 0;
            }

            if (publishBridge)
            {
                ros2Payload ??= Ros2CdrCompressedImageBuilder.Serialize(unixNs, _frameId, jpeg, "jpeg");
                PublishRos2Bridge(ros2Payload, unixNs);
                _backpressureSkipLogCount = 0;
            }
        }

        private void SubmitVideoFrame(AsyncGPUReadbackRequest req, ulong renderUnixNs)
        {
            var sidecar = _videoSidecar;
            if (sidecar == null)
            {
                LogVideoEncoderUnavailable(ActiveProfile, "Video encoder is not running.");
                return;
            }

            if (!sidecar.IsRunning)
            {
                LogVideoEncoderUnavailable(ActiveProfile, DescribeVideoEncoderFailure(sidecar, "Video encoder process exited."));
                return;
            }

            var frameBytes = req.GetData<byte>().ToArray();
            if (_videoSidecarMode == CameraOutputMode.H264OpenH264)
            {
                var i420 = new byte[Math.Max(1, _width) * Math.Max(1, _height) * 3 / 2];
                if (!Rgb24ToI420Converter.TryConvertRgb24ToI420(
                        frameBytes,
                        Math.Max(1, _width),
                        Math.Max(1, _height),
                        i420,
                        flipVertical: true,
                        out var conversionError))
                {
                    LogVideoEncoderUnavailable(ActiveProfile, conversionError);
                    return;
                }

                frameBytes = i420;
            }

            var submitted = sidecar is ITimestampedCameraVideoEncoderSidecar timestampedSidecar
                ? timestampedSidecar.TrySubmitFrame(frameBytes, renderUnixNs)
                : sidecar.TrySubmitFrame(frameBytes);
            if (!submitted)
            {
                LogVideoEncoderUnavailable(ActiveProfile, DescribeVideoEncoderFailure(sidecar, "Video encoder refused the frame."));
                return;
            }

            DrainEncodedAccessUnits();
        }

        /// <summary>
        /// Starts explicit video modes only; video setup failure never falls through into
        /// extra JPEG work during the same publish tick.
        /// </summary>
        private bool EnsureVideoSidecarStarted(CameraVideoOutputProfile profile)
        {
            if (!profile.IsVideo)
                return false;

            if (_videoSidecar != null && _videoSidecar.IsRunning && _videoSidecarMode == profile.Mode)
                return true;

            StopVideoSidecar();
            _videoSidecarMode = profile.Mode;

            var started = false;
            switch (profile.Codec)
            {
                case CameraVideoCodec.H264 when profile.Mode == CameraOutputMode.H264Ffmpeg:
                    var h264 = new FfmpegH264EncoderSidecar();
                    _videoSidecar = h264;
                    started = h264.Start(CreateH264Options());
                    break;
                case CameraVideoCodec.H264 when profile.Mode == CameraOutputMode.H264OpenH264:
                    var openH264 = new OpenH264EncoderSidecar();
                    _videoSidecar = openH264;
                    started = openH264.Start(CreateOpenH264Options());
                    break;
                case CameraVideoCodec.H264 when profile.Mode == CameraOutputMode.H264MediaFoundationExperimental:
                    var nativeH264 = new MediaFoundationH264EncoderSidecar();
                    _videoSidecar = nativeH264;
                    started = nativeH264.Start(CreateMediaFoundationH264Options());
                    break;
                case CameraVideoCodec.H265:
                    var h265 = new FfmpegH265EncoderSidecar();
                    _videoSidecar = h265;
                    started = h265.Start(CreateH265Options());
                    break;
            }

            if (started)
            {
                _warnedVideoEncoderUnavailable = false;
                return true;
            }

            LogVideoEncoderUnavailable(profile, _videoSidecar?.LastError ?? "Failed to start video encoder.");
            StopVideoSidecar();
            return false;
        }

        private FfmpegH264EncoderOptions CreateH264Options()
        {
            return new FfmpegH264EncoderOptions
            {
                FfmpegPath = string.IsNullOrWhiteSpace(_ffmpegPath) ? "ffmpeg" : _ffmpegPath,
                Width = Math.Max(1, _width),
                Height = Math.Max(1, _height),
                FrameRate = ResolveEncoderFrameRate(),
                BitrateKbps = Math.Max(1, _videoBitrateKbps),
                KeyframeInterval = Math.Max(1, _videoKeyframeInterval),
                MaxInputQueue = Math.Max(1, _maxPendingReadbacks),
                MaxOutputQueue = Math.Max(1, _videoMaxOutputQueue)
            };
        }

        private FfmpegH265EncoderOptions CreateH265Options()
        {
            return new FfmpegH265EncoderOptions
            {
                FfmpegPath = string.IsNullOrWhiteSpace(_ffmpegPath) ? "ffmpeg" : _ffmpegPath,
                Width = Math.Max(1, _width),
                Height = Math.Max(1, _height),
                FrameRate = ResolveEncoderFrameRate(),
                BitrateKbps = Math.Max(1, _videoBitrateKbps),
                KeyframeInterval = Math.Max(1, _videoKeyframeInterval),
                MaxInputQueue = Math.Max(1, _maxPendingReadbacks),
                MaxOutputQueue = Math.Max(1, _videoMaxOutputQueue)
            };
        }

        private OpenH264EncoderOptions CreateOpenH264Options()
        {
            return new OpenH264EncoderOptions
            {
                HelperExecutablePath = _openH264HelperPath,
                OpenH264DllPath = _openH264DllPath,
                Width = Math.Max(1, _width),
                Height = Math.Max(1, _height),
                FrameRate = ResolveEncoderFrameRate(),
                BitrateKbps = Math.Max(1, _videoBitrateKbps),
                KeyframeInterval = Math.Max(1, _videoKeyframeInterval),
                MaxInputQueue = Math.Max(1, _openH264MaxInputQueue),
                MaxOutputQueue = Math.Max(1, _videoMaxOutputQueue)
            };
        }

        private MediaFoundationH264EncoderOptions CreateMediaFoundationH264Options()
        {
            return new MediaFoundationH264EncoderOptions
            {
                Width = Math.Max(1, _width),
                Height = Math.Max(1, _height),
                FrameRate = ResolveEncoderFrameRate(),
                BitrateKbps = Math.Max(1, _videoBitrateKbps),
                KeyframeInterval = Math.Max(1, _videoKeyframeInterval),
                MaxInputQueue = Math.Max(1, _maxPendingReadbacks),
                MaxOutputQueue = Math.Max(1, _videoMaxOutputQueue)
            };
        }

        private void DrainEncodedAccessUnits()
        {
            var sidecar = _videoSidecar;
            if (sidecar == null)
                return;

            var profile = CameraVideoOutputProfile.ForMode(_videoSidecarMode);
            var videoFormat = profile.Codec == CameraVideoCodec.H264
                ? CameraCompressedVideoBuilder.H264Format
                : profile.Codec == CameraVideoCodec.H265
                    ? CameraCompressedVideoBuilder.H265Format
                    : profile.VideoFormat;
            if (sidecar is ITimestampedCameraVideoEncoderSidecar timestampedSidecar)
            {
                while (timestampedSidecar.TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit))
                    PublishVideoAccessUnit(accessUnit.Data, accessUnit.TimestampNs, videoFormat);
            }
            else
            {
                while (sidecar.TryDequeueAccessUnit(out var accessUnit))
                    PublishVideoAccessUnit(accessUnit, CurrentLogTimeNs, videoFormat);
            }

            LogEncoderStderrIfNeeded(sidecar);
        }

        private void PublishVideoAccessUnit(byte[] accessUnit, ulong unixNs, string videoFormat)
        {
            if (accessUnit == null || accessUnit.Length == 0)
                return;

            if (unixNs == 0UL)
                unixNs = CurrentLogTimeNs;
            var payload = CameraCompressedVideoBuilder.Serialize(
                unixNs,
                _frameId,
                accessUnit,
                videoFormat);
            PublishProto(payload, unixNs);
        }

        private void StopVideoSidecar()
        {
            if (_videoSidecar == null)
                return;

            DrainEncodedAccessUnits();
            _videoSidecar.Dispose();
            DrainEncodedAccessUnits();
            _videoSidecar = null;
            _videoSidecarMode = CameraOutputMode.Jpeg;
        }

        private void EnsureSidecarMatchesMode(CameraVideoOutputProfile profile)
        {
            if (_videoSidecar == null)
                return;

            if (!profile.IsVideo || _videoSidecarMode != profile.Mode)
            {
                StopVideoSidecar();
                _warnedVideoEncoderUnavailable = false;
                _lastLoggedStderr = null;
            }
        }

        private int ResolveEncoderFrameRate()
        {
            var rate = EffectivePublishRateHz;
            if (rate > 0f && rate < 1000f)
                return Mathf.Max(1, Mathf.RoundToInt(rate));

            return 30;
        }

        /// <summary>
        /// Allocates Unity capture resources on the main thread using the current
        /// Inspector-requested dimensions before each readback snapshots the actual RT size.
        /// </summary>
        private void EnsureCaptureResources()
        {
            _sourceCam = _sourceCam != null ? _sourceCam : GetComponent<Camera>();
            var width = Math.Max(1, _width);
            var height = Math.Max(1, _height);

            if (_captureRT == null || _captureRT.width != width || _captureRT.height != height)
            {
                if (_captureRT != null)
                    _captureRT.Release();

                _captureRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                _captureRT.Create();
            }

            if (_texture2D == null || _texture2D.width != width || _texture2D.height != height)
            {
                if (_texture2D != null)
                    Destroy(_texture2D);

                _texture2D = new Texture2D(width, height, TextureFormat.RGB24, false);
            }

            if (_captureCam == null)
            {
                var go = new GameObject("_FoxgloveCaptureCam");
                go.transform.SetParent(transform, false);
                _captureCam = go.AddComponent<Camera>();
                _captureCam.enabled = false;
            }

            _captureCam.CopyFrom(_sourceCam);
            _captureCam.targetTexture = _captureRT;
            _captureCam.enabled = false;
        }

        /// <summary>
        /// Destroys Unity-owned capture resources only after local pending readbacks are
        /// drained or invalidated.
        /// </summary>
        private void CleanupResources()
        {
            if (_captureCam != null)
                _captureCam.targetTexture = null;

            if (_captureRT != null)
            {
                _captureRT.Release();
                _captureRT = null;
            }

            if (_captureCam != null)
            {
                Destroy(_captureCam.gameObject);
                _captureCam = null;
            }

            if (_texture2D != null)
            {
                Destroy(_texture2D);
                _texture2D = null;
            }
        }

        private void EnsureJpegQueues()
        {
            var encodeCapacity = Math.Max(1, _maxJpegEncodeQueue);
            if (_jpegEncodeQueue == null || _jpegEncodeQueue.Capacity != encodeCapacity)
                _jpegEncodeQueue = new DropOldestBoundedQueue<JpegEncodeRequest>(encodeCapacity);

            var completedCapacity = Math.Max(1, _maxCompletedJpegQueue);
            if (_completedJpegQueue == null || _completedJpegQueue.Capacity != completedCapacity)
                _completedJpegQueue = new DropOldestBoundedQueue<JpegEncodeResult>(completedCapacity);
        }

        /// <summary>
        /// Lazily starts the background JPEG worker after demand and budget gates pass.
        /// </summary>
        private bool EnsureJpegWorkerStarted()
        {
            EnsureJpegQueues();
            if (_jpegWorker != null && _jpegWorker.IsAlive)
                return true;

            try
            {
                if (_jpegWorkerSignal != null)
                {
                    _jpegWorkerSignal.Dispose();
                    _jpegWorkerSignal = null;
                }

                _jpegWorkerStopping = false;
                _jpegWorkerSignal = new AutoResetEvent(false);
                _jpegWorker = new Thread(EncodeJpegWorkerLoop)
                {
                    IsBackground = true,
                    Name = "FoxgloveCameraJpegEncoder"
                };
                _jpegWorker.Start();
                _warnedJpegWorkerShutdown = false;
                return true;
            }
            catch (Exception ex)
            {
                _jpegWorker = null;
                LogJpegWorkerFailure("Unable to start JPEG worker: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Requests worker shutdown without blocking Play Mode indefinitely; late output is
        /// discarded by queue clearing and generation checks.
        /// </summary>
        private void StopJpegWorker(bool clearQueues)
        {
            _jpegWorkerStopping = true;
            _jpegWorkerSignal?.Set();
            var worker = _jpegWorker;
            if (worker != null && worker.IsAlive && !worker.Join(JpegWorkerStopWaitMs))
            {
                if (!_warnedJpegWorkerShutdown)
                {
                    Debug.LogWarning("[Foxglove] Camera JPEG worker is still stopping; stale output will be ignored.");
                    _warnedJpegWorkerShutdown = true;
                }

                if (clearQueues)
                    ClearJpegQueues();
                return;
            }

            _jpegWorker = null;
            _jpegWorkerStopping = false;
            if (_jpegWorkerSignal != null)
            {
                _jpegWorkerSignal.Dispose();
                _jpegWorkerSignal = null;
            }

            if (clearQueues)
                ClearJpegQueues();
        }

        private void ClearJpegQueues()
        {
            _jpegEncodeQueue?.Clear();
            _completedJpegQueue?.Clear();
            lock (_readbackTimingGate)
                _readbackRequestTicks.Clear();
        }

        private void ResetJpegPipelineState()
        {
            EnsureJpegQueues();
            ClearJpegQueues();
            _lastPublishedCaptureUnixNs = 0;
            _warnedJpegWorkerFailure = false;
            _nextCameraDiagLogSec = 0;
            _lastRenderMs = 0;
            _lastReadbackLatencyMs = 0;
            _lastReadbackCopyMs = 0;
            _lastJpegEncodeMs = 0;
            _lastSerializeMs = 0;
            _lastPublishDrainMs = 0;
            _lastJpegBytes = 0;
            _readbackBudgetSkipCount = 0;
            _encodeBudgetSkipCount = 0;
            _completedBudgetSkipCount = 0;
            _pixelBudgetSkipCount = 0;
            _noDemandJpegDropCount = 0;
            _droppedEncodeQueueCount = 0;
            _droppedCompletedJpegCount = 0;
            _droppedEncodedBudgetCount = 0;
            _droppedLateJpegCount = 0;
        }

        /// <summary>
        /// Tracks readback latency for diagnostics without making timing data part of the
        /// publish contract.
        /// </summary>
        private void RememberReadbackStart(ulong unixNs, long ticks)
        {
            lock (_readbackTimingGate)
                _readbackRequestTicks[unixNs] = ticks;
        }

        private double TakeReadbackLatencyMs(ulong unixNs)
        {
            lock (_readbackTimingGate)
            {
                if (_readbackRequestTicks.TryGetValue(unixNs, out var ticks))
                {
                    _readbackRequestTicks.Remove(unixNs);
                    return ElapsedMs(ticks);
                }
            }

            return 0;
        }

        private void LogJpegWorkerFailure(string reason)
        {
            if (_warnedJpegWorkerFailure)
                return;

            _warnedJpegWorkerFailure = true;
            Debug.LogWarning("[Foxglove] Camera JPEG worker disabled: " + (string.IsNullOrWhiteSpace(reason) ? "unknown failure" : reason));
        }

        /// <summary>
        /// Reports render, readback, encode, serialization and queue pressure separately
        /// so camera cost can be attributed before future pipeline changes.
        /// </summary>
        private void LogCameraDiagnosticsIfNeeded()
        {
            if (!_logCameraDiagnostics)
                return;

            var now = Time.unscaledTimeAsDouble;
            if (now < _nextCameraDiagLogSec)
                return;

            _nextCameraDiagLogSec = now + Math.Max(0.1f, _cameraDiagnosticsIntervalSeconds);
            Debug.Log(
                "[Foxglove][CameraDiag] " +
                $"renderMs={_lastRenderMs:F2} readbackLatencyMs={_lastReadbackLatencyMs:F2} readbackCopyMs={_lastReadbackCopyMs:F2} " +
                $"jpegMs={_lastJpegEncodeMs:F2} serializeMs={_lastSerializeMs:F2} publishDrainMs={_lastPublishDrainMs:F2} " +
                $"bytes={_lastJpegBytes} pendingReadbacks={_pendingRequests} encodeQueue={_jpegEncodeQueue?.Count ?? 0} completedQueue={_completedJpegQueue?.Count ?? 0} " +
                $"skips(readback={_readbackBudgetSkipCount},encode={_encodeBudgetSkipCount},completed={_completedBudgetSkipCount},pixels={_pixelBudgetSkipCount}) " +
                $"drops(noDemand={_noDemandJpegDropCount},encodeQueue={_droppedEncodeQueueCount},completedQueue={_droppedCompletedJpegCount},encodedBudget={_droppedEncodedBudgetCount},late={_droppedLateJpegCount}).");
            ResetCameraDiagnosticCounters();
        }

        private void ResetCameraDiagnosticCounters()
        {
            _readbackBudgetSkipCount = 0;
            _encodeBudgetSkipCount = 0;
            _completedBudgetSkipCount = 0;
            _pixelBudgetSkipCount = 0;
            _noDemandJpegDropCount = 0;
            _droppedEncodeQueueCount = 0;
            _droppedCompletedJpegCount = 0;
            _droppedEncodedBudgetCount = 0;
            _droppedLateJpegCount = 0;
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
            {
                _backpressureBaselineInitialized = false;
                return true;
            }

            var stats = _manager.GetTransportStatsSnapshot();
            var currentDrop = stats.Supported ? stats.TotalDroppedDataFrames : _lastDropCount;
            var now = Time.unscaledTimeAsDouble;
            if (stats.Supported && !_backpressureBaselineInitialized)
            {
                _lastDropCount = currentDrop;
                _cooldownUntilSec = now;
                _backpressureBaselineInitialized = true;
            }

            var result = CameraBackpressurePolicy.Evaluate(
                enabled: true,
                currentTimeSec: now,
                cooldownSec: _backpressureCooldownSeconds,
                previousDropCount: _lastDropCount,
                currentDropCount: currentDrop,
                currentCooldownUntilSec: _cooldownUntilSec);

            _lastDropCount = result.NextDropCount;
            _cooldownUntilSec = result.NextCooldownUntilSec;

            if (result.AllowCapture)
                return true;

            LogBackpressureSkip("[Foxglove] Camera capture skipped by backpressure cooldown.");
            return false;
        }

        private void ResetBackpressureState()
        {
            _lastDropCount = 0;
            _cooldownUntilSec = 0;
            _backpressureSkipLogCount = 0;
            _backpressureBaselineInitialized = false;
        }

        private void LogBackpressureSkip(string message)
        {
            if (!_logBackpressureSkips || _backpressureSkipLogCount >= 10) return;

            _backpressureSkipLogCount++;
            Debug.LogWarning(message);
        }

        private void LogVideoEncoderUnavailable(CameraVideoOutputProfile profile, string reason)
        {
            if (_warnedVideoEncoderUnavailable)
                return;

            _warnedVideoEncoderUnavailable = true;
            Debug.LogWarning("[Foxglove] " + profile.DisplayName + " camera video disabled: " + reason);
        }

        private void LockRuntimeOutputMode()
        {
            _runtimeOutputMode = _outputMode;
            _runtimeOutputModeInitialized = true;
            _warnedRuntimeOutputModeSwitch = false;
        }

        private void UnlockRuntimeOutputMode()
        {
            _runtimeOutputModeInitialized = false;
            _warnedRuntimeOutputModeSwitch = false;
        }

        private void WarnRuntimeOutputModeSwitch()
        {
            if (_warnedRuntimeOutputModeSwitch)
                return;

            _warnedRuntimeOutputModeSwitch = true;
            var active = CameraVideoOutputProfile.ForMode(_runtimeOutputMode).DisplayName;
            var requested = CameraVideoOutputProfile.ForMode(_outputMode).DisplayName;
            Debug.LogWarning(
                "[Foxglove] Camera output mode changes during Play Mode are ignored to avoid stale channel advertisements. " +
                $"Restart Play Mode to switch from {active} to {requested}.");
        }

        private static string DescribeVideoEncoderFailure(ICameraVideoEncoderSidecar sidecar, string fallback)
        {
            var reason = string.IsNullOrWhiteSpace(sidecar?.LastError) ? fallback : sidecar.LastError;
            var diagnostic = sidecar?.LastDiagnosticLine;
            if (!string.IsNullOrWhiteSpace(diagnostic)
                && (string.IsNullOrWhiteSpace(reason)
                    || reason.IndexOf(diagnostic, StringComparison.OrdinalIgnoreCase) < 0))
            {
                reason = string.IsNullOrWhiteSpace(reason)
                    ? diagnostic
                    : reason + " Last diagnostic: " + diagnostic;
            }

            return string.IsNullOrWhiteSpace(reason) ? fallback : reason;
        }

        private void LogEncoderStderrIfNeeded(ICameraVideoEncoderSidecar sidecar)
        {
            if (!_logEncoderStderr || sidecar == null)
                return;

            var line = sidecar.LastDiagnosticLine;
            if (string.IsNullOrEmpty(line) || line == _lastLoggedStderr)
                return;

            _lastLoggedStderr = line;
            var profile = CameraVideoOutputProfile.ForMode(_videoSidecarMode);
            Debug.LogWarning("[Foxglove] " + profile.DisplayName + ": " + line);
        }

        /// <summary>
        /// Worker-side encode path operating only on owned buffers and pure managed
        /// serializers; Unity APIs must stay out of this method.
        /// </summary>
        private static JpegEncodeResult EncodeJpegRequest(JpegEncodeRequest request)
        {
            var encodeStart = Stopwatch.GetTimestamp();
            byte[] jpeg;
            try
            {
                // AsyncGPUReadback delivers rows in Unity texture order; JPEG viewers expect top-first rows.
                jpeg = ManagedJpegEncoder.EncodeRgb24(
                    request.Rgb24,
                    request.Width,
                    request.Height,
                    request.Quality,
                    flipVertical: true);
            }
            catch (Exception ex)
            {
                return JpegEncodeResult.Failure(request, ex.Message, ElapsedMs(encodeStart));
            }

            var encodeMs = ElapsedMs(encodeStart);
            if (jpeg == null || jpeg.Length == 0)
                return JpegEncodeResult.Failure(request, "JPEG encoder returned no bytes.", encodeMs);

            if (request.MaxEncodedBytes > 0 && jpeg.Length > request.MaxEncodedBytes)
                return JpegEncodeResult.EncodedBudgetDrop(request, jpeg.Length, encodeMs);

            var serializeStart = Stopwatch.GetTimestamp();
            byte[] webSocketPayload = null;
            byte[] bridgePayload = null;
            byte[] ros2Payload = null;
            CompressedImageMessage jsonMessage = null;

            try
            {
                if (request.PublishWebSocket && request.WebSocketEncoding == PublisherEffectiveEncoding.Protobuf)
                {
                    webSocketPayload = CameraCompressedImageBuilder.Serialize(request.CaptureUnixNs, request.FrameId, jpeg, "jpeg");
                }
                else if (request.PublishWebSocket && request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
                {
                    ros2Payload = Ros2CdrCompressedImageBuilder.Serialize(request.CaptureUnixNs, request.FrameId, jpeg, "jpeg");
                    webSocketPayload = ros2Payload;
                }
                else if (request.PublishWebSocket)
                {
                    jsonMessage = new CompressedImageMessage
                    {
                        Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(request.CaptureUnixNs),
                        FrameId = request.FrameId,
                        Data = Convert.ToBase64String(jpeg),
                        Format = "jpeg"
                    };
                }

                if (request.PublishBridge)
                {
                    ros2Payload ??= Ros2CdrCompressedImageBuilder.Serialize(request.CaptureUnixNs, request.FrameId, jpeg, "jpeg");
                    bridgePayload = ros2Payload;
                }
            }
            catch (Exception ex)
            {
                return JpegEncodeResult.Failure(
                    request,
                    "Unable to serialize JPEG camera payload off thread: " + ex.Message,
                    encodeMs,
                    ElapsedMs(serializeStart),
                    jpeg.Length);
            }

            return JpegEncodeResult.Completed(
                request,
                webSocketPayload,
                bridgePayload,
                jsonMessage,
                jpeg.Length,
                encodeMs,
                ElapsedMs(serializeStart));
        }

        private sealed class JpegEncodeRequest
        {
            public JpegEncodeRequest(
                byte[] rgb24,
                int width,
                int height,
                int quality,
                ulong captureUnixNs,
                string frameId,
                bool publishWebSocket,
                bool publishBridge,
                PublisherEffectiveEncoding webSocketEncoding,
                int maxEncodedBytes,
                int generation)
            {
                Rgb24 = rgb24;
                Width = width;
                Height = height;
                Quality = quality;
                CaptureUnixNs = captureUnixNs;
                FrameId = frameId ?? "";
                PublishWebSocket = publishWebSocket;
                PublishBridge = publishBridge;
                WebSocketEncoding = webSocketEncoding;
                MaxEncodedBytes = maxEncodedBytes;
                Generation = generation;
            }

            public byte[] Rgb24 { get; }
            public int Width { get; }
            public int Height { get; }
            public int Quality { get; }
            public ulong CaptureUnixNs { get; }
            public string FrameId { get; }
            public bool PublishWebSocket { get; }
            public bool PublishBridge { get; }
            public PublisherEffectiveEncoding WebSocketEncoding { get; }
            public int MaxEncodedBytes { get; }
            public int Generation { get; }
        }

        private sealed class JpegEncodeResult
        {
            private JpegEncodeResult(
                JpegEncodeRequest request,
                bool success,
                bool droppedByEncodedBudget,
                byte[] webSocketPayload,
                byte[] bridgePayload,
                CompressedImageMessage jsonMessage,
                int jpegBytes,
                string error,
                double encodeMs,
                double serializeMs)
            {
                Request = request;
                Success = success;
                DroppedByEncodedBudget = droppedByEncodedBudget;
                WebSocketPayload = webSocketPayload;
                BridgePayload = bridgePayload;
                JsonMessage = jsonMessage;
                JpegBytes = jpegBytes;
                Error = error;
                EncodeMs = encodeMs;
                SerializeMs = serializeMs;
            }

            public JpegEncodeRequest Request { get; }
            public bool Success { get; }
            public bool DroppedByEncodedBudget { get; }
            public byte[] WebSocketPayload { get; }
            public byte[] BridgePayload { get; }
            public CompressedImageMessage JsonMessage { get; }
            public int JpegBytes { get; }
            public string Error { get; }
            public double EncodeMs { get; }
            public double SerializeMs { get; }

            public static JpegEncodeResult Completed(
                JpegEncodeRequest request,
                byte[] webSocketPayload,
                byte[] bridgePayload,
                CompressedImageMessage jsonMessage,
                int jpegBytes,
                double encodeMs,
                double serializeMs)
                => new JpegEncodeResult(
                    request,
                    success: true,
                    droppedByEncodedBudget: false,
                    webSocketPayload,
                    bridgePayload,
                    jsonMessage,
                    jpegBytes,
                    error: null,
                    encodeMs,
                    serializeMs);

            public static JpegEncodeResult Failure(
                JpegEncodeRequest request,
                string error,
                double encodeMs,
                double serializeMs = 0,
                int jpegBytes = 0)
                => new JpegEncodeResult(
                    request,
                    success: false,
                    droppedByEncodedBudget: false,
                    webSocketPayload: null,
                    bridgePayload: null,
                    jsonMessage: null,
                    jpegBytes,
                    error,
                    encodeMs,
                    serializeMs);

            /// <summary>
            /// Records an encoded payload that was produced successfully but intentionally
            /// dropped because it exceeded the configured byte budget.
            /// </summary>
            public static JpegEncodeResult EncodedBudgetDrop(JpegEncodeRequest request, int jpegBytes, double encodeMs)
                => new JpegEncodeResult(
                    request,
                    success: false,
                    droppedByEncodedBudget: true,
                    webSocketPayload: null,
                    bridgePayload: null,
                    jsonMessage: null,
                    jpegBytes,
                    error: null,
                    encodeMs,
                    serializeMs: 0);
        }

        /// <summary>
        /// Background loop for stale-droppable visualization frames. It consumes owned
        /// request buffers and posts completed payloads back to the main-thread drain.
        /// </summary>
        private void EncodeJpegWorkerLoop()
        {
            while (!_jpegWorkerStopping)
            {
                var queue = _jpegEncodeQueue;
                if (queue != null && queue.TryDequeue(out var request))
                {
                    var result = EncodeJpegRequest(request);
                    if (!_jpegWorkerStopping)
                    {
                        var completed = _completedJpegQueue;
                        if (completed != null && completed.Enqueue(result))
                            _droppedCompletedJpegCount++;
                    }

                    continue;
                }

                _jpegWorkerSignal?.WaitOne(50);
            }
        }
    }
}
