// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Generated
// Purpose: Packaged Foxglove ROS 2 CDR typed decoder factory for MCAP DataLoader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Google.Protobuf;
using Unity.FoxgloveSDK.IO;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>
    /// Decodes MCAP ros2msg+cdr messages when the schema name belongs to the
    /// packaged Foxglove ROS 2 schema catalog.
    /// </summary>
    public sealed class McapRos2CdrTypedDecoderFactory :
        IStableMcapMessageDecoderFactory
    {
        public string StableDecoderId =>
            "unity2foxglove.ros2bridge/cdr-typed";

        /// <inheritdoc />
        public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
        {
            if (!string.Equals(channel?.MessageEncoding, "cdr", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(schema?.Encoding, FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!Ros2CdrDeserializerRegistry.TryGetBySchemaName(schema?.Name ?? string.Empty, out var entry))
                return null;
            if (!FoxgloveRos2MsgSchemaCatalog.TryGet(schema.Name, out var catalogEntry)
                || schema.Data == null)
                return null;

            // Empty schema data is a supported legacy MCAP form. When content
            // is present, compare the complete merged ros2msg definition so a
            // same-name schema cannot select the wrong typed deserializer.
            if (schema.Data.Length > 0
                && !SchemaContentEqual(schema.Data, catalogEntry.Content))
                return null;

            return new Decoder(schema.Name, channel.Topic, entry);
        }

        private static bool SchemaContentEqual(byte[] recorded, string bundled)
        {
            if (recorded == null || string.IsNullOrEmpty(bundled))
                return false;
            var recordedText = CanonicalizeSchema(Encoding.UTF8.GetString(recorded));
            return string.Equals(recordedText, CanonicalizeSchema(bundled), StringComparison.Ordinal);
        }

        private static string CanonicalizeSchema(string value)
        {
            var normalized = (value ?? string.Empty)
                .TrimStart('\uFEFF')
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var meaningfulLines = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                var canonicalLine = CanonicalizeLine(line);
                if (canonicalLine.Length == 0 || canonicalLine.StartsWith("#", StringComparison.Ordinal))
                    continue;
                meaningfulLines.Add(canonicalLine);
            }

            return string.Join("\n", meaningfulLines);
        }

        private static string CanonicalizeLine(string line)
        {
            var builder = new StringBuilder();
            var inQuotes = false;
            var pendingWhitespace = false;
            foreach (var character in line ?? string.Empty)
            {
                if (character == '"')
                {
                    if (pendingWhitespace && builder.Length > 0)
                        builder.Append(' ');
                    pendingWhitespace = false;
                    inQuotes = !inQuotes;
                    builder.Append(character);
                    continue;
                }

                if (!inQuotes && character == '#')
                    break;

                if (!inQuotes && (character == ' ' || character == '\t'))
                {
                    pendingWhitespace = true;
                    continue;
                }

                if (pendingWhitespace && builder.Length > 0)
                    builder.Append(' ');
                pendingWhitespace = false;
                builder.Append(character);
            }

            return builder.ToString().Trim();
        }

        private sealed class Decoder :
            IMcapMessageDecoder,
            IMcapMessageDecoderFailureFallback
        {
            private readonly string _schemaName;
            private readonly string _topic;
            private readonly Ros2CdrDeserializerEntry _entry;

            public Decoder(string schemaName, string topic, Ros2CdrDeserializerEntry entry)
            {
                _schemaName = schemaName ?? string.Empty;
                _topic = topic ?? string.Empty;
                _entry = entry ?? throw new ArgumentNullException(nameof(entry));
            }

            public McapDecodedPayload Decode(McapDataLoaderMessage message)
            {
                var raw = message?.Data ?? new byte[0];
                try
                {
                    var parsed = _entry.Deserialize(raw);
                    return new McapDecodedPayload
                    {
                        Kind = McapDecodedPayloadKind.Provider,
                        DecoderId = "unity2foxglove.ros2bridge/cdr-typed",
                        Value = parsed,
                        Text = JsonFormatter.Default.Format(parsed),
                        RawData = raw
                    };
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "Failed to decode ROS2 CDR payload for schema '" + _schemaName +
                        "', topic '" + _topic +
                        "', payload bytes " + raw.Length + ".",
                        ex);
                }
            }

            public string FailureProblemCode =>
                "McapRos2CdrTypedDecodeFailed";

            public McapDecodedPayload DecodeFallback(McapDataLoaderMessage message)
            {
                var decoder = new McapRos2CdrDiagnosticDecoderFactory()
                    .TryCreate(
                        new McapSchema
                        {
                            Name = _schemaName,
                            Encoding = Ros2BridgeMcapCodecs.SchemaEncoding
                        },
                        new McapChannel
                        {
                            Topic = _topic,
                            MessageEncoding = Ros2BridgeMcapCodecs.MessageEncoding
                        });
                return decoder?.Decode(message);
            }
        }
    }
}
