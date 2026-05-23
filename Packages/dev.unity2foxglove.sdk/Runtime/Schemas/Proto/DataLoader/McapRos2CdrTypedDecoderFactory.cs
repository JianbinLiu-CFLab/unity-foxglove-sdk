// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/DataLoader
// Purpose: Packaged Foxglove ROS 2 CDR typed decoder factory for MCAP DataLoader.

using System;
using System.IO;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Decodes MCAP ros2msg+cdr messages when the schema name belongs to the
    /// packaged Foxglove ROS 2 schema catalog.
    /// </summary>
    public sealed class McapRos2CdrTypedDecoderFactory : IMcapMessageDecoderFactory
    {
        /// <inheritdoc />
        public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
        {
            if (!string.Equals(channel?.MessageEncoding, "cdr", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(schema?.Encoding, FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!Ros2CdrDeserializerRegistry.TryGetBySchemaName(schema?.Name ?? string.Empty, out var entry))
                return null;

            return new Decoder(schema.Name, channel.Topic, entry);
        }

        private sealed class Decoder : IMcapMessageDecoder
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
                        Kind = McapDecodedPayloadKind.Ros2CdrTyped,
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
        }
    }
}
