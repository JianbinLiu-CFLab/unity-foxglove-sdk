// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Declarative service attribute for generated Foxglove RPC handlers.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Marks a method for generated Foxglove service registration.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class FoxServiceAttribute : Attribute
    {
        /// <summary>
        /// Creates a declarative Foxglove service attribute.
        /// </summary>
        /// <param name="name">Service path, for example <c>/cube/reset_pose</c>.</param>
        public FoxServiceAttribute(string name)
        {
            Name = name ?? string.Empty;
        }

        /// <summary>Service path advertised to Foxglove clients.</summary>
        public string Name { get; }

        /// <summary>Foxglove service type string. Generated defaults are used when empty.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Optional human-readable service description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Request schema name. Generated defaults are used when empty.</summary>
        public string RequestSchemaName { get; set; } = string.Empty;

        /// <summary>Response schema name. Generated defaults are used when empty.</summary>
        public string ResponseSchemaName { get; set; } = string.Empty;
    }
}
