// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Reflection;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Factory for creating <see cref="ProtobufSchemaRegistry"/> from various sources.
    /// Primary path: <see cref="FromDefault"/> uses the pre-compiled <see cref="FoxgloveSchemas.FileDescriptorSetData"/>
    /// constant (works in both Unity and standalone .NET without file I/O).
    /// Alternative paths: <see cref="FromBytes"/>, <see cref="FromFile"/>, <see cref="FromEmbeddedResource"/>.
    /// </summary>
    public static class ProtobufSchemaRegistryLoader
    {
        /// <summary>
        /// Create a registry using the pre-compiled <see cref="FoxgloveSchemas.FileDescriptorSetData"/> constant.
        /// This is the primary path for both Unity and standalone .NET. Works without file I/O or
        /// embedded resources.
        /// </summary>
        public static ProtobufSchemaRegistry FromDefault(ISchemaRegistry schemaRegistry)
        {
            return new ProtobufSchemaRegistry(FoxgloveSchemas.FileDescriptorSetData, schemaRegistry);
        }

        /// <summary>
        /// Create a registry from in-memory FileDescriptorSet bytes.
        /// </summary>
        public static ProtobufSchemaRegistry FromBytes(byte[] fileDescriptorSetBytes, ISchemaRegistry schemaRegistry)
        {
            return new ProtobufSchemaRegistry(fileDescriptorSetBytes, schemaRegistry);
        }

        /// <summary>
        /// Create a registry from a file path to a compiled FileDescriptorSet binary.
        /// </summary>
        public static ProtobufSchemaRegistry FromFile(string path, ISchemaRegistry schemaRegistry)
        {
            return new ProtobufSchemaRegistry(File.ReadAllBytes(path), schemaRegistry);
        }

        /// <summary>
        /// Create a registry from an embedded resource in the calling assembly.
        /// Useful for standalone .NET scenarios where the .pb is an EmbeddedResource.
        /// For Unity, prefer <see cref="FromDefault"/>.
        /// </summary>
        public static ProtobufSchemaRegistry FromEmbeddedResource(ISchemaRegistry schemaRegistry,
            string resourceName = "Foxglove.Schemas.foxglove_schemas.pb")
        {
            var assembly = Assembly.GetCallingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'. " +
                    "Ensure foxglove_schemas.pb is added as an EmbeddedResource in the project file.");
            return new ProtobufSchemaRegistry(stream, schemaRegistry);
        }
    }
}
