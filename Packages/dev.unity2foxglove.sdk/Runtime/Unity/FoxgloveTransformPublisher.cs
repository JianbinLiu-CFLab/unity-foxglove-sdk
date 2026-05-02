using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes this GameObject's transform as foxglove.FrameTransform at a configurable rate.
    /// </summary>
    public class FoxgloveTransformPublisher : FoxglovePublisher<FrameTransformMessage>
    {
        public enum CoordinateMode
        {
            UnityRaw,
            FoxgloveRos
        }

        [SerializeField] private string _parentFrameId = "unity_world";
        [SerializeField] private string _childFrameId = "";
        [SerializeField] private CoordinateMode _coordinateMode = CoordinateMode.UnityRaw;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/tf";
        }

        public string ResolvedChildFrameId =>
            SanitizeFrameId(_childFrameId, gameObject.name);

        protected override FrameTransformMessage CreateMessage()
        {
            var unixNs = FoxgloveTimeUtil.NowUnixTimeNs();
            var time = FoxgloveTimeUtil.ToFoxgloveTime(unixNs);
            var pos = transform.position;
            var rot = transform.rotation;

            double tx, ty, tz;
            double rx, ry, rz, rw;
            switch (_coordinateMode)
            {
                case CoordinateMode.FoxgloveRos:
                    tx = pos.z; ty = -pos.x; tz = pos.y;
                    rx = -rot.z; ry = rot.x; rz = -rot.y; rw = rot.w;
                    break;
                default:
                    tx = pos.x; ty = pos.y; tz = pos.z;
                    rx = rot.x; ry = rot.y; rz = rot.z; rw = rot.w;
                    break;
            }

            return new FrameTransformMessage
            {
                Timestamp = time,
                ParentFrameId = _parentFrameId,
                ChildFrameId = ResolvedChildFrameId,
                Translation = new FoxgloveVector3 { X = tx, Y = ty, Z = tz },
                Rotation = new FoxgloveQuaternion { X = rx, Y = ry, Z = rz, W = rw }
            };
        }
    }
}
