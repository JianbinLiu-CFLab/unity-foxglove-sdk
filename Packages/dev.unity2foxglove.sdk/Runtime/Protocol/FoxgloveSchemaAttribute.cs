// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol
// Purpose: Associates a reference-type DTO with its foxglove schema name for
// automatic schema binding in FoxglovePublisher<T>.

using System;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>
    /// Associates a reference-type DTO class with its foxglove schema name.
    /// Struct DTOs are not supported because schema binding is based on type identity.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class FoxgloveSchemaAttribute : Attribute
    {
        /// <summary>The foxglove schema name (e.g. "foxglove.FrameTransform").</summary>
        public string SchemaName { get; }

        /// <summary>Create the attribute with the given schema name.</summary>
        public FoxgloveSchemaAttribute(string schemaName)
        {
            if (string.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException("Schema name must be non-empty.", nameof(schemaName));

            SchemaName = schemaName;
        }
    }
}
