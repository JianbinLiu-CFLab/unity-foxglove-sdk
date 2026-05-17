// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg
// Purpose: Convenience bootstrap for registering bundled Foxglove ROS 2 .msg schemas.

using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>
    /// Centralized entry point for registering all official Foxglove ROS 2
    /// .msg schemas into an <see cref="ISchemaRegistry"/>.
    /// </summary>
    public static class Ros2MsgSchemasSetup
    {
        /// <summary>
        /// Register all generated official Foxglove ROS 2 .msg schemas.
        /// Re-registering is safe because schema registries overwrite entries
        /// for the same schema name and encoding.
        /// </summary>
        public static void RegisterSchemas(ISchemaRegistry schemaRegistry)
        {
            FoxgloveRos2MsgSchemaCatalog.RegisterSchemas(schemaRegistry);
        }
    }
}
