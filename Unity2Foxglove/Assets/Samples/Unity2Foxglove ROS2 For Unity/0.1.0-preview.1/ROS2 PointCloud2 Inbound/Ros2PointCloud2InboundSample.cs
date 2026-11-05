// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/ROS2 PointCloud2 Inbound
// Purpose: Minimal main-thread PointCloud2 inbound acceptance consumer.

using UnityEngine;
using Unity.FoxgloveSDK.Schemas.PointCloud;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

/// <summary>Logs received PointCloud2 counts and draws the latest XYZ bounds.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/PointCloud2 Inbound Sample")]
public sealed class Ros2PointCloud2InboundSample : MonoBehaviour
{
    [SerializeField] private float _logIntervalSeconds = 1f;
    [SerializeField] private string _status = "Waiting for PointCloud2 inbound bridge.";
    [SerializeField] private long _receivedFrames;
    [SerializeField] private long _receivedPoints;
    [SerializeField] private long _droppedFrames;
    [SerializeField] private Bounds _latestBounds;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
    [SerializeField] private Ros2ForUnityPointCloud2InboundBridge _bridge;
    private float _nextLogAt;
    private bool _subscribed;

    private void OnEnable()
    {
        if (_bridge == null)
            _bridge = GetComponent<Ros2ForUnityPointCloud2InboundBridge>();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (!_subscribed)
            Subscribe();
    }

    private void Subscribe()
    {
        if (_subscribed || _bridge == null)
            return;
        _bridge.FrameReceived += OnFrameReceived;
        _subscribed = true;
        _status = "Subscribed to PointCloud2 inbound frames.";
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        if (_bridge != null)
            _bridge.FrameReceived -= OnFrameReceived;
        _subscribed = false;
    }

    private void OnFrameReceived(PackedPointCloudFrame frame)
    {
        _receivedFrames++;
        _receivedPoints += frame.ValidCount;
        if (TryCalculateBounds(frame, out var bounds))
            _latestBounds = bounds;

        var stats = _bridge.Stats;
        _droppedFrames = stats.Dropped;
        if (Time.unscaledTime >= _nextLogAt)
        {
            _nextLogAt = Time.unscaledTime + Mathf.Max(0.1f, _logIntervalSeconds);
            _status = $"Received {_receivedFrames} frame(s), {_receivedPoints} point(s), drops {_droppedFrames}.";
            Debug.Log("[Foxglove][PointCloud2InboundSample] " + _status, this);
        }
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (_receivedFrames == 0)
            return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(_latestBounds.center, _latestBounds.size);
    }

    private static bool TryCalculateBounds(PackedPointCloudFrame frame, out Bounds bounds)
    {
        bounds = default;
        if (!TryFindField(frame, "x", out var x)
            || !TryFindField(frame, "y", out var y)
            || !TryFindField(frame, "z", out var z))
            return false;

        var found = false;
        for (var index = 0; index < frame.ValidCount; index++)
        {
            var baseOffset = checked(index * (int)frame.PointStep);
            if (!TryReadFloat(frame.Data, baseOffset + (int)x.Offset, out var pointX)
                || !TryReadFloat(frame.Data, baseOffset + (int)y.Offset, out var pointY)
                || !TryReadFloat(frame.Data, baseOffset + (int)z.Offset, out var pointZ))
                continue;

            var point = new Vector3(pointX, pointY, pointZ);
            if (!found)
            {
                bounds = new Bounds(point, Vector3.zero);
                found = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }

        return found;
    }

    private static bool TryFindField(PackedPointCloudFrame frame, string name, out PointCloudPackedField field)
    {
        foreach (var candidate in frame.Fields)
        {
            if (candidate.Name == name && candidate.Type == PointCloudPackedNumericType.Float32)
            {
                field = candidate;
                return true;
            }
        }

        field = default;
        return false;
    }

    private static bool TryReadFloat(byte[] data, int offset, out float value)
    {
        if (offset < 0 || offset > data.Length - sizeof(float))
        {
            value = 0f;
            return false;
        }

        value = System.BitConverter.ToSingle(data, offset);
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
