// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Raw U2R2 health ping/pong codec that permits zero payload.

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Parsed U2R2 health response returned by the ROS2 Bridge sidecar.</summary>
    public sealed class Ros2BridgeHealthPong
    {
        public Ros2BridgeHealthPong(
            string requestId,
            int protocolVersion,
            string status,
            string sidecarName,
            string sidecarVersion,
            string errorCode,
            string message)
        {
            RequestId = requestId ?? string.Empty;
            ProtocolVersion = protocolVersion;
            Status = status ?? string.Empty;
            SidecarName = sidecarName ?? string.Empty;
            SidecarVersion = sidecarVersion ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string RequestId { get; }
        public int ProtocolVersion { get; }
        public string Status { get; }
        public string SidecarName { get; }
        public string SidecarVersion { get; }
        public string ErrorCode { get; }
        public string Message { get; }
    }

    /// <summary>Encodes and decodes zero-payload U2R2 health ping/pong frames.</summary>
    public static class Ros2BridgeU2R2HealthCodec
    {
        /// <summary>Application-level health protocol version inside the U2R2 frame header.</summary>
        public const int ProtocolVersion = 1;
        private const ushort EnvelopeVersion = 1;

        public static byte[] WriteHealthPing(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException("Health requestId must be non-empty.", nameof(requestId));

            var header = new JObject
            {
                ["op"] = "health_ping",
                ["requestId"] = requestId,
                ["protocolVersion"] = ProtocolVersion
            };
            return WriteRawFrame(header, Array.Empty<byte>());
        }

        public static byte[] WriteHealthPongForTests(
            string requestId,
            string status = "ok",
            int protocolVersion = ProtocolVersion,
            string sidecarName = "unity2foxglove_ros2_bridge",
            string sidecarVersion = "0.1.0",
            string errorCode = "",
            string message = "")
        {
            var header = new JObject
            {
                ["op"] = "health_pong",
                ["requestId"] = requestId,
                ["protocolVersion"] = protocolVersion,
                ["status"] = status
            };
            if (!string.IsNullOrEmpty(sidecarName))
                header["sidecarName"] = sidecarName;
            if (!string.IsNullOrEmpty(sidecarVersion))
                header["sidecarVersion"] = sidecarVersion;
            if (!string.IsNullOrEmpty(errorCode))
                header["errorCode"] = errorCode;
            if (!string.IsNullOrEmpty(message))
                header["message"] = message;
            return WriteRawFrame(header, Array.Empty<byte>());
        }

        public static Ros2BridgeHealthPong ParseHealthPong(byte[] frame, string expectedRequestId)
        {
            var header = ReadRawFrameHeader(frame, out var payloadLength);
            if (payloadLength != 0)
                throw new FormatException("Health pong payload must be empty.");

            var op = header.Value<string>("op");
            if (!string.Equals(op, "health_pong", StringComparison.Ordinal))
                throw new FormatException("Health response op must be health_pong.");

            var requestId = header.Value<string>("requestId");
            if (string.IsNullOrEmpty(requestId))
                throw new FormatException("Health pong missing requestId.");
            if (!string.Equals(requestId, expectedRequestId, StringComparison.Ordinal))
                throw new FormatException("Health pong requestId does not match.");

            var protocolVersion = header.Value<int?>("protocolVersion");
            if (!protocolVersion.HasValue)
                throw new FormatException("Health pong missing protocolVersion.");
            if (protocolVersion.Value != ProtocolVersion)
                throw new FormatException("Health pong protocolVersion does not match.");

            var status = header.Value<string>("status");
            if (!string.Equals(status, "ok", StringComparison.Ordinal)
                && !string.Equals(status, "error", StringComparison.Ordinal))
            {
                throw new FormatException("Health pong status must be ok or error.");
            }

            var sidecarName = header.Value<string>("sidecarName") ?? string.Empty;
            var sidecarVersion = header.Value<string>("sidecarVersion") ?? string.Empty;
            var errorCode = header.Value<string>("errorCode") ?? string.Empty;
            var message = header.Value<string>("message") ?? string.Empty;

            if (string.Equals(status, "ok", StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(sidecarName) || string.IsNullOrWhiteSpace(sidecarVersion)))
                throw new FormatException("Health pong ok response requires sidecarName and sidecarVersion.");
            if (string.Equals(status, "error", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(errorCode))
                throw new FormatException("Health pong error response requires errorCode.");

            return new Ros2BridgeHealthPong(
                requestId,
                protocolVersion.Value,
                status,
                sidecarName,
                sidecarVersion,
                errorCode,
                message);
        }

        private static byte[] WriteRawFrame(JObject header, byte[] payload)
        {
            var headerJson = JsonConvert.SerializeObject(header, Formatting.None);
            var headerBytes = Encoding.UTF8.GetBytes(headerJson);
            if (headerBytes.Length == 0 || headerBytes.Length > Ros2BridgeFrameWriter.MaxHeaderBytes)
                throw new ArgumentException("U2R2 health JSON header length is invalid.", nameof(header));
            payload ??= Array.Empty<byte>();
            if (payload.Length > Ros2BridgeFrameWriter.MaxPayloadBytes)
                throw new ArgumentException("U2R2 health payload exceeds the maximum.", nameof(payload));

            using var stream = new MemoryStream(16 + headerBytes.Length + payload.Length);
            stream.WriteByte((byte)'U');
            stream.WriteByte((byte)'2');
            stream.WriteByte((byte)'R');
            stream.WriteByte((byte)'2');
            WriteUInt16LE(stream, EnvelopeVersion);
            WriteUInt16LE(stream, 0);
            WriteUInt32LE(stream, checked((uint)headerBytes.Length));
            WriteUInt32LE(stream, checked((uint)payload.Length));
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(payload, 0, payload.Length);
            return stream.ToArray();
        }

        private static JObject ReadRawFrameHeader(byte[] frame, out uint payloadLength)
        {
            if (frame == null || frame.Length < 16)
                throw new FormatException("U2R2 frame is too short.");
            if (frame[0] != 'U' || frame[1] != '2' || frame[2] != 'R' || frame[3] != '2')
                throw new FormatException("U2R2 frame magic is invalid.");
            if (ReadUInt16LE(frame, 4) != EnvelopeVersion)
                throw new FormatException("U2R2 frame version is unsupported.");
            if (ReadUInt16LE(frame, 6) != 0)
                throw new FormatException("U2R2 frame flags must be zero.");

            var headerLength = ReadUInt32LE(frame, 8);
            payloadLength = ReadUInt32LE(frame, 12);
            if (headerLength == 0 || headerLength > Ros2BridgeFrameWriter.MaxHeaderBytes)
                throw new FormatException("U2R2 JSON header length is invalid.");
            if (payloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes)
                throw new FormatException("U2R2 payload length exceeds maximum.");

            var expected = checked(16 + (int)headerLength + (int)payloadLength);
            if (frame.Length != expected)
                throw new FormatException("U2R2 frame length does not match header.");

            var headerJson = Encoding.UTF8.GetString(frame, 16, checked((int)headerLength));
            try
            {
                return JObject.Parse(headerJson);
            }
            catch (JsonException ex)
            {
                throw new FormatException("U2R2 JSON header is malformed: " + ex.Message, ex);
            }
        }

        private static ushort ReadUInt16LE(byte[] data, int offset)
            => (ushort)(data[offset] | (data[offset + 1] << 8));

        private static uint ReadUInt32LE(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));

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
    }
}
