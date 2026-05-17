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
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Util;
using UnityEngine;
using UnityEngine.Rendering;

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

        private CameraVideoOutputProfile ActiveProfile => CameraVideoOutputProfile.ForMode(_outputMode);

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

        /// <summary>Defaults the topic to the current mode default if not set.</summary>
        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic))
                _topic = ActiveProfile.DefaultTopic;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _destroyed = false;
            _warnedVideoEncoderUnavailable = false;
            _lastLoggedStderr = null;
            ResetBackpressureState();
            _sourceCam = GetComponent<Camera>();
            EnsureCaptureResources();
        }

        /// <summary>
        /// Schedules a camera capture only when cadence, demand, replay state,
        /// and readback limits allow useful payload work.
        /// </summary>
        private void LateUpdate()
        {
            var profile = ActiveProfile;
            EnsureSidecarMatchesMode(profile);
            DrainEncodedAccessUnits();

            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            var maxPendingReadbacks = Math.Max(1, _maxPendingReadbacks);
            if (_pendingRequests >= maxPendingReadbacks) return;
            if (!profile.IsVideo && !AllowJpegCaptureByBackpressure()) return;
            if (!ShouldPreparePublishPayload()) return;
            if (profile.IsVideo && !EnsureVideoSidecarStarted(profile)) return;

            EnsureCaptureResources();
            _captureCam.Render();
            _pendingRequests++;
            AsyncGPUReadback.Request(_captureRT, 0, TextureFormat.RGB24, OnReadbackComplete);
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest req)
        {
            _pendingRequests = Mathf.Max(0, _pendingRequests - 1);

            if (_destroyed || !isActiveAndEnabled) return;
            if (req.hasError)
            {
                Debug.LogWarning("[Foxglove] Camera AsyncGPUReadback failed.");
                return;
            }
            if (_manager == null) return;

            var profile = ActiveProfile;
            if (profile.IsVideo)
            {
                SubmitVideoFrame(req);
                return;
            }

            PublishJpegFrame(req);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopVideoSidecar();
        }

        private void OnDestroy()
        {
            _destroyed = true;
            AsyncGPUReadback.WaitAllRequests();
            StopVideoSidecar();
            CleanupResources();
        }

        private void PublishJpegFrame(AsyncGPUReadbackRequest req)
        {
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

            var unixNs = CurrentLogTimeNs;
            if (EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                var payload = CameraCompressedImageBuilder.Serialize(unixNs, _frameId, jpeg, "jpeg");
                PublishProto(payload, unixNs);
                _backpressureSkipLogCount = 0;
                return;
            }

            if (EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                var payload = Ros2CdrCompressedImageBuilder.Serialize(unixNs, _frameId, jpeg, "jpeg");
                PublishRos2(payload, unixNs);
                _backpressureSkipLogCount = 0;
                return;
            }

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

        private void SubmitVideoFrame(AsyncGPUReadbackRequest req)
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

            if (!sidecar.TrySubmitFrame(frameBytes))
            {
                LogVideoEncoderUnavailable(ActiveProfile, DescribeVideoEncoderFailure(sidecar, "Video encoder refused the frame."));
                return;
            }

            DrainEncodedAccessUnits();
        }

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
            while (sidecar.TryDequeueAccessUnit(out var accessUnit))
            {
                var unixNs = CurrentLogTimeNs;
                var payload = CameraCompressedVideoBuilder.Serialize(
                    unixNs,
                    _frameId,
                    accessUnit,
                    videoFormat);
                PublishProto(payload, unixNs);
            }

            LogEncoderStderrIfNeeded(sidecar);
        }

        private void StopVideoSidecar()
        {
            if (_videoSidecar == null)
                return;

            _videoSidecar.Dispose();
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
    }
}
