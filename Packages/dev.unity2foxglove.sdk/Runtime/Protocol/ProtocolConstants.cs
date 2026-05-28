// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol
// Purpose: Shared Foxglove WebSocket protocol constants — subprotocols,
// capabilities, enum converter, and status level enum.

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

    /// <summary>Official Foxglove status severity levels.</summary>
    public enum FoxgloveStatusLevel
    {
        /// <summary>Informational status.</summary>
        Info = 0,

        /// <summary>Warning status.</summary>
        Warning = 1,

        /// <summary>Error status.</summary>
        Error = 2
    }
}
