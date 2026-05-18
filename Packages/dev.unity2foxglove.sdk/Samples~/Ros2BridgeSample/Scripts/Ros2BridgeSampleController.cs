// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Ros2BridgeSample
// Purpose: Small sample-only controller for visible ROS2 Bridge motion and status.

using Unity.FoxgloveSDK.Components;
using UnityEngine;

/// <summary>Drives visible motion and status text for the ROS2 Bridge sample scene.</summary>
public sealed class Ros2BridgeSampleController : MonoBehaviour
{
    [SerializeField] private FoxgloveManager _manager;
    [SerializeField] private Transform _movingTarget;
    [SerializeField] private float _motionRadius = 1.25f;
    [SerializeField] private float _motionSpeed = 0.6f;
    [SerializeField] private bool _showStatusOverlay = true;

    private string _status = "ROS2 Bridge sample";

    private void Awake()
    {
        if (_manager == null)
            _manager = FindFirstObjectByType<FoxgloveManager>();
    }

    private void Update()
    {
        if (_movingTarget != null)
        {
            var t = Time.time * _motionSpeed;
            _movingTarget.localPosition = new Vector3(
                Mathf.Cos(t) * _motionRadius,
                0.5f + Mathf.Sin(t * 0.7f) * 0.25f,
                Mathf.Sin(t) * _motionRadius);
            _movingTarget.Rotate(Vector3.up, 40f * Time.deltaTime, Space.World);
        }

        if (_manager != null)
        {
            var stats = _manager.GetRos2BridgeStatsSnapshot();
            _status = $"ROS2 Bridge {(_manager.Ros2BridgeEnabled ? "enabled" : "disabled")} | connected={stats.Connected} | sent={stats.SentFrames} | dropped={stats.DroppedFrames}";
        }
    }

    private void OnGUI()
    {
        if (!_showStatusOverlay)
            return;

        GUI.Label(new Rect(16, 16, 760, 28), _status);
    }
}
