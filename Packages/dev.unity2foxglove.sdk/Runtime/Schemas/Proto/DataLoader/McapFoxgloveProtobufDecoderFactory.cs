// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/DataLoader
// Purpose: Packaged Foxglove protobuf decoder factory for MCAP DataLoader.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Foxglove.Schemas;
using Google.Protobuf;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Decodes MCAP protobuf messages when the schema name belongs to the
    /// packaged Foxglove protobuf catalog.
    /// </summary>
    public sealed class McapFoxgloveProtobufDecoderFactory : IMcapMessageDecoderFactory
    {
        private static readonly ConcurrentDictionary<Type, Lazy<MessageParser>> s_parserCache =
            new ConcurrentDictionary<Type, Lazy<MessageParser>>();
        private static readonly Lazy<ProtobufSchemaRegistry> s_bundledRegistry =
            new Lazy<ProtobufSchemaRegistry>(() =>
                ProtobufSchemaRegistryLoader.FromDefault(new DefaultSchemaRegistry()));

        /// <inheritdoc />
        public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
        {
            if (!string.Equals(channel?.MessageEncoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(schema?.Encoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!FoxgloveProtoSchemaCatalog.TryGet(schema?.Name ?? string.Empty, out var entry))
                return new FailingDecoder("Packaged Foxglove protobuf schema is unknown: " + (schema?.Name ?? string.Empty) + ".");

            var bundledDescriptor = s_bundledRegistry.Value.GetFileDescriptorSet(entry.SchemaName);
            if (schema.Data == null || schema.Data.Length == 0)
                return new FailingDecoder("MCAP protobuf schema descriptor is missing for " + entry.SchemaName + ".");
            if (bundledDescriptor == null || !BytesEqual(schema.Data, bundledDescriptor))
                return new FailingDecoder("MCAP protobuf schema descriptor does not match the bundled snapshot for " + entry.SchemaName + ".");

            var parser = ResolveParser(entry.ClrType);
            if (parser == null)
                return new FailingDecoder("Packaged Foxglove protobuf schema does not expose a Parser: " + entry.SchemaName + ".");

            return new Decoder(parser);
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static MessageParser ResolveParser(Type clrType)
        {
            if (clrType == null)
                return null;

            return s_parserCache.GetOrAdd(
                clrType,
                type => new Lazy<MessageParser>(() => ResolveParserUncached(type))).Value;
        }

        private static MessageParser ResolveParserUncached(Type clrType)
        {
            return clrType.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as MessageParser;
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
                var raw = message?.Data ?? Array.Empty<byte>();
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
