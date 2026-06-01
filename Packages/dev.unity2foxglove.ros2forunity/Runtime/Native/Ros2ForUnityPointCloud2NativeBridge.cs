// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Product bridge from PointCloud2 Native publishers to ROS2 For Unity DDS.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using ROS2;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-450)]
    internal sealed class Ros2ForUnityPointCloud2NativeBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU PointCloud2 Native Bridge";
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;

        private static Ros2ForUnityPointCloud2NativeBridge _instance;

        private readonly Dictionary<int, Binding> _bindings = new Dictionary<int, Binding>();
        private ROS2UnityComponent _ros2Unity;
        private float _nextScanAt;
        private int _ros2FailureCount;
        private bool _warnedRos2Unavailable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            var existing = FindFirstObjectByType<Ros2ForUnityPointCloud2NativeBridge>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var bridgeObject = new GameObject(BridgeObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(bridgeObject);
            _instance = bridgeObject.AddComponent<Ros2ForUnityPointCloud2NativeBridge>();
        }

        private void OnDestroy()
        {
            ClearBindings();
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!Ros2NativeOutputPolicy.Enabled)
            {
                ClearBindings();
                return;
            }

            if (Time.unscaledTime < _nextScanAt)
                return;

            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            RefreshBindings();
        }

        private void RefreshBindings()
        {
            var seen = new HashSet<int>();
            var publishers = FindObjectsByType<FoxglovePointCloudPublisher>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var publisher in publishers)
            {
                if (!IsEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                seen.Add(instanceId);
                var topic = NormalizeTopic(publisher.PointCloud2NativeTopic);
                if (_bindings.TryGetValue(instanceId, out var existing))
                {
                    if (existing.Topic == topic)
                        continue;

                    existing.Dispose();
                    _bindings.Remove(instanceId);
                }

                var binding = new Binding(this, publisher, topic);
                binding.Subscribe();
                _bindings.Add(instanceId, binding);
            }

            var stale = new List<int>();
            foreach (var pair in _bindings)
            {
                if (!seen.Contains(pair.Key) || !pair.Value.IsStillEligible())
                    stale.Add(pair.Key);
            }

            foreach (var key in stale)
            {
                _bindings[key].Dispose();
                _bindings.Remove(key);
            }
        }

        private static bool IsEligible(FoxglovePointCloudPublisher publisher)
            => publisher != null
               && publisher.isActiveAndEnabled
               && publisher.IsPointCloud2NativeOutput;

        private bool TryGetRos2Unity(out ROS2UnityComponent ros2Unity)
        {
            ros2Unity = null;
            if (_ros2Unity == null)
                _ros2Unity = GetComponent<ROS2UnityComponent>() ?? gameObject.AddComponent<ROS2UnityComponent>();

            try
            {
                if (!_ros2Unity.Ok())
                {
                    RecordRos2Failure("ROS2 For Unity runtime is not ready; PointCloud2 Native DDS output is paused.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RecordRos2Failure("ROS2 For Unity runtime check failed: " + ex.Message);
                return false;
            }

            _warnedRos2Unavailable = false;
            ros2Unity = _ros2Unity;
            return true;
        }

        private void RecordRos2Failure(string message)
        {
            _ros2FailureCount++;
            if (_warnedRos2Unavailable && _ros2FailureCount % WarningIntervalFrames != 0)
                return;

            _warnedRos2Unavailable = true;
            Debug.LogWarning("[Foxglove][R2FU] " + message);
        }

        private void ClearBindings()
        {
            foreach (var binding in _bindings.Values)
                binding.Dispose();

            _bindings.Clear();
        }

        private static string NormalizeTopic(string topic)
        {
            var value = string.IsNullOrWhiteSpace(topic)
                ? PointCloudOutputModeDefaults.PointCloud2NativeTopic
                : topic.Trim();

            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        private static string BuildNodeName(FoxglovePointCloudPublisher source, int attempt)
        {
            var suffix = unchecked((uint)source.GetInstanceID()).ToString("x8");
            var name = "u2f_pointcloud2_native_" + suffix;
            return attempt == 0 ? name : name + "_" + attempt;
        }

        private sealed class Binding : IDisposable
        {
            private readonly Ros2ForUnityPointCloud2NativeBridge _owner;
            private readonly FoxglovePointCloudPublisher _source;
            private ROS2Node _node;
            private IPublisher<sensor_msgs.msg.PointCloud2> _publisher;
            private bool _subscribed;
            private bool _warnedPublishFailure;
            private int _publishFailureCount;

            public Binding(
                Ros2ForUnityPointCloud2NativeBridge owner,
                FoxglovePointCloudPublisher source,
                string topic)
            {
                _owner = owner;
                _source = source;
                Topic = topic;
            }

            public string Topic { get; }

            public void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.PointCloud2NativeFrameReady += OnPointCloud2NativeFrameReady;
                _subscribed = true;
            }

            public bool IsStillEligible()
                => IsEligible(_source)
                   && NormalizeTopic(_source.PointCloud2NativeTopic) == Topic;

            public void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.PointCloud2NativeFrameReady -= OnPointCloud2NativeFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnPointCloud2NativeFrameReady(PointCloud2NativeFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled)
                    return;

                if (!_owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    _publisher.Publish(Ros2ForUnityPointCloud2MessageBuilder.Build(frame));
                    _warnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 PointCloud2 publish failed for " + Topic + ": " + ex.Message);
                }
            }

            private bool TryEnsurePublisher(ROS2UnityComponent ros2Unity)
            {
                if (_node != null && _publisher != null)
                    return true;

                Exception lastException = null;
                for (var attempt = 0; attempt < MaxNodeCreateAttempts; attempt++)
                {
                    try
                    {
                        _node = ros2Unity.CreateNode(BuildNodeName(_source, attempt));
                        _publisher = _node.CreatePublisher<sensor_msgs.msg.PointCloud2>(Topic);
                        _warnedPublishFailure = false;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        CleanupRos2();
                    }
                }

                RecordPublishFailure(
                    "Unable to create ROS2 PointCloud2 publisher for " + Topic + ": "
                    + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void RecordPublishFailure(string message)
            {
                _publishFailureCount++;
                if (_warnedPublishFailure && _publishFailureCount % WarningIntervalFrames != 0)
                    return;

                _warnedPublishFailure = true;
                Debug.LogWarning("[Foxglove][R2FU] " + message);
            }

            private void CleanupRos2()
            {
                if (_node != null && _publisher != null)
                {
                    try { _node.RemovePublisher<sensor_msgs.msg.PointCloud2>(_publisher); }
                    catch (Exception) { }
                }

                if (_owner._ros2Unity != null && _node != null)
                {
                    try { _owner._ros2Unity.RemoveNode(_node); }
                    catch (Exception) { }
                }

                _publisher = null;
                _node = null;
            }
        }
    }
}
#endif
