// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket serverInfo and DataTimestamp DTOs.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Sent immediately after WebSocket connection. Tells Foxglove what this server supports.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ServerInfo
    {
        [JsonProperty("op")]
        public string Op => "serverInfo";

        /// <summary>Display name for the server.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Always serialized, even when empty: "capabilities": [].</summary>
        [JsonProperty("capabilities")]
        public List<Capability> Capabilities { get; set; } = new List<Capability>();

        /// <summary>Omitted from JSON when null or empty.</summary>
        [JsonProperty("supportedEncodings", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> SupportedEncodings { get; set; }

        /// <summary>Omitted from JSON when null or empty.</summary>
        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Metadata { get; set; }

        /// <summary>Session identifier for resuming connections, omitted when null.</summary>
        [JsonProperty("sessionId", NullValueHandling = NullValueHandling.Ignore)]
        public string SessionId { get; set; }

        /// <summary>Earliest data timestamp available, omitted when null.</summary>
        [JsonProperty("dataStartTime", NullValueHandling = NullValueHandling.Ignore)]
        public DataTimestamp DataStartTime { get; set; }

        /// <summary>Latest data timestamp available, omitted when null.</summary>
        [JsonProperty("dataEndTime", NullValueHandling = NullValueHandling.Ignore)]
        public DataTimestamp DataEndTime { get; set; }

        public bool ShouldSerializeSupportedEncodings() => SupportedEncodings?.Count > 0;

        public bool ShouldSerializeMetadata() => Metadata?.Count > 0;
    }

    /// <summary>Timestamp with seconds and nanoseconds components.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class DataTimestamp
    {
        private uint _nsec;

        /// <summary>Whole seconds since epoch.</summary>
        [JsonProperty("sec")]
        public ulong Sec { get; set; }

        /// <summary>Sub-second nanoseconds component. Values above one second are normalized into <see cref="Sec"/>.</summary>
        [JsonProperty("nsec")]
        public uint Nsec
        {
            get => _nsec;
            set
            {
                var carry = value / 1_000_000_000U;
                if (carry != 0)
                {
                    if (Sec > ulong.MaxValue - carry)
                        throw new ArgumentOutOfRangeException(nameof(value), "Nanoseconds overflow timestamp seconds.");

                    Sec += carry;
                }

                _nsec = value % 1_000_000_000U;
            }
        }
    }
}
