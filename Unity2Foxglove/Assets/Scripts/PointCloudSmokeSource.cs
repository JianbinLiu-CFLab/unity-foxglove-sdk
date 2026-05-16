// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/PointCloudSmoke
// Purpose: Generates a deterministic dense point cloud for manual QoS smoke tests.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using UnityEngine;

/// <summary>
/// Generates a synthetic point cloud and feeds it into the colocated
/// <see cref="FoxglovePointCloudPublisher"/>. This is a demo smoke source for
/// validating point-cloud QoS modes in Foxglove's 3D panel.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FoxglovePointCloudPublisher))]
[AddComponentMenu("Foxglove/Smoke/PointCloud Smoke Source")]
public class PointCloudSmokeSource : MonoBehaviour
{
    [SerializeField] private FoxglovePointCloudPublisher _publisher;
    [SerializeField, Min(1)] private int _pointCount = 1000;
    [SerializeField] private string _frameId = "unity_world";
    [SerializeField, Min(1)] private int _columns = 50;
    [SerializeField, Min(0.001f)] private float _spacingMeters = 0.08f;
    [SerializeField, Min(0f)] private float _waveHeightMeters = 0.35f;
    [SerializeField] private bool _animate = true;
    [SerializeField, Min(0f)] private float _animationSpeed = 1f;
    [SerializeField] private bool _includeIntensity;
    [SerializeField] private bool _publishContinuously = true;
    [SerializeField, Min(0f)] private float _sourceHz = 10f;

    private float _nextFrameTime;

    private void Reset()
    {
        _publisher = GetComponent<FoxglovePointCloudPublisher>();
    }

    private void Awake()
    {
        if (_publisher == null)
            _publisher = GetComponent<FoxglovePointCloudPublisher>();
    }

    private void OnEnable()
    {
        _nextFrameTime = 0f;
        PushFrame();
    }

    private void Update()
    {
        if (!_publishContinuously || _publisher == null)
            return;

        var now = Time.unscaledTime;
        if (_sourceHz > 0f && now < _nextFrameTime)
            return;

        _nextFrameTime = _sourceHz > 0f ? now + 1f / _sourceHz : now;
        PushFrame();
    }

    private void OnValidate()
    {
        _pointCount = Mathf.Max(1, _pointCount);
        _columns = Mathf.Max(1, _columns);
        _spacingMeters = Mathf.Max(0.001f, _spacingMeters);
        _waveHeightMeters = Mathf.Max(0f, _waveHeightMeters);
        _animationSpeed = Mathf.Max(0f, _animationSpeed);
        _sourceHz = Mathf.Max(0f, _sourceHz);

        if (_publisher == null)
            _publisher = GetComponent<FoxglovePointCloudPublisher>();
    }

    /// <summary>
    /// Pushes one synthetic frame to the point-cloud publisher.
    /// </summary>
    public void PushFrame()
    {
        if (_publisher == null)
            return;

        var phase = _animate ? Time.unscaledTime * _animationSpeed : 0f;
        _publisher.SetFrame(BuildFrame(phase));
    }

    private PointCloudFrame BuildFrame(float phase)
    {
        var count = Mathf.Max(1, _pointCount);
        var columns = Mathf.Max(1, _columns);
        var rows = Mathf.CeilToInt(count / (float)columns);
        var xOrigin = (columns - 1) * 0.5f;
        var yOrigin = (rows - 1) * 0.5f;

        var frame = new PointCloudFrame
        {
            FrameId = string.IsNullOrEmpty(_frameId) ? "unity_world" : _frameId
        };

        for (var index = 0; index < count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var x = (column - xOrigin) * _spacingMeters;
            var y = (row - yOrigin) * _spacingMeters;
            var z = Mathf.Sin(column * 0.37f + row * 0.19f + phase) * _waveHeightMeters;

            var point = new PointCloudPoint(x, y, z);
            if (_includeIntensity)
                point.Intensity = count == 1 ? 1f : index / (float)(count - 1);

            frame.Points.Add(point);
        }

        return frame;
    }
}
