// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Deterministic U2R2 binary frame writer for the experimental ROS2 bridge.

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    internal readonly struct Ros2BridgeFrameMeasurement
    {
        internal Ros2BridgeFrameMeasurement(
            Ros2BridgeFrame frame,
            string headerJson,
            int headerBytes,
            int payloadBytes,
            int totalWireBytes)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            HeaderJson = headerJson
                ?? throw new ArgumentNullException(nameof(headerJson));
            HeaderBytes = headerBytes;
            PayloadBytes = payloadBytes;
            TotalWireBytes = totalWireBytes;
        }

        internal Ros2BridgeFrame Frame { get; }
        internal string HeaderJson { get; }
        internal int HeaderBytes { get; }
        internal int PayloadBytes { get; }
        internal int TotalWireBytes { get; }
    }

    /// <summary>Encodes <see cref="Ros2BridgeFrame"/> values to the Phase 94 U2R2 wire frame.</summary>
    public static class Ros2BridgeFrameWriter
    {
        /// <summary>Maximum JSON header size in bytes for one U2R2 frame.</summary>
        public const int MaxHeaderBytes = 64 * 1024;
        /// <summary>Maximum CDR payload size in bytes for one U2R2 frame.</summary>
        public const int MaxPayloadBytes = 64 * 1024 * 1024;

        private static readonly byte[] FramePrefix =
        {
            (byte)'U', (byte)'2', (byte)'R', (byte)'2',
            1, 0,
            0, 0
        };

        [ThreadStatic]
        private static byte[] _fixedHeaderBuffer;

        public static byte[] Write(Ros2BridgeFrame frame)
        {
            var measurement = Measure(frame);
            return EncodeOwned(frame, measurement);
        }

        internal static void Write(Ros2BridgeFrame frame, Stream destination)
        {
            var measurement = Measure(frame);
            var headerBytes = Encoding.UTF8.GetBytes(
                measurement.HeaderJson);
            if (headerBytes.Length != measurement.HeaderBytes)
            {
                throw new InvalidOperationException(
                    "ROS 2 bridge JSON header measurement changed during encoding.");
            }
            Write(frame, destination, headerBytes);
        }

        internal static Ros2BridgeFrameMeasurement Measure(
            Ros2BridgeFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (frame.PayloadLength > MaxPayloadBytes)
            {
                throw new ArgumentException(
                    $"ROS 2 bridge payload is {frame.PayloadLength} bytes, exceeding the {MaxPayloadBytes} byte maximum.",
                    nameof(frame));
            }

            var header = new FrameHeader
            {
                Op = "publish",
                Topic = frame.Topic,
                SchemaName = frame.SchemaName,
                Encoding = frame.Encoding,
                LogTimeNs = frame.LogTimeNs,
                Sequence = frame.Sequence
            };
            if (frame.Qos.HasValue)
            {
                var qos = frame.Qos.Value;
                header.ProfileName = ProfileWireValue(qos.Profile);
                header.Qos = new FrameQos
                {
                    Profile = ProfileWireValue(qos.Profile),
                    Reliability = ReliabilityWireValue(qos.Reliability),
                    Durability = DurabilityWireValue(qos.Durability),
                    History = HistoryWireValue(qos.History),
                    Depth = qos.Depth
                };
            }

            var headerJson = JsonConvert.SerializeObject(header, Formatting.None);
            var headerBytes = Encoding.UTF8.GetByteCount(headerJson);
            if (headerBytes > MaxHeaderBytes)
            {
                throw new ArgumentException(
                    $"ROS 2 bridge JSON header is {headerBytes} bytes, exceeding the {MaxHeaderBytes} byte maximum.",
                    nameof(frame));
            }

            return new Ros2BridgeFrameMeasurement(
                frame,
                headerJson,
                headerBytes,
                frame.PayloadLength,
                checked(16 + headerBytes + frame.PayloadLength));
        }

        internal static byte[] EncodeOwned(
            Ros2BridgeFrame frame,
            Ros2BridgeFrameMeasurement measurement)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (!ReferenceEquals(frame, measurement.Frame))
            {
                throw new ArgumentException(
                    "The ROS 2 bridge frame measurement belongs to a different frame.",
                    nameof(measurement));
            }
            if (measurement.HeaderJson == null
                || measurement.HeaderBytes < 0
                || measurement.PayloadBytes != frame.PayloadLength
                || measurement.TotalWireBytes
                != checked(
                    16
                    + measurement.HeaderBytes
                    + measurement.PayloadBytes))
            {
                throw new ArgumentException(
                    "The ROS 2 bridge frame measurement is invalid.",
                    nameof(measurement));
            }

            var buffer = new byte[measurement.TotalWireBytes];
            Buffer.BlockCopy(
                FramePrefix,
                0,
                buffer,
                0,
                FramePrefix.Length);
            WriteUInt32LE(
                buffer,
                8,
                checked((uint)measurement.HeaderBytes));
            WriteUInt32LE(
                buffer,
                12,
                checked((uint)measurement.PayloadBytes));
            var encodedHeaderBytes = Encoding.UTF8.GetBytes(
                measurement.HeaderJson,
                0,
                measurement.HeaderJson.Length,
                buffer,
                16);
            if (encodedHeaderBytes != measurement.HeaderBytes)
            {
                throw new InvalidOperationException(
                    "ROS 2 bridge JSON header measurement changed during encoding.");
            }

            var payloadOffset = checked(16 + encodedHeaderBytes);
            using var stream = new MemoryStream(
                buffer,
                0,
                buffer.Length,
                writable: true,
                publiclyVisible: true);
            stream.Position = payloadOffset;
            frame.WritePayloadTo(stream);
            if (stream.Position != buffer.Length)
            {
                throw new InvalidOperationException(
                    "ROS 2 bridge frame writer produced an unexpected byte count.");
            }
            return buffer;
        }

        private static void Write(Ros2BridgeFrame frame, Stream destination, byte[] headerBytes)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            var fixedHeader = GetFixedHeaderBuffer();
            WriteUInt32LE(fixedHeader, 8, checked((uint)headerBytes.Length));
            WriteUInt32LE(fixedHeader, 12, checked((uint)frame.PayloadLength));
            destination.Write(fixedHeader, 0, fixedHeader.Length);
            destination.Write(headerBytes, 0, headerBytes.Length);
            frame.WritePayloadTo(destination);
        }

        private static byte[] GetFixedHeaderBuffer()
        {
            var buffer = _fixedHeaderBuffer;
            if (buffer == null)
            {
                buffer = new byte[16];
                Buffer.BlockCopy(FramePrefix, 0, buffer, 0, FramePrefix.Length);
                _fixedHeaderBuffer = buffer;
            }

            return buffer;
        }

        private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xff);
            buffer[offset + 1] = (byte)((value >> 8) & 0xff);
            buffer[offset + 2] = (byte)((value >> 16) & 0xff);
            buffer[offset + 3] = (byte)((value >> 24) & 0xff);
        }

        private sealed class FrameHeader
        {
            [JsonProperty("op", Order = 0)]
            public string Op { get; set; }

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
            [JsonProperty("profile", Order = 1)]
            public string Profile { get; set; }

            [JsonProperty("reliability", Order = 2)]
            public string Reliability { get; set; }

            [JsonProperty("durability", Order = 3)]
            public string Durability { get; set; }

            [JsonProperty("history", Order = 4)]
            public string History { get; set; }

            [JsonProperty("depth", Order = 5)]
            public int Depth { get; set; }
        }

        internal static string ProfileWireValue(FoxRunQosProfile value)
        {
            switch (value)
            {
                case FoxRunQosProfile.Default:
                    return "default";
                case FoxRunQosProfile.SensorData:
                    return "sensor_data";
                case FoxRunQosProfile.SystemDefault:
                    return "system_default";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown ROS 2 QoS profile.");
            }
        }

        internal static string ReliabilityWireValue(FoxRunQosReliability value)
        {
            switch (value)
            {
                case FoxRunQosReliability.SystemDefault:
                    return "system_default";
                case FoxRunQosReliability.Reliable:
                    return "reliable";
                case FoxRunQosReliability.BestEffort:
                    return "best_effort";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown ROS 2 QoS reliability.");
            }
        }

        internal static string DurabilityWireValue(FoxRunQosDurability value)
        {
            switch (value)
            {
                case FoxRunQosDurability.SystemDefault:
                    return "system_default";
                case FoxRunQosDurability.Volatile:
                    return "volatile";
                case FoxRunQosDurability.TransientLocal:
                    return "transient_local";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown ROS 2 QoS durability.");
            }
        }

        internal static string HistoryWireValue(FoxRunQosHistory value)
        {
            switch (value)
            {
                case FoxRunQosHistory.SystemDefault:
                    return "system_default";
                case FoxRunQosHistory.KeepLast:
                    return "keep_last";
                case FoxRunQosHistory.KeepAll:
                    return "keep_all";
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown ROS 2 QoS history.");
            }
        }
    }
}
