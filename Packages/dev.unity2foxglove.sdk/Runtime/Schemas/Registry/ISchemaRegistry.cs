// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Registry
// Purpose: Abstraction over foxglove schema storage and lookup.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Schemas
{
    /// <summary>
    /// Abstraction over schema storage and lookup.
    /// Schema strings are advertised to Foxglove so it knows how to decode channel data.
    /// </summary>
    public interface ISchemaRegistry
    {
        /// <summary>Try to get a schema by its full name (e.g. "foxglove.FrameTransform").</summary>
        bool TryGetSchema(string name, out SchemaEntry entry);

        /// <summary>Register a schema. Schema bytes can be JSON Schema text or raw bytes.</summary>
        void Register(SchemaEntry entry);
    }

    /// <summary>
    /// Optional registry capability for resolving schemas when the same name exists
    /// with multiple schema encodings, such as jsonschema and protobuf.
    /// </summary>
    public interface IEncodingAwareSchemaRegistry : ISchemaRegistry
    {
        /// <summary>Try to get a schema by full name and schema encoding.</summary>
        bool TryGetSchema(string name, string encoding, out SchemaEntry entry);
    }

    /// <summary>Schema metadata + content.</summary>
    public struct SchemaEntry
    {
        /// <summary>Full schema name, e.g. "foxglove.FrameTransform".</summary>
        public string Name;

        /// <summary>Encoding type: "jsonschema", "protobuf", "flatbuffer", "ros1msg", etc.</summary>
        public string Encoding;

        /// <summary>Schema content as a string (e.g. JSON Schema text or base64-encoded binary).</summary>
        public string Content;

        /// <summary>Binary schema content (e.g. protobuf FileDescriptorSet bytes).</summary>
        public byte[] RawContent;
    }

    /// <summary>Minimal in-memory schema registry. Not thread-safe; use from main thread.</summary>
    public class DefaultSchemaRegistry : IEncodingAwareSchemaRegistry
    {
        /// <summary>Foxglove schemaEncoding value for JSON Schema definitions.</summary>
        private const string JsonSchemaEncoding = "jsonschema";

        private readonly Dictionary<string, SchemaEntry> _schemas
            = new Dictionary<string, SchemaEntry>();
        private readonly Dictionary<string, SchemaEntry> _schemasByEncoding
            = new Dictionary<string, SchemaEntry>();

        /// <summary>Try to get a schema by name.</summary>
        public bool TryGetSchema(string name, out SchemaEntry entry)
        {
            if (_schemas.TryGetValue(name, out entry))
            {
                entry = CloneEntryWithRawContentSnapshot(entry);
                return true;
            }

            return false;
        }

        /// <summary>Try to get a schema by name and schema encoding.</summary>
        public bool TryGetSchema(string name, string encoding, out SchemaEntry entry)
        {
            if (_schemasByEncoding.TryGetValue(MakeKey(name, NormalizeEncoding(encoding)), out entry))
            {
                entry = CloneEntryWithRawContentSnapshot(entry);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Register a schema. Multiple encodings can coexist for the same name;
        /// name-only lookup preserves jsonschema as the default when present.
        /// </summary>
        public void Register(SchemaEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Name))
                throw new ArgumentException("Schema name is required", nameof(entry));

            entry = CloneEntryWithRawContentSnapshot(entry);
            entry.Encoding = NormalizeEncoding(entry.Encoding);
            _schemasByEncoding[MakeKey(entry.Name, entry.Encoding)] = entry;

            if (!_schemas.TryGetValue(entry.Name, out var existing)
                || ShouldReplaceNameDefault(existing.Encoding, entry.Encoding))
            {
                _schemas[entry.Name] = entry;
            }
        }

        private static string MakeKey(string name, string encoding)
        {
            return (name ?? string.Empty) + "\n" + NormalizeEncoding(encoding);
        }

        private static string NormalizeEncoding(string encoding)
        {
            return (encoding ?? string.Empty).ToLowerInvariant();
        }

        private static bool ShouldReplaceNameDefault(string existingEncoding, string newEncoding)
        {
            if (string.Equals(newEncoding, JsonSchemaEncoding, StringComparison.OrdinalIgnoreCase))
                return true;
            return !string.Equals(existingEncoding, JsonSchemaEncoding, StringComparison.OrdinalIgnoreCase);
        }

        private static SchemaEntry CloneEntryWithRawContentSnapshot(SchemaEntry entry)
        {
            // RawContent is the only mutable field on SchemaEntry; strings are immutable snapshots.
            if (entry.RawContent != null)
                entry.RawContent = (byte[])entry.RawContent.Clone();
            return entry;
        }
    }
}
