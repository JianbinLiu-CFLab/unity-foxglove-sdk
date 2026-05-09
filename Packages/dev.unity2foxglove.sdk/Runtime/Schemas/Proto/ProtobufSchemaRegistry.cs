// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Runtime registry that maps Foxglove protobuf schema names to
// FileDescriptorSet bytes for live and MCAP channel registration.

using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Registry that maps Foxglove protobuf schema names to FileDescriptorSet bytes.
    /// Accepts raw FileDescriptorSet bytes (typically from <see cref="FoxgloveSchemas.FileDescriptorSetData"/>).
    /// Use <see cref="ProtobufSchemaRegistryLoader"/> for convenient construction.
    /// </summary>
    public class ProtobufSchemaRegistry
    {
        private readonly Dictionary<string, byte[]> _descriptors = new();
        private readonly ISchemaRegistry _schemaRegistry;

        /// <summary>Schema encoding identifier for protobuf.</summary>
        public const string SchemaEncoding = "protobuf";

        /// <summary>Message encoding identifier for protobuf.</summary>
        public const string MessageEncoding = "protobuf";

        /// <summary>
        /// Create a registry from a FileDescriptorSet byte array (e.g. loaded from Resources or embedded constant).
        /// </summary>
        public ProtobufSchemaRegistry(byte[] fileDescriptorSetBytes, ISchemaRegistry schemaRegistry)
        {
            _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
            if (fileDescriptorSetBytes == null || fileDescriptorSetBytes.Length == 0)
                throw new ArgumentException("FileDescriptorSet bytes must not be null or empty", nameof(fileDescriptorSetBytes));

            var fds = FileDescriptorSet.Parser.ParseFrom(fileDescriptorSetBytes);
            BuildDescriptorMap(fds);
        }

        /// <summary>
        /// Read FileDescriptorSet bytes from a stream and build the registry.
        /// </summary>
        public ProtobufSchemaRegistry(Stream fileDescriptorSetStream, ISchemaRegistry schemaRegistry)
            : this(ReadAllBytes(fileDescriptorSetStream), schemaRegistry) { }

        /// <summary>
        /// Get the FileDescriptorSet bytes for a specific schema, containing only the
        /// descriptors needed to decode that schema (the target file + all transitive dependencies).
        /// Returns null if the schema name is not found.
        /// </summary>
        public byte[] GetFileDescriptorSet(string schemaName)
        {
            return _descriptors.TryGetValue(schemaName, out var bytes) ? bytes : null;
        }

        /// <summary>
        /// Register all known protobuf schemas into the given schema registry.
        /// Each schema is stored with Encoding = "protobuf" and RawContent = its FileDescriptorSet bytes.
        /// </summary>
        public void RegisterAll()
        {
            foreach (var kv in _descriptors)
            {
                _schemaRegistry.Register(new SchemaEntry
                {
                    Name = kv.Key,
                    Encoding = SchemaEncoding,
                    Content = Convert.ToBase64String(kv.Value),
                    RawContent = kv.Value
                });
            }
        }

        /// <summary>
        /// All schema names known to this registry.
        /// </summary>
        public IEnumerable<string> SchemaNames => _descriptors.Keys;

        /// <summary>
        /// Number of registered schema entries.
        /// </summary>
        public int Count => _descriptors.Count;

        // Helpers

        private static byte[] ReadAllBytes(Stream stream)
        {
            if (stream is MemoryStream ms)
                return ms.ToArray();
            using var mem = new MemoryStream();
            stream.CopyTo(mem);
            return mem.ToArray();
        }

        /// <summary>
        /// For each proto file in the FileDescriptorSet, build a minimal FileDescriptorSet
        /// containing that file + all transitive dependencies, and map it by the fully-qualified
        /// message names of the top-level messages in that file.
        /// </summary>
        private void BuildDescriptorMap(FileDescriptorSet fds)
        {
            // Build lookup: proto file name -> FileDescriptorProto.
            var fileMap = new Dictionary<string, FileDescriptorProto>();
            foreach (var file in fds.File)
            {
                if (file.Name != null)
                    fileMap[file.Name] = file;
            }

            foreach (var file in fds.File)
            {
                if (file.Name == null || file.MessageType.Count == 0)
                    continue;

                // Collect transitive dependencies for this file.
                var neededFiles = new HashSet<string>();
                CollectDependencies(file.Name, fileMap, neededFiles);

                // Build a minimal FileDescriptorSet.
                var subset = new FileDescriptorSet();
                foreach (var depName in neededFiles)
                {
                    if (fileMap.TryGetValue(depName, out var depFile))
                        subset.File.Add(depFile);
                }

                var subsetBytes = subset.ToByteArray();

                // Map each top-level message to this FileDescriptorSet.
                foreach (var msg in file.MessageType)
                {
                    var fullName = $"{file.Package}.{msg.Name}";
                    _descriptors[fullName] = subsetBytes;
                }
            }
        }

        private static void CollectDependencies(string fileName,
            Dictionary<string, FileDescriptorProto> fileMap,
            HashSet<string> result)
        {
            if (!result.Add(fileName))
                return;

            if (!fileMap.TryGetValue(fileName, out var file))
                return;

            foreach (var dep in file.Dependency)
            {
                CollectDependencies(dep, fileMap, result);
            }
        }
    }
}
