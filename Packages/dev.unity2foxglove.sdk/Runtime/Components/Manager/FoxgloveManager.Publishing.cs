// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Provides FoxgloveManager channel registration and publish helpers.

using Unity.FoxgloveSDK.Core;
using UnityEngine;
#if UNITY_2020_3_OR_NEWER
using Unity.Profiling;
#endif

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
        /// Foxglove message encoding label for MessagePack payloads.
        /// </summary>
        private const string MsgPackEncoding = "msgpack";

        /// <summary>
        /// Empty schema name used for schemaless manual JSON channels.
        /// </summary>
        private const string EmptySchemaName = "";

        /// <summary>
        /// Empty schema payload used for schemaless manual JSON channels.
        /// </summary>
        private const string EmptySchemaPayload = "";

#if UNITY_2020_3_OR_NEWER
        private static readonly ProfilerMarker PublishJsonMarker = new ProfilerMarker("FoxgloveManager.PublishJson");
        private static readonly ProfilerMarker PublishProtoMarker = new ProfilerMarker("FoxgloveManager.PublishProto");
        private static readonly ProfilerMarker PublishMsgPackMarker = new ProfilerMarker("FoxgloveManager.PublishMsgPack");
#endif

        /// <summary>
        /// Gets or registers a schema-bound channel.
        /// </summary>
        /// <param name="topic">Topic name, for example "/tf".</param>
        /// <param name="schemaName">Schema name, for example "foxglove.FrameTransform".</param>
        /// <param name="encoding">Foxglove message encoding.</param>
        /// <returns>The channel identifier associated with the topic, schema, and encoding.</returns>
        public uint GetOrRegisterSchemaChannel(string topic, string schemaName, string encoding = JsonEncoding)
        {
            if (!IsValidPublishTopic(topic))
                throw new System.InvalidOperationException("Foxglove publisher topic must be non-empty.");

            var key = (topic, schemaName, encoding, "");
            if (_channelCache.TryGetValue(key, out var id))
            {
                return id;
            }

            id = (uint)_connectionState.NextChannelId;
            _runtime.RegisterSchemaChannel(id, topic, schemaName, encoding);
            _connectionState.NextChannelId++;
            _channelCache[key] = id;
            return id;
        }

        /// <summary>
        /// Register or reuse a channel before a publisher prepares payload data.
        /// </summary>
        /// <param name="topic">Topic to advertise and potentially publish to.</param>
        /// <param name="schemaName">Schema name, or null/empty for schemaless JSON.</param>
        /// <param name="encoding">Foxglove message encoding.</param>
        /// <param name="channelId">Resolved channel identifier when preparation succeeds.</param>
        /// <param name="requireDemand">When true, return false unless a subscriber or MCAP recorder needs data.</param>
        /// <returns>True when payload preparation should continue.</returns>
        public bool TryPrepareSchemaPublish(
            string topic,
            string schemaName,
            string encoding,
            out uint channelId,
            bool requireDemand = true)
        {
            channelId = 0;

            if (SuppressLivePublishersForReplay)
                return false;

            if (!IsRunning)
                return false;

            if (!TryValidatePublishTopic(topic, "prepare schema publish"))
                return false;

            var messageEncoding = string.IsNullOrEmpty(encoding) ? JsonEncoding : encoding;
            channelId = string.IsNullOrEmpty(schemaName)
                ? GetOrRegisterChannel(topic, messageEncoding)
                : GetOrRegisterSchemaChannel(topic, schemaName, messageEncoding);

            return !requireDemand || _runtime.HasChannelDemand(channelId);
        }

        /// <summary>
        /// Register or reuse a schemaless MessagePack channel before a publisher prepares payload data.
        /// </summary>
        /// <param name="topic">Topic to advertise and potentially publish to.</param>
        /// <param name="channelId">Resolved channel identifier when preparation succeeds.</param>
        /// <param name="requireDemand">When true, return false unless a subscriber or MCAP recorder needs data.</param>
        /// <returns>True when payload preparation should continue.</returns>
        public bool TryPrepareMsgPackPublish(
            string topic,
            out uint channelId,
            bool requireDemand = true)
        {
            channelId = 0;

            if (SuppressLivePublishersForReplay)
                return false;

            if (!IsRunning)
                return false;

            if (!TryValidatePublishTopic(topic, "prepare MsgPack publish"))
                return false;

            channelId = GetOrRegisterChannel(topic, MsgPackEncoding);
            return !requireDemand || _runtime.HasChannelDemand(channelId);
        }

        /// <summary>
        /// Attach or detach an optional mirror sink supplied by an add-on package.
        /// </summary>
        public void SetMirrorSink(IFoxgloveMirrorSink sink)
        {
            if (sink == null && _runtime == null)
                return;

            EnsureRuntimeCreated();
            _runtime.SetMirrorSink(sink);
        }

        /// <summary>Return the currently attached optional mirror sink, if any.</summary>
        public IFoxgloveMirrorSink GetMirrorSink()
            => _runtime?.GetMirrorSink();

        /// <summary>
        /// Serializes a message to JSON and publishes it on the specified topic.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="schemaName">Schema name, or null/empty for schemaless JSON.</param>
        /// <param name="message">Object to serialize via Newtonsoft.Json.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishJson(string topic, string schemaName, object message, ulong logTimeNs)
        {
#if UNITY_2020_3_OR_NEWER
            PublishJsonMarker.Begin();
            try
            {
#endif
            if (SuppressLivePublishersForReplay)
            {
                return;
            }

            if (!IsRunning)
            {
                if (_foxgloveOutputEnabled && !_warningDebounceState.WarnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishJson called but server is not running.");
                    _warningDebounceState.WarnedNotRunning = true;
                }

                return;
            }

            if (!TryValidatePublishTopic(topic, "publish JSON"))
                return;

            var channelId = string.IsNullOrEmpty(schemaName)
                ? GetOrRegisterChannel(topic, JsonEncoding)
                : GetOrRegisterSchemaChannel(topic, schemaName, JsonEncoding);
            _runtime.PublishJson(channelId, message, logTimeNs);
            RecordPublishCadence(topic, JsonEncoding);
#if UNITY_2020_3_OR_NEWER
            }
            finally
            {
                PublishJsonMarker.End();
            }
#endif
        }

        /// <summary>
        /// Publishes a source-generated FoxRun JSON payload that has already
        /// been serialized without runtime reflection.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="schemaName">Schema name, or null/empty for schemaless JSON.</param>
        /// <param name="payload">UTF-8 JSON payload bytes.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishFoxRunJsonBytes(string topic, string schemaName, byte[] payload, ulong logTimeNs)
        {
#if UNITY_2020_3_OR_NEWER
            PublishJsonMarker.Begin();
            try
            {
#endif
            if (SuppressLivePublishersForReplay)
            {
                return;
            }

            if (!IsRunning)
            {
                if (_foxgloveOutputEnabled && !_warningDebounceState.WarnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishFoxRunJsonBytes called but server is not running.");
                    _warningDebounceState.WarnedNotRunning = true;
                }

                return;
            }

            if (!TryValidatePublishTopic(topic, "publish FoxRun JSON"))
                return;

            var channelId = string.IsNullOrEmpty(schemaName)
                ? GetOrRegisterChannel(topic, JsonEncoding)
                : GetOrRegisterSchemaChannel(topic, schemaName, JsonEncoding);
            _runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), logTimeNs);
            RecordPublishCadence(topic, JsonEncoding);
#if UNITY_2020_3_OR_NEWER
            }
            finally
            {
                PublishJsonMarker.End();
            }
#endif
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
#if UNITY_2020_3_OR_NEWER
            PublishProtoMarker.Begin();
            try
            {
#endif
            if (SuppressLivePublishersForReplay)
            {
                return;
            }

            if (!IsRunning)
            {
                if (_foxgloveOutputEnabled && !_warningDebounceState.WarnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishProto called but server is not running.");
                    _warningDebounceState.WarnedNotRunning = true;
                }

                return;
            }

            if (!TryValidatePublishTopic(topic, "publish Protobuf"))
                return;

            var channelId = GetOrRegisterSchemaChannel(topic, schemaName, ProtobufEncoding);
            _runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), logTimeNs);
            RecordPublishCadence(topic, ProtobufEncoding);
#if UNITY_2020_3_OR_NEWER
            }
            finally
            {
                PublishProtoMarker.End();
            }
#endif
        }

        /// <summary>
        /// Publishes a pre-serialized MessagePack payload on a schemaless raw channel.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="payload">Serialized MessagePack payload.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishMsgPack(string topic, byte[] payload, ulong logTimeNs)
        {
#if UNITY_2020_3_OR_NEWER
            PublishMsgPackMarker.Begin();
            try
            {
#endif
            if (SuppressLivePublishersForReplay)
            {
                return;
            }

            if (!IsRunning)
            {
                if (_foxgloveOutputEnabled && !_warningDebounceState.WarnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishMsgPack called but server is not running.");
                    _warningDebounceState.WarnedNotRunning = true;
                }

                return;
            }

            if (!TryValidatePublishTopic(topic, "publish MsgPack"))
                return;

            var channelId = GetOrRegisterChannel(topic, MsgPackEncoding);
            _runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), logTimeNs);
            RecordPublishCadence(topic, MsgPackEncoding);
#if UNITY_2020_3_OR_NEWER
            }
            finally
            {
                PublishMsgPackMarker.End();
            }
#endif
        }

        private static bool IsValidPublishTopic(string topic)
            => TopicNameNormalizer.IsValidPublishTopic(topic);

        private bool TryValidatePublishTopic(string topic, string operation)
        {
            if (IsValidPublishTopic(topic))
                return true;

            var key = "invalid-topic:" + operation;
            if (_warningDebounceState.LastInvalidPublishTopicWarningKey != key)
            {
                _warningDebounceState.LastInvalidPublishTopicWarningKey = key;
                Debug.LogWarning($"[Foxglove] Cannot {operation}: publisher topic is empty.");
            }

            return false;
        }

        /// <summary>
        /// Gets or registers a schemaless channel for manual publish calls.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="encoding">Foxglove message encoding.</param>
        /// <returns>The channel identifier associated with the topic and encoding.</returns>
        private uint GetOrRegisterChannel(string topic, string encoding)
        {
            if (!IsValidPublishTopic(topic))
                throw new System.InvalidOperationException("Foxglove publisher topic must be non-empty.");

            var key = (topic, EmptySchemaName, encoding, "");
            if (_channelCache.TryGetValue(key, out var id))
            {
                return id;
            }

            id = (uint)_connectionState.NextChannelId;
            _runtime.RegisterChannel(new Protocol.AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = encoding,
                SchemaName = EmptySchemaName,
                Schema = EmptySchemaPayload
            });
            _connectionState.NextChannelId++;
            _channelCache[key] = id;
            return id;
        }
    }
}
