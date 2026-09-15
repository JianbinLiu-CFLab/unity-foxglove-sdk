using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
namespace Unity.FoxgloveSDK.Components
{
    // Internal direct-byte projections for PointCloud MessagePack publication.
    [FoxgloveSchema("foxglove.PointCloud")]
    internal sealed class ComponentMessagePackPointCloudView
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        [JsonProperty("point_stride")] public uint PointStride { get; set; }
        [JsonProperty("fields")] public List<PackedElementFieldMessage> Fields { get; set; } = new List<PackedElementFieldMessage>();
        [JsonProperty("data")] public byte[] Data { get; set; }
    }

    [FoxgloveSchema("foxglove.CompressedPointCloud")]
    internal sealed class ComponentMessagePackCompressedPointCloudView
    {
        [JsonProperty("timestamp")] public FoxgloveTime Timestamp { get; set; }
        [JsonProperty("frame_id")] public string FrameId { get; set; }
        [JsonProperty("pose")] public FoxglovePose Pose { get; set; }
        [JsonProperty("data")] public byte[] Data { get; set; }
        [JsonProperty("format")] public string Format { get; set; }
    }
}
