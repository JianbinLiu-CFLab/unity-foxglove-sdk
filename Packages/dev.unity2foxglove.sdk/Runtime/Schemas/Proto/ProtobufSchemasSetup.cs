// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Convenience bootstrap for registering bundled Foxglove protobuf
// schemas and enabling protobuf message encoding support.

using System.Collections.Generic;
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
        // Reference equality is intentional: each schema registry instance must
        // be populated once, but separate sessions/registries remain independent.
        private static readonly HashSet<ISchemaRegistry> _seenRegistries = new();

        /// <summary>
        /// Register all protobuf schemas into the given registry (idempotent per-registry)
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
        /// Idempotent: each registry instance is only populated once.
        /// </summary>
        public static void RegisterSchemas(ISchemaRegistry schemaRegistry)
        {
            if (schemaRegistry == null) return;
            lock (_seenRegistries)
            {
                if (!_seenRegistries.Add(schemaRegistry))
                    return;
            }
            ProtobufSchemaRegistryLoader.FromDefault(schemaRegistry).RegisterAll();
        }
    }
}
