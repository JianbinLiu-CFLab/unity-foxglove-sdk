// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity/Attributes
// Purpose: FoxRun custom attribute — marks fields and properties for auto-publishing to Foxglove topics via source generators.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Mark a field or property to be auto-published as a Foxglove topic.
    /// Usage: [FoxRun("/debug/health", RateHz = 5)]
    /// The annotated class must be declared as partial.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class FoxRunAttribute : Attribute
    {
        /// <summary>Foxglove topic name (e.g. "/debug/pose").</summary>
        public string Topic { get; }

        /// <summary>Publish rate in Hz (default 10).</summary>
        public float RateHz { get; set; } = 10f;

        /// <summary>Optional Foxglove schema name. If empty, publishes schemaless JSON.</summary>
        public string SchemaName { get; set; }

        public FoxRunAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
