// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket parameter DTOs.

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Protocol
{
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
        public JToken Value { get; set; }

        /// <summary>Type hint (e.g. "float64", "string"), omit if unknown.</summary>
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string Type { get; set; }
    }

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
}
