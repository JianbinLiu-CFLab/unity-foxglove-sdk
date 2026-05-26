// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Optional decoded MCAP DataLoader message view.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Creates a decoder for one MCAP schema/channel pair, or returns <c>null</c> when unsupported.</summary>
    public interface IMcapMessageDecoderFactory
    {
        /// <summary>Try to create a message decoder for the supplied schema and channel.</summary>
        IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel);
    }

    /// <summary>Decodes a raw DataLoader message into an optional higher-level diagnostic payload.</summary>
    public interface IMcapMessageDecoder
    {
        /// <summary>Decode one raw message. Implementations must not mutate <paramref name="message"/>.</summary>
        McapDecodedPayload Decode(McapDataLoaderMessage message);
    }

    /// <summary>Kind of payload returned by the decoded DataLoader view.</summary>
    public enum McapDecodedPayloadKind
    {
        /// <summary>Raw bytes are preserved without a higher-level decode.</summary>
        Raw = 0,
        /// <summary>Payload decoded as a Newtonsoft.Json.Linq.JToken.</summary>
        Json = 1,
        /// <summary>Payload decoded as a packaged Foxglove protobuf IMessage.</summary>
        Protobuf = 2,
        /// <summary>Payload inspected as a ROS2 CDR diagnostic envelope.</summary>
        Ros2CdrDiagnostic = 3,
        /// <summary>No decoder supports this schema/channel encoding.</summary>
        Unsupported = 4,
        /// <summary>A matching decoder failed and failure policy kept the raw message.</summary>
        Failed = 5,
        /// <summary>Payload decoded as a packaged Foxglove ROS2 CDR IMessage.</summary>
        Ros2CdrTyped = 6
    }

    /// <summary>Controls how decode errors are surfaced to callers.</summary>
    public enum McapDecodeFailurePolicy
    {
        /// <summary>Return the raw message with a structured problem when decode fails.</summary>
        RawWithProblem = 0,
        /// <summary>Throw the decoder exception immediately.</summary>
        Throw = 1
    }

    /// <summary>
    /// Options for opt-in decoded DataLoader iteration. Raw payload bytes remain
    /// the source of truth regardless of these settings.
    /// </summary>
    public sealed class McapDecodeOptions
    {
        /// <summary>Caller-provided factories. These run before built-in decoders.</summary>
        public List<IMcapMessageDecoderFactory> DecoderFactories = new List<IMcapMessageDecoderFactory>();

        /// <summary>Whether JSON, packaged protobuf, and ROS2 typed/diagnostic decoders are enabled.</summary>
        public bool UseBuiltInDecoders = true;

        /// <summary>Policy for malformed payloads or decoder exceptions.</summary>
        public McapDecodeFailurePolicy FailurePolicy = McapDecodeFailurePolicy.RawWithProblem;
    }

    /// <summary>Raw message plus decoded payload and any structured decode problems.</summary>
    public sealed class McapDecodedMessage
    {
        /// <summary>Original raw DataLoader message. This object is never modified by decoders.</summary>
        public McapDataLoaderMessage Raw;

        /// <summary>Decoded payload view or raw/unsupported/failed placeholder.</summary>
        public McapDecodedPayload Payload;

        /// <summary>Structured diagnostics emitted while decoding this message.</summary>
        public List<McapDecodeProblem> Problems = new List<McapDecodeProblem>();

        /// <summary>True when a higher-level payload was decoded without problems.</summary>
        public bool IsDecoded =>
            Payload != null &&
            Payload.Kind != McapDecodedPayloadKind.Raw &&
            Payload.Kind != McapDecodedPayloadKind.Unsupported &&
            Payload.Kind != McapDecodedPayloadKind.Failed &&
            Problems.Count == 0;

        /// <summary>
        /// True when a higher-level payload is available even if warnings were attached,
        /// such as a diagnostic fallback after a typed decoder failure.
        /// </summary>
        public bool HasDecodedPayload =>
            Payload != null &&
            Payload.Kind != McapDecodedPayloadKind.Raw &&
            Payload.Kind != McapDecodedPayloadKind.Unsupported &&
            Payload.Kind != McapDecodedPayloadKind.Failed;
    }

    /// <summary>Decoded payload container. <see cref="RawData"/> always preserves the original payload bytes.</summary>
    public sealed class McapDecodedPayload
    {
        /// <summary>Payload kind.</summary>
        public McapDecodedPayloadKind Kind = McapDecodedPayloadKind.Raw;

        /// <summary>Decoded value, such as JToken, IMessage, or McapRos2CdrDiagnosticPayload.</summary>
        public object Value;

        /// <summary>Optional diagnostic or JSON text representation for logs and tests.</summary>
        public string Text = string.Empty;

        /// <summary>Original raw payload bytes.</summary>
        public byte[] RawData = new byte[0];

        /// <summary>Create a raw payload view.</summary>
        public static McapDecodedPayload Raw(byte[] rawData)
            => new McapDecodedPayload { Kind = McapDecodedPayloadKind.Raw, RawData = rawData ?? new byte[0] };
    }

    /// <summary>Schema-aware diagnostic view for ROS2 CDR payloads.</summary>
    public sealed class McapRos2CdrDiagnosticPayload
    {
        /// <summary>Schema name recorded in MCAP.</summary>
        public string SchemaName = string.Empty;

        /// <summary>Whether the schema name exists in the bundled ROS2 .msg catalog.</summary>
        public bool SchemaKnown;

        /// <summary>CDR encapsulation kind from the first two bytes.</summary>
        public ushort EncapsulationKind;

        /// <summary>True for little-endian CDR encapsulation kinds.</summary>
        public bool IsLittleEndian;

        /// <summary>Total raw message payload length.</summary>
        public int PayloadByteLength;

        /// <summary>Payload length after the four-byte CDR encapsulation header.</summary>
        public int DataByteLength;
    }

    /// <summary>Structured decode diagnostic attached to one decoded message.</summary>
    public sealed class McapDecodeProblem
    {
        /// <summary>Severity assigned to this decode diagnostic.</summary>
        public McapDataLoaderProblemSeverity Severity = McapDataLoaderProblemSeverity.Warning;

        /// <summary>Stable diagnostic code.</summary>
        public string Code = string.Empty;

        /// <summary>Human-readable diagnostic message.</summary>
        public string Message = string.Empty;

        /// <summary>Channel ID from the raw message.</summary>
        public ushort ChannelId;

        /// <summary>Schema ID from the raw message.</summary>
        public ushort SchemaId;

        /// <summary>Topic from the raw message.</summary>
        public string Topic = string.Empty;

        /// <summary>Exception type name when a decoder exception was converted to a problem.</summary>
        public string ExceptionType = string.Empty;
    }
}
