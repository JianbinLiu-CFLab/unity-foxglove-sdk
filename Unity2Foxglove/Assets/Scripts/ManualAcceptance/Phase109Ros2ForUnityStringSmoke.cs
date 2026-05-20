// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Phase109 ROS2 For Unity bidirectional string topic smoke component.

using Unity2Foxglove.Ros2ForUnity;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase109 ROS2 For Unity String Smoke")]
public sealed class Phase109Ros2ForUnityStringSmoke : MonoBehaviour
{
    private const string OutTopic = "/unity2foxglove/phase109/out";
    private const string InTopic = "/unity2foxglove/phase109/in";

    [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = 1f;
    [SerializeField] private int _publishedCount;
    [SerializeField] private int _receivedCount;
    [SerializeField] private string _lastReceived = string.Empty;
    [SerializeField] private string _statusMessage = "Not started.";

    private float _nextPublishTime;
    private bool _warnedMissingDefine;
    private bool _loggedFirstPublish;
    private Phase109Ros2ForUnityContext _context;
    private IUnity2FoxgloveRos2Node _node;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private IUnity2FoxgloveRos2Publisher<std_msgs.msg.String> _publisher;
    private IUnity2FoxgloveRos2Subscription _subscription;
#endif

    private void OnEnable()
    {
        Application.runInBackground = true;
        _nextPublishTime = 0f;
        _publishedCount = 0;
        _receivedCount = 0;
        _lastReceived = string.Empty;
        _loggedFirstPublish = false;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        _context = Phase109Ros2ForUnityContextFactory.Create(gameObject);
        _statusMessage = _context.StatusMessage;
#else
        WarnMissingDefine();
#endif
    }

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (_context == null)
            _context = Phase109Ros2ForUnityContextFactory.Create(gameObject);

        _context.DrainPendingCallbacks();
        if (!_context.TryEnsureReady())
        {
            _statusMessage = _context.StatusMessage;
            return;
        }

        EnsureEndpoints();
        PublishIfDue();
#else
        WarnMissingDefine();
#endif
    }

    private void OnDisable()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        _subscription?.Dispose();
        _subscription = null;
        _publisher?.Dispose();
        _publisher = null;
#endif
        _node?.Dispose();
        _node = null;
        _context?.Dispose();
        _context = null;
    }

    private void OnValidate()
    {
        _publishIntervalSeconds = Mathf.Max(0.1f, _publishIntervalSeconds);
    }

    private void WarnMissingDefine()
    {
        _statusMessage = "Import ROS2 For Unity and add UNITY2FOXGLOVE_ROS2_FOR_UNITY before running Phase109.";
        if (_warnedMissingDefine)
            return;

        _warnedMissingDefine = true;
        Debug.LogWarning("[Phase109Ros2ForUnityStringSmoke] " + _statusMessage);
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void EnsureEndpoints()
    {
        if (_publisher != null && _subscription != null)
            return;

        if (_node == null)
            _node = _context.CreateNode("unity2foxglove_phase109");

        _publisher = _node.CreatePublisher<std_msgs.msg.String>(OutTopic);
        _subscription = _node.CreateSubscription<std_msgs.msg.String>(InTopic, OnStringReceived);
        _statusMessage = "Phase109 ROS2 For Unity string smoke ready.";
    }

    private void PublishIfDue()
    {
        if (_publisher == null)
            return;

        var now = Time.unscaledTime;
        if (now < _nextPublishTime)
            return;

        _nextPublishTime = now + _publishIntervalSeconds;
        var message = new std_msgs.msg.String
        {
            Data = "phase109 unity tick " + (_publishedCount + 1)
        };

        if (!_publisher.TryPublish(message, out var error))
        {
            _statusMessage = error;
            return;
        }

        _publishedCount++;
        _statusMessage = "Published " + _publishedCount + " Phase109 string message(s).";
        if (!_loggedFirstPublish)
        {
            _loggedFirstPublish = true;
            Debug.Log("[Phase109Ros2ForUnityStringSmoke] first publish: " + message.Data);
        }
    }

    private void OnStringReceived(std_msgs.msg.String message)
    {
        _receivedCount++;
        _lastReceived = message.Data;
        _statusMessage = "Received " + _receivedCount + " Phase109 string message(s).";
        Debug.Log("[Phase109Ros2ForUnityStringSmoke] received: " + message.Data);
    }
#endif
}
