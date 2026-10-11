// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Strict, canonical U2R2 envelope and v2 header codec.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public static partial class U2R2ProtocolCodec
    {
        private const int FixedHeaderBytes = 16;
        private static readonly byte[] Magic = { (byte)'U', (byte)'2', (byte)'R', (byte)'2' };
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly IComparer<string> Utf8ByteOrdinalComparer =
            Comparer<string>.Create(CompareUtf8ByteOrdinal);

        private static readonly IReadOnlyDictionary<string, U2R2Operation> Operations =
            new Dictionary<string, U2R2Operation>(StringComparer.Ordinal)
            {
                ["hello"] = U2R2Operation.Hello,
                ["hello_ack"] = U2R2Operation.HelloAck,
                ["health_ping"] = U2R2Operation.HealthPing,
                ["health_pong"] = U2R2Operation.HealthPong,
                ["prepare_publisher"] = U2R2Operation.PreparePublisher,
                ["publisher_ready"] = U2R2Operation.PublisherReady,
                ["publish"] = U2R2Operation.Publish,
                ["publish_result"] = U2R2Operation.PublishResult,
                ["register_subscription"] = U2R2Operation.RegisterSubscription,
                ["subscription_ready"] = U2R2Operation.SubscriptionReady,
                ["message"] = U2R2Operation.Message,
                ["unregister_subscription"] = U2R2Operation.UnregisterSubscription,
                ["subscription_removed"] = U2R2Operation.SubscriptionRemoved,
                ["busy"] = U2R2Operation.Busy,
                ["fault"] = U2R2Operation.Fault,
            };

        private static readonly IReadOnlyDictionary<string, StableErrorRule>
            StableErrors =
            new Dictionary<string, StableErrorRule>(StringComparer.Ordinal)
            {
                ["busy"] = new StableErrorRule(
                    terminal: true,
                    U2R2Operation.Busy),
                ["unsupported_protocol"] = new StableErrorRule(
                    terminal: true,
                    U2R2Operation.Fault),
                ["missing_capability"] = new StableErrorRule(
                    terminal: true,
                    U2R2Operation.Fault),
                ["invalid_frame"] = new StableErrorRule(
                    terminal: true,
                    U2R2Operation.Fault),
                ["invalid_contract"] = new StableErrorRule(
                    terminal: false,
                    U2R2Operation.PublisherReady,
                    U2R2Operation.PublishResult,
                    U2R2Operation.SubscriptionReady,
                    U2R2Operation.SubscriptionRemoved),
                ["contract_identity_mismatch"] = new StableErrorRule(
                    terminal: true),
                ["publisher_unavailable"] = new StableErrorRule(
                    terminal: false,
                    U2R2Operation.PublisherReady),
                ["invalid_request_id"] = new StableErrorRule(
                    terminal: true),
                ["request_id_exhausted"] = new StableErrorRule(
                    terminal: true),
                ["counter_exhausted"] = new StableErrorRule(
                    terminal: true),
                ["request_id_conflict"] = new StableErrorRule(
                    terminal: true,
                    U2R2Operation.Fault),
                ["response_mismatch"] = new StableErrorRule(
                    terminal: true),
                ["request_in_flight"] = new StableErrorRule(
                    terminal: false,
                    U2R2Operation.HealthPong,
                    U2R2Operation.PublisherReady,
                    U2R2Operation.PublishResult,
                    U2R2Operation.SubscriptionReady,
                    U2R2Operation.SubscriptionRemoved),
                ["stale_request"] = new StableErrorRule(
                    terminal: false,
                    U2R2Operation.HealthPong,
                    U2R2Operation.PublisherReady,
                    U2R2Operation.PublishResult,
                    U2R2Operation.SubscriptionReady,
                    U2R2Operation.SubscriptionRemoved),
                ["capacity_exceeded"] = new StableErrorRule(
                    terminal: false,
                    U2R2Operation.HealthPong,
                    U2R2Operation.PublisherReady,
                    U2R2Operation.PublishResult,
                    U2R2Operation.SubscriptionReady,
                    U2R2Operation.SubscriptionRemoved),
                ["contract_not_ready"] = new StableErrorRule(
                    terminal: true),
                ["unknown_contract"] = new StableErrorRule(
                    terminal: true,
                    U2R2Operation.SubscriptionRemoved,
                    U2R2Operation.Fault),
                ["contract_sequence_fault"] = new StableErrorRule(
                    terminal: false),
                ["contract_sequence_exhausted"] = new StableErrorRule(
                    terminal: false),
                ["invalid_configuration"] = new StableErrorRule(
                    terminal: true),
                ["dialect_downgrade"] = new StableErrorRule(
                    terminal: true),
                ["peer_closed"] = new StableErrorRule(
                    terminal: true),
                ["timeout"] = new StableErrorRule(
                    terminal: true),
            };

        public const int EnvelopeVersion = 1;
        public const int ProtocolVersion = 2;

        public static bool TryGetStableErrorTerminal(
            string errorCode,
            out bool terminal)
        {
            if (errorCode != null
                && StableErrors.TryGetValue(errorCode, out var rule))
            {
                terminal = rule.Terminal;
                return true;
            }
            terminal = false;
            return false;
        }

        public static bool IsStableErrorAllowedForResponse(
            string errorCode,
            U2R2Operation operation)
            => errorCode != null
               && StableErrors.TryGetValue(errorCode, out var rule)
               && rule.ResponseOperations.Contains(operation);

        private static void ValidateFixedHeaderLimit(
            U2R2ProtocolLimits limits)
        {
            if (limits.FixedFrameBytes != FixedHeaderBytes)
            {
                throw new U2R2ProtocolException(
                    "invalid_configuration",
                    "The U2R2 wire fixedFrameBytes value must be 16.",
                    terminal: true);
            }
        }

        public static byte[] EncodeFrame(JObject header, byte[] payload)
            => EncodeFrame(header, payload, U2R2ProtocolLimits.Default);

        public static byte[] EncodeFrame(
            JObject header,
            byte[] payload,
            U2R2ProtocolLimits limits)
        {
            if (header == null)
                throw new ArgumentNullException(nameof(header));
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));
            ValidateFixedHeaderLimit(limits);

            payload ??= Array.Empty<byte>();
            ValidateHeaderValueDomain(
                header,
                containerDepth: 1,
                limits.MaxJsonDepth);
            var canonicalHeader = SerializeCanonicalHeader(Canonicalize(header));
            Rfc8259JsonValidator.Validate(
                canonicalHeader,
                limits.MaxJsonDepth);
            byte[] headerBytes;
            try
            {
                headerBytes = StrictUtf8.GetBytes(canonicalHeader);
            }
            catch (EncoderFallbackException exception)
            {
                throw InvalidFrame("The U2R2 header is not valid UTF-8.", exception);
            }

            if (headerBytes.Length == 0
                || checked((ulong)headerBytes.LongLength) > limits.MaxHeaderBytes)
                throw InvalidFrame("The U2R2 JSON header length is out of range.");
            if (checked((ulong)payload.LongLength) > limits.MaxPayloadBytes)
                throw InvalidFrame("The U2R2 payload length is out of range.");

            var frame = new byte[checked(FixedHeaderBytes + headerBytes.Length + payload.Length)];
            Buffer.BlockCopy(Magic, 0, frame, 0, Magic.Length);
            frame[4] = EnvelopeVersion;
            WriteUInt32(frame, 8, checked((uint)headerBytes.Length));
            WriteUInt32(frame, 12, checked((uint)payload.Length));
            Buffer.BlockCopy(headerBytes, 0, frame, FixedHeaderBytes, headerBytes.Length);
            Buffer.BlockCopy(
                payload,
                0,
                frame,
                FixedHeaderBytes + headerBytes.Length,
                payload.Length);
            return frame;
        }

        public static U2R2Frame DecodeFrame(byte[] frame)
            => DecodeFrame(frame, U2R2ProtocolLimits.Default);

        public static U2R2Frame DecodeFrame(
            byte[] frame,
            U2R2ProtocolLimits limits)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));
            ValidateFixedHeaderLimit(limits);
            if (frame.Length < FixedHeaderBytes)
                throw InvalidFrame("The U2R2 frame is shorter than its fixed header.");

            for (var index = 0; index < Magic.Length; index++)
            {
                if (frame[index] != Magic[index])
                    throw InvalidFrame("The U2R2 frame magic is invalid.");
            }

            if (frame[4] != EnvelopeVersion || frame[5] != 0 || frame[6] != 0 || frame[7] != 0)
                throw InvalidFrame("The U2R2 envelope version or reserved flags are invalid.");

            var headerLength = ReadUInt32(frame, 8);
            var payloadLength = ReadUInt32(frame, 12);
            if (headerLength == 0 || headerLength > limits.MaxHeaderBytes)
                throw InvalidFrame("The U2R2 JSON header length is out of range.");
            if (payloadLength > limits.MaxPayloadBytes)
                throw InvalidFrame("The U2R2 payload length is out of range.");

            long expectedLength = FixedHeaderBytes + (long)headerLength + payloadLength;
            if (expectedLength != frame.Length)
                throw InvalidFrame("The U2R2 frame has truncated or trailing bytes.");

            string json;
            try
            {
                json = StrictUtf8.GetString(
                    frame,
                    FixedHeaderBytes,
                    checked((int)headerLength));
            }
            catch (DecoderFallbackException exception)
            {
                throw InvalidFrame("The U2R2 JSON header is not valid UTF-8.", exception);
            }

            var header = ParseStrictObject(json, limits.MaxJsonDepth);
            ValidateHeaderValueDomain(
                header,
                containerDepth: 1,
                limits.MaxJsonDepth);
            var payload = new byte[checked((int)payloadLength)];
            Buffer.BlockCopy(
                frame,
                FixedHeaderBytes + checked((int)headerLength),
                payload,
                0,
                payload.Length);
            return U2R2Frame.CreateOwned(header, payload);
        }

        public static U2R2Message ParseV2(U2R2Frame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var header = frame.Header;
            var operationName = RequiredString(header, "op");
            if (!Operations.TryGetValue(operationName, out var operation))
                throw InvalidFrame("The U2R2 operation is unknown.");

            var protocolVersion = RequiredUnsigned(header, "protocolVersion", allowZero: false);
            if (protocolVersion != ProtocolVersion)
            {
                throw new U2R2ProtocolException(
                    "unsupported_protocol",
                    "U2R2 protocolVersion 2 is required.");
            }

            var isRequest = U2R2OperationRules.IsRequest(operation);
            var isResponse = U2R2OperationRules.IsResponse(operation);
            var isEvent = operation == U2R2Operation.Message;
            if (!isRequest && !isResponse && !isEvent)
                throw InvalidFrame("The U2R2 operation kind is invalid.");
            var isErrorResponse =
                isResponse
                && header["status"]?.Type == JTokenType.String
                && string.Equals(
                    header.Value<string>("status"),
                    "error",
                    StringComparison.Ordinal);

            var requestId = 0UL;
            if (isRequest || isResponse)
            {
                requestId = RequiredUnsigned(header, "requestId", allowZero: false);
            }
            else if (header["requestId"] != null)
            {
                throw InvalidFrame(
                    "requestId is only valid on U2R2 requests and responses.");
            }

            var messageId = 0UL;
            var hasMessageId =
                operation == U2R2Operation.Publish
                || operation == U2R2Operation.PublishResult
                || operation == U2R2Operation.Message;
            if (hasMessageId && !isErrorResponse)
            {
                messageId = RequiredUnsigned(header, "messageId", allowZero: false);
            }
            else if (header["messageId"] != null)
            {
                throw InvalidFrame(
                    "messageId is not valid for this U2R2 operation.");
            }

            var sequence = 0UL;
            if (operation == U2R2Operation.Publish
                || operation == U2R2Operation.Message)
            {
                sequence = RequiredUnsigned(header, "sequence", allowZero: false);
            }
            else if (header["sequence"] != null)
            {
                throw InvalidFrame(
                    "sequence is only valid on a U2R2 data operation.");
            }

            var contractId = 0UL;
            var hasContractId =
                operation == U2R2Operation.RegisterSubscription
                || operation == U2R2Operation.SubscriptionReady
                || operation == U2R2Operation.Message
                || operation == U2R2Operation.UnregisterSubscription
                || operation == U2R2Operation.SubscriptionRemoved;
            if (hasContractId && !isErrorResponse)
            {
                contractId = RequiredUnsigned(header, "contractId", allowZero: false);
            }
            else if (header["contractId"] != null)
            {
                throw InvalidFrame(
                    "contractId is not valid for this U2R2 operation.");
            }

            var logTimeNs = 0UL;
            var receiveTimeNs = 0UL;
            var encoding = string.Empty;
            var representation = string.Empty;
            if (operation != U2R2Operation.Publish && header["logTimeNs"] != null)
                throw InvalidFrame("logTimeNs is only valid on a publish request.");
            if (operation != U2R2Operation.Message
                && (header["receiveTimeNs"] != null
                    || header["representation"] != null))
            {
                throw InvalidFrame(
                    "receiveTimeNs and representation are only valid on an inbound message.");
            }
            var mayDeclareEncoding =
                operation == U2R2Operation.PreparePublisher
                || operation == U2R2Operation.Publish
                || operation == U2R2Operation.RegisterSubscription
                || operation == U2R2Operation.Message;
            if (!mayDeclareEncoding && header["encoding"] != null)
                throw InvalidFrame("encoding is not valid for this U2R2 operation.");
            if (operation == U2R2Operation.Message)
                encoding = RequiredString(header, "encoding");
            else if (mayDeclareEncoding && header["encoding"]?.Type == JTokenType.String)
                encoding = header.Value<string>("encoding") ?? string.Empty;
            if (operation == U2R2Operation.Publish)
            {
                logTimeNs = RequiredUnsigned(header, "logTimeNs", allowZero: true);
            }
            else if (operation == U2R2Operation.Message)
            {
                receiveTimeNs = RequiredUnsigned(
                    header,
                    "receiveTimeNs",
                    allowZero: true);
                representation = RequiredString(header, "representation");
                if (!string.Equals(encoding, "cdr", StringComparison.Ordinal)
                    || !string.Equals(
                        representation,
                        "xcdr1-le",
                        StringComparison.Ordinal))
                {
                    throw InvalidFrame(
                        "An inbound message requires cdr with its encapsulation header.");
                }
            }

            if (operation == U2R2Operation.Hello
                && (header["sessionId"] != null
                    || header["connectionGeneration"] != null))
            {
                throw InvalidFrame(
                    "A client hello cannot provide sidecar-owned session identity.");
            }
            var sessionId = OptionalString(header, "sessionId");
            var connectionGeneration = OptionalUnsigned(
                header,
                "connectionGeneration",
                allowZero: false);
            var sessionRequired =
                operation != U2R2Operation.Hello
                && operation != U2R2Operation.Busy
                && operation != U2R2Operation.Fault;
            if (sessionRequired
                && (sessionId.Length == 0 || connectionGeneration == 0))
            {
                throw InvalidFrame(
                    "This U2R2 operation requires a sessionId and connectionGeneration.");
            }
            if ((sessionId.Length == 0) != (connectionGeneration == 0))
                throw InvalidFrame("U2R2 session identity fields must be present together.");

            var capabilities = ReadCapabilities(header, operation);
            U2R2CommandAdmission.ParseContract(
                header,
                operation,
                out var topic,
                out var schemaName,
                out var qos);
            ValidatePayload(operation, frame.Payload.Length);
            if (operation == U2R2Operation.Message)
                ValidateXcdr1LittleEndianPayload(frame.Payload);
            ReadResponseStatus(
                header,
                operation,
                isResponse,
                out var status,
                out var errorCode,
                out var errorMessage,
                out var terminal);
            return new U2R2Message(
                operation,
                operationName,
                isRequest,
                isResponse,
                terminal,
                requestId,
                messageId,
                sequence,
                contractId,
                sessionId,
                connectionGeneration,
                capabilities,
                status,
                errorCode,
                errorMessage,
                logTimeNs,
                receiveTimeNs,
                encoding,
                representation,
                topic,
                schemaName,
                qos);
        }

    }
}
