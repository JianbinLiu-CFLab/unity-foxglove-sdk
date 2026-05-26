// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Decoder registry and built-in JSON/ROS2 diagnostic decoders.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapDecodeRegistry
    {
        private static readonly Lazy<List<IMcapMessageDecoderFactory>> BuiltInFactories =
            new Lazy<List<IMcapMessageDecoderFactory>>(BuildBuiltInFactories);
        private static readonly object FactoryDiagnosticsGate = new object();
        private static readonly List<string> FactoryDiagnostics = new List<string>();

        private readonly McapDecodeOptions _options;
        private readonly Dictionary<ushort, McapSchema> _schemas;
        private readonly Dictionary<ushort, McapChannel> _channels;
        private readonly List<IMcapMessageDecoderFactory> _factories;
        private readonly Dictionary<ushort, IMcapMessageDecoder> _decoderCache = new Dictionary<ushort, IMcapMessageDecoder>();

        internal static IReadOnlyList<string> OptionalFactoryDiagnostics
        {
            get
            {
                _ = BuiltInFactories.Value;
                lock (FactoryDiagnosticsGate)
                    return new List<string>(FactoryDiagnostics);
            }
        }

        public McapDecodeRegistry(
            McapDecodeOptions options,
            Dictionary<ushort, McapSchema> schemas,
            Dictionary<ushort, McapChannel> channels)
        {
            _options = options ?? new McapDecodeOptions();
            _schemas = schemas ?? new Dictionary<ushort, McapSchema>();
            _channels = channels ?? new Dictionary<ushort, McapChannel>();
            _factories = BuildFactories(_options);
        }

        public bool TryDecode(McapDataLoaderMessage raw, out McapDecodedMessage decoded)
        {
            decoded = Decode(raw);
            return decoded.Payload != null &&
                   decoded.Payload.Kind != McapDecodedPayloadKind.Unsupported &&
                   decoded.Payload.Kind != McapDecodedPayloadKind.Failed;
        }

        public McapDecodedMessage Decode(McapDataLoaderMessage raw)
        {
            raw = raw ?? new McapDataLoaderMessage();
            var decoded = new McapDecodedMessage
            {
                Raw = raw,
                Payload = McapDecodedPayload.Raw(raw.Data)
            };

            var decoder = ResolveDecoder(raw);
            if (decoder == null)
            {
                decoded.Payload = new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Unsupported,
                    RawData = raw.Data ?? new byte[0],
                    Text = "No decoder supports message_encoding '" + (raw.MessageEncoding ?? string.Empty) + "'."
                };
                decoded.Problems.Add(CreateProblem(
                    raw,
                    "McapDecodeUnsupported",
                    "No decoded MCAP DataLoader decoder supports this channel/schema encoding.",
                    null,
                    McapDataLoaderProblemSeverity.Warning));
                return decoded;
            }

            try
            {
                decoded.Payload = decoder.Decode(raw) ?? McapDecodedPayload.Raw(raw.Data);
                if (decoded.Payload.RawData == null || decoded.Payload.RawData.Length == 0)
                    decoded.Payload.RawData = raw.Data ?? new byte[0];
                return decoded;
            }
            catch (Exception ex)
            {
                if (_options.FailurePolicy == McapDecodeFailurePolicy.Throw)
                    throw;

                if (TryDecodeRos2CdrDiagnosticFallback(raw, out var fallback, out var fallbackException))
                {
                    decoded.Payload = fallback;
                    decoded.Problems.Add(CreateProblem(
                        raw,
                        "McapRos2CdrTypedDecodeFailed",
                        ex.Message,
                        ex,
                        McapDataLoaderProblemSeverity.Warning));
                    return decoded;
                }

                decoded.Payload = new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Failed,
                    RawData = raw.Data ?? new byte[0],
                    Text = ex.Message ?? string.Empty
                };
                decoded.Problems.Add(CreateProblem(
                    raw,
                    "McapDecodeFailed",
                    ex.Message,
                    ex,
                    McapDataLoaderProblemSeverity.Error));
                if (fallbackException != null)
                {
                    decoded.Problems.Add(CreateProblem(
                        raw,
                        "McapRos2CdrDiagnosticFallbackFailed",
                        fallbackException.Message,
                        fallbackException,
                        McapDataLoaderProblemSeverity.Warning));
                }
                return decoded;
            }
        }

        private IMcapMessageDecoder ResolveDecoder(McapDataLoaderMessage raw)
        {
            if (_decoderCache.TryGetValue(raw.ChannelId, out var cached))
                return cached;

            _channels.TryGetValue(raw.ChannelId, out var channel);
            if (channel == null)
            {
                channel = new McapChannel
                {
                    Id = raw.ChannelId,
                    SchemaId = raw.SchemaId,
                    Topic = raw.Topic ?? string.Empty,
                    MessageEncoding = raw.MessageEncoding ?? string.Empty
                };
            }

            _schemas.TryGetValue(channel.SchemaId, out var schema);
            for (var i = 0; i < _factories.Count; i++)
            {
                var decoder = _factories[i]?.TryCreate(schema, channel);
                if (decoder != null)
                {
                    _decoderCache[raw.ChannelId] = decoder;
                    return decoder;
                }
            }

            _decoderCache[raw.ChannelId] = null;
            return null;
        }

        private static List<IMcapMessageDecoderFactory> BuildFactories(McapDecodeOptions options)
        {
            var factories = new List<IMcapMessageDecoderFactory>();
            if (options.DecoderFactories != null)
            {
                for (var i = 0; i < options.DecoderFactories.Count; i++)
                    if (options.DecoderFactories[i] != null)
                        factories.Add(options.DecoderFactories[i]);
            }

            if (options.UseBuiltInDecoders)
                factories.AddRange(BuiltInFactories.Value);

            return factories;
        }

        private static List<IMcapMessageDecoderFactory> BuildBuiltInFactories()
        {
            var factories = new List<IMcapMessageDecoderFactory>
            {
                new McapJsonMessageDecoderFactory()
            };
            var protobufFactory = TryCreateProtobufFactory();
            if (protobufFactory != null)
                factories.Add(protobufFactory);
            var ros2TypedFactory = TryCreateRos2CdrTypedFactory();
            if (ros2TypedFactory != null)
                factories.Add(ros2TypedFactory);
            factories.Add(new McapRos2CdrDiagnosticDecoderFactory());
            return factories;
        }

        private static IMcapMessageDecoderFactory TryCreateProtobufFactory()
        {
            return TryCreateAssemblyFactory("Unity.FoxgloveSDK.IO.McapFoxgloveProtobufDecoderFactory");
        }

        private static IMcapMessageDecoderFactory TryCreateRos2CdrTypedFactory()
        {
            return TryCreateAssemblyFactory("Unity.FoxgloveSDK.IO.McapRos2CdrTypedDecoderFactory");
        }

        private static IMcapMessageDecoderFactory TryCreateAssemblyFactory(string typeName)
        {
            try
            {
                var type = Type.GetType(typeName + ", Unity.FoxgloveSDK.Proto", throwOnError: false);
                if (type == null)
                {
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (var i = 0; i < assemblies.Length && type == null; i++)
                        type = assemblies[i].GetType(typeName, throwOnError: false);
                }

                if (type == null)
                {
                    AddFactoryDiagnostic(typeName + " was not found in loaded assemblies.");
                    return null;
                }

                if (!typeof(IMcapMessageDecoderFactory).IsAssignableFrom(type))
                {
                    AddFactoryDiagnostic(typeName + " does not implement IMcapMessageDecoderFactory.");
                    return null;
                }

                var factory = Activator.CreateInstance(type) as IMcapMessageDecoderFactory;
                if (factory == null)
                    AddFactoryDiagnostic(typeName + " could not be constructed as IMcapMessageDecoderFactory.");
                return factory;
            }
            catch (Exception ex)
            {
                AddFactoryDiagnostic(typeName + " failed to load: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static void AddFactoryDiagnostic(string message)
        {
            lock (FactoryDiagnosticsGate)
                FactoryDiagnostics.Add(message ?? string.Empty);
        }

        private bool TryDecodeRos2CdrDiagnosticFallback(
            McapDataLoaderMessage raw,
            out McapDecodedPayload payload,
            out Exception exception)
        {
            payload = null;
            exception = null;
            _channels.TryGetValue(raw.ChannelId, out var channel);
            if (channel == null)
                return false;

            _schemas.TryGetValue(channel.SchemaId, out var schema);
            if (!string.Equals(channel.MessageEncoding, "cdr", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(schema?.Encoding, FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                payload = new McapRos2CdrDiagnosticDecoder(schema?.Name ?? string.Empty).Decode(raw);
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                payload = null;
                return false;
            }
        }

        private static McapDecodeProblem CreateProblem(
            McapDataLoaderMessage raw,
            string code,
            string message,
            Exception exception,
            McapDataLoaderProblemSeverity severity)
        {
            return new McapDecodeProblem
            {
                Severity = severity,
                Code = code ?? string.Empty,
                Message = message ?? string.Empty,
                ChannelId = raw.ChannelId,
                SchemaId = raw.SchemaId,
                Topic = raw.Topic ?? string.Empty,
                ExceptionType = exception?.GetType().Name ?? string.Empty
            };
        }
    }

    internal sealed class McapJsonMessageDecoderFactory : IMcapMessageDecoderFactory
    {
        public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
        {
            return string.Equals(channel?.MessageEncoding, "json", StringComparison.OrdinalIgnoreCase)
                ? new McapJsonMessageDecoder()
                : null;
        }
    }

    internal sealed class McapJsonMessageDecoder : IMcapMessageDecoder
    {
        public McapDecodedPayload Decode(McapDataLoaderMessage message)
        {
            var raw = message?.Data ?? new byte[0];
            if (raw.Length == 0)
                throw new InvalidDataException("JSON payload is empty.");
            var json = Encoding.UTF8.GetString(raw);
            var token = JToken.Parse(json);
            return new McapDecodedPayload
            {
                Kind = McapDecodedPayloadKind.Json,
                Value = token,
                Text = token.ToString(Newtonsoft.Json.Formatting.None),
                RawData = raw
            };
        }
    }

    internal sealed class McapRos2CdrDiagnosticDecoderFactory : IMcapMessageDecoderFactory
    {
        public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
        {
            if (!string.Equals(channel?.MessageEncoding, "cdr", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!string.Equals(schema?.Encoding, FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, StringComparison.OrdinalIgnoreCase))
                return null;
            return new McapRos2CdrDiagnosticDecoder(schema?.Name ?? string.Empty);
        }
    }

    internal sealed class McapRos2CdrDiagnosticDecoder : IMcapMessageDecoder
    {
        private readonly string _schemaName;
        private readonly bool _schemaKnown;

        public McapRos2CdrDiagnosticDecoder(string schemaName)
        {
            _schemaName = schemaName ?? string.Empty;
            _schemaKnown = FoxgloveRos2MsgSchemaCatalog.TryGet(_schemaName, out _);
        }

        public McapDecodedPayload Decode(McapDataLoaderMessage message)
        {
            var raw = message?.Data ?? new byte[0];
            if (raw.Length < 4)
                throw new InvalidDataException("ROS2 CDR payload is shorter than the four-byte encapsulation header.");

            var encapsulation = (ushort)((raw[0] << 8) | raw[1]);
            if (encapsulation > 3)
                throw new InvalidDataException("ROS2 CDR encapsulation kind is not recognized: " + encapsulation + ".");

            var diagnostic = new McapRos2CdrDiagnosticPayload
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
                Kind = McapDecodedPayloadKind.Ros2CdrDiagnostic,
                Value = diagnostic,
                Text = "schema=" + _schemaName + ";cdr=" + encapsulation + ";bytes=" + raw.Length,
                RawData = raw
            };
        }
    }
}
