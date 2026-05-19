// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Implements the Phase109 ROS2 For Unity backed facade for string smoke tests.

using System;
using System.Collections.Generic;
using Unity2Foxglove.Ros2ForUnity;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

public sealed class Phase109Ros2ForUnityContext : IUnity2FoxgloveRos2Context
{
    private const string UnavailableMessage =
        "Phase109 ROS2 For Unity manual adapter is unavailable. Import ROS2 For Unity and define UNITY2FOXGLOVE_ROS2_FOR_UNITY.";

    private readonly GameObject _host;
    private readonly List<Phase109Ros2ForUnityNode> _nodes = new List<Phase109Ros2ForUnityNode>();
    private bool _disposed;
    private string _statusMessage = UnavailableMessage;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private bool _initializationFailed;
#endif

    public Phase109Ros2ForUnityContext(GameObject host)
    {
        _host = host;
    }

    public bool IsAvailable => TryEnsureReady();

    public Unity2FoxgloveRos2Status Status
    {
        get
        {
            if (_disposed)
                return Unity2FoxgloveRos2Status.Disposed;
            return IsAvailable ? Unity2FoxgloveRos2Status.Ready : Unity2FoxgloveRos2Status.Unavailable;
        }
    }

    public string StatusMessage => _statusMessage;

    public bool TryEnsureReady()
    {
        if (_disposed)
        {
            _statusMessage = "Phase109 ROS2 For Unity context is disposed.";
            return false;
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (_initializationFailed)
            return false;

        if (_host == null)
        {
            _statusMessage = "Phase109 ROS2 For Unity context has no Unity host GameObject.";
            return false;
        }

        try
        {
            if (_ros2Unity == null)
            {
                _ros2Unity = _host.GetComponent<ROS2UnityComponent>();
                if (_ros2Unity == null)
                    _ros2Unity = _host.AddComponent<ROS2UnityComponent>();
            }

            if (!_ros2Unity.Ok())
            {
                _statusMessage = "Waiting for ROS2 For Unity to initialize.";
                return false;
            }

            _statusMessage = "ROS2 For Unity is ready for Phase109 string smoke.";
            return true;
        }
        catch (Exception ex)
        {
            _initializationFailed = true;
            _statusMessage = "ROS2 For Unity initialization failed: " + ex.Message;
            return false;
        }
#else
        _statusMessage = UnavailableMessage;
        return false;
#endif
    }

    public IUnity2FoxgloveRos2Node CreateNode(string nodeName)
    {
        if (_disposed)
            return new UnavailableNode(NormalizeName(nodeName), "Phase109 ROS2 For Unity context is disposed.");

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (!TryEnsureReady() || _ros2Unity == null)
            return new UnavailableNode(NormalizeName(nodeName), _statusMessage);

        var normalizedName = NormalizeName(nodeName);
        foreach (var existing in _nodes)
        {
            if (!existing.IsDisposed && existing.Name == normalizedName)
                return existing;
        }

        try
        {
            var ros2Node = _ros2Unity.CreateNode("unity2foxglove_phase109");
            var node = new Phase109Ros2ForUnityNode(_ros2Unity, ros2Node, normalizedName);
            _nodes.Add(node);
            return node;
        }
        catch (Exception ex)
        {
            _statusMessage = "ROS2 For Unity node creation failed: " + ex.Message;
            return new UnavailableNode(normalizedName, _statusMessage);
        }
#else
        return new UnavailableNode(NormalizeName(nodeName), UnavailableMessage);
#endif
    }

    public void DrainPendingCallbacks()
    {
        for (var i = 0; i < _nodes.Count; i++)
            _nodes[i].DrainPendingCallbacks();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (var i = 0; i < _nodes.Count; i++)
            _nodes[i].Dispose();
        _nodes.Clear();
        _statusMessage = "Phase109 ROS2 For Unity context is disposed.";
    }

    private static string NormalizeName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unity2foxglove_phase109" : value;
    }

    private static string NormalizeTopic(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "/unity2foxglove/phase109/unavailable" : value;
    }

    private sealed class UnavailableNode : IUnity2FoxgloveRos2Node
    {
        private readonly string _message;

        public UnavailableNode(string name, string message)
        {
            Name = name;
            _message = message;
        }

        public string Name { get; }

        public IUnity2FoxgloveRos2Publisher<T> CreatePublisher<T>(string topic)
        {
            return new UnavailablePublisher<T>(NormalizeTopic(topic), _message);
        }

        public IUnity2FoxgloveRos2Subscription CreateSubscription<T>(string topic, Action<T> callback)
        {
            return new UnavailableSubscription(NormalizeTopic(topic));
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnavailablePublisher<T> : IUnity2FoxgloveRos2Publisher<T>
    {
        private readonly string _message;

        public UnavailablePublisher(string topic, string message)
        {
            Topic = topic;
            _message = message;
        }

        public string Topic { get; }

        public bool TryPublish(T message, out string error)
        {
            error = _message;
            return false;
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnavailableSubscription : IUnity2FoxgloveRos2Subscription
    {
        public UnavailableSubscription(string topic)
        {
            Topic = topic;
        }

        public string Topic { get; }

        public void Dispose()
        {
        }
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private sealed class Phase109Ros2ForUnityNode : IUnity2FoxgloveRos2Node
    {
        private readonly ROS2UnityComponent _ros2Unity;
        private readonly ROS2Node _ros2Node;
        private readonly List<IPhase109DrainableSubscription> _subscriptions =
            new List<IPhase109DrainableSubscription>();

        public Phase109Ros2ForUnityNode(ROS2UnityComponent ros2Unity, ROS2Node ros2Node, string name)
        {
            _ros2Unity = ros2Unity;
            _ros2Node = ros2Node;
            Name = name;
        }

        public string Name { get; }

        public bool IsDisposed { get; private set; }

        public IUnity2FoxgloveRos2Publisher<T> CreatePublisher<T>(string topic)
        {
            if (typeof(T) == typeof(std_msgs.msg.String))
            {
                try
                {
                    IPublisher<std_msgs.msg.String> publisher =
                        _ros2Node.CreatePublisher<std_msgs.msg.String>(NormalizeTopic(topic));
                    var wrapper = new StringPublisher(NormalizeTopic(topic), publisher);
                    return (IUnity2FoxgloveRos2Publisher<T>)(object)wrapper;
                }
                catch (Exception ex)
                {
                    return new UnavailablePublisher<T>(
                        NormalizeTopic(topic),
                        "Phase109 ROS2 For Unity publisher creation failed: " + ex.Message);
                }
            }

            return new UnavailablePublisher<T>(
                NormalizeTopic(topic),
                "Unsupported Phase109 ROS2 message type. Only std_msgs/msg/String is implemented.");
        }

        public IUnity2FoxgloveRos2Subscription CreateSubscription<T>(string topic, Action<T> callback)
        {
            if (typeof(T) == typeof(std_msgs.msg.String))
            {
                try
                {
                    var typedCallback = (Action<std_msgs.msg.String>)(object)callback;
                    var wrapper = new StringSubscription(NormalizeTopic(topic), typedCallback);
                    ISubscription<std_msgs.msg.String> subscription =
                        _ros2Node.CreateSubscription<std_msgs.msg.String>(
                            NormalizeTopic(topic),
                            wrapper.Enqueue);
                    wrapper.Attach(subscription);
                    _subscriptions.Add(wrapper);
                    return wrapper;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Phase109Ros2ForUnityContext] subscription creation failed: " + ex.Message);
                    return new UnavailableSubscription(NormalizeTopic(topic));
                }
            }

            return new UnavailableSubscription(NormalizeTopic(topic));
        }

        public void DrainPendingCallbacks()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
                _subscriptions[i].Drain();
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            for (var i = 0; i < _subscriptions.Count; i++)
                _subscriptions[i].Dispose();
            _subscriptions.Clear();

            try
            {
                _ros2Unity.RemoveNode(_ros2Node);
            }
            catch (Exception)
            {
            }
        }
    }

    private interface IPhase109DrainableSubscription : IDisposable
    {
        void Drain();
    }

    private sealed class StringPublisher : IUnity2FoxgloveRos2Publisher<std_msgs.msg.String>
    {
        private readonly IPublisher<std_msgs.msg.String> _publisher;
        private bool _disposed;

        public StringPublisher(string topic, IPublisher<std_msgs.msg.String> publisher)
        {
            Topic = topic;
            _publisher = publisher;
        }

        public string Topic { get; }

        public bool TryPublish(std_msgs.msg.String message, out string error)
        {
            if (_disposed)
            {
                error = "Phase109 ROS2 For Unity publisher is disposed.";
                return false;
            }

            try
            {
                _publisher.Publish(message);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = "Phase109 ROS2 For Unity publish failed: " + ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    private sealed class StringSubscription :
        IUnity2FoxgloveRos2Subscription,
        IPhase109DrainableSubscription
    {
        private readonly object _gate = new object();
        private readonly Queue<std_msgs.msg.String> _pending = new Queue<std_msgs.msg.String>();
        private readonly Action<std_msgs.msg.String> _callback;
        private ISubscription<std_msgs.msg.String> _subscription;
        private bool _disposed;

        public StringSubscription(string topic, Action<std_msgs.msg.String> callback)
        {
            Topic = topic;
            _callback = callback;
        }

        public string Topic { get; }

        public void Attach(ISubscription<std_msgs.msg.String> subscription)
        {
            _subscription = subscription;
        }

        public void Enqueue(std_msgs.msg.String message)
        {
            if (_disposed)
                return;

            lock (_gate)
                _pending.Enqueue(message);
        }

        public void Drain()
        {
            while (true)
            {
                std_msgs.msg.String message;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                        return;
                    message = _pending.Dequeue();
                }

                _callback?.Invoke(message);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _subscription = null;
            lock (_gate)
                _pending.Clear();
        }
    }
#endif
}
