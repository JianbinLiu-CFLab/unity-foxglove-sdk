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

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [DefaultExecutionOrder(-440)]
    internal sealed class Ros2ForUnityCameraNativeBridge : MonoBehaviour
    {
        private const string BridgeObjectName = "Unity2Foxglove R2FU Camera Native Bridge";
        private const string TfAnchorTopic = "/tf";
        private const float ScanIntervalSeconds = 0.5f;
        private const int MaxNodeCreateAttempts = 4;
        private const int WarningIntervalFrames = 240;

        private static Ros2ForUnityCameraNativeBridge _instance;
        private static bool _runtimeShuttingDown;

        private readonly Dictionary<int, ImageBinding> _imageBindings = new Dictionary<int, ImageBinding>();
        private readonly Dictionary<int, InfoBinding> _infoBindings = new Dictionary<int, InfoBinding>();
        private ROS2UnityComponent _ros2Unity;
        private float _nextScanAt;
        private int _ros2FailureCount;
        private bool _warnedRos2Unavailable;
        private bool _isStopping;
        private bool _ros2RuntimeWasReady;

        private bool IsShuttingDown => _isStopping || _runtimeShuttingDown || !Application.isPlaying;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _runtimeShuttingDown = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
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

            if (Time.unscaledTime < _nextScanAt)
                return;

            _nextScanAt = Time.unscaledTime + ScanIntervalSeconds;
            RefreshBindings();
        }

        private void RefreshBindings()
        {
            RefreshImageBindings();
            RefreshInfoBindings();
        }

        private void RefreshImageBindings()
        {
            var seen = new HashSet<int>();
            var publishers = FindObjectsByType<FoxgloveCameraPublisher>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var publisher in publishers)
            {
                if (!IsEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                seen.Add(instanceId);
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

            RemoveStale(_imageBindings, seen);
        }

        private void RefreshInfoBindings()
        {
            var seen = new HashSet<int>();
            var publishers = FindObjectsByType<FoxgloveCameraInfoPublisher>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (var publisher in publishers)
            {
                if (!IsEligible(publisher))
                    continue;

                var instanceId = publisher.GetInstanceID();
                seen.Add(instanceId);
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

            RemoveStale(_infoBindings, seen);
        }

        private static void RemoveStale<TBinding>(Dictionary<int, TBinding> bindings, HashSet<int> seen)
            where TBinding : BindingBase
        {
            var stale = new List<int>();
            foreach (var pair in bindings)
            {
                if (!seen.Contains(pair.Key) || !pair.Value.IsStillEligible())
                    stale.Add(pair.Key);
            }

            foreach (var key in stale)
            {
                bindings[key].Dispose();
                bindings.Remove(key);
            }
        }

        private static bool IsEligible(FoxgloveCameraPublisher publisher)
            => publisher != null
               && publisher.isActiveAndEnabled
               && publisher.IsStandardRos2CompressedImageOutput;

        private static bool IsEligible(FoxgloveCameraInfoPublisher publisher)
            => publisher != null && publisher.isActiveAndEnabled;

        private bool TryGetRos2Unity(out ROS2UnityComponent ros2Unity)
        {
            ros2Unity = null;
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
            _isStopping = true;
            _runtimeShuttingDown = true;
            ClearBindings();
        }

        private void ClearBindings()
        {
            foreach (var binding in _imageBindings.Values)
                binding.Dispose();
            foreach (var binding in _infoBindings.Values)
                binding.Dispose();

            _imageBindings.Clear();
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

        private abstract class BindingBase : IDisposable
        {
            protected readonly Ros2ForUnityCameraNativeBridge Owner;
            protected ROS2Node Node;
            protected bool WarnedPublishFailure;
            protected bool ReadyLogged;
            private int _publishFailureCount;

            protected BindingBase(Ros2ForUnityCameraNativeBridge owner, string topic)
            {
                Owner = owner;
                Topic = topic;
            }

            public string Topic { get; }

            public abstract void Subscribe();
            public abstract bool IsStillEligible();
            public abstract void Dispose();

            protected void RecordPublishFailure(string message)
            {
                _publishFailureCount++;
                if (WarnedPublishFailure && _publishFailureCount % WarningIntervalFrames != 0)
                    return;

                WarnedPublishFailure = true;
                Debug.LogWarning("[Foxglove][R2FU] " + message);
            }

            protected void CleanupNode()
            {
                if (Owner._ros2Unity != null && Node != null)
                {
                    try { Owner._ros2Unity.RemoveNode(Node); }
                    catch (Exception) { }
                }

                Node = null;
            }
        }

        private sealed class ImageBinding : BindingBase
        {
            private readonly FoxgloveCameraPublisher _source;
            private IPublisher<sensor_msgs.msg.CompressedImage> _publisher;
            private bool _subscribed;

            public ImageBinding(Ros2ForUnityCameraNativeBridge owner, FoxgloveCameraPublisher source, string topic)
                : base(owner, topic)
            {
                _source = source;
            }

            public override void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.SensorCompressedImageReady += OnFrameReady;
                _subscribed = true;
            }

            public override bool IsStillEligible()
                => IsEligible(_source) && NormalizeTopic(_source.SensorCameraImageTopic) == Topic;

            public override void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.SensorCompressedImageReady -= OnFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnFrameReady(SensorCompressedImageFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || Owner.IsShuttingDown)
                    return;

                if (!Owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    _publisher.Publish(Ros2ForUnityCameraMessageBuilder.BuildCompressedImage(frame));
                    WarnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Camera CompressedImage publish failed for " + Topic + ": " + ex.Message);
                }
            }

            private bool TryEnsurePublisher(ROS2UnityComponent ros2Unity)
            {
                if (Owner.IsShuttingDown)
                    return false;

                if (Node != null && _publisher != null)
                    return true;

                Exception lastException = null;
                for (var attempt = 0; attempt < MaxNodeCreateAttempts; attempt++)
                {
                    try
                    {
                        Node = ros2Unity.CreateNode(BuildNodeName(_source, "image", attempt));
                        _publisher = Node.CreatePublisher<sensor_msgs.msg.CompressedImage>(Topic);
                        WarnedPublishFailure = false;
                        LogReadyOnce();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        CleanupRos2();
                    }
                }

                RecordPublishFailure("Unable to create ROS2 Camera CompressedImage publisher for " + Topic + ": "
                                     + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void LogReadyOnce()
            {
                if (ReadyLogged)
                    return;

                ReadyLogged = true;
                Debug.Log("[Foxglove][R2FU] Camera CompressedImage DDS ready: topic=" + Topic + ".");
            }

            private void CleanupRos2()
            {
                if (Node != null && _publisher != null)
                {
                    try { Node.RemovePublisher<sensor_msgs.msg.CompressedImage>(_publisher); }
                    catch (Exception) { }
                }

                _publisher = null;
                CleanupNode();
            }
        }

        private sealed class InfoBinding : BindingBase
        {
            private readonly FoxgloveCameraInfoPublisher _source;
            private IPublisher<sensor_msgs.msg.CameraInfo> _publisher;
            private IPublisher<tf2_msgs.msg.TFMessage> _tfAnchorPublisher;
            private bool _subscribed;

            public InfoBinding(Ros2ForUnityCameraNativeBridge owner, FoxgloveCameraInfoPublisher source, string topic)
                : base(owner, topic)
            {
                _source = source;
            }

            public override void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.SensorCameraInfoReady += OnFrameReady;
                _subscribed = true;
            }

            public override bool IsStillEligible()
                => IsEligible(_source) && NormalizeTopic(_source.SensorCameraInfoTopic) == Topic;

            public override void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.SensorCameraInfoReady -= OnFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnFrameReady(SensorCameraInfoFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || Owner.IsShuttingDown)
                    return;

                if (!Owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    PublishTfAnchor(frame);
                    _publisher.Publish(Ros2ForUnityCameraMessageBuilder.BuildCameraInfo(frame));
                    WarnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 CameraInfo publish failed for " + Topic + ": " + ex.Message);
                }
            }

            private bool TryEnsurePublisher(ROS2UnityComponent ros2Unity)
            {
                if (Owner.IsShuttingDown)
                    return false;

                if (Node != null && _publisher != null)
                    return true;

                Exception lastException = null;
                for (var attempt = 0; attempt < MaxNodeCreateAttempts; attempt++)
                {
                    try
                    {
                        Node = ros2Unity.CreateNode(BuildNodeName(_source, "info", attempt));
                        _publisher = Node.CreatePublisher<sensor_msgs.msg.CameraInfo>(Topic);
                        WarnedPublishFailure = false;
                        LogReadyOnce();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        CleanupRos2();
                    }
                }

                RecordPublishFailure("Unable to create ROS2 CameraInfo publisher for " + Topic + ": "
                                     + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void LogReadyOnce()
            {
                if (ReadyLogged)
                    return;

                ReadyLogged = true;
                Debug.Log("[Foxglove][R2FU] CameraInfo DDS ready: topic=" + Topic + " tf=" + DescribeTfAnchor() + ".");
            }

            private string DescribeTfAnchor()
            {
                if (!_source.PublishCameraTfAnchor)
                    return "disabled";

                var parentFrame = _source.CameraTfParentFrame;
                var childFrame = _source.CameraTfChildFrame;
                if (string.IsNullOrWhiteSpace(parentFrame)
                    || string.IsNullOrWhiteSpace(childFrame)
                    || string.Equals(parentFrame, childFrame, StringComparison.Ordinal))
                {
                    return "skipped parent=" + parentFrame + " child=" + childFrame;
                }

                return TfAnchorTopic + " " + parentFrame + "->" + childFrame;
            }

            private void PublishTfAnchor(SensorCameraInfoFrame frame)
            {
                if (!_source.PublishCameraTfAnchor || Node == null)
                    return;

                var parentFrame = _source.CameraTfParentFrame;
                var childFrame = _source.CameraTfChildFrame;
                if (string.IsNullOrWhiteSpace(parentFrame)
                    || string.IsNullOrWhiteSpace(childFrame)
                    || string.Equals(parentFrame, childFrame, StringComparison.Ordinal))
                {
                    return;
                }

                try
                {
                    _tfAnchorPublisher ??= Node.CreatePublisher<tf2_msgs.msg.TFMessage>(TfAnchorTopic);
                    _tfAnchorPublisher.Publish(BuildTfAnchorMessage(frame, parentFrame, childFrame));
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Camera TF anchor publish failed for " + childFrame + ": " + ex.Message);
                }
            }

            private tf2_msgs.msg.TFMessage BuildTfAnchorMessage(
                SensorCameraInfoFrame frame,
                string parentFrame,
                string childFrame)
            {
                var translation = _source.CameraTfTranslation;
                var rotation = _source.CameraTfRotation;

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
                                    Sec = (int)(frame.UnixNs / 1_000_000_000UL),
                                    Nanosec = (uint)(frame.UnixNs % 1_000_000_000UL)
                                },
                                Frame_id = parentFrame
                            },
                            Child_frame_id = childFrame,
                            Transform = new geometry_msgs.msg.Transform
                            {
                                Translation = new geometry_msgs.msg.Vector3
                                {
                                    X = translation.X,
                                    Y = translation.Y,
                                    Z = translation.Z
                                },
                                Rotation = new geometry_msgs.msg.Quaternion
                                {
                                    X = rotation.X,
                                    Y = rotation.Y,
                                    Z = rotation.Z,
                                    W = rotation.W
                                }
                            }
                        }
                    }
                };
            }

            private void CleanupRos2()
            {
                if (Node != null && _publisher != null)
                {
                    try { Node.RemovePublisher<sensor_msgs.msg.CameraInfo>(_publisher); }
                    catch (Exception) { }
                }

                if (Node != null && _tfAnchorPublisher != null)
                {
                    try { Node.RemovePublisher<tf2_msgs.msg.TFMessage>(_tfAnchorPublisher); }
                    catch (Exception) { }
                }

                _publisher = null;
                _tfAnchorPublisher = null;
                CleanupNode();
            }
        }
    }
}
#endif
