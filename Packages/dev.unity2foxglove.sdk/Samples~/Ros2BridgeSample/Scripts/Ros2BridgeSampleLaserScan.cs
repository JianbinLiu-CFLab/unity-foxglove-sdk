// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Ros2BridgeSample
// Purpose: Sample-only motion for the laser scan frame.

using UnityEngine;

public sealed class Ros2BridgeSampleLaserScan : MonoBehaviour
{
    [SerializeField] private float _yawDegreesPerSecond = 20f;
    [SerializeField] private float _bobAmplitude = 0.08f;
    [SerializeField] private float _bobSpeed = 1.1f;

    private Vector3 _startPosition;

    private void Awake()
    {
        _startPosition = transform.localPosition;
    }

    private void Update()
    {
        transform.Rotate(Vector3.up, _yawDegreesPerSecond * Time.deltaTime, Space.World);
        transform.localPosition = _startPosition + Vector3.up * (Mathf.Sin(Time.time * _bobSpeed) * _bobAmplitude);
    }
}
