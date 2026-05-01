using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>foxglove.CompressedImage message.</summary>
    public class CompressedImageMessage
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        /// <summary>Base64-encoded compressed image data.</summary>
        [JsonProperty("data")] public string Data { get; set; }
        /// <summary>Image format: "jpeg", "png", "webp", or "avif".</summary>
        [JsonProperty("format")] public string Format { get; set; }
    }
}
