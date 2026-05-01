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

        [JsonProperty("sessionId", NullValueHandling = NullValueHandling.Ignore)]
        public string SessionId { get; set; }

        public bool ShouldSerializeSupportedEncodings() => SupportedEncodings?.Count > 0;

        public bool ShouldSerializeMetadata() => Metadata?.Count > 0;
    }

    /// <summary>Server → Client: notify client about available channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Advertise
    {
        [JsonProperty("op")]
        public string Op => "advertise";

        [JsonProperty("channels")]
        public List<AdvertiseChannel> Channels { get; set; } = new List<AdvertiseChannel>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class AdvertiseChannel
    {
        [JsonProperty("id")]
        public uint Id { get; set; }

        [JsonProperty("topic")]
        public string Topic { get; set; }

        [JsonProperty("encoding")]
        public string Encoding { get; set; }

        [JsonProperty("schemaName")]
        public string SchemaName { get; set; }

        [JsonProperty("schemaEncoding", NullValueHandling = NullValueHandling.Ignore)]
        public string SchemaEncoding { get; set; }

        [JsonProperty("schema", NullValueHandling = NullValueHandling.Ignore)]
        public string Schema { get; set; }
    }

    /// <summary>Server → Client: remove previously advertised channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Unadvertise
    {
        [JsonProperty("op")]
        public string Op => "unadvertise";

        [JsonProperty("channelIds")]
        public List<uint> ChannelIds { get; set; } = new List<uint>();
    }

    /// <summary>Server → Client: update parameter values.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ParameterValues
    {
        [JsonProperty("op")]
        public string Op => "parameterValues";

        [JsonProperty("parameters")]
        public List<Parameter> Parameters { get; set; } = new List<Parameter>();

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Parameter
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public object Value { get; set; }
    }

    // ── Client → Server messages (parsed from incoming JSON) ──

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
        [JsonProperty("id")]
        public uint Id { get; set; }

        [JsonProperty("channelId")]
        public uint ChannelId { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class UnsubscribeMessage
    {
        [JsonProperty("op")]
        public string Op => "unsubscribe";

        [JsonProperty("subscriptionIds")]
        public List<uint> SubscriptionIds { get; set; } = new List<uint>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribeParameterUpdates
    {
        [JsonProperty("op")]
        public string Op => "subscribeParameterUpdates";

        [JsonProperty("parameterNames")]
        public List<string> ParameterNames { get; set; } = new List<string>();
    }

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
}
