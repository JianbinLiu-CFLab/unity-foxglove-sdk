// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Convenience bootstrap for registering bundled Foxglove protobuf
// schemas and enabling protobuf message encoding support.

using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Centralized entry point for registering all official Foxglove protobuf
    /// schemas into an ISchemaRegistry and enabling protobuf on a session.
    /// Uses <see cref="FoxgloveSchemas.FileDescriptorSetData"/> as the data source.
    /// Call this from FoxgloveManager or FoxgloveRuntime startup.
    /// </summary>
    public static class ProtobufSchemasSetup
    {
        /// <summary>
        /// Register all protobuf schemas into the given registry
        /// and enable protobuf encoding support on the session.
        /// </summary>
        public static void Initialize(ISchemaRegistry schemaRegistry, Unity.FoxgloveSDK.Core.FoxgloveSession session)
        {
            if (schemaRegistry == null || session == null) return;
            RegisterSchemas(schemaRegistry);
            session.EnableProtobuf();
        }

        /// <summary>
        /// Register all bundled official Foxglove protobuf schemas into the given registry.
        /// Re-registering is safe because schema registries overwrite existing
        /// entries for the same schema name and encoding.
        /// </summary>
        public static void RegisterSchemas(ISchemaRegistry schemaRegistry)
        {
            if (schemaRegistry == null) return;
            ProtobufSchemaRegistryLoader.FromDefault(schemaRegistry).RegisterAll();
        }
    }
}
