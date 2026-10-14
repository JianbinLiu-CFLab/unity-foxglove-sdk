// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: Freeze ordinary ROS2 Bridge CDR and ROS MCAP behavior before package extraction.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Phase186
{
    public sealed partial class PreMoveBridgeAndMcapAuthorityTests
    {
        private static JObject LoadFixture()
        {
            var path = FindRepositoryFile(
                "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Fixtures/" +
                "pre_move_bridge_and_mcap_vectors.json");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static string FindRepositoryFile(string relativePath)
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    var candidate = Path.Combine(
                        current.FullName,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate))
                        return candidate;
                    current = current.Parent;
                }
            }

            throw new FileNotFoundException(
                "Could not locate Phase186 pre-move authority fixture.",
                relativePath);
        }

        private static FoxRunResolvedQos ReadQos(JObject value)
            => new FoxRunResolvedQos(
                Enum.Parse<FoxRunQosProfile>(value.Value<string>("profile")),
                Enum.Parse<FoxRunQosReliability>(value.Value<string>("reliability")),
                Enum.Parse<FoxRunQosDurability>(value.Value<string>("durability")),
                Enum.Parse<FoxRunQosHistory>(value.Value<string>("history")),
                value.Value<int>("depth"));

        private static McapSchema Schema(ushort id, string name, string encoding)
            => new McapSchema
            {
                Id = id,
                Name = name,
                Encoding = encoding,
                Data = Array.Empty<byte>()
            };

        private static McapChannel Channel(
            ushort id,
            ushort schemaId,
            string topic,
            string messageEncoding)
            => new McapChannel
            {
                Id = id,
                SchemaId = schemaId,
                Topic = topic,
                MessageEncoding = messageEncoding
            };

        private static McapDataLoaderMessage Message(McapChannel channel, byte[] payload)
            => new McapDataLoaderMessage
            {
                ChannelId = channel.Id,
                SchemaId = channel.SchemaId,
                Topic = channel.Topic,
                MessageEncoding = channel.MessageEncoding,
                LogTime = SampleTimeNs,
                PublishTime = SampleTimeNs,
                Data = payload
            };

        private static McapDecodeRegistry Registry(
            McapDecodeOptions options,
            McapSchema schema,
            McapChannel channel)
            => new McapDecodeRegistry(
                options,
                new Dictionary<ushort, McapSchema> { [schema.Id] = schema },
                new Dictionary<ushort, McapChannel> { [channel.Id] = channel });

        private static McapDecodeOptions BridgeDecodeOptions()
            => new McapDecodeOptions
            {
                DecoderFactories = Ros2BridgeMcapCodecs
                    .CreateFactories()
                    .ToList()
            };

        private static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private sealed class RecordingFactory : IMcapMessageDecoderFactory
        {
            private readonly string _id;
            private readonly List<string> _calls;
            private readonly IMcapMessageDecoder _decoder;

            public RecordingFactory(
                string id,
                List<string> calls,
                IMcapMessageDecoder decoder)
            {
                _id = id;
                _calls = calls;
                _decoder = decoder;
            }

            public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
            {
                _calls.Add(_id);
                return _decoder;
            }
        }

        private sealed class MarkerDecoder : IMcapMessageDecoder
        {
            private readonly string _marker;

            public MarkerDecoder(string marker)
            {
                _marker = marker;
            }

            public McapDecodedPayload Decode(McapDataLoaderMessage message)
                => new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Json,
                    Text = _marker,
                    RawData = message.Data
                };
        }

        private sealed class OrdinarySample
        {
            public OrdinarySample(
                string id,
                string publisher,
                string topic,
                string schemaName,
                byte[] payload)
            {
                Id = id;
                Publisher = publisher;
                Topic = topic;
                SchemaName = schemaName;
                Payload = payload;
            }

            public string Id { get; }
            public string Publisher { get; }
            public string Topic { get; }
            public string SchemaName { get; }
            public byte[] Payload { get; }
        }
    }
}
