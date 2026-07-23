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

        /// <summary>Optional output rate in Hz; an omitted value resolves to 10 Hz.</summary>
        public float RateHz { get; set; } = -1f;

        /// <summary>Optional schema name. Defaults to the declaring type when empty.</summary>
        public string SchemaName { get; set; }

        /// <summary>Scheduling policy for the aggregate topic.</summary>
        public FoxRunPolicy Policy { get; set; } = FoxRunPolicy.FixedRate;

        /// <summary>
        /// Declared wire encoding for this aggregate topic. The default is
        /// resolved by FoxgloveManager when the topic is registered.
        /// </summary>
        public FoxRunWireEncoding Encoding { get; set; } = FoxRunWireEncoding.Inherit;

        /// <summary>Epsilon for numeric and Unity value-type change detection.</summary>
        public float ChangeEpsilon { get; set; } = 0f;

        /// <summary>Heartbeat interval in seconds for ChangeOrInterval.</summary>
        public float ForceIntervalSeconds { get; set; } = 0f;

        /// <summary>Optional bool field, property, or zero-argument method that must be true to publish.</summary>
        public string When { get; set; } = string.Empty;

        /// <summary>Optional bool field, property, or zero-argument method that must be false to publish.</summary>
        public string Unless { get; set; } = string.Empty;

        /// <summary>Create a class-level FoxRun message for the given topic.</summary>
        public FoxRunMessageAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
