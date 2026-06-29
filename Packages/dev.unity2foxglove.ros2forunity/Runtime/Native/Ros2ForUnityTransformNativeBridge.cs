// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Product bridge from FrameTransform publishers to ROS2 For Unity DDS.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using ROS2;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-440)]
    internal sealed class Ros2ForUnityTransformNativeBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU Transform Native Bridge";
        private const string TfTopic = "/tf";
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;

        private static Ros2ForUnityTransformNativeBridge _instance;
        private static volatile bool _runtimeShuttingDown;
        private static volatile bool _playModeSceneLoaded;
#if UNITY_EDITOR
        private static volatile bool _editorEnteredPlayMode;
        private static double _editorEnteredPlayModeAt;
        private static bool _editorPlayModeStable;
        private static volatile bool _editorQuitting;
#endif

        private readonly Dictionary<int, Binding> _bindings = new Dictionary<int, Binding>();
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
               || !_playModeSceneLoaded
               || Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(IsEditorPlayModeTransition())
               || Ros2ForUnityNativeBridgeSceneGate.IsBackupScene(gameObject.scene);

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
            _editorPlayModeStable = false;
            if (state == PlayModeStateChange.EnteredPlayMode)
                _runtimeShuttingDown = false;
            else if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
                _runtimeShuttingDown = true;
        }

        private static bool IsEditorPlayModeTransition()
        {
            if (_editorQuitting
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || !_editorEnteredPlayMode)
                return true;

            if (_editorPlayModeStable)
                return false;

            var elapsed = EditorApplication.timeSinceStartup - _editorEnteredPlayModeAt;
            _editorPlayModeStable = elapsed >= 3.0;
            return !_editorPlayModeStable;
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
            _editorPlayModeStable = false;
            _editorQuitting = false;
#endif
            Ros2ForUnityNativeBridgeSceneGate.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(editorTransition: false))
            {
                _runtimeShuttingDown = true;
                _playModeSceneLoaded = false;
                return;
            }

            _runtimeShuttingDown = false;
            _playModeSceneLoaded = true;

            if (_instance != null)
                return;

            var existing = FindFirstObjectByType<Ros2ForUnityTransformNativeBridge>();
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
            _instance = bridgeObject.AddComponent<Ros2ForUnityTransformNativeBridge>();
        }

        private void OnEnable()
        {
            _isStopping = false;
            _ros2RuntimeWasReady = false;
            if (!Ros2ForUnityNativeBridgeSceneGate.IsSceneUnsafe(IsEditorPlayModeTransition()))
                _runtimeShuttingDown = false;
            Application.quitting += OnApplicationQuitting;
        }

        private void OnDisable()
        {
            _isStopping = true;
            ClearBindings();
            Application.quitting -= OnApplicationQuitting;
        }

        private void OnApplicationQuit()
        {
            BeginShutdown();
        }

        private void OnDestroy()
        {
            BeginShutdown();
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (IsShuttingDown)
            {
                ClearBindings();
                return;
            }

            if (!Ros2NativeOutputPolicy.Enabled)
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
            var publishers = FindObjectsByType<FoxgloveTransformPublisher>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var publisher in publishers)
            {
                if (!IsEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                _seen.Add(instanceId);
                if (_bindings.ContainsKey(instanceId))
                    continue;

                var binding = new Binding(this, publisher);
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

        private static bool IsEligible(FoxgloveTransformPublisher publisher)
            => publisher != null
               && publisher.isActiveAndEnabled;

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
                        RecordRos2Failure("ROS2 For Unity runtime is not ready; Transform Native DDS output is paused.");

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

        private static string BuildNodeName(FoxgloveTransformPublisher source, int attempt)
        {
            var suffix = unchecked((uint)source.GetInstanceID()).ToString("x8");
            var name = "u2f_transform_native_" + suffix;
            return attempt == 0 ? name : name + "_" + attempt;
        }

        private sealed class Binding : IDisposable
        {
            private static bool _warnedTimestampClamp;

            private readonly Ros2ForUnityTransformNativeBridge _owner;
            private readonly FoxgloveTransformPublisher _source;
            private ROS2Node _node;
            private IPublisher<tf2_msgs.msg.TFMessage> _publisher;
            private bool _subscribed;
            private bool _warnedPublishFailure;
            private bool _readyLogged;
            private int _publishFailureCount;

            public Binding(Ros2ForUnityTransformNativeBridge owner, FoxgloveTransformPublisher source)
            {
                _owner = owner;
                _source = source;
            }

            public void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.FrameTransformReady += OnFrameTransformReady;
                _subscribed = true;
            }

            public bool IsStillEligible()
                => IsEligible(_source);

            public void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.FrameTransformReady -= OnFrameTransformReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnFrameTransformReady(FrameTransformMessage frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || _owner.IsShuttingDown)
                    return;

                if (!IsValidFrame(frame))
                    return;

                if (!_owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    _publisher.Publish(BuildTfMessage(frame));
                    _warnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Transform publish failed for " + DescribeFrame(frame) + ": " + ex.Message);
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
                        _publisher = _node.CreatePublisher<tf2_msgs.msg.TFMessage>(TfTopic);
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
                    "Unable to create ROS2 Transform publisher for " + TfTopic + ": "
                    + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void LogReadyOnce()
            {
                if (_readyLogged)
                    return;

                _readyLogged = true;
                Debug.Log(
                    "[Foxglove][R2FU] Transform DDS ready: topic="
                    + TfTopic
                    + " source="
                    + (_source == null ? "unknown" : _source.Topic)
                    + ".");
            }

            private static bool IsValidFrame(FrameTransformMessage frame)
            {
                return frame != null
                       && !string.IsNullOrWhiteSpace(frame.ParentFrameId)
                       && !string.IsNullOrWhiteSpace(frame.ChildFrameId)
                       && !string.Equals(frame.ParentFrameId, frame.ChildFrameId, StringComparison.Ordinal);
            }

            private static tf2_msgs.msg.TFMessage BuildTfMessage(FrameTransformMessage frame)
            {
                var timestamp = frame.Timestamp;
                var sec = timestamp == null ? 0UL : timestamp.Sec;
                var nsec = timestamp == null ? 0U : timestamp.Nsec;
                var translation = frame.Translation;
                var rotation = frame.Rotation;

                return new tf2_msgs.msg.TFMessage
                {
                    Transforms = new[]
                    {
                        new geometry_msgs.msg.TransformStamped
                        {
                            Header = new std_msgs.msg.Header
                            {
                                Stamp = new builtin_interfaces.msg.Time
                                {
                                    Sec = ClampRosTimeSeconds(sec),
                                    Nanosec = nsec
                                },
                                Frame_id = frame.ParentFrameId
                            },
                            Child_frame_id = frame.ChildFrameId,
                            Transform = new geometry_msgs.msg.Transform
                            {
                                Translation = new geometry_msgs.msg.Vector3
                                {
                                    X = translation == null ? 0.0 : translation.X,
                                    Y = translation == null ? 0.0 : translation.Y,
                                    Z = translation == null ? 0.0 : translation.Z
                                },
                                Rotation = new geometry_msgs.msg.Quaternion
                                {
                                    X = rotation == null ? 0.0 : rotation.X,
                                    Y = rotation == null ? 0.0 : rotation.Y,
                                    Z = rotation == null ? 0.0 : rotation.Z,
                                    W = rotation == null ? 1.0 : rotation.W
                                }
                            }
                        }
                    }
                };
            }

            private static int ClampRosTimeSeconds(ulong seconds)
            {
                if (seconds <= int.MaxValue)
                    return (int)seconds;

                if (!_warnedTimestampClamp)
                {
                    _warnedTimestampClamp = true;
                    Debug.LogWarning(
                        "[Foxglove][R2FU] Transform timestamp seconds exceeded ROS2 builtin_interfaces/Time int32 range; clamping to int.MaxValue.");
                }

                return int.MaxValue;
            }

            private static string DescribeFrame(FrameTransformMessage frame)
                => frame == null
                    ? "unknown"
                    : frame.ParentFrameId + "->" + frame.ChildFrameId;

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
                    try { _node.RemovePublisher<tf2_msgs.msg.TFMessage>(_publisher); }
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

    internal static class Ros2ForUnityNativeBridgeSceneGate
    {
        private static int _cachedFrame = -1;
        private static bool _cachedEditorTransition;
        private static bool _cachedUnsafe;

        internal static bool IsSceneUnsafe(bool editorTransition)
        {
            var frame = Time.frameCount;
            if (_cachedFrame == frame && _cachedEditorTransition == editorTransition)
                return _cachedUnsafe;

            _cachedFrame = Time.frameCount;
            _cachedEditorTransition = editorTransition;
            _cachedUnsafe = !Application.isPlaying
                            || editorTransition
                            || !IsStableUserSceneLoaded()
                            || IsBackupSceneActive()
                            || IsAnyBackupSceneLoaded();
            return _cachedUnsafe;
        }

        internal static void Reset()
        {
            _cachedFrame = -1;
            _cachedEditorTransition = false;
            _cachedUnsafe = false;
        }

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

        internal static bool IsBackupScene(Scene scene)
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
    }
}
#endif
