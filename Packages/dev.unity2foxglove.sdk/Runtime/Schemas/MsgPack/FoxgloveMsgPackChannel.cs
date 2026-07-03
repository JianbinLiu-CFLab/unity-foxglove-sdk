// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MsgPack
// Purpose: SDK-style MessagePack channel wrapper.

using System;
using Unity.FoxgloveSDK.Schemas.MsgPack;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveMsgPackChannel
    {
        private readonly FoxgloveManager _manager;
        private readonly ulong _generation;

        internal FoxgloveMsgPackChannel(
            FoxgloveManager manager,
            ulong generation,
            uint channelId,
            string topic)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _generation = generation;
            ChannelId = channelId;
            Topic = topic;
        }

        public string Topic { get; }
        public uint ChannelId { get; }
        public string Encoding => "msgpack";

        /// <summary>Publish MessagePack bytes on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(byte[] payload) => Log(payload, _manager.NowNs);

        /// <summary>Publish MessagePack bytes on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(byte[] payload, ulong timestampNs)
            => _manager.PublishMsgPackChannel(_generation, ChannelId, Topic, payload, timestampNs);

        /// <summary>Publish the current contents of a MessagePack writer on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(FoxgloveMsgPackWriter writer) => Log(writer, _manager.NowNs);

        /// <summary>Publish the current contents of a MessagePack writer on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(FoxgloveMsgPackWriter writer, ulong timestampNs)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            Log(writer.ToArray(), timestampNs);
        }
    }
}
