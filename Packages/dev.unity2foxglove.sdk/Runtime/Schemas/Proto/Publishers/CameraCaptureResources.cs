// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Owns Unity camera capture objects used by FoxgloveCameraPublisher.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Util;
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
        private readonly List<RetiredRenderTexture> _retiredRenderTextures = new List<RetiredRenderTexture>();
        private readonly object _retiredRenderTexturesGate = new object();
        private readonly CameraReadbackGenerationTracker _readbackGenerations = new CameraReadbackGenerationTracker();
        private int _captureRenderTextureGeneration;
        private Texture2D _texture2D;
        private byte[] _rgbScratch;
        private byte[] _rowScratch;
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

        public Camera SourceCamera => _sourceCamera;

        public void Ensure(Component owner, Transform parent, int width, int height, int captureGeneration)
        {
            if (owner == null)
                return;

            if (_sourceCamera == null)
            {
                _sourceCamera = owner.GetComponent<Camera>();
            }
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (_captureRenderTexture == null
                || _captureRenderTexture.width != width
                || _captureRenderTexture.height != height)
            {
                RetireRenderTexture();
                _captureRenderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                _captureRenderTexture.Create();
                _captureRenderTextureGeneration = captureGeneration;
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
            var flipped = EnsureRgbScratch(data.Length);
            data.CopyTo(flipped);
            FlipRgb24RowsInPlace(flipped, width, height, ref _rowScratch);
            _texture2D.LoadRawTextureData(flipped);
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

            var flipped = EnsureRgbScratch(expectedBytes);
            Buffer.BlockCopy(rgb24Readback, 0, flipped, 0, expectedBytes);
            FlipRgb24RowsInPlace(flipped, width, height, ref _rowScratch);
            _texture2D.LoadRawTextureData(flipped);
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

        private byte[] EnsureRgbScratch(int length)
        {
            if (_rgbScratch == null || _rgbScratch.Length != length)
                _rgbScratch = new byte[length];
            return _rgbScratch;
        }

        private static void FlipRgb24RowsInPlace(byte[] data, int width, int height, ref byte[] rowScratch)
        {
            var rowBytes = width * 3;
            if (rowScratch == null || rowScratch.Length != rowBytes)
                rowScratch = new byte[rowBytes];
            for (var top = 0; top < height / 2; top++)
            {
                var bottom = height - 1 - top;
                Buffer.BlockCopy(data, top * rowBytes, rowScratch, 0, rowBytes);
                Buffer.BlockCopy(data, bottom * rowBytes, data, top * rowBytes, rowBytes);
                Buffer.BlockCopy(rowScratch, 0, data, bottom * rowBytes, rowBytes);
            }
        }

        public void Cleanup()
        {
            if (_captureCamera != null)
                _captureCamera.targetTexture = null;

            DestroyRenderTexture();
            ReleaseAllRetiredRenderTextures();

            if (_captureCamera != null)
            {
                DestroyUnityObject(_captureCamera.gameObject);
                _captureCamera = null;
            }

            DestroyUnityObject(_texture2D);
            _texture2D = null;
            _rgbScratch = null;
            _rowScratch = null;
            _sourceCamera = null;
            _captureRenderTextureGeneration = 0;
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

        public void ReleaseRetiredRenderTextures()
        {
            List<RenderTexture> texturesToRelease = null;
            lock (_retiredRenderTexturesGate)
            {
                for (var i = _retiredRenderTextures.Count - 1; i >= 0; i--)
                {
                    var retired = _retiredRenderTextures[i];
                    if (_readbackGenerations.HasPending(retired.Generation))
                        continue;

                    texturesToRelease ??= new List<RenderTexture>();
                    texturesToRelease.Add(retired.Texture);
                    _retiredRenderTextures.RemoveAt(i);
                }
            }

            ReleaseRenderTextures(texturesToRelease);
        }

        public void RegisterReadback(int generation)
            => _readbackGenerations.Register(generation);

        public void CompleteReadback(int generation)
        {
            _readbackGenerations.Complete(generation);
            ReleaseRetiredRenderTextures();
        }

        private void RetireRenderTexture()
        {
            if (_captureRenderTexture == null)
                return;

            lock (_retiredRenderTexturesGate)
            {
                _retiredRenderTextures.Add(new RetiredRenderTexture(
                    _captureRenderTexture,
                    _captureRenderTextureGeneration));
            }
            _captureRenderTexture = null;
        }

        private void ReleaseAllRetiredRenderTextures()
        {
            List<RenderTexture> texturesToRelease = null;
            lock (_retiredRenderTexturesGate)
            {
                for (var i = 0; i < _retiredRenderTextures.Count; i++)
                {
                    texturesToRelease ??= new List<RenderTexture>();
                    texturesToRelease.Add(_retiredRenderTextures[i].Texture);
                }

                _retiredRenderTextures.Clear();
                _readbackGenerations.Clear();
            }

            ReleaseRenderTextures(texturesToRelease);
        }

        private static void ReleaseRenderTextures(List<RenderTexture> textures)
        {
            if (textures == null)
                return;

            for (var i = 0; i < textures.Count; i++)
            {
                var texture = textures[i];
                if (texture == null)
                    continue;

                texture.Release();
                DestroyUnityObject(texture);
            }
        }

        private sealed class RetiredRenderTexture
        {
            public RetiredRenderTexture(RenderTexture texture, int generation)
            {
                Texture = texture;
                Generation = generation;
            }

            public RenderTexture Texture { get; }
            public int Generation { get; }
        }

        private static void DestroyUnityObject(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
