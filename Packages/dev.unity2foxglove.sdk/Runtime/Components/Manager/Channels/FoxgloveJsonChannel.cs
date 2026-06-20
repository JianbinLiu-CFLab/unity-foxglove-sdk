// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: SDK-style JSON channel wrapper.

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveJsonChannel
    {
        private readonly FoxgloveManager _manager;
        private readonly ulong _generation;

        internal FoxgloveJsonChannel(FoxgloveManager manager, ulong generation, uint channelId, string topic, string schemaName)
        {
            _manager = manager;
            _generation = generation;
            ChannelId = channelId;
            Topic = topic;
            SchemaName = schemaName;
        }

        public string Topic { get; }
        public uint ChannelId { get; }
        public string SchemaName { get; }

        /// <summary>Publish a JSON-serialized sample on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(object message) => Log(message, _manager.NowNs);

        /// <summary>Publish a JSON-serialized sample on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(object message, ulong timestampNs)
            => _manager.PublishJsonChannel(_generation, ChannelId, Topic, message, timestampNs);
    }
}
