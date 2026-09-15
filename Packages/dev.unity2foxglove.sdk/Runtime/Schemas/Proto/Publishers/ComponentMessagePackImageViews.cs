using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
namespace Unity.FoxgloveSDK.Components
{
    // Internal byte-array projections for direct Component MessagePack output.
    // Public JSON DTOs retain their historical Base64 string representation.
    [FoxgloveSchema("foxglove.CompressedImage")]
    internal sealed class ComponentMessagePackCompressedImageView
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        [JsonProperty("data")] public byte[] Data { get; set; }
        [JsonProperty("format")] public string Format { get; set; }
    }

    [FoxgloveSchema("foxglove.CompressedVideo")]
    internal sealed class ComponentMessagePackCompressedVideoView
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        [JsonProperty("data")] public byte[] Data { get; set; }
        [JsonProperty("format")] public string Format { get; set; }
    }
}
