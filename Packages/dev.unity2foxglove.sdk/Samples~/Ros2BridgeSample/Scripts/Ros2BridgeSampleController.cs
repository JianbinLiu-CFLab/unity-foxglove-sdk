// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Ros2BridgeSample
// Purpose: Small sample-only controller for visible ROS2 Bridge motion and status.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Ros2Bridge;
using UnityEngine;

/// <summary>Drives visible motion and status text for the ROS2 Bridge sample scene.</summary>
public sealed class Ros2BridgeSampleController : MonoBehaviour
{
    [SerializeField] private FoxgloveManager _manager;
    [SerializeField] private Transform _movingTarget;
    [SerializeField] private float _motionRadius = 1.25f;
    [SerializeField] private float _motionSpeed = 0.6f;
    [SerializeField] private bool _showStatusOverlay = true;
    [SerializeField] private float _statusRefreshSeconds = 0.25f;

    private string _status = "ROS2 Bridge sample";
    private float _statusRefreshTimer;
    private bool _lastRos2BridgeEnabled;
    private bool _lastConnected;
    private long _lastSentFrames;
    private long _lastDroppedFrames;
    private bool _hasStatusSnapshot;

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
            _statusRefreshTimer -= Time.deltaTime;
            if (_statusRefreshTimer <= 0f)
            {
                _statusRefreshTimer = Mathf.Max(0.05f, _statusRefreshSeconds);
                var stats = _manager.GetRos2BridgeStatsSnapshot();
                UpdateStatusIfChanged(stats, _manager.Ros2BridgeEnabled);
            }
        }
    }

    private void UpdateStatusIfChanged(Ros2BridgeStatsSnapshot stats, bool ros2BridgeEnabled)
    {
        if (_hasStatusSnapshot
            && _lastRos2BridgeEnabled == ros2BridgeEnabled
            && _lastConnected == stats.Connected
            && _lastSentFrames == stats.SentFrames
            && _lastDroppedFrames == stats.DroppedFrames)
        {
            return;
        }

        _hasStatusSnapshot = true;
        _lastRos2BridgeEnabled = ros2BridgeEnabled;
        _lastConnected = stats.Connected;
        _lastSentFrames = stats.SentFrames;
        _lastDroppedFrames = stats.DroppedFrames;
        _status = $"ROS2 Bridge {(ros2BridgeEnabled ? "enabled" : "disabled")} | connected={stats.Connected} | sent={stats.SentFrames} | dropped={stats.DroppedFrames}";
    }

    private void OnGUI()
    {
        if (!_showStatusOverlay)
            return;

        GUI.Label(new Rect(16, 16, 760, 28), _status);
    }
}
