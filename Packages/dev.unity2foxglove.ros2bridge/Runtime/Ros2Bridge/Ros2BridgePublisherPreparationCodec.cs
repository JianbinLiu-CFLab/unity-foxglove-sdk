// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Strict correlated U2R2 per-publisher preparation handshake.

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    internal enum Ros2BridgePublisherReadiness
    {
        Pending = 0,
        Ready = 1,
        Rejected = 2
    }

    internal sealed class Ros2BridgePublisherPreparationRequest
    {
        internal Ros2BridgePublisherPreparationRequest(
            string requestId,
            int protocolVersion,
            string topic,
            string schemaName,
            string encoding,
            FoxRunResolvedQos qos)
        {
            RequestId = requestId;
            ProtocolVersion = protocolVersion;
            Topic = topic;
            SchemaName = schemaName;
            Encoding = encoding;
            Qos = qos;
        }

        internal string RequestId { get; }
        internal int ProtocolVersion { get; }
        internal string Topic { get; }
        internal string SchemaName { get; }
        internal string Encoding { get; }
        internal FoxRunResolvedQos Qos { get; }
    }

    internal sealed class Ros2BridgePublisherPreparationResponse
    {
        internal Ros2BridgePublisherPreparationResponse(
            string requestId,
            string status,
            string errorCode,
            string message)
        {
            RequestId = requestId;
            Status = status;
            ErrorCode = errorCode;
            Message = message;
        }

        internal string RequestId { get; }
        internal string Status { get; }
        internal string ErrorCode { get; }
        internal string Message { get; }
    }

    internal static class Ros2BridgePublisherPreparationCodec
    {
        internal const int ProtocolVersion = 1;
        internal const int MaxDiagnosticChars = 512;
        private const ushort EnvelopeVersion = 1;

        internal static byte[] WriteRequest(
            string requestId,
            string topic,
            string schemaName,
            FoxRunResolvedQos qos)
        {
            ValidateRequestId(requestId);
            ValidateContract(topic, schemaName, qos);
            var header = new JObject
            {
                ["op"] = "prepare_publisher",
                ["requestId"] = requestId,
                ["protocolVersion"] = ProtocolVersion,
                ["topic"] = topic,
                ["schemaName"] = schemaName,
                ["encoding"] = Ros2BridgeFrame.CdrEncoding,
                ["qos"] = new JObject
                {
                    ["profile"] = Ros2BridgeFrameWriter.ProfileWireValue(qos.Profile),
                    ["reliability"] = Ros2BridgeFrameWriter.ReliabilityWireValue(qos.Reliability),
                    ["durability"] = Ros2BridgeFrameWriter.DurabilityWireValue(qos.Durability),
                    ["history"] = Ros2BridgeFrameWriter.HistoryWireValue(qos.History),
                    ["depth"] = qos.Depth
                }
            };
            return WriteRawFrame(header);
        }

        internal static Ros2BridgePublisherPreparationRequest ParseRequest(byte[] frame)
        {
            var header = ReadRawFrameHeader(frame);
            if (!string.Equals(RequiredString(header, "op"), "prepare_publisher", StringComparison.Ordinal))
                throw new FormatException("Publisher preparation op must be prepare_publisher.");

            var requestId = RequiredString(header, "requestId");
            var protocolVersion = RequiredInt32(header, "protocolVersion");
            if (protocolVersion != ProtocolVersion)
                throw new FormatException("Publisher preparation protocolVersion is unsupported.");
            var topic = RequiredString(header, "topic");
            var schemaName = RequiredString(header, "schemaName");
            var encoding = RequiredString(header, "encoding");
            if (!string.Equals(encoding, Ros2BridgeFrame.CdrEncoding, StringComparison.Ordinal))
                throw new FormatException("Publisher preparation encoding must be cdr.");
            var qosObject = header["qos"] as JObject
                            ?? throw new FormatException("Publisher preparation qos is required.");
            var qos = ParseQos(qosObject);
            ValidateRequestId(requestId);
            ValidateContract(topic, schemaName, qos);
            return new Ros2BridgePublisherPreparationRequest(
                requestId,
                protocolVersion,
                topic,
                schemaName,
                encoding,
                qos);
        }

        internal static Ros2BridgePublisherPreparationResponse ParseResponse(
            byte[] frame,
            string expectedRequestId)
        {
            var header = ReadRawFrameHeader(frame);
            if (!string.Equals(RequiredString(header, "op"), "publisher_ready", StringComparison.Ordinal))
                throw new FormatException("Publisher preparation response op must be publisher_ready.");
            var requestId = RequiredString(header, "requestId");
            if (!string.Equals(requestId, expectedRequestId, StringComparison.Ordinal))
                throw new FormatException("Publisher preparation response requestId does not match.");
            if (RequiredInt32(header, "protocolVersion") != ProtocolVersion)
                throw new FormatException("Publisher preparation response protocolVersion does not match.");
            var status = RequiredString(header, "status");
            if (!string.Equals(status, "ok", StringComparison.Ordinal)
                && !string.Equals(status, "error", StringComparison.Ordinal))
            {
                throw new FormatException("Publisher preparation status must be ok or error.");
            }
            var errorCode = OptionalString(header, "errorCode");
            var message = OptionalString(header, "message");
            if (string.Equals(status, "error", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(errorCode))
            {
                throw new FormatException("Rejected publisher preparation requires errorCode.");
            }
            return new Ros2BridgePublisherPreparationResponse(
                requestId,
                status,
                BoundDiagnostic(errorCode, 128),
                BoundDiagnostic(message, MaxDiagnosticChars));
        }

        internal static byte[] WriteResponseForTests(
            string requestId,
            string status,
            string errorCode = "",
            string message = "")
        {
            var header = new JObject
            {
                ["op"] = "publisher_ready",
                ["requestId"] = requestId,
                ["protocolVersion"] = ProtocolVersion,
                ["status"] = status
            };
            if (!string.IsNullOrWhiteSpace(errorCode))
                header["errorCode"] = errorCode;
            if (!string.IsNullOrWhiteSpace(message))
                header["message"] = message;
            return WriteRawFrame(header);
        }

        internal static byte[] ReadFrame(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            var fixedHeader = ReadExact(stream, 16);
            ValidateFixedHeader(fixedHeader);
            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            if (headerLength == 0 || headerLength > Ros2BridgeFrameWriter.MaxHeaderBytes)
                throw new FormatException("U2R2 publisher response header length is invalid.");
            if (payloadLength != 0)
                throw new FormatException("U2R2 publisher response payload must be empty.");
            var frame = new byte[checked(16 + (int)headerLength)];
            Buffer.BlockCopy(fixedHeader, 0, frame, 0, fixedHeader.Length);
            var header = ReadExact(stream, checked((int)headerLength));
            Buffer.BlockCopy(header, 0, frame, 16, header.Length);
            return frame;
        }

        private static byte[] WriteRawFrame(JObject header)
        {
            var headerBytes = Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(header, Formatting.None));
            if (headerBytes.Length == 0
                || headerBytes.Length > Ros2BridgeFrameWriter.MaxHeaderBytes)
            {
                throw new ArgumentException("U2R2 publisher preparation header length is invalid.");
            }
            var frame = new byte[16 + headerBytes.Length];
            frame[0] = (byte)'U';
            frame[1] = (byte)'2';
            frame[2] = (byte)'R';
            frame[3] = (byte)'2';
            WriteUInt16LE(frame, 4, EnvelopeVersion);
            WriteUInt32LE(frame, 8, checked((uint)headerBytes.Length));
            Buffer.BlockCopy(headerBytes, 0, frame, 16, headerBytes.Length);
            return frame;
        }

        private static JObject ReadRawFrameHeader(byte[] frame)
        {
            if (frame == null || frame.Length < 16)
                throw new FormatException("U2R2 publisher preparation frame is too short.");
            ValidateFixedHeader(frame);
            var headerLength = ReadUInt32LE(frame, 8);
            var payloadLength = ReadUInt32LE(frame, 12);
            if (headerLength == 0 || headerLength > Ros2BridgeFrameWriter.MaxHeaderBytes)
                throw new FormatException("U2R2 publisher preparation header length is invalid.");
            if (payloadLength != 0)
                throw new FormatException("U2R2 publisher preparation payload must be empty.");
            if (frame.Length != checked(16 + (int)headerLength))
                throw new FormatException("U2R2 publisher preparation frame length is invalid.");
            try
            {
                return JObject.Parse(Encoding.UTF8.GetString(frame, 16, (int)headerLength));
            }
            catch (JsonException exception)
            {
                throw new FormatException(
                    "U2R2 publisher preparation JSON is malformed: " + exception.Message,
                    exception);
            }
        }

        private static void ValidateFixedHeader(byte[] frame)
        {
            if (frame[0] != 'U' || frame[1] != '2' || frame[2] != 'R' || frame[3] != '2')
                throw new FormatException("U2R2 publisher preparation magic is invalid.");
            if ((ushort)(frame[4] | (frame[5] << 8)) != EnvelopeVersion)
                throw new FormatException("U2R2 publisher preparation envelope version is unsupported.");
            if (frame[6] != 0 || frame[7] != 0)
                throw new FormatException("U2R2 publisher preparation flags must be zero.");
        }

        internal static void ValidateContract(
            string topic,
            string schemaName,
            FoxRunResolvedQos qos)
        {
            if (string.IsNullOrWhiteSpace(topic)
                || !topic.StartsWith("/", StringComparison.Ordinal)
                || !Ros2BridgeTopicProfile.IsValidRos2TopicName(topic))
            {
                throw new ArgumentException("Publisher preparation topic is invalid.", nameof(topic));
            }
            if (!FoxRunRos2InterfaceIdentity.IsValidCanonicalRosMessageType(schemaName))
                throw new ArgumentException("Publisher preparation schemaName is invalid.", nameof(schemaName));
            if (!Ros2BridgeFrame.IsValidResolvedQos(qos))
                throw new ArgumentException("Publisher preparation QoS is invalid.", nameof(qos));
        }

        internal static bool TryValidateCompleteRequest(
            string topic,
            string schemaName,
            FoxRunResolvedQos qos,
            out string reason)
        {
            try
            {
                _ = WriteRequest(
                    "u2r2-prepare-00000000000000000000000000000000",
                    topic,
                    schemaName,
                    qos);
                reason = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is FormatException
                || exception is OverflowException
                || exception is JsonException)
            {
                reason = BoundDiagnostic(exception.Message, MaxDiagnosticChars);
                return false;
            }
        }

        private static void ValidateRequestId(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId)
                || requestId.IndexOf('\r') >= 0
                || requestId.IndexOf('\n') >= 0)
            {
                throw new ArgumentException(
                    "Publisher preparation requestId is invalid.",
                    nameof(requestId));
            }
        }

        private static FoxRunResolvedQos ParseQos(JObject qos)
            => new FoxRunResolvedQos(
                ParseProfile(RequiredString(qos, "profile")),
                ParseReliability(RequiredString(qos, "reliability")),
                ParseDurability(RequiredString(qos, "durability")),
                ParseHistory(RequiredString(qos, "history")),
                RequiredInt32(qos, "depth"));

        private static FoxRunQosProfile ParseProfile(string value)
            => value == "default" ? FoxRunQosProfile.Default
                : value == "sensor_data" ? FoxRunQosProfile.SensorData
                : value == "system_default" ? FoxRunQosProfile.SystemDefault
                : throw new FormatException("Publisher preparation qos.profile is invalid.");

        private static FoxRunQosReliability ParseReliability(string value)
            => value == "reliable" ? FoxRunQosReliability.Reliable
                : value == "best_effort" ? FoxRunQosReliability.BestEffort
                : value == "system_default" ? FoxRunQosReliability.SystemDefault
                : throw new FormatException("Publisher preparation qos.reliability is invalid.");

        private static FoxRunQosDurability ParseDurability(string value)
            => value == "volatile" ? FoxRunQosDurability.Volatile
                : value == "transient_local" ? FoxRunQosDurability.TransientLocal
                : value == "system_default" ? FoxRunQosDurability.SystemDefault
                : throw new FormatException("Publisher preparation qos.durability is invalid.");

        private static FoxRunQosHistory ParseHistory(string value)
            => value == "keep_last" ? FoxRunQosHistory.KeepLast
                : value == "keep_all" ? FoxRunQosHistory.KeepAll
                : value == "system_default" ? FoxRunQosHistory.SystemDefault
                : throw new FormatException("Publisher preparation qos.history is invalid.");

        private static string RequiredString(JObject value, string name)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.String)
                throw new FormatException("Publisher preparation " + name + " must be a string.");
            var result = (string)token;
            if (string.IsNullOrWhiteSpace(result))
                throw new FormatException("Publisher preparation " + name + " is required.");
            return result;
        }

        private static string OptionalString(JObject value, string name)
        {
            var token = value[name];
            if (token == null)
                return string.Empty;
            if (token.Type != JTokenType.String)
                throw new FormatException("Publisher preparation " + name + " must be a string.");
            return (string)token ?? string.Empty;
        }

        private static int RequiredInt32(JObject value, string name)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.Integer)
                throw new FormatException("Publisher preparation " + name + " must be an integer.");
            try
            {
                var number = token.Value<long>();
                if (number < int.MinValue || number > int.MaxValue)
                    throw new FormatException("Publisher preparation " + name + " is out of range.");
                return (int)number;
            }
            catch (OverflowException exception)
            {
                throw new FormatException(
                    "Publisher preparation " + name + " is out of range.",
                    exception);
            }
        }

        private static string BoundDiagnostic(string value, int maxChars)
        {
            value ??= string.Empty;
            return value.Length <= maxChars
                ? value
                : value.Substring(0, maxChars);
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            var bytes = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(bytes, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException("ROS2 Bridge sidecar closed during publisher preparation.");
                offset += read;
            }
            return bytes;
        }

        private static uint ReadUInt32LE(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));

        private static void WriteUInt16LE(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value & 0xff);
            data[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32LE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xff);
            data[offset + 1] = (byte)((value >> 8) & 0xff);
            data[offset + 2] = (byte)((value >> 16) & 0xff);
            data[offset + 3] = (byte)((value >> 24) & 0xff);
        }
    }
}
