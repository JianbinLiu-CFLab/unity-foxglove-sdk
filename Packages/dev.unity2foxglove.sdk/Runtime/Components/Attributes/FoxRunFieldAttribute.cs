// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: FoxRun aggregate field attribute for class-level JSON topics.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Marks a field or property as part of the containing
    /// <see cref="FoxRunMessageAttribute"/> aggregate payload.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class FoxRunFieldAttribute : Attribute
    {
        /// <summary>Optional JSON property name. Defaults to the member name without leading underscores.</summary>
        public string JsonName { get; }

        /// <summary>Alias for <see cref="JsonName"/>.</summary>
        public string Name => JsonName;

        /// <summary>Create a FoxRun aggregate field using the default JSON name.</summary>
        public FoxRunFieldAttribute()
            : this(string.Empty)
        {
        }

        /// <summary>Create a FoxRun aggregate field with an explicit JSON name.</summary>
        public FoxRunFieldAttribute(string name)
        {
            JsonName = name ?? string.Empty;
        }
    }
}
