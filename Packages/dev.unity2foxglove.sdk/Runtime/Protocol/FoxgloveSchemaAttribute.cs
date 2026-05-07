// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol
// Purpose: Associates a DTO class with its foxglove schema name for
// automatic schema binding in FoxglovePublisher<T>.

using System;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>
    /// Associates a DTO class with its foxglove schema name.
    /// Used by FoxglovePublisher<T> for automatic schema binding.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class FoxgloveSchemaAttribute : Attribute
    {
        /// <summary>The foxglove schema name (e.g. "foxglove.FrameTransform").</summary>
        public string SchemaName { get; }

        /// <summary>Create the attribute with the given schema name.</summary>
        public FoxgloveSchemaAttribute(string schemaName) => SchemaName = schemaName;
    }
}
