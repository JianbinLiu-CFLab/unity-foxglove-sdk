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

    /// <summary>
    /// Stable identity required for explicit, session-local decoder factory
    /// snapshots. IDs are compared ordinally and duplicates fail closed.
    /// </summary>
    public interface IStableMcapMessageDecoderFactory :
        IMcapMessageDecoderFactory
    {
        string StableDecoderId { get; }
    }

    /// <summary>Decodes a raw DataLoader message into an optional higher-level diagnostic payload.</summary>
    public interface IMcapMessageDecoder
    {
        /// <summary>Decode one raw message. Implementations must not mutate <paramref name="message"/>.</summary>
        McapDecodedPayload Decode(McapDataLoaderMessage message);
    }

    /// <summary>
    /// Optional encoding-neutral recovery hook for a decoder that can preserve
    /// a diagnostic view when its primary typed decode fails.
    /// </summary>
    public interface IMcapMessageDecoderFailureFallback
    {
        string FailureProblemCode { get; }

        McapDecodedPayload DecodeFallback(McapDataLoaderMessage message);
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
        /// <summary>Payload decoded by an explicitly supplied Provider factory.</summary>
        Provider = 3,
        /// <summary>No decoder supports this schema/channel encoding.</summary>
        Unsupported = 4,
        /// <summary>A matching decoder failed and failure policy kept the raw message.</summary>
        Failed = 5
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

        /// <summary>Whether SDK-owned JSON and packaged protobuf decoders are enabled.</summary>
        public bool UseBuiltInDecoders = true;

        /// <summary>Policy for malformed payloads or decoder exceptions.</summary>
        public McapDecodeFailurePolicy FailurePolicy = McapDecodeFailurePolicy.RawWithProblem;
    }

    /// <summary>Raw message plus decoded payload and any structured decode problems.</summary>
    public sealed class McapDecodedMessage
    {
        private List<McapDecodeProblem> _problems;

        /// <summary>Original raw DataLoader message. This object is never modified by decoders.</summary>
        public McapDataLoaderMessage Raw;

        /// <summary>Decoded payload view or raw/unsupported/failed placeholder.</summary>
        public McapDecodedPayload Payload;

        /// <summary>Structured diagnostics emitted while decoding this message.</summary>
        public List<McapDecodeProblem> Problems
        {
            get => _problems ?? (_problems = new List<McapDecodeProblem>());
            set => _problems = value;
        }

        /// <summary>True when a higher-level payload was decoded without problems.</summary>
        public bool IsDecoded =>
            Payload != null &&
            Payload.Kind != McapDecodedPayloadKind.Raw &&
            Payload.Kind != McapDecodedPayloadKind.Unsupported &&
            Payload.Kind != McapDecodedPayloadKind.Failed &&
            (_problems == null || _problems.Count == 0);

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

        /// <summary>Decoded value supplied by the selected decoder.</summary>
        public object Value;

        /// <summary>
        /// Stable decoder identity for <see cref="McapDecodedPayloadKind.Provider"/>
        /// payloads. Empty for SDK built-ins.
        /// </summary>
        public string DecoderId = string.Empty;

        /// <summary>Optional diagnostic or JSON text representation for logs and tests.</summary>
        public string Text = string.Empty;

        /// <summary>Original raw payload bytes.</summary>
        public byte[] RawData = Array.Empty<byte>();

        /// <summary>Create a raw payload view.</summary>
        public static McapDecodedPayload Raw(byte[] rawData)
            => new McapDecodedPayload { Kind = McapDecodedPayloadKind.Raw, RawData = rawData ?? Array.Empty<byte>() };
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
