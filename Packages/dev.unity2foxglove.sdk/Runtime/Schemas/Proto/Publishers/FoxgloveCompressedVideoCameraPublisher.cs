// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Captures camera frames and publishes H.264 foxglove.CompressedVideo protobuf frames.

using System;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Legacy standalone H.264 publisher. Prefer FoxgloveCameraPublisher with
    /// Camera Output Mode set to H.264 (FFmpeg) for normal scenes.
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(Camera))]
    public class FoxgloveCompressedVideoCameraPublisher : FoxglovePublisherBase
    {
        [Header("Compressed Video")]
        [SerializeField] private string _frameId = "unity_camera";
        [SerializeField, Min(1)] private int _width = 640;
        [SerializeField, Min(1)] private int _height = 480;
        [SerializeField, Min(1)] private int _targetFps = 30;
        [SerializeField, Min(1)] private int _bitrateKbps = 4000;
        [SerializeField, Min(1)] private int _keyframeInterval = 30;
        [SerializeField] private string _ffmpegPath = "ffmpeg";
        [SerializeField, Min(1)] private int _maxPendingReadbacks = 1;
        [SerializeField, Min(1)] private int _maxOutputQueue = 4;
        [SerializeField] private bool _logEncoderStderr;

        protected override string SchemaName => "foxglove.CompressedVideo";
        public override bool SupportsJsonEncoding => false;
        public override bool SupportsProtobufEncoding => true;

        private Camera _sourceCam;
        private Camera _captureCam;
        private RenderTexture _captureRT;
        private FfmpegH264EncoderSidecar _sidecar;
        private int _pendingRequests;
        private bool _destroyed;
        private bool _warnedEncoderUnavailable;
        private string _lastLoggedStderr;
        private int _captureGeneration;
        private bool _cleanupWhenReadbacksDrain;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/camera/video";
        }

        protected override void Reset()
        {
            base.Reset();
            _publishRateHz = 30f;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _destroyed = false;
            _warnedEncoderUnavailable = false;
            _lastLoggedStderr = null;
            _cleanupWhenReadbacksDrain = false;
            _captureGeneration++;
            _sourceCam = GetComponent<Camera>();
            EnsureCaptureResources();
        }

        private void LateUpdate()
        {
            DrainEncodedAccessUnits();

            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            var maxPendingReadbacks = Math.Max(1, _maxPendingReadbacks);
            if (_pendingRequests >= maxPendingReadbacks) return;
            if (!ShouldPreparePublishPayload()) return;
            if (!EnsureSidecarStarted()) return;

            _captureCam.Render();
            var generation = _captureGeneration;
            _pendingRequests++;
            AsyncGPUReadback.Request(_captureRT, 0, TextureFormat.RGB24, req => OnReadbackComplete(req, generation));
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest req, int generation)
        {
            CompletePendingReadback();

            if (_destroyed || !isActiveAndEnabled || generation != _captureGeneration) return;
            if (req.hasError)
            {
                Debug.LogWarning("[Foxglove] H.264 camera AsyncGPUReadback failed.");
                return;
            }

            var sidecar = _sidecar;
            if (sidecar == null || !sidecar.IsRunning)
            {
                LogEncoderUnavailable("FFmpeg encoder is not running.");
                return;
            }

            var frameBytes = req.GetData<byte>().ToArray();
            if (!sidecar.TrySubmitFrame(frameBytes))
                LogEncoderUnavailable(sidecar.LastError ?? "FFmpeg encoder refused the frame.");
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            _captureGeneration++;
            _cleanupWhenReadbacksDrain = _pendingRequests > 0;
            StopSidecar();
            if (_pendingRequests == 0)
                CleanupResources();
        }

        private void OnDestroy()
        {
            _destroyed = true;
            _captureGeneration++;
            StopSidecar();
            _cleanupWhenReadbacksDrain = _pendingRequests > 0;
            if (_pendingRequests == 0)
                CleanupResources();
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

        private void EnsureCaptureResources()
        {
            var width = Math.Max(1, _width);
            var height = Math.Max(1, _height);

            if (_captureRT == null || _captureRT.width != width || _captureRT.height != height)
            {
                if (_captureRT != null)
                    _captureRT.Release();

                _captureRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                _captureRT.Create();
            }

            if (_captureCam == null)
            {
                var go = new GameObject("_FoxgloveCompressedVideoCaptureCam");
                go.transform.SetParent(transform, false);
                _captureCam = go.AddComponent<Camera>();
                _captureCam.enabled = false;
            }

            _captureCam.CopyFrom(_sourceCam);
            _captureCam.targetTexture = _captureRT;
            _captureCam.enabled = false;
        }

        private bool EnsureSidecarStarted()
        {
            if (_sidecar != null && _sidecar.IsRunning)
                return true;

            StopSidecar();
            _sidecar = new FfmpegH264EncoderSidecar();
            var options = new FfmpegH264EncoderOptions
            {
                FfmpegPath = string.IsNullOrWhiteSpace(_ffmpegPath) ? "ffmpeg" : _ffmpegPath,
                Width = Math.Max(1, _width),
                Height = Math.Max(1, _height),
                FrameRate = Math.Max(1, _targetFps),
                BitrateKbps = Math.Max(1, _bitrateKbps),
                KeyframeInterval = Math.Max(1, _keyframeInterval),
                MaxInputQueue = Math.Max(1, _maxPendingReadbacks),
                MaxOutputQueue = Math.Max(1, _maxOutputQueue)
            };

            if (_sidecar.Start(options))
            {
                _warnedEncoderUnavailable = false;
                return true;
            }

            LogEncoderUnavailable(_sidecar.LastError ?? "Failed to start FFmpeg encoder.");
            StopSidecar();
            return false;
        }

        private void DrainEncodedAccessUnits()
        {
            var sidecar = _sidecar;
            if (sidecar == null)
                return;

            while (sidecar.TryDequeueAccessUnit(out var accessUnit))
            {
                var unixNs = CurrentLogTimeNs;
                var payload = CameraCompressedVideoBuilder.Serialize(
                    unixNs,
                    _frameId,
                    accessUnit,
                    CameraCompressedVideoBuilder.H264Format);
                PublishProto(payload, unixNs);
            }

            LogEncoderStderrIfNeeded(sidecar);
        }

        private void StopSidecar()
        {
            if (_sidecar == null)
                return;

            DrainEncodedAccessUnits();
            _sidecar.Dispose();
            DrainEncodedAccessUnits();
            _sidecar = null;
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
        }

        private void LogEncoderUnavailable(string reason)
        {
            if (_warnedEncoderUnavailable)
                return;

            _warnedEncoderUnavailable = true;
            Debug.LogWarning("[Foxglove] H.264 camera video disabled: " + reason);
        }

        private void LogEncoderStderrIfNeeded(FfmpegH264EncoderSidecar sidecar)
        {
            if (!_logEncoderStderr || sidecar == null)
                return;

            var line = sidecar.LastStderrLine;
            if (string.IsNullOrEmpty(line) || line == _lastLoggedStderr)
                return;

            _lastLoggedStderr = line;
            Debug.LogWarning("[Foxglove] FFmpeg: " + line);
        }
    }
}
