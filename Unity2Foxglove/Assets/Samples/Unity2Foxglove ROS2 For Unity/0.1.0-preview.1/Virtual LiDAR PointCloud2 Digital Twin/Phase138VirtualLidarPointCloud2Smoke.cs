// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Virtual LiDAR PointCloud2 Digital Twin
// Purpose: Publishes prepared PointCloud2 native frames to ROS2 DDS via ROS2 For Unity.
// All ROS2 references are guarded by #if UNITY2FOXGLOVE_ROS2_FOR_UNITY.

using System;
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Virtual LiDAR PointCloud2 Native")]
public sealed class Phase138VirtualLidarPointCloud2Smoke : MonoBehaviour
{
    private const string LogPrefix = "[Phase138VirtualLidarPointCloud2Smoke]";

    [Header("ROS2")]
    [SerializeField] private string _nodeName = "phase138_virtual_lidar";
    [SerializeField] private string _topic = "/points";
    [SerializeField] private string _fallbackFrameId = "os_lidar";
    [SerializeField, Min(0.016f)] private float _publishIntervalSeconds = 0.1f;
    [SerializeField] private bool _copyDataBeforePublish;

    [Header("TF")]
    [SerializeField] private string _parentFrame = "map";

    [Header("Source")]
    [SerializeField] private VirtualLidar _virtualLidar;
    [SerializeField] private FoxglovePointCloudPublisher _pointCloudPublisher;

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Not started.";
    [SerializeField] private string _lastError = string.Empty;
    [SerializeField] private int _publishedPointCloudCount;
    [SerializeField] private int _publishedTfCount;
    [SerializeField] private int _droppedFrameCount;
    [SerializeField] private int _validPointCount;
    [SerializeField] private int _payloadBytes;
    [SerializeField] private uint _pointStep;
    [SerializeField] private uint _rowStep;
    [SerializeField] private double _lastPublishCallMs;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _node;
    private IPublisher<sensor_msgs.msg.PointCloud2> _publisher;
    private IPublisher<tf2_msgs.msg.TFMessage> _tfPublisher;
    private bool _ownsRos2UnityComponent;
#endif

    private float _nextPublishAt;
    private bool _subscribed;
    private bool _warnedMissingDefine;
    private bool _endpointsLogged;

    private void OnEnable()
    {
        Application.runInBackground = true;
        ResetStatus();
        ResolveComponents();
        SubscribeToNativeFrames();

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        EnsureRos2UnityComponent();
#else
        WarnMissingDefine();
#endif
    }

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (_subscribed)
            TryEnsureRos2Ready();
#else
        WarnMissingDefine();
#endif
    }

    private void OnDisable()
    {
        UnsubscribeFromNativeFrames();
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        CleanupRuntime();
#endif
    }

    private void OnDestroy()
    {
        UnsubscribeFromNativeFrames();
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        CleanupRuntime();
#endif
    }

    private void OnValidate()
    {
        _publishIntervalSeconds = Mathf.Max(0.016f, _publishIntervalSeconds);
    }

    private void ResetStatus()
    {
        _nextPublishAt = 0f;
        _statusMessage = "Waiting for PointCloud2 Native frames.";
        _lastError = string.Empty;
        _publishedPointCloudCount = 0;
        _publishedTfCount = 0;
        _droppedFrameCount = 0;
        _validPointCount = 0;
        _payloadBytes = 0;
        _pointStep = 0;
        _rowStep = 0;
        _lastPublishCallMs = 0d;
        _endpointsLogged = false;
    }

    private void ResolveComponents()
    {
        if (_virtualLidar == null)
            _virtualLidar = GetComponentInChildren<VirtualLidar>();
        if (_virtualLidar == null)
            _virtualLidar = FindFirstObjectByType<VirtualLidar>();

        if (_pointCloudPublisher == null)
            _pointCloudPublisher = GetComponentInParent<FoxglovePointCloudPublisher>();
        if (_pointCloudPublisher == null)
            _pointCloudPublisher = GetComponent<FoxglovePointCloudPublisher>();
        if (_pointCloudPublisher == null)
            _pointCloudPublisher = GetComponentInChildren<FoxglovePointCloudPublisher>();
        if (_pointCloudPublisher == null && _virtualLidar != null)
            _pointCloudPublisher = _virtualLidar.GetComponentInParent<FoxglovePointCloudPublisher>();
        if (_pointCloudPublisher == null)
            _pointCloudPublisher = FindFirstObjectByType<FoxglovePointCloudPublisher>();
    }

    private void SubscribeToNativeFrames()
    {
        if (_pointCloudPublisher == null)
        {
            _lastError = "Assign a FoxglovePointCloudPublisher configured for PointCloud2 Native.";
            _statusMessage = _lastError;
            Debug.LogWarning(LogPrefix + " " + _lastError);
            return;
        }

        if (_subscribed)
            return;

        _pointCloudPublisher.PointCloud2NativeFrameReady += OnPointCloud2NativeFrameReady;
        _subscribed = true;
        _statusMessage = "Subscribed to PointCloud2 Native frames.";
    }

    private void UnsubscribeFromNativeFrames()
    {
        if (!_subscribed || _pointCloudPublisher == null)
            return;

        _pointCloudPublisher.PointCloud2NativeFrameReady -= OnPointCloud2NativeFrameReady;
        _subscribed = false;
    }

    private void OnPointCloud2NativeFrameReady(PointCloud2NativeFrame frame)
    {
        if (frame == null)
            return;

        if (Time.unscaledTime < _nextPublishAt)
        {
            _droppedFrameCount++;
            return;
        }

        _nextPublishAt = Time.unscaledTime + _publishIntervalSeconds;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (!TryEnsureRos2Ready())
        {
            _droppedFrameCount++;
            return;
        }

        var childFrame = string.IsNullOrWhiteSpace(frame.FrameId) ? _fallbackFrameId : frame.FrameId;
        var sec = (int)(frame.UnixNs / 1_000_000_000UL);
        var nsec = (uint)(frame.UnixNs % 1_000_000_000UL);
        PublishTf(childFrame, sec, nsec);

        var message = Phase138CPointCloud2MessageBuilder.Build(frame, _copyDataBeforePublish);
        var publishStart = Stopwatch.GetTimestamp();
        _publisher.Publish(message);
        _lastPublishCallMs = (Stopwatch.GetTimestamp() - publishStart) * 1000d / Stopwatch.Frequency;

        _publishedPointCloudCount++;
        _validPointCount = frame.ValidCount;
        _payloadBytes = frame.Data.Length;
        _pointStep = frame.PointStep;
        _rowStep = frame.RowStep;
        _statusMessage = "Published " + _publishedPointCloudCount + " PointCloud2 native frame(s).";
#else
        _droppedFrameCount++;
        WarnMissingDefine();
#endif
    }

    private void WarnMissingDefine()
    {
        _lastError = "Import ROS2 For Unity and add UNITY2FOXGLOVE_ROS2_FOR_UNITY before running this sample.";
        _statusMessage = _lastError;
        if (_warnedMissingDefine)
            return;

        _warnedMissingDefine = true;
        Debug.LogWarning(LogPrefix + " " + _statusMessage);
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void EnsureRos2UnityComponent()
    {
        if (_ros2Unity != null)
            return;

        _ros2Unity = GetComponent<ROS2UnityComponent>();
        if (_ros2Unity == null)
        {
            _ros2Unity = gameObject.AddComponent<ROS2UnityComponent>();
            _ownsRos2UnityComponent = true;
        }
    }

    private bool TryEnsureRos2Ready()
    {
        if (!Ros2NativeOutputPolicy.Enabled)
        {
            _statusMessage = "ROS2 Native output is disabled in FoxgloveManager.";
            return false;
        }

        EnsureRos2UnityComponent();
        if (_ros2Unity == null || !_ros2Unity.Ok())
        {
            _statusMessage = "Waiting for ROS2 For Unity runtime.";
            return false;
        }

        if (_node == null)
            _node = _ros2Unity.CreateNode(_nodeName);
        if (_publisher == null)
            _publisher = _node.CreatePublisher<sensor_msgs.msg.PointCloud2>(_topic);
        if (_tfPublisher == null)
            _tfPublisher = _node.CreatePublisher<tf2_msgs.msg.TFMessage>("/tf");

        if (!_endpointsLogged)
        {
            _endpointsLogged = true;
            Debug.Log(LogPrefix + " publishing " + _topic + " as sensor_msgs/msg/PointCloud2");
        }

        return _publisher != null && _tfPublisher != null;
    }

    private void PublishTf(string childFrame, int sec, uint nsec)
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
                    Child_frame_id = childFrame,
                    Transform = new geometry_msgs.msg.Transform
                    {
                        Translation = new geometry_msgs.msg.Vector3 { X = 0.0, Y = 0.0, Z = 0.0 },
                        Rotation = new geometry_msgs.msg.Quaternion { X = 0.0, Y = 0.0, Z = 0.0, W = 1.0 }
                    }
                }
            }
        });
        _publishedTfCount++;
    }

    private void CleanupRuntime()
    {
        if (_node != null && _tfPublisher != null)
        {
            try { _node.RemovePublisher<tf2_msgs.msg.TFMessage>(_tfPublisher); }
            catch (Exception) { }
        }

        if (_node != null && _publisher != null)
        {
            try { _node.RemovePublisher<sensor_msgs.msg.PointCloud2>(_publisher); }
            catch (Exception) { }
        }

        if (_ros2Unity != null && _node != null)
        {
            try { _ros2Unity.RemoveNode(_node); }
            catch (Exception) { }
        }

        _tfPublisher = null;
        _publisher = null;
        _node = null;
        if (_ownsRos2UnityComponent && _ros2Unity != null)
            Destroy(_ros2Unity);
        _ros2Unity = null;
        _ownsRos2UnityComponent = false;
    }
#endif
}
