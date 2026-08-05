// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Product bridge from PointCloud2 Native publishers to ROS2 For Unity DDS.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Globalization;
using ROS2;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Util;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-450)]
    internal sealed class Ros2ForUnityPackedPointCloudBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU PointCloud2 Native Bridge";
        private const string TfAnchorTopic = "/tf";
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;
        private const double ZenohBackpressurePublishSlowThresholdMs = 40D;
        private const double ZenohBackpressureCooldownSeconds = 0.15D;

        private static Ros2ForUnityPackedPointCloudBridge _instance;

        private readonly Dictionary<int, Binding> _bindings = new Dictionary<int, Binding>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<GameObject> _scanRoots = new List<GameObject>(16);
        private readonly List<FoxglovePointCloudPublisher> _scanPublishers =
            new List<FoxglovePointCloudPublisher>(16);
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

            var existing = FindFirstObjectByType<Ros2ForUnityPackedPointCloudBridge>();
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var bridgeObject = new GameObject(BridgeObjectName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            // HideAndDontSave prevents scene serialization; DontDestroyOnLoad keeps the bridge alive across scene swaps.
            DontDestroyOnLoad(bridgeObject);
            _instance = bridgeObject.AddComponent<Ros2ForUnityPackedPointCloudBridge>();
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

            if (!Ros2ForUnityNativeScanGate.TryAdvance(
                    Time.unscaledTimeAsDouble,
                    ref _nextScanAt))
                return;

            RefreshBindings();
        }

        private void RefreshBindings()
        {
            _seen.Clear();

            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                    continue;

                _scanRoots.Clear();
                scene.GetRootGameObjects(_scanRoots);
                foreach (var root in _scanRoots)
                {
                    _scanPublishers.Clear();
                    root.GetComponentsInChildren(includeInactive: false, _scanPublishers);
                    foreach (var publisher in _scanPublishers)
                        RegisterPublisherBinding(publisher);
                }
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

        private void RegisterPublisherBinding(FoxglovePointCloudPublisher publisher)
        {
            if (!IsEligible(publisher))
                return;

            var instanceId = publisher.GetInstanceID();
            _seen.Add(instanceId);
            var topic = NormalizeTopic(publisher.PackedPointCloudTopic);
            if (_bindings.TryGetValue(instanceId, out var existing))
            {
                if (existing.Topic == topic)
                {
                    existing.PrewarmPublishers(_ros2Unity);
                    return;
                }

                existing.Dispose();
                _bindings.Remove(instanceId);
            }

            var binding = new Binding(this, publisher, topic);
            binding.Subscribe();
            binding.PrewarmPublishers(_ros2Unity);
            _bindings.Add(instanceId, binding);
        }

        private static bool IsEligible(FoxglovePointCloudPublisher publisher)
            => publisher != null
               && publisher.isActiveAndEnabled
               && publisher.IsPackedPointCloudOutput;

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
                        RecordRos2Failure("ROS2 For Unity runtime is not ready; PointCloud2 Native DDS output is paused.");

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
            var value = string.IsNullOrWhiteSpace(topic)
                ? PointCloudOutputModeDefaults.PackedPointCloudTopic
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
            private readonly Ros2ForUnityPackedPointCloudBridge _owner;
            private readonly FoxglovePointCloudPublisher _source;
            private readonly Dictionary<string, IPublisher<sensor_msgs.msg.PointCloud2>> _publishers =
                new Dictionary<string, IPublisher<sensor_msgs.msg.PointCloud2>>(StringComparer.Ordinal);
            private readonly HashSet<string> _readyLoggedTopics = new HashSet<string>(StringComparer.Ordinal);
            private readonly tf2_msgs.msg.TFMessage _tfAnchorMessage;
            private readonly geometry_msgs.msg.TransformStamped _tfAnchorTransform;
            private readonly bool _usesZenohRmw;
            private ROS2Node _node;
            private IPublisher<tf2_msgs.msg.TFMessage> _tfAnchorPublisher;
            private double _zenohBackpressureSuppressUntil;
            private bool _subscribed;
            private bool _warnedPublishFailure;
            private bool _warnedUnexpectedTopic;
            private int _publishFailureCount;

            public Binding(
                Ros2ForUnityPackedPointCloudBridge owner,
                FoxglovePointCloudPublisher source,
                string topic)
            {
                _owner = owner;
                _source = source;
                Topic = topic;
                _usesZenohRmw = IsZenohRmwActive();
                _tfAnchorTransform = new geometry_msgs.msg.TransformStamped
                {
                    Header = new std_msgs.msg.Header
                    {
                        Stamp = new builtin_interfaces.msg.Time()
                    },
                    Transform = new geometry_msgs.msg.Transform
                    {
                        Translation = new geometry_msgs.msg.Vector3(),
                        Rotation = new geometry_msgs.msg.Quaternion()
                    }
                };
                _tfAnchorMessage = new tf2_msgs.msg.TFMessage
                {
                    Transforms = new[] { _tfAnchorTransform }
                };
            }

            public string Topic { get; }

            public void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.PackedPointCloudFrameReady += OnPackedPointCloudFrameReady;
                _subscribed = true;
            }

            public bool IsStillEligible()
                => IsEligible(_source)
                   && NormalizeTopic(_source.PackedPointCloudTopic) == Topic;

            public void PrewarmPublishers(ROS2UnityComponent ros2Unity)
            {
                if (ros2Unity == null || _source == null || _owner.IsShuttingDown)
                    return;

                if (!TryEnsurePublisher(ros2Unity, Topic, out _))
                    return;

                var deskewedTopic = ResolvePrewarmDeskewedTopic();
                if (!string.IsNullOrWhiteSpace(deskewedTopic)
                    && !string.Equals(deskewedTopic, Topic, StringComparison.Ordinal))
                {
                    TryEnsurePublisher(ros2Unity, deskewedTopic, out _);
                }

                PrewarmTfAnchorPublisher();
            }

            public void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.PackedPointCloudFrameReady -= OnPackedPointCloudFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnPackedPointCloudFrameReady(PackedPointCloudFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || _owner.IsShuttingDown)
                    return;

                var timingEnabled = ShouldLogPackedPointCloudTiming;
                var totalStart = BeginPackedPointCloudTiming(timingEnabled);
                if (!_owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                var frameTopic = ResolveFrameTopic(frame);
                if (!IsKnownFrameTopic(frameTopic))
                {
                    if (!_warnedUnexpectedTopic)
                    {
                        _warnedUnexpectedTopic = true;
                        RecordPublishFailure(
                            "Ignoring unexpected dynamic PointCloud2 Native topic '" + frameTopic
                            + "' for configured topic '" + Topic + "'.");
                    }

                    return;
                }

                if (ShouldSkipZenohBackpressureFrame())
                {
                    if (timingEnabled)
                    {
                        LogPackedPointCloudPublishTiming(
                            frameTopic,
                            frame,
                            0D,
                            0D,
                            0D,
                            0D,
                            ElapsedPackedPointCloudMilliseconds(totalStart),
                            "zenohBackpressureSkip");
                    }

                    return;
                }

                var ensurePublisherStart = BeginPackedPointCloudTiming(timingEnabled);
                if (!TryEnsurePublisher(ros2Unity, frameTopic, out var publisher))
                {
                    if (timingEnabled)
                    {
                        LogPackedPointCloudPublishTiming(
                            frameTopic,
                            frame,
                            ElapsedPackedPointCloudMilliseconds(ensurePublisherStart),
                            0D,
                            0D,
                            0D,
                            ElapsedPackedPointCloudMilliseconds(totalStart),
                            "publisherUnavailable");
                    }

                    return;
                }

                var ensurePublisherMs = ElapsedPackedPointCloudMilliseconds(ensurePublisherStart);
                var tfAnchorMs = 0D;
                var buildMessageMs = 0D;
                var publishMs = 0D;
                var buildMessageStart = 0L;
                var publishStart = 0L;
                try
                {
                    var tfAnchorStart = BeginPackedPointCloudTiming(timingEnabled);
                    PublishTfAnchor(frame);
                    tfAnchorMs = ElapsedPackedPointCloudMilliseconds(tfAnchorStart);

                    buildMessageStart = BeginPackedPointCloudTiming(timingEnabled);
                    var message = Ros2ForUnityPointCloud2MessageBuilder.Build(frame);
                    buildMessageMs = ElapsedPackedPointCloudMilliseconds(buildMessageStart);

                    publishStart = BeginPackedPointCloudTiming(timingEnabled);
                    publisher.Publish(message);
                    publishMs = ElapsedPackedPointCloudMilliseconds(publishStart);
                    UpdateZenohBackpressure(publishMs);
                    _warnedPublishFailure = false;
                    if (timingEnabled)
                    {
                        LogPackedPointCloudPublishTiming(
                            frameTopic,
                            frame,
                            ensurePublisherMs,
                            tfAnchorMs,
                            buildMessageMs,
                            publishMs,
                            ElapsedPackedPointCloudMilliseconds(totalStart),
                            "ok");
                    }
                }
                catch (Exception ex)
                {
                    if (timingEnabled)
                    {
                        if (buildMessageStart != 0L && buildMessageMs == 0D)
                            buildMessageMs = ElapsedPackedPointCloudMilliseconds(buildMessageStart);
                        if (publishStart != 0L && publishMs == 0D)
                            publishMs = ElapsedPackedPointCloudMilliseconds(publishStart);

                        LogPackedPointCloudPublishTiming(
                            frameTopic,
                            frame,
                            ensurePublisherMs,
                            tfAnchorMs,
                            buildMessageMs,
                            publishMs,
                            ElapsedPackedPointCloudMilliseconds(totalStart),
                            "failed");
                    }

                    RecordPublishFailure("ROS2 PointCloud2 publish failed for " + frameTopic + ": " + ex.Message);
                }
            }

            private bool ShouldSkipZenohBackpressureFrame()
            {
                return _usesZenohRmw
                       && Time.unscaledTimeAsDouble < _zenohBackpressureSuppressUntil;
            }

            private void UpdateZenohBackpressure(double publishMs)
            {
                if (!_usesZenohRmw || publishMs < ZenohBackpressurePublishSlowThresholdMs)
                    return;

                _zenohBackpressureSuppressUntil =
                    Time.unscaledTimeAsDouble + ZenohBackpressureCooldownSeconds;
            }

            private static bool IsZenohRmwActive()
            {
                return string.Equals(
                    Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"),
                    "rmw_zenoh_cpp",
                    StringComparison.OrdinalIgnoreCase);
            }

            private bool TryEnsurePublisher(
                ROS2UnityComponent ros2Unity,
                string topic,
                out IPublisher<sensor_msgs.msg.PointCloud2> publisher)
            {
                publisher = null;
                if (_owner.IsShuttingDown)
                    return false;

                if (!IsKnownFrameTopic(topic))
                    return false;

                if (_node != null && _publishers.TryGetValue(topic, out publisher) && publisher != null)
                    return true;

                Exception lastException = null;
                for (var attempt = 0; attempt < MaxNodeCreateAttempts; attempt++)
                {
                    try
                    {
                        _node ??= ros2Unity.CreateNode(BuildNodeName(_source, attempt));
                        publisher = _node.CreateSensorPublisher<sensor_msgs.msg.PointCloud2>(topic);
                        _publishers[topic] = publisher;
                        _warnedPublishFailure = false;
                        LogReady(topic);
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
                    "Unable to create ROS2 PointCloud2 publisher for " + topic + ": "
                    + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private string ResolveFrameTopic(PackedPointCloudFrame frame)
            {
                if (frame != null && !string.IsNullOrWhiteSpace(frame.Topic))
                    return NormalizeTopic(frame.Topic);

                return Topic;
            }

            private string ResolvePrewarmDeskewedTopic()
            {
                if (_source == null || !_source.EnableMotionCompensatedPackedPointCloud)
                    return null;

                if (_source.MotionCompensationOutputPolicy == PointCloudMotionCompensationOutputPolicy.RawOnly)
                    return null;

                if (_source.MotionCompensationOutputPolicy == PointCloudMotionCompensationOutputPolicy.ReplaceOutput)
                    return Topic;

                return NormalizeTopic(_source.MotionCompensatedPackedPointCloudTopic);
            }

            private bool IsKnownFrameTopic(string topic)
            {
                if (string.Equals(topic, Topic, StringComparison.Ordinal))
                    return true;

                var deskewedTopic = ResolvePrewarmDeskewedTopic();
                return !string.IsNullOrWhiteSpace(deskewedTopic)
                       && string.Equals(topic, deskewedTopic, StringComparison.Ordinal);
            }

            private void LogReady(string topic)
            {
                if (_readyLoggedTopics.Contains(topic))
                    return;

                _readyLoggedTopics.Add(topic);
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    _source,
                    "[Foxglove][R2FU] PointCloud2 Native DDS ready: topic={0} tf={1}.",
                    new object[]
                    {
                        topic,
                        DescribeTfAnchor()
                    });
            }

            private string DescribeTfAnchor()
            {
                if (!_source.PublishPackedPointCloudTfAnchor)
                    return "disabled";

                var parentFrame = _source.PackedPointCloudTfParentFrame;
                var childFrame = _source.PackedPointCloudTfChildFrame;
                if (string.IsNullOrWhiteSpace(parentFrame)
                    || string.IsNullOrWhiteSpace(childFrame)
                    || string.Equals(parentFrame, childFrame, StringComparison.Ordinal))
                {
                    return "skipped parent=" + parentFrame + " child=" + childFrame;
                }

                return TfAnchorTopic + " " + parentFrame + "->" + childFrame;
            }

            private void PrewarmTfAnchorPublisher()
            {
                if (!_source.PublishPackedPointCloudTfAnchor || _node == null || _tfAnchorPublisher != null)
                    return;

                var parentFrame = _source.PackedPointCloudTfParentFrame;
                var childFrame = _source.PackedPointCloudTfChildFrame;
                if (string.IsNullOrWhiteSpace(parentFrame)
                    || string.IsNullOrWhiteSpace(childFrame)
                    || string.Equals(parentFrame, childFrame, StringComparison.Ordinal))
                {
                    return;
                }

                try
                {
                    _tfAnchorPublisher = _node.CreatePublisher<tf2_msgs.msg.TFMessage>(TfAnchorTopic);
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("Unable to create ROS2 PointCloud2 TF anchor publisher for " + childFrame + ": " + ex.Message);
                }
            }

            private void PublishTfAnchor(PackedPointCloudFrame frame)
            {
                if (!_source.PublishPackedPointCloudTfAnchor || _node == null)
                    return;

                var parentFrame = _source.PackedPointCloudTfParentFrame;
                var childFrame = _source.PackedPointCloudTfChildFrame;
                if (string.IsNullOrWhiteSpace(parentFrame)
                    || string.IsNullOrWhiteSpace(childFrame)
                    || string.Equals(parentFrame, childFrame, StringComparison.Ordinal))
                {
                    return;
                }

                try
                {
                    _tfAnchorPublisher ??= _node.CreatePublisher<tf2_msgs.msg.TFMessage>(TfAnchorTopic);
                    _tfAnchorPublisher.Publish(BuildTfAnchorMessage(frame, parentFrame, childFrame));
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 PointCloud2 TF anchor publish failed for " + childFrame + ": " + ex.Message);
                }
            }

            private tf2_msgs.msg.TFMessage BuildTfAnchorMessage(
                PackedPointCloudFrame frame,
                string parentFrame,
                string childFrame)
            {
                var unixNs = frame == null ? 0UL : frame.UnixNs;
                ResolveDynamicTfAnchor(out var translation, out var rotation);

                _tfAnchorTransform.Header.Stamp.Sec = (int)(unixNs / 1_000_000_000UL);
                _tfAnchorTransform.Header.Stamp.Nanosec = (uint)(unixNs % 1_000_000_000UL);
                _tfAnchorTransform.Header.Frame_id = parentFrame;
                _tfAnchorTransform.Child_frame_id = childFrame;
                _tfAnchorTransform.Transform.Translation.X = translation.x;
                _tfAnchorTransform.Transform.Translation.Y = translation.y;
                _tfAnchorTransform.Transform.Translation.Z = translation.z;
                _tfAnchorTransform.Transform.Rotation.X = rotation.x;
                _tfAnchorTransform.Transform.Rotation.Y = rotation.y;
                _tfAnchorTransform.Transform.Rotation.Z = rotation.z;
                _tfAnchorTransform.Transform.Rotation.W = rotation.w;
                return _tfAnchorMessage;
            }

            private void ResolveDynamicTfAnchor(out Vector3 translation, out Quaternion rotation)
            {
                translation = CoordinateConverter.UnityToFoxglovePosition(_source.transform.position)
                              + _source.PackedPointCloudTfTranslation;
                rotation = CoordinateConverter.UnityToFoxgloveRotation(_source.transform.rotation)
                           * _source.PackedPointCloudTfRotationRos;
            }

            private void RecordPublishFailure(string message)
            {
                _publishFailureCount++;
                if (_warnedPublishFailure && _publishFailureCount % WarningIntervalFrames != 0)
                    return;

                _warnedPublishFailure = true;
                Debug.LogWarning("[Foxglove][R2FU] " + message);
            }

            private bool ShouldLogPackedPointCloudTiming
                => _source != null && _source.PerformanceDiagnosticsEnabled;

            private static long BeginPackedPointCloudTiming(bool enabled)
                => enabled ? Stopwatch.GetTimestamp() : 0L;

            private static double ElapsedPackedPointCloudMilliseconds(long startTimestamp)
                => startTimestamp == 0L
                    ? 0D
                    : (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

            private static string FormatPackedPointCloudMilliseconds(double milliseconds)
                => milliseconds.ToString("F2", CultureInfo.InvariantCulture);

            private void LogPackedPointCloudPublishTiming(
                string topic,
                PackedPointCloudFrame frame,
                double tryEnsurePublisherMs,
                double tfAnchorMs,
                double buildMessageMs,
                double publishMs,
                double totalMs,
                string result)
            {
                if (!ShouldLogPackedPointCloudTiming)
                    return;

                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    _source,
                    "[Foxglove][R2FU] PointCloud2 native publish timing: topic={0} points={1} bytes={2} deskewed={3} result={4} stageTryEnsurePublisherMs={5} stageTfAnchorMs={6} stageBuildMessageMs={7} stagePublishMs={8} stageTotalMs={9}",
                    new object[]
                    {
                        string.IsNullOrWhiteSpace(topic) ? "(none)" : topic,
                        frame == null ? 0 : frame.ValidCount,
                        frame == null || frame.Data == null ? 0 : frame.Data.Length,
                        frame != null && frame.IsMotionCompensatedVisualization ? "true" : "false",
                        string.IsNullOrWhiteSpace(result) ? "unknown" : result,
                        FormatPackedPointCloudMilliseconds(tryEnsurePublisherMs),
                        FormatPackedPointCloudMilliseconds(tfAnchorMs),
                        FormatPackedPointCloudMilliseconds(buildMessageMs),
                        FormatPackedPointCloudMilliseconds(publishMs),
                        FormatPackedPointCloudMilliseconds(totalMs)
                    });
            }

            private void CleanupRos2()
            {
                if (_node != null)
                {
                    foreach (var publisher in _publishers.Values)
                    {
                        try { _node.RemovePublisher<sensor_msgs.msg.PointCloud2>(publisher); }
                        catch (Exception) { }
                    }
                }

                if (_node != null && _tfAnchorPublisher != null)
                {
                    try { _node.RemovePublisher<tf2_msgs.msg.TFMessage>(_tfAnchorPublisher); }
                    catch (Exception) { }
                }

                if (_owner._ros2Unity != null && _node != null)
                {
                    try { _owner._ros2Unity.RemoveNode(_node); }
                    catch (Exception) { }
                }

                _publishers.Clear();
                _readyLoggedTopics.Clear();
                _tfAnchorPublisher = null;
                _node = null;
            }
        }
    }
}
#endif
