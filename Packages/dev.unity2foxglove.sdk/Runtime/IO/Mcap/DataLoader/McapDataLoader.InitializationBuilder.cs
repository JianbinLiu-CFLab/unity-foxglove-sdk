// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Builds MCAP DataLoader initialization DTOs from indexed summaries.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    internal static class McapDataLoaderInitializationBuilder
    {
        public static void AddSchemas(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapSchema> schemas)
        {
            if (schemas == null)
                return;

            for (var i = 0; i < schemas.Count; i++)
            {
                var schema = schemas[i];
                if (schema == null)
                    continue;

                initialization.Schemas.Add(new McapDataLoaderSchema
                {
                    SchemaId = schema.Id,
                    Name = schema.Name ?? string.Empty,
                    Encoding = schema.Encoding ?? string.Empty,
                    Data = schema.Data ?? Array.Empty<byte>()
                });
            }
        }

        public static void AddChannels(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapChannel> channels,
            McapStatistics statistics)
        {
            if (channels == null)
                return;

            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel == null)
                    continue;

                var dto = new McapDataLoaderChannel
                {
                    ChannelId = channel.Id,
                    SchemaId = channel.SchemaId,
                    Topic = channel.Topic ?? string.Empty,
                    MessageEncoding = channel.MessageEncoding ?? string.Empty
                };

                if (statistics?.ChannelMessageCounts != null &&
                    statistics.ChannelMessageCounts.TryGetValue(channel.Id, out var count))
                {
                    dto.HasMessageCount = true;
                    dto.MessageCount = count;
                }

                initialization.Channels.Add(dto);
            }
        }

        public static void AddTimeRange(
            McapDataLoaderInitialization initialization,
            McapFileSummary summary)
        {
            if (summary?.Statistics != null && summary.Statistics.MessageCount > 0)
            {
                initialization.TimeRange.HasRange = true;
                initialization.TimeRange.StartTimeNs = summary.Statistics.MessageStartTime;
                initialization.TimeRange.EndTimeNs = summary.Statistics.MessageEndTime;
                return;
            }

            if (summary?.ChunkIndexes == null || summary.ChunkIndexes.Count == 0)
                return;

            var hasRange = false;
            var start = ulong.MaxValue;
            var end = 0UL;
            for (var i = 0; i < summary.ChunkIndexes.Count; i++)
            {
                var chunk = summary.ChunkIndexes[i];
                if (chunk == null)
                    continue;

                hasRange = true;
                if (chunk.MessageStartTime < start)
                    start = chunk.MessageStartTime;
                if (chunk.MessageEndTime > end)
                    end = chunk.MessageEndTime;
            }

            if (!hasRange)
                return;

            initialization.TimeRange.HasRange = true;
            initialization.TimeRange.StartTimeNs = start;
            initialization.TimeRange.EndTimeNs = end;
        }

        public static void AddMetadataIndexes(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapMetadataIndex> metadataIndexes)
        {
            if (metadataIndexes == null)
                return;

            for (var i = 0; i < metadataIndexes.Count; i++)
            {
                var index = metadataIndexes[i];
                if (index == null)
                    continue;

                initialization.MetadataIndexes.Add(new McapDataLoaderMetadataIndex
                {
                    Name = index.Name ?? string.Empty,
                    Offset = index.Offset,
                    Length = index.Length
                });
            }
        }

        public static void AddAttachmentIndexes(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapAttachmentIndex> attachmentIndexes)
        {
            if (attachmentIndexes == null)
                return;

            for (var i = 0; i < attachmentIndexes.Count; i++)
            {
                var index = attachmentIndexes[i];
                if (index == null)
                    continue;

                initialization.AttachmentIndexes.Add(new McapDataLoaderAttachmentIndex
                {
                    Name = index.Name ?? string.Empty,
                    MediaType = index.MediaType ?? string.Empty,
                    Offset = index.Offset,
                    Length = index.Length,
                    LogTime = index.LogTime,
                    CreateTime = index.CreateTime,
                    DataSize = index.DataSize
                });
            }
        }

        public static void AddSummaryCounts(
            McapDataLoaderInitialization initialization,
            McapStatistics statistics)
        {
            if (statistics == null)
                return;

            initialization.HasTotalMessageCount = true;
            initialization.TotalMessageCount = statistics.MessageCount;
        }
    }
}
