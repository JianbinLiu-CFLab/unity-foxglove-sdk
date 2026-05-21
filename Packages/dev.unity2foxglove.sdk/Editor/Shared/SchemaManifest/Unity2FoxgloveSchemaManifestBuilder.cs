// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/SchemaManifest
// Purpose: Builds deterministic SDK schema manifest aggregates from SDK catalogs.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Editor
{
    public static class Unity2FoxgloveSchemaManifestBuilder
    {
        public const int ManifestVersion = 1;
        public const string PackageName = "Unity2Foxglove";
        public const string GeneratorName = "Unity2FoxgloveSchemaManifest";
        public const int GeneratorMajorVersion = 1;

        public static Unity2FoxgloveSchemaManifest Build(FoxRunCanonicalManifest foxRunManifest)
        {
            var sections = new Unity2FoxgloveSchemaManifestSections(
                BuildFoxRunSection(foxRunManifest),
                BuildProtobufRegistrySection(),
                BuildRos2MsgRegistrySection(),
                BuildSdkTypedPublishersSection());

            var sectionHashes = new Unity2FoxgloveSchemaManifestSectionHashes(
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteFoxRunSectionHashInput(sections.FoxRun)),
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteProtobufRegistrySectionHashInput(sections.ProtobufRegistry)),
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteRos2MsgRegistrySectionHashInput(sections.Ros2MsgRegistry)),
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteSdkTypedPublishersSectionHashInput(sections.SdkTypedPublishers)));

            var manifestWithoutHash = new Unity2FoxgloveSchemaManifest(
                ManifestVersion,
                PackageName,
                new Unity2FoxgloveSchemaManifestGeneratorInfo(GeneratorName, GeneratorMajorVersion),
                sections,
                sectionHashes,
                "");

            var sdkHash = FoxRunManifestHasher.Sha256Hex(
                Unity2FoxgloveSchemaManifestJsonWriter.WriteAggregateHashInput(manifestWithoutHash));

            return new Unity2FoxgloveSchemaManifest(
                manifestWithoutHash.ManifestVersion,
                manifestWithoutHash.Package,
                manifestWithoutHash.Generator,
                manifestWithoutHash.Sections,
                manifestWithoutHash.SectionHashes,
                sdkHash);
        }

        public static Unity2FoxgloveSchemaManifest Build(FoxRunSchemaManifestInfo foxRunSchemaInfo)
        {
            var foxRun = foxRunSchemaInfo == null
                ? BuildFoxRunSection((FoxRunCanonicalManifest)null)
                : new Unity2FoxgloveFoxRunSummarySection(
                    true,
                    foxRunSchemaInfo.ManifestVersion,
                    foxRunSchemaInfo.GeneratorMajorVersion,
                    foxRunSchemaInfo.GlobalManifestHash,
                    foxRunSchemaInfo.FoxRunManifestHash,
                    foxRunSchemaInfo.TypeCount,
                    foxRunSchemaInfo.ContractCount,
                    foxRunSchemaInfo.FieldCount,
                    "generatedRegistry");

            var sections = new Unity2FoxgloveSchemaManifestSections(
                foxRun,
                BuildProtobufRegistrySection(),
                BuildRos2MsgRegistrySection(),
                BuildSdkTypedPublishersSection());

            var sectionHashes = new Unity2FoxgloveSchemaManifestSectionHashes(
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteFoxRunSectionHashInput(sections.FoxRun)),
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteProtobufRegistrySectionHashInput(sections.ProtobufRegistry)),
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteRos2MsgRegistrySectionHashInput(sections.Ros2MsgRegistry)),
                FoxRunManifestHasher.Sha256Hex(Unity2FoxgloveSchemaManifestJsonWriter.WriteSdkTypedPublishersSectionHashInput(sections.SdkTypedPublishers)));

            var manifestWithoutHash = new Unity2FoxgloveSchemaManifest(
                ManifestVersion,
                PackageName,
                new Unity2FoxgloveSchemaManifestGeneratorInfo(GeneratorName, GeneratorMajorVersion),
                sections,
                sectionHashes,
                "");
            var sdkHash = FoxRunManifestHasher.Sha256Hex(
                Unity2FoxgloveSchemaManifestJsonWriter.WriteAggregateHashInput(manifestWithoutHash));

            return new Unity2FoxgloveSchemaManifest(
                manifestWithoutHash.ManifestVersion,
                manifestWithoutHash.Package,
                manifestWithoutHash.Generator,
                manifestWithoutHash.Sections,
                manifestWithoutHash.SectionHashes,
                sdkHash);
        }

        private static Unity2FoxgloveFoxRunSummarySection BuildFoxRunSection(FoxRunCanonicalManifest manifest)
        {
            if (manifest == null)
            {
                return new Unity2FoxgloveFoxRunSummarySection(
                    false,
                    0,
                    0,
                    "",
                    "",
                    0,
                    0,
                    0,
                    "");
            }

            var types = manifest.Sections.FoxRun.Types ?? Array.Empty<FoxRunManifestType>();
            var contracts = types.Sum(type => type.Contracts.Count);
            var fields = types.Sum(type => type.Contracts.Sum(contract => contract.Fields.Count));
            return new Unity2FoxgloveFoxRunSummarySection(
                true,
                manifest.ManifestVersion,
                manifest.Generator.MajorVersion,
                manifest.GlobalManifestHash,
                manifest.Sections.FoxRun.ManifestHash,
                types.Count,
                contracts,
                fields,
                "generatedRegistry");
        }

        private static Unity2FoxgloveProtobufRegistrySection BuildProtobufRegistrySection()
        {
            var entries = FoxgloveProtoSchemaCatalog.Entries
                .Select(entry => new Unity2FoxgloveProtobufRegistryEntry(
                    entry.SchemaName,
                    entry.ClrType.FullName,
                    entry.Category,
                    entry.HasDedicatedUnityPublisher,
                    entry.Note))
                .OrderBy(entry => entry.SchemaName, StringComparer.Ordinal)
                .ThenBy(entry => entry.ClrTypeFullName, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();

            var catalogEntryHash = FoxRunManifestHasher.Sha256Hex(
                Unity2FoxgloveSchemaManifestJsonWriter.WriteProtobufCatalogEntryHashInput(entries));
            var descriptorData = FoxgloveSchemas.FileDescriptorSetData;
            if (descriptorData == null || descriptorData.Length == 0)
                throw new InvalidOperationException("Foxglove protobuf FileDescriptorSetData is empty.");

            return new Unity2FoxgloveProtobufRegistrySection(
                "protobuf",
                catalogEntryHash,
                Sha256Hex(descriptorData),
                entries.Count,
                entries);
        }

        private static Unity2FoxgloveRos2MsgRegistrySection BuildRos2MsgRegistrySection()
        {
            var entries = FoxgloveRos2MsgSchemaCatalog.Entries
                .Select(entry => new Unity2FoxgloveRos2MsgRegistryEntry(
                    entry.SchemaName,
                    entry.SourceFile,
                    entry.SourceSha256,
                    entry.Category,
                    entry.HasDedicatedJsonOrProtobufPublisher))
                .OrderBy(entry => entry.SchemaName, StringComparer.Ordinal)
                .ThenBy(entry => entry.SourceFile, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();

            return new Unity2FoxgloveRos2MsgRegistrySection(
                FoxgloveRos2MsgSchemaCatalog.SchemaEncoding,
                FoxgloveRos2MsgSchemaCatalog.SourceSnapshot,
                FoxgloveRos2MsgSchemaCatalog.SourceCommit,
                FoxgloveRos2MsgSchemaCatalog.SourceTreeSha256,
                FoxgloveRos2MsgSchemaCatalog.SourceFileCount,
                entries.Count,
                entries);
        }

        private static Unity2FoxgloveSdkTypedPublishersSection BuildSdkTypedPublishersSection()
        {
            var entries = FoxgloveSdkPublisherCatalog.Entries
                .OrderBy(entry => entry.PublisherTypeFullName, StringComparer.Ordinal)
                .ThenBy(entry => entry.EntryKind, StringComparer.Ordinal)
                .ThenBy(entry => entry.FoxgloveSchemaName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Ros2SchemaName, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();

            ValidatePublisherCatalog(entries);
            return new Unity2FoxgloveSdkTypedPublishersSection(entries.Count, entries);
        }

        private static void ValidatePublisherCatalog(IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.IsTemplate)
                {
                    if (!string.IsNullOrEmpty(entry.FoxgloveSchemaName) || !string.IsNullOrEmpty(entry.Ros2SchemaName))
                        throw new InvalidOperationException("Generic publisher template entries must not carry fixed schema names: " + entry.PublisherTypeFullName);
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.FoxgloveSchemaName)
                    && !FoxgloveProtoSchemaCatalog.TryGet(entry.FoxgloveSchemaName, out _))
                {
                    throw new InvalidOperationException("SDK publisher catalog references unknown protobuf schema: " + entry.FoxgloveSchemaName);
                }

                if (!string.IsNullOrEmpty(entry.Ros2SchemaName)
                    && !FoxgloveRos2MsgSchemaCatalog.TryGet(entry.Ros2SchemaName, out _))
                {
                    throw new InvalidOperationException("SDK publisher catalog references unknown ROS2 schema: " + entry.Ros2SchemaName);
                }
            }
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
