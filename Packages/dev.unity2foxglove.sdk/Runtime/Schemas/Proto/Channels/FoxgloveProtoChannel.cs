// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: SDK-style protobuf channel wrapper.

using System;
using Foxglove.Schemas;
using Google.Protobuf;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxgloveProtoChannel<T> where T : class, Google.Protobuf.IMessage
    {
        private readonly FoxgloveManager _manager;
        private readonly ulong _generation;

        internal FoxgloveProtoChannel(FoxgloveManager manager, ulong generation, uint channelId, string topic, string schemaName)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _generation = generation;
            ChannelId = channelId;
            Topic = topic;
            SchemaName = schemaName;
        }

        public string Topic { get; }
        public uint ChannelId { get; }
        public string SchemaName { get; }

        /// <summary>Publish a protobuf sample on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(T message) => Log(message, _manager.NowNs);

        /// <summary>Publish a protobuf sample on this session-bound channel.</summary>
        /// <remarks>Call from the Unity main thread and recreate the wrapper after restarting the server.</remarks>
        public void Log(T message, ulong timestampNs)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            _manager.PublishProtoChannel(_generation, ChannelId, Topic, message.ToByteArray(), timestampNs);
        }
    }

    public static class FoxgloveProtoChannelExtensions
    {
        /// <summary>Create or reuse a protobuf channel for the current running Foxglove session.</summary>
        /// <remarks>Call from the Unity main thread, matching the manager's publishing lifecycle contract.</remarks>
        public static FoxgloveProtoChannel<T> CreateProtoChannel<T>(this FoxgloveManager manager, string topic)
            where T : class, Google.Protobuf.IMessage
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            if (!FoxgloveProtoSchemaCatalog.TryGetByClrType(typeof(T), out var entry))
                throw new InvalidOperationException($"Unknown Foxglove protobuf message type '{typeof(T).FullName}'.");

            return manager.CreateProtoChannel<T>(topic, entry.SchemaName);
        }

        /// <summary>Create or reuse a protobuf channel for the current running Foxglove session.</summary>
        /// <remarks>Call from the Unity main thread, matching the manager's publishing lifecycle contract.</remarks>
        public static FoxgloveProtoChannel<T> CreateProtoChannel<T>(this FoxgloveManager manager, string topic, string schemaName)
            where T : class, Google.Protobuf.IMessage
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            if (string.IsNullOrWhiteSpace(schemaName))
                throw new ArgumentException("Protobuf channels require a schema name.", nameof(schemaName));

            if (!FoxgloveProtoSchemaCatalog.TryGet(schemaName, out var entry))
                throw new InvalidOperationException($"Unknown Foxglove protobuf schema '{schemaName}'.");

            if (entry.ClrType != typeof(T))
            {
                throw new InvalidOperationException(
                    $"Foxglove protobuf schema '{schemaName}' maps to '{entry.ClrType.FullName}', " +
                    $"not '{typeof(T).FullName}'.");
            }

            var channelId = manager.GetOrRegisterSchemaChannel(topic, schemaName, "protobuf");
            return new FoxgloveProtoChannel<T>(manager, manager.CurrentChannelSessionGeneration, channelId, topic, schemaName);
        }
    }
}
