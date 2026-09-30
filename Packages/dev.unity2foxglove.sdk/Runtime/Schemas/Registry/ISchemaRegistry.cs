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
        /// <summary>
        /// Try to get a schema by its full name (e.g. "foxglove.FrameTransform").
        /// Name-only lookup returns false when multiple non-JSON encodings are registered.
        /// </summary>
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

    /// <summary>Minimal in-memory schema registry.</summary>
    public class DefaultSchemaRegistry : IEncodingAwareSchemaRegistry
    {
        /// <summary>Foxglove schemaEncoding value for JSON Schema definitions.</summary>
        private const string JsonSchemaEncoding = "jsonschema";

        private readonly object _gate = new object();
        private readonly Dictionary<string, SchemaEntry> _schemas
            = new Dictionary<string, SchemaEntry>();
        private readonly Dictionary<string, SchemaEntry> _schemasByEncoding
            = new Dictionary<string, SchemaEntry>();

        /// <summary>Try to get a schema by name.</summary>
        public bool TryGetSchema(string name, out SchemaEntry entry)
        {
            lock (_gate)
            {
                if (_schemas.TryGetValue(name, out entry))
                {
                    entry = CloneEntryWithRawContentSnapshot(entry);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Try to get a schema by name and schema encoding.</summary>
        public bool TryGetSchema(string name, string encoding, out SchemaEntry entry)
        {
            lock (_gate)
            {
                if (_schemasByEncoding.TryGetValue(MakeKey(name, NormalizeEncoding(encoding)), out entry))
                {
                    entry = CloneEntryWithRawContentSnapshot(entry);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Register a schema. Multiple encodings can coexist for the same name;
        /// duplicate keys must have identical content and name-only lookup preserves
        /// jsonschema as the default when present.
        /// </summary>
        public void Register(SchemaEntry entry)
        {
            entry = PrepareEntry(entry);
            lock (_gate)
            {
                var key = MakeKey(entry.Name, entry.Encoding);
                if (_schemasByEncoding.TryGetValue(key, out var existing))
                {
                    if (!EntriesEqual(existing, entry))
                    {
                        throw new InvalidOperationException(
                            "Schema '" + entry.Name + "' with encoding '" + entry.Encoding +
                            "' is already registered with different content.");
                    }

                    return;
                }

                _schemasByEncoding.Add(key, entry);
                RecomputeNameDefaultLocked(entry.Name);
            }
        }

        /// <summary>Replace an existing schema key explicitly.</summary>
        public void Replace(SchemaEntry entry)
        {
            entry = PrepareEntry(entry);
            lock (_gate)
            {
                var key = MakeKey(entry.Name, entry.Encoding);
                if (!_schemasByEncoding.ContainsKey(key))
                {
                    throw new InvalidOperationException(
                        "Schema '" + entry.Name + "' with encoding '" + entry.Encoding +
                        "' is not registered and cannot be replaced.");
                }

                _schemasByEncoding[key] = entry;
                RecomputeNameDefaultLocked(entry.Name);
            }
        }

        private static string MakeKey(string name, string encoding)
        {
            return (name ?? string.Empty) + "\n" + (encoding ?? string.Empty);
        }

        private static SchemaEntry PrepareEntry(SchemaEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Name))
                throw new ArgumentException("Schema name is required", nameof(entry));

            entry.Encoding = NormalizeEncoding(entry.Encoding);
            if (entry.Encoding.Length == 0)
                throw new ArgumentException("Schema encoding is required", nameof(entry));
            ValidateContentInvariant(entry);
            return CloneEntryWithRawContentSnapshot(entry);
        }

        private static string NormalizeEncoding(string encoding)
        {
            if (string.IsNullOrWhiteSpace(encoding))
                return string.Empty;
            return encoding.Trim().ToLowerInvariant();
        }

        private void RecomputeNameDefaultLocked(string name)
        {
            SchemaEntry selected = default;
            var count = 0;
            var hasJson = false;
            foreach (var candidate in _schemasByEncoding.Values)
            {
                if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                    continue;

                count++;
                if (string.Equals(candidate.Encoding, JsonSchemaEncoding, StringComparison.Ordinal))
                {
                    selected = candidate;
                    hasJson = true;
                }
                else if (!hasJson)
                {
                    selected = candidate;
                }
            }

            if (count == 0 || (count > 1 && !hasJson))
            {
                _schemas.Remove(name);
                return;
            }

            _schemas[name] = selected;
        }

        private static bool EntriesEqual(SchemaEntry left, SchemaEntry right)
        {
            if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || !string.Equals(left.Encoding, right.Encoding, StringComparison.Ordinal)
                || !string.Equals(left.Content, right.Content, StringComparison.Ordinal))
                return false;

            if (ReferenceEquals(left.RawContent, right.RawContent))
                return true;
            if (left.RawContent == null || right.RawContent == null || left.RawContent.Length != right.RawContent.Length)
                return false;
            for (var i = 0; i < left.RawContent.Length; i++)
                if (left.RawContent[i] != right.RawContent[i])
                    return false;
            return true;
        }

        private static void ValidateContentInvariant(SchemaEntry entry)
        {
            if (string.Equals(entry.Encoding, "protobuf", StringComparison.Ordinal)
                && entry.Content != null
                && entry.RawContent != null
                && !string.Equals(entry.Content, Convert.ToBase64String(entry.RawContent), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Protobuf schema Content must equal the base64 encoding of RawContent.",
                    nameof(entry));
            }
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
