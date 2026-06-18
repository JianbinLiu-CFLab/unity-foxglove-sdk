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

        internal FoxgloveProtoChannel(FoxgloveManager manager, uint channelId, string topic, string schemaName)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            ChannelId = channelId;
            Topic = topic;
            SchemaName = schemaName;
        }

        public string Topic { get; }
        public uint ChannelId { get; }
        public string SchemaName { get; }

        public void Log(T message) => Log(message, _manager.NowNs);

        public void Log(T message, ulong timestampNs)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            _manager.PublishProto(Topic, SchemaName, message.ToByteArray(), timestampNs);
        }
    }

    public static class FoxgloveProtoChannelExtensions
    {
        public static FoxgloveProtoChannel<T> CreateProtoChannel<T>(this FoxgloveManager manager, string topic)
            where T : class, Google.Protobuf.IMessage
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            if (!FoxgloveProtoSchemaCatalog.TryGetByClrType(typeof(T), out var entry))
                throw new InvalidOperationException($"Unknown Foxglove protobuf message type '{typeof(T).FullName}'.");

            return manager.CreateProtoChannel<T>(topic, entry.SchemaName);
        }

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
            return new FoxgloveProtoChannel<T>(manager, channelId, topic, schemaName);
        }
    }
}
