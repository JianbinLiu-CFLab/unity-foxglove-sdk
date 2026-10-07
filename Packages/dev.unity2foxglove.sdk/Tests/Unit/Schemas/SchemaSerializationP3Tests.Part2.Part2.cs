// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Schemas
// Purpose: Regression coverage for Module 8 schema serialization P3 fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{


public sealed partial class SchemaSerializationP3Tests
    {
        [Fact]
        public void McapRecorderTracksWrittenComponentContractsOnce()
        {
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            recorder.AddChannel(1, "/module8/component", "msgpack", "", "", "");

            recorder.WriteMessage(1, 1UL, new byte[] { 1 }, "msgpack", "module8.Component", "shape.v1");
            recorder.WriteMessage(1, 2UL, new byte[] { 2 }, "msgpack", "module8.Component", "shape.v1");

            var contracts = recorder.GetRecordedComponentContractSnapshot();
            var contract = Assert.Single(contracts);
            Assert.Equal("/module8/component", contract.Topic);
            Assert.Equal("module8.Component", contract.LogicalSchema);
            Assert.Equal("shape.v1", contract.ShapeIdentity);
        }

        [Fact]
        public void SdkWireSchemaMetadataRoundTripsItsHash()
        {
            Assert.True(SdkWireSchemaMcapMetadata.TryCreateJson("aabbcc", out var json));
            Assert.True(SdkWireSchemaMcapMetadata.TryParseJson(json, out var hash, out var version, out var error), error);
            Assert.Equal("aabbcc", hash);
            Assert.Equal(2, version);

            const string legacyJson = "{\"version\":1,\"hash\":\"aabbcc\"}";
            Assert.True(SdkWireSchemaMcapMetadata.TryParseJson(
                legacyJson,
                out var legacyHash,
                out var legacyVersion,
                out var legacyError), legacyError);
            Assert.Equal("aabbcc", legacyHash);
            Assert.Equal(1, legacyVersion);
        }
        [Fact]
        public void LegacyFoxRunHashFallbackUsesFullRegistry()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-sdk-legacy-hash-fallback-" + Guid.NewGuid().ToString("N") + ".mcap");
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "module8.Legacy",
                Encoding = "jsonschema",
                Content = "{\"type\":\"integer\"}"
            });
            var recordedSchema = new McapSchema
            {
                Id = 1,
                Name = "module8.Legacy",
                Encoding = "jsonschema",
                Data = Encoding.UTF8.GetBytes("{\"type\":\"integer\"}")
            };
            Assert.True(SdkWireSchemaIdentity.TryCompute(
                registry,
                new[] { recordedSchema },
                out var recordedSchemaHash));
            registry.Register(new SchemaEntry
            {
                Name = "module8.Unrelated",
                Encoding = "jsonschema",
                Content = "{\"type\":\"string\"}"
            });
            Assert.True(SdkWireSchemaIdentity.TryCompute(registry, out var recordedHash));
            Assert.NotEqual(recordedSchemaHash, recordedHash);
            var manifest = new FoxRunSchemaManifestInfo(
                1,
                "module8",
                "FoxRunSchemaManifestGenerator",
                1,
                "legacy-manifest",
                "legacy-foxrun",
                Array.Empty<FoxRunSchemaTypeInfo>());
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(manifest, recordedHash, out var foxRunJson));

            FoxRunSchemaInfoRegistry.ClearForTests();
            FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/module8/legacy", "json", "module8.Legacy", "jsonschema", "{\"type\":\"integer\"}");
                    recorder.WriteMessage(1, 1UL, Encoding.UTF8.GetBytes("1"));
                    recorder.WriteMetadata(FoxRunSchemaMcapMetadata.MetadataName, foxRunJson);
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = ReplaySchemaGuard.EvaluateWithMode(
                    engine,
                    SchemaIdentityMode.Strict,
                    registry);

                Assert.Equal(FoxRunReplaySchemaGuardState.Match, result.State);
                Assert.False(result.IsBlocking);

                registry.Register(new SchemaEntry
                {
                    Name = "module8.Later",
                    Encoding = "jsonschema",
                    Content = "{\"type\":\"boolean\"}"
                });
                var changed = ReplaySchemaGuard.EvaluateWithMode(
                    engine,
                    SchemaIdentityMode.Strict,
                    registry);
                Assert.Equal(FoxRunReplaySchemaGuardState.Mismatch, changed.State);
                Assert.True(changed.IsBlocking);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        [Fact]
        public void RawAndTypedMessagePackChannelKeysRemainDistinct()
        {
            var raw = FoxgloveMsgPackChannelIdentity.GetCacheKey(
                "/same/topic",
                "msgpack",
                string.Empty,
                string.Empty);
            var typed = FoxgloveMsgPackChannelIdentity.GetCacheKey(
                "/same/topic",
                "msgpack",
                "module8.Component",
                "shape.v1");
            var changedShape = FoxgloveMsgPackChannelIdentity.GetCacheKey(
                "/same/topic",
                "msgpack",
                "module8.Component",
                "shape.v2");

            Assert.Equal(string.Empty, raw.schemaName);
            Assert.Equal(string.Empty, raw.shapeIdentity);
            Assert.Equal("module8.Component", typed.schemaName);
            Assert.Equal("shape.v1", typed.shapeIdentity);
            Assert.NotEqual(raw, typed);
            Assert.NotEqual(typed, changedShape);
        }

        private static SchemaEncodingMatrixRow CreateProtobufMatrixRow()
        {
            var message = new Foxglove.KeyValuePair { Key = "value", Value = "8" };
            var registry = ProtobufSchemaRegistryLoader.FromDefault(new DefaultSchemaRegistry());
            var descriptor = registry.GetFileDescriptorSet("foxglove.KeyValuePair");
            return new SchemaEncodingMatrixRow(
                "protobuf",
                "foxglove.KeyValuePair",
                Convert.ToBase64String(descriptor),
                () => message.ToByteArray(),
                payload =>
                {
                    var decoder = new McapFoxgloveProtobufDecoderFactory().TryCreate(
                        new McapSchema { Name = "foxglove.KeyValuePair", Encoding = "protobuf", Data = descriptor },
                        new McapChannel { MessageEncoding = "protobuf" });
                    var decoded = Assert.IsType<Foxglove.KeyValuePair>(decoder.Decode(new McapDataLoaderMessage { Data = payload }).Value);
                    Assert.Equal(message.Key, decoded.Key);
                    Assert.Equal(message.Value, decoded.Value);
                },
                payload => Assert.Equal(payload, message.ToByteArray()),
                descriptor);
        }

        private sealed class ComponentIdentityMessage
        {
            public int Value { get; set; }
        }

        private sealed class SchemaEncodingMatrixRow
        {
            public SchemaEncodingMatrixRow(string schemaEncoding, string schemaName, string schemaContent, Func<byte[]> payload, Action<byte[]> decode, byte[] rawContent = null)
                : this(schemaEncoding, schemaName, schemaContent, payload, decode, bytes => Assert.Equal(bytes, payload()), rawContent) { }

            public SchemaEncodingMatrixRow(string schemaEncoding, string schemaName, string schemaContent, Func<byte[]> payload, Action<byte[]> decode, Action<byte[]> roundTrip, byte[] rawContent = null)
            {
                SchemaEncoding = schemaEncoding;
                SchemaName = schemaName;
                SchemaContent = schemaContent;
                Payload = payload;
                Decode = decode;
                RoundTrip = roundTrip;
                RawContent = rawContent;
            }

            public string SchemaEncoding { get; }
            public string SchemaName { get; }
            public string SchemaContent { get; }
            public byte[] RawContent { get; }
            public Func<byte[]> Payload { get; }
            public Action<byte[]> Decode { get; }
            public Action<byte[]> RoundTrip { get; }
        }

        private sealed class MatrixTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public List<string> BroadcastTexts { get; } = new List<string>();
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived { add { } remove { } }
            public event Action<uint, byte[]> OnBinaryReceived { add { } remove { } }
            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() { }
        }
    }
}
