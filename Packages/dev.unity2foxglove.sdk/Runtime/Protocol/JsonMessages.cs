// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol
// Purpose: Foxglove WebSocket JSON message DTOs — serverInfo, advertise,
// subscribe, parameters, services, connection graph, and protocol constants.

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Foxglove WebSocket subprotocol identifiers.</summary>
    public static class Subprotocol
    {
        /// <summary>Used by foxglove-sdk (Rust, Python, C++).</summary>
        public const string SdkV1 = "foxglove.sdk.v1";

        /// <summary>Used by Foxglove Desktop and foxglove_bridge.</summary>
        public const string WebSocketV1 = "foxglove.websocket.v1";

        /// <summary>All accepted subprotocols for matching logic.</summary>
        public static readonly string[] Accepted = { SdkV1, WebSocketV1 };
    }

    /// <summary>Capabilities advertised in serverInfo. Only declare what is actually supported.</summary>
    [JsonConverter(typeof(FoxgloveEnumConverter))]
    public enum Capability
    {
        Time,
        Parameters,
        ParametersSubscribe,
        Services,
        ConnectionGraph,
        Assets,
        ClientPublish,
        PlaybackControl
    }

    /// <summary>
    /// CamelCase enum converter for Foxglove protocol.
    /// Newtonsoft.Json's StringEnumConverter with CamelCaseNamingStrategy.
    /// Wire format: "time", "clientPublish", "parametersSubscribe", etc.
    /// </summary>
    public class FoxgloveEnumConverter : StringEnumConverter
    {
        /// <summary>
        /// Default instance. Used when configuring JsonSerializerSettings globally.
        /// </summary>
        public static readonly FoxgloveEnumConverter Instance = new FoxgloveEnumConverter();

        /// <summary>Create a converter that serializes enum values as camelCase strings.</summary>
        public FoxgloveEnumConverter()
            : base(new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy()) { }
    }

    // ── Server → Client messages ──

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

    /// <summary>Server → Client: update parameter values.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ParameterValues
    {
        [JsonProperty("op")]
        public string Op => "parameterValues";

        /// <summary>List of parameter name-value pairs.</summary>
        [JsonProperty("parameters")]
        public List<Parameter> Parameters { get; set; } = new List<Parameter>();

        /// <summary>Request correlation ID, omitted when null.</summary>
        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }
    }

    /// <summary>Key-value parameter with optional type hint.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Parameter
    {
        /// <summary>Parameter name.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Parameter value as a JSON token.</summary>
        [JsonProperty("value")]
        public Newtonsoft.Json.Linq.JToken Value { get; set; }

        /// <summary>Type hint (e.g. "float64", "string"), omit if unknown.</summary>
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }
    }

    // ── Client → Server messages (parsed from incoming JSON) ──

    /// <summary>Client -> Server: set parameter values.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SetParameters
    {
        [JsonProperty("op")]
        public string Op => "setParameters";

        [JsonProperty("parameters")]
        public List<Parameter> Parameters { get; set; } = new List<Parameter>();

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }
    }

    /// <summary>Client -> Server: stop receiving parameter updates.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class UnsubscribeParameterUpdates
    {
        [JsonProperty("op")]
        public string Op => "unsubscribeParameterUpdates";

        [JsonProperty("parameterNames")]
        public List<string> ParameterNames { get; set; } = new List<string>();
    }

    // ── Service messages ──

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

    /// <summary>Client -> Server: subscribe to channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribeMessage
    {
        [JsonProperty("op")]
        public string Op => "subscribe";

        [JsonProperty("subscriptions")]
        public List<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }

    /// <summary>
    /// A single subscription entry. "id" is the subscription ID assigned by the client;
    /// "channelId" is the channel the client wants to receive data from.
    /// Server must send MessageData using this subscription ID, not the channel ID.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Subscription
    {
        /// <summary>Client-assigned subscription ID.</summary>
        [JsonProperty("id")]
        public uint Id { get; set; }

        /// <summary>Channel to subscribe to.</summary>
        [JsonProperty("channelId")]
        public uint ChannelId { get; set; }
    }

    /// <summary>Client -> Server: unsubscribe from channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class UnsubscribeMessage
    {
        [JsonProperty("op")]
        public string Op => "unsubscribe";

        [JsonProperty("subscriptionIds")]
        public List<uint> SubscriptionIds { get; set; } = new List<uint>();
    }

    /// <summary>Client -> Server: subscribe to parameter updates.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribeParameterUpdates
    {
        [JsonProperty("op")]
        public string Op => "subscribeParameterUpdates";

        [JsonProperty("parameterNames")]
        public List<string> ParameterNames { get; set; } = new List<string>();
    }

    /// <summary>Client -> Server: request current parameter values.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class GetParameters
    {
        [JsonProperty("op")]
        public string Op => "getParameters";

        [JsonProperty("parameterNames")]
        public List<string> ParameterNames { get; set; } = new List<string>();

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }
    }

    // ── ConnectionGraph messages (Phase 8) ──

    /// <summary>Client -> Server: subscribe to connection graph updates.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribeConnectionGraph
    {
        [JsonProperty("op")]
        public string Op => "subscribeConnectionGraph";
    }

    /// <summary>Client -> Server: unsubscribe from connection graph updates.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class UnsubscribeConnectionGraph
    {
        [JsonProperty("op")]
        public string Op => "unsubscribeConnectionGraph";
    }

    /// <summary>Server → Client: full connection graph snapshot.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ConnectionGraphUpdate
    {
        [JsonProperty("op")]
        public string Op => "connectionGraphUpdate";

        /// <summary>Currently published topics.</summary>
        [JsonProperty("publishedTopics")]
        public List<PublishedTopic> PublishedTopics { get; set; } = new List<PublishedTopic>();

        /// <summary>Currently subscribed topics.</summary>
        [JsonProperty("subscribedTopics")]
        public List<SubscribedTopic> SubscribedTopics { get; set; } = new List<SubscribedTopic>();

        /// <summary>Currently advertised services.</summary>
        [JsonProperty("advertisedServices")]
        public List<AdvertisedService> AdvertisedServices { get; set; } = new List<AdvertisedService>();

        /// <summary>Topics to remove from the graph.</summary>
        [JsonProperty("removedTopics")]
        public List<string> RemovedTopics { get; set; } = new List<string>();

        /// <summary>Services to remove from the graph.</summary>
        [JsonProperty("removedServices")]
        public List<string> RemovedServices { get; set; } = new List<string>();
    }

    /// <summary>A published topic with its publisher IDs.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class PublishedTopic
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("publisherIds")]
        public List<string> PublisherIds { get; set; } = new List<string>();
    }

    /// <summary>A subscribed topic with its subscriber IDs.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribedTopic
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("subscriberIds")]
        public List<string> SubscriberIds { get; set; } = new List<string>();
    }

    /// <summary>An advertised service with its provider IDs.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class AdvertisedService
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("providerIds")]
        public List<string> ProviderIds { get; set; } = new List<string>();
    }

    // ── Phase 9: Assets / fetchAsset ──

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

    // ── Phase 9: DataTimestamp for playback control ──

    /// <summary>Timestamp with seconds and nanoseconds components.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class DataTimestamp
    {
        /// <summary>Whole seconds since epoch.</summary>
        [JsonProperty("sec")]
        public ulong Sec { get; set; }

        /// <summary>Sub-second nanoseconds component (0-999999999).</summary>
        [JsonProperty("nsec")]
        public uint Nsec { get; set; }
    }
}
