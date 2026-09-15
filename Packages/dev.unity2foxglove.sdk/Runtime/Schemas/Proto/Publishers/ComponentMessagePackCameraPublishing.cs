#if UNITY_5_3_OR_NEWER
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Schemas;
namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveCameraPublisher
    {
        private bool TryPublishComponentMessagePackImage(byte[] data, ulong unixNs, string frameId, string format)
        {
            if (data == null || data.Length == 0 || EffectiveEncoding != PublisherEffectiveEncoding.MsgPack) return false;
            var view = new ComponentMessagePackCompressedImageView { Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(unixNs), FrameId = frameId, Data = data, Format = format };
            if (!ComponentMessagePackCodecRegistry.TryGet(typeof(ComponentMessagePackCompressedImageView), out var entry) || !entry.IsAvailable) return false;
            PublishMsgPack(entry.Serialize(view), unixNs);
            return true;
        }

        private bool TryPublishComponentMessagePackVideo(byte[] data, ulong unixNs, string frameId, string format)
        {
            if (data == null || data.Length == 0 || EffectiveEncoding != PublisherEffectiveEncoding.MsgPack) return false;
            var view = new ComponentMessagePackCompressedVideoView { Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(unixNs), FrameId = frameId, Data = data, Format = format };
            if (!ComponentMessagePackCodecRegistry.TryGet(typeof(ComponentMessagePackCompressedVideoView), out var entry) || !entry.IsAvailable) return false;
            PublishMsgPack(entry.Serialize(view), unixNs);
            return true;
        }
    }
}
#endif
