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
            }

            if (_captureCamera == null)
            {
                var go = new GameObject("_FoxgloveCaptureCam");
                go.transform.SetParent(parent, false);
                _captureCamera = go.AddComponent<Camera>();
                _captureCamera.enabled = false;
            }

            if (_sourceCamera != null)
                _captureCamera.CopyFrom(_sourceCamera);
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
