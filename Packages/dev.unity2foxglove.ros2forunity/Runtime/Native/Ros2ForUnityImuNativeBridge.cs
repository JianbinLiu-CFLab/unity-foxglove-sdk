// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Product bridge from VirtualImu to ROS2 For Unity DDS.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using ROS2;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.Imu;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-460)]
    internal sealed class Ros2ForUnityImuNativeBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU IMU Native Bridge";
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;

        private static Ros2ForUnityImuNativeBridge _instance;

        private readonly Dictionary<int, ImuBinding> _bindings = new Dictionary<int, ImuBinding>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();
        private ROS2UnityComponent _ros2Unity;
        private double _nextScanAt;
        private int _ros2FailureCount;
        private bool _warnedRos2Unavailable;
        private bool _isStopping;
        private bool _ros2RuntimeWasReady;

        private bool IsShuttingDown
            => _isStopping
               || Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge)
                return;

            if (_instance != null)
                return;

            var existing = FindFirstObjectByType<Ros2ForUnityImuNativeBridge>();
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
            _instance = bridgeObject.AddComponent<Ros2ForUnityImuNativeBridge>();
        }

        private void OnEnable()
        {
            _isStopping = false;
            _ros2RuntimeWasReady = false;
            Application.quitting += OnApplicationQuitting;
        }

        private void OnDisable()
        {
            _isStopping = true;
            ClearBindings();
            Application.quitting -= OnApplicationQuitting;
        }

        private void OnDestroy()
        {
            BeginShutdown();
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (IsShuttingDown || !Ros2NativeOutputPolicy.Enabled)
            {
                ClearBindings();
                return;
            }

            if (!_ros2RuntimeWasReady && !EnsureRos2UnityReady())
                return;

            if (!Ros2ForUnityNativeScanGate.TryAdvance(
                    Time.unscaledTimeAsDouble,
                    ref _nextScanAt))
                return;

            RefreshBindings();
        }

        private void RefreshBindings()
        {
            _seen.Clear();
            var sources = FindObjectsByType<VirtualImu>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var source in sources)
            {
                if (!IsEligible(source))
                    continue;

                var instanceId = source.GetInstanceID();
                _seen.Add(instanceId);
                var topic = NormalizeTopic(source.ImuNativeTopic);
                if (_bindings.TryGetValue(instanceId, out var existing))
                {
                    if (existing.Topic == topic)
                        continue;

                    existing.Dispose();
                    _bindings.Remove(instanceId);
                }

                var binding = new ImuBinding(this, source, topic);
                binding.Subscribe();
                _bindings.Add(instanceId, binding);
            }

            _stale.Clear();
            foreach (var pair in _bindings)
            {
                if (!_seen.Contains(pair.Key) || !pair.Value.IsStillEligible())
                    _stale.Add(pair.Key);
            }

            foreach (var key in _stale)
            {
                _bindings[key].Dispose();
                _bindings.Remove(key);
            }
        }

        private static bool IsEligible(VirtualImu source)
            => source != null && source.isActiveAndEnabled && source.IsImuNativeOutput;

        private bool TryGetRos2Unity(out ROS2UnityComponent ros2Unity)
        {
            ros2Unity = null;
            if (IsShuttingDown)
                return false;

            if (!_ros2RuntimeWasReady)
                return false;

            return TryGetExistingRos2Unity(out ros2Unity);
        }

        private bool EnsureRos2UnityReady()
        {
            if (!Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene))
                return false;

            if (_ros2Unity == null)
                _ros2Unity = GetComponent<ROS2UnityComponent>() ?? gameObject.AddComponent<ROS2UnityComponent>();

            try
            {
                if (!_ros2Unity.Ok())
                {
                    if (_ros2RuntimeWasReady)
                    {
                        BeginShutdown();
                        return false;
                    }

                    if (!IsShuttingDown)
                        RecordRos2Failure("ROS2 For Unity runtime is not ready; IMU Native DDS output is paused.");

                    return false;
                }
            }
            catch (Exception ex)
            {
                if (_ros2RuntimeWasReady)
                {
                    BeginShutdown();
                    return false;
                }

                if (!IsShuttingDown)
                    RecordRos2Failure("ROS2 For Unity runtime check failed: " + ex.Message);

                return false;
            }

            _warnedRos2Unavailable = false;
            _ros2RuntimeWasReady = true;
            return true;
        }

        private bool TryGetExistingRos2Unity(out ROS2UnityComponent ros2Unity)
        {
            if (_ros2Unity == null)
            {
                _ros2Unity = GetComponent<ROS2UnityComponent>();
                if (_ros2Unity == null)
                {
                    BeginShutdown();
                    ros2Unity = null;
                    return false;
                }
            }

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

        private void OnApplicationQuitting()
        {
            BeginShutdown();
        }

        private void BeginShutdown()
        {
            if (_isStopping)
                return;

            _isStopping = true;
            ClearBindings();
        }

        private void ClearBindings()
        {
            foreach (var binding in _bindings.Values)
                binding.Dispose();

            _bindings.Clear();
        }

        private static string NormalizeTopic(string topic)
        {
            var value = string.IsNullOrWhiteSpace(topic) ? "/imu/data" : topic.Trim();
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        private static string BuildNodeName(VirtualImu source, int attempt)
        {
            var suffix = unchecked((uint)source.GetInstanceID()).ToString("x8");
            var name = "u2f_imu_native_" + suffix;
            return attempt == 0 ? name : name + "_" + attempt;
        }

        private sealed class ImuBinding : IDisposable
        {
            private readonly Ros2ForUnityImuNativeBridge _owner;
            private readonly VirtualImu _source;
            private ROS2Node _node;
            private IPublisher<sensor_msgs.msg.Imu> _publisher;
            private bool _subscribed;
            private bool _warnedPublishFailure;
            private bool _readyLogged;
            private int _publishFailureCount;

            public ImuBinding(Ros2ForUnityImuNativeBridge owner, VirtualImu source, string topic)
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

                _source.ImuNativeFrameReady += OnFrameReady;
                _subscribed = true;
            }

            public bool IsStillEligible()
                => IsEligible(_source) && NormalizeTopic(_source.ImuNativeTopic) == Topic;

            public void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.ImuNativeFrameReady -= OnFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnFrameReady(ImuNativeFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || _owner.IsShuttingDown)
                    return;

                if (!_owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    _publisher.Publish(Ros2ForUnityImuMessageBuilder.Build(
                        frame,
                        _source.ImuOrientationCovariance,
                        _source.ImuAngularVelocityCovariance,
                        _source.ImuLinearAccelerationCovariance));
                    _warnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 IMU publish failed for " + Topic + ": " + ex.Message);
                }
            }

            private bool TryEnsurePublisher(ROS2UnityComponent ros2Unity)
            {
                if (_owner.IsShuttingDown)
                    return false;

                if (_node != null && _publisher != null)
                    return true;

                Exception lastException = null;
                for (var attempt = 0; attempt < MaxNodeCreateAttempts; attempt++)
                {
                    try
                    {
                        _node = ros2Unity.CreateNode(BuildNodeName(_source, attempt));
                        _publisher = _node.CreatePublisher<sensor_msgs.msg.Imu>(Topic);
                        _warnedPublishFailure = false;
                        LogReadyOnce();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        CleanupRos2();

                        if (_owner.IsShuttingDown)
                            return false;
                    }
                }

                RecordPublishFailure(
                    "Unable to create ROS2 IMU publisher for " + Topic + ": "
                    + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void LogReadyOnce()
            {
                if (_readyLogged)
                    return;

                _readyLogged = true;
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    _source,
                    "[Foxglove][R2FU] IMU Native DDS ready: topic={0}.",
                    new object[] { Topic });
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
                    try { _node.RemovePublisher<sensor_msgs.msg.Imu>(_publisher); }
                    catch (Exception ex)
                    {
                        if (!_owner.IsShuttingDown)
                            Debug.LogWarning("[Foxglove][R2FU] IMU publisher cleanup failed: " + ex.Message);
                    }
                }

                _publisher = null;
                if (_owner._ros2Unity != null && _node != null)
                {
                    try { _owner._ros2Unity.RemoveNode(_node); }
                    catch (Exception ex)
                    {
                        if (!_owner.IsShuttingDown)
                            Debug.LogWarning("[Foxglove][R2FU] IMU node cleanup failed: " + ex.Message);
                    }
                }

                _node = null;
            }
        }
    }
}
#endif
