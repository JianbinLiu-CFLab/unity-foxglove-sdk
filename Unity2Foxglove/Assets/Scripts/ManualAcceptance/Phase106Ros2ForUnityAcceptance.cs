// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Phase106-only ROS2 For Unity standalone pub/sub acceptance component.

using System;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase106 ROS2 For Unity Acceptance")]
public sealed class Phase106Ros2ForUnityAcceptance : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = 1f;
    [SerializeField] private bool _logPublishedMessages = true;

    private float _nextPublishTime;
    private int _publishCount;
    private bool _warnedMissingDefine;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _ros2Node;
    private IPublisher<std_msgs.msg.String> _publisher;
    private ISubscription<std_msgs.msg.String> _subscriber;
    private bool _initializationFailed;
#endif

    private void OnEnable()
    {
        Application.runInBackground = true;
        _nextPublishTime = 0f;
        _publishCount = 0;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        EnsureRos2UnityComponent();
#else
        WarnMissingDefine();
#endif
    }

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (!TryEnsureRos2Ready())
            return;

        EnsureNodeAndEndpoints();
        PublishIfDue();
#else
        WarnMissingDefine();
#endif
    }

    private void OnValidate()
    {
        _publishIntervalSeconds = Mathf.Max(0.1f, _publishIntervalSeconds);
    }

    private void WarnMissingDefine()
    {
        if (_warnedMissingDefine)
            return;

        _warnedMissingDefine = true;
        Debug.LogWarning("[Phase106Ros2ForUnityAcceptance] Import ROS2 For Unity and add UNITY2FOXGLOVE_ROS2_FOR_UNITY before running this smoke.");
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void EnsureRos2UnityComponent()
    {
        var ros2Unity = GetComponent<ROS2UnityComponent>();
        if (ros2Unity == null)
        {
            ros2Unity = gameObject.AddComponent<ROS2UnityComponent>();
        }

        _ros2Unity = ros2Unity;
    }

    private bool TryEnsureRos2Ready()
    {
        if (_initializationFailed)
            return false;

        try
        {
            if (_ros2Unity == null)
                EnsureRos2UnityComponent();

            return _ros2Unity != null && _ros2Unity.Ok();
        }
        catch (Exception ex)
        {
            _initializationFailed = true;
            Debug.LogWarning("[Phase106Ros2ForUnityAcceptance] ROS2 For Unity initialization failed: " + ex.Message);
            return false;
        }
    }

    private void EnsureNodeAndEndpoints()
    {
        if (_ros2Node != null)
            return;

        var ros2Unity = _ros2Unity;
        if (ros2Unity == null || !ros2Unity.Ok())
            return;

        _ros2Node = ros2Unity.CreateNode("unity2foxglove_phase106");
        _publisher = _ros2Node.CreatePublisher<std_msgs.msg.String>("/unity2foxglove/phase106/out");
        _subscriber = _ros2Node.CreateSubscription<std_msgs.msg.String>(
            "/unity2foxglove/phase106/in",
            OnRos2StringReceived);

        Debug.Log("[Phase106Ros2ForUnityAcceptance] ROS2 node ready: unity2foxglove_phase106");
    }

    private void PublishIfDue()
    {
        if (_publisher == null)
            return;

        var now = Time.unscaledTime;
        if (now < _nextPublishTime)
            return;

        _nextPublishTime = now + _publishIntervalSeconds;
        _publishCount++;

        var message = new std_msgs.msg.String
        {
            Data = "phase106 unity publish count=" + _publishCount
        };

        _publisher.Publish(message);

        if (_logPublishedMessages)
            Debug.Log("[Phase106Ros2ForUnityAcceptance] published: " + message.Data);
    }

    private void OnRos2StringReceived(std_msgs.msg.String message)
    {
        Debug.Log("[Phase106Ros2ForUnityAcceptance] received: " + message.Data);
    }
#endif
}
