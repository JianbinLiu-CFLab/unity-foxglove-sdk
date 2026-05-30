// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Imu
// Purpose: Publish body-frame virtual IMU samples from Rigidbody motion.

using System;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Sensors.Imu;
using Unity.FoxgloveSDK.Schemas;

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
        private const int MinQueueSamples = 8;
        private const int MaxQueueSamples = 512;
        private const int DefaultTargetRateHz = 200;

        private readonly ImuSampleQueue _queue = new ImuSampleQueue();

        [Header("IMU")]
        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField, Tooltip("Topic for imu data. Default: /imu/data.")] private string _topic = DefaultTopic;
        [SerializeField, Tooltip("Reference frame id for each IMU sample.")] private string _frameId = DefaultFrameId;
        [SerializeField, Tooltip("Enable streaming as soon as this component starts.")] private bool _publishOnStart = true;
        [SerializeField, Tooltip("Include orientation in each IMU message.")] private bool _includeOrientation = true;
        [SerializeField, Min(0), Tooltip(
            "If greater than 0, set Time.fixedDeltaTime globally to 1 / value for higher IMU rate.\n"
            + "This affects all physics in the project.")]
        private int _globalPhysicsRateHzOverride = 0;

        [Header("Rate")]
        [Tooltip(
            "IMU output rate via sub-step resampling between physics ticks.\n"
            + "0 = one sample per physics tick (138D behavior).\n"
            + "> 0 up-samples/down-samples with interpolation across tick interval.")]
        [SerializeField, Min(0)] private int _targetRateHz = DefaultTargetRateHz;

        [Header("Noise (future)")]
        [SerializeField] private bool _enableNoise;
        [SerializeField] private float _accelNoiseStdDev;
        [SerializeField] private float _gyroNoiseStdDev;

        private bool _publishing;
        private int _maxQueuedSamples;
        private Vector3 _lastWorldVelocity;
        private bool _hasLastVelocity;
        private Vector3 _lastBodyAcceleration;
        private Vector3 _lastBodyAngularVelocity;
        private Quaternion _lastBodyRotation;
        private float _originalFixedDeltaTime;
        private bool _didSetFixedDelta;
        private bool _hasEpoch;
        private ulong _epochUnixNs;
        private double _epochPhysSeconds;
        private long _nextSampleIndex;

        private bool PublishEnabled => _publishOnStart && _publishing;

        private void Start()
        {
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
            _queue.Resize(_maxQueuedSamples);
            _lastWorldVelocity = _rigidbody.linearVelocity;
            _lastBodyAcceleration = Vector3.zero;
            _lastBodyAngularVelocity = Vector3.zero;
            _lastBodyRotation = _rigidbody.rotation;
            _hasLastVelocity = false;
            _hasEpoch = false;
            _nextSampleIndex = 0;
            _publishing = _publishOnStart;

            if (_publishing)
                EnsureSchemaRegistered();
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

            if (_targetRateHz <= 0)
            {
                _queue.Enqueue(CreateSample(
                    FoxgloveTimeUtil.NowUnixTimeNs(),
                    linearBody,
                    angularBody,
                    bodyRotation));
            }
            else
            {
                var tickEndPhysical = Time.fixedTimeAsDouble;
                if (!_hasEpoch)
                {
                    _epochUnixNs = FoxgloveTimeUtil.NowUnixTimeNs();
                    _epochPhysSeconds = tickEndPhysical - Time.fixedDeltaTime;
                    _nextSampleIndex = 0;
                    _hasEpoch = true;
                }

                var tickStartRel = tickEndPhysical - Time.fixedDeltaTime - _epochPhysSeconds;
                var tickEndRel = tickEndPhysical - _epochPhysSeconds;

                _nextSampleIndex = ImuSubStep.AlignSampleIndexToTickStart(
                    tickStartRel,
                    _targetRateHz,
                    _nextSampleIndex);

                while (ImuSubStep.TryGetSampleTime(_targetRateHz, _nextSampleIndex, out var sampleRel))
                {
                    if (sampleRel > tickEndRel + 1e-12)
                        break;

                    var phase = (float)Math.Clamp((sampleRel - tickStartRel) / Time.fixedDeltaTime, 0.0, 1.0);
                    // CreateSample applies the Unity->Foxglove coordinate conversion, matching
                    // the targetHz<=0 path. Interpolate in Unity body frame, then convert.
                    _queue.Enqueue(CreateSample(
                        ImuSubStep.SampleTimestampNs(_epochUnixNs, _nextSampleIndex, _targetRateHz),
                        Vector3.Lerp(_lastBodyAcceleration, linearBody, phase),
                        Vector3.Lerp(_lastBodyAngularVelocity, angularBody, phase),
                        Quaternion.Slerp(_lastBodyRotation, bodyRotation, phase)));

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
            if (!PublishEnabled || _manager == null || _queue.Count == 0)
                return;
            if (_manager.Runtime == null)
                return;

            EnsureSchemaRegistered();

            if (_queue.Count == 0)
                return;

            while (_queue.Count > 0)
            {
                var sample = _queue.Dequeue();
                var bytes = ImuMessageBuilder.Serialize(
                    sample.TimestampNs,
                    _frameId,
                    sample.LinearAcceleration,
                    sample.AngularVelocity,
                    sample.Orientation,
                    _includeOrientation);

                _manager.PublishProto(_topic, ImuSchema.SchemaName, bytes, sample.TimestampNs);
            }
        }

        private void OnValidate()
        {
            if (_globalPhysicsRateHzOverride < 0)
                _globalPhysicsRateHzOverride = 0;
            if (_targetRateHz < 0)
                _targetRateHz = 0;

            if (string.IsNullOrWhiteSpace(_topic))
                _topic = DefaultTopic;
            if (string.IsNullOrWhiteSpace(_frameId))
                _frameId = DefaultFrameId;
        }

        private int ComputeMaxQueuedSamples()
        {
            return ImuSubStep.ComputeQueueCapacity(_targetRateHz, MinQueueSamples, MaxQueueSamples);
        }

        private void ApplyGlobalPhysicsRateOverride(int targetHz)
        {
            var target = 1f / targetHz;
            if (target <= 0f)
                return;

            _originalFixedDeltaTime = Time.fixedDeltaTime;
            Time.fixedDeltaTime = target;
            _didSetFixedDelta = true;
        }

        private void RestoreFixedDeltaTime()
        {
            if (!_didSetFixedDelta)
                return;

            if (Math.Abs(Time.fixedDeltaTime - _originalFixedDeltaTime) > float.Epsilon)
                Time.fixedDeltaTime = _originalFixedDeltaTime;

            _didSetFixedDelta = false;
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
                CoordinateConverter.UnityToFoxglovePosition(angularBody),
                CoordinateConverter.UnityToFoxgloveRotation(rotation));
        }

        private readonly struct ImuSample
        {
            public ImuSample(ulong timestampNs, Vector3 linearAcceleration, Vector3 angularVelocity, Quaternion orientation)
            {
                TimestampNs = timestampNs;
                LinearAcceleration = linearAcceleration;
                AngularVelocity = angularVelocity;
                Orientation = orientation;
            }

            public ulong TimestampNs { get; }
            public Vector3 LinearAcceleration { get; }
            public Vector3 AngularVelocity { get; }
            public Quaternion Orientation { get; }
        }

        /// <summary>
        /// Bounded sample queue that drops oldest samples under back-pressure.
        /// </summary>
        private sealed class ImuSampleQueue
        {
            private ImuSample[] _items = new ImuSample[MinQueueSamples];
            private int _head;
            private int _count;

            public int Count => _count;

            public void Resize(int capacity)
            {
                if (capacity <= 0)
                    capacity = MinQueueSamples;
                if (_items.Length == capacity)
                    return;

                var next = new ImuSample[capacity];
                var copyCount = Math.Min(_count, capacity);
                for (var i = 0; i < copyCount; i++)
                {
                    next[i] = Dequeue();
                }

                _items = next;
                _count = copyCount;
                _head = 0;
            }

            public void Enqueue(ImuSample sample)
            {
                if (_count < _items.Length)
                {
                    var tail = (_head + _count) % _items.Length;
                    _items[tail] = sample;
                    _count++;
                    return;
                }

                _items[_head] = sample;
                _head = (_head + 1) % _items.Length;
            }

            public ImuSample Dequeue()
            {
                if (_count == 0)
                    return default;

                var index = _head;
                var sample = _items[index];
                _head = (_head + 1) % _items.Length;
                _count--;
                return sample;
            }
        }
    }
}
