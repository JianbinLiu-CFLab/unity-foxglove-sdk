// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/DataLoader
// Purpose: Packaged Foxglove protobuf decoder factory for MCAP DataLoader.

using System;
using System.IO;
using System.Reflection;
using Foxglove.Schemas;
using Google.Protobuf;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Decodes MCAP protobuf messages when the schema name belongs to the
    /// packaged Foxglove protobuf catalog.
    /// </summary>
    public sealed class McapFoxgloveProtobufDecoderFactory : IMcapMessageDecoderFactory
    {
        /// <inheritdoc />
        public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
        {
            if (!string.Equals(channel?.MessageEncoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(schema?.Encoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!FoxgloveProtoSchemaCatalog.TryGet(schema?.Name ?? string.Empty, out var entry))
                return new FailingDecoder("Packaged Foxglove protobuf schema is unknown: " + (schema?.Name ?? string.Empty) + ".");

            var parser = entry.ClrType.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as MessageParser;
            if (parser == null)
                return new FailingDecoder("Packaged Foxglove protobuf schema does not expose a Parser: " + entry.SchemaName + ".");

            return new Decoder(parser);
        }

        private sealed class Decoder : IMcapMessageDecoder
        {
            private readonly MessageParser _parser;

            public Decoder(MessageParser parser)
            {
                _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            }

            public McapDecodedPayload Decode(McapDataLoaderMessage message)
            {
                var raw = message?.Data ?? new byte[0];
                var parsed = _parser.ParseFrom(raw);
                return new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Protobuf,
                    Value = parsed,
                    Text = JsonFormatter.Default.Format(parsed),
                    RawData = raw
                };
            }
        }

        private sealed class FailingDecoder : IMcapMessageDecoder
        {
            private readonly string _message;

            public FailingDecoder(string message)
            {
                _message = message ?? string.Empty;
            }

            public McapDecodedPayload Decode(McapDataLoaderMessage message)
            {
                throw new InvalidDataException(_message);
            }
        }
    }
}
