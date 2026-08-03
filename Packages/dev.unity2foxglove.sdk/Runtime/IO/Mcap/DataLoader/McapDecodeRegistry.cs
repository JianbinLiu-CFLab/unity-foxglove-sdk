// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Decoder registry and SDK-owned built-in decoders.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapDecodeRegistry
    {
        private static readonly object BuiltInFactoriesGate = new object();
        private static Lazy<List<IMcapMessageDecoderFactory>> BuiltInFactories = CreateBuiltInFactoriesLazy();
        private static readonly object FactoryDiagnosticsGate = new object();
        private static readonly List<string> FactoryDiagnostics = new List<string>();

        private readonly McapDecodeOptions _options;
        private readonly Dictionary<ushort, McapSchema> _schemas;
        private readonly Dictionary<ushort, McapChannel> _channels;
        private readonly List<IMcapMessageDecoderFactory> _factories;
        private readonly Dictionary<ushort, IMcapMessageDecoder> _decoderCache = new Dictionary<ushort, IMcapMessageDecoder>();

        /// <summary>
        /// Gets optional decoder load diagnostics after forcing built-in factory initialization.
        /// </summary>
        internal static IReadOnlyList<string> OptionalFactoryDiagnostics
        {
            get
            {
                _ = GetBuiltInFactories();
                lock (FactoryDiagnosticsGate)
                    return new List<string>(FactoryDiagnostics);
            }
        }

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFactoryDiagnosticsForRuntimeLoad()
        {
            lock (BuiltInFactoriesGate)
                BuiltInFactories = CreateBuiltInFactoriesLazy();
            lock (FactoryDiagnosticsGate)
                FactoryDiagnostics.Clear();
        }
#endif

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

        /// <summary>
        /// Decodes a message and returns whether a supported, non-failed payload was produced.
        /// This registry instance is not thread-safe; use one instance per reader thread or synchronize externally.
        /// </summary>
        public bool TryDecode(McapDataLoaderMessage raw, out McapDecodedMessage decoded)
        {
            decoded = Decode(raw);
            return decoded.Payload != null &&
                   decoded.Payload.Kind != McapDecodedPayloadKind.Unsupported &&
                   decoded.Payload.Kind != McapDecodedPayloadKind.Failed;
        }

        /// <summary>
        /// Decodes a message with this registry's channel decoder cache.
        /// This registry instance is not thread-safe; use one instance per reader thread or synchronize externally.
        /// </summary>
        public McapDecodedMessage Decode(McapDataLoaderMessage raw)
        {
            raw = raw ?? new McapDataLoaderMessage();
            var decoded = new McapDecodedMessage
            {
                Raw = raw
            };

            var decoder = ResolveDecoder(raw);
            if (decoder == null)
            {
                decoded.Payload = new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Unsupported,
                    RawData = raw.Data ?? Array.Empty<byte>(),
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
                    decoded.Payload.RawData = raw.Data ?? Array.Empty<byte>();
                return decoded;
            }
            catch (Exception ex)
            {
                if (_options.FailurePolicy == McapDecodeFailurePolicy.Throw)
                    throw;

                Exception fallbackException = null;
                if (decoder is IMcapMessageDecoderFailureFallback fallback)
                {
                    try
                    {
                        var recovered = fallback.DecodeFallback(raw);
                        if (recovered != null)
                        {
                            decoded.Payload = recovered;
                            if (decoded.Payload.RawData == null || decoded.Payload.RawData.Length == 0)
                                decoded.Payload.RawData = raw.Data ?? Array.Empty<byte>();
                            decoded.Problems.Add(CreateProblem(
                                raw,
                                fallback.FailureProblemCode,
                                ex.Message,
                                ex,
                                McapDataLoaderProblemSeverity.Warning));
                            return decoded;
                        }
                    }
                    catch (Exception recoveryException)
                    {
                        fallbackException = recoveryException;
                    }
                }

                decoded.Payload = new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Failed,
                    RawData = raw.Data ?? Array.Empty<byte>(),
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
                        "McapDecodeDiagnosticFallbackFailed",
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
                factories.AddRange(GetBuiltInFactories());

            return factories;
        }

        private static List<IMcapMessageDecoderFactory> GetBuiltInFactories()
        {
            lock (BuiltInFactoriesGate)
                return BuiltInFactories.Value;
        }

        private static Lazy<List<IMcapMessageDecoderFactory>> CreateBuiltInFactoriesLazy()
        {
            return new Lazy<List<IMcapMessageDecoderFactory>>(BuildBuiltInFactories);
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
            return factories;
        }

        private static IMcapMessageDecoderFactory TryCreateProtobufFactory()
        {
            return TryCreateAssemblyFactory(
                "Unity.FoxgloveSDK.IO.McapFoxgloveProtobufDecoderFactory",
                "Unity.FoxgloveSDK.Proto");
        }

        private static IMcapMessageDecoderFactory TryCreateAssemblyFactory(string typeName, string preferredAssemblyName)
        {
            try
            {
                var type = Type.GetType(typeName + ", " + preferredAssemblyName, throwOnError: false);
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
            var raw = message?.Data ?? Array.Empty<byte>();
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

}
