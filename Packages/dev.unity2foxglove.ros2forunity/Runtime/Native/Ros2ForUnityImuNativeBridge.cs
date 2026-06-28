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
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-460)]
    internal sealed class Ros2ForUnityImuNativeBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU IMU Native Bridge";
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;

        private static Ros2ForUnityImuNativeBridge _instance;
        private static volatile bool _runtimeShuttingDown;
        private static volatile bool _playModeSceneLoaded;
#if UNITY_EDITOR
        private static volatile bool _editorEnteredPlayMode;
        private static double _editorEnteredPlayModeAt;
        private static volatile bool _editorQuitting;
#endif

        private readonly Dictionary<int, ImuBinding> _bindings = new Dictionary<int, ImuBinding>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();
        private ROS2UnityComponent _ros2Unity;
        private float _nextScanAt;
        private int _ros2FailureCount;
        private bool _warnedRos2Unavailable;
        private bool _isStopping;
        private bool _ros2RuntimeWasReady;

        private bool IsShuttingDown
            => _isStopping
               || _runtimeShuttingDown
               || !Application.isPlaying
               || !_playModeSceneLoaded
               || !IsStableUserSceneLoaded()
               || IsEditorPlayModeTransition()
               || IsBackupSceneActive()
               || IsBackupScene(gameObject.scene)
               || IsAnyBackupSceneLoaded();

        private static bool IsBackupSceneActive()
        {
            return IsBackupScene(SceneManager.GetActiveScene());
        }

        private static bool IsAnyBackupSceneLoaded()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (IsBackupScene(SceneManager.GetSceneAt(i)))
                    return true;
            }

            return false;
        }

        private static bool IsBackupScene(Scene scene)
        {
            var path = scene.path ?? string.Empty;
            var name = scene.name ?? string.Empty;
            return path.StartsWith("Temp/__Backupscenes/", StringComparison.Ordinal)
                   || path.Contains("__Backupscenes", StringComparison.Ordinal)
                   || name.Contains("__Backupscenes", StringComparison.Ordinal)
                   || name.EndsWith(".backup", StringComparison.Ordinal);
        }

        private static bool IsStableUserSceneLoaded()
        {
            var scene = SceneManager.GetActiveScene();
            var path = scene.path ?? string.Empty;
            return scene.isLoaded
                   && (path.StartsWith("Assets/", StringComparison.Ordinal)
                       || path.StartsWith("Packages/", StringComparison.Ordinal));
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditorPlayModeGate()
        {
            EditorApplication.playModeStateChanged -= OnEditorPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnEditorPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
            _editorEnteredPlayMode = false;
            _editorQuitting = false;
        }

        private static void OnEditorQuitting()
        {
            _editorQuitting = true;
            _runtimeShuttingDown = true;
        }

        private static void OnEditorPlayModeStateChanged(PlayModeStateChange state)
        {
            _editorEnteredPlayMode = state == PlayModeStateChange.EnteredPlayMode;
            _editorEnteredPlayModeAt = _editorEnteredPlayMode
                ? EditorApplication.timeSinceStartup
                : 0.0;
            if (state == PlayModeStateChange.EnteredPlayMode)
                _runtimeShuttingDown = false;
            else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                _runtimeShuttingDown = true;
        }

        private static bool IsEditorPlayModeTransition()
        {
            return _editorQuitting
                   || EditorApplication.isCompiling
                   || EditorApplication.isUpdating
                   || !_editorEnteredPlayMode
                   || EditorApplication.timeSinceStartup - _editorEnteredPlayModeAt < 3.0;
        }
#else
        private static bool IsEditorPlayModeTransition()
        {
            return false;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _runtimeShuttingDown = false;
            _playModeSceneLoaded = false;
#if UNITY_EDITOR
            _editorEnteredPlayMode = false;
            _editorEnteredPlayModeAt = 0.0;
            _editorQuitting = false;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!IsStableUserSceneLoaded() || IsBackupSceneActive() || IsAnyBackupSceneLoaded())
            {
                _runtimeShuttingDown = true;
                _playModeSceneLoaded = false;
                return;
            }

            _runtimeShuttingDown = false;
            _playModeSceneLoaded = true;

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
            if (IsStableUserSceneLoaded() && !IsEditorPlayModeTransition() && !IsBackupSceneActive() && !IsAnyBackupSceneLoaded())
                _runtimeShuttingDown = false;
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

            if (Time.unscaledTime < _nextScanAt)
                return;

            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
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
            if (IsShuttingDown)
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
                _ros2Unity = GetComponent<ROS2UnityComponent>() ?? FindFirstObjectByType<ROS2UnityComponent>();

            ros2Unity = _ros2Unity;
            return ros2Unity != null;
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
            _runtimeShuttingDown = true;
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
                Debug.Log("[Foxglove][R2FU] IMU Native DDS ready: topic=" + Topic + ".");
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
                    catch (Exception) { }
                }

                _publisher = null;
                if (_owner._ros2Unity != null && _node != null)
                {
                    try { _owner._ros2Unity.RemoveNode(_node); }
                    catch (Exception) { }
                }

                _node = null;
            }
        }
    }
}
#endif
