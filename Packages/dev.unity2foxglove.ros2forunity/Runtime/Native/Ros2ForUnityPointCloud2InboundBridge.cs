// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Inbound PointCloud2 subscription with a bounded main-thread handoff.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ROS2;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Subscribes to a standard ROS2 PointCloud2 topic and raises packed frames
    /// from Update on the Unity main thread. The bridge owns its R2FU node
    /// because the public host API has no shared non-FoxRun node lease.
    /// </summary>
    [AddComponentMenu("Foxglove/ROS2 For Unity/PointCloud2 Inbound")]
    [DefaultExecutionOrder(-430)]
    public sealed class Ros2ForUnityPointCloud2InboundBridge : MonoBehaviour
    {
        [SerializeField]
        private string _topic = "/points";

        [SerializeField]
        private long _maxFrameBytes = 4L * 1024L * 1024L;

        private ROS2UnityComponent _ros2Unity;
        private ROS2Node _node;
        private ISubscriptionBase _subscription;
        private PackedPointCloudFrameSlot _slot;
        private bool _isStopping;
        private bool _runtimeWasReady;
        private bool _cleanupPending;
        private bool _warnedRuntime;
        private int _failureCount;
        private string _subscribedTopic = string.Empty;

        /// <summary>Raised from Update after the callback-thread copy is queued.</summary>
        public event Action<PackedPointCloudFrame> FrameReceived;

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

        public PackedPointCloudSlotStats Stats
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
            _slot?.Dispose();
            _slot = new PackedPointCloudFrameSlot();
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
                    throw new InvalidOperationException("R2FU returned no PointCloud2 inbound node.");

                if (!Ros2ForUnityNativeQosMapper.TryCreate(
                        FoxRunResolvedQos.SensorData,
                        out var qosProfile).Succeeded)
                {
                    throw new InvalidOperationException("R2FU sensor-data QoS profile could not be created.");
                }

                using (qosProfile)
                {
                    _subscription = _node.CreateSubscription<sensor_msgs.msg.PointCloud2>(
                        Topic,
                        OnPointCloud2,
                        qosProfile.NativeProfile);
                }

                if (_subscription == null)
                    throw new InvalidOperationException("R2FU returned no PointCloud2 subscription.");
                _subscribedTopic = Topic;
                _cleanupPending = false;
                _failureCount = 0;
            }
            catch (Exception exception)
            {
                CleanupNative();
                RecordFailure("PointCloud2 subscription setup failed: " + exception.Message);
            }
        }

        private void OnPointCloud2(sensor_msgs.msg.PointCloud2 message)
        {
            var data = message?.Data;
            if (message == null || data == null)
            {
                _slot.RecordRejected("PointCloud2 callback returned no message data");
                return;
            }

            if (data.Length > _maxFrameBytes)
            {
                _slot.RecordRejected("PointCloud2 data exceeds the configured frame limit");
                return;
            }

            if (message.Fields != null && message.Fields.Length > PointCloud2InboundMapper.MaxFieldCount)
            {
                _slot.RecordRejected("PointCloud2 field metadata exceeds the field count limit");
                return;
            }
            var metadataBytes = 0;
            if (message.Fields != null)
            {
                foreach (var field in message.Fields)
                {
                    if (field == null || field.Count != 1)
                    {
                        _slot.RecordRejected("PointCloud2 fields must have count 1");
                        return;
                    }
                    var fieldName = field.Name ?? string.Empty;
                    if (fieldName.Length > PointCloud2InboundMapper.MaxFieldNameBytes)
                    {
                        _slot.RecordRejected("PointCloud2 field metadata name exceeds the limit");
                        return;
                    }
                    var nameBytes = Encoding.UTF8.GetByteCount(fieldName);
                    if (nameBytes > PointCloud2InboundMapper.MaxFieldNameBytes)
                    {
                        _slot.RecordRejected("PointCloud2 field metadata name exceeds the limit");
                        return;
                    }
                    metadataBytes = checked(metadataBytes + nameBytes + 16);
                    if (metadataBytes > PointCloud2InboundMapper.MaxFieldMetadataBytes)
                    {
                        _slot.RecordRejected("PointCloud2 field metadata exceeds the limit");
                        return;
                    }
                }
            }

            if (!PointCloud2InboundMapper.TryGetCompactByteCount(
                    message.Height,
                    message.Width,
                    message.Point_step,
                    message.Row_step,
                    data.Length,
                    _maxFrameBytes,
                    out var packedRowStep,
                    out var compactBytes,
                    out var dimensionError))
            {
                _slot.RecordRejected(dimensionError);
                return;
            }

            var pooled = PointCloudPackedByteBufferPool.Rent(data.Length);
            var ownsPooled = true;
            try
            {
                Buffer.BlockCopy(data, 0, pooled, 0, data.Length);
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
                    _slot.RecordRejected("PointCloud2 header timestamp exceeds the supported range");
                    return;
                }

                if (message.Row_step > packedRowStep)
                {
                    var compacted = PointCloudPackedByteBufferPool.Rent(compactBytes);
                    for (var row = 0; row < message.Height; row++)
                    {
                        Buffer.BlockCopy(
                            pooled,
                            checked((int)((ulong)row * message.Row_step)),
                            compacted,
                            checked((int)((ulong)row * packedRowStep)),
                            checked((int)packedRowStep));
                    }
                    PointCloudPackedByteBufferPool.Return(pooled);
                    pooled = compacted;
                }

                var mappedRowStep = checked((uint)packedRowStep);

                var fields = new List<PointCloud2InboundField>(message.Fields?.Length ?? 0);
                if (message.Fields != null)
                {
                    foreach (var field in message.Fields)
                        fields.Add(new PointCloud2InboundField(field.Name, field.Offset, field.Datatype, field.Count));
                }

                var neutral = new PointCloud2InboundMessage(
                    unixNs,
                    header?.Frame_id,
                    message.Height,
                    message.Width,
                    fields,
                    message.Point_step,
                    mappedRowStep,
                    pooled,
                    message.Is_bigendian,
                    message.Is_dense);

                if (!PointCloud2InboundMapper.TryMapOwned(
                        neutral,
                        _maxFrameBytes,
                        Topic,
                        out var frame,
                        out var error))
                {
                    _slot.RecordRejected(error);
                    return;
                }

                ownsPooled = false;
                if (!_slot.Publish(frame))
                    frame.Dispose();
            }
            catch (Exception exception)
            {
                _slot.RecordRejected("PointCloud2 callback failed: " + exception.Message);
            }
            finally
            {
                if (ownsPooled)
                    PointCloudPackedByteBufferPool.Return(pooled);
            }
        }

        private void DrainOne()
        {
            if (_slot == null || !_slot.TryTake(out var frame))
                return;

            var handler = FrameReceived;
            if (handler == null)
            {
                frame.Dispose();
                return;
            }

            try
            {
                handler(frame);
            }
            catch (Exception exception)
            {
                _slot.RecordRejected("PointCloud2 frame handler failed: " + exception.Message);
                RecordFailure("PointCloud2 frame handler failed: " + exception.Message);
            }
            finally
            {
                frame.Dispose();
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
                    RecordFailure("PointCloud2 subscription cleanup failed: " + exception.Message);
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
                RecordFailure("PointCloud2 node cleanup failed: " + exception.Message);
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
            var value = string.IsNullOrWhiteSpace(topic) ? "/points" : topic.Trim();
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }

        private string NodeName
            => "u2f_pointcloud2_inbound_"
               + unchecked((uint)GetInstanceID()).ToString(CultureInfo.InvariantCulture);
    }
}
#endif
