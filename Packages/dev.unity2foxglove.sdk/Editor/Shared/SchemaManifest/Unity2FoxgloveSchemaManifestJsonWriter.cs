// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/SchemaManifest
// Purpose: Deterministic compact JSON writer for SDK schema manifest aggregate.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class Unity2FoxgloveSchemaManifestJsonWriter
    {
        public static string WriteCanonical(Unity2FoxgloveSchemaManifest manifest)
        {
            return WriteAggregate(manifest, includeSdkSchemaManifestHash: true);
        }

        public static string WriteAggregateHashInput(Unity2FoxgloveSchemaManifest manifest)
        {
            return WriteAggregate(manifest, includeSdkSchemaManifestHash: false);
        }

        public static string WriteFoxRunSectionHashInput(Unity2FoxgloveFoxRunSummarySection section)
        {
            var sb = new StringBuilder();
            WriteFoxRunSection(sb, section);
            return sb.ToString();
        }

        public static string WriteProtobufRegistrySectionHashInput(Unity2FoxgloveProtobufRegistrySection section)
        {
            var sb = new StringBuilder();
            WriteProtobufRegistrySection(sb, section);
            return sb.ToString();
        }

        public static string WriteRos2MsgRegistrySectionHashInput(Unity2FoxgloveRos2MsgRegistrySection section)
        {
            var sb = new StringBuilder();
            WriteRos2MsgRegistrySection(sb, section);
            return sb.ToString();
        }

        public static string WriteSdkTypedPublishersSectionHashInput(Unity2FoxgloveSdkTypedPublishersSection section)
        {
            var sb = new StringBuilder();
            WriteSdkTypedPublishersSection(sb, section);
            return sb.ToString();
        }

        public static string WriteProtobufCatalogEntryHashInput(IReadOnlyList<Unity2FoxgloveProtobufRegistryEntry> entries)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "entries");
            WriteProtobufEntries(sb, entries ?? Array.Empty<Unity2FoxgloveProtobufRegistryEntry>());
            sb.Append('}');
            return sb.ToString();
        }

        public static string WriteReport(
            Unity2FoxgloveSchemaManifest manifest,
            string generatedAtUtc,
            IReadOnlyList<string> warnings)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "generatedAtUtc");
            AppendString(sb, generatedAtUtc ?? string.Empty);
            sb.Append(',');
            AppendPropertyName(sb, "sdkSchemaManifestHash");
            AppendString(sb, manifest.SdkSchemaManifestHash);
            sb.Append(',');
            AppendPropertyName(sb, "sectionHashes");
            WriteSectionHashes(sb, manifest.SectionHashes);
            sb.Append(',');
            AppendPropertyName(sb, "warnings");
            WriteStringArray(sb, warnings ?? Array.Empty<string>());
            sb.Append('}');
            return sb.ToString();
        }

        private static string WriteAggregate(Unity2FoxgloveSchemaManifest manifest, bool includeSdkSchemaManifestHash)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "manifestVersion");
            sb.Append(manifest.ManifestVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "package");
            AppendString(sb, manifest.Package);
            sb.Append(',');
            AppendPropertyName(sb, "generator");
            WriteGenerator(sb, manifest.Generator);
            sb.Append(',');
            AppendPropertyName(sb, "sections");
            WriteSections(sb, manifest.Sections);
            sb.Append(',');
            AppendPropertyName(sb, "sectionHashes");
            WriteSectionHashes(sb, manifest.SectionHashes);
            if (includeSdkSchemaManifestHash)
            {
                sb.Append(',');
                AppendPropertyName(sb, "sdkSchemaManifestHash");
                AppendString(sb, manifest.SdkSchemaManifestHash);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteGenerator(StringBuilder sb, Unity2FoxgloveSchemaManifestGeneratorInfo generator)
        {
            sb.Append('{');
            AppendPropertyName(sb, "name");
            AppendString(sb, generator.Name);
            sb.Append(',');
            AppendPropertyName(sb, "majorVersion");
            sb.Append(generator.MajorVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
        }

        private static void WriteSections(StringBuilder sb, Unity2FoxgloveSchemaManifestSections sections)
        {
            sb.Append('{');
            AppendPropertyName(sb, "foxRun");
            WriteFoxRunSection(sb, sections.FoxRun);
            sb.Append(',');
            AppendPropertyName(sb, "protobufRegistry");
            WriteProtobufRegistrySection(sb, sections.ProtobufRegistry);
            sb.Append(',');
            AppendPropertyName(sb, "ros2MsgRegistry");
            WriteRos2MsgRegistrySection(sb, sections.Ros2MsgRegistry);
            sb.Append(',');
            AppendPropertyName(sb, "sdkTypedPublishers");
            WriteSdkTypedPublishersSection(sb, sections.SdkTypedPublishers);
            sb.Append('}');
        }

        private static void WriteSectionHashes(StringBuilder sb, Unity2FoxgloveSchemaManifestSectionHashes hashes)
        {
            sb.Append('{');
            AppendPropertyName(sb, "foxRun");
            AppendString(sb, hashes.FoxRun);
            sb.Append(',');
            AppendPropertyName(sb, "protobufRegistry");
            AppendString(sb, hashes.ProtobufRegistry);
            sb.Append(',');
            AppendPropertyName(sb, "ros2MsgRegistry");
            AppendString(sb, hashes.Ros2MsgRegistry);
            sb.Append(',');
            AppendPropertyName(sb, "sdkTypedPublishers");
            AppendString(sb, hashes.SdkTypedPublishers);
            sb.Append('}');
        }

        private static void WriteFoxRunSection(StringBuilder sb, Unity2FoxgloveFoxRunSummarySection section)
        {
            sb.Append('{');
            AppendPropertyName(sb, "present");
            AppendBool(sb, section.Present);
            sb.Append(',');
            AppendPropertyName(sb, "manifestVersion");
            sb.Append(section.ManifestVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "generatorMajorVersion");
            sb.Append(section.GeneratorMajorVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "globalManifestHash");
            AppendString(sb, section.GlobalManifestHash);
            sb.Append(',');
            AppendPropertyName(sb, "manifestHash");
            AppendString(sb, section.ManifestHash);
            sb.Append(',');
            AppendPropertyName(sb, "typeCount");
            sb.Append(section.TypeCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "contractCount");
            sb.Append(section.ContractCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "fieldCount");
            sb.Append(section.FieldCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "source");
            AppendString(sb, section.Source);
            sb.Append('}');
        }

        private static void WriteProtobufRegistrySection(StringBuilder sb, Unity2FoxgloveProtobufRegistrySection section)
        {
            sb.Append('{');
            AppendPropertyName(sb, "schemaEncoding");
            AppendString(sb, section.SchemaEncoding);
            sb.Append(',');
            AppendPropertyName(sb, "catalogEntryHash");
            AppendString(sb, section.CatalogEntryHash);
            sb.Append(',');
            AppendPropertyName(sb, "descriptorDataSha256");
            AppendString(sb, section.DescriptorDataSha256);
            sb.Append(',');
            AppendPropertyName(sb, "entryCount");
            sb.Append(section.EntryCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "entries");
            WriteProtobufEntries(sb, section.Entries);
            sb.Append('}');
        }

        private static void WriteProtobufEntries(StringBuilder sb, IReadOnlyList<Unity2FoxgloveProtobufRegistryEntry> entries)
        {
            sb.Append('[');
            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                var entry = entries[i];
                sb.Append('{');
                AppendPropertyName(sb, "schemaName");
                AppendString(sb, entry.SchemaName);
                sb.Append(',');
                AppendPropertyName(sb, "clrTypeFullName");
                AppendString(sb, entry.ClrTypeFullName);
                sb.Append(',');
                AppendPropertyName(sb, "category");
                AppendString(sb, entry.Category);
                sb.Append(',');
                AppendPropertyName(sb, "hasDedicatedUnityPublisher");
                AppendBool(sb, entry.HasDedicatedUnityPublisher);
                sb.Append(',');
                AppendPropertyName(sb, "note");
                AppendString(sb, entry.Note);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void WriteRos2MsgRegistrySection(StringBuilder sb, Unity2FoxgloveRos2MsgRegistrySection section)
        {
            sb.Append('{');
            AppendPropertyName(sb, "schemaEncoding");
            AppendString(sb, section.SchemaEncoding);
            sb.Append(',');
            AppendPropertyName(sb, "sourceSnapshot");
            AppendString(sb, section.SourceSnapshot);
            sb.Append(',');
            AppendPropertyName(sb, "sourceCommit");
            AppendString(sb, section.SourceCommit);
            sb.Append(',');
            AppendPropertyName(sb, "sourceTreeSha256");
            AppendString(sb, section.SourceTreeSha256);
            sb.Append(',');
            AppendPropertyName(sb, "sourceFileCount");
            sb.Append(section.SourceFileCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "entryCount");
            sb.Append(section.EntryCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "entries");
            WriteRos2Entries(sb, section.Entries);
            sb.Append('}');
        }

        private static void WriteRos2Entries(StringBuilder sb, IReadOnlyList<Unity2FoxgloveRos2MsgRegistryEntry> entries)
        {
            sb.Append('[');
            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                var entry = entries[i];
                sb.Append('{');
                AppendPropertyName(sb, "schemaName");
                AppendString(sb, entry.SchemaName);
                sb.Append(',');
                AppendPropertyName(sb, "sourceFile");
                AppendString(sb, entry.SourceFile);
                sb.Append(',');
                AppendPropertyName(sb, "sourceSha256");
                AppendString(sb, entry.SourceSha256);
                sb.Append(',');
                AppendPropertyName(sb, "category");
                AppendString(sb, entry.Category);
                sb.Append(',');
                AppendPropertyName(sb, "hasDedicatedJsonOrProtobufPublisher");
                AppendBool(sb, entry.HasDedicatedJsonOrProtobufPublisher);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void WriteSdkTypedPublishersSection(StringBuilder sb, Unity2FoxgloveSdkTypedPublishersSection section)
        {
            sb.Append('{');
            AppendPropertyName(sb, "entryCount");
            sb.Append(section.EntryCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "entries");
            WritePublisherEntries(sb, section.Entries);
            sb.Append('}');
        }

        private static void WritePublisherEntries(StringBuilder sb, IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> entries)
        {
            sb.Append('[');
            for (var i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                var entry = entries[i];
                sb.Append('{');
                AppendPropertyName(sb, "publisherTypeFullName");
                AppendString(sb, entry.PublisherTypeFullName);
                sb.Append(',');
                AppendPropertyName(sb, "entryKind");
                AppendString(sb, entry.EntryKind);
                sb.Append(',');
                AppendPropertyName(sb, "publisherFamily");
                AppendString(sb, entry.PublisherFamily);
                sb.Append(',');
                AppendPropertyName(sb, "defaultTopic");
                AppendString(sb, entry.DefaultTopic);
                sb.Append(',');
                AppendPropertyName(sb, "foxgloveSchemaName");
                AppendString(sb, entry.FoxgloveSchemaName);
                sb.Append(',');
                AppendPropertyName(sb, "ros2SchemaName");
                AppendString(sb, entry.Ros2SchemaName);
                sb.Append(',');
                AppendPropertyName(sb, "supportsJson");
                AppendBool(sb, entry.SupportsJson);
                sb.Append(',');
                AppendPropertyName(sb, "supportsProtobuf");
                AppendBool(sb, entry.SupportsProtobuf);
                sb.Append(',');
                AppendPropertyName(sb, "supportsRos2");
                AppendBool(sb, entry.SupportsRos2);
                sb.Append(',');
                AppendPropertyName(sb, "isTemplate");
                AppendBool(sb, entry.IsTemplate);
                sb.Append(',');
                AppendPropertyName(sb, "productNote");
                AppendString(sb, entry.ProductNote);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void WriteStringArray(StringBuilder sb, IReadOnlyList<string> values)
        {
            sb.Append('[');
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                AppendString(sb, values[i]);
            }
            sb.Append(']');
        }

        private static void AppendPropertyName(StringBuilder sb, string value)
        {
            AppendString(sb, value);
            sb.Append(':');
        }

        private static void AppendBool(StringBuilder sb, bool value)
        {
            sb.Append(value ? "true" : "false");
        }

        private static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var ch in value ?? string.Empty)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
