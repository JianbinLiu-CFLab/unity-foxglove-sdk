// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes this GameObject's transform as foxglove.FrameTransform
// messages at a configurable rate. Supports JSON and protobuf encoding.

using System;
using Google.Protobuf;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using UVector3 = UnityEngine.Vector3;
using UQuaternion = UnityEngine.Quaternion;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes this GameObject's transform as foxglove.FrameTransform at a configurable rate.
    /// Supports dual encoding: JSON (default) and protobuf.
    /// </summary>
    public class FoxgloveTransformPublisher : FoxglovePublisher<FrameTransformMessage>
    {
        [SerializeField] private string _parentFrameId = "unity_world";
        [SerializeField] private string _childFrameId = "";
        [Tooltip("Publish localPosition/localRotation instead of world transform. Use for a static child link (e.g. base_link -> sensor) under a moving parent.")]
        [SerializeField] private bool _useLocalTransform;
        [Tooltip("Use the manager's physics-based sensor clock for TF timestamps so LiDAR/IMU point-cloud data and transforms share one timeline.")]
        [SerializeField] private bool _useSharedSensorClock = true;

        public override bool SupportsProtobufEncoding => true;
        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => Ros2PublisherSchemaNames.FrameTransform;

        public event Action<FrameTransformMessage> FrameTransformReady;

        private bool _childFrameIdCacheValid;
        private string _cachedChildFrameIdRaw;
        private string _cachedChildFrameIdFallback;
        private string _cachedResolvedChildFrameId;
        private string _cachedGameObjectName;
        private bool _parentFrameIdCacheValid;
        private string _cachedParentFrameIdRaw;
        private string _cachedResolvedParentFrameId;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/tf";
        }

        protected override void OnEnable()
        {
            RefreshGameObjectNameCache();
            InvalidateFrameIdCache();
            base.OnEnable();
        }

        /// <summary>Resolved child frame id used in generated frame transform messages.</summary>
        public string ResolvedChildFrameId =>
            ResolveChildFrameId();

        /// <summary>Resolved parent frame id used in generated frame transform messages.</summary>
        public string ResolvedParentFrameId =>
            ResolveParentFrameId();

        protected override void OnValidate()
        {
            base.OnValidate();
            RefreshGameObjectNameCache();
            InvalidateFrameIdCache();
        }

        protected override void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;
            var nativeHandler = FrameTransformReady;
            var publishNativeFrame = nativeHandler != null;
            if (!ShouldPrepareAnyPublishPayload(
                out var publishWebSocket,
                out var publishBridge,
                out var encodingResolution,
                out var bridgeResolution) && !publishNativeFrame)
                return;

            var unixNs = CurrentTransformTimeNs();
            ResolveTransform(out var pos, out var rot);
            FrameTransformMessage message = null;
            byte[] ros2Payload = null;

            if (publishWebSocket && encodingResolution.Effective == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProtobufTransform(unixNs, encodingResolution, pos, rot);
            }
            else if (publishWebSocket && encodingResolution.Effective == PublisherEffectiveEncoding.Ros2)
            {
                message = CreateMessage(unixNs, pos, rot);
                ros2Payload = Ros2CdrFrameTransformBuilder.Serialize(message);
                PublishRos2(ros2Payload, unixNs, encodingResolution);
            }
            else if (publishWebSocket)
            {
                message = CreateMessage(unixNs, pos, rot);
                Publish(message, unixNs, encodingResolution);
            }

            if (publishBridge)
            {
                message ??= CreateMessage(unixNs, pos, rot);
                ros2Payload ??= Ros2CdrFrameTransformBuilder.Serialize(message);
                PublishRos2Bridge(ros2Payload, unixNs, bridgeResolution);
            }

            if (publishNativeFrame)
                PublishNativeFrame(nativeHandler, message ??= CreateMessage(unixNs, pos, rot));
        }

        protected override FrameTransformMessage CreateMessage()
            => CreateMessage(CurrentTransformTimeNs());

        private ulong CurrentTransformTimeNs()
            => _useSharedSensorClock && _manager != null
                ? _manager.GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)
                : CurrentLogTimeNs;

        private FrameTransformMessage CreateMessage(ulong unixNs)
        {
            ResolveTransform(out var pos, out var rot);
            return CreateMessage(unixNs, pos, rot);
        }

        private FrameTransformMessage CreateMessage(ulong unixNs, UVector3 pos, UQuaternion rot)
        {
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);

            return new FrameTransformMessage
            {
                Timestamp = time,
                ParentFrameId = ResolvedParentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new FoxgloveVector3 { X = pos.x, Y = pos.y, Z = pos.z },
                Rotation = new FoxgloveQuaternion { X = rot.x, Y = rot.y, Z = rot.z, W = rot.w }
            };
        }

        private void PublishProtobufTransform(ulong unixNs, PublisherEncodingResolution resolution, UVector3 pos, UQuaternion rot)
        {
            var protoFt = new Foxglove.FrameTransform
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs),
                ParentFrameId = ResolvedParentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new Foxglove.Vector3 { X = (double)pos.x, Y = (double)pos.y, Z = (double)pos.z },
                Rotation = new Foxglove.Quaternion { X = (double)rot.x, Y = (double)rot.y, Z = (double)rot.z, W = (double)rot.w }
            };

            PublishProto(protoFt.ToByteArray(), unixNs, resolution);
        }

        private string ResolveChildFrameId()
        {
            var fallback = string.IsNullOrEmpty(_cachedGameObjectName)
                ? nameof(FoxgloveTransformPublisher)
                : _cachedGameObjectName;
            if (!_childFrameIdCacheValid
                || !string.Equals(_cachedChildFrameIdRaw, _childFrameId, StringComparison.Ordinal)
                || !string.Equals(_cachedChildFrameIdFallback, fallback, StringComparison.Ordinal))
            {
                _cachedResolvedChildFrameId = SanitizeFrameId(_childFrameId, fallback);
                _cachedChildFrameIdRaw = _childFrameId;
                _cachedChildFrameIdFallback = fallback;
                _childFrameIdCacheValid = true;
            }

            return _cachedResolvedChildFrameId;
        }

        private static void PublishNativeFrame(Action<FrameTransformMessage> nativeHandler, FrameTransformMessage message)
        {
            if (nativeHandler == null)
                return;

            foreach (var subscriber in nativeHandler.GetInvocationList())
            {
                try
                {
                    ((Action<FrameTransformMessage>)subscriber)(message);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        "[Foxglove] Transform native subscriber "
                        + DescribeSubscriber(subscriber)
                        + " failed: "
                        + ex);
                }
            }
        }

        private static string DescribeSubscriber(Delegate subscriber)
        {
            var method = subscriber.Method;
            var declaringType = method.DeclaringType == null ? "(unknown)" : method.DeclaringType.FullName;
            return declaringType + "." + method.Name;
        }

        private string ResolveParentFrameId()
        {
            if (!_parentFrameIdCacheValid
                || !string.Equals(_cachedParentFrameIdRaw, _parentFrameId, StringComparison.Ordinal))
            {
                _cachedResolvedParentFrameId = SanitizeFrameId(_parentFrameId, "unity_world");
                _cachedParentFrameIdRaw = _parentFrameId;
                _parentFrameIdCacheValid = true;
            }

            return _cachedResolvedParentFrameId;
        }

        private void InvalidateFrameIdCache()
        {
            _childFrameIdCacheValid = false;
            _parentFrameIdCacheValid = false;
        }

        private void RefreshGameObjectNameCache()
        {
            _cachedGameObjectName = gameObject == null ? nameof(FoxgloveTransformPublisher) : gameObject.name;
        }

        private void ResolveTransform(out UVector3 position, out UQuaternion rotation)
        {
            var localPos = _useLocalTransform ? transform.localPosition : transform.position;
            var localRot = _useLocalTransform ? transform.localRotation : transform.rotation;

            if (Manager?.ActiveCoordinateMode == CoordinateMode.RightHand)
            {
                position = CoordinateConverter.UnityToFoxglovePosition(localPos);
                rotation = CoordinateConverter.UnityToFoxgloveRotation(localRot);
                return;
            }

            position = localPos;
            rotation = localRot;
        }
    }
}
