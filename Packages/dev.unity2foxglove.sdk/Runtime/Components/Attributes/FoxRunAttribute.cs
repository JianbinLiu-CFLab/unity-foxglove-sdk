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
    /// Marks a field or property for source-generated publishing as a Foxglove
    /// topic. The containing <c>MonoBehaviour</c> must be declared
    /// <c>partial</c> so the generator can add the publish implementation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class FoxRunAttribute : Attribute
    {
        /// <summary>Foxglove topic name (e.g. "/debug/pose").</summary>
        public string Topic { get; }

        /// <summary>
        /// Optional update rate in Hz. A positive value limits fixed-rate
        /// publication and input application. For <see cref="FoxRunPolicy.Change"/>,
        /// it also enables periodic publish heartbeats and fresh-input duplicate
        /// refreshes. The omitted sentinel resolves through the frozen directional
        /// Manager profile where the selected policy requires a cadence.
        /// </summary>
        public float Hz { get; set; } = -1f;

        /// <summary>Optional Foxglove schema name. If empty, publishes schemaless JSON.</summary>
        public string SchemaName { get; set; }

        /// <summary>
        /// Scheduling policy: FixedRate (default), Change, or Trigger. Trigger
        /// topics cross the Unity boundary only when generated directional
        /// methods are called explicitly by user code.
        /// </summary>
        public FoxRunPolicy Policy { get; set; } = FoxRunPolicy.FixedRate;

        /// <summary>
        /// Data-flow mode for this topic. Publish is the default; inbound modes
        /// explicitly expose a control surface.
        /// </summary>
        public FoxRunFlow Mode { get; set; } = FoxRunFlow.Publish;

        /// <summary>
        /// Optional stable publish Provider IDs. Omission inherits the
        /// Manager's frozen publish selection. An explicit array must contain
        /// one or more unique IDs; lowering canonicalizes their order.
        /// </summary>
        public string[] PublishTransportIds { get; set; }

        /// <summary>
        /// Optional stable Subscribe Provider ID. Omission inherits the
        /// Manager's frozen single-source selection.
        /// </summary>
        public string SubscribeTransportId { get; set; }

        /// <summary>
        /// Foxglove encoding for every Foxglove direction selected by this
        /// declaration. Omission inherits each directional profile.
        /// </summary>
        public FoxRunEncoding Encoding { get; set; }

        /// <summary>Optional transport-neutral reliability intent.</summary>
        public FoxRunDeliveryReliability Reliability { get; set; }

        /// <summary>Optional transport-neutral durability intent.</summary>
        public FoxRunDeliveryDurability Durability { get; set; }

        /// <summary>Optional transport-neutral history intent.</summary>
        public FoxRunDeliveryHistory History { get; set; }

        /// <summary>
        /// Optional positive Keep Last depth. It is invalid when the resolved
        /// history is Keep All or System Default.
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// Optional pinned Protobuf field number for this member. Zero uses the
        /// generated stable field number.
        /// </summary>
        public int ProtobufFieldNumber { get; set; }

        /// <summary>Tolerance for supported numeric change detection. Negative values are treated as zero.</summary>
        public float Tolerance { get; set; } = 0f;

        /// <summary>
        /// Optional bool field, property, or zero-argument method that must be
        /// true before the declaration may cross its Unity boundary.
        /// </summary>
        public string OnlyIf { get; set; } = string.Empty;

        /// <summary>Create a FoxRun attribute for the given Foxglove topic.</summary>
        public FoxRunAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
