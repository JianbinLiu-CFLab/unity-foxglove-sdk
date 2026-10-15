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
    [Trait("Phase", "Module8")]
    [Trait("Domain", "Schemas")]
    public sealed partial class SchemaSerializationP3Tests
    {
        [Fact]
        public void RegisteringSameKeyWithSameContentIsIdempotent()
        {
            var registry = new DefaultSchemaRegistry();
            var entry = new SchemaEntry
            {
                Name = "phase8.Type",
                Encoding = " JSONSCHEMA ",
                Content = "{\"type\":\"object\"}"
            };

            registry.Register(entry);
            registry.Register(entry);

            Assert.True(registry.TryGetSchema("phase8.Type", "jsonschema", out var resolved));
            Assert.Equal("jsonschema", resolved.Encoding);
            Assert.Equal(entry.Content, resolved.Content);
        }

        [Fact]
        public void RegisteringSameKeyWithDifferentContentFails()
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "phase8.Type",
                Encoding = "jsonschema",
                Content = "first"
            });

            var exception = Assert.Throws<InvalidOperationException>(() => registry.Register(new SchemaEntry
            {
                Name = "phase8.Type",
                Encoding = " JSONSCHEMA ",
                Content = "second"
            }));

            Assert.Contains("different content", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplaceUpdatesAnExistingSchemaExplicitly()
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "phase8.Type",
                Encoding = "jsonschema",
                Content = "first"
            });

            registry.Replace(new SchemaEntry
            {
                Name = "phase8.Type",
                Encoding = "jsonschema",
                Content = "second"
            });

            Assert.True(registry.TryGetSchema("phase8.Type", out var resolved));
            Assert.Equal("second", resolved.Content);
        }

        [Fact]
        public void EncodingIsTrimmedAndEmptyEncodingIsRejected()
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "phase8.Type",
                Encoding = "  custom  ",
                Content = "schema"
            });

            Assert.True(registry.TryGetSchema("phase8.Type", "CUSTOM", out var resolved));
            Assert.Equal("custom", resolved.Encoding);
            Assert.Throws<ArgumentException>(() => registry.Register(new SchemaEntry
            {
                Name = "phase8.Empty",
                Encoding = " \t",
                Content = "schema"
            }));
        }

        [Fact]
        public void NameOnlyLookupRejectsAmbiguousNonJsonEncodings()
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry { Name = "phase8.Type", Encoding = "protobuf", Content = "one" });
            registry.Register(new SchemaEntry { Name = "phase8.Type", Encoding = "ros2msg", Content = "two" });

            Assert.False(registry.TryGetSchema("phase8.Type", out _));
            Assert.True(registry.TryGetSchema("phase8.Type", "protobuf", out _));
            Assert.True(registry.TryGetSchema("phase8.Type", "ros2msg", out _));
        }

        [Fact]
        public void ProtobufContentAndRawContentMustAgree()
        {
            var registry = new DefaultSchemaRegistry();

            var exception = Assert.Throws<ArgumentException>(() => registry.Register(new SchemaEntry
            {
                Name = "phase8.Type",
                Encoding = "protobuf",
                Content = "AAAA",
                RawContent = new byte[] { 1, 2, 3 }
            }));

            Assert.Contains("base64", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ImuRegistrationDoesNotConflictWithTheOfficialProtobufCatalog()
        {
            var registry = new DefaultSchemaRegistry();
            ProtobufSchemasSetup.RegisterSchemas(registry);

            ImuSchema.Register(registry);

            Assert.True(registry.TryGetSchema(ImuSchema.SchemaName, "protobuf", out var entry));
            Assert.NotNull(entry.RawContent);
            Assert.Equal(Convert.ToBase64String(entry.RawContent), entry.Content);
        }

        [Fact]
        public void UnknownFoxgloveProtobufSchemaIsUnsupported()
        {
            var factory = new McapFoxgloveProtobufDecoderFactory();
            var decoder = factory.TryCreate(
                new McapSchema
                {
                    Name = "foxglove.UnknownModule8Schema",
                    Encoding = "protobuf",
                    Data = new byte[] { 1 }
                },
                new McapChannel { MessageEncoding = "protobuf" });

            Assert.Null(decoder);
        }

        [Fact]
        public void EquivalentDescriptorFileOrderingIsAccepted()
        {
            var schemaName = "foxglove.FrameTransform";
            var bundled = ProtobufSchemaRegistryLoader
                .FromDefault(new DefaultSchemaRegistry())
                .GetFileDescriptorSet(schemaName);
            var descriptorSet = FileDescriptorSet.Parser.ParseFrom(bundled);
            var reordered = new FileDescriptorSet();
            foreach (var file in descriptorSet.File.Reverse())
                reordered.File.Add(file);

            var decoder = new McapFoxgloveProtobufDecoderFactory().TryCreate(
                new McapSchema
                {
                    Name = schemaName,
                    Encoding = "protobuf",
                    Data = reordered.ToByteArray()
                },
                new McapChannel { MessageEncoding = "protobuf" });

            Assert.NotNull(decoder);
            var decoded = decoder.Decode(new McapDataLoaderMessage
            {
                MessageEncoding = "protobuf",
                Data = new Foxglove.FrameTransform
                {
                    ParentFrameId = "module8",
                    ChildFrameId = "descriptor-order"
                }.ToByteArray()
            });
            var value = Assert.IsType<Foxglove.FrameTransform>(decoded.Value);
            Assert.Equal("module8", value.ParentFrameId);
            Assert.Equal("descriptor-order", value.ChildFrameId);
        }

        [Fact]
        public void SupportedSchemaEncodingMatrixAdvertisePayloadDecodeAndRoundTrip()
        {
            var rows = new[]
            {
                new SchemaEncodingMatrixRow("jsonschema", "module8.Json", "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}",
                    payload: () => Encoding.UTF8.GetBytes("{\"value\":8}"),
                    decode: payload => Assert.Equal(8, (int)JObject.Parse(Encoding.UTF8.GetString(payload))["value"])),
                CreateProtobufMatrixRow(),
                new SchemaEncodingMatrixRow("msgpack", "module8.MsgPack", "",
                    payload: () =>
                    {
                        using var writer = new FoxgloveMsgPackWriter();
                        writer.WriteMapHeader(1);
                        writer.WriteString("value");
                        writer.WriteInt32(8);
                        return writer.ToArray();
                    },
                    decode: payload =>
                    {
                        var reader = new FoxgloveMsgPackReader(payload, FoxgloveMsgPackReadLimits.ForPayloadBytes(payload.Length));
                        Assert.True(reader.TryReadMapHeader(out var count));
                        Assert.Equal(1, count);
                        Assert.True(reader.TryReadString(out var key));
                        Assert.Equal("value", key);
                        Assert.True(reader.TryReadInt32(out var value));
                        Assert.Equal(8, value);
                        Assert.False(reader.HasError);
                        Assert.Equal(0, reader.RemainingBytes);
                    })
            };

            foreach (var row in rows)
            {
                var registry = new DefaultSchemaRegistry();
                registry.Register(new SchemaEntry
                {
                    Name = row.SchemaName,
                    Encoding = row.SchemaEncoding,
                    Content = row.SchemaContent,
                    RawContent = row.RawContent
                });
                Assert.True(registry.TryGetSchema(row.SchemaName, row.SchemaEncoding, out var advertised));
                Assert.Equal(row.SchemaContent, advertised.Content);
                Assert.Equal(row.RawContent ?? Array.Empty<byte>(), advertised.RawContent ?? Array.Empty<byte>());

                var payload = row.Payload();
                row.Decode(payload);
                row.RoundTrip(payload);
            }
        }

        [Fact]
        public void SupportedSchemaEncodingMatrixTraversesSessionAdvertisementAndMcap()
        {
            var rows = new[]
            {
                new SchemaEncodingMatrixRow(
                    "jsonschema",
                    "module8.RuntimeJson",
                    "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}",
                    () => Encoding.UTF8.GetBytes("{\"value\":8}"),
                    payload => Assert.Equal(8, (int)JObject.Parse(Encoding.UTF8.GetString(payload))["value"])),
                CreateProtobufMatrixRow(),
                new SchemaEncodingMatrixRow(
                    "",
                    "",
                    "",
                    () =>
                    {
                        using var writer = new FoxgloveMsgPackWriter();
                        writer.WriteMapHeader(1);
                        writer.WriteString("value");
                        writer.WriteInt32(8);
                        return writer.ToArray();
                    },
                    payload =>
                    {
                        var reader = new FoxgloveMsgPackReader(payload, FoxgloveMsgPackReadLimits.ForPayloadBytes(payload.Length));
                        Assert.True(reader.TryReadMapHeader(out var count));
                        Assert.Equal(1, count);
                        Assert.True(reader.TryReadString(out var key));
                        Assert.Equal("value", key);
                        Assert.True(reader.TryReadInt32(out var value));
                        Assert.Equal(8, value);
                        Assert.False(reader.HasError);
                        Assert.Equal(0, reader.RemainingBytes);
                    })
            };

            foreach (var row in rows)
            {
                var registry = new DefaultSchemaRegistry();
                using var transport = new MatrixTransport();
                using var session = new FoxgloveSession("module8-runtime-matrix", transport, schemaRegistry: registry);
                using var stream = new MemoryStream();
                using var recorder = new McapRecorder(stream, leaveOpen: true);

                if (!string.IsNullOrEmpty(row.SchemaName))
                    registry.Register(new SchemaEntry
                    {
                        Name = row.SchemaName,
                        Encoding = row.SchemaEncoding,
                        Content = row.SchemaContent,
                        RawContent = row.RawContent
                    });

                session.SetRecorder(recorder);
                if (row.SchemaEncoding == "jsonschema")
                    session.RegisterSchemaChannel(1, "/module8/runtime/json", row.SchemaName, "json");
                else if (row.SchemaEncoding == "protobuf")
                    session.RegisterProtobufSchemaChannel(1, "/module8/runtime/protobuf", row.SchemaName);
                else
                    session.RegisterChannel(new AdvertiseChannel
                    {
                        Id = 1,
                        Topic = "/module8/runtime/msgpack",
                        Encoding = "msgpack",
                        SchemaName = "",
                        SchemaEncoding = "",
                        Schema = ""
                    });

                var payload = row.Payload();
                session.Publish(1, payload, 8UL);
                session.SetRecorder(null);
                recorder.Close();

                Assert.Contains(transport.BroadcastTexts, value => value.Contains("advertise", StringComparison.Ordinal));
                stream.Position = 0;
                using var reader = new McapStreamingReader(stream, leaveOpen: true);
                var recorded = reader.Read();
                var channel = Assert.Single(recorded.Summary.Channels);
                Assert.Equal(row.SchemaEncoding == "" ? "msgpack" : row.SchemaEncoding == "protobuf" ? "protobuf" : "json", channel.MessageEncoding);
                if (row.SchemaEncoding == "")
                    Assert.Equal((ushort)0, channel.SchemaId);
                else
                    Assert.NotEqual((ushort)0, channel.SchemaId);
                row.Decode(Assert.Single(recorded.Messages).Data);
            }
        }

        [Fact]
        public void StrictIdentityIgnoresClientInputSchemasInRecordedMcap()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-sdk-client-input-" + Guid.NewGuid().ToString("N") + ".mcap");
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "module8.ServerOutput",
                Encoding = "jsonschema",
                Content = "{\"type\":\"integer\"}"
            });

            try
            {
                using (var stream = new MemoryStream())
                {
                    using (var recorder = new McapRecorder(stream, leaveOpen: true))
                    {
                        recorder.AddChannel(1, "/module8/output", "json", "module8.ServerOutput", "jsonschema", "{\"type\":\"integer\"}");
                        recorder.WriteMessage(1, 1UL, Encoding.UTF8.GetBytes("1"));
                        recorder.WriteClientMessage(
                            7,
                            1,
                            2UL,
                            Encoding.UTF8.GetBytes("2"),
                            "/module8/input",
                            "json",
                            "module8.ClientOnly",
                            "jsonschema",
                            "{\"type\":\"string\"}");

                        var schemas = recorder.GetRecordedSchemaSnapshot();
                        Assert.Single(schemas);
                        Assert.Equal("module8.ServerOutput", schemas[0].Name);
                        Assert.True(SdkWireSchemaIdentity.TryCompute(
                            schemas,
                            Array.Empty<SdkWireSchemaComponentIdentity>(),
                            out var hash));
                        Assert.True(SdkWireSchemaMcapMetadata.TryCreateJson(hash, out var metadataJson));
                        recorder.WriteMetadata(SdkWireSchemaMcapMetadata.MetadataName, metadataJson);
                        recorder.Close();
                    }
                    File.WriteAllBytes(path, stream.ToArray());
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = ReplaySchemaGuard.EvaluateWithMode(
                    engine,
                    SchemaIdentityMode.Strict,
                    registry);

                Assert.Equal(FoxRunReplaySchemaGuardState.Match, result.State);
                Assert.False(result.IsBlocking);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

    }
}
