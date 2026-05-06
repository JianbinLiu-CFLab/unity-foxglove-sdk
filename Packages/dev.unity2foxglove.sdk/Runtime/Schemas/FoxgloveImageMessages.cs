// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas
// Purpose: foxglove.CompressedImage DTO for camera frame publishing.

using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>foxglove.CompressedImage message.</summary>
    [Unity.FoxgloveSDK.Protocol.FoxgloveSchema("foxglove.CompressedImage")]
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
