// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket status message DTOs.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Server -> Client: publish a diagnostic status entry to Foxglove Problems.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class StatusMessage
    {
        /// <summary>Protocol operation name.</summary>
        [JsonProperty("op")]
        public string Op => "status";

        /// <summary>Status severity level encoded as the official numeric wire value.</summary>
        [JsonProperty("level")]
        public FoxgloveStatusLevel Level { get; set; }

        /// <summary>Human-readable diagnostic message.</summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>Optional stable identifier used to replace or remove this status later.</summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        /// <summary>Return true when the optional status identifier should be serialized.</summary>
        public bool ShouldSerializeId() => Id != null && Id.Length > 0;
    }

    /// <summary>Server -> Client: remove one or more status entries by identifier.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class RemoveStatusMessage
    {
        /// <summary>Protocol operation name.</summary>
        [JsonProperty("op")]
        public string Op => "removeStatus";

        /// <summary>Status identifiers to remove from Foxglove Problems.</summary>
        [JsonProperty("statusIds")]
        public List<string> StatusIds { get; set; } = new List<string>();
    }
}
