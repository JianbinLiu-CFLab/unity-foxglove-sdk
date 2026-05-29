// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR PointCloud2 Digital Twin
// Purpose: Mirrors VirtualLidar PointCloudFrame output to ROS2 sensor_msgs/msg/PointCloud2 /points.
// All ROS2 references are guarded by #if UNITY2FOXGLOVE_ROS2_FOR_UNITY.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif
using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Virtual LiDAR PointCloud2 Mirror")]
public sealed class Phase138VirtualLidarPointCloud2Smoke : MonoBehaviour
{
    [Header("ROS2")]
    [SerializeField] private string _nodeName = "phase138_virtual_lidar";
    [SerializeField] private string _topic = "/points";
    [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = 0.1f;

    [Header("Source")]
    [SerializeField] private VirtualLidar _virtualLidar;

    #if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _node;
    private IPublisher<sensor_msgs.msg.PointCloud2> _publisher;
    #endif

    private float _nextPublishAt;
    private PointCloudFrame _lastFrame;
    private bool _hasFrame;

    private void Start()
    {
        if (_virtualLidar == null)
            _virtualLidar = GetComponent<VirtualLidar>();
        if (_virtualLidar == null)
        {
            Debug.LogError("[Phase138VirtualLidarPointCloud2Smoke] VirtualLidar component not assigned or found.");
            enabled = false;
            return;
        }

        #if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        _ros2Unity = GetComponent<ROS2UnityComponent>();
        if (_ros2Unity == null)
            _ros2Unity = gameObject.AddComponent<ROS2UnityComponent>();
        #endif
    }

    private void Update()
    {
        #if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (_ros2Unity == null || !_ros2Unity.Ok())
            return;

        if (_node == null)
        {
            _node = _ros2Unity.CreateNode(_nodeName);
            _publisher = _node.CreatePublisher<sensor_msgs.msg.PointCloud2>(_topic);
        }

        if (_publisher == null)
            return;

        // Cache the most recent frame from VirtualLidar
        var frame = _virtualLidar.LastFrame;
        if (frame != null && frame.Points.Count > 0)
        {
            _lastFrame = frame;
            _hasFrame = true;
        }

        if (!_hasFrame || _publisher == null || Time.unscaledTime < _nextPublishAt)
            return;

        var now = DateTimeOffset.UtcNow;
        var sec = (int)now.ToUnixTimeSeconds();
        var nsec = (uint)(now.ToUnixTimeMilliseconds() % 1000 * 1000000);

        var msg = Phase129PointCloud2MessageBuilder.Build(_lastFrame, sec, nsec);
        _publisher.Publish(msg);
        _nextPublishAt = Time.unscaledTime + _publishIntervalSeconds;
        #else
        Debug.LogWarning("[Phase138VirtualLidarPointCloud2Smoke] ROS2 For Unity not available. Add UNITY2FOXGLOVE_ROS2_FOR_UNITY to project defines.");
        enabled = false;
        #endif
    }
}
