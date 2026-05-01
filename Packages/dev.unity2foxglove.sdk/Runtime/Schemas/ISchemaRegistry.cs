using System;

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

    /// <summary>Schema metadata + content.</summary>
    public struct SchemaEntry
    {
        /// <summary>Full schema name, e.g. "foxglove.FrameTransform".</summary>
        public string Name;

        /// <summary>Encoding type: "jsonschema", "protobuf", "flatbuffer", "ros1msg", etc.</summary>
        public string Encoding;

        /// <summary>Raw schema content (e.g. JSON Schema text).</summary>
        public string Content;
    }

    /// <summary>Minimal in-memory schema registry. Not thread-safe; use from main thread.</summary>
    public class DefaultSchemaRegistry : ISchemaRegistry
    {
        private readonly System.Collections.Generic.Dictionary<string, SchemaEntry> _schemas
            = new System.Collections.Generic.Dictionary<string, SchemaEntry>();

        public bool TryGetSchema(string name, out SchemaEntry entry)
        {
            return _schemas.TryGetValue(name, out entry);
        }

        public void Register(SchemaEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Name))
                throw new ArgumentException("Schema name is required", nameof(entry));
            _schemas[entry.Name] = entry;
        }
    }
}
