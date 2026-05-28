// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket channel advertise/unadvertise DTOs.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Server → Client: notify client about available channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Advertise
    {
        [JsonProperty("op")]
        public string Op => "advertise";

        /// <summary>List of available channels.</summary>
        [JsonProperty("channels")]
        public List<AdvertiseChannel> Channels { get; set; } = new List<AdvertiseChannel>();
    }

    /// <summary>Channel descriptor sent in advertise messages.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class AdvertiseChannel
    {
        /// <summary>Numeric channel identifier.</summary>
        [JsonProperty("id")]
        public uint Id { get; set; }

        /// <summary>Topic name (e.g. "/imu/data").</summary>
        [JsonProperty("topic")]
        public string Topic { get; set; }

        /// <summary>Message encoding (e.g. "protobuf", "json").</summary>
        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        private string _schemaName = "";
        /// <summary>Always serialized as non-null; null setter is coerced to "".</summary>
        [JsonProperty("schemaName")]
        public string SchemaName
        {
            get => _schemaName;
            set => _schemaName = value ?? "";
        }

        /// <summary>Omitted when null or empty, per official v1 spec.</summary>
        [JsonProperty("schemaEncoding", NullValueHandling = NullValueHandling.Ignore)]
        public string SchemaEncoding { get; set; }

        private string _schema = "";
        /// <summary>Always serialized as non-null; null setter is coerced to "".</summary>
        [JsonProperty("schema")]
        public string Schema
        {
            get => _schema;
            set => _schema = value ?? "";
        }
    }

    /// <summary>Server → Client: remove previously advertised channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Unadvertise
    {
        [JsonProperty("op")]
        public string Op => "unadvertise";

        /// <summary>IDs of channels to remove.</summary>
        [JsonProperty("channelIds")]
        public List<uint> ChannelIds { get; set; } = new List<uint>();
    }
}
