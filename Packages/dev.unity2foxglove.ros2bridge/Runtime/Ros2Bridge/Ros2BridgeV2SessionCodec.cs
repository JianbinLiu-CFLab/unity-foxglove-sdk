// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Session-scoped U2R2 v2 publish requests and exact response correlation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed class Ros2BridgeV2SessionSnapshot
    {
        private readonly IReadOnlyList<U2R2Capability> _capabilities;

        internal Ros2BridgeV2SessionSnapshot(
            string sessionId,
            ulong connectionGeneration,
            IEnumerable<U2R2Capability> capabilities,
            U2R2ProtocolLimits limits)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException(
                    "A U2R2 v2 session ID is required.",
                    nameof(sessionId));
            if (connectionGeneration == 0)
                throw new ArgumentOutOfRangeException(
                    nameof(connectionGeneration));
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));

            var frozenCapabilities = (capabilities
                    ?? throw new ArgumentNullException(nameof(capabilities)))
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            if (!frozenCapabilities.Contains(U2R2Capability.Publish))
            {
                throw new U2R2ProtocolException(
                    "missing_capability",
                    "The U2R2 v2 session did not grant publish capability.");
            }

            Dialect = U2R2Dialect.V2;
            SessionId = sessionId;
            ConnectionGeneration = connectionGeneration;
            _capabilities = Array.AsReadOnly(frozenCapabilities);
        }

        internal U2R2Dialect Dialect { get; }

        internal string SessionId { get; }

        internal ulong ConnectionGeneration { get; }

        internal IReadOnlyList<U2R2Capability> Capabilities => _capabilities;

        internal U2R2ProtocolLimits Limits { get; }

        internal bool HasCapability(U2R2Capability capability)
            => _capabilities.Contains(capability);
    }

    internal sealed class Ros2BridgeV2Request
    {
        private readonly int _payloadOffset;
        private readonly int _payloadLength;

        internal Ros2BridgeV2Request(
            byte[] wireBytes,
            U2R2ResponseExpectation expectation,
            IReadOnlyCollection<U2R2Capability> offeredCapabilities = null)
        {
            WireBytes = wireBytes
                ?? throw new ArgumentNullException(nameof(wireBytes));
            Expectation = expectation
                ?? throw new ArgumentNullException(nameof(expectation));
            OfferedCapabilities = Array.AsReadOnly(
                (offeredCapabilities ?? Array.Empty<U2R2Capability>())
                .Distinct()
                .OrderBy(value => value)
                .ToArray());

            if (wireBytes.Length < 16)
                throw new ArgumentException(
                    "A U2R2 request must contain a complete fixed header.",
                    nameof(wireBytes));
            var headerBytes = ReadUInt32LE(wireBytes, 8);
            var payloadBytes = ReadUInt32LE(wireBytes, 12);
            var payloadOffset = checked(16UL + headerBytes);
            if (payloadOffset > int.MaxValue
                || payloadBytes > int.MaxValue
                || payloadOffset + payloadBytes
                != checked((ulong)wireBytes.Length))
            {
                throw new ArgumentException(
                    "A U2R2 request has inconsistent wire lengths.",
                    nameof(wireBytes));
            }

            _payloadOffset = checked((int)payloadOffset);
            _payloadLength = checked((int)payloadBytes);
        }

        internal byte[] WireBytes { get; }

        internal byte[] Payload
        {
            get
            {
                if (_payloadLength == 0)
                    return Array.Empty<byte>();
                var payload = new byte[_payloadLength];
                Buffer.BlockCopy(
                    WireBytes,
                    _payloadOffset,
                    payload,
                    0,
                    payload.Length);
                return payload;
            }
        }

        internal U2R2ResponseExpectation Expectation { get; }

        internal IReadOnlyList<U2R2Capability> OfferedCapabilities { get; }

        private static ulong ReadUInt32LE(byte[] buffer, int offset)
            => (ulong)buffer[offset]
               | ((ulong)buffer[offset + 1] << 8)
               | ((ulong)buffer[offset + 2] << 16)
               | ((ulong)buffer[offset + 3] << 24);
    }

    internal readonly struct Ros2BridgeV2PublishMeasurement
    {
        internal Ros2BridgeV2PublishMeasurement(
            Ros2BridgeFrame frame,
            Ros2BridgeV2SessionSnapshot snapshot,
            ulong requestId,
            ulong messageId,
            string headerJson,
            int headerBytes,
            int totalWireBytes)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Snapshot = snapshot
                ?? throw new ArgumentNullException(nameof(snapshot));
            RequestId = requestId;
            MessageId = messageId;
            HeaderJson = headerJson
                ?? throw new ArgumentNullException(nameof(headerJson));
            HeaderBytes = headerBytes;
            TotalWireBytes = totalWireBytes;
        }

        internal Ros2BridgeFrame Frame { get; }
        internal Ros2BridgeV2SessionSnapshot Snapshot { get; }
        internal ulong RequestId { get; }
        internal ulong MessageId { get; }
        internal string HeaderJson { get; }
        internal int HeaderBytes { get; }
        internal int TotalWireBytes { get; }
    }

    internal static class Ros2BridgeV2SessionCodec
    {
        private const int FixedFrameBytes = 16;
        private static readonly byte[] FramePrefix =
        {
            (byte)'U', (byte)'2', (byte)'R', (byte)'2',
            U2R2ProtocolCodec.EnvelopeVersion, 0, 0, 0,
        };
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        internal static Ros2BridgeV2Request CreateHello(
            ulong requestId,
            bool requiresSubscription,
            U2R2ProtocolLimits limits)
        {
            limits ??= U2R2ProtocolLimits.Default;
            var capabilities = requiresSubscription
                ? new[]
                {
                    U2R2Capability.Publish,
                    U2R2Capability.Subscribe,
                }
                : new[] { U2R2Capability.Publish };
            var header = new JObject
            {
                ["capabilities"] = new JArray(
                    capabilities.Select(CapabilityWireValue)),
                ["op"] = "hello",
                ["protocolVersion"] = U2R2ProtocolCodec.ProtocolVersion,
                ["requestId"] = requestId,
            };
            return CreateRequest(
                header,
                Array.Empty<byte>(),
                limits,
                U2R2ResponseExpectation.FromHelloRequest(requestId),
                capabilities);
        }

        internal static Ros2BridgeV2SessionSnapshot AcceptHello(
            Ros2BridgeV2Request request,
            byte[] responseWireBytes,
            U2R2ProtocolLimits limits)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            limits ??= U2R2ProtocolLimits.Default;

            var response = ParseResponse(responseWireBytes, limits);
            U2R2ProtocolCodec.ValidateResponseCorrelation(
                request.Expectation,
                response);
            ThrowIfError(response);

            var offered = request.OfferedCapabilities;
            if (response.Capabilities.Any(
                    capability => !offered.Contains(capability)))
            {
                throw new U2R2ProtocolException(
                    "response_mismatch",
                    "The U2R2 hello response granted an unoffered capability.");
            }
            foreach (var required in offered)
            {
                if (!response.Capabilities.Contains(required))
                {
                    throw new U2R2ProtocolException(
                        "missing_capability",
                        "The U2R2 hello response omitted a required capability.");
                }
            }

            return new Ros2BridgeV2SessionSnapshot(
                response.SessionId,
                response.ConnectionGeneration,
                response.Capabilities,
                limits);
        }

        internal static Ros2BridgeV2Request CreatePublisherPreparation(
            Ros2BridgeV2SessionSnapshot snapshot,
            ulong requestId,
            string topic,
            string schemaName,
            FoxRunResolvedQos qos)
        {
            ValidateSnapshot(snapshot);
            var header = new JObject
            {
                ["connectionGeneration"] = snapshot.ConnectionGeneration,
                ["encoding"] = Ros2BridgeFrame.CdrEncoding,
                ["op"] = "prepare_publisher",
                ["protocolVersion"] = U2R2ProtocolCodec.ProtocolVersion,
                ["qos"] = CreateQos(qos),
                ["requestId"] = requestId,
                ["schemaName"] = schemaName,
                ["sessionId"] = snapshot.SessionId,
                ["topic"] = topic,
            };
            return CreateRequest(
                header,
                Array.Empty<byte>(),
                snapshot.Limits,
                U2R2ResponseExpectation.FromKnownRequest(
                    U2R2Operation.PreparePublisher,
                    requestId,
                    snapshot.SessionId,
                    snapshot.ConnectionGeneration));
        }

        internal static Ros2BridgeV2Request
            CreateSubscriptionRegistration(
                Ros2BridgeV2SessionSnapshot snapshot,
                ulong requestId,
                Ros2BridgeSessionContract contract)
        {
            ValidateSubscriptionSnapshot(snapshot);
            ValidateSubscriptionContract(contract);
            var header = new JObject
            {
                ["connectionGeneration"] =
                    snapshot.ConnectionGeneration,
                ["contractId"] = contract.ContractId,
                ["encoding"] = Ros2BridgeFrame.CdrEncoding,
                ["op"] = "register_subscription",
                ["protocolVersion"] =
                    U2R2ProtocolCodec.ProtocolVersion,
                ["qos"] = CreateQos(contract.Qos),
                ["requestId"] = requestId,
                ["schemaName"] = contract.CanonicalRosType,
                ["sessionId"] = snapshot.SessionId,
                ["topic"] = contract.Topic,
            };
            return CreateRequest(
                header,
                Array.Empty<byte>(),
                snapshot.Limits,
                U2R2ResponseExpectation.FromKnownRequest(
                    U2R2Operation.RegisterSubscription,
                    requestId,
                    snapshot.SessionId,
                    snapshot.ConnectionGeneration,
                    contractId: contract.ContractId));
        }

        internal static Ros2BridgeV2Request
            CreateSubscriptionRemoval(
                Ros2BridgeV2SessionSnapshot snapshot,
                ulong requestId,
                Ros2BridgeSessionContract contract)
        {
            ValidateSubscriptionSnapshot(snapshot);
            ValidateSubscriptionContract(contract);
            var header = new JObject
            {
                ["connectionGeneration"] =
                    snapshot.ConnectionGeneration,
                ["contractId"] = contract.ContractId,
                ["op"] = "unregister_subscription",
                ["protocolVersion"] =
                    U2R2ProtocolCodec.ProtocolVersion,
                ["requestId"] = requestId,
                ["sessionId"] = snapshot.SessionId,
            };
            return CreateRequest(
                header,
                Array.Empty<byte>(),
                snapshot.Limits,
                U2R2ResponseExpectation.FromKnownRequest(
                    U2R2Operation.UnregisterSubscription,
                    requestId,
                    snapshot.SessionId,
                    snapshot.ConnectionGeneration,
                    contractId: contract.ContractId));
        }

        internal static Ros2BridgeV2PublishMeasurement MeasurePublish(
            Ros2BridgeFrame frame,
            Ros2BridgeV2SessionSnapshot snapshot,
            ulong requestId,
            ulong messageId)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            ValidateSnapshot(snapshot);
            if (!frame.Qos.HasValue)
            {
                throw new U2R2ProtocolException(
                    "invalid_contract",
                    "A U2R2 v2 publish request requires an exact QoS contract.",
                    terminal: false);
            }
            if (requestId == 0)
                throw new U2R2ProtocolException(
                    "invalid_request_id",
                    "A U2R2 publish request ID must be nonzero.");
            if (messageId == 0)
                throw new U2R2ProtocolException(
                    "invalid_frame",
                    "A U2R2 publish message ID must be nonzero.");

            var header = CreatePublishHeader(
                frame,
                snapshot,
                requestId,
                messageId);
            var headerJson = JsonConvert.SerializeObject(
                header,
                Formatting.None);
            var headerBytes = StrictUtf8.GetByteCount(headerJson);
            var size = U2R2FrameSize.Create(
                snapshot.Limits,
                checked((ulong)headerBytes),
                checked((ulong)frame.PayloadLength));
            return new Ros2BridgeV2PublishMeasurement(
                frame,
                snapshot,
                requestId,
                messageId,
                headerJson,
                headerBytes,
                checked((int)size.TotalBytes));
        }

        internal static Ros2BridgeV2Request EncodePublish(
            Ros2BridgeFrame frame,
            Ros2BridgeV2SessionSnapshot snapshot,
            ulong requestId,
            ulong messageId,
            Ros2BridgeV2PublishMeasurement measurement)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));
            ValidateSnapshot(snapshot);
            if (!ReferenceEquals(frame, measurement.Frame)
                || !ReferenceEquals(snapshot, measurement.Snapshot)
                || requestId != measurement.RequestId
                || messageId != measurement.MessageId)
            {
                throw new ArgumentException(
                    "The U2R2 publish measurement belongs to a different request.",
                    nameof(measurement));
            }

            var wireBytes = new byte[measurement.TotalWireBytes];
            Buffer.BlockCopy(
                FramePrefix,
                0,
                wireBytes,
                0,
                FramePrefix.Length);
            WriteUInt32LE(
                wireBytes,
                8,
                checked((uint)measurement.HeaderBytes));
            WriteUInt32LE(
                wireBytes,
                12,
                checked((uint)frame.PayloadLength));
            var actualHeaderBytes = StrictUtf8.GetBytes(
                measurement.HeaderJson,
                0,
                measurement.HeaderJson.Length,
                wireBytes,
                FixedFrameBytes);
            if (actualHeaderBytes != measurement.HeaderBytes)
            {
                throw new InvalidOperationException(
                    "The U2R2 publish header changed after measurement.");
            }

            using (var stream = new MemoryStream(
                       wireBytes,
                       writable: true))
            {
                stream.Position = checked(
                    FixedFrameBytes + actualHeaderBytes);
                frame.WritePayloadTo(stream);
                if (stream.Position != wireBytes.Length)
                {
                    throw new InvalidOperationException(
                        "The U2R2 publish encoder produced an unexpected byte count.");
                }
            }

            return new Ros2BridgeV2Request(
                wireBytes,
                U2R2ResponseExpectation.FromKnownRequest(
                    U2R2Operation.Publish,
                    requestId,
                    snapshot.SessionId,
                    snapshot.ConnectionGeneration,
                    messageId: messageId));
        }

        internal static U2R2Message ValidateResponse(
            Ros2BridgeV2Request request,
            byte[] responseWireBytes,
            Ros2BridgeV2SessionSnapshot snapshot)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ValidateSnapshot(snapshot);
            var response = ParseResponse(
                responseWireBytes,
                snapshot.Limits);
            U2R2ProtocolCodec.ValidateResponseCorrelation(
                request.Expectation,
                response);
            ThrowIfError(response);
            return response;
        }

        internal static U2R2Message ValidateAcceptedResponse(
            U2R2Message response)
        {
            ThrowIfError(response);
            return response;
        }

        private static Ros2BridgeV2Request CreateRequest(
            JObject header,
            byte[] payload,
            U2R2ProtocolLimits limits,
            U2R2ResponseExpectation expectation,
            IReadOnlyCollection<U2R2Capability> offeredCapabilities = null)
        {
            var wireBytes = U2R2ProtocolCodec.EncodeFrame(
                header,
                payload,
                limits);
            return new Ros2BridgeV2Request(
                wireBytes,
                expectation,
                offeredCapabilities);
        }

        private static U2R2Message ParseResponse(
            byte[] responseWireBytes,
            U2R2ProtocolLimits limits)
        {
            if (responseWireBytes == null)
                throw new ArgumentNullException(nameof(responseWireBytes));
            return U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(
                    responseWireBytes,
                    limits));
        }

        private static void ThrowIfError(U2R2Message response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (string.Equals(response.Status, "ok", StringComparison.Ordinal))
                return;
            throw new U2R2ProtocolException(
                response.ErrorCode,
                response.ErrorMessage.Length == 0
                    ? "The U2R2 peer rejected the request."
                    : response.ErrorMessage,
                response.Terminal);
        }

        private static JObject CreatePublishHeader(
            Ros2BridgeFrame frame,
            Ros2BridgeV2SessionSnapshot snapshot,
            ulong requestId,
            ulong messageId)
        {
            var header = new JObject
            {
                ["connectionGeneration"] = snapshot.ConnectionGeneration,
                ["encoding"] = frame.Encoding,
                ["logTimeNs"] = frame.LogTimeNs,
                ["messageId"] = messageId,
                ["op"] = "publish",
                ["protocolVersion"] = U2R2ProtocolCodec.ProtocolVersion,
            };
            header["qos"] = CreateQos(frame.Qos.Value);
            header["requestId"] = requestId;
            header["schemaName"] = frame.SchemaName;
            header["sequence"] = frame.Sequence;
            header["sessionId"] = snapshot.SessionId;
            header["topic"] = frame.Topic;
            return header;
        }

        private static JObject CreateQos(FoxRunResolvedQos qos)
        {
            if (!Ros2BridgeFrame.IsValidResolvedQos(qos))
                throw new ArgumentException(
                    "A U2R2 publisher requires a fully resolved QoS contract.",
                    nameof(qos));
            return new JObject
            {
                ["depth"] = qos.Depth,
                ["durability"] =
                    Ros2BridgeFrameWriter.DurabilityWireValue(
                        qos.Durability),
                ["history"] =
                    Ros2BridgeFrameWriter.HistoryWireValue(qos.History),
                ["profile"] =
                    Ros2BridgeFrameWriter.ProfileWireValue(qos.Profile),
                ["reliability"] =
                    Ros2BridgeFrameWriter.ReliabilityWireValue(
                        qos.Reliability),
            };
        }

        private static void ValidateSnapshot(
            Ros2BridgeV2SessionSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Dialect != U2R2Dialect.V2
                || !snapshot.HasCapability(U2R2Capability.Publish))
            {
                throw new U2R2ProtocolException(
                    "missing_capability",
                    "The U2R2 session cannot publish.");
            }
        }

        private static void ValidateSubscriptionSnapshot(
            Ros2BridgeV2SessionSnapshot snapshot)
        {
            ValidateSnapshot(snapshot);
            if (!snapshot.HasCapability(
                    U2R2Capability.Subscribe))
            {
                throw new U2R2ProtocolException(
                    "missing_capability",
                    "The U2R2 session cannot subscribe.");
            }
        }

        private static void ValidateSubscriptionContract(
            Ros2BridgeSessionContract contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (contract.Direction
                != FoxRunTransportDirection.Subscribe)
            {
                throw new U2R2ProtocolException(
                    "invalid_contract",
                    "A U2R2 subscription request requires subscribe direction.",
                    terminal: false);
            }
        }

        private static string CapabilityWireValue(
            U2R2Capability capability)
        {
            switch (capability)
            {
                case U2R2Capability.Publish:
                    return "publish";
                case U2R2Capability.Subscribe:
                    return "subscribe";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(capability));
            }
        }

        private static void WriteUInt32LE(
            byte[] buffer,
            int offset,
            uint value)
        {
            buffer[offset] = (byte)(value & 0xff);
            buffer[offset + 1] = (byte)((value >> 8) & 0xff);
            buffer[offset + 2] = (byte)((value >> 16) & 0xff);
            buffer[offset + 3] = (byte)((value >> 24) & 0xff);
        }
    }
}
