// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket channel advertise/unadvertise DTOs.

using System;
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
        private bool _readOnly;
        private uint _id;
        private string _topic;
        private string _encoding;

        /// <summary>Numeric channel identifier.</summary>
        [JsonProperty("id")]
        public uint Id
        {
            get => _id;
            set
            {
                EnsureWritable();
                _id = value;
            }
        }

        /// <summary>Topic name (e.g. "/imu/data").</summary>
        [JsonProperty("topic")]
        public string Topic
        {
            get => _topic;
            set
            {
                EnsureWritable();
                _topic = value;
            }
        }

        /// <summary>Message encoding (e.g. "protobuf", "json").</summary>
        [JsonProperty("encoding")]
        public string Encoding
        {
            get => _encoding;
            set
            {
                EnsureWritable();
                _encoding = value;
            }
        }

        private string _schemaName = "";
        /// <summary>Always serialized as non-null; null setter is coerced to "".</summary>
        [JsonProperty("schemaName")]
        public string SchemaName
        {
            get => _schemaName;
            set
            {
                EnsureWritable();
                _schemaName = value ?? "";
            }
        }

        private string _schemaEncoding;
        /// <summary>Omitted when null or empty, per official v1 spec.</summary>
        [JsonProperty("schemaEncoding", NullValueHandling = NullValueHandling.Ignore)]
        public string SchemaEncoding
        {
            get => _schemaEncoding;
            set
            {
                EnsureWritable();
                _schemaEncoding = value;
            }
        }

        private string _schema = "";
        /// <summary>Always serialized as non-null; null setter is coerced to "".</summary>
        [JsonProperty("schema")]
        public string Schema
        {
            get => _schema;
            set
            {
                EnsureWritable();
                _schema = value ?? "";
            }
        }

        internal AdvertiseChannel CreateImmutableSnapshot()
        {
            var snapshot = new AdvertiseChannel
            {
                Id = Id,
                Topic = Topic,
                Encoding = Encoding,
                SchemaName = SchemaName,
                SchemaEncoding = SchemaEncoding,
                Schema = Schema
            };
            snapshot._readOnly = true;
            return snapshot;
        }

        private void EnsureWritable()
        {
            if (_readOnly)
            {
                throw new InvalidOperationException(
                    "Registered channel descriptors are immutable; create and register a replacement descriptor.");
            }
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
