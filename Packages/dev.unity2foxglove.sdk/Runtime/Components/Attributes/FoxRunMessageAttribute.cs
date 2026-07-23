// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: FoxRun aggregate message attribute for class-level wire-contract topics.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declares a class-level FoxRun message. Members marked with
    /// <see cref="FoxRunFieldAttribute"/> are grouped into one topic payload.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class FoxRunMessageAttribute : Attribute
    {
        /// <summary>Foxglove topic name for the aggregate payload.</summary>
        public string Topic { get; }

        /// <summary>
        /// Optional output rate in Hz. For <see cref="FoxRunPolicy.Change"/>,
        /// a positive value also enables periodic heartbeats.
        /// </summary>
        public float Hz { get; set; } = -1f;

        /// <summary>Optional schema name. Defaults to the declaring type when empty.</summary>
        public string SchemaName { get; set; }

        /// <summary>Scheduling policy for the aggregate topic.</summary>
        public FoxRunPolicy Policy { get; set; } = FoxRunPolicy.FixedRate;

        /// <summary>
        /// Publish targets. Omission inherits the frozen Publish Profile.
        /// An explicit non-empty flags set replaces the profile target set.
        /// </summary>
        public FoxRunEndpoint Targets { get; set; }

        /// <summary>
        /// Foxglove encoding when the effective targets include Foxglove.
        /// Omission inherits the frozen Publish Profile.
        /// </summary>
        public FoxRunEncoding Encoding { get; set; }

        /// <summary>Tolerance for supported numeric change detection.</summary>
        public float Tolerance { get; set; } = 0f;

        /// <summary>
        /// Optional bool field, property, or zero-argument method that must be
        /// true before publishing.
        /// </summary>
        public string OnlyIf { get; set; } = string.Empty;

        /// <summary>Create a class-level FoxRun message for the given topic.</summary>
        public FoxRunMessageAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
