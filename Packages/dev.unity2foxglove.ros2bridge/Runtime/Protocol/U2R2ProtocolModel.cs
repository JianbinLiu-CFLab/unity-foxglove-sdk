// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Encoding-independent U2R2 v2 identifiers and connection state.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public enum U2R2Capability
    {
        Publish = 1,
        Subscribe = 2,
    }

    public enum U2R2Dialect
    {
        None = 0,
        V1 = 1,
        V2 = 2,
    }

    public enum U2R2ConnectionState
    {
        AwaitingFirstFrame = 0,
        V1Probe = 1,
        V1Data = 2,
        V2Active = 3,
        Terminal = 4,
    }

    public enum U2R2Operation
    {
        Unknown = 0,
        Hello,
        HelloAck,
        HealthPing,
        HealthPong,
        PreparePublisher,
        PublisherReady,
        Publish,
        PublishResult,
        RegisterSubscription,
        SubscriptionReady,
        Message,
        UnregisterSubscription,
        SubscriptionRemoved,
        Busy,
        Fault,
    }

    internal static class U2R2OperationRules
    {
        public static bool IsRequest(U2R2Operation operation)
        {
            switch (operation)
            {
                case U2R2Operation.Hello:
                case U2R2Operation.HealthPing:
                case U2R2Operation.PreparePublisher:
                case U2R2Operation.Publish:
                case U2R2Operation.RegisterSubscription:
                case U2R2Operation.UnregisterSubscription:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsResponse(U2R2Operation operation)
        {
            switch (operation)
            {
                case U2R2Operation.HelloAck:
                case U2R2Operation.HealthPong:
                case U2R2Operation.PublisherReady:
                case U2R2Operation.PublishResult:
                case U2R2Operation.SubscriptionReady:
                case U2R2Operation.SubscriptionRemoved:
                case U2R2Operation.Busy:
                case U2R2Operation.Fault:
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryGetSuccessResponse(
            U2R2Operation requestOperation,
            out U2R2Operation responseOperation)
        {
            switch (requestOperation)
            {
                case U2R2Operation.Hello:
                    responseOperation = U2R2Operation.HelloAck;
                    return true;
                case U2R2Operation.HealthPing:
                    responseOperation = U2R2Operation.HealthPong;
                    return true;
                case U2R2Operation.PreparePublisher:
                    responseOperation = U2R2Operation.PublisherReady;
                    return true;
                case U2R2Operation.Publish:
                    responseOperation = U2R2Operation.PublishResult;
                    return true;
                case U2R2Operation.RegisterSubscription:
                    responseOperation = U2R2Operation.SubscriptionReady;
                    return true;
                case U2R2Operation.UnregisterSubscription:
                    responseOperation = U2R2Operation.SubscriptionRemoved;
                    return true;
                default:
                    responseOperation = U2R2Operation.Unknown;
                    return false;
            }
        }
    }

    public sealed class U2R2ProtocolException : FormatException
    {
        public U2R2ProtocolException(
            string errorCode,
            string message,
            bool terminal = true,
            Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = string.IsNullOrWhiteSpace(errorCode)
                ? "invalid_frame"
                : errorCode;
            Terminal = terminal;
        }

        public string ErrorCode { get; }

        public bool Terminal { get; }
    }

    public sealed class U2R2Frame
    {
        private readonly JObject _header;

        public U2R2Frame(JObject header, byte[] payload)
            : this(header, payload, clone: true)
        {
        }

        internal static U2R2Frame CreateOwned(
            JObject header,
            byte[] payload)
            => new U2R2Frame(header, payload, clone: false);

        private U2R2Frame(
            JObject header,
            byte[] payload,
            bool clone)
        {
            _header = header == null
                ? throw new ArgumentNullException(nameof(header))
                : clone
                    ? (JObject)header.DeepClone()
                    : header;
            Payload = new ReadOnlyMemory<byte>(
                payload == null
                    ? Array.Empty<byte>()
                    : clone
                        ? (byte[])payload.Clone()
                        : payload);
        }

        public JObject Header => (JObject)_header.DeepClone();

        public ReadOnlyMemory<byte> Payload { get; }
    }

    public sealed class U2R2Message
    {
        internal U2R2Message(
            U2R2Operation operation,
            string operationName,
            bool isRequest,
            bool isResponse,
            bool terminal,
            ulong requestId,
            ulong messageId,
            ulong contractId,
            string sessionId,
            ulong connectionGeneration,
            IReadOnlyCollection<U2R2Capability> capabilities,
            string status,
            string errorCode,
            string errorMessage,
            ulong logTimeNs,
            ulong receiveTimeNs,
            string encoding,
            string representation,
            string topic,
            string schemaName,
            U2R2Qos qos)
        {
            Operation = operation;
            OperationName = operationName;
            IsRequest = isRequest;
            IsResponse = isResponse;
            Terminal = terminal;
            RequestId = requestId;
            MessageId = messageId;
            ContractId = contractId;
            SessionId = sessionId ?? string.Empty;
            ConnectionGeneration = connectionGeneration;
            Capabilities = Array.AsReadOnly(
                (capabilities ?? Array.Empty<U2R2Capability>()).ToArray());
            Status = status ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            LogTimeNs = logTimeNs;
            ReceiveTimeNs = receiveTimeNs;
            Encoding = encoding ?? string.Empty;
            Representation = representation ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Qos = qos;
        }

        public U2R2Operation Operation { get; }

        public string OperationName { get; }

        public bool IsRequest { get; }

        public bool IsResponse { get; }

        public bool Terminal { get; }

        public ulong RequestId { get; }

        public ulong MessageId { get; }

        public ulong ContractId { get; }

        public string SessionId { get; }

        public ulong ConnectionGeneration { get; }

        public IReadOnlyList<U2R2Capability> Capabilities { get; }

        public string Status { get; }

        public string ErrorCode { get; }

        public string ErrorMessage { get; }

        public ulong LogTimeNs { get; }

        public ulong ReceiveTimeNs { get; }

        public string Encoding { get; }

        public string Representation { get; }

        public string Topic { get; }

        public string SchemaName { get; }

        public U2R2Qos Qos { get; }
    }

    public sealed class U2R2ResponseExpectation
    {
        private U2R2ResponseExpectation(
            U2R2Operation requestOperation,
            ulong requestId,
            string sessionId,
            ulong connectionGeneration,
            ulong contractId,
            ulong messageId)
        {
            ValidateRequestId(requestId);
            if (!U2R2OperationRules.TryGetSuccessResponse(
                    requestOperation,
                    out var successResponseOperation))
            {
                throw new ArgumentOutOfRangeException(nameof(requestOperation));
            }

            SessionId = sessionId ?? string.Empty;
            if ((SessionId.Length == 0) != (connectionGeneration == 0))
            {
                throw new U2R2ProtocolException(
                    "invalid_frame",
                    "Expected U2R2 session identity fields must be present together.");
            }

            RequestOperation = requestOperation;
            RequestId = requestId;
            SuccessResponseOperation = successResponseOperation;
            AllowedResponseOperations = Array.AsReadOnly(
                new[]
                {
                    successResponseOperation,
                    U2R2Operation.Busy,
                    U2R2Operation.Fault,
                });
            ConnectionGeneration = connectionGeneration;
            ContractId = contractId;
            MessageId = messageId;
        }

        public static U2R2ResponseExpectation FromHelloRequest(
            ulong requestId)
            => new U2R2ResponseExpectation(
                U2R2Operation.Hello,
                requestId,
                string.Empty,
                0,
                0,
                0);

        public static U2R2ResponseExpectation FromRequest(
            U2R2Message request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.IsRequest
                || !U2R2OperationRules.IsRequest(request.Operation))
            {
                throw new ArgumentException(
                    "A U2R2 response expectation requires a request message.",
                    nameof(request));
            }
            return new U2R2ResponseExpectation(
                request.Operation,
                request.RequestId,
                request.SessionId,
                request.ConnectionGeneration,
                request.ContractId,
                request.MessageId);
        }

        internal static U2R2ResponseExpectation FromKnownRequest(
            U2R2Operation operation,
            ulong requestId,
            string sessionId = "",
            ulong connectionGeneration = 0,
            ulong contractId = 0,
            ulong messageId = 0)
            => new U2R2ResponseExpectation(
                operation,
                requestId,
                sessionId,
                connectionGeneration,
                contractId,
                messageId);

        public U2R2Operation RequestOperation { get; }

        public ulong RequestId { get; }

        public U2R2Operation SuccessResponseOperation { get; }

        public IReadOnlyList<U2R2Operation> AllowedResponseOperations { get; }

        public string SessionId { get; }

        public ulong ConnectionGeneration { get; }

        public ulong ContractId { get; }

        public ulong MessageId { get; }

        internal bool AssignsSessionIdentity
            => RequestOperation == U2R2Operation.Hello;

        private static void ValidateRequestId(ulong requestId)
        {
            if (requestId == 0)
            {
                throw new U2R2ProtocolException(
                    "invalid_request_id",
                    "A correlated U2R2 request ID must be nonzero.");
            }
        }
    }

    public sealed class U2R2LegacyV1Message
    {
        internal U2R2LegacyV1Message(
            string operation,
            string requestId,
            bool acquiresDataLease)
        {
            Operation = operation;
            RequestId = requestId ?? string.Empty;
            AcquiresDataLease = acquiresDataLease;
        }

        public string Operation { get; }

        public string RequestId { get; }

        public bool AcquiresDataLease { get; }
    }

    public sealed class U2R2MonotonicCounter
    {
        private ulong _current;

        public U2R2MonotonicCounter(ulong current = 0)
        {
            _current = current;
        }

        public bool IsFaulted { get; private set; }

        public ulong Next()
        {
            if (IsFaulted || _current == ulong.MaxValue)
            {
                IsFaulted = true;
                throw new U2R2ProtocolException(
                    "counter_exhausted",
                    "The U2R2 uint64 identifier counter is exhausted.");
            }

            _current++;
            return _current;
        }
    }

    public sealed class U2R2SessionIdentity
    {
        internal U2R2SessionIdentity(string sessionId, ulong connectionGeneration)
        {
            SessionId = sessionId;
            ConnectionGeneration = connectionGeneration;
        }

        public string SessionId { get; }

        public ulong ConnectionGeneration { get; }
    }

    internal sealed class U2R2SidecarSessionIdentityAllocator
    {
        private readonly U2R2MonotonicCounter _generation;

        public U2R2SidecarSessionIdentityAllocator(ulong currentGeneration = 0)
        {
            _generation = new U2R2MonotonicCounter(currentGeneration);
        }

        public U2R2SessionIdentity Allocate()
        {
            var generation = _generation.Next();
            return new U2R2SessionIdentity(
                Guid.NewGuid().ToString("D"),
                generation);
        }
    }

    public sealed class U2R2SessionStateMachine
    {
        public U2R2Dialect Dialect { get; private set; }

        public U2R2ConnectionState State { get; private set; }
            = U2R2ConnectionState.AwaitingFirstFrame;

        public bool AcquiresDataLease { get; private set; }

        public void AcceptV2(
            U2R2Message hello,
            IEnumerable<U2R2Capability> requiredCapabilities)
        {
            if (hello == null)
                throw new ArgumentNullException(nameof(hello));

            if (State != U2R2ConnectionState.AwaitingFirstFrame)
                ThrowTerminal("dialect_downgrade", "A socket cannot change U2R2 dialect.");

            if (hello.Operation != U2R2Operation.Hello || !hello.IsRequest)
                ThrowTerminal("invalid_frame", "The first U2R2 v2 frame must be hello.");

            var offered = new HashSet<U2R2Capability>(hello.Capabilities);
            var required = requiredCapabilities ?? Array.Empty<U2R2Capability>();
            if (required.Any(capability => !offered.Contains(capability)))
                ThrowTerminal("missing_capability", "A required U2R2 capability was not offered.");

            Dialect = U2R2Dialect.V2;
            State = U2R2ConnectionState.V2Active;
            AcquiresDataLease = true;
        }

        public void AcceptLegacy(U2R2LegacyV1Message firstFrame)
        {
            if (firstFrame == null)
                throw new ArgumentNullException(nameof(firstFrame));

            if (State != U2R2ConnectionState.AwaitingFirstFrame)
                ThrowTerminal("dialect_downgrade", "A socket cannot change U2R2 dialect.");

            Dialect = U2R2Dialect.V1;
            AcquiresDataLease = firstFrame.AcquiresDataLease;
            State = firstFrame.AcquiresDataLease
                ? U2R2ConnectionState.V1Data
                : U2R2ConnectionState.V1Probe;
        }

        public void Fault(string errorCode, string message)
            => ThrowTerminal(errorCode, message);

        private void ThrowTerminal(string errorCode, string message)
        {
            State = U2R2ConnectionState.Terminal;
            AcquiresDataLease = false;
            throw new U2R2ProtocolException(errorCode, message);
        }
    }
}
