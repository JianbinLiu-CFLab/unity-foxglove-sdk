// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Captures camera frames via AsyncGPUReadback and publishes them
// as foxglove.CompressedImage JPEG frames in JSON or protobuf encoding.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;
using Foxglove.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Captures camera frames via AsyncGPUReadback and publishes as foxglove.CompressedImage.
    /// Default: 640x480, JPEG quality 70, 10 Hz.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FoxgloveCameraPublisher : FoxglovePublisherBase
    {
        // Serialized fields
        /// <summary>Identifier for the Foxglove frame, e.g. <c>"unity_camera"</c>.</summary>
        [SerializeField] private string _frameId = "unity_camera";
        /// <summary>Capture resolution width in pixels.</summary>
        [SerializeField] private int _width = 640;
        /// <summary>Capture resolution height in pixels.</summary>
        [SerializeField] private int _height = 480;
        /// <summary>JPEG quality 10-100.</summary>
        [Range(10, 100)]
        [SerializeField] private int _jpegQuality = 70;
        /// <summary>Max number of concurrent AsyncGPUReadback requests.</summary>
        [SerializeField] private int _maxPendingReadbacks = 2;

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

        protected override string SchemaName => "foxglove.CompressedImage";
        public override bool SupportsProtobufEncoding => true;

        // Internal state
        /// <summary>Cached reference to the source Camera on this GameObject.</summary>
        private Camera _sourceCam;
        /// <summary>Hidden helper Camera rendering into <c>_captureRT</c>.</summary>
        private Camera _captureCam;
        /// <summary>RenderTexture used as the capture target.</summary>
        private RenderTexture _captureRT;
        /// <summary>CPU-side Texture2D for JPEG encoding.</summary>
        private Texture2D _texture2D;
        /// <summary>Number of in-flight GPU readback requests.</summary>
        private int _pendingRequests;
        /// <summary>True after OnDestroy starts, to suppress late callbacks.</summary>
        private bool _destroyed;

        // Backpressure policy state
        private long _lastDropCount;
        private double _cooldownUntilSec;
        private int _backpressureSkipLogCount;
        private bool _backpressureBaselineInitialized;

        /// <summary>Defaults the topic to <c>/unity/camera</c> if not set.</summary>
        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/camera";
        }

        /// <summary>
        /// Creates or reuses the capture Camera, RenderTexture, and Texture2D.
        /// Resets the destroyed flag so disable/re-enable cycles are safe.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            _destroyed = false;
            ResetBackpressureState();
            _sourceCam = GetComponent<Camera>();

            if (_captureRT == null)
            {
                _captureRT = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32);
                _captureRT.Create();
            }
            if (_texture2D == null)
            {
                _texture2D = new Texture2D(_width, _height, TextureFormat.RGB24, false);
            }
            if (_captureCam == null)
            {
                var go = new GameObject("_FoxgloveCaptureCam");
                go.transform.SetParent(transform, false);
                _captureCam = go.AddComponent<Camera>();
                _captureCam.CopyFrom(_sourceCam);
                _captureCam.targetTexture = _captureRT;
                _captureCam.enabled = false;
            }
        }

        /// <summary>
        /// Renders the capture Camera and queues an AsyncGPUReadback request.
        /// Respects publish rate, pending-readback cap, and replay state.
        /// </summary>
        private void LateUpdate()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            if (_pendingRequests >= _maxPendingReadbacks) return;

            if (!_enableBackpressureAdaptation)
            {
                _backpressureBaselineInitialized = false;
            }
            else
            {
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

                if (!result.AllowCapture)
                {
                    LogBackpressureSkip("[Foxglove] Camera capture skipped by backpressure cooldown.");
                    return;
                }
            }

            _captureCam.Render();
            _pendingRequests++;
            AsyncGPUReadback.Request(_captureRT, 0, TextureFormat.RGB24, OnReadbackComplete);
        }

        /// <summary>
        /// Callback from AsyncGPUReadback. Encodes the GPU data to JPEG and publishes
        /// a <c>CompressedImageMessage</c> through the manager.
        /// </summary>
        private void OnReadbackComplete(AsyncGPUReadbackRequest req)
        {
            _pendingRequests = Mathf.Max(0, _pendingRequests - 1);

            if (_destroyed || !enabled) return;
            if (req.hasError)
            {
                Debug.LogWarning("[Foxglove] AsyncGPUReadback failed");
                return;
            }
            if (_manager == null) return;

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

        /// <summary>
        /// Does NOT destroy resources; disable/re-enable must be safe.
        /// <c>LateUpdate</c> stops naturally when the component is disabled,
        /// and in-flight readback callbacks check <c>!enabled</c> to skip publishing.
        /// </summary>
        protected override void OnDisable()
        {
            base.OnDisable();
        }

        /// <summary>
        /// Marks the component as destroyed, drains all pending GPU readbacks,
        /// then releases the capture Camera, RenderTexture, and Texture2D.
        /// </summary>
        private void OnDestroy()
        {
            _destroyed = true;
            AsyncGPUReadback.WaitAllRequests();
            CleanupResources();
        }

        /// <summary>Releases the capture Camera object, RenderTexture, and Texture2D.</summary>
        private void CleanupResources()
        {
            if (_captureCam != null) _captureCam.targetTexture = null;
            if (_captureRT != null) { _captureRT.Release(); _captureRT = null; }
            if (_captureCam != null) { Destroy(_captureCam.gameObject); _captureCam = null; }
            if (_texture2D != null) { Destroy(_texture2D); _texture2D = null; }
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
    }
}
