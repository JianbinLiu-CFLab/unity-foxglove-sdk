// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Full-descriptor identity for hidden FoxRun MCAP channels.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Components
{
    internal readonly struct FoxRunRawRecordingChannelDescriptor :
        IEquatable<FoxRunRawRecordingChannelDescriptor>
    {
        public FoxRunRawRecordingChannelDescriptor(
            string topic,
            string messageEncoding,
            string schemaName,
            string schemaEncoding,
            string schema)
        {
            Topic = topic ?? string.Empty;
            MessageEncoding = messageEncoding ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            SchemaEncoding = schemaEncoding ?? string.Empty;
            Schema = schema ?? string.Empty;
        }

        public string Topic { get; }
        public string MessageEncoding { get; }
        public string SchemaName { get; }
        public string SchemaEncoding { get; }
        public string Schema { get; }

        public AdvertiseChannel ToChannel(uint channelId)
            => new AdvertiseChannel
            {
                Id = channelId,
                Topic = Topic,
                Encoding = MessageEncoding,
                SchemaName = SchemaName,
                SchemaEncoding = SchemaEncoding,
                Schema = Schema
            };

        public bool Equals(FoxRunRawRecordingChannelDescriptor other)
            => string.Equals(Topic, other.Topic, StringComparison.Ordinal)
               && string.Equals(
                   MessageEncoding,
                   other.MessageEncoding,
                   StringComparison.Ordinal)
               && string.Equals(
                   SchemaName,
                   other.SchemaName,
                   StringComparison.Ordinal)
               && string.Equals(
                   SchemaEncoding,
                   other.SchemaEncoding,
                   StringComparison.Ordinal)
               && string.Equals(Schema, other.Schema, StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is FoxRunRawRecordingChannelDescriptor other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(Topic);
                hash = (hash * 397)
                       ^ StringComparer.Ordinal.GetHashCode(MessageEncoding);
                hash = (hash * 397)
                       ^ StringComparer.Ordinal.GetHashCode(SchemaName);
                hash = (hash * 397)
                       ^ StringComparer.Ordinal.GetHashCode(SchemaEncoding);
                return (hash * 397)
                       ^ StringComparer.Ordinal.GetHashCode(Schema);
            }
        }
    }

    internal sealed class FoxRunRawRecordingChannelCache
    {
        private readonly Dictionary<FoxRunRawRecordingChannelDescriptor, uint>
            _channels =
                new Dictionary<FoxRunRawRecordingChannelDescriptor, uint>();

        public uint GetOrAdd(
            FoxRunRawRecordingChannelDescriptor descriptor,
            Func<uint> allocateChannelId)
        {
            if (_channels.TryGetValue(descriptor, out var channelId))
                return channelId;
            if (allocateChannelId == null)
                throw new ArgumentNullException(nameof(allocateChannelId));

            channelId = allocateChannelId();
            _channels.Add(descriptor, channelId);
            return channelId;
        }

        public void Clear() => _channels.Clear();
    }
}
