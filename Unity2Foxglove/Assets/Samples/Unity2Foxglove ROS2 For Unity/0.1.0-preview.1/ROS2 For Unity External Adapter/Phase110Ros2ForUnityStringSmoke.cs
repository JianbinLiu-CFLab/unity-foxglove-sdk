// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Phase110 ROS2 For Unity bidirectional string topic smoke component.

using Unity2Foxglove.Ros2ForUnity;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Phase110 String Smoke")]
public sealed class Phase110Ros2ForUnityStringSmoke : MonoBehaviour
{
    private const string OutTopic = "/unity2foxglove/ros2forunity/string/out";
    private const string InTopic = "/unity2foxglove/ros2forunity/string/in";
    private const string NodeName = "unity2foxglove_phase110";

    [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = 1f;
    [SerializeField] private bool _useDirectR2fu;
    [SerializeField] private string _nodeName = NodeName;
    [SerializeField] private string _outTopic = OutTopic;
    [SerializeField] private string _inTopic = InTopic;
    [SerializeField] private bool _enablePublisher = true;
    [SerializeField] private bool _enableSubscription = true;
    [SerializeField] private string _statusMessage = "Not started.";
    [SerializeField] private int _publishedCount;
    [SerializeField] private int _receivedCount;
    [SerializeField] private string _lastReceived = string.Empty;
    [SerializeField] private string _lastError = string.Empty;

    private float _nextPublishTime;
    private bool _warnedMissingDefine;
    private bool _loggedFirstPublish;
    private Phase110Ros2ForUnityContext _context;
    private IUnity2FoxgloveRos2Node _node;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private IUnity2FoxgloveRos2Publisher<std_msgs.msg.String> _publisher;
    private IUnity2FoxgloveRos2Subscription _subscription;
    private ROS2UnityComponent _directRos2Unity;
    private ROS2Node _directRos2Node;
    private IPublisher<std_msgs.msg.String> _directPublisher;
    private ISubscription<std_msgs.msg.String> _directSubscription;
    private bool _directInitializationFailed;
#endif

    private void OnEnable()
    {
        Application.runInBackground = true;
        _nextPublishTime = 0f;
        _publishedCount = 0;
        _receivedCount = 0;
        _lastReceived = string.Empty;
        _lastError = string.Empty;
        _loggedFirstPublish = false;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (_useDirectR2fu)
        {
            _statusMessage = "Phase110 direct ROS2 For Unity diagnostic mode.";
        }
        else
        {
            _context = Phase110Ros2ForUnityContextFactory.Create(gameObject);
            _statusMessage = _context.StatusMessage;
        }
#else
        WarnMissingDefine();
#endif
    }

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (_useDirectR2fu)
        {
            if (!TryEnsureDirectReady())
                return;

            EnsureDirectEndpoints();
            PublishDirectIfDue();
            return;
        }

        if (_context == null)
            _context = Phase110Ros2ForUnityContextFactory.Create(gameObject);

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
        DisposeDirectEndpoints();
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
        if (string.IsNullOrWhiteSpace(_nodeName))
            _nodeName = NodeName;
        if (string.IsNullOrWhiteSpace(_outTopic))
            _outTopic = OutTopic;
        if (string.IsNullOrWhiteSpace(_inTopic))
            _inTopic = InTopic;
    }

    private void WarnMissingDefine()
    {
        _lastError = "Import ROS2 For Unity and add UNITY2FOXGLOVE_ROS2_FOR_UNITY before running Phase110.";
        _statusMessage = _lastError;
        if (_warnedMissingDefine)
            return;

        _warnedMissingDefine = true;
        Debug.LogWarning("[Phase110Ros2ForUnityStringSmoke] " + _statusMessage);
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private bool TryEnsureDirectReady()
    {
        if (_directInitializationFailed)
            return false;

        try
        {
            if (_directRos2Unity == null)
            {
                _directRos2Unity = GetComponent<ROS2UnityComponent>();
                if (_directRos2Unity == null)
                    _directRos2Unity = gameObject.AddComponent<ROS2UnityComponent>();
            }

            if (!_directRos2Unity.Ok())
            {
                _statusMessage = "Waiting for ROS2 For Unity direct mode to initialize.";
                return false;
            }

            _statusMessage = "Phase110 direct ROS2 For Unity mode is ready.";
            _lastError = string.Empty;
            return true;
        }
        catch (System.Exception ex)
        {
            _directInitializationFailed = true;
            _lastError = "Phase110 direct ROS2 For Unity initialization failed: " + ex.Message;
            _statusMessage = _lastError;
            return false;
        }
    }

    private void EnsureDirectEndpoints()
    {
        if (!_enablePublisher && !_enableSubscription)
        {
            _statusMessage = "Phase110 direct endpoints are disabled.";
            return;
        }

        if ((_directPublisher != null || !_enablePublisher)
            && (_directSubscription != null || !_enableSubscription))
            return;

        if (_directRos2Node == null)
            _directRos2Node = _directRos2Unity.CreateNode(NormalizeTopic(_nodeName, NodeName));

        if (_enablePublisher && _directPublisher == null)
        {
            _directPublisher =
                _directRos2Node.CreatePublisher<std_msgs.msg.String>(NormalizeTopic(_outTopic, OutTopic));
        }

        if (_enableSubscription && _directSubscription == null)
        {
            _directSubscription =
                _directRos2Node.CreateSubscription<std_msgs.msg.String>(
                    NormalizeTopic(_inTopic, InTopic),
                    OnStringReceived);
        }

        _statusMessage = "Phase110 direct ROS2 For Unity endpoints ready.";
        _lastError = string.Empty;
    }

    private void PublishDirectIfDue()
    {
        if (!_enablePublisher || _directPublisher == null)
            return;

        var now = Time.unscaledTime;
        if (now < _nextPublishTime)
            return;

        _nextPublishTime = now + _publishIntervalSeconds;
        var message = new std_msgs.msg.String
        {
            Data = "phase110 unity tick " + (_publishedCount + 1)
        };

        try
        {
            _directPublisher.Publish(message);
        }
        catch (System.Exception ex)
        {
            _lastError = "Phase110 direct ROS2 For Unity publish failed: " + ex.Message;
            _statusMessage = _lastError;
            return;
        }

        _publishedCount++;
        _statusMessage = "Published " + _publishedCount + " Phase110 string message(s).";
        _lastError = string.Empty;
        if (!_loggedFirstPublish)
        {
            _loggedFirstPublish = true;
            Debug.Log("[Phase110Ros2ForUnityStringSmoke] first publish: " + message.Data);
        }
    }

    private void DisposeDirectEndpoints()
    {
        if (_directRos2Node != null && _directSubscription != null)
        {
            try
            {
                _directRos2Node.RemoveSubscription<std_msgs.msg.String>(_directSubscription);
            }
            catch (System.Exception)
            {
            }
        }

        if (_directRos2Node != null && _directPublisher != null)
        {
            try
            {
                _directRos2Node.RemovePublisher<std_msgs.msg.String>(_directPublisher);
            }
            catch (System.Exception)
            {
            }
        }

        if (_directRos2Unity != null && _directRos2Node != null)
        {
            try
            {
                _directRos2Unity.RemoveNode(_directRos2Node);
            }
            catch (System.Exception)
            {
            }
        }

        _directSubscription = null;
        _directPublisher = null;
        _directRos2Node = null;
    }

    private void EnsureEndpoints()
    {
        if (!_enablePublisher && !_enableSubscription)
        {
            _statusMessage = "Phase110 endpoints are disabled.";
            return;
        }

        if ((_publisher != null || !_enablePublisher)
            && (_subscription != null || !_enableSubscription))
            return;

        if (_node == null)
            _node = _context.CreateNode(NormalizeTopic(_nodeName, NodeName));

        if (_enablePublisher && _publisher == null)
            _publisher = _node.CreatePublisher<std_msgs.msg.String>(NormalizeTopic(_outTopic, OutTopic));
        if (_enableSubscription && _subscription == null)
            _subscription = _node.CreateSubscription<std_msgs.msg.String>(
                NormalizeTopic(_inTopic, InTopic),
                OnStringReceived);

        _statusMessage = "Phase110 ROS2 For Unity string smoke ready.";
        _lastError = string.Empty;
    }

    private void PublishIfDue()
    {
        if (!_enablePublisher)
            return;

        if (_publisher == null)
            return;

        var now = Time.unscaledTime;
        if (now < _nextPublishTime)
            return;

        _nextPublishTime = now + _publishIntervalSeconds;
        var message = new std_msgs.msg.String
        {
            Data = "phase110 unity tick " + (_publishedCount + 1)
        };

        if (!_publisher.TryPublish(message, out var error))
        {
            _lastError = error;
            _statusMessage = error;
            return;
        }

        _publishedCount++;
        _statusMessage = "Published " + _publishedCount + " Phase110 string message(s).";
        _lastError = string.Empty;
        if (!_loggedFirstPublish)
        {
            _loggedFirstPublish = true;
            Debug.Log("[Phase110Ros2ForUnityStringSmoke] first publish: " + message.Data);
        }
    }

    private void OnStringReceived(std_msgs.msg.String message)
    {
        _receivedCount++;
        _lastReceived = message.Data;
        _statusMessage = "Received " + _receivedCount + " Phase110 string message(s).";
        _lastError = string.Empty;
        Debug.Log("[Phase110Ros2ForUnityStringSmoke] received: " + message.Data);
    }

    private static string NormalizeTopic(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
#endif
}
