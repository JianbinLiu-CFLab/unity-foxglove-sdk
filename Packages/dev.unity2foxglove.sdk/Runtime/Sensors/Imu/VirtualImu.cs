// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Imu
// Purpose: Publish body-frame virtual IMU samples from Rigidbody motion.

using System;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Core;
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
        private const float MinDisplayHz = 10f;
        private const int MinQueueSamples = 8;
        private const int MaxQueueSamples = 512;

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

        [Header("Noise (future)")]
        [SerializeField] private bool _enableNoise;
        [SerializeField] private float _accelNoiseStdDev;
        [SerializeField] private float _gyroNoiseStdDev;

        private bool _publishing;
        private int _maxQueuedSamples;
        private Vector3 _lastWorldVelocity;
        private bool _hasLastVelocity;
        private float _originalFixedDeltaTime;
        private bool _didSetFixedDelta;

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

            var specificForceWorld = worldAcceleration - Physics.gravity;
            var toBody = Quaternion.Inverse(_rigidbody.rotation);
            var linearBody = toBody * specificForceWorld;
            var angularBody = toBody * _rigidbody.angularVelocity;

            var sample = new ImuSample(
                FoxgloveTimeUtil.NowUnixTimeNs(),
                CoordinateConverter.UnityToFoxglovePosition(linearBody),
                CoordinateConverter.UnityToFoxglovePosition(angularBody),
                CoordinateConverter.UnityToFoxgloveRotation(_rigidbody.rotation));

            _queue.Enqueue(sample);
            _lastWorldVelocity = worldVelocity;
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

            if (string.IsNullOrWhiteSpace(_topic))
                _topic = DefaultTopic;
            if (string.IsNullOrWhiteSpace(_frameId))
                _frameId = DefaultFrameId;
        }

        private int ComputeMaxQueuedSamples()
        {
            var fixedDelta = Time.fixedDeltaTime;
            if (fixedDelta <= 0f)
                fixedDelta = 1f / MinDisplayHz;

            var physicsHz = 1f / fixedDelta;
            var computed = (int)Math.Ceiling(physicsHz / MinDisplayHz) * 2;
            return Math.Clamp(computed, MinQueueSamples, MaxQueueSamples);
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
