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
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-450)]
    internal sealed class Ros2ForUnityPointCloud2NativeBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU PointCloud2 Native Bridge";
        private const string TfAnchorTopic = "/tf";
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;

        private static Ros2ForUnityPointCloud2NativeBridge _instance;

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
            var publishers = FindObjectsByType<FoxglovePointCloudPublisher>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var publisher in publishers)
            {
                if (!IsEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                _seen.Add(instanceId);
                var topic = NormalizeTopic(publisher.PointCloud2NativeTopic);
                if (_bindings.TryGetValue(instanceId, out var existing))
                {
                    if (existing.Topic == topic)
                    {
                        existing.PrewarmPublishers(_ros2Unity);
                        continue;
                    }

                    existing.Dispose();
                    _bindings.Remove(instanceId);
                }

                var binding = new Binding(this, publisher, topic);
                binding.Subscribe();
                binding.PrewarmPublishers(_ros2Unity);
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

        private static bool IsEligible(FoxglovePointCloudPublisher publisher)
            => publisher != null
               && publisher.isActiveAndEnabled
               && publisher.IsPointCloud2NativeOutput;

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
            private readonly Dictionary<string, IPublisher<sensor_msgs.msg.PointCloud2>> _publishers =
                new Dictionary<string, IPublisher<sensor_msgs.msg.PointCloud2>>(StringComparer.Ordinal);
            private readonly HashSet<string> _readyLoggedTopics = new HashSet<string>(StringComparer.Ordinal);
            private ROS2Node _node;
            private IPublisher<tf2_msgs.msg.TFMessage> _tfAnchorPublisher;
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
                    _source.PointCloud2NativeFrameReady -= OnPointCloud2NativeFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnPointCloud2NativeFrameReady(PointCloud2NativeFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || _owner.IsShuttingDown)
                    return;

                var timingEnabled = ShouldLogPointCloud2NativeTiming;
                var totalStart = BeginPointCloud2NativeTiming(timingEnabled);
                if (!_owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                var frameTopic = ResolveFrameTopic(frame);
                var ensurePublisherStart = BeginPointCloud2NativeTiming(timingEnabled);
                if (!TryEnsurePublisher(ros2Unity, frameTopic, out var publisher))
                {
                    if (timingEnabled)
                    {
                        LogPointCloud2NativePublishTiming(
                            frameTopic,
                            frame,
                            ElapsedPointCloud2NativeMilliseconds(ensurePublisherStart),
                            0D,
                            0D,
                            0D,
                            ElapsedPointCloud2NativeMilliseconds(totalStart),
                            "publisherUnavailable");
                    }

                    return;
                }

                var ensurePublisherMs = ElapsedPointCloud2NativeMilliseconds(ensurePublisherStart);
                var tfAnchorMs = 0D;
                var buildMessageMs = 0D;
                var publishMs = 0D;
                var buildMessageStart = 0L;
                var publishStart = 0L;
                try
                {
                    var tfAnchorStart = BeginPointCloud2NativeTiming(timingEnabled);
                    PublishTfAnchor(frame);
                    tfAnchorMs = ElapsedPointCloud2NativeMilliseconds(tfAnchorStart);

                    buildMessageStart = BeginPointCloud2NativeTiming(timingEnabled);
                    var message = Ros2ForUnityPointCloud2MessageBuilder.Build(frame);
                    buildMessageMs = ElapsedPointCloud2NativeMilliseconds(buildMessageStart);

                    publishStart = BeginPointCloud2NativeTiming(timingEnabled);
                    publisher.Publish(message);
                    publishMs = ElapsedPointCloud2NativeMilliseconds(publishStart);
                    _warnedPublishFailure = false;
                    if (timingEnabled)
                    {
                        LogPointCloud2NativePublishTiming(
                            frameTopic,
                            frame,
                            ensurePublisherMs,
                            tfAnchorMs,
                            buildMessageMs,
                            publishMs,
                            ElapsedPointCloud2NativeMilliseconds(totalStart),
                            "ok");
                    }
                }
                catch (Exception ex)
                {
                    if (timingEnabled)
                    {
                        if (buildMessageStart != 0L && buildMessageMs == 0D)
                            buildMessageMs = ElapsedPointCloud2NativeMilliseconds(buildMessageStart);
                        if (publishStart != 0L && publishMs == 0D)
                            publishMs = ElapsedPointCloud2NativeMilliseconds(publishStart);

                        LogPointCloud2NativePublishTiming(
                            frameTopic,
                            frame,
                            ensurePublisherMs,
                            tfAnchorMs,
                            buildMessageMs,
                            publishMs,
                            ElapsedPointCloud2NativeMilliseconds(totalStart),
                            "failed");
                    }

                    RecordPublishFailure("ROS2 PointCloud2 publish failed for " + frameTopic + ": " + ex.Message);
                }
            }

            private bool TryEnsurePublisher(
                ROS2UnityComponent ros2Unity,
                string topic,
                out IPublisher<sensor_msgs.msg.PointCloud2> publisher)
            {
                publisher = null;
                if (_owner.IsShuttingDown)
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

            private string ResolveFrameTopic(PointCloud2NativeFrame frame)
            {
                if (frame != null && !string.IsNullOrWhiteSpace(frame.Topic))
                    return NormalizeTopic(frame.Topic);

                return Topic;
            }

            private string ResolvePrewarmDeskewedTopic()
            {
                if (_source == null || !_source.EnableMotionCompensatedPointCloud2)
                    return null;

                if (_source.MotionCompensationOutputPolicy == PointCloudMotionCompensationOutputPolicy.RawOnly)
                    return null;

                if (_source.MotionCompensationOutputPolicy == PointCloudMotionCompensationOutputPolicy.ReplaceOutput)
                    return Topic;

                return NormalizeTopic(_source.MotionCompensatedPointCloud2Topic);
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
                if (!_source.PublishPointCloud2NativeTfAnchor)
                    return "disabled";

                var parentFrame = _source.PointCloud2NativeTfParentFrame;
                var childFrame = _source.PointCloud2NativeTfChildFrame;
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
                if (!_source.PublishPointCloud2NativeTfAnchor || _node == null || _tfAnchorPublisher != null)
                    return;

                var parentFrame = _source.PointCloud2NativeTfParentFrame;
                var childFrame = _source.PointCloud2NativeTfChildFrame;
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

            private void PublishTfAnchor(PointCloud2NativeFrame frame)
            {
                if (!_source.PublishPointCloud2NativeTfAnchor || _node == null)
                    return;

                var parentFrame = _source.PointCloud2NativeTfParentFrame;
                var childFrame = _source.PointCloud2NativeTfChildFrame;
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
                PointCloud2NativeFrame frame,
                string parentFrame,
                string childFrame)
            {
                var unixNs = frame == null ? 0UL : frame.UnixNs;
                ResolveDynamicTfAnchor(out var translation, out var rotation);

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
                                    Sec = (int)(unixNs / 1_000_000_000UL),
                                    Nanosec = (uint)(unixNs % 1_000_000_000UL)
                                },
                                Frame_id = parentFrame
                            },
                            Child_frame_id = childFrame,
                            Transform = new geometry_msgs.msg.Transform
                            {
                                Translation = new geometry_msgs.msg.Vector3
                                {
                                    X = translation.x,
                                    Y = translation.y,
                                    Z = translation.z
                                },
                                Rotation = new geometry_msgs.msg.Quaternion
                                {
                                    X = rotation.x,
                                    Y = rotation.y,
                                    Z = rotation.z,
                                    W = rotation.w
                                }
                            }
                        }
                    }
                };
            }

            private void ResolveDynamicTfAnchor(out Vector3 translation, out Quaternion rotation)
            {
                translation = CoordinateConverter.UnityToFoxglovePosition(_source.transform.position)
                              + _source.PointCloud2NativeTfTranslation;
                rotation = CoordinateConverter.UnityToFoxgloveRotation(_source.transform.rotation)
                           * _source.PointCloud2NativeTfRotationRos;
            }

            private void RecordPublishFailure(string message)
            {
                _publishFailureCount++;
                if (_warnedPublishFailure && _publishFailureCount % WarningIntervalFrames != 0)
                    return;

                _warnedPublishFailure = true;
                Debug.LogWarning("[Foxglove][R2FU] " + message);
            }

            private bool ShouldLogPointCloud2NativeTiming
                => _source != null && _source.PerformanceDiagnosticsEnabled;

            private static long BeginPointCloud2NativeTiming(bool enabled)
                => enabled ? Stopwatch.GetTimestamp() : 0L;

            private static double ElapsedPointCloud2NativeMilliseconds(long startTimestamp)
                => startTimestamp == 0L
                    ? 0D
                    : (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

            private static string FormatPointCloud2NativeMilliseconds(double milliseconds)
                => milliseconds.ToString("F2", CultureInfo.InvariantCulture);

            private void LogPointCloud2NativePublishTiming(
                string topic,
                PointCloud2NativeFrame frame,
                double tryEnsurePublisherMs,
                double tfAnchorMs,
                double buildMessageMs,
                double publishMs,
                double totalMs,
                string result)
            {
                if (!ShouldLogPointCloud2NativeTiming)
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
                        FormatPointCloud2NativeMilliseconds(tryEnsurePublisherMs),
                        FormatPointCloud2NativeMilliseconds(tfAnchorMs),
                        FormatPointCloud2NativeMilliseconds(buildMessageMs),
                        FormatPointCloud2NativeMilliseconds(publishMs),
                        FormatPointCloud2NativeMilliseconds(totalMs)
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
