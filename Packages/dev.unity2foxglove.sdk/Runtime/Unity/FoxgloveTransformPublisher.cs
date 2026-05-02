using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes this GameObject's transform as foxglove.FrameTransform at a configurable rate.
    /// </summary>
    public class FoxgloveTransformPublisher : FoxglovePublisherBase
    {
        public enum CoordinateMode
        {
            /// <summary>Unity raw output: X right, Y up, Z forward (left-handed).</summary>
            UnityRaw,
            /// <summary>Foxglove/ROS convention: X forward, Y left, Z up (right-handed).</summary>
            FoxgloveRos
        }

        [SerializeField] private string _parentFrameId = "unity_world";
        [SerializeField] private string _childFrameId = "";
        [SerializeField] private CoordinateMode _coordinateMode = CoordinateMode.UnityRaw;

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

            double tx, ty, tz;
            double rx, ry, rz, rw;
            switch (_coordinateMode)
            {
                case CoordinateMode.FoxgloveRos:
                    // Unity (X right, Y up, Z fwd LH) → Foxglove/ROS (X fwd, Y left, Z up RH)
                    tx = pos.z; ty = -pos.x; tz = pos.y;
                    rx = -rot.z; ry = rot.x; rz = -rot.y; rw = rot.w;
                    break;
                default:
                    tx = pos.x; ty = pos.y; tz = pos.z;
                    rx = rot.x; ry = rot.y; rz = rot.z; rw = rot.w;
                    break;
            }

            var msg = new FrameTransformMessage
            {
                Timestamp = time,
                ParentFrameId = _parentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new FoxgloveVector3 { X = tx, Y = ty, Z = tz },
                Rotation = new FoxgloveQuaternion { X = rx, Y = ry, Z = rz, W = rw }
            };

            Publish(msg, unixNs);
        }
    }
}
