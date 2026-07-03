// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: SDK-style channel facade factories and channel-id publish helpers.

using System;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private ulong _channelSessionGeneration;

        /// <summary>
        /// Create or reuse a JSON channel for the current running Foxglove session.
        /// </summary>
        /// <remarks>Call from the Unity main thread, matching the manager's publishing lifecycle contract.</remarks>
        public FoxgloveJsonChannel CreateJsonChannel(string topic, string schemaName = "")
        {
            EnsureChannelFactoryCanRegister();
            var channelId = string.IsNullOrEmpty(schemaName)
                ? GetOrRegisterChannel(topic, JsonEncoding)
                : GetOrRegisterSchemaChannel(topic, schemaName, JsonEncoding);
            return new FoxgloveJsonChannel(this, _channelSessionGeneration, channelId, topic, schemaName ?? string.Empty);
        }

        /// <summary>
        /// Create or reuse a raw byte channel for the current running Foxglove session.
        /// </summary>
        /// <remarks>Call from the Unity main thread, matching the manager's publishing lifecycle contract.</remarks>
        public FoxgloveRawChannel CreateRawChannel(string topic, string encoding, string schemaName = "")
        {
            if (string.IsNullOrWhiteSpace(encoding))
                throw new ArgumentException("Channel encoding must be non-empty.", nameof(encoding));

            EnsureChannelFactoryCanRegister();
            var normalizedSchemaName = schemaName ?? string.Empty;
            var channelId = string.IsNullOrEmpty(normalizedSchemaName)
                ? GetOrRegisterChannel(topic, encoding)
                : GetOrRegisterSchemaChannel(topic, normalizedSchemaName, encoding);
            return new FoxgloveRawChannel(this, _channelSessionGeneration, channelId, topic, encoding, normalizedSchemaName);
        }

        /// <summary>
        /// Create or reuse a schemaless MessagePack channel for the current running Foxglove session.
        /// </summary>
        /// <remarks>Call from the Unity main thread, matching the manager's publishing lifecycle contract.</remarks>
        public FoxgloveMsgPackChannel CreateMsgPackChannel(string topic)
        {
            EnsureChannelFactoryCanRegister();
            var channelId = GetOrRegisterChannel(topic, MsgPackEncoding);
            return new FoxgloveMsgPackChannel(this, _channelSessionGeneration, channelId, topic);
        }

        internal ulong CurrentChannelSessionGeneration => _channelSessionGeneration;

        internal void PublishJsonChannel(ulong generation, uint channelId, string topic, object message, ulong timestampNs)
        {
            if (!TryPrepareChannelLog(generation, topic, "publish JSON channel"))
                return;

            _runtime.PublishJson(channelId, message, timestampNs);
            RecordPublishCadence(topic, JsonEncoding);
        }

        internal void PublishRawChannel(ulong generation, uint channelId, string topic, string encoding, byte[] payload, ulong timestampNs)
        {
            if (!TryPrepareChannelLog(generation, topic, "publish raw channel"))
                return;

            _runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), timestampNs);
            RecordPublishCadence(topic, encoding);
        }

        internal void PublishProtoChannel(ulong generation, uint channelId, string topic, byte[] payload, ulong timestampNs)
        {
            if (!TryPrepareChannelLog(generation, topic, "publish protobuf channel"))
                return;

            _runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), timestampNs);
            RecordPublishCadence(topic, ProtobufEncoding);
        }

        internal void PublishMsgPackChannel(ulong generation, uint channelId, string topic, byte[] payload, ulong timestampNs)
        {
            if (!TryPrepareChannelLog(generation, topic, "publish MsgPack channel"))
                return;

            _runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), timestampNs);
            RecordPublishCadence(topic, MsgPackEncoding);
        }

        private void EnsureChannelFactoryCanRegister()
        {
            if (!IsRunning)
                throw new InvalidOperationException("Foxglove channel factories require a running server.");
        }

        private bool TryPrepareChannelLog(ulong generation, string topic, string operation)
        {
            ValidateChannelSessionGeneration(generation);

            if (SuppressLivePublishersForReplay)
                return false;

            if (!IsRunning)
            {
                if (_foxgloveOutputEnabled && !_warnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] Channel Log called but server is not running.");
                    _warnedNotRunning = true;
                }

                return false;
            }

            return TryValidatePublishTopic(topic, operation);
        }

        private void ValidateChannelSessionGeneration(ulong generation)
        {
            if (generation != _channelSessionGeneration)
            {
                throw new InvalidOperationException(
                    "Foxglove channel belongs to an old session. Re-create the channel after restarting the server.");
            }
        }

        private void AdvanceChannelSessionGeneration()
        {
            unchecked
            {
                _channelSessionGeneration++;
                if (_channelSessionGeneration == 0)
                    _channelSessionGeneration = 1;
            }
        }
    }
}
