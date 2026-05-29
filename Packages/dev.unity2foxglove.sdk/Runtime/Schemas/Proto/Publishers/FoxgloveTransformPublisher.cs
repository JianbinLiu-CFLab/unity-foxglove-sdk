// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes this GameObject's transform as foxglove.FrameTransform
// messages at a configurable rate. Supports JSON and protobuf encoding.

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

        public override bool SupportsProtobufEncoding => true;
        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => Ros2PublisherSchemaNames.FrameTransform;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/tf";
        }

        public string ResolvedChildFrameId =>
            SanitizeFrameId(_childFrameId, gameObject.name);

        public string ResolvedParentFrameId =>
            SanitizeFrameId(_parentFrameId, "unity_world");

        protected override void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;
            if (!ShouldPrepareAnyPublishPayload()) return;

            var unixNs = CurrentLogTimeNs;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            var message = CreateMessage(unixNs);
            if (message == null) return;
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProtobufTransform(unixNs);
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrFrameTransformBuilder.Serialize(message);
                PublishRos2(ros2Payload, unixNs);
            }
            else if (publishWebSocket)
            {
                Publish(message, unixNs);
            }

            if (publishBridge)
            {
                ros2Payload ??= Ros2CdrFrameTransformBuilder.Serialize(message);
                PublishRos2Bridge(ros2Payload, unixNs);
            }
        }

        protected override FrameTransformMessage CreateMessage()
            => CreateMessage(CurrentLogTimeNs);

        private FrameTransformMessage CreateMessage(ulong unixNs)
        {
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);

            ResolveTransform(out var pos, out var rot);

            return new FrameTransformMessage
            {
                Timestamp = time,
                ParentFrameId = ResolvedParentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new FoxgloveVector3 { X = pos.x, Y = pos.y, Z = pos.z },
                Rotation = new FoxgloveQuaternion { X = rot.x, Y = rot.y, Z = rot.z, W = rot.w }
            };
        }

        private void PublishProtobufTransform(ulong unixNs)
        {
            ResolveTransform(out var pos, out var rot);

            var protoFt = new Foxglove.FrameTransform
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs),
                ParentFrameId = ResolvedParentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new Foxglove.Vector3 { X = (double)pos.x, Y = (double)pos.y, Z = (double)pos.z },
                Rotation = new Foxglove.Quaternion { X = (double)rot.x, Y = (double)rot.y, Z = (double)rot.z, W = (double)rot.w }
            };

            PublishProto(protoFt.ToByteArray(), unixNs);
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
