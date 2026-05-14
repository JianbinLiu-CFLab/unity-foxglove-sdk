// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Provides FoxgloveManager channel registration and publish helpers.

using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        /// <summary>
        /// Foxglove message encoding label for JSON payloads.
        /// </summary>
        private const string JsonEncoding = "json";

        /// <summary>
        /// Foxglove message encoding label for protobuf payloads.
        /// </summary>
        private const string ProtobufEncoding = "protobuf";

        /// <summary>
        /// Empty schema name used for schemaless manual JSON channels.
        /// </summary>
        private const string EmptySchemaName = "";

        /// <summary>
        /// Empty schema payload used for schemaless manual JSON channels.
        /// </summary>
        private const string EmptySchemaPayload = "";

        /// <summary>
        /// Gets or registers a schema-bound channel.
        /// </summary>
        /// <param name="topic">Topic name, for example "/tf".</param>
        /// <param name="schemaName">Schema name, for example "foxglove.FrameTransform".</param>
        /// <param name="encoding">Foxglove message encoding.</param>
        /// <returns>The channel identifier associated with the topic, schema, and encoding.</returns>
        public uint GetOrRegisterSchemaChannel(string topic, string schemaName, string encoding = JsonEncoding)
        {
            var key = (topic, schemaName, encoding);
            if (_channelCache.TryGetValue(key, out var id))
            {
                return id;
            }

            id = (uint)_nextChannelId;
            _runtime.RegisterSchemaChannel(id, topic, schemaName, encoding);
            _nextChannelId++;
            _channelCache[key] = id;
            return id;
        }

        /// <summary>
        /// Serializes a message to JSON and publishes it on the specified topic.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="schemaName">Schema name, or null/empty for schemaless JSON.</param>
        /// <param name="message">Object to serialize via Newtonsoft.Json.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishJson(string topic, string schemaName, object message, ulong logTimeNs)
        {
            if (SuppressLivePublishersForReplay)
            {
                return;
            }

            if (!IsRunning)
            {
                if (!_warnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishJson called but server is not running.");
                    _warnedNotRunning = true;
                }

                return;
            }

            var channelId = string.IsNullOrEmpty(schemaName)
                ? GetOrRegisterChannel(topic, JsonEncoding)
                : GetOrRegisterSchemaChannel(topic, schemaName, JsonEncoding);
            _runtime.PublishJson(channelId, message, logTimeNs);
        }

        /// <summary>
        /// Publishes a protobuf-encoded payload on the specified topic.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="schemaName">Schema name advertised to Foxglove.</param>
        /// <param name="payload">Serialized protobuf payload.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishProto(string topic, string schemaName, byte[] payload, ulong logTimeNs)
        {
            if (SuppressLivePublishersForReplay)
            {
                return;
            }

            if (!IsRunning)
            {
                if (!_warnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishProto called but server is not running.");
                    _warnedNotRunning = true;
                }

                return;
            }

            var channelId = GetOrRegisterSchemaChannel(topic, schemaName, ProtobufEncoding);
            _runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), logTimeNs);
        }

        /// <summary>
        /// Gets or registers a schemaless channel for manual publish calls.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="encoding">Foxglove message encoding.</param>
        /// <returns>The channel identifier associated with the topic and encoding.</returns>
        private uint GetOrRegisterChannel(string topic, string encoding)
        {
            var key = (topic, EmptySchemaName, encoding);
            if (_channelCache.TryGetValue(key, out var id))
            {
                return id;
            }

            id = (uint)_nextChannelId;
            _runtime.RegisterChannel(new Protocol.AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = encoding,
                SchemaName = EmptySchemaName,
                Schema = EmptySchemaPayload
            });
            _nextChannelId++;
            _channelCache[key] = id;
            return id;
        }
    }
}
