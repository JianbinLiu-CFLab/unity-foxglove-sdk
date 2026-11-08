// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;
using ROS2;
using ROS2.Internal;
using Unity.FoxgloveSDK.Schemas.Imu;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [AddComponentMenu("Foxglove/ROS2 For Unity/IMU Inbound")]
    [DefaultExecutionOrder(-430)]
    public sealed class Ros2ForUnityImuInboundBridge : MonoBehaviour
    {
        private const int MaxFramesPerUpdate = 32;

        [SerializeField]
        private string _topic = "/imu/data";

        private ROS2UnityComponent _ros2Unity;
        private ROS2Node _node;
        private ISubscriptionBase _subscription;
        private ImuNativeFrameSlot _slot;
        private readonly ImuInboundLifecycleState _lifecycle = new ImuInboundLifecycleState();
        private bool _cleanupPending;
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
            _lifecycle.BeginGeneration();
            _isStopping = false;
            _runtimeWasReady = false;
            CleanupNative();
            _cleanupPending = _subscription != null || _node != null;
            _slot?.Dispose();
            _slot = new ImuNativeFrameSlot();
            Application.quitting += OnApplicationQuitting;
        }

        private void OnDisable()
        {
            _lifecycle.BeginGeneration();
            _isStopping = true;
            _cleanupPending = true;
            CleanupNative();
            _slot?.Dispose();
            Application.quitting -= OnApplicationQuitting;
        }

        private void OnDestroy()
        {
            _lifecycle.BeginGeneration();
            _isStopping = true;
            _cleanupPending = true;
            CleanupNative();
            Application.quitting -= OnApplicationQuitting;
        }

        private void Update()
        {
            if (IsShuttingDown)
            {
                CleanupNative();
                DrainAvailable();
                return;
            }

            if (_cleanupPending)
            {
                CleanupNative();
                if (_cleanupPending)
                {
                    DrainAvailable();
                    return;
                }
            }

            if (!EnsureRuntime())
            {
                DrainAvailable();
                return;
            }

            EnsureSubscription();
            DrainAvailable();
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
                    throw new InvalidOperationException("R2FU returned no IMU inbound node.");

                if (!Ros2ForUnityNativeQosMapper.TryCreate(
                        FoxRunResolvedQos.SensorData,
                        out var qosProfile).Succeeded)
                    throw new InvalidOperationException("R2FU sensor-data QoS profile could not be created.");

                using (qosProfile)
                {
                    var generation = _lifecycle.CurrentGeneration;
                    var slot = _slot;
                    _subscription = _node.CreateSubscription<Ros2ForUnityGuardedImuMessage>(
                        Topic,
                        message => OnImu(message, generation, slot),
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

        private void OnImu(Ros2ForUnityGuardedImuMessage message, long generation, ImuNativeFrameSlot slot)
        {
            if (!_lifecycle.IsCurrent(generation) || slot == null || !ReferenceEquals(slot, _slot))
                return;
            if (message != null && message.FrameIdRejected)
            {
                slot.RecordRejected("IMU frame_id exceeds 256 bytes before managed copy");
                return;
            }
            if (message == null || message.Header == null || message.Header.Stamp == null)
            {
                slot.RecordRejected("IMU callback returned no header");
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
                slot.RecordRejected("IMU header timestamp exceeds the supported range");
                return;
            }

            if (!TryReadVector(message.Linear_acceleration, out var linear)
                || !TryReadVector(message.Angular_velocity, out var angular))
            {
                slot.RecordRejected("IMU callback returned no acceleration or angular velocity");
                return;
            }

            var hasOrientation = message.Orientation != null
                                 && (message.Orientation_covariance == null
                                     || message.Orientation_covariance.Length == 0
                                     || message.Orientation_covariance[0] >= 0d);
            var orientation = hasOrientation && TryReadQuaternion(message.Orientation, out var parsedOrientation)
                ? parsedOrientation
                : NumericsQuaternion.Identity;
            var hasLinearAcceleration = HasCovariance(message.Linear_acceleration_covariance);
            var hasAngularVelocity = HasCovariance(message.Angular_velocity_covariance);

            var neutral = new ImuInboundMessage(
                unixNs,
                message.Header.Frame_id,
                linear,
                angular,
                orientation,
                hasOrientation,
                hasLinearAcceleration,
                hasAngularVelocity);
            if (!ImuInboundMapper.TryMap(neutral, out var frame, out var error))
            {
                slot.RecordRejected(error);
                return;
            }

            slot.Publish(frame);
        }

        private void DrainAvailable()
        {
            var budget = _slot == null
                ? 0
                : Math.Min(Math.Min(_slot.Count, _slot.Capacity), MaxFramesPerUpdate);
            while (budget-- > 0 && DrainOne())
            {
            }
        }

        private bool DrainOne()
        {
            if (_slot == null || !_slot.TryTake(out var frame))
                return false;

            var handler = FrameReceived;
            if (handler == null)
                return true;

            try
            {
                handler(frame);
            }
            catch (Exception exception)
            {
                _slot.RecordRejected("IMU frame handler failed: " + exception.Message);
                RecordFailure("IMU frame handler failed: " + exception.Message);
            }
            return true;
        }

        private void CleanupNative()
        {
            if (_node == null)
            {
                _cleanupPending = _subscription != null;
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
                    RecordFailure("IMU subscription cleanup failed: " + exception.Message);
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
                _cleanupPending = false;
            }
            catch (Exception exception)
            {
                RecordFailure("IMU node cleanup failed: " + exception.Message);
                _cleanupPending = true;
            }
        }

        private void OnApplicationQuitting()
        {
            _lifecycle.BeginGeneration();
            _isStopping = true;
            _cleanupPending = true;
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

        private static bool HasCovariance(double[] covariance)
            => covariance != null && covariance.Length > 0 && covariance[0] >= 0d;

        private string NodeName
            => "u2f_imu_inbound_" + unchecked((uint)GetInstanceID()).ToString(CultureInfo.InvariantCulture);

        private static string NormalizeTopic(string topic)
        {
            var value = string.IsNullOrWhiteSpace(topic) ? "/imu/data" : topic.Trim();
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }
    }

    public sealed class Ros2ForUnityGuardedImuMessage : sensor_msgs.msg.Imu, MessageInternals
    {
        public const int MaxFrameIdBytes = ImuInboundMapper.MaxFrameIdUtf8Bytes;

        public bool FrameIdRejected { get; private set; }

        public ulong NativeFrameIdBytes { get; private set; }

        public Ros2ForUnityGuardedImuMessage()
            : base()
        {
        }

        void MessageInternals.ReadNativeMessage()
        {
            FrameIdRejected = false;
            NativeFrameIdBytes = 0;

            if (Ros2ForUnityImuNativeMessageGuard.IsSupportedLayout)
            {
                if (!Ros2ForUnityImuNativeMessageGuard.TryReadFrameIdBytes(
                        Handle,
                        out var frameIdBytes)
                    || frameIdBytes > MaxFrameIdBytes)
                {
                    NativeFrameIdBytes = frameIdBytes;
                    FrameIdRejected = true;
                    return;
                }

                NativeFrameIdBytes = frameIdBytes;
            }

            base.ReadNativeMessage(Handle);
        }

        void MessageInternals.WriteNativeMessage()
            => base.WriteNativeMessage(Handle);
    }

    internal static class Ros2ForUnityImuNativeMessageGuard
    {
        private const int FrameIdDataOffset = 8;
        private const int FrameIdSizeOffset = 16;
        private const int FrameIdCapacityOffset = 24;

        internal static bool IsSupportedLayout
            => IntPtr.Size == 8;

        internal static bool TryAdmit(IntPtr imuHandle, int maxFrameIdBytes)
        {
            if (!IsSupportedLayout)
                return true;
            return maxFrameIdBytes >= 0
                   && TryReadFrameIdBytes(imuHandle, out var frameIdBytes)
                   && frameIdBytes <= (ulong)maxFrameIdBytes;
        }

        internal static bool TryReadFrameIdBytes(IntPtr imuHandle, out ulong frameIdBytes)
        {
            frameIdBytes = 0;
            if (!IsSupportedLayout || imuHandle == IntPtr.Zero)
                return false;

            try
            {
                var data = Marshal.ReadIntPtr(imuHandle, FrameIdDataOffset);
                var size = Marshal.ReadInt64(imuHandle, FrameIdSizeOffset);
                var capacity = Marshal.ReadInt64(imuHandle, FrameIdCapacityOffset);
                if (size < 0 || capacity < 0 || size > capacity)
                    return false;
                if (size > 0 && data == IntPtr.Zero)
                    return false;

                frameIdBytes = (ulong)size;
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
#endif
