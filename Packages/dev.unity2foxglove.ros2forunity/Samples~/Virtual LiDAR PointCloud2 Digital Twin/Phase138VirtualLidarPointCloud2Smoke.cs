// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR PointCloud2 Digital Twin
// Purpose: Mirrors VirtualLidar PointCloudFrame output to ROS2 sensor_msgs/msg/PointCloud2 /points.
// Publishes a map → os_lidar static transform so RViz2 can render the cloud.
// All ROS2 references are guarded by #if UNITY2FOXGLOVE_ROS2_FOR_UNITY.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif
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
    [SerializeField] private string _frameId = "os_lidar";
    [SerializeField, Min(0.016f)] private float _publishIntervalSeconds = 0.1f;

    [Header("TF")]
    [SerializeField] private string _parentFrame = "map";

    [Header("Source")]
    [SerializeField] private VirtualLidar _virtualLidar;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _node;
    private IPublisher<sensor_msgs.msg.PointCloud2> _publisher;
    private IPublisher<tf2_msgs.msg.TFMessage> _tfPublisher;
#endif

    private float _nextPublishAt;

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
            _tfPublisher = _node.CreatePublisher<tf2_msgs.msg.TFMessage>("/tf");
        }

        if (_publisher == null || _tfPublisher == null)
            return;

        if (Time.unscaledTime < _nextPublishAt)
            return;
        _nextPublishAt = Time.unscaledTime + _publishIntervalSeconds;

        var frame = _virtualLidar.LastFrame;
        var hasCloud = frame != null && frame.Points.Count > 0;

        // Stamp TF and cloud from the SAME clock so tf2 can resolve the lookup.
        var unixNs = hasCloud ? frame.UnixNs : FoxgloveTimeUtil.NowUnixTimeNs();
        var sec = (int)(unixNs / 1_000_000_000UL);
        var nsec = (uint)(unixNs % 1_000_000_000UL);

        // Republish the transform every tick: R2FU exposes no /tf_static (latched)
        // QoS, so a one-shot /tf would be missed by late RViz2 subscribers and would
        // go stale in the tf2 buffer ("Frame [os_lidar] does not exist").
        PublishTf(sec, nsec);

        if (!hasCloud)
            return;

        _publisher.Publish(Phase138CPointCloud2MessageBuilder.Build(frame, _frameId, sec, nsec));
#else
        Debug.LogWarning("[Phase138VirtualLidarPointCloud2Smoke] ROS2 For Unity not available. Add UNITY2FOXGLOVE_ROS2_FOR_UNITY to project defines.");
        enabled = false;
#endif
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void PublishTf(int sec, uint nsec)
    {
        _tfPublisher.Publish(new tf2_msgs.msg.TFMessage
        {
            Transforms = new[]
            {
                new geometry_msgs.msg.TransformStamped
                {
                    Header = new std_msgs.msg.Header
                    {
                        Stamp = new builtin_interfaces.msg.Time { Sec = sec, Nanosec = nsec },
                        Frame_id = _parentFrame
                    },
                    Child_frame_id = _frameId,
                    Transform = new geometry_msgs.msg.Transform
                    {
                        Translation = new geometry_msgs.msg.Vector3 { X = 0.0, Y = 0.0, Z = 0.0 },
                        Rotation = new geometry_msgs.msg.Quaternion { X = 0.0, Y = 0.0, Z = 0.0, W = 1.0 }
                    }
                }
            }
        });
    }
#endif
}
