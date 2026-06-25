// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Product bridge from standard camera publishers to ROS2 For Unity DDS.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using ROS2;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.Camera;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-440)]
    internal sealed partial class Ros2ForUnityCameraNativeBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU Camera Native Bridge";
        private const string TfAnchorTopic = "/tf";
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;

        private static Ros2ForUnityCameraNativeBridge _instance;
        private static bool _runtimeShuttingDown;
        private static bool _playModeSceneLoaded;
#if UNITY_EDITOR
        private static bool _editorEnteredPlayMode;
        private static double _editorEnteredPlayModeAt;
        private static bool _editorQuitting;
#endif

        private readonly Dictionary<int, ImageBinding> _imageBindings = new Dictionary<int, ImageBinding>();
        private readonly Dictionary<int, RawImageBinding> _rawImageBindings = new Dictionary<int, RawImageBinding>();
        private readonly Dictionary<int, InfoBinding> _infoBindings = new Dictionary<int, InfoBinding>();
        private readonly HashSet<int> _imageSeen = new HashSet<int>();
        private readonly HashSet<int> _rawImageSeen = new HashSet<int>();
        private readonly HashSet<int> _infoSeen = new HashSet<int>();
        private readonly List<int> _staleBindings = new List<int>();
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

            var existing = FindFirstObjectByType<Ros2ForUnityCameraNativeBridge>();
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
            _instance = bridgeObject.AddComponent<Ros2ForUnityCameraNativeBridge>();
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
            var cameraPublishers = FindObjectsByType<FoxgloveCameraPublisher>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            RefreshImageBindings(cameraPublishers);
            RefreshRawImageBindings(cameraPublishers);
            RefreshInfoBindings();
        }

        private void RefreshRawImageBindings(FoxgloveCameraPublisher[] publishers)
        {
            _rawImageSeen.Clear();

            foreach (var publisher in publishers)
            {
                if (!IsRawEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                _rawImageSeen.Add(instanceId);
                var topic = NormalizeTopic(publisher.SensorCameraRawImageTopic);
                if (_rawImageBindings.TryGetValue(instanceId, out var existing))
                {
                    if (existing.Topic == topic)
                        continue;

                    existing.Dispose();
                    _rawImageBindings.Remove(instanceId);
                }

                var binding = new RawImageBinding(this, publisher, topic);
                binding.Subscribe();
                _rawImageBindings.Add(instanceId, binding);
            }

            RemoveStale(_rawImageBindings, _rawImageSeen);
        }

        private void RefreshImageBindings(FoxgloveCameraPublisher[] publishers)
        {
            _imageSeen.Clear();

            foreach (var publisher in publishers)
            {
                if (!IsEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                _imageSeen.Add(instanceId);
                var topic = NormalizeTopic(publisher.SensorCameraImageTopic);
                if (_imageBindings.TryGetValue(instanceId, out var existing))
                {
                    if (existing.Topic == topic)
                        continue;

                    existing.Dispose();
                    _imageBindings.Remove(instanceId);
                }

                var binding = new ImageBinding(this, publisher, topic);
                binding.Subscribe();
                _imageBindings.Add(instanceId, binding);
            }

            RemoveStale(_imageBindings, _imageSeen);
        }

        private void RefreshInfoBindings()
        {
            _infoSeen.Clear();
            var publishers = FindObjectsByType<FoxgloveCameraInfoPublisher>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var publisher in publishers)
            {
                if (!IsEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                _infoSeen.Add(instanceId);
                var topic = NormalizeTopic(publisher.SensorCameraInfoTopic);
                if (_infoBindings.TryGetValue(instanceId, out var existing))
                {
                    if (existing.Topic == topic)
                        continue;

                    existing.Dispose();
                    _infoBindings.Remove(instanceId);
                }

                var binding = new InfoBinding(this, publisher, topic);
                binding.Subscribe();
                _infoBindings.Add(instanceId, binding);
            }

            RemoveStale(_infoBindings, _infoSeen);
        }

        private void RemoveStale<TBinding>(Dictionary<int, TBinding> bindings, HashSet<int> seen)
            where TBinding : BindingBase
        {
            _staleBindings.Clear();
            foreach (var pair in bindings)
            {
                if (!seen.Contains(pair.Key) || !pair.Value.IsStillEligible())
                    _staleBindings.Add(pair.Key);
            }

            foreach (var key in _staleBindings)
            {
                bindings[key].Dispose();
                bindings.Remove(key);
            }
        }

        private static bool IsEligible(FoxgloveCameraPublisher publisher)
            => publisher != null
               && publisher.isActiveAndEnabled
               && publisher.IsStandardRos2CompressedImageOutput;

        private static bool IsRawEligible(FoxgloveCameraPublisher publisher)
            => publisher != null
               && publisher.isActiveAndEnabled
               && publisher.IsStandardRos2RawImageOutput;

        private static bool IsEligible(FoxgloveCameraInfoPublisher publisher)
            => publisher != null && publisher.isActiveAndEnabled;

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

                    RecordRos2Failure("ROS2 For Unity runtime is not ready; Camera Native DDS output is paused.");
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
            _isStopping = true;
            _runtimeShuttingDown = true;
            ClearBindings();
        }

        private void ClearBindings()
        {
            foreach (var binding in _imageBindings.Values)
                binding.Dispose();
            foreach (var binding in _rawImageBindings.Values)
                binding.Dispose();
            foreach (var binding in _infoBindings.Values)
                binding.Dispose();

            _imageBindings.Clear();
            _rawImageBindings.Clear();
            _infoBindings.Clear();
        }

        private static string NormalizeTopic(string topic)
        {
            var value = string.IsNullOrWhiteSpace(topic)
                ? "/unity/sensor/camera/image/compressed"
                : topic.Trim();

            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        private static string BuildNodeName(UnityEngine.Object source, string kind, int attempt)
        {
            var suffix = unchecked((uint)source.GetInstanceID()).ToString("x8");
            var name = "u2f_camera_" + kind + "_" + suffix;
            return attempt == 0 ? name : name + "_" + attempt;
        }

    }
}
#endif
