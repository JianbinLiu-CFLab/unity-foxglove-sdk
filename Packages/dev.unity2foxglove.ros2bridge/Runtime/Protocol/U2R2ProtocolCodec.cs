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
    public static class U2R2ProtocolCodec
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
                    U2R2Operation.PublisherReady,
                    U2R2Operation.PublishResult,
                    U2R2Operation.SubscriptionReady,
                    U2R2Operation.SubscriptionRemoved),
                ["stale_request"] = new StableErrorRule(
                    terminal: false,
                    U2R2Operation.PublisherReady,
                    U2R2Operation.PublishResult,
                    U2R2Operation.SubscriptionReady,
                    U2R2Operation.SubscriptionRemoved),
                ["capacity_exceeded"] = new StableErrorRule(
                    terminal: false,
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

        public static void ValidateResponseCorrelation(
            U2R2ResponseExpectation expected,
            U2R2Message response)
        {
            if (expected == null)
                throw new ArgumentNullException(nameof(expected));
            if (response == null || !response.IsResponse)
                throw InvalidFrame("The correlated U2R2 frame is not a response.");
            if (!expected.AllowedResponseOperations.Contains(response.Operation))
            {
                throw new U2R2ProtocolException(
                    "response_mismatch",
                    "The U2R2 response operation is not valid for its request.");
            }

            var isSuccess =
                response.Operation == expected.SuccessResponseOperation
                && string.Equals(response.Status, "ok", StringComparison.Ordinal);
            bool identityMatches;
            if (expected.AssignsSessionIdentity)
            {
                identityMatches = isSuccess
                    ? response.SessionId.Length > 0
                      && response.ConnectionGeneration != 0
                    : response.SessionId.Length == 0
                      && response.ConnectionGeneration == 0;
            }
            else
            {
                identityMatches = string.Equals(
                                      response.SessionId,
                                      expected.SessionId,
                                      StringComparison.Ordinal)
                                  && response.ConnectionGeneration
                                  == expected.ConnectionGeneration;
            }
            var identifiersMatch = isSuccess
                ? response.ContractId == expected.ContractId
                  && response.MessageId == expected.MessageId
                : response.ContractId == 0 && response.MessageId == 0;
            if (response.RequestId != expected.RequestId
                || !identityMatches
                || !identifiersMatch)
            {
                throw new U2R2ProtocolException(
                    "response_mismatch",
                    "The U2R2 response does not match its exact request context.");
            }
        }

        internal static JObject ParseStrictV1Object(
            byte[] bytes,
            int offset,
            int count,
            string context)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 0 || offset > bytes.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            context = string.IsNullOrWhiteSpace(context)
                ? "U2R2 v1"
                : context;

            string json;
            try
            {
                json = StrictUtf8.GetString(bytes, offset, count);
            }
            catch (DecoderFallbackException exception)
            {
                throw new FormatException(
                    context + " JSON UTF-8 is invalid.",
                    exception);
            }

            try
            {
                return ParseStrictObject(
                    json,
                    U2R2ProtocolLimits.Default.MaxJsonDepth);
            }
            catch (U2R2ProtocolException exception)
            {
                throw new FormatException(
                    context + " JSON is invalid: " + exception.Message,
                    exception);
            }
        }

        private static JObject ParseStrictObject(
            string json,
            ulong maxJsonDepth)
        {
            try
            {
                Rfc8259JsonValidator.Validate(json, maxJsonDepth);
                using var textReader = new StringReader(json);
                using var reader = new JsonTextReader(textReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = maxJsonDepth > int.MaxValue
                        ? int.MaxValue
                        : checked((int)maxJsonDepth),
                    SupportMultipleContent = true,
                };
                if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                    throw InvalidFrame("The U2R2 JSON header must be an object.");

                var header = JObject.Load(
                    reader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    });
                if (reader.Read())
                    throw InvalidFrame("The U2R2 JSON header has trailing content.");
                return header;
            }
            catch (U2R2ProtocolException)
            {
                throw;
            }
            catch (JsonException exception)
                when (exception.Message.IndexOf(
                          "already exists",
                          StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw InvalidFrame(
                    "The U2R2 JSON header contains a duplicate property.",
                    exception);
            }
            catch (JsonException exception)
            {
                throw InvalidFrame("The U2R2 JSON header is invalid.", exception);
            }
        }

        private static JToken Canonicalize(JToken token)
        {
            if (token is JObject valueObject)
            {
                var result = new JObject();
                foreach (var property in valueObject.Properties()
                             .OrderBy(property => property.Name, Utf8ByteOrdinalComparer))
                {
                    result.Add(property.Name, Canonicalize(property.Value));
                }
                return result;
            }
            if (token is JArray valueArray)
                return new JArray(valueArray.Select(Canonicalize));
            return token.DeepClone();
        }

        private static string SerializeCanonicalHeader(JToken header)
        {
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new JsonTextWriter(text)
                   {
                       Formatting = Formatting.None,
                       StringEscapeHandling = StringEscapeHandling.EscapeNonAscii,
                       Culture = CultureInfo.InvariantCulture,
                   })
            {
                header.WriteTo(writer);
            }
            return text.ToString();
        }

        private static int CompareUtf8ByteOrdinal(string left, string right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            byte[] leftBytes;
            byte[] rightBytes;
            try
            {
                leftBytes = StrictUtf8.GetBytes(left);
                rightBytes = StrictUtf8.GetBytes(right);
            }
            catch (EncoderFallbackException exception)
            {
                throw InvalidFrame(
                    "The U2R2 header contains a property name that is not valid UTF-8.",
                    exception);
            }

            var commonLength = Math.Min(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < commonLength; index++)
            {
                var comparison = leftBytes[index].CompareTo(rightBytes[index]);
                if (comparison != 0)
                    return comparison;
            }
            return leftBytes.Length.CompareTo(rightBytes.Length);
        }

        private static IReadOnlyCollection<U2R2Capability> ReadCapabilities(
            JObject header,
            U2R2Operation operation)
        {
            if (operation != U2R2Operation.Hello && operation != U2R2Operation.HelloAck)
            {
                if (header["capabilities"] != null)
                    throw InvalidFrame("Capabilities are only valid during U2R2 hello.");
                return Array.Empty<U2R2Capability>();
            }

            if (!(header["capabilities"] is JArray values) || values.Count == 0)
                throw InvalidFrame("U2R2 hello requires capabilities.");

            var result = new HashSet<U2R2Capability>();
            foreach (var value in values)
            {
                if (value.Type != JTokenType.String)
                    throw InvalidFrame("A U2R2 capability must be a string.");
                U2R2Capability capability;
                switch (value.Value<string>())
                {
                    case "publish":
                        capability = U2R2Capability.Publish;
                        break;
                    case "subscribe":
                        capability = U2R2Capability.Subscribe;
                        break;
                    default:
                        throw InvalidFrame("The U2R2 capability is unknown.");
                }
                if (!result.Add(capability))
                    throw InvalidFrame("The U2R2 capability list contains duplicates.");
            }
            return result.ToArray();
        }

        private static void ValidatePayload(U2R2Operation operation, int length)
        {
            var carriesPayload =
                operation == U2R2Operation.Publish || operation == U2R2Operation.Message;
            if (carriesPayload && length == 0)
                throw InvalidFrame("The U2R2 data operation requires a payload.");
            if (!carriesPayload && length != 0)
                throw InvalidFrame("This U2R2 operation cannot carry a payload.");
        }

        private static void ValidateXcdr1LittleEndianPayload(
            ReadOnlyMemory<byte> payload)
        {
            if (payload.Length < 4)
            {
                throw InvalidFrame(
                    "An inbound xcdr1-le message requires a four-byte encapsulation header.");
            }
            var bytes = payload.Span;
            if (bytes[0] != 0
                || bytes[1] != 1
                || bytes[2] != 0
                || bytes[3] != 0)
            {
                throw InvalidFrame(
                    "An inbound xcdr1-le message has an invalid encapsulation header.");
            }
        }

        private static void ReadResponseStatus(
            JObject header,
            U2R2Operation operation,
            bool isResponse,
            out string status,
            out string errorCode,
            out string errorMessage,
            out bool terminal)
        {
            status = string.Empty;
            errorCode = string.Empty;
            errorMessage = string.Empty;
            terminal = false;
            if (!isResponse)
            {
                if (header["status"] != null
                    || header["errorCode"] != null
                    || header["message"] != null
                    || header["terminal"] != null)
                {
                    throw InvalidFrame(
                        "Only U2R2 responses may contain response metadata.");
                }
                return;
            }

            status = RequiredString(header, "status");
            if (!string.Equals(status, "ok", StringComparison.Ordinal)
                && !string.Equals(status, "error", StringComparison.Ordinal))
            {
                throw InvalidFrame("The U2R2 response status is invalid.");
            }
            if (string.Equals(status, "ok", StringComparison.Ordinal))
            {
                if (operation == U2R2Operation.Busy
                    || operation == U2R2Operation.Fault)
                {
                    throw InvalidFrame(
                        "Busy and fault responses must have error status.");
                }
                if (header["errorCode"] != null
                    || header["message"] != null
                    || header["terminal"] != null)
                {
                    throw InvalidFrame(
                        "A successful U2R2 response cannot contain error metadata.");
                }
                return;
            }

            errorCode = RequiredString(header, "errorCode");
            errorMessage = RequiredString(header, "message");
            terminal = RequiredBoolean(header, "terminal");
            if (!StableErrors.TryGetValue(errorCode, out var rule))
                throw InvalidFrame("The U2R2 response errorCode is unknown.");
            if (terminal != rule.Terminal)
            {
                throw InvalidFrame(
                    "The U2R2 response terminal classification does not match its errorCode.");
            }
            if (!rule.ResponseOperations.Contains(operation))
                throw InvalidFrame("The U2R2 errorCode is invalid for this response operation.");
        }

        private static void ValidateHeaderValueDomain(
            JToken token,
            ulong containerDepth,
            ulong maxJsonDepth)
        {
            if (token == null)
                throw InvalidFrame("The U2R2 JSON header is invalid.");

            switch (token.Type)
            {
                case JTokenType.Object:
                    if (containerDepth > maxJsonDepth)
                        throw InvalidFrame("The U2R2 JSON header exceeds its depth limit.");
                    foreach (var property in ((JObject)token).Properties())
                    {
                        ValidateUtf8Text(property.Name, "property name");
                        ValidateHeaderValueDomain(
                            property.Value,
                            checked(containerDepth + 1),
                            maxJsonDepth);
                    }
                    return;
                case JTokenType.Array:
                    if (containerDepth > maxJsonDepth)
                        throw InvalidFrame("The U2R2 JSON header exceeds its depth limit.");
                    foreach (var value in (JArray)token)
                        ValidateHeaderValueDomain(
                            value,
                            checked(containerDepth + 1),
                            maxJsonDepth);
                    return;
                case JTokenType.Integer:
                    ReadUnsigned(token, "JSON number");
                    return;
                case JTokenType.String:
                    ValidateUtf8Text(token.Value<string>(), "string value");
                    return;
                case JTokenType.Boolean:
                case JTokenType.Null:
                    return;
                default:
                    throw InvalidFrame(
                        "U2R2 JSON values must be strings, Booleans, null, "
                        + "containers, or unsigned 64-bit integers.");
            }
        }

        private static void ValidateUtf8Text(string value, string role)
        {
            try
            {
                StrictUtf8.GetByteCount(value ?? string.Empty);
            }
            catch (EncoderFallbackException exception)
            {
                throw InvalidFrame(
                    "The U2R2 header contains a " + role + " that is not valid UTF-8.",
                    exception);
            }
        }

        private static string RequiredString(JObject header, string name)
        {
            var value = OptionalString(header, name);
            if (IsEmptyOrAsciiWhitespace(value))
                throw InvalidFrame("The U2R2 " + name + " must be a nonempty string.");
            return value;
        }

        private static string OptionalString(JObject header, string name)
        {
            var token = header[name];
            if (token == null)
                return string.Empty;
            if (token.Type != JTokenType.String)
                throw InvalidFrame("The U2R2 " + name + " must be a string.");
            var value = token.Value<string>();
            if (value.Length > 0 && IsEmptyOrAsciiWhitespace(value))
                throw InvalidFrame("The U2R2 " + name + " cannot be whitespace.");
            return value;
        }

        private static bool RequiredBoolean(JObject header, string name)
        {
            var token = header[name];
            if (token == null)
                throw InvalidFrame("The U2R2 " + name + " is required.");
            if (token.Type != JTokenType.Boolean)
                throw InvalidFrame("The U2R2 " + name + " must be a Boolean.");
            return token.Value<bool>();
        }

        private static ulong RequiredUnsigned(
            JObject header,
            string name,
            bool allowZero)
        {
            if (header[name] == null)
                throw InvalidFrame("The U2R2 " + name + " is required.");
            var value = ReadUnsigned(header[name], name);
            if (!allowZero && value == 0)
            {
                var errorCode = string.Equals(name, "requestId", StringComparison.Ordinal)
                    ? "invalid_request_id"
                    : "invalid_frame";
                throw new U2R2ProtocolException(
                    errorCode,
                    "The U2R2 " + name + " must be nonzero.");
            }
            return value;
        }

        private static ulong OptionalUnsigned(
            JObject header,
            string name,
            bool allowZero)
        {
            if (header[name] == null)
                return 0;
            return RequiredUnsigned(header, name, allowZero);
        }

        private static ulong ReadUnsigned(JToken token, string name)
        {
            if (token.Type != JTokenType.Integer)
                throw InvalidFrame("The U2R2 " + name + " must be an unsigned integer.");
            try
            {
                var rawValue = ((JValue)token).Value;
                if (rawValue is BigInteger bigInteger)
                {
                    if (bigInteger < BigInteger.Zero
                        || bigInteger > new BigInteger(ulong.MaxValue))
                    {
                        throw new OverflowException();
                    }
                    return (ulong)bigInteger;
                }
                return Convert.ToUInt64(rawValue, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (
                exception is OverflowException
                || exception is InvalidCastException
                || exception is FormatException)
            {
                throw InvalidFrame("The U2R2 " + name + " must be an unsigned integer.", exception);
            }
        }

        private static U2R2ProtocolException InvalidFrame(
            string message,
            Exception innerException = null)
            => new U2R2ProtocolException("invalid_frame", message, true, innerException);

        private static bool IsEmptyOrAsciiWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;
            for (var index = 0; index < value.Length; index++)
            {
                switch (value[index])
                {
                    case ' ':
                    case '\t':
                    case '\r':
                    case '\n':
                        continue;
                    default:
                        return false;
                }
            }
            return true;
        }

        private sealed class StableErrorRule
        {
            public StableErrorRule(
                bool terminal,
                params U2R2Operation[] responseOperations)
            {
                Terminal = terminal;
                ResponseOperations = new HashSet<U2R2Operation>(
                    responseOperations ?? Array.Empty<U2R2Operation>());
            }

            public bool Terminal { get; }

            public HashSet<U2R2Operation> ResponseOperations { get; }
        }

        private static class Rfc8259JsonValidator
        {
            public static void Validate(
                string json,
                ulong maxJsonDepth)
            {
                if (json == null)
                    throw InvalidFrame("The U2R2 JSON header is invalid.");

                var parser = new Parser(json, maxJsonDepth);
                parser.SkipWhitespace();
                parser.ParseValue(depth: 0);
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                    throw InvalidFrame("The U2R2 JSON header has trailing content.");
            }

            private sealed class Parser
            {
                private readonly string _json;
                private readonly ulong _maxJsonDepth;
                private int _index;

                public Parser(string json, ulong maxJsonDepth)
                {
                    _json = json;
                    _maxJsonDepth = maxJsonDepth;
                }

                public bool AtEnd => _index == _json.Length;

                public void SkipWhitespace()
                {
                    while (_index < _json.Length)
                    {
                        switch (_json[_index])
                        {
                            case ' ':
                            case '\t':
                            case '\r':
                            case '\n':
                                _index++;
                                continue;
                            default:
                                return;
                        }
                    }
                }

                public void ParseValue(int depth)
                {
                    if (depth < 0
                        || checked((ulong)depth) > _maxJsonDepth
                        || _index >= _json.Length)
                        Fail();

                    switch (_json[_index])
                    {
                        case '{':
                            ParseObject(depth);
                            return;
                        case '[':
                            ParseArray(depth);
                            return;
                        case '"':
                            ParseString();
                            return;
                        case 't':
                            ParseLiteral("true");
                            return;
                        case 'f':
                            ParseLiteral("false");
                            return;
                        case 'n':
                            ParseLiteral("null");
                            return;
                        default:
                            ParseNumber();
                            return;
                    }
                }

                private void ParseObject(int depth)
                {
                    _index++;
                    SkipWhitespace();
                    if (TryConsume('}'))
                        return;

                    while (true)
                    {
                        if (_index >= _json.Length || _json[_index] != '"')
                            Fail();
                        ParseString();
                        SkipWhitespace();
                        Require(':');
                        SkipWhitespace();
                        ParseValue(depth + 1);
                        SkipWhitespace();
                        if (TryConsume('}'))
                            return;
                        Require(',');
                        SkipWhitespace();
                    }
                }

                private void ParseArray(int depth)
                {
                    _index++;
                    SkipWhitespace();
                    if (TryConsume(']'))
                        return;

                    while (true)
                    {
                        ParseValue(depth + 1);
                        SkipWhitespace();
                        if (TryConsume(']'))
                            return;
                        Require(',');
                        SkipWhitespace();
                    }
                }

                private void ParseString()
                {
                    Require('"');
                    while (_index < _json.Length)
                    {
                        var character = _json[_index++];
                        if (character == '"')
                            return;
                        if (character < 0x20)
                            Fail();
                        if (character != '\\')
                            continue;
                        if (_index >= _json.Length)
                            Fail();

                        var escape = _json[_index++];
                        switch (escape)
                        {
                            case '"':
                            case '\\':
                            case '/':
                            case 'b':
                            case 'f':
                            case 'n':
                            case 'r':
                            case 't':
                                break;
                            case 'u':
                                var codeUnit = ParseHexCodeUnit();
                                if (codeUnit >= 0xd800 && codeUnit <= 0xdbff)
                                {
                                    if (_index + 2 > _json.Length
                                        || _json[_index] != '\\'
                                        || _json[_index + 1] != 'u')
                                    {
                                        Fail();
                                    }
                                    _index += 2;
                                    var lowSurrogate = ParseHexCodeUnit();
                                    if (lowSurrogate < 0xdc00 || lowSurrogate > 0xdfff)
                                        Fail();
                                }
                                else if (codeUnit >= 0xdc00 && codeUnit <= 0xdfff)
                                {
                                    Fail();
                                }
                                break;
                            default:
                                Fail();
                                break;
                        }
                    }
                    Fail();
                }

                private void ParseLiteral(string literal)
                {
                    if (_index + literal.Length > _json.Length
                        || !string.Equals(
                            _json.Substring(_index, literal.Length),
                            literal,
                            StringComparison.Ordinal))
                    {
                        Fail();
                    }
                    _index += literal.Length;
                }

                private int ParseHexCodeUnit()
                {
                    var result = 0;
                    for (var digit = 0; digit < 4; digit++)
                    {
                        if (_index >= _json.Length)
                            Fail();
                        var character = _json[_index++];
                        result <<= 4;
                        if (character >= '0' && character <= '9')
                            result |= character - '0';
                        else if (character >= 'a' && character <= 'f')
                            result |= character - 'a' + 10;
                        else if (character >= 'A' && character <= 'F')
                            result |= character - 'A' + 10;
                        else
                            Fail();
                    }
                    return result;
                }

                private void ParseNumber()
                {
                    if (_index >= _json.Length || _json[_index] == '-')
                        Fail();

                    ulong value = 0;
                    if (TryConsume('0'))
                    {
                        if (_index < _json.Length && IsDigit(_json[_index]))
                            Fail();
                    }
                    else
                    {
                        if (_index >= _json.Length || !IsOneToNine(_json[_index]))
                            Fail();
                        do
                        {
                            var digit = (uint)(_json[_index] - '0');
                            if (value > (ulong.MaxValue - digit) / 10UL)
                                Fail();
                            value = value * 10UL + digit;
                            _index++;
                        } while (_index < _json.Length && IsDigit(_json[_index]));
                    }

                    if (_index < _json.Length
                        && (_json[_index] == '.'
                            || _json[_index] == 'e'
                            || _json[_index] == 'E'))
                    {
                        Fail();
                    }
                }

                private bool TryConsume(char expected)
                {
                    if (_index >= _json.Length || _json[_index] != expected)
                        return false;
                    _index++;
                    return true;
                }

                private void Require(char expected)
                {
                    if (!TryConsume(expected))
                        Fail();
                }

                private static bool IsDigit(char value)
                    => value >= '0' && value <= '9';

                private static bool IsOneToNine(char value)
                    => value >= '1' && value <= '9';

                private static void Fail()
                    => throw InvalidFrame("The U2R2 JSON header is invalid.");
            }
        }

        private static uint ReadUInt32(byte[] data, int offset)
            => (uint)(data[offset]
                      | data[offset + 1] << 8
                      | data[offset + 2] << 16
                      | data[offset + 3] << 24);

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }

    public static class U2R2LegacyV1Codec
    {
        public static U2R2LegacyV1Message ParseFirstFrame(byte[] frameBytes)
        {
            var frame = U2R2ProtocolCodec.DecodeFrame(frameBytes);
            var operation = RequiredLegacyString(frame.Header, "op");
            switch (operation)
            {
                case "health_ping":
                case "prepare_publisher":
                    var version = RequiredLegacyInteger(frame.Header, "protocolVersion");
                    if (version != 1)
                        throw InvalidLegacy("A legacy control frame requires protocolVersion 1.");
                    var requestId = RequiredLegacyString(frame.Header, "requestId");
                    if (requestId.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        throw InvalidLegacy("A legacy requestId cannot contain line breaks.");
                    return new U2R2LegacyV1Message(
                        operation,
                        requestId,
                        operation == "prepare_publisher");
                case "publish":
                    return new U2R2LegacyV1Message(operation, string.Empty, true);
                default:
                    throw InvalidLegacy(
                        "The first legacy U2R2 frame must be health_ping, prepare_publisher, or publish.");
            }
        }

        private static string RequiredLegacyString(JObject header, string name)
        {
            var token = header[name];
            if (token == null || token.Type != JTokenType.String)
                throw InvalidLegacy("The legacy U2R2 " + name + " must be a string.");
            var value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
                throw InvalidLegacy("The legacy U2R2 " + name + " must be nonempty.");
            return value;
        }

        private static long RequiredLegacyInteger(JObject header, string name)
        {
            var token = header[name];
            if (token == null || token.Type != JTokenType.Integer)
                throw InvalidLegacy("The legacy U2R2 " + name + " must be an integer.");
            try
            {
                return token.Value<long>();
            }
            catch (Exception exception) when (
                exception is OverflowException
                || exception is InvalidCastException
                || exception is FormatException)
            {
                throw InvalidLegacy("The legacy U2R2 " + name + " must be an integer.", exception);
            }
        }

        private static U2R2ProtocolException InvalidLegacy(
            string message,
            Exception innerException = null)
            => new U2R2ProtocolException("invalid_frame", message, true, innerException);
    }
}
