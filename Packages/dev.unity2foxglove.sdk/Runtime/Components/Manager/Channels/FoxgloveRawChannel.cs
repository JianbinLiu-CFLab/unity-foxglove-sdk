// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: SDK-style raw byte channel wrapper.

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveRawChannel
    {
        private readonly FoxgloveManager _manager;
        private readonly ulong _generation;

        internal FoxgloveRawChannel(
            FoxgloveManager manager,
            ulong generation,
            uint channelId,
            string topic,
            string encoding,
            string schemaName)
        {
            _manager = manager;
            _generation = generation;
            ChannelId = channelId;
            Topic = topic;
            Encoding = encoding;
            SchemaName = schemaName;
        }

        public string Topic { get; }
        public uint ChannelId { get; }
        public string Encoding { get; }
        public string SchemaName { get; }

        /// <summary>Publish raw bytes on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(byte[] payload) => Log(payload, _manager.NowNs);

        /// <summary>Publish raw bytes on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(byte[] payload, ulong timestampNs)
            => _manager.PublishRawChannel(_generation, ChannelId, Topic, Encoding, payload, timestampNs);
    }
}
