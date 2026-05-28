// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket asset fetch DTOs.

using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Client -> Server: fetch an asset by URI.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class FetchAsset
    {
        [JsonProperty("op")]
        public string Op => "fetchAsset";

        [JsonProperty("requestId")]
        public uint RequestId { get; set; }

        [JsonProperty("uri")]
        public string Uri { get; set; }
    }
}
