// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity
// Purpose: Captures camera frames via AsyncGPUReadback and publishes them
// as foxglove.CompressedImage JPEG frames.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Captures camera frames via AsyncGPUReadback and publishes as foxglove.CompressedImage.
    /// Default: 640x480, JPEG quality 70, 10 Hz.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FoxgloveCameraPublisher : FoxglovePublisherBase
    {
        // ── Serialized fields ──
        /// <summary>Identifier for the Foxglove frame, e.g. <c>"unity_camera"</c>.</summary>
        [SerializeField] private string _frameId = "unity_camera";
        /// <summary>Capture resolution width in pixels.</summary>
        [SerializeField] private int _width = 640;
        /// <summary>Capture resolution height in pixels.</summary>
        [SerializeField] private int _height = 480;
        /// <summary>JPEG quality 10–100.</summary>
        [Range(10, 100)]
        [SerializeField] private int _jpegQuality = 70;
        /// <summary>Max number of concurrent AsyncGPUReadback requests.</summary>
        [SerializeField] private int _maxPendingReadbacks = 2;

        protected override string SchemaName => "foxglove.CompressedImage";

        // ── Internal state ──
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

            var unixNs = CurrentLogTimeNs;
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);

            var msg = new CompressedImageMessage
            {
                Timestamp = time,
                FrameId = _frameId,
                Data = Convert.ToBase64String(jpeg),
                Format = "jpeg"
            };

            Publish(msg, unixNs);
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
    }
}
