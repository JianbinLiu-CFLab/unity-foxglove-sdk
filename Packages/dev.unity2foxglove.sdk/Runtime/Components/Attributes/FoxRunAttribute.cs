// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: FoxRun custom attribute - marks fields and properties for
// source-generated publishing to Foxglove topics.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declares the data-flow direction for a <see cref="FoxRunAttribute"/>
    /// topic. Publishing remains the default for backward compatibility.
    /// </summary>
    public enum FoxRunMode
    {
        /// <summary>Current FoxRun behavior: Unity publishes the member value.</summary>
        PublishOnly = 0,

        /// <summary>Inbound-only control surface; no generated publish path.</summary>
        SubscribeOnly = 1,

        /// <summary>Advanced bidirectional state sync; publish path remains enabled.</summary>
        PublishAndSubscribe = 2
    }

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

        /// <summary>
        /// Publish mode: FixedRate (default), OnChange, OnChangeOrInterval, or
        /// OnTrigger. OnTrigger topics publish only when generated trigger
        /// methods are called explicitly by user code.
        /// </summary>
        public FoxRunPublishMode PublishMode { get; set; } = FoxRunPublishMode.FixedRate;

        /// <summary>
        /// Data-flow mode for this topic. The default keeps existing FoxRun
        /// members publish-only; inbound modes explicitly expose a control surface.
        /// </summary>
        public FoxRunMode Mode { get; set; } = FoxRunMode.PublishOnly;

        /// <summary>
        /// Declared wire encoding for this topic. The default is resolved by
        /// FoxgloveManager when the topic is registered for a session.
        /// </summary>
        public FoxRunWireEncoding Encoding { get; set; } = FoxRunWireEncoding.Inherit;

        /// <summary>
        /// Subscription provider for inbound data. The default is resolved by
        /// FoxgloveManager when subscriptions are registered for a session.
        /// </summary>
        public FoxRunSubscriptionProvider SubscriptionProvider { get; set; } =
            FoxRunSubscriptionProvider.Inherit;

        /// <summary>
        /// ROS2 QoS preset for an optional native subscription. The default is
        /// resolved by FoxgloveManager when subscriptions are registered.
        /// </summary>
        public FoxRunRos2QosPreset Ros2Qos { get; set; } = FoxRunRos2QosPreset.Inherit;

        /// <summary>
        /// Optional pinned Protobuf field number for this member. Zero uses the
        /// generated stable field number.
        /// </summary>
        public int ProtobufFieldNumber { get; set; }

        /// <summary>Epsilon for float/double/Vector change detection. Negative treated as 0.</summary>
        public float ChangeEpsilon { get; set; } = 0f;

        /// <summary>Heartbeat interval in seconds for OnChangeOrInterval mode. Non-positive disables.</summary>
        public float ForceIntervalSeconds { get; set; } = 0f;

        /// <summary>Optional bool field, property, or zero-argument method that must be true to publish.</summary>
        public string When { get; set; } = string.Empty;

        /// <summary>Optional bool field, property, or zero-argument method that must be false to publish.</summary>
        public string Unless { get; set; } = string.Empty;

        /// <summary>Create a FoxRun attribute for the given Foxglove topic.</summary>
        public FoxRunAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
