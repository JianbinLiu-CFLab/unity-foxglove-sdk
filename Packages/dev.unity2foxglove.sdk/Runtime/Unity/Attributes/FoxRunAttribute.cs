// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity/Attributes
// Purpose: FoxRun custom attribute - marks fields and properties for
// source-generated publishing to Foxglove topics.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Marks a field or property for source-generated publishing as a Foxglove
    /// topic. The containing <c>MonoBehaviour</c> must be declared
    /// <c>partial</c> so the generator can add the publish implementation.
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

        /// <summary>Publish mode: FixedRate (default), OnChange, or OnChangeOrInterval.</summary>
        public FoxRunPublishMode PublishMode { get; set; } = FoxRunPublishMode.FixedRate;

        /// <summary>Epsilon for float/double/Vector change detection. Negative treated as 0.</summary>
        public float ChangeEpsilon { get; set; } = 0f;

        /// <summary>Heartbeat interval in seconds for OnChangeOrInterval mode. Non-positive disables.</summary>
        public float ForceIntervalSeconds { get; set; } = 0f;

        /// <summary>Create a FoxRun attribute for the given Foxglove topic.</summary>
        public FoxRunAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
