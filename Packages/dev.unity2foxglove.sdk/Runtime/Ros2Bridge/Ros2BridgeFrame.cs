// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Immutable frame object for the experimental Unity-to-ROS2 bridge.

using System;
using System.IO;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Ros2Bridge
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
            Ros2BridgeQosProfile? qos)
        {
            if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException("ROS 2 bridge topic must be non-empty and start with '/'.", nameof(topic));
            if (string.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException("ROS 2 bridge schemaName must be non-empty.", nameof(schemaName));
            if (!FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out _))
                throw new ArgumentException("ROS 2 bridge schemaName must exist in the bundled ros2msg catalog: " + schemaName, nameof(schemaName));
            if (!string.Equals(encoding, CdrEncoding, StringComparison.Ordinal))
                throw new ArgumentException("ROS 2 bridge encoding must be exactly 'cdr'.", nameof(encoding));
            if (payload == null || payload.Length == 0)
                throw new ArgumentException("ROS 2 bridge payload must be non-empty.", nameof(payload));

            Topic = topic;
            SchemaName = schemaName;
            Encoding = encoding;
            LogTimeNs = logTimeNs;
            Sequence = sequence;
            _payload = (byte[])payload.Clone();
            Qos = qos;
            ProfileName = qos.HasValue ? qos.Value.PresetName : null;
        }

        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public ulong LogTimeNs { get; }
        public ulong Sequence { get; }
        public byte[] Payload => (byte[])_payload.Clone();
        public string ProfileName { get; }
        public Ros2BridgeQosProfile? Qos { get; }

        internal int PayloadLength => _payload.Length;

        internal void WritePayloadTo(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            stream.Write(_payload, 0, _payload.Length);
        }
    }
}
