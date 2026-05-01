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
        [SerializeField] private string _frameId = "unity_camera";
        [SerializeField] private int _width = 640;
        [SerializeField] private int _height = 480;
        [Range(10, 100)]
        [SerializeField] private int _jpegQuality = 70;
        [SerializeField] private int _maxPendingReadbacks = 2;

        protected override string SchemaName => "foxglove.CompressedImage";

        private Camera _sourceCam;
        private Camera _captureCam;
        private RenderTexture _captureRT;
        private Texture2D _texture2D;
        private int _pendingRequests;
        private bool _destroyed;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/camera";
        }

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

            var unixNs = FoxgloveTimeUtil.NowUnixTimeNs();
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

        protected override void OnDisable()
        {
            base.OnDisable();
            // Don't destroy resources here — disable/re-enable must be safe.
            // LateUpdate stops naturally when the component is disabled.
            // In-flight readback callbacks check !enabled and skip publishing.
        }

        private void OnDestroy()
        {
            _destroyed = true;
            AsyncGPUReadback.WaitAllRequests();
            CleanupResources();
        }

        private void CleanupResources()
        {
            if (_captureCam != null) _captureCam.targetTexture = null;
            if (_captureRT != null) { _captureRT.Release(); _captureRT = null; }
            if (_captureCam != null) { Destroy(_captureCam.gameObject); _captureCam = null; }
            if (_texture2D != null) { Destroy(_texture2D); _texture2D = null; }
        }
    }
}
