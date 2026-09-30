// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Schemas
// Purpose: Regression coverage for Module 8 schema serialization P3 fixes.

using System;
using System.Linq;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "Module8")]
    [Trait("Domain", "Schemas")]
    public sealed class SchemaSerializationP3Tests
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
        }
    }
}
