// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity
// Purpose: Publishes this GameObject's transform as foxglove.FrameTransform
// messages at a configurable rate.

using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes this GameObject's transform as foxglove.FrameTransform at a configurable rate.
    /// </summary>
    public class FoxgloveTransformPublisher : FoxglovePublisher<FrameTransformMessage>
    {
        // ── Serialized fields ──
        /// <summary>Parent frame ID, typically <c>"unity_world"</c>.</summary>
        [SerializeField] private string _parentFrameId = "unity_world";
        /// <summary>Child frame ID. Falls back to GameObject name if empty.</summary>
        [SerializeField] private string _childFrameId = "";

        /// <summary>Defaults the topic to <c>/tf</c> if not set.</summary>
        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/tf";
        }

        /// <summary>Resolved child frame ID, using GameObject name as fallback.</summary>
        public string ResolvedChildFrameId =>
            SanitizeFrameId(_childFrameId, gameObject.name);

        /// <summary>
        /// Builds a <c>FrameTransformMessage</c> from the current transform,
        /// applying Foxglove coordinate conversion if in RightHand mode.
        /// </summary>
        protected override FrameTransformMessage CreateMessage()
        {
            var unixNs = CurrentLogTimeNs;
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);

            var pos = Manager?.ActiveCoordinateMode == CoordinateMode.RightHand
                ? CoordinateConverter.UnityToFoxglovePosition(transform.position)
                : transform.position;

            var rot = Manager?.ActiveCoordinateMode == CoordinateMode.RightHand
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
    }
}
