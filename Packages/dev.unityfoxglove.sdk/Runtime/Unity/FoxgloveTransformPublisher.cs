using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes this GameObject's transform as foxglove.FrameTransform at a configurable rate.
    /// </summary>
    public class FoxgloveTransformPublisher : FoxglovePublisherBase
    {
        [SerializeField] private string _parentFrameId = "unity_world";
        [SerializeField] private string _childFrameId = "";

        protected override string SchemaName => "foxglove.FrameTransform";

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/tf";
        }

        /// <summary>Resolved child frame ID (sanitized gameObject name if not set).</summary>
        public string ResolvedChildFrameId =>
            SanitizeFrameId(_childFrameId, gameObject.name);

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;

            var unixNs = FoxgloveTimeUtil.NowUnixTimeNs();
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);
            var pos = transform.position;
            var rot = transform.rotation;

            var msg = new FrameTransformMessage
            {
                Timestamp = time,
                ParentFrameId = _parentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new FoxgloveVector3 { X = pos.x, Y = pos.y, Z = pos.z },
                Rotation = new FoxgloveQuaternion { X = rot.x, Y = rot.y, Z = rot.z, W = rot.w }
            };

            Publish(msg, unixNs);
        }
    }
}
