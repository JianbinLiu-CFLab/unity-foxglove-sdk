// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Immutable frame object for the experimental Unity-to-ROS2 bridge.

using System;
using System.IO;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>One serialized ROS 2 bridge frame before TCP encoding.</summary>
    public sealed class Ros2BridgeFrame
    {
        /// <summary>Message encoding label used for ROS2 CDR payloads in U2R2 frames.</summary>
        public const string CdrEncoding = "cdr";

        private readonly byte[] _payload;

        public Ros2BridgeFrame(string topic, string schemaName, string encoding, ulong logTimeNs, ulong sequence, byte[] payload)
            : this(topic, schemaName, encoding, logTimeNs, sequence, payload, null)
        {
        }

        public Ros2BridgeFrame(
            string topic,
            string schemaName,
            string encoding,
            ulong logTimeNs,
            ulong sequence,
            byte[] payload,
            FoxRunResolvedQos? qos)
            : this(topic, schemaName, encoding, logTimeNs, sequence, payload, qos, clonePayload: true)
        {
        }

        internal static Ros2BridgeFrame CreateOwned(
            string topic,
            string schemaName,
            string encoding,
            ulong logTimeNs,
            ulong sequence,
            byte[] payload,
            FoxRunResolvedQos? qos = null)
            => new Ros2BridgeFrame(topic, schemaName, encoding, logTimeNs, sequence, payload, qos, clonePayload: false, validateSchema: false);

        internal static Ros2BridgeFrame CreateValidated(
            string topic,
            string schemaName,
            string encoding,
            ulong logTimeNs,
            ulong sequence,
            byte[] payload,
            FoxRunResolvedQos? qos = null)
            => new Ros2BridgeFrame(topic, schemaName, encoding, logTimeNs, sequence, payload, qos, clonePayload: true, validateSchema: false);

        private Ros2BridgeFrame(
            string topic,
            string schemaName,
            string encoding,
            ulong logTimeNs,
            ulong sequence,
            byte[] payload,
            FoxRunResolvedQos? qos,
            bool clonePayload)
            : this(topic, schemaName, encoding, logTimeNs, sequence, payload, qos, clonePayload, validateSchema: true)
        {
        }

        private Ros2BridgeFrame(
            string topic,
            string schemaName,
            string encoding,
            ulong logTimeNs,
            ulong sequence,
            byte[] payload,
            FoxRunResolvedQos? qos,
            bool clonePayload,
            bool validateSchema)
        {
            if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("ROS 2 bridge topic must be non-empty and start with '/'.", nameof(topic));
            if (topic.IndexOf('\r') >= 0 || topic.IndexOf('\n') >= 0)
                throw new ArgumentException("ROS 2 bridge topic must not contain newline characters.", nameof(topic));
            if (!Ros2BridgeTopicProfile.IsValidRos2TopicName(topic))
                throw new ArgumentException("ROS 2 bridge topic contains invalid ROS 2 characters.", nameof(topic));
            if (string.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException("ROS 2 bridge schemaName must be non-empty.", nameof(schemaName));
            if (!Ros2MessageTypeIdentity.IsValidCanonicalMessageType(schemaName))
                throw new ArgumentException(
                    "ROS 2 bridge schemaName must be an exact canonical package/msg/Message identity.",
                    nameof(schemaName));
            if (validateSchema && !FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out _))
            {
                throw new ArgumentException(
                    "ROS 2 bridge schemaName must exist in the bundled ros2msg catalog: " + schemaName,
                    nameof(schemaName));
            }
            if (!string.Equals(encoding, CdrEncoding, StringComparison.Ordinal))
                throw new ArgumentException("ROS 2 bridge encoding must be exactly 'cdr'.", nameof(encoding));
            if (payload == null || payload.Length == 0)
                throw new ArgumentException("ROS 2 bridge payload must be non-empty.", nameof(payload));
            if (qos.HasValue && !IsValidResolvedQos(qos.Value))
            {
                throw new ArgumentException(
                    "ROS 2 bridge QoS must be a fully resolved portable contract.",
                    nameof(qos));
            }

            Topic = topic;
            SchemaName = schemaName;
            Encoding = encoding;
            LogTimeNs = logTimeNs;
            Sequence = sequence;
            _payload = clonePayload ? (byte[])payload.Clone() : payload;
            Qos = qos;
        }

        internal static bool IsValidResolvedQos(FoxRunResolvedQos qos)
            => FoxRunResolvedQos.IsDefined(qos.Profile)
               && FoxRunResolvedQos.IsDefined(qos.Reliability)
               && FoxRunResolvedQos.IsDefined(qos.Durability)
               && FoxRunResolvedQos.IsDefined(qos.History)
               && (qos.History == FoxRunQosHistory.KeepLast
                   ? qos.Depth > 0
                   : qos.Depth == 0);

        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public ulong LogTimeNs { get; }
        public ulong Sequence { get; }
        /// <summary>Read-only view of the serialized payload without allocating a defensive copy.</summary>
        public ReadOnlyMemory<byte> PayloadMemory => _payload;
        [Obsolete("Payload returns a defensive copy on every call. Use PayloadMemory for a non-allocating read-only view, or cache Payload if a mutable copy is required.")]
        public byte[] Payload => (byte[])_payload.Clone();
        public FoxRunResolvedQos? Qos { get; }

        internal int PayloadLength => _payload.Length;

        internal void WritePayloadTo(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            stream.Write(_payload, 0, _payload.Length);
        }
    }
}
