#if UNITY_5_3_OR_NEWER
using System.Linq;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxglovePointCloudPublisher
    {
        private bool TryPublishComponentMessagePackRaw(PointCloudFrame frame, ulong unixNs, PointCloudPackedDataBuilder.PointCloudLayout layout)
        {
            if (EffectiveEncoding != PublisherEffectiveEncoding.MsgPack) return false;
            var packed = layout == null ? PointCloudPackedDataBuilder.Build(frame) : PointCloudPackedDataBuilder.Build(frame, layout);
            var view = new ComponentMessagePackPointCloudView
            {
                Timestamp = Unity.FoxgloveSDK.Schemas.FoxgloveTimeUtil.ToFoxgloveTime(unixNs),
                FrameId = frame.FrameId ?? string.Empty,
                Pose = new FoxglovePose { Position = new FoxgloveVector3(), Orientation = new FoxgloveQuaternion { W = 1d } },
                PointStride = packed.PointStride,
                Data = packed.Data
            };
            foreach (var field in packed.Fields)
                view.Fields.Add(new PackedElementFieldMessage { Name = field.Name, Offset = field.Offset, Type = (int)field.Type });
            if (!ComponentMessagePackCodecRegistry.TryGet(typeof(ComponentMessagePackPointCloudView), out var entry) || !entry.IsAvailable) return false;
            PublishMsgPack(entry.Serialize(view), unixNs);
            return true;
        }
    }
}
#endif
