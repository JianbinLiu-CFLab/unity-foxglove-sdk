// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Registry
// Purpose: Factory helpers for creating ProtobufSchemaRegistry from bundled,
// in-memory, file, or embedded-resource descriptor data.

using System;
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
        /// This overload relies on <see cref="Assembly.GetCallingAssembly"/>, which can be
        /// brittle under wrapper methods, trimming, or IL2CPP. For Unity, prefer
        /// <see cref="FromDefault"/>. For standalone callers, prefer
        /// <see cref="FromEmbeddedResource(Assembly, ISchemaRegistry, string)"/> when the
        /// resource assembly is known.
        /// </summary>
        public static ProtobufSchemaRegistry FromEmbeddedResource(ISchemaRegistry schemaRegistry,
            string resourceName = "Foxglove.Schemas.foxglove_schemas.pb")
        {
            var assembly = Assembly.GetCallingAssembly();
            return FromEmbeddedResource(assembly, schemaRegistry, resourceName);
        }

        /// <summary>
        /// Create a registry from an embedded resource in the specified assembly.
        /// This avoids the calling-assembly ambiguity of <see cref="FromEmbeddedResource(ISchemaRegistry, string)"/>.
        /// </summary>
        public static ProtobufSchemaRegistry FromEmbeddedResource(Assembly assembly,
            ISchemaRegistry schemaRegistry,
            string resourceName = "Foxglove.Schemas.foxglove_schemas.pb")
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'. " +
                    "Ensure foxglove_schemas.pb is added as an EmbeddedResource in the project file.");
            return new ProtobufSchemaRegistry(stream, schemaRegistry);
        }
    }
}
