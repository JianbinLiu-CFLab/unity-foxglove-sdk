// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/McapConformance
// Purpose: Conservative C# writer bridge for official MCAP conformance
// writer tests covered by Phase 122 direct-record option parity.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests.McapConformance
{
    internal static class McapConformanceWriter
    {
        public static int Write(string testcaseJsonPath, string featureCsv, Stream stdout, TextWriter stderr)
        {
            var testcase = JObject.Parse(File.ReadAllText(testcaseJsonPath));
            var records = testcase["records"] as JArray ?? new JArray();
            var features = ParseFeatures(featureCsv, testcase);
            var options = CreateOptionsFromFeatures(features);

            if (features.Contains("pad"))
                return Unsupported("extra record padding is not implemented", stderr);

            var dataRecords = records.OfType<JObject>()
                .TakeWhile(r => !string.Equals((string)r["type"], "DataEnd", StringComparison.Ordinal))
                .ToList();
            if (!dataRecords.Any(r => string.Equals((string)r["type"], "Header", StringComparison.Ordinal)))
                return Unsupported("test case does not contain a Header record", stderr);

            using var stream = new MemoryStream();
            using var writer = new McapWriter(stream, leaveOpen: true);
            writer.WriteMagic();

            var schemas = new List<SchemaState>();
            var channels = new List<ChannelState>();
            var schemasByInputId = new Dictionary<ushort, SchemaState>();
            var channelsByInputId = new Dictionary<ushort, ChannelState>();
            var writtenSchemaIds = new HashSet<ushort>();
            var writtenChannelIds = new HashSet<ushort>();
            var metadataIndexes = new List<MetadataIndexState>();
            var attachmentIndexes = new List<McapAttachmentIndex>();
            var chunkIndexes = new List<ChunkIndexState>();
            var channelMessageCounts = new Dictionary<ushort, ulong>();
            var chunkMessageIndexes = new Dictionary<ushort, List<(ulong, ulong)>>();
            ulong messageCount = 0;
            ulong messageStartTime = ulong.MaxValue;
            ulong messageEndTime = 0;
            uint metadataCount = 0;
            uint attachmentCount = 0;
            ushort nextSchemaId = 1;
            ushort nextChannelId = 1;

            using var chunkStream = new MemoryStream();
            using var chunkWriter = new McapWriter(chunkStream, leaveOpen: true);

            foreach (var record in dataRecords)
            {
                var type = (string)record["type"] ?? "";
                var fields = Fields(record);
                switch (type)
                {
                    case "Header":
                        writer.WriteHeader(S(fields, "profile"), S(fields, "library"));
                        break;
                    case "Schema":
                    {
                        var schema = new SchemaState
                        {
                            Id = nextSchemaId++,
                            Name = S(fields, "name"),
                            Encoding = S(fields, "encoding"),
                            Data = Bytes(fields, "data")
                        };
                        schemas.Add(schema);
                        schemasByInputId.Add(U16(fields, "id"), schema);
                        break;
                    }
                    case "Channel":
                    {
                        var inputSchemaId = U16(fields, "schema_id");
                        var channel = new ChannelState
                        {
                            Id = nextChannelId++,
                            SchemaId = inputSchemaId == 0
                                ? (ushort)0
                                : schemasByInputId.TryGetValue(inputSchemaId, out var schema)
                                    ? schema.Id
                                    : throw new InvalidDataException("Channel references unknown schema " + inputSchemaId + "."),
                            Topic = S(fields, "topic"),
                            MessageEncoding = S(fields, "message_encoding"),
                            Metadata = Map(fields, "metadata")
                        };
                        channels.Add(channel);
                        channelsByInputId.Add(U16(fields, "id"), channel);
                        break;
                    }
                    case "Message":
                    {
                        var inputChannelId = U16(fields, "channel_id");
                        if (!channelsByInputId.TryGetValue(inputChannelId, out var channel))
                            throw new InvalidDataException("Message references unknown channel " + inputChannelId + ".");

                        var targetWriter = options.UseChunking ? chunkWriter : writer;
                        if (writtenChannelIds.Add(channel.Id))
                        {
                            if (channel.SchemaId != 0 && writtenSchemaIds.Add(channel.SchemaId))
                            {
                                var schema = schemas.Single(item => item.Id == channel.SchemaId);
                                targetWriter.WriteSchema(schema.Id, schema.Name, schema.Encoding, schema.Data);
                            }
                            targetWriter.WriteChannel(
                                channel.Id,
                                channel.SchemaId,
                                channel.Topic,
                                channel.MessageEncoding,
                                channel.Metadata);
                            if (options.HasIndex(McapIndexTypes.Message))
                                chunkMessageIndexes.Add(channel.Id, new List<(ulong, ulong)>());
                        }

                        var logTime = U64(fields, "log_time");
                        if (options.UseChunking && options.HasIndex(McapIndexTypes.Message))
                            chunkMessageIndexes[channel.Id].Add((logTime, (ulong)chunkStream.Position));
                        targetWriter.WriteMessage(
                            channel.Id,
                            U32(fields, "sequence"),
                            logTime,
                            U64(fields, "publish_time"),
                            Bytes(fields, "data"));
                        messageCount++;
                        channelMessageCounts[channel.Id] = channelMessageCounts.TryGetValue(channel.Id, out var count) ? count + 1 : 1;
                        if (logTime < messageStartTime) messageStartTime = logTime;
                        if (logTime > messageEndTime) messageEndTime = logTime;
                        break;
                    }
                    case "Metadata":
                    {
                        var offset = (ulong)writer.Position;
                        writer.WriteMetadata(S(fields, "name"), Map(fields, "metadata"));
                        var length = (ulong)writer.Position - offset;
                        if (features.Contains("mdx"))
                            metadataIndexes.Add(new MetadataIndexState { Offset = offset, Length = length, Name = S(fields, "name") });
                        metadataCount++;
                        break;
                    }
                    case "Attachment":
                    {
                        var index = writer.WriteAttachment(U64(fields, "log_time"), U64(fields, "create_time"), S(fields, "name"), S(fields, "media_type"), Bytes(fields, "data"));
                        if (features.Contains("ax"))
                            attachmentIndexes.Add(index);
                        attachmentCount++;
                        break;
                    }
                    default:
                        return Unsupported("unsupported direct writer record type: " + type, stderr);
                }
            }

            uint chunkCount = 0;
            if (options.UseChunking && messageCount > 0)
            {
                chunkWriter.Flush();
                var raw = chunkStream.ToArray();
                var chunkOffset = (ulong)writer.Position;
                writer.WriteChunk(
                    messageStartTime,
                    messageEndTime,
                    (ulong)raw.Length,
                    Crc32Helper.Compute(raw),
                    "",
                    (ulong)raw.Length,
                    raw);
                var chunkLength = (ulong)writer.Position - chunkOffset;
                var messageIndexOffsets = new Dictionary<ushort, ulong>();
                ulong messageIndexLength = 0;
                foreach (var item in chunkMessageIndexes)
                {
                    var indexOffset = (ulong)writer.Position;
                    writer.WriteMessageIndex(item.Key, item.Value);
                    messageIndexOffsets[item.Key] = indexOffset;
                    messageIndexLength += (ulong)writer.Position - indexOffset;
                }

                if (options.HasIndex(McapIndexTypes.Chunk))
                {
                    chunkIndexes.Add(new ChunkIndexState
                    {
                        StartTime = messageStartTime,
                        EndTime = messageEndTime,
                        ChunkOffset = chunkOffset,
                        ChunkLength = chunkLength,
                        MessageIndexOffsets = messageIndexOffsets,
                        MessageIndexLength = messageIndexLength,
                        CompressedSize = (ulong)raw.Length,
                        UncompressedSize = (ulong)raw.Length
                    });
                }
                chunkCount = 1;
            }

            writer.WriteDataEnd(writer.ComputeCrc32FromStartToCurrent());
            var summaryStart = (ulong)writer.Position;

            using var summaryBuilder = new MemoryStream();
            using var summaryWriter = new McapWriter(summaryBuilder, leaveOpen: true);

            var schemaStart = (ulong)summaryBuilder.Position;
            if (options.RepeatSchemas)
            {
                foreach (var schema in schemas)
                    summaryWriter.WriteSchema(schema.Id, schema.Name, schema.Encoding, schema.Data);
            }
            var schemaLength = (ulong)summaryBuilder.Position - schemaStart;

            var channelStart = (ulong)summaryBuilder.Position;
            if (options.RepeatChannels)
            {
                foreach (var channel in channels)
                    summaryWriter.WriteChannel(
                        channel.Id,
                        channel.SchemaId,
                        channel.Topic,
                        channel.MessageEncoding,
                        channel.Metadata);
            }
            var channelLength = (ulong)summaryBuilder.Position - channelStart;

            var statsStart = (ulong)summaryBuilder.Position;
            if (options.UseStatistics)
            {
                summaryWriter.WriteStatistics(
                    messageCount,
                    checked((ushort)schemas.Count),
                    checked((uint)channels.Count),
                    attachmentCount,
                    metadataCount,
                    chunkCount,
                    messageCount > 0 ? messageStartTime : 0,
                    messageCount > 0 ? messageEndTime : 0,
                    channelMessageCounts);
            }
            var statsLength = (ulong)summaryBuilder.Position - statsStart;

            var metadataIndexStart = (ulong)summaryBuilder.Position;
            foreach (var index in metadataIndexes)
                summaryWriter.WriteMetadataIndex(index.Offset, index.Length, index.Name);
            var metadataIndexLength = (ulong)summaryBuilder.Position - metadataIndexStart;

            var attachmentIndexStart = (ulong)summaryBuilder.Position;
            foreach (var index in attachmentIndexes)
                summaryWriter.WriteAttachmentIndex(index);
            var attachmentIndexLength = (ulong)summaryBuilder.Position - attachmentIndexStart;

            var chunkIndexStart = (ulong)summaryBuilder.Position;
            foreach (var index in chunkIndexes)
            {
                summaryWriter.WriteChunkIndex(
                    index.StartTime,
                    index.EndTime,
                    index.ChunkOffset,
                    index.ChunkLength,
                    index.MessageIndexOffsets,
                    index.MessageIndexLength,
                    "",
                    index.CompressedSize,
                    index.UncompressedSize);
            }
            var chunkIndexLength = (ulong)summaryBuilder.Position - chunkIndexStart;

            ulong summaryOffsetStart = 0;
            if (options.UseSummaryOffsets)
            {
                summaryOffsetStart = summaryStart + (ulong)summaryBuilder.Position;
                if (schemaLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeSchema, summaryStart + schemaStart, schemaLength);
                if (channelLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeChannel, summaryStart + channelStart, channelLength);
                if (statsLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeStatistics, summaryStart + statsStart, statsLength);
                if (metadataIndexLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeMetadataIndex, summaryStart + metadataIndexStart, metadataIndexLength);
                if (attachmentIndexLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeAttachmentIndex, summaryStart + attachmentIndexStart, attachmentIndexLength);
                if (chunkIndexLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeChunkIndex, summaryStart + chunkIndexStart, chunkIndexLength);
            }

            summaryWriter.Flush();
            if (!summaryBuilder.TryGetBuffer(out var summaryData))
                throw new InvalidOperationException("MCAP summary buffer is not publicly visible.");
            var hasSummary = summaryData.Count > 0;
            var footerSummaryStart = hasSummary ? summaryStart : 0UL;
            if (!hasSummary)
                summaryOffsetStart = 0;
            var footerPrefix = McapWriter.BuildFooterCrcPrefix(footerSummaryStart, summaryOffsetStart);
            var crc = Crc32Helper.Initialize();
            crc = Crc32Helper.Update(
                crc,
                new ReadOnlySpan<byte>(summaryData.Array, summaryData.Offset, summaryData.Count));
            crc = Crc32Helper.Update(crc, footerPrefix);
            var summaryCrc = Crc32Helper.Finalize(crc);
            writer.WriteBytes(summaryData);
            writer.WriteFooter(footerSummaryStart, summaryOffsetStart, summaryCrc);
            writer.WriteMagic();
            writer.Flush();

            var bytes = stream.ToArray();
            stdout.Write(bytes, 0, bytes.Length);
            return 0;
        }

        private static int Unsupported(string reason, TextWriter stderr)
        {
            stderr.WriteLine("Unsupported: " + reason);
            return 2;
        }

        private static McapWriterOptions CreateOptionsFromFeatures(ISet<string> features)
        {
            var indexTypes = McapIndexTypes.None;
            if (features.Contains("mx")) indexTypes |= McapIndexTypes.Message;
            if (features.Contains("chx")) indexTypes |= McapIndexTypes.Chunk;
            if (features.Contains("ax")) indexTypes |= McapIndexTypes.Attachment;
            if (features.Contains("mdx")) indexTypes |= McapIndexTypes.Metadata;

            return new McapWriterOptions
            {
                UseChunking = features.Contains("ch"),
                IndexTypes = indexTypes,
                RepeatSchemas = features.Contains("rsh"),
                RepeatChannels = features.Contains("rch"),
                UseStatistics = features.Contains("st"),
                UseSummaryOffsets = features.Contains("sum"),
                EnableCrcs = true,
                EnableDataCrcs = true
            };
        }

        private static ISet<string> ParseFeatures(string featureCsv, JObject testcase)
        {
            var features = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(featureCsv))
            {
                foreach (var item in featureCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    features.Add(item.Trim());
            }

            var metaFeatures = testcase["meta"]?["variant"]?["features"] as JArray;
            if (metaFeatures != null)
            {
                foreach (var item in metaFeatures)
                    features.Add((string)item);
            }

            return features;
        }

        private static Dictionary<string, JToken> Fields(JObject record)
        {
            var result = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (var field in record["fields"] as JArray ?? new JArray())
            {
                if (field is JArray pair && pair.Count == 2)
                    result[(string)pair[0]] = pair[1];
            }
            return result;
        }

        private static string S(Dictionary<string, JToken> fields, string key)
            => fields.TryGetValue(key, out var value) ? (string)value ?? "" : "";

        private static ushort U16(Dictionary<string, JToken> fields, string key)
            => ushort.Parse(S(fields, key), CultureInfo.InvariantCulture);

        private static uint U32(Dictionary<string, JToken> fields, string key)
            => uint.Parse(S(fields, key), CultureInfo.InvariantCulture);

        private static ulong U64(Dictionary<string, JToken> fields, string key)
            => ulong.Parse(S(fields, key), CultureInfo.InvariantCulture);

        private static byte[] Bytes(Dictionary<string, JToken> fields, string key)
        {
            if (!fields.TryGetValue(key, out var value) || value is not JArray array)
                return Array.Empty<byte>();
            var bytes = new byte[array.Count];
            for (var i = 0; i < array.Count; i++)
                bytes[i] = byte.Parse((string)array[i], CultureInfo.InvariantCulture);
            return bytes;
        }

        private static Dictionary<string, string> Map(Dictionary<string, JToken> fields, string key)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!fields.TryGetValue(key, out var value) || value is not JObject obj)
                return result;
            foreach (var property in obj.Properties())
                result[property.Name] = (string)property.Value ?? "";
            return result;
        }

        private struct MetadataIndexState
        {
            public ulong Offset;
            public ulong Length;
            public string Name;
        }

        private sealed class SchemaState
        {
            public ushort Id;
            public string Name;
            public string Encoding;
            public byte[] Data;
        }

        private sealed class ChannelState
        {
            public ushort Id;
            public ushort SchemaId;
            public string Topic;
            public string MessageEncoding;
            public Dictionary<string, string> Metadata;
        }

        private sealed class ChunkIndexState
        {
            public ulong StartTime;
            public ulong EndTime;
            public ulong ChunkOffset;
            public ulong ChunkLength;
            public Dictionary<ushort, ulong> MessageIndexOffsets;
            public ulong MessageIndexLength;
            public ulong CompressedSize;
            public ulong UncompressedSize;
        }
    }
}
