// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;
using ROS2;
using Unity.FoxgloveSDK.Schemas.Imu;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [AddComponentMenu("Foxglove/ROS2 For Unity/IMU Inbound")]
    [DefaultExecutionOrder(-430)]
    public sealed class Ros2ForUnityImuInboundBridge : MonoBehaviour
    {
        [SerializeField]
        private string _topic = "/imu/data";

        private ROS2UnityComponent _ros2Unity;
        private ROS2Node _node;
        private ISubscriptionBase _subscription;
        private ImuNativeFrameSlot _slot;
        private bool _isStopping;
        private bool _runtimeWasReady;
        private bool _warnedRuntime;
        private int _failureCount;

        public event Action<ImuNativeFrame> FrameReceived;

        public string Topic
        {
            get => NormalizeTopic(_topic);
            set => _topic = NormalizeTopic(value);
        }

        public ImuNativeFrameSlotStats Stats
            => _slot == null ? default : _slot.Stats;

        private bool IsShuttingDown
            => _isStopping
               || Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene);

        private void OnEnable()
        {
            _isStopping = false;
            _runtimeWasReady = false;
            _slot?.Dispose();
            _slot = new ImuNativeFrameSlot();
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
            if (_subscription != null)
                return;
            if (_node != null)
                CleanupNative();
            if (_ros2Unity == null || IsShuttingDown)
                return;

            try
            {
                _node = _ros2Unity.CreateNode("u2f_imu_inbound");
                if (_node == null)
                    throw new InvalidOperationException("R2FU returned no IMU inbound node.");

                if (!Ros2ForUnityNativeQosMapper.TryCreate(
                        FoxRunResolvedQos.SensorData,
                        out var qosProfile).Succeeded)
                    throw new InvalidOperationException("R2FU sensor-data QoS profile could not be created.");

                using (qosProfile)
                {
                    _subscription = _node.CreateSubscription<sensor_msgs.msg.Imu>(
                        Topic,
                        OnImu,
                        qosProfile.NativeProfile);
                }

                if (_subscription == null)
                    throw new InvalidOperationException("R2FU returned no IMU inbound subscription.");
                _failureCount = 0;
            }
            catch (Exception exception)
            {
                CleanupNative();
                RecordFailure("IMU subscription setup failed: " + exception.Message);
            }
        }

        private void OnImu(sensor_msgs.msg.Imu message)
        {
            if (message == null || message.Header == null || message.Header.Stamp == null)
            {
                _slot.RecordRejected("IMU callback returned no header");
                return;
            }

            var stamp = message.Header.Stamp;
            ulong unixNs;
            try
            {
                unixNs = stamp.Sec < 0
                    ? 0UL
                    : checked((ulong)stamp.Sec * 1_000_000_000UL + stamp.Nanosec);
            }
            catch (OverflowException)
            {
                _slot.RecordRejected("IMU header timestamp exceeds the supported range");
                return;
            }

            if (!TryReadVector(message.Linear_acceleration, out var linear)
                || !TryReadVector(message.Angular_velocity, out var angular))
            {
                _slot.RecordRejected("IMU callback returned no acceleration or angular velocity");
                return;
            }

            var hasOrientation = message.Orientation != null
                                 && (message.Orientation_covariance == null
                                     || message.Orientation_covariance.Length == 0
                                     || message.Orientation_covariance[0] >= 0d);
            var orientation = hasOrientation && TryReadQuaternion(message.Orientation, out var parsedOrientation)
                ? parsedOrientation
                : NumericsQuaternion.Identity;

            var neutral = new ImuInboundMessage(
                unixNs,
                message.Header.Frame_id,
                linear,
                angular,
                orientation,
                hasOrientation);
            if (!ImuInboundMapper.TryMap(neutral, out var frame, out var error))
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
                _slot.RecordRejected("IMU frame handler failed: " + exception.Message);
                RecordFailure("IMU frame handler failed: " + exception.Message);
            }
        }

        private void CleanupNative()
        {
            if (_node == null)
            {
                _subscription = null;
                return;
            }

            if (_subscription != null)
            {
                try
                {
                    if (!_node.RemoveSubscription(_subscription))
                        return;
                    _subscription = null;
                }
                catch (Exception exception)
                {
                    RecordFailure("IMU subscription cleanup failed: " + exception.Message);
                    return;
                }
            }

            if (_ros2Unity == null)
                return;
            try
            {
                if (!_ros2Unity.TryRemoveNode(_node))
                    return;
                _node = null;
            }
            catch (Exception exception)
            {
                RecordFailure("IMU node cleanup failed: " + exception.Message);
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

        private static bool TryReadVector(geometry_msgs.msg.Vector3 value, out NumericsVector3 result)
        {
            result = default;
            if (value == null)
                return false;
            result = new NumericsVector3((float)value.X, (float)value.Y, (float)value.Z);
            return true;
        }

        private static bool TryReadQuaternion(geometry_msgs.msg.Quaternion value, out NumericsQuaternion result)
        {
            result = default;
            if (value == null)
                return false;
            result = new NumericsQuaternion((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);
            return true;
        }

        private static string NormalizeTopic(string topic)
        {
            var value = string.IsNullOrWhiteSpace(topic) ? "/imu/data" : topic.Trim();
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }
    }
}
#endif
