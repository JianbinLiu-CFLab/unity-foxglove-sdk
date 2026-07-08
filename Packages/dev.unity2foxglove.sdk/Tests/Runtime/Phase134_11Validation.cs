// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-11 regression coverage for schema descriptor immutability.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase134_11Validation.
    /// </summary>
    public static class Phase134_11Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-11: Schema Registries And Message Definitions ===");
            _passed = 0;

            ProtobufDescriptorLookupReturnsCopies();
            RegisteredProtobufRawContentReturnsCopies();
            ProtobufStreamConstructorRejectsNullStream();
            ProtobufRegisterAllReusesCachedBase64Content();
            ProtobufRegistryHandlesDeepDependencyChainsIteratively();
            DefaultRegistryClonesRawContentOnRegister();
            DefaultRegistryCloneHelperDocumentsRawContentScope();
            EmptyPackageDescriptorNamesAreMapped();
            ProtobufSetupRejectsNullInputs();
            ProtobufLoaderRejectsNullAssembly();
            ProtobufLoaderRejectsMissingFilePath();
            ProtoCatalogUsesSharedSchemaNamesAndNullSafeLookup();
            ScenePrimitiveListsAreStronglyTyped();
            SceneCdrBuilderUnsupportedMessageIsGeneric();
            FoxgloveTimeUtilUsesTickPrecisionAnchor();

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

        private static void ProtobufStreamConstructorRejectsNullStream()
        {
            try
            {
                _ = new ProtobufSchemaRegistry((Stream)null, new DefaultSchemaRegistry());
            }
            catch (ArgumentNullException ex) when (ex.ParamName == "fileDescriptorSetStream")
            {
                Pass("134-11B-6: protobuf stream constructor rejects null streams with a named argument");
                return;
            }

            throw new Exception("[FAIL] 134-11B-6: protobuf stream constructor rejects null streams with a named argument");
        }

        private static void ProtobufRegisterAllReusesCachedBase64Content()
        {
            var descriptorSet = CreateSingleMessageDescriptorSet("phase134_cached.proto", "phase134", "CachedBase64");
            var capture = new CaptureSchemaRegistry();
            var registry = new ProtobufSchemaRegistry(descriptorSet.ToByteArray(), capture);

            registry.RegisterAll();
            registry.RegisterAll();

            Check(capture.Entries.Count == 2
                  && ReferenceEquals(capture.Entries[0].Content, capture.Entries[1].Content),
                "134-11B-7: protobuf RegisterAll reuses cached base64 schema strings");
        }

        private static void ProtobufRegistryHandlesDeepDependencyChainsIteratively()
        {
            const int Depth = 500;
            var descriptorSet = new FileDescriptorSet();
            for (var i = 0; i < Depth; i++)
            {
                var file = new FileDescriptorProto
                {
                    Name = "phase134_deep_" + i + ".proto",
                    Package = "phase134"
                };
                if (i == 0)
                    file.MessageType.Add(new DescriptorProto { Name = "DeepRoot" });
                if (i + 1 < Depth)
                    file.Dependency.Add("phase134_deep_" + (i + 1) + ".proto");
                descriptorSet.File.Add(file);
            }

            var registry = new ProtobufSchemaRegistry(descriptorSet.ToByteArray(), new DefaultSchemaRegistry());
            var bytes = registry.GetFileDescriptorSet("phase134.DeepRoot");

            Check(bytes != null && bytes.Length > 0,
                "134-11B-8: protobuf dependency collection handles deep chains without recursive stack growth");
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

        private static void DefaultRegistryCloneHelperDocumentsRawContentScope()
        {
            var source = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Registry/ISchemaRegistry.cs");

            Check(source.Contains("CloneEntryWithRawContentSnapshot", StringComparison.Ordinal)
                  && source.Contains("RawContent is the only mutable field", StringComparison.Ordinal),
                "134-11D-1: schema clone helper name and comments document its RawContent-only deep copy scope");
        }

        private static void EmptyPackageDescriptorNamesAreMapped()
        {
            var descriptorSet = new FileDescriptorSet();
            var file = new FileDescriptorProto
            {
                Name = "phase134_empty_package.proto",
                Syntax = "proto3"
            };
            file.MessageType.Add(new DescriptorProto { Name = "EmptyPackageMessage" });
            descriptorSet.File.Add(file);

            var registry = new ProtobufSchemaRegistry(descriptorSet.ToByteArray(), new DefaultSchemaRegistry());

            Check(registry.GetFileDescriptorSet("EmptyPackageMessage") != null,
                "134-11E-1: protobuf descriptor map supports package-less message names");
            Check(registry.GetFileDescriptorSet(".EmptyPackageMessage") == null,
                "134-11E-2: protobuf descriptor map does not create leading-dot package-less names");
        }

        private static void ProtobufSetupRejectsNullInputs()
        {
            Check(Throws<ArgumentNullException>(() => ProtobufSchemasSetup.RegisterSchemas(null)),
                "134-11F-1: protobuf schema setup rejects null schema registry");
        }

        private static void ProtobufLoaderRejectsNullAssembly()
        {
            Check(Throws<ArgumentNullException>(() => ProtobufSchemaRegistryLoader.FromEmbeddedResource((Assembly)null, new DefaultSchemaRegistry())),
                "134-11G-1: protobuf embedded-resource loader rejects null assembly");
        }

        private static void ProtobufLoaderRejectsMissingFilePath()
        {
            Check(Throws<ArgumentException>(() => ProtobufSchemaRegistryLoader.FromFile(null, new DefaultSchemaRegistry())),
                "134-11G-2: protobuf file loader rejects null paths clearly");
            Check(Throws<ArgumentException>(() => ProtobufSchemaRegistryLoader.FromFile(" ", new DefaultSchemaRegistry())),
                "134-11G-3: protobuf file loader rejects blank paths clearly");
        }

        private static void ProtoCatalogUsesSharedSchemaNamesAndNullSafeLookup()
        {
            Check(FoxgloveProtoSchemaCatalog.TryGet(FoxgloveSchemaDefinitions.FrameTransformSchemaName, out var entry)
                  && entry.SchemaName == FoxgloveSchemaDefinitions.FrameTransformSchemaName,
                "134-11H-1: protobuf catalog resolves shared schema definition names");

            Check(!FoxgloveProtoSchemaCatalog.TryGet(null, out var nullEntry) && nullEntry == null,
                "134-11H-2: protobuf catalog null lookup is a safe miss");
        }

        private static void ScenePrimitiveListsAreStronglyTyped()
        {
            var entity = new SceneEntity();
            entity.Arrows.Add(new ArrowPrimitive());
            entity.Spheres.Add(new SpherePrimitive());
            entity.Cylinders.Add(new CylinderPrimitive());
            entity.Lines.Add(new LinePrimitive { Type = LinePrimitiveType.LineList });
            entity.Triangles.Add(new TriangleListPrimitive());
            entity.Texts.Add(new TextPrimitive());
            entity.Models.Add(new ModelPrimitive { Data = new byte[] { 1, 2, 3 } });

            Check(entity.Arrows.Count == 1
                  && entity.Spheres.Count == 1
                  && entity.Cylinders.Count == 1
                  && entity.Lines.Count == 1
                  && entity.Triangles.Count == 1
                  && entity.Texts.Count == 1
                  && entity.Models.Count == 1,
                "134-11I-1: SceneEntity exposes concrete primitive DTO lists");

            var source = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/MessageDefinitions/FoxgloveVisualMessages.cs");
            Check(!source.Contains("List<object> Arrows", StringComparison.Ordinal)
                  && !source.Contains("List<object> Spheres", StringComparison.Ordinal)
                  && !source.Contains("List<object> Cylinders", StringComparison.Ordinal)
                  && !source.Contains("List<object> Lines", StringComparison.Ordinal)
                  && !source.Contains("List<object> Triangles", StringComparison.Ordinal)
                  && !source.Contains("List<object> Texts", StringComparison.Ordinal)
                  && !source.Contains("List<object> Models", StringComparison.Ordinal),
                "134-11I-2: SceneEntity visual primitive lists no longer use List<object>");
        }

        private static void SceneCdrBuilderUnsupportedMessageIsGeneric()
        {
            var source = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Builders/Ros2CdrSceneUpdateBuilder.cs");
            Check(source.Contains("EnsureUnsupportedEmpty<T>", StringComparison.Ordinal)
                  && !source.Contains("Phase 91 CDR smoke builder", StringComparison.Ordinal),
                "134-11J-1: SceneUpdate CDR unsupported primitive guard is generic and not phase-specific");
        }

        private static void FoxgloveTimeUtilUsesTickPrecisionAnchor()
        {
            var source = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/MessageDefinitions/FoxgloveTimeUtil.cs");
            Check(source.Contains("UnixEpochTicks", StringComparison.Ordinal)
                  && !source.Contains("ToUnixTimeMilliseconds", StringComparison.Ordinal),
                "134-11K-1: FoxgloveTimeUtil anchors wall clock at tick precision instead of millisecond precision");
        }

        private static FileDescriptorSet CreateSingleMessageDescriptorSet(
            string fileName,
            string packageName,
            string messageName)
        {
            var descriptorSet = new FileDescriptorSet();
            var file = new FileDescriptorProto
            {
                Name = fileName,
                Package = packageName
            };
            file.MessageType.Add(new DescriptorProto { Name = messageName });
            descriptorSet.File.Add(file);
            return descriptorSet;
        }

        private sealed class CaptureSchemaRegistry : ISchemaRegistry
        {
            public readonly List<SchemaEntry> Entries = new List<SchemaEntry>();

            public bool TryGetSchema(string name, out SchemaEntry entry)
            {
                entry = default;
                return false;
            }

            public void Register(SchemaEntry entry)
            {
                Entries.Add(entry);
            }
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            Pass(label);
        }

        private static void Pass(string label)
        {
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
