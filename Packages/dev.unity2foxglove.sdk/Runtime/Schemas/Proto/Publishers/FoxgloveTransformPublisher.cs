// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes this GameObject's transform as foxglove.FrameTransform
// messages at a configurable rate. Supports JSON and protobuf encoding.

using Google.Protobuf;
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

        public override bool SupportsProtobufEncoding => true;
        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => Ros2PublisherSchemaNames.FrameTransform;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/tf";
        }

        public string ResolvedChildFrameId =>
            SanitizeFrameId(_childFrameId, gameObject.name);

        protected override void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;
            if (!ShouldPreparePublishPayload()) return;

            var unixNs = CurrentLogTimeNs;

            if (EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProtobufTransform(unixNs);
            }
            else if (EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                PublishRos2Transform(unixNs);
            }
            else
            {
                var message = CreateMessage(unixNs);
                if (message == null) return;
                Publish(message, unixNs);
            }
        }

        protected override FrameTransformMessage CreateMessage()
            => CreateMessage(CurrentLogTimeNs);

        private FrameTransformMessage CreateMessage(ulong unixNs)
        {
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);

            UVector3 pos = Manager?.ActiveCoordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxglovePosition(transform.position)
                : transform.position;

            UQuaternion rot = Manager?.ActiveCoordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxgloveRotation(transform.rotation)
                : transform.rotation;

            return new FrameTransformMessage
            {
                Timestamp = time,
                ParentFrameId = _parentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new FoxgloveVector3 { X = pos.x, Y = pos.y, Z = pos.z },
                Rotation = new FoxgloveQuaternion { X = rot.x, Y = rot.y, Z = rot.z, W = rot.w }
            };
        }

        private void PublishRos2Transform(ulong unixNs)
        {
            var payload = Ros2CdrFrameTransformBuilder.Serialize(CreateMessage(unixNs));
            PublishRos2(payload, unixNs);
        }

        private void PublishProtobufTransform(ulong unixNs)
        {
            UVector3 pos = Manager?.ActiveCoordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxglovePosition(transform.position)
                : transform.position;

            UQuaternion rot = Manager?.ActiveCoordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxgloveRotation(transform.rotation)
                : transform.rotation;

            var protoFt = new Foxglove.FrameTransform
            {
                Timestamp = new Google.Protobuf.WellKnownTypes.Timestamp
                {
                    Seconds = (long)(unixNs / 1_000_000_000UL),
                    Nanos = (int)(unixNs % 1_000_000_000UL)
                },
                ParentFrameId = _parentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new Foxglove.Vector3 { X = (double)pos.x, Y = (double)pos.y, Z = (double)pos.z },
                Rotation = new Foxglove.Quaternion { X = (double)rot.x, Y = (double)rot.y, Z = (double)rot.z, W = (double)rot.w }
            };

            PublishProto(protoFt.ToByteArray(), unixNs);
        }
    }
}
