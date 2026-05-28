// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket service DTOs.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Server → Client: advertise available services.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class AdvertiseServices
    {
        [JsonProperty("op")]
        public string Op => "advertiseServices";

        [JsonProperty("services")]
        public List<ServiceDescriptor> Services { get; set; } = new List<ServiceDescriptor>();
    }

    /// <summary>Server → Client: remove previously advertised services.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class UnadvertiseServices
    {
        [JsonProperty("op")]
        public string Op => "unadvertiseServices";

        [JsonProperty("serviceIds")]
        public List<uint> ServiceIds { get; set; } = new List<uint>();
    }

    /// <summary>Service descriptor sent in advertiseServices.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ServiceDescriptor
    {
        /// <summary>Numeric service identifier.</summary>
        [JsonProperty("id")]
        public uint Id { get; set; }

        /// <summary>Service name (e.g. "/get_map").</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Service type (e.g. "GetMap").</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>Request schema descriptor.</summary>
        [JsonProperty("request")]
        public ServiceSchemaDescriptor Request { get; set; }

        /// <summary>Response schema descriptor.</summary>
        [JsonProperty("response")]
        public ServiceSchemaDescriptor Response { get; set; }
    }

    /// <summary>Schema descriptor for a service request or response.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ServiceSchemaDescriptor
    {
        /// <summary>Encoding type (e.g. "jsonschema", "protobuf"), omit if unknown.</summary>
        [JsonProperty("encoding", NullValueHandling = NullValueHandling.Ignore)]
        public string Encoding { get; set; }

        /// <summary>Schema name (e.g. "foxglove.GetMapRequest").</summary>
        [JsonProperty("schemaName")]
        public string SchemaName { get; set; }

        /// <summary>Omitted from JSON when null or empty.</summary>
        [JsonProperty("schema", NullValueHandling = NullValueHandling.Ignore)]
        public string Schema { get; set; }
    }

    /// <summary>Server → Client: service call failed.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ServiceCallFailure
    {
        [JsonProperty("op")]
        public string Op => "serviceCallFailure";

        /// <summary>Service that failed.</summary>
        [JsonProperty("serviceId")]
        public uint ServiceId { get; set; }

        /// <summary>Client-assigned call ID that failed.</summary>
        [JsonProperty("callId")]
        public uint CallId { get; set; }

        /// <summary>Human-readable failure reason.</summary>
        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
