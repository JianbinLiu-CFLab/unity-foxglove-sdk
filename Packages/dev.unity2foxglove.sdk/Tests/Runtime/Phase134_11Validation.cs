// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-11 regression coverage for schema descriptor immutability.

using System;
using System.Linq;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_11Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-11: Schema Registries And Message Definitions ===");
            _passed = 0;

            ProtobufDescriptorLookupReturnsCopies();
            RegisteredProtobufRawContentReturnsCopies();
            DefaultRegistryClonesRawContentOnRegister();

            Console.WriteLine($"Phase 134-11: {_passed} checks passed.");
        }

        private static void ProtobufDescriptorLookupReturnsCopies()
        {
            var registry = ProtobufSchemaRegistryLoader.FromDefault(new DefaultSchemaRegistry());
            var first = registry.GetFileDescriptorSet("foxglove.FrameTransform");
            Check(first != null && first.Length > 0,
                "134-11A-1: protobuf descriptor lookup returns bytes");

            var original = (byte[])first.Clone();
            first[0] ^= 0xff;

            var second = registry.GetFileDescriptorSet("foxglove.FrameTransform");
            Check(second != null && second.SequenceEqual(original),
                "134-11A-2: mutating returned protobuf descriptor does not corrupt registry state");
            Check(!ReferenceEquals(first, second),
                "134-11A-3: protobuf descriptor lookups return distinct arrays");
        }

        private static void RegisteredProtobufRawContentReturnsCopies()
        {
            var schemaRegistry = new DefaultSchemaRegistry();
            var registry = ProtobufSchemaRegistryLoader.FromDefault(schemaRegistry);
            registry.RegisterAll();

            Check(schemaRegistry.TryGetSchema("foxglove.FrameTransform", "protobuf", out var first),
                "134-11B-1: protobuf schema is registered");
            Check(first.RawContent != null && first.RawContent.Length > 0,
                "134-11B-2: protobuf schema exposes raw descriptor bytes");

            var original = (byte[])first.RawContent.Clone();
            first.RawContent[0] ^= 0xff;

            Check(schemaRegistry.TryGetSchema("foxglove.FrameTransform", "protobuf", out var second),
                "134-11B-3: protobuf schema remains lookupable after caller mutation");
            Check(second.RawContent != null && second.RawContent.SequenceEqual(original),
                "134-11B-4: mutating returned RawContent does not corrupt schema registry state");
            Check(!ReferenceEquals(first.RawContent, second.RawContent),
                "134-11B-5: schema lookups return distinct RawContent arrays");
        }

        private static void DefaultRegistryClonesRawContentOnRegister()
        {
            var rawContent = new byte[] { 1, 2, 3, 4 };
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "phase134.RawSchema",
                Encoding = "protobuf",
                Content = Convert.ToBase64String(rawContent),
                RawContent = rawContent
            });

            rawContent[0] = 99;

            Check(registry.TryGetSchema("phase134.RawSchema", "protobuf", out var entry),
                "134-11C-1: directly registered schema is lookupable");
            Check(entry.RawContent != null && entry.RawContent.SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "134-11C-2: mutating caller-owned RawContent after Register does not corrupt registry state");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
