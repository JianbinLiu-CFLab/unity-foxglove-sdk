// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/SchemaManifest
// Purpose: Host-independent DTOs for the SDK schema manifest aggregate.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class Unity2FoxgloveSchemaManifest
    {
        public int ManifestVersion { get; }
        public string Package { get; }
        public Unity2FoxgloveSchemaManifestGeneratorInfo Generator { get; }
        public Unity2FoxgloveSchemaManifestSections Sections { get; }
        public Unity2FoxgloveSchemaManifestSectionHashes SectionHashes { get; }
        public string SdkSchemaManifestHash { get; }

        public Unity2FoxgloveSchemaManifest(
            int manifestVersion,
            string packageName,
            Unity2FoxgloveSchemaManifestGeneratorInfo generator,
            Unity2FoxgloveSchemaManifestSections sections,
            Unity2FoxgloveSchemaManifestSectionHashes sectionHashes,
            string sdkSchemaManifestHash)
        {
            ManifestVersion = manifestVersion;
            Package = packageName ?? string.Empty;
            Generator = generator ?? throw new ArgumentNullException(nameof(generator));
            Sections = sections ?? throw new ArgumentNullException(nameof(sections));
            SectionHashes = sectionHashes ?? throw new ArgumentNullException(nameof(sectionHashes));
            SdkSchemaManifestHash = sdkSchemaManifestHash ?? string.Empty;
        }
    }

    public sealed class Unity2FoxgloveSchemaManifestGeneratorInfo
    {
        public string Name { get; }
        public int MajorVersion { get; }

        public Unity2FoxgloveSchemaManifestGeneratorInfo(string name, int majorVersion)
        {
            Name = name ?? string.Empty;
            MajorVersion = majorVersion;
        }
    }

    public sealed class Unity2FoxgloveSchemaManifestSections
    {
        public Unity2FoxgloveFoxRunSummarySection FoxRun { get; }
        public Unity2FoxgloveProtobufRegistrySection ProtobufRegistry { get; }
        public Unity2FoxgloveRos2MsgRegistrySection Ros2MsgRegistry { get; }
        public Unity2FoxgloveSdkTypedPublishersSection SdkTypedPublishers { get; }

        public Unity2FoxgloveSchemaManifestSections(
            Unity2FoxgloveFoxRunSummarySection foxRun,
            Unity2FoxgloveProtobufRegistrySection protobufRegistry,
            Unity2FoxgloveRos2MsgRegistrySection ros2MsgRegistry,
            Unity2FoxgloveSdkTypedPublishersSection sdkTypedPublishers)
        {
            FoxRun = foxRun ?? throw new ArgumentNullException(nameof(foxRun));
            ProtobufRegistry = protobufRegistry ?? throw new ArgumentNullException(nameof(protobufRegistry));
            Ros2MsgRegistry = ros2MsgRegistry ?? throw new ArgumentNullException(nameof(ros2MsgRegistry));
            SdkTypedPublishers = sdkTypedPublishers ?? throw new ArgumentNullException(nameof(sdkTypedPublishers));
        }
    }

    public sealed class Unity2FoxgloveSchemaManifestSectionHashes
    {
        public string FoxRun { get; }
        public string ProtobufRegistry { get; }
        public string Ros2MsgRegistry { get; }
        public string SdkTypedPublishers { get; }

        public Unity2FoxgloveSchemaManifestSectionHashes(
            string foxRun,
            string protobufRegistry,
            string ros2MsgRegistry,
            string sdkTypedPublishers)
        {
            FoxRun = foxRun ?? string.Empty;
            ProtobufRegistry = protobufRegistry ?? string.Empty;
            Ros2MsgRegistry = ros2MsgRegistry ?? string.Empty;
            SdkTypedPublishers = sdkTypedPublishers ?? string.Empty;
        }
    }

    public sealed class Unity2FoxgloveFoxRunSummarySection
    {
        public bool Present { get; }
        public int ManifestVersion { get; }
        public int GeneratorMajorVersion { get; }
        public string GlobalManifestHash { get; }
        public string ManifestHash { get; }
        public int TypeCount { get; }
        public int ContractCount { get; }
        public int FieldCount { get; }
        public string Source { get; }

        public Unity2FoxgloveFoxRunSummarySection(
            bool present,
            int manifestVersion,
            int generatorMajorVersion,
            string globalManifestHash,
            string manifestHash,
            int typeCount,
            int contractCount,
            int fieldCount,
            string source)
        {
            Present = present;
            ManifestVersion = manifestVersion;
            GeneratorMajorVersion = generatorMajorVersion;
            GlobalManifestHash = globalManifestHash ?? string.Empty;
            ManifestHash = manifestHash ?? string.Empty;
            TypeCount = typeCount;
            ContractCount = contractCount;
            FieldCount = fieldCount;
            Source = source ?? string.Empty;
        }
    }

    public sealed class Unity2FoxgloveProtobufRegistrySection
    {
        public string SchemaEncoding { get; }
        public string CatalogEntryHash { get; }
        public string DescriptorDataSha256 { get; }
        public int EntryCount { get; }
        public IReadOnlyList<Unity2FoxgloveProtobufRegistryEntry> Entries { get; }

        public Unity2FoxgloveProtobufRegistrySection(
            string schemaEncoding,
            string catalogEntryHash,
            string descriptorDataSha256,
            int entryCount,
            IReadOnlyList<Unity2FoxgloveProtobufRegistryEntry> entries)
        {
            SchemaEncoding = schemaEncoding ?? string.Empty;
            CatalogEntryHash = catalogEntryHash ?? string.Empty;
            DescriptorDataSha256 = descriptorDataSha256 ?? string.Empty;
            Entries = Copy(entries);
            ValidateEntryCount(entryCount, Entries.Count, nameof(Unity2FoxgloveProtobufRegistrySection));
            EntryCount = entryCount;
        }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
            => new List<T>(values ?? Array.Empty<T>()).AsReadOnly();

        private static void ValidateEntryCount(int declared, int actual, string sectionName)
        {
            if (declared != actual)
                throw new ArgumentException(sectionName + " entryCount " + declared + " does not match entries.Count " + actual + ".");
        }
    }

    public sealed class Unity2FoxgloveProtobufRegistryEntry
    {
        public string SchemaName { get; }
        public string ClrTypeFullName { get; }
        public string Category { get; }
        public bool HasDedicatedUnityPublisher { get; }
        public string Note { get; }

        public Unity2FoxgloveProtobufRegistryEntry(
            string schemaName,
            string clrTypeFullName,
            string category,
            bool hasDedicatedUnityPublisher,
            string note)
        {
            SchemaName = schemaName ?? string.Empty;
            ClrTypeFullName = clrTypeFullName ?? string.Empty;
            Category = category ?? string.Empty;
            HasDedicatedUnityPublisher = hasDedicatedUnityPublisher;
            Note = note ?? string.Empty;
        }
    }

    public sealed class Unity2FoxgloveRos2MsgRegistrySection
    {
        public string SchemaEncoding { get; }
        public string SourceSnapshot { get; }
        public string SourceCommit { get; }
        public string SourceTreeSha256 { get; }
        public int SourceFileCount { get; }
        public int EntryCount { get; }
        public IReadOnlyList<Unity2FoxgloveRos2MsgRegistryEntry> Entries { get; }

        public Unity2FoxgloveRos2MsgRegistrySection(
            string schemaEncoding,
            string sourceSnapshot,
            string sourceCommit,
            string sourceTreeSha256,
            int sourceFileCount,
            int entryCount,
            IReadOnlyList<Unity2FoxgloveRos2MsgRegistryEntry> entries)
        {
            SchemaEncoding = schemaEncoding ?? string.Empty;
            SourceSnapshot = sourceSnapshot ?? string.Empty;
            SourceCommit = sourceCommit ?? string.Empty;
            SourceTreeSha256 = sourceTreeSha256 ?? string.Empty;
            SourceFileCount = sourceFileCount;
            Entries = Copy(entries);
            ValidateEntryCount(entryCount, Entries.Count, nameof(Unity2FoxgloveRos2MsgRegistrySection));
            EntryCount = entryCount;
        }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
            => new List<T>(values ?? Array.Empty<T>()).AsReadOnly();

        private static void ValidateEntryCount(int declared, int actual, string sectionName)
        {
            if (declared != actual)
                throw new ArgumentException(sectionName + " entryCount " + declared + " does not match entries.Count " + actual + ".");
        }
    }

    public sealed class Unity2FoxgloveRos2MsgRegistryEntry
    {
        public string SchemaName { get; }
        public string SourceFile { get; }
        public string SourceSha256 { get; }
        public string Category { get; }
        public bool HasDedicatedJsonOrProtobufPublisher { get; }

        public Unity2FoxgloveRos2MsgRegistryEntry(
            string schemaName,
            string sourceFile,
            string sourceSha256,
            string category,
            bool hasDedicatedJsonOrProtobufPublisher)
        {
            SchemaName = schemaName ?? string.Empty;
            SourceFile = sourceFile ?? string.Empty;
            SourceSha256 = sourceSha256 ?? string.Empty;
            Category = category ?? string.Empty;
            HasDedicatedJsonOrProtobufPublisher = hasDedicatedJsonOrProtobufPublisher;
        }
    }

    public sealed class Unity2FoxgloveSdkTypedPublishersSection
    {
        public int EntryCount { get; }
        public IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> Entries { get; }

        public Unity2FoxgloveSdkTypedPublishersSection(
            int entryCount,
            IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> entries)
        {
            Entries = new List<Unity2FoxgloveSdkTypedPublisherEntry>(
                entries ?? Array.Empty<Unity2FoxgloveSdkTypedPublisherEntry>()).AsReadOnly();
            if (entryCount != Entries.Count)
                throw new ArgumentException(
                    nameof(Unity2FoxgloveSdkTypedPublishersSection) + " entryCount " + entryCount +
                    " does not match entries.Count " + Entries.Count + ".");
            EntryCount = entryCount;
        }
    }

    public sealed class Unity2FoxgloveSdkTypedPublisherEntry
    {
        public string PublisherTypeFullName { get; }
        public string EntryKind { get; }
        public string PublisherFamily { get; }
        public string DefaultTopic { get; }
        public string FoxgloveSchemaName { get; }
        public string Ros2SchemaName { get; }
        public bool SupportsJson { get; }
        public bool SupportsProtobuf { get; }
        public bool SupportsRos2 { get; }
        public bool IsTemplate { get; }
        public string ProductNote { get; }

        public Unity2FoxgloveSdkTypedPublisherEntry(
            string publisherTypeFullName,
            string entryKind,
            string publisherFamily,
            string defaultTopic,
            string foxgloveSchemaName,
            string ros2SchemaName,
            bool supportsJson,
            bool supportsProtobuf,
            bool supportsRos2,
            bool isTemplate,
            string productNote)
        {
            PublisherTypeFullName = publisherTypeFullName ?? string.Empty;
            EntryKind = entryKind ?? string.Empty;
            PublisherFamily = publisherFamily ?? string.Empty;
            DefaultTopic = defaultTopic ?? string.Empty;
            FoxgloveSchemaName = foxgloveSchemaName ?? string.Empty;
            Ros2SchemaName = ros2SchemaName ?? string.Empty;
            SupportsJson = supportsJson;
            SupportsProtobuf = supportsProtobuf;
            SupportsRos2 = supportsRos2;
            IsTemplate = isTemplate;
            ProductNote = productNote ?? string.Empty;
        }
    }
}
