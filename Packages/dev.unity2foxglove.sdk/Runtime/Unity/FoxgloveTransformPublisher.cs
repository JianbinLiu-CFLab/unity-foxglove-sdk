using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes this GameObject's transform as foxglove.FrameTransform at a configurable rate.
    /// </summary>
    public class FoxgloveTransformPublisher : FoxglovePublisher<FrameTransformMessage>
    {
        [SerializeField] private string _parentFrameId = "unity_world";
        [SerializeField] private string _childFrameId = "";

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/tf";
        }

        public string ResolvedChildFrameId =>
            SanitizeFrameId(_childFrameId, gameObject.name);

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
