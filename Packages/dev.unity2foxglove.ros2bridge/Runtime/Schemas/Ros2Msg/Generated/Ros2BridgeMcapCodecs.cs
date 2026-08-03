// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Generated
// Purpose: Explicit Bridge-owned schema, session, and MCAP codec helpers.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <summary>
    /// Explicit entry point for callers that want ROS 2 Bridge MCAP support.
    /// The SDK never discovers these factories through reflection.
    /// </summary>
    public static class Ros2BridgeMcapCodecs
    {
        public const string MessageEncoding = "cdr";
        public const string SchemaEncoding = "ros2msg";

        public static IReadOnlyList<IMcapMessageDecoderFactory> CreateFactories()
            => new IMcapMessageDecoderFactory[]
            {
                new McapRos2CdrTypedDecoderFactory(),
                new McapRos2CdrDiagnosticDecoderFactory()
            };

        public static void EnableRos2BridgeSchemas(this FoxgloveRuntime runtime)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            Ros2MsgSchemasSetup.RegisterSchemas(runtime.Schemas);
            runtime.EnableMessageEncoding(MessageEncoding);
        }

        public static void EnableRos2BridgeSchemas(this FoxgloveSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            Ros2MsgSchemasSetup.RegisterSchemas(session.Schemas);
            session.EnableMessageEncoding(MessageEncoding);
        }

        public static void RegisterRos2MsgSchemaChannel(
            this FoxgloveRuntime runtime,
            uint channelId,
            string topic,
            string schemaName)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            Ros2MsgSchemasSetup.RegisterSchemas(runtime.Schemas);
            runtime.RegisterSchemaChannel(
                channelId,
                topic,
                schemaName,
                MessageEncoding,
                SchemaEncoding);
        }

        public static void RegisterRos2MsgSchemaChannel(
            this FoxgloveSession session,
            uint channelId,
            string topic,
            string schemaName)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            Ros2MsgSchemasSetup.RegisterSchemas(session.Schemas);
            session.EnableMessageEncoding(MessageEncoding);
            session.RegisterSchemaChannel(
                channelId,
                topic,
                schemaName,
                MessageEncoding,
                SchemaEncoding);
        }

        public static void PublishRos2Cdr(
            this FoxgloveRuntime runtime,
            uint channelId,
            byte[] payload)
        {
            Validate(payload);
            runtime.Publish(channelId, payload);
        }

        public static void PublishRos2Cdr(
            this FoxgloveRuntime runtime,
            uint channelId,
            byte[] payload,
            ulong logTimeNs)
        {
            Validate(payload);
            runtime.Publish(channelId, payload, logTimeNs);
        }

        public static void PublishRos2Cdr(
            this FoxgloveSession session,
            uint channelId,
            byte[] payload)
        {
            Validate(payload);
            session.Publish(channelId, payload);
        }

        public static void PublishRos2Cdr(
            this FoxgloveSession session,
            uint channelId,
            byte[] payload,
            ulong logTimeNs)
        {
            Validate(payload);
            session.Publish(channelId, payload, logTimeNs);
        }

        public static bool PublishRecordingOnlyRos2Cdr(
            this FoxgloveRuntime runtime,
            uint channelId,
            byte[] payload,
            ulong logTimeNs)
        {
            Validate(payload);
            return runtime.PublishRecordingOnly(channelId, payload, logTimeNs);
        }

        private static void Validate(byte[] payload)
            => Ros2CdrPayloadValidator.Validate(payload);
    }

    public sealed class McapRos2CdrDiagnosticDecoderFactory
        : IStableMcapMessageDecoderFactory
    {
        public string StableDecoderId =>
            "unity2foxglove.ros2bridge/cdr-diagnostic";

        public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
        {
            if (!string.Equals(
                    channel?.MessageEncoding,
                    Ros2BridgeMcapCodecs.MessageEncoding,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (!string.Equals(
                    schema?.Encoding,
                    Ros2BridgeMcapCodecs.SchemaEncoding,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return new Decoder(schema?.Name ?? string.Empty);
        }

        private sealed class Decoder : IMcapMessageDecoder
        {
            private readonly string _schemaName;
            private readonly bool _schemaKnown;

            internal Decoder(string schemaName)
            {
                _schemaName = schemaName ?? string.Empty;
                _schemaKnown = FoxgloveRos2MsgSchemaCatalog.TryGet(
                    _schemaName,
                    out _);
            }

            public McapDecodedPayload Decode(McapDataLoaderMessage message)
            {
                var raw = message?.Data ?? Array.Empty<byte>();
                if (raw.Length < 4)
                {
                    throw new InvalidDataException(
                        "ROS 2 CDR payload is shorter than the four-byte encapsulation header.");
                }

                var encapsulation = (ushort)((raw[0] << 8) | raw[1]);
                if (encapsulation > 3)
                {
                    throw new InvalidDataException(
                        "ROS 2 CDR encapsulation kind is not recognized: "
                        + encapsulation
                        + ".");
                }

                var diagnostic = new Ros2CdrDiagnosticPayload
                {
                    SchemaName = _schemaName,
                    SchemaKnown = _schemaKnown,
                    EncapsulationKind = encapsulation,
                    IsLittleEndian = encapsulation == 1 || encapsulation == 3,
                    PayloadByteLength = raw.Length,
                    DataByteLength = raw.Length - 4
                };

                return new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Provider,
                    DecoderId = "unity2foxglove.ros2bridge/cdr-diagnostic",
                    Value = diagnostic,
                    Text = "schema="
                           + _schemaName
                           + ";cdr="
                           + encapsulation
                           + ";bytes="
                           + raw.Length,
                    RawData = raw
                };
            }
        }
    }

    public sealed class Ros2CdrDiagnosticPayload
    {
        public string SchemaName = string.Empty;
        public bool SchemaKnown;
        public ushort EncapsulationKind;
        public bool IsLittleEndian;
        public int PayloadByteLength;
        public int DataByteLength;
    }
}
