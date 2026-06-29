// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Owns Unity camera capture objects used by FoxgloveCameraPublisher.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Owns the main-thread Unity objects used to render and synchronously encode camera readbacks.
    /// </summary>
    internal sealed class CameraCaptureResources
    {
        private Camera _sourceCamera;
        private Camera _captureCamera;
        private RenderTexture _captureRenderTexture;
        private Texture2D _texture2D;
        private Camera _lastCopiedSourceCamera;
        private int _lastCaptureWidth;
        private int _lastCaptureHeight;
        private float _lastFieldOfView;
        private bool _lastOrthographic;
        private float _lastOrthographicSize;
        private float _lastNearClipPlane;
        private float _lastFarClipPlane;
        private int _lastCullingMask;
        private CameraClearFlags _lastClearFlags;
        private Color _lastBackgroundColor;
        private bool _captureCameraDirty = true;

        public Camera CaptureCamera => _captureCamera;

        public RenderTexture CaptureRenderTexture => _captureRenderTexture;

        public void Ensure(Component owner, Transform parent, int width, int height)
        {
            if (owner == null)
                return;

            _sourceCamera = _sourceCamera != null ? _sourceCamera : owner.GetComponent<Camera>();
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (_captureRenderTexture == null
                || _captureRenderTexture.width != width
                || _captureRenderTexture.height != height)
            {
                DestroyRenderTexture();
                _captureRenderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                _captureRenderTexture.Create();
                _captureCameraDirty = true;
            }

            if (_captureCamera == null)
            {
                var go = new GameObject("_FoxgloveCaptureCam");
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetParent(parent, false);
                _captureCamera = go.AddComponent<Camera>();
                _captureCamera.enabled = false;
                _captureCameraDirty = true;
            }

            SyncCaptureCameraIfDirty(width, height);
            _captureCamera.targetTexture = _captureRenderTexture;
            _captureCamera.enabled = false;
        }

        public byte[] EncodeJpeg(AsyncGPUReadbackRequest req, int width, int height, int quality)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (!EnsureTexture(width, height))
                return null;

            var data = req.GetData<byte>();
            _texture2D.LoadRawTextureData(data);
            _texture2D.Apply(false);
            return _texture2D.EncodeToJPG(quality);
        }

        public byte[] EncodeJpeg(byte[] rgb24Readback, int width, int height, int quality)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            var expectedBytes = width * height * 3;
            if (rgb24Readback == null || rgb24Readback.Length < expectedBytes || !EnsureTexture(width, height))
                return null;

            _texture2D.LoadRawTextureData(rgb24Readback);
            _texture2D.Apply(false);
            return _texture2D.EncodeToJPG(quality);
        }

        private bool EnsureTexture(int width, int height)
        {
            if (_texture2D == null || _texture2D.width != width || _texture2D.height != height)
            {
                DestroyUnityObject(_texture2D);
                _texture2D = new Texture2D(width, height, TextureFormat.RGB24, false);
            }

            return _texture2D != null;
        }

        public void Cleanup()
        {
            if (_captureCamera != null)
                _captureCamera.targetTexture = null;

            DestroyRenderTexture();

            if (_captureCamera != null)
            {
                DestroyUnityObject(_captureCamera.gameObject);
                _captureCamera = null;
            }

            DestroyUnityObject(_texture2D);
            _texture2D = null;
            _sourceCamera = null;
            _lastCopiedSourceCamera = null;
            _captureCameraDirty = true;
        }

        private void SyncCaptureCameraIfDirty(int width, int height)
        {
            if (_sourceCamera == null || _captureCamera == null)
                return;

            if (!_captureCameraDirty
                && _lastCopiedSourceCamera == _sourceCamera
                && _lastCaptureWidth == width
                && _lastCaptureHeight == height
                && Mathf.Approximately(_lastFieldOfView, _sourceCamera.fieldOfView)
                && _lastOrthographic == _sourceCamera.orthographic
                && Mathf.Approximately(_lastOrthographicSize, _sourceCamera.orthographicSize)
                && Mathf.Approximately(_lastNearClipPlane, _sourceCamera.nearClipPlane)
                && Mathf.Approximately(_lastFarClipPlane, _sourceCamera.farClipPlane)
                && _lastCullingMask == _sourceCamera.cullingMask
                && _lastClearFlags == _sourceCamera.clearFlags
                && _lastBackgroundColor == _sourceCamera.backgroundColor)
            {
                return;
            }

            _captureCamera.CopyFrom(_sourceCamera);
            _lastCopiedSourceCamera = _sourceCamera;
            _lastCaptureWidth = width;
            _lastCaptureHeight = height;
            _lastFieldOfView = _sourceCamera.fieldOfView;
            _lastOrthographic = _sourceCamera.orthographic;
            _lastOrthographicSize = _sourceCamera.orthographicSize;
            _lastNearClipPlane = _sourceCamera.nearClipPlane;
            _lastFarClipPlane = _sourceCamera.farClipPlane;
            _lastCullingMask = _sourceCamera.cullingMask;
            _lastClearFlags = _sourceCamera.clearFlags;
            _lastBackgroundColor = _sourceCamera.backgroundColor;
            _captureCameraDirty = false;
        }

        private void DestroyRenderTexture()
        {
            if (_captureRenderTexture == null)
                return;

            _captureRenderTexture.Release();
            DestroyUnityObject(_captureRenderTexture);
            _captureRenderTexture = null;
        }

        private static void DestroyUnityObject(Object target)
        {
            if (target != null)
                Object.Destroy(target);
        }
    }
}
