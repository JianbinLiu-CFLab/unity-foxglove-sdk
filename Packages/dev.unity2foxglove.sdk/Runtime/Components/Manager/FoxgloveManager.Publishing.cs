// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Provides FoxgloveManager channel registration and publish helpers.

using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
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
        /// Foxglove message encoding label for ROS 2 CDR payloads.
        /// </summary>
        private const string CdrEncoding = "cdr";

        /// <summary>
        /// Foxglove schema encoding label for ROS 2 .msg schemas.
        /// </summary>
        private const string Ros2MsgSchemaEncoding = "ros2msg";

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
            var key = (topic, schemaName, encoding, "");
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
        /// Gets or registers a ROS 2 .msg schema-bound CDR channel.
        /// </summary>
        /// <param name="topic">Topic name, for example "/tf".</param>
        /// <param name="schemaName">ROS 2 interface name, for example "foxglove_msgs/msg/FrameTransform".</param>
        /// <returns>The channel identifier associated with the topic, schema, cdr, and ros2msg.</returns>
        public uint GetOrRegisterRos2MsgSchemaChannel(string topic, string schemaName)
        {
            if (string.IsNullOrWhiteSpace(schemaName))
                throw new System.InvalidOperationException("ROS2 schema channels require a schema name.");

            var key = (topic, schemaName, CdrEncoding, Ros2MsgSchemaEncoding);
            if (_channelCache.TryGetValue(key, out var id))
            {
                return id;
            }

            if (!FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out _))
                throw new System.InvalidOperationException($"Unknown ROS2 schema '{schemaName}'.");

            id = (uint)_nextChannelId;
            _runtime.RegisterRos2MsgSchemaChannel(id, topic, schemaName);
            _nextChannelId++;
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

            var messageEncoding = string.IsNullOrEmpty(encoding) ? JsonEncoding : encoding;
            channelId = string.IsNullOrEmpty(schemaName)
                ? GetOrRegisterChannel(topic, messageEncoding)
                : GetOrRegisterSchemaChannel(topic, schemaName, messageEncoding);

            return !requireDemand || _runtime.HasChannelDemand(channelId);
        }

        /// <summary>
        /// Register or reuse a ROS 2 CDR channel before a publisher prepares payload data.
        /// </summary>
        /// <param name="topic">Topic to advertise and potentially publish to.</param>
        /// <param name="schemaName">ROS 2 interface schema name.</param>
        /// <param name="channelId">Resolved channel identifier when preparation succeeds.</param>
        /// <param name="requireDemand">When true, return false unless a subscriber or MCAP recorder needs data.</param>
        /// <returns>True when payload preparation should continue.</returns>
        public bool TryPrepareRos2Publish(
            string topic,
            string schemaName,
            out uint channelId,
            bool requireDemand = true)
        {
            channelId = 0;

            if (SuppressLivePublishersForReplay)
                return false;

            if (!IsRunning)
                return false;

            channelId = GetOrRegisterRos2MsgSchemaChannel(topic, schemaName);
            return !requireDemand || _runtime.HasChannelDemand(channelId);
        }

        /// <summary>
        /// Returns the latest ROS2 Bridge runtime stats for Inspector and diagnostics.
        /// </summary>
        public Ros2BridgeStatsSnapshot GetRos2BridgeStatsSnapshot()
        {
            if (_ros2BridgeRuntime != null)
                return _ros2BridgeRuntime.GetStatsSnapshot();

            if (!string.IsNullOrEmpty(_ros2BridgeSetupError))
            {
                return new Ros2BridgeStatsSnapshot(
                    enabled: false,
                    connected: false,
                    connecting: false,
                    queuedFrames: 0,
                    sentFrames: 0,
                    droppedFrames: 0,
                    failedFrames: 0,
                    lastError: _ros2BridgeSetupError,
                    lastConnectedUnixMs: 0,
                    lastDisconnectedUnixMs: 0);
            }

            return Ros2BridgeStatsSnapshot.Disabled;
        }

        /// <summary>
        /// Return whether a publisher should prepare a ROS2 Bridge payload.
        /// This path is independent of the Foxglove WebSocket server and subscriber demand.
        /// </summary>
        /// <param name="topic">ROS 2 topic name.</param>
        /// <param name="schemaName">ROS 2 interface schema name.</param>
        /// <param name="reason">Human-readable skip reason when false.</param>
        /// <returns>True when payload preparation should continue.</returns>
        public bool TryPrepareRos2BridgePublish(string topic, string schemaName, out string reason)
        {
            reason = string.Empty;

            if (SuppressLivePublishersForReplay)
            {
                reason = "Replay is suppressing live publishers.";
                return false;
            }

            if (!_ros2BridgeEnabled)
            {
                reason = "ROS2 Bridge is disabled.";
                return false;
            }

            if (_ros2BridgeRuntime == null)
            {
                reason = string.IsNullOrEmpty(_ros2BridgeSetupError)
                    ? "ROS2 Bridge runtime is unavailable."
                    : _ros2BridgeSetupError;
                return false;
            }

            if (string.IsNullOrWhiteSpace(topic) || !topic.StartsWith("/", System.StringComparison.Ordinal))
            {
                reason = "ROS2 Bridge topic must start with '/'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(schemaName))
            {
                reason = "ROS2 Bridge schema name is required.";
                return false;
            }

            if (!FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out _))
            {
                reason = $"Unknown ROS2 Bridge schema '{schemaName}'.";
                return false;
            }

            return true;
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
        /// Publishes a ROS 2 CDR payload on a ROS 2 .msg schema channel.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="schemaName">ROS 2 interface schema name advertised to Foxglove.</param>
        /// <param name="payload">Serialized CDR payload, including little-endian encapsulation header.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishRos2Cdr(string topic, string schemaName, byte[] payload, ulong logTimeNs)
            => PublishRos2(topic, schemaName, payload, logTimeNs);

        /// <summary>
        /// Publishes a ROS 2 payload on a ROS 2 .msg schema channel using the user-facing ROS2 product path.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="schemaName">ROS 2 interface schema name advertised to Foxglove.</param>
        /// <param name="payload">Serialized CDR payload, including little-endian encapsulation header.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishRos2(string topic, string schemaName, byte[] payload, ulong logTimeNs)
        {
            if (SuppressLivePublishersForReplay)
            {
                return;
            }

            if (!IsRunning)
            {
                if (!_warnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishRos2 called but server is not running.");
                    _warnedNotRunning = true;
                }

                return;
            }

            var channelId = GetOrRegisterRos2MsgSchemaChannel(topic, schemaName);
            _runtime.PublishRos2Cdr(channelId, payload, logTimeNs);
        }

        /// <summary>
        /// Mirrors an already serialized ROS 2 CDR payload to the optional ROS2 Bridge sidecar.
        /// </summary>
        /// <param name="topic">ROS 2 topic name.</param>
        /// <param name="schemaName">ROS 2 interface schema name.</param>
        /// <param name="payload">Serialized CDR payload, including little-endian encapsulation header.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishRos2BridgeCdr(string topic, string schemaName, byte[] payload, ulong logTimeNs)
        {
            if (!TryPrepareRos2BridgePublish(topic, schemaName, out var reason))
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    Debug.LogWarning("[Foxglove] ROS2 Bridge publish skipped: " + reason);
                return;
            }

            Ros2CdrPayloadValidator.Validate(payload);

            var frame = new Ros2BridgeFrame(
                topic,
                schemaName,
                CdrEncoding,
                logTimeNs,
                ++_ros2BridgeSequence,
                payload);

            if (!_ros2BridgeRuntime.TryEnqueue(frame, out var enqueueReason))
                Debug.LogWarning("[Foxglove] ROS2 Bridge publish skipped: " + enqueueReason);
        }

        /// <summary>
        /// Gets or registers a schemaless channel for manual publish calls.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="encoding">Foxglove message encoding.</param>
        /// <returns>The channel identifier associated with the topic and encoding.</returns>
        private uint GetOrRegisterChannel(string topic, string encoding)
        {
            var key = (topic, EmptySchemaName, encoding, "");
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
