// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Inbound CompressedImage subscription with a bounded main-thread handoff.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Globalization;
using Stopwatch = System.Diagnostics.Stopwatch;
using ROS2;
using Unity.FoxgloveSDK.Schemas.Camera;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>Subscribes to JPEG/PNG CompressedImage frames and raises them from Update.</summary>
    [AddComponentMenu("Foxglove/ROS2 For Unity/Compressed Image Inbound")]
    [DefaultExecutionOrder(-430)]
    public sealed class Ros2ForUnityCompressedImageInboundBridge : MonoBehaviour
    {
        [SerializeField]
        private string _topic = "/camera/image/compressed";

        [SerializeField]
        private long _maxFrameBytes = 4L * 1024L * 1024L;

        [SerializeField]
        private int _maxFramesPerSecond = 60;

        [SerializeField]
        private long _maxBytesPerSecond = 64L * 1024L * 1024L;

        private ROS2UnityComponent _ros2Unity;
        private ROS2Node _node;
        private ISubscriptionBase _subscription;
        private SensorCompressedImageFrameSlot _slot;
        private bool _isStopping;
        private bool _runtimeWasReady;
        private bool _cleanupPending;
        private bool _warnedRuntime;
        private int _failureCount;
        private string _subscribedTopic = string.Empty;
        private CompressedImageInboundBudget _ingressBudget;

        /// <summary>Raised from Update after the callback-thread copy is queued.</summary>
        public event Action<SensorCompressedImageFrame> FrameReceived;

        public string Topic
        {
            get => NormalizeTopic(_topic);
            set => _topic = NormalizeTopic(value);
        }

        public long MaxFrameBytes
        {
            get => _maxFrameBytes;
            set => _maxFrameBytes = value;
        }

        public CompressedImageSlotStats Stats
            => _slot == null ? default : _slot.Stats;

        private bool IsShuttingDown
            => _isStopping
               || Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene);

        private void OnEnable()
        {
            _isStopping = false;
            _runtimeWasReady = false;
            _cleanupPending = false;
            _subscribedTopic = string.Empty;
            _ingressBudget = new CompressedImageInboundBudget(_maxFramesPerSecond, _maxBytesPerSecond);
            _slot?.Dispose();
            _slot = new SensorCompressedImageFrameSlot();
            Application.quitting += OnApplicationQuitting;
        }

        private void OnDisable()
        {
            _isStopping = true;
            CleanupNative();
            _slot?.Dispose();
            Application.quitting -= OnApplicationQuitting;
        }

        private void OnDestroy()
        {
            _isStopping = true;
            CleanupNative();
        }

        private void Update()
        {
            if (IsShuttingDown)
            {
                CleanupNative();
                DrainOne();
                return;
            }

            if (_subscription != null && !string.Equals(_subscribedTopic, Topic, StringComparison.Ordinal))
                _cleanupPending = true;
            if (_cleanupPending)
            {
                CleanupNative();
                if (_cleanupPending)
                {
                    DrainOne();
                    return;
                }
            }

            if (!EnsureRuntime())
            {
                DrainOne();
                return;
            }

            EnsureSubscription();
            DrainOne();
        }

        private bool EnsureRuntime()
        {
            if (!Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene))
                return false;

            if (_ros2Unity == null)
                _ros2Unity = GetComponent<ROS2UnityComponent>() ?? gameObject.AddComponent<ROS2UnityComponent>();

            try
            {
                if (!_ros2Unity.Ok())
                {
                    if (_runtimeWasReady)
                    {
                        Ros2ForUnityNativeBridgeRecovery.ResetAfterRuntimeLoss(
                            ref _runtimeWasReady,
                            CleanupNative);
                    }
                    return false;
                }
            }
            catch (Exception exception)
            {
                if (_runtimeWasReady)
                {
                    Ros2ForUnityNativeBridgeRecovery.ResetAfterRuntimeLoss(
                        ref _runtimeWasReady,
                        CleanupNative);
                }
                RecordFailure("ROS2 runtime check failed: " + exception.Message);
                return false;
            }

            _runtimeWasReady = true;
            _warnedRuntime = false;
            return true;
        }

        private void EnsureSubscription()
        {
            if (_cleanupPending)
            {
                CleanupNative();
                if (_cleanupPending)
                    return;
            }
            if (_subscription != null)
                return;
            if (_node != null)
            {
                CleanupNative();
                if (_cleanupPending)
                    return;
            }
            if (_ros2Unity == null || IsShuttingDown)
                return;

            try
            {
                _node = _ros2Unity.CreateNode(NodeName);
                if (_node == null)
                    throw new InvalidOperationException("R2FU returned no CompressedImage inbound node.");

                if (!Ros2ForUnityNativeQosMapper.TryCreate(
                        FoxRunResolvedQos.SensorData,
                        out var qosProfile).Succeeded)
                    throw new InvalidOperationException("R2FU sensor-data QoS profile could not be created.");

                using (qosProfile)
                {
                    _subscription = _node.CreateSubscription<sensor_msgs.msg.CompressedImage>(
                        Topic,
                        OnCompressedImage,
                        qosProfile.NativeProfile);
                }

                if (_subscription == null)
                    throw new InvalidOperationException("R2FU returned no CompressedImage subscription.");
                _subscribedTopic = Topic;
                _cleanupPending = false;
                _failureCount = 0;
            }
            catch (Exception exception)
            {
                CleanupNative();
                RecordFailure("CompressedImage subscription setup failed: " + exception.Message);
            }
        }

        private void OnCompressedImage(sensor_msgs.msg.CompressedImage message)
        {
            var data = message?.Data;
            if (message == null || data == null)
            {
                _slot.RecordRejected("CompressedImage callback returned no message data");
                return;
            }

            if (data.Length > _maxFrameBytes)
            {
                _slot.RecordRejected("CompressedImage data exceeds the configured frame limit");
                return;
            }

            if (_ingressBudget == null)
                _ingressBudget = new CompressedImageInboundBudget(_maxFramesPerSecond, _maxBytesPerSecond);
            if (!_ingressBudget.TryAdmit(data.Length, Stopwatch.GetTimestamp()))
            {
                _slot.RecordRejected("CompressedImage ingress budget exceeded");
                return;
            }

            var header = message.Header;
            var stamp = header?.Stamp;
            ulong unixNs;
            try
            {
                unixNs = stamp == null || stamp.Sec < 0
                    ? 0UL
                    : checked((ulong)stamp.Sec * 1_000_000_000UL + stamp.Nanosec);
            }
            catch (OverflowException)
            {
                _slot.RecordRejected("CompressedImage header timestamp exceeds the supported range");
                return;
            }

            var neutral = new CompressedImageInboundMessage(
                unixNs,
                header?.Frame_id,
                data,
                message.Format);
            if (!CompressedImageInboundMapper.TryMap(
                    neutral,
                    _maxFrameBytes,
                    out var frame,
                    out var error))
            {
                _slot.RecordRejected(error);
                return;
            }

            _slot.Publish(frame);
        }

        private void DrainOne()
        {
            if (_slot == null || !_slot.TryTake(out var frame))
                return;

            var handler = FrameReceived;
            if (handler == null)
                return;

            try
            {
                handler(frame);
            }
            catch (Exception exception)
            {
                _slot.RecordRejected("CompressedImage frame handler failed: " + exception.Message);
                RecordFailure("CompressedImage frame handler failed: " + exception.Message);
            }
        }

        private void CleanupNative()
        {
            if (_node == null)
            {
                _subscription = null;
                _cleanupPending = false;
                return;
            }

            if (_subscription != null)
            {
                try
                {
                    if (!_node.RemoveSubscription(_subscription))
                    {
                        _cleanupPending = true;
                        return;
                    }
                    _subscription = null;
                }
                catch (Exception exception)
                {
                    RecordFailure("CompressedImage subscription cleanup failed: " + exception.Message);
                    _cleanupPending = true;
                    return;
                }
            }

            if (_ros2Unity == null)
            {
                _cleanupPending = true;
                return;
            }
            try
            {
                if (!_ros2Unity.TryRemoveNode(_node))
                {
                    _cleanupPending = true;
                    return;
                }
                _node = null;
                _subscribedTopic = string.Empty;
                _cleanupPending = false;
            }
            catch (Exception exception)
            {
                RecordFailure("CompressedImage node cleanup failed: " + exception.Message);
                _cleanupPending = true;
            }
        }

        private void OnApplicationQuitting()
        {
            _isStopping = true;
            CleanupNative();
        }

        private void RecordFailure(string message)
        {
            _failureCount++;
            if (_warnedRuntime && _failureCount % 240 != 0)
                return;
            _warnedRuntime = true;
            Debug.LogWarning("[Foxglove][R2FU] " + message);
        }

        private static string NormalizeTopic(string topic)
        {
            var value = string.IsNullOrWhiteSpace(topic) ? "/camera/image/compressed" : topic.Trim();
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        private string NodeName
            => "u2f_compressed_image_inbound_"
               + unchecked((uint)GetInstanceID()).ToString(CultureInfo.InvariantCulture);
    }
}
#endif
