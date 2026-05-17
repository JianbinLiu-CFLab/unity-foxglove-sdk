// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Deterministic U2R2 binary frame writer for the experimental ROS2 bridge.

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Encodes <see cref="Ros2BridgeFrame"/> values to the Phase 94 U2R2 wire frame.</summary>
    public static class Ros2BridgeFrameWriter
    {
        public const int MaxHeaderBytes = 64 * 1024;
        public const int MaxPayloadBytes = 64 * 1024 * 1024;

        public static byte[] Write(Ros2BridgeFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.Payload.Length > MaxPayloadBytes)
                throw new ArgumentException("ROS 2 bridge payload exceeds the Phase 94 maximum.", nameof(frame));

            var header = new FrameHeader
            {
                Topic = frame.Topic,
                SchemaName = frame.SchemaName,
                Encoding = frame.Encoding,
                LogTimeNs = frame.LogTimeNs,
                Sequence = frame.Sequence
            };
            if (frame.Qos.HasValue)
            {
                var qos = frame.Qos.Value;
                header.ProfileName = frame.ProfileName;
                header.Qos = new FrameQos
                {
                    Reliability = qos.ReliabilityWireValue,
                    Durability = qos.DurabilityWireValue,
                    Depth = qos.Depth
                };
            }

            var headerJson = JsonConvert.SerializeObject(header, Formatting.None);
            var headerBytes = Encoding.UTF8.GetBytes(headerJson);
            if (headerBytes.Length > MaxHeaderBytes)
                throw new ArgumentException("ROS 2 bridge JSON header exceeds the Phase 94 maximum.", nameof(frame));

            using var stream = new MemoryStream(16 + headerBytes.Length + frame.Payload.Length);
            stream.WriteByte((byte)'U');
            stream.WriteByte((byte)'2');
            stream.WriteByte((byte)'R');
            stream.WriteByte((byte)'2');
            WriteUInt16LE(stream, 1);
            WriteUInt16LE(stream, 0);
            WriteUInt32LE(stream, checked((uint)headerBytes.Length));
            WriteUInt32LE(stream, checked((uint)frame.Payload.Length));
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(frame.Payload, 0, frame.Payload.Length);
            return stream.ToArray();
        }

        private static void WriteUInt16LE(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xff));
            stream.WriteByte((byte)((value >> 8) & 0xff));
        }

        private static void WriteUInt32LE(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xff));
            stream.WriteByte((byte)((value >> 8) & 0xff));
            stream.WriteByte((byte)((value >> 16) & 0xff));
            stream.WriteByte((byte)((value >> 24) & 0xff));
        }

        private sealed class FrameHeader
        {
            [JsonProperty("topic", Order = 1)]
            public string Topic { get; set; }

            [JsonProperty("schemaName", Order = 2)]
            public string SchemaName { get; set; }

            [JsonProperty("encoding", Order = 3)]
            public string Encoding { get; set; }

            [JsonProperty("logTimeNs", Order = 4)]
            public ulong LogTimeNs { get; set; }

            [JsonProperty("sequence", Order = 5)]
            public ulong Sequence { get; set; }

            [JsonProperty("profileName", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
            public string ProfileName { get; set; }

            [JsonProperty("qos", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
            public FrameQos Qos { get; set; }
        }

        private sealed class FrameQos
        {
            [JsonProperty("reliability", Order = 1)]
            public string Reliability { get; set; }

            [JsonProperty("durability", Order = 2)]
            public string Durability { get; set; }

            [JsonProperty("depth", Order = 3)]
            public int Depth { get; set; }
        }
    }
}
