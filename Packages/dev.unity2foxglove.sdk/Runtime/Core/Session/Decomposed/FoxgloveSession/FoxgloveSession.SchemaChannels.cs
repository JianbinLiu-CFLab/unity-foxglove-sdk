// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Foxglove WebSocket session — owns channel/advertise/subscribe
// lifecycle, client messaging dispatch, connection graph, and MCAP recording
// integration. Split into partial classes for readability (Connection,
// Parameters, Services).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Core session that handles Foxglove WebSocket protocol logic:
    /// channel lifecycle, subscription routing, binary message encoding,
    /// client text/binary dispatch, time broadcast, connection graph,
    /// and serverInfo on connect.
    ///
    /// Split across source files for readability:
    /// FoxgloveSession.Connection.cs (subscribe/unsubscribe,
    /// ConnectionGraph, ClientPublish, PlaybackControl, Assets),
    /// FoxgloveSession.Parameters.cs, FoxgloveSession.Services.cs, and
    /// the Phase192 lifecycle, channel, publishing, service, time, and
    /// transport sections.
    /// </summary>
    /// <remarks>Phase192 source sections: FoxgloveSession.Lifecycle.cs, FoxgloveSession.Channels.cs, FoxgloveSession.SchemaChannels.cs, FoxgloveSession.Publishing.cs, FoxgloveSession.ServiceRegistration.cs, FoxgloveSession.TimeAndEncoding.cs, FoxgloveSession.TransportEvents.cs</remarks>
    public partial class FoxgloveSession : IDisposable
    {
        public void RegisterSchemaChannel(
            uint channelId,
            string topic,
            string schemaName,
            string encoding = "json",
            string schemaEncoding = null)
        {
            var messageEncoding = string.IsNullOrEmpty(encoding) ? "json" : encoding;
            if (!string.Equals(messageEncoding, "json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(messageEncoding, "protobuf", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(schemaEncoding))
            {
                throw new InvalidOperationException(
                    "Provider-defined message encodings require an explicit schemaEncoding.");
            }

            var expectedSchemaEncoding = string.IsNullOrEmpty(schemaEncoding)
                ? ExpectedSchemaEncodingForMessageEncoding(messageEncoding)
                : schemaEncoding;

            var found = false;
            SchemaEntry entry;
            if (expectedSchemaEncoding != null && _schemaRegistry is IEncodingAwareSchemaRegistry encodingAwareRegistry)
                found = encodingAwareRegistry.TryGetSchema(schemaName, expectedSchemaEncoding, out entry);
            else
                entry = default;

            if (!found && !_schemaRegistry.TryGetSchema(schemaName, out entry))
                throw new InvalidOperationException($"Schema not found: '{schemaName}'.");

            var resolvedSchemaEncoding = entry.Encoding;
            var schema = entry.Content;
            if (expectedSchemaEncoding != null
                && !string.Equals(resolvedSchemaEncoding, expectedSchemaEncoding, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Schema '{schemaName}' has schema encoding '{resolvedSchemaEncoding}', " +
                    $"but message encoding '{messageEncoding}' requires '{expectedSchemaEncoding}'.");
            }

            // For protobuf, the schema content is already base64-encoded FileDescriptorSet bytes.
            // For JSON, the schema content is the raw JSON Schema text.
            RegisterChannel(new AdvertiseChannel
            {
                Id = channelId, Topic = topic,
                Encoding = messageEncoding, SchemaName = entry.Name,
                SchemaEncoding = resolvedSchemaEncoding, Schema = schema
            });
        }

        private static string ExpectedSchemaEncodingForMessageEncoding(string messageEncoding)
        {
            if (string.Equals(messageEncoding, "json", StringComparison.OrdinalIgnoreCase))
                return "jsonschema";
            if (string.Equals(messageEncoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return "protobuf";
            return null;
        }

        /// <summary>
        /// Register a protobuf-encoded channel and advertise it.
        /// Uses the schema registry to look up FileDescriptorSet bytes.
        /// </summary>
        public void RegisterProtobufSchemaChannel(uint channelId, string topic, string schemaName)
        {
            RegisterSchemaChannel(channelId, topic, schemaName, "protobuf");
        }

    }
}
