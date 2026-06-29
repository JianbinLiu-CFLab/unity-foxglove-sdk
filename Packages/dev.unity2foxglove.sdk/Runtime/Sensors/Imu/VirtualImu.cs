// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Imu
// Purpose: Publish body-frame virtual IMU samples from Rigidbody motion.

using System;
using System.Collections.Generic;
using Foxglove.Schemas;
using Unity.Profiling;
using UnityEngine;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Imu;
using Unity.FoxgloveSDK.Sensors.Imu;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Sample body-frame IMU data each physics tick and publish on display frames.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Foxglove/Sensors/Virtual IMU")]
    public class VirtualImu : MonoBehaviour
    {
        private const string DefaultTopic = "/imu/data";
        private const string DefaultFrameId = "imu_link";
        private const int MaxQueueSamples = 512;
        private const int DefaultTargetRateHz = 200;
        private const int DefaultMaxWebSocketSamplesPerFrame = 32;
        private static readonly double[] DefaultOrientationCovariance = { 0.01, 0, 0, 0, 0.01, 0, 0, 0, 0.01 };
        private static readonly double[] DefaultAngularVelocityCovariance = { 0.02, 0, 0, 0, 0.02, 0, 0, 0, 0.02 };
        private static readonly double[] DefaultLinearAccelerationCovariance = { 0.04, 0, 0, 0, 0.04, 0, 0, 0, 0.04 };
        private static readonly ProfilerMarker PublishMarker = new ProfilerMarker("VirtualImu.Publish");
        private static int _fixedDeltaOverrideUsers;
        private static float _fixedDeltaOverrideOriginal;
        private static float _fixedDeltaOverrideTarget;
        private static int _fixedDeltaOverrideTargetHz;
        private static bool _warnedFixedDeltaOverrideConflict;

        private readonly ImuSampleQueue _queue = new ImuSampleQueue();

        [Header("IMU")]
        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField, Tooltip("Topic for imu data. Default: /imu/data.")] private string _topic = DefaultTopic;
        [SerializeField, Tooltip("Reference frame id for each IMU sample.")] private string _frameId = DefaultFrameId;
        [SerializeField, HideInInspector] private bool _publishImuNative;
        [SerializeField, HideInInspector] private string _imuNativeTopic = DefaultTopic;
        [SerializeField, Tooltip("IMU orientation covariance (9 values, diagonal default).")] private double[] _imuOrientationCovariance = { 0.01, 0, 0, 0, 0.01, 0, 0, 0, 0.01 };
        [SerializeField, Tooltip("IMU angular velocity covariance (9 values, diagonal default).")] private double[] _imuAngularVelocityCovariance = { 0.02, 0, 0, 0, 0.02, 0, 0, 0, 0.02 };
        [SerializeField, Tooltip("IMU linear acceleration covariance (9 values, diagonal default).")] private double[] _imuLinearAccelerationCovariance = { 0.04, 0, 0, 0, 0.04, 0, 0, 0, 0.04 };
        [SerializeField, Tooltip("Include orientation in each IMU message.")] private bool _includeOrientation = true;
        [SerializeField, Min(0), Tooltip(
            "If greater than 0, set Time.fixedDeltaTime globally to 1 / value for higher IMU rate.\n"
            + "This affects all physics in the project.")]
        private int _globalPhysicsRateHzOverride = 0;

        [Header("Rate")]
        [SerializeField] private PublisherRateSource _publishRateSource = PublisherRateSource.OverrideLocal;
        [Tooltip(
            "IMU output rate via sub-step resampling between physics ticks.\n"
            + "0 = one sample per physics tick (138D behavior).\n"
            + "> 0 up-samples/down-samples with interpolation across tick interval.")]
        [SerializeField, Min(0)] private int _targetRateHz = DefaultTargetRateHz;
        [SerializeField, Min(0), Tooltip(
            "Maximum IMU WebSocket visualization catch-up samples published per render frame.\n"
            + "Use at least sample rate / expected lowest FPS. 0 = legacy unlimited draining. Native IMU handoff is never capped.")]
        private int _maxWebSocketSamplesPerFrame = DefaultMaxWebSocketSamplesPerFrame;

        private bool _publishing;
        private int _maxQueuedSamples;
        private Vector3 _lastWorldVelocity;
        private bool _hasLastVelocity;
        private Vector3 _lastBodyAcceleration;
        private Vector3 _lastBodyAngularVelocity;
        private Quaternion _lastBodyRotation;
        private bool _didSetFixedDelta;
        private bool _hasEpoch;
        private ulong _epochUnixNs;
        private double _epochPhysSeconds;
        private long _nextSampleIndex;
        private long _lastReportedDroppedSamples;

        private bool PublishEnabled => _publishing;

        /// <summary>True when the component can provide IMU native frame handoffs.</summary>
        public bool IsImuNativeOutput => isActiveAndEnabled;

        /// <summary>Resolved topic for IMU native DDS adapters.</summary>
        public string ImuNativeTopic
        {
            get
            {
                var topic = string.IsNullOrWhiteSpace(_topic) ? DefaultTopic : _topic.Trim();
                return topic.StartsWith("/", StringComparison.Ordinal) ? topic : "/" + topic;
            }
        }

        /// <summary>Orientation covariance written into IMU messages.</summary>
        public IReadOnlyList<double> ImuOrientationCovariance => _imuOrientationCovariance;

        /// <summary>Angular velocity covariance written into IMU messages.</summary>
        public IReadOnlyList<double> ImuAngularVelocityCovariance => _imuAngularVelocityCovariance;

        /// <summary>Linear acceleration covariance written into IMU messages.</summary>
        public IReadOnlyList<double> ImuLinearAccelerationCovariance => _imuLinearAccelerationCovariance;

        /// <summary>Raised when a native IMU frame is ready for optional DDS adapters.</summary>
        public event Action<ImuNativeFrame> ImuNativeFrameReady;

        private void Start()
        {
            NormalizeSerializedConfiguration();

            if (_rigidbody == null)
                _rigidbody = GetComponent<Rigidbody>();

            if (_rigidbody == null)
            {
                Debug.LogWarning("[VirtualImu] No Rigidbody found on this GameObject. VirtualImu is disabled.");
                enabled = false;
                return;
            }

            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();

            if (_manager == null)
            {
                Debug.LogWarning("[VirtualImu] No FoxgloveManager found. VirtualImu is disabled.");
                enabled = false;
                return;
            }

            if (_globalPhysicsRateHzOverride > 0)
                ApplyGlobalPhysicsRateOverride(_globalPhysicsRateHzOverride);

            _maxQueuedSamples = ComputeMaxQueuedSamples();
            _queue.Resize(_maxQueuedSamples, ImuSampleQueue.MinCapacity);
            _lastReportedDroppedSamples = 0;
            _lastWorldVelocity = _rigidbody.linearVelocity;
            _lastBodyAcceleration = Vector3.zero;
            _lastBodyAngularVelocity = Vector3.zero;
            _lastBodyRotation = _rigidbody.rotation;
            _hasLastVelocity = false;
            _hasEpoch = false;
            _nextSampleIndex = 0;
            _publishing = true;
            EnsureSchemaRegistered();
        }

        private void OnEnable()
        {
            _hasLastVelocity = false;
            _hasEpoch = false;
            _nextSampleIndex = 0;
        }

        private void OnDisable()
        {
            RestoreFixedDeltaTime();
        }

        private void OnDestroy()
        {
            RestoreFixedDeltaTime();
        }

        private void FixedUpdate()
        {
            if (!PublishEnabled)
                return;
            if (_rigidbody == null || Time.fixedDeltaTime <= 0f)
                return;

            var worldVelocity = _rigidbody.linearVelocity;
            if (!_hasLastVelocity)
            {
                _lastWorldVelocity = worldVelocity;
                _hasLastVelocity = true;
                return;
            }

            var worldAcceleration = (worldVelocity - _lastWorldVelocity) / Time.fixedDeltaTime;
            var toBody = Quaternion.Inverse(_rigidbody.rotation);
            var linearBody = toBody * (worldAcceleration - Physics.gravity);
            var angularBody = toBody * _rigidbody.angularVelocity;
            var bodyRotation = _rigidbody.rotation;

            var targetRateHz = ResolveTargetRateHz();
            if (targetRateHz <= 0)
            {
                var sampleTimeNs = _manager == null
                    ? FoxgloveTimeUtil.NowUnixTimeNs()
                    : _manager.GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble);
                _queue.Enqueue(CreateSample(
                    sampleTimeNs,
                    linearBody,
                    angularBody,
                    bodyRotation));
            }
            else
            {
                var tickEndPhysical = Time.fixedTimeAsDouble;
                var initializedEpochThisTick = false;
                if (!_hasEpoch)
                {
                    _epochUnixNs = _manager == null
                        ? FoxgloveTimeUtil.NowUnixTimeNs()
                        : _manager.GetSharedSensorClockUnixTime(tickEndPhysical - Time.fixedDeltaTime);
                    _epochPhysSeconds = tickEndPhysical - Time.fixedDeltaTime;
                    _nextSampleIndex = 0;
                    _hasEpoch = true;
                    initializedEpochThisTick = true;
                }

                var tickStartRel = tickEndPhysical - Time.fixedDeltaTime - _epochPhysSeconds;
                var tickEndRel = tickEndPhysical - _epochPhysSeconds;

                _nextSampleIndex = ImuSubStep.AlignSampleIndexToTickStart(
                    tickStartRel,
                    targetRateHz,
                    _nextSampleIndex);

                while (ImuSubStep.TryGetSampleTime(targetRateHz, _nextSampleIndex, out var sampleRel))
                {
                    if (sampleRel > tickEndRel + 1e-12)
                        break;

                    var phase = (float)Math.Clamp((sampleRel - tickStartRel) / Time.fixedDeltaTime, 0.0, 1.0);
                    var startLinearBody = initializedEpochThisTick ? linearBody : _lastBodyAcceleration;
                    var startAngularBody = initializedEpochThisTick ? angularBody : _lastBodyAngularVelocity;
                    var startBodyRotation = initializedEpochThisTick ? bodyRotation : _lastBodyRotation;
                    // CreateSample applies the Unity->Foxglove coordinate conversion, matching
                    // the targetHz<=0 path. Interpolate in Unity body frame, then convert.
                    _queue.Enqueue(CreateSample(
                        ImuSubStep.SampleTimestampNs(_epochUnixNs, _nextSampleIndex, targetRateHz),
                        Vector3.Lerp(startLinearBody, linearBody, phase),
                        Vector3.Lerp(startAngularBody, angularBody, phase),
                        Quaternion.Slerp(startBodyRotation, bodyRotation, phase)));

                    _nextSampleIndex++;
                }
            }

            _lastWorldVelocity = worldVelocity;
            _lastBodyAcceleration = linearBody;
            _lastBodyAngularVelocity = angularBody;
            _lastBodyRotation = bodyRotation;
        }

        private void Update()
        {
            using (PublishMarker.Auto())
            {
                if (!PublishEnabled || _manager == null || _queue.Count == 0)
                    return;
                if (_manager.Runtime == null)
                    return;

                EnsureSchemaRegistered();

                LogDroppedSamplesIfNeeded();

                var queuedAtFrameStart = _queue.Count;
                var webSocketBudget = ResolveWebSocketSamplesPerFrame(queuedAtFrameStart);
                var webSocketSkipCount = queuedAtFrameStart - webSocketBudget;
                var webSocketPublished = 0;
                var nativeFrameHandler = ImuNativeFrameReady;

                while (_queue.Count > 0)
                {
                    var sample = _queue.Dequeue();
                    ImuNativeFrame nativeFrame = null;
                    if (nativeFrameHandler != null)
                    {
                        nativeFrame = CreateNativeFrame(
                            sample.TimestampNs,
                            _frameId,
                            sample.LinearAcceleration,
                            sample.AngularVelocity,
                            sample.Orientation,
                            _includeOrientation);
                    }

                    if (webSocketSkipCount > 0)
                    {
                        webSocketSkipCount--;
                    }
                    else if (webSocketPublished < webSocketBudget)
                    {
                        PublishWebSocketSample(sample);
                        webSocketPublished++;
                    }

                    if (nativeFrame != null)
                        nativeFrameHandler.Invoke(nativeFrame);
                }
            }
        }

        private void OnValidate()
        {
            NormalizeSerializedConfiguration();
        }

        private void NormalizeSerializedConfiguration()
        {
            if (_globalPhysicsRateHzOverride < 0)
                _globalPhysicsRateHzOverride = 0;
            if (_targetRateHz < 0)
                _targetRateHz = 0;
            if (_maxWebSocketSamplesPerFrame < 0)
                _maxWebSocketSamplesPerFrame = 0;
            if (string.IsNullOrWhiteSpace(_topic))
                _topic = DefaultTopic;
            if (string.IsNullOrWhiteSpace(_frameId))
                _frameId = DefaultFrameId;
            if (string.IsNullOrWhiteSpace(_imuNativeTopic))
                _imuNativeTopic = DefaultTopic;

            _imuOrientationCovariance = NormalizeCovariance(_imuOrientationCovariance, DefaultOrientationCovariance);
            _imuAngularVelocityCovariance = NormalizeCovariance(_imuAngularVelocityCovariance, DefaultAngularVelocityCovariance);
            _imuLinearAccelerationCovariance = NormalizeCovariance(_imuLinearAccelerationCovariance, DefaultLinearAccelerationCovariance);
        }

        private int ComputeMaxQueuedSamples()
        {
            return ImuSubStep.ComputeQueueCapacity(ResolveTargetRateHz(), ImuSampleQueue.MinCapacity, MaxQueueSamples);
        }

        private int ResolveWebSocketSamplesPerFrame(int queuedAtFrameStart)
        {
            if (_maxWebSocketSamplesPerFrame <= 0)
                return queuedAtFrameStart;

            return Math.Min(_maxWebSocketSamplesPerFrame, queuedAtFrameStart);
        }

        private void PublishWebSocketSample(ImuSample sample)
        {
            var bytes = ImuMessageBuilder.Serialize(
                sample.TimestampNs,
                _frameId,
                sample.LinearAcceleration,
                sample.AngularVelocity,
                sample.Orientation,
                _includeOrientation,
                ImuOrientationCovariance,
                ImuAngularVelocityCovariance,
                ImuLinearAccelerationCovariance);

            _manager.PublishProto(_topic, ImuSchema.SchemaName, bytes, sample.TimestampNs);
        }

        private int ResolveTargetRateHz()
        {
            if (_publishRateSource != PublisherRateSource.UseManagerDefault)
                return _targetRateHz;

            if (_manager == null)
                return _targetRateHz;

            return Math.Max(0, (int)Math.Round(_manager.DefaultPublishRateHz));
        }

        private void ApplyGlobalPhysicsRateOverride(int targetHz)
        {
            var target = 1f / targetHz;
            if (target <= 0f)
                return;

            if (_fixedDeltaOverrideUsers == 0)
            {
                _fixedDeltaOverrideOriginal = Time.fixedDeltaTime;
                Time.fixedDeltaTime = target;
                _fixedDeltaOverrideTarget = target;
                _fixedDeltaOverrideTargetHz = targetHz;
            }
            else if (Math.Abs(_fixedDeltaOverrideTarget - target) > float.Epsilon
                     && !_warnedFixedDeltaOverrideConflict)
            {
                Debug.LogWarning(
                    $"[VirtualImu] Global physics rate override is already active at {_fixedDeltaOverrideTargetHz} Hz; ignoring conflicting request for {targetHz} Hz on {name}.",
                    this);
                _warnedFixedDeltaOverrideConflict = true;
            }

            _fixedDeltaOverrideUsers++;
            _didSetFixedDelta = true;
        }

        private void RestoreFixedDeltaTime()
        {
            if (!_didSetFixedDelta)
                return;

            if (_fixedDeltaOverrideUsers > 0)
                _fixedDeltaOverrideUsers--;

            if (_fixedDeltaOverrideUsers == 0
                && Math.Abs(Time.fixedDeltaTime - _fixedDeltaOverrideOriginal) > float.Epsilon)
            {
                Time.fixedDeltaTime = _fixedDeltaOverrideOriginal;
            }

            if (_fixedDeltaOverrideUsers == 0)
            {
                _fixedDeltaOverrideTarget = 0f;
                _fixedDeltaOverrideTargetHz = 0;
                _warnedFixedDeltaOverrideConflict = false;
            }

            _didSetFixedDelta = false;
        }

        private void LogDroppedSamplesIfNeeded()
        {
            var dropped = _queue.DroppedCount;
            if (dropped <= _lastReportedDroppedSamples)
                return;

            Debug.LogWarning(
                $"[VirtualImu] IMU sample queue dropped {dropped - _lastReportedDroppedSamples} sample(s) under back-pressure; total dropped={dropped}.",
                this);
            _lastReportedDroppedSamples = dropped;
        }

        private void EnsureSchemaRegistered()
        {
            if (!_publishing)
                return;

            var schemas = _manager == null || _manager.Runtime == null ? null : _manager.Runtime.Schemas;
            if (schemas == null)
                return;

            // Idempotent against the live registry: re-registers automatically if the
            // runtime (and its schema registry) is recreated, unlike a global flag.
            if (schemas.TryGetSchema(ImuSchema.SchemaName, out _))
                return;

            ProtobufSchemaRegistryLoader.FromBytes(ImuSchema.FileDescriptorSetData, schemas).RegisterAll();
        }

        private static ImuSample CreateSample(
            ulong timestampNs,
            Vector3 linearBody,
            Vector3 angularBody,
            Quaternion rotation)
        {
            return new ImuSample(
                timestampNs,
                CoordinateConverter.UnityToFoxglovePosition(linearBody),
                CoordinateConverter.UnityToFoxgloveAngularVelocity(angularBody),
                CoordinateConverter.UnityToFoxgloveRotation(rotation));
        }

        private static ImuNativeFrame CreateNativeFrame(
            ulong timestampNs,
            string frameId,
            Vector3 linearAcceleration,
            Vector3 angularVelocity,
            Quaternion orientation,
            bool hasOrientation)
        {
            return new ImuNativeFrame(
                timestampNs,
                frameId,
                ToNumerics(linearAcceleration),
                ToNumerics(angularVelocity),
                ToNumerics(orientation),
                hasOrientation);
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 value)
            => new System.Numerics.Vector3(value.x, value.y, value.z);

        private static System.Numerics.Quaternion ToNumerics(Quaternion value)
            => new System.Numerics.Quaternion(value.x, value.y, value.z, value.w);

        private static double[] NormalizeCovariance(double[] values, double[] fallback)
        {
            if (values == null || values.Length != 9)
                return (double[])fallback.Clone();

            var normalized = (double[])values.Clone();
            for (var i = 0; i < normalized.Length; i++)
            {
                if (normalized[i] < 0d || double.IsNaN(normalized[i]) || double.IsInfinity(normalized[i]))
                    normalized[i] = 0d;
            }

            return normalized;
        }

    }
}
