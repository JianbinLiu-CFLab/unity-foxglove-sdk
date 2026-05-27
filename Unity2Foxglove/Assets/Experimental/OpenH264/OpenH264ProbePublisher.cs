// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove/Assets/Experimental/OpenH264
// Purpose: Demo-only OpenH264 camera probe for Phase 80 source spike.

using System;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Demo-only probe that sends Unity camera frames through a locally built
/// OpenH264 helper process and republishes H.264 access units to Foxglove.
/// </summary>
[AddComponentMenu("Foxglove/Experimental/OpenH264 Source Probe Publisher")]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class OpenH264ProbePublisher : FoxglovePublisherBase
{
    private const string ProbeTopic = "/unity/camera/openh264_probe";
    private const string ProbeSchema = "foxglove.CompressedVideo";

    [Header("OpenH264 Probe")]
    [SerializeField] private string _helperExecutablePath = "";
    [SerializeField] private string _frameId = "unity_camera_openh264_probe";
    [SerializeField, Range(2, OpenH264ProbeSidecarOptions.MaxDimension)] private int _width = 640;
    [SerializeField, Range(2, OpenH264ProbeSidecarOptions.MaxDimension)] private int _height = 480;
    [SerializeField, Min(1)] private int _targetFrameRate = 30;
    [SerializeField, Min(1)] private int _bitrateKbps = 4000;
    [SerializeField, Min(1)] private int _keyframeInterval = 30;
    [SerializeField, Min(1)] private int _maxPendingReadbacks = 1;
    [SerializeField, Min(1)] private int _maxInputQueue = 2;
    [SerializeField, Min(1)] private int _maxOutputQueue = 4;
    [SerializeField] private bool _logDiagnostics;

    [Header("Read-only Counters")]
    [SerializeField] private int _framesCaptured;
    [SerializeField] private int _framesSubmitted;
    [SerializeField] private int _accessUnitsReceived;
    [SerializeField] private int _publishedMessages;
    [SerializeField] private int _droppedInputFrames;
    [SerializeField] private int _invalidAccessUnits;
    [SerializeField] private string _lastHelperError = "";
    [SerializeField] private string _lastHelperStderr = "";

    private Camera _sourceCamera;
    private Camera _captureCamera;
    private RenderTexture _captureTexture;
    private OpenH264ProbeSidecar _sidecar;
    private int _pendingRequests;
    private bool _destroyed;
    private int _captureGeneration;
    private bool _cleanupWhenReadbacksDrain;
    private int _sidecarWidth;
    private int _sidecarHeight;
    private bool _warnedUnavailable;
    private bool _warnedConversionFailure;

    protected override string SchemaName => ProbeSchema;
    public override bool SupportsJsonEncoding => false;
    public override bool SupportsProtobufEncoding => true;

    protected override void Reset()
    {
        base.Reset();
        _topic = ProbeTopic;
        _encodingOverride = PublisherEncodingOverride.Protobuf;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _destroyed = false;
        _cleanupWhenReadbacksDrain = false;
        _captureGeneration++;
        _warnedUnavailable = false;
        _warnedConversionFailure = false;
        if (string.IsNullOrEmpty(_topic))
            _topic = ProbeTopic;

        _encodingOverride = PublisherEncodingOverride.Protobuf;
        _sourceCamera = GetComponent<Camera>();
        if (TryGetProbeFrameLayout(out var width, out var height, out _, out var error))
            EnsureCaptureResources(width, height);
        else
            LogUnavailable(error);
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

    private void LateUpdate()
    {
        DrainAccessUnits();

        if (!_publishOnEnable)
            return;

        if (_manager == null)
            ResolveManager();

        if (_manager == null)
            return;

        if (!ShouldPublishNow())
            return;

        var maxPending = Math.Max(1, _maxPendingReadbacks);
        if (_pendingRequests >= maxPending)
            return;

        if (!ShouldPreparePublishPayload(PublisherEffectiveEncoding.Protobuf))
            return;

        if (!ValidateProbeConfig(out var width, out var height, out var i420Bytes))
            return;

        if (!EnsureSidecarStarted(width, height))
            return;

        EnsureCaptureResources(width, height);
        _captureCamera.Render();
        var generation = _captureGeneration;
        _pendingRequests++;
        AsyncGPUReadback.Request(_captureTexture, 0, TextureFormat.RGB24,
            request => OnReadbackComplete(request, generation, width, height, i420Bytes));
    }

    private void OnReadbackComplete(
        AsyncGPUReadbackRequest request,
        int generation,
        int width,
        int height,
        int i420Bytes)
    {
        CompletePendingReadback();

        if (_destroyed || !isActiveAndEnabled || generation != _captureGeneration)
            return;

        if (request.hasError)
        {
            Debug.LogWarning("[Foxglove] OpenH264 probe AsyncGPUReadback failed.");
            return;
        }

        var rgb = request.GetData<byte>().ToArray();
        var i420 = new byte[i420Bytes];
        if (!TryConvertRgb24ToI420(rgb, width, height, i420, out var error))
        {
            LogConversionFailure(error);
            return;
        }

        _framesCaptured++;
        var sidecar = _sidecar;
        if (sidecar == null || !sidecar.IsRunning)
        {
            LogUnavailable("OpenH264 helper is not running.");
            return;
        }

        if (!sidecar.TrySubmitFrame(i420))
        {
            LogUnavailable(sidecar.LastError ?? "OpenH264 helper refused the frame.");
            return;
        }

        _framesSubmitted = sidecar.FramesSubmitted;
        _droppedInputFrames = sidecar.DroppedInputFrames;
        DrainAccessUnits();
    }

    private bool EnsureSidecarStarted(int width, int height)
    {
        if (_sidecar != null && _sidecar.IsRunning && _sidecarWidth == width && _sidecarHeight == height)
            return true;

        StopSidecar();
        var options = new OpenH264ProbeSidecarOptions
        {
            HelperExecutablePath = _helperExecutablePath,
            Width = width,
            Height = height,
            FrameRate = Math.Max(1, _targetFrameRate),
            BitrateKbps = Math.Max(1, _bitrateKbps),
            KeyframeInterval = Math.Max(1, _keyframeInterval),
            MaxInputQueue = Math.Max(1, _maxInputQueue),
            MaxOutputQueue = Math.Max(1, _maxOutputQueue)
        };

        _sidecar = new OpenH264ProbeSidecar();
        if (_sidecar.Start(options))
        {
            _sidecarWidth = width;
            _sidecarHeight = height;
            _warnedUnavailable = false;
            return true;
        }

        LogUnavailable(_sidecar.LastError ?? "Failed to start OpenH264 helper.");
        StopSidecar();
        return false;
    }

    private void DrainAccessUnits()
    {
        var sidecar = _sidecar;
        if (sidecar == null)
            return;

        while (sidecar.TryDequeueAccessUnit(out var accessUnit))
        {
            _accessUnitsReceived = sidecar.AccessUnitsReceived;
            if (!H264AnnexBAccessUnitPacketizer.LooksLikeDecodableH264AccessUnit(accessUnit))
            {
                _invalidAccessUnits++;
                if (_logDiagnostics)
                    Debug.LogWarning("[Foxglove] OpenH264 probe dropped a non-decodable H.264 access unit.");
                continue;
            }

            var unixNs = CurrentLogTimeNs;
            var payload = CameraCompressedVideoBuilder.Serialize(
                unixNs,
                _frameId,
                accessUnit,
                CameraCompressedVideoBuilder.H264Format);
            PublishProto(payload, unixNs);
            _publishedMessages++;
        }

        _lastHelperError = sidecar.LastError ?? "";
        _lastHelperStderr = sidecar.LastStderrLine ?? "";
        if (_logDiagnostics && !string.IsNullOrEmpty(_lastHelperStderr))
            Debug.LogWarning("[Foxglove] OpenH264 helper: " + _lastHelperStderr);
    }

    public static bool TryConvertRgb24ToI420(
        byte[] rgb24,
        int width,
        int height,
        byte[] i420,
        out string error)
        => Rgb24ToI420Converter.TryConvertRgb24ToI420(
            rgb24,
            width,
            height,
            i420,
            flipVertical: true,
            out error);

    private void EnsureCaptureResources(int width, int height)
    {
        _sourceCamera = _sourceCamera != null ? _sourceCamera : GetComponent<Camera>();

        if (_captureTexture == null || _captureTexture.width != width || _captureTexture.height != height)
        {
            if (_captureTexture != null)
                _captureTexture.Release();

            _captureTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            _captureTexture.Create();
        }

        if (_captureCamera == null)
        {
            var go = new GameObject("_OpenH264ProbeCaptureCamera");
            go.transform.SetParent(transform, false);
            _captureCamera = go.AddComponent<Camera>();
            _captureCamera.enabled = false;
        }

        _captureCamera.CopyFrom(_sourceCamera);
        _captureCamera.targetTexture = _captureTexture;
        _captureCamera.enabled = false;
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

    private void CleanupResources()
    {
        if (_captureTexture != null)
        {
            _captureTexture.Release();
            Destroy(_captureTexture);
            _captureTexture = null;
        }

        if (_captureCamera != null)
        {
            Destroy(_captureCamera.gameObject);
            _captureCamera = null;
        }
    }

    private void StopSidecar()
    {
        if (_sidecar == null)
            return;

        _sidecar.Dispose();
        _sidecar = null;
        _sidecarWidth = 0;
        _sidecarHeight = 0;
    }

    private void LogUnavailable(string message)
    {
        _lastHelperError = message ?? "";
        if (_warnedUnavailable)
            return;

        _warnedUnavailable = true;
        Debug.LogWarning("[Foxglove] OpenH264 probe disabled: " + _lastHelperError);
    }

    private void LogConversionFailure(string message)
    {
        _lastHelperError = message ?? "";
        if (_warnedConversionFailure)
            return;

        _warnedConversionFailure = true;
        Debug.LogWarning("[Foxglove] OpenH264 probe conversion failed: " + _lastHelperError);
    }

    private bool ValidateProbeConfig(out int width, out int height, out int i420Bytes)
    {
        width = 0;
        height = 0;
        i420Bytes = 0;

        if (string.IsNullOrWhiteSpace(_helperExecutablePath))
        {
            LogUnavailable("OpenH264 helper executable path is empty.");
            return false;
        }

        if ((_width % 2) != 0 || (_height % 2) != 0)
        {
            LogUnavailable("OpenH264 probe requires even width and height for I420 conversion.");
            return false;
        }

        if (!TryGetProbeFrameLayout(out width, out height, out i420Bytes, out var error))
        {
            LogUnavailable(error);
            return false;
        }

        return true;
    }

    private bool TryGetProbeFrameLayout(out int width, out int height, out int i420Bytes, out string error)
    {
        width = PositiveDimension(_width);
        height = PositiveDimension(_height);
        return OpenH264ProbeSidecarOptions.TryComputeFrameByteCount(width, height, out i420Bytes, out error);
    }

    private static int PositiveDimension(int value)
        => Math.Max(2, value);
}
