// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Ros2BridgeSample
// Purpose: Creates and animates child transforms consumed by FoxglovePointCloudPublisher.

using UnityEngine;

public sealed class Ros2BridgeSamplePointCloud : MonoBehaviour
{
    [SerializeField, Min(8)] private int _pointCount = 96;
    [SerializeField] private float _width = 4f;
    [SerializeField] private float _height = 1.2f;

    private Transform[] _points;

    private void Awake()
    {
        _points = new Transform[_pointCount];
        for (var i = 0; i < _points.Length; ++i)
        {
            var point = new GameObject("Point " + i.ToString("000"));
            point.transform.SetParent(transform, false);
            _points[i] = point.transform;
        }
    }

    private void Update()
    {
        if (_points == null || _points.Length == 0)
            return;

        var t = Time.time;
        for (var i = 0; i < _points.Length; ++i)
        {
            var u = _points.Length == 1 ? 0f : i / (float)(_points.Length - 1);
            var x = Mathf.Lerp(-_width * 0.5f, _width * 0.5f, u);
            var z = Mathf.Sin(u * Mathf.PI * 4f + t) * 0.7f;
            var y = Mathf.Cos(u * Mathf.PI * 2f + t * 0.8f) * _height * 0.5f;
            _points[i].localPosition = new Vector3(x, y, z);
        }
    }
}
