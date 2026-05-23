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
            if (options.UseChunking)
                return Unsupported("chunked official writer byte parity is deferred", stderr);

            var dataRecords = records.OfType<JObject>()
                .TakeWhile(r => !string.Equals((string)r["type"], "DataEnd", StringComparison.Ordinal))
                .ToList();
            if (!dataRecords.Any(r => string.Equals((string)r["type"], "Header", StringComparison.Ordinal)))
                return Unsupported("test case does not contain a Header record", stderr);

            using var stream = new MemoryStream();
            using var writer = new McapWriter(stream, leaveOpen: true);
            writer.WriteMagic();

            var schemas = new List<JObject>();
            var channels = new List<JObject>();
            var metadataIndexes = new List<MetadataIndexState>();
            var attachmentIndexes = new List<McapAttachmentIndex>();
            var channelMessageCounts = new Dictionary<ushort, ulong>();
            ulong messageCount = 0;
            ulong messageStartTime = ulong.MaxValue;
            ulong messageEndTime = 0;
            uint metadataCount = 0;
            uint attachmentCount = 0;

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
                        writer.WriteSchema(U16(fields, "id"), S(fields, "name"), S(fields, "encoding"), Bytes(fields, "data"));
                        schemas.Add(record);
                        break;
                    case "Channel":
                        writer.WriteChannel(U16(fields, "id"), U16(fields, "schema_id"), S(fields, "topic"), S(fields, "message_encoding"), Map(fields, "metadata"));
                        channels.Add(record);
                        break;
                    case "Message":
                    {
                        var channelId = U16(fields, "channel_id");
                        var logTime = U64(fields, "log_time");
                        writer.WriteMessage(channelId, U32(fields, "sequence"), logTime, U64(fields, "publish_time"), Bytes(fields, "data"));
                        messageCount++;
                        channelMessageCounts[channelId] = channelMessageCounts.TryGetValue(channelId, out var count) ? count + 1 : 1;
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

            writer.WriteDataEnd(writer.ComputeCrc32FromStartToCurrent());
            var summaryStart = (ulong)writer.Position;

            using var summaryBuilder = new MemoryStream();
            using var summaryWriter = new McapWriter(summaryBuilder, leaveOpen: true);

            var schemaStart = (ulong)summaryBuilder.Position;
            if (options.RepeatSchemas)
            {
                foreach (var schema in schemas)
                {
                    var fields = Fields(schema);
                    summaryWriter.WriteSchema(U16(fields, "id"), S(fields, "name"), S(fields, "encoding"), Bytes(fields, "data"));
                }
            }
            var schemaLength = (ulong)summaryBuilder.Position - schemaStart;

            var channelStart = (ulong)summaryBuilder.Position;
            if (options.RepeatChannels)
            {
                foreach (var channel in channels)
                {
                    var fields = Fields(channel);
                    summaryWriter.WriteChannel(U16(fields, "id"), U16(fields, "schema_id"), S(fields, "topic"), S(fields, "message_encoding"), Map(fields, "metadata"));
                }
            }
            var channelLength = (ulong)summaryBuilder.Position - channelStart;

            var statsStart = (ulong)summaryBuilder.Position;
            if (options.UseStatistics)
            {
                summaryWriter.WriteStatistics(
                    messageCount,
                    (ushort)schemas.Count,
                    (uint)channels.Count,
                    attachmentCount,
                    metadataCount,
                    0,
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

            ulong summaryOffsetStart = 0;
            if (options.UseSummaryOffsets)
            {
                summaryOffsetStart = summaryStart + (ulong)summaryBuilder.Position;
                if (schemaLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeSchema, summaryStart + schemaStart, schemaLength);
                if (channelLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeChannel, summaryStart + channelStart, channelLength);
                if (statsLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeStatistics, summaryStart + statsStart, statsLength);
                if (metadataIndexLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeMetadataIndex, summaryStart + metadataIndexStart, metadataIndexLength);
                if (attachmentIndexLength > 0) summaryWriter.WriteSummaryOffset(McapWriter.OpcodeAttachmentIndex, summaryStart + attachmentIndexStart, attachmentIndexLength);
            }

            summaryWriter.Flush();
            var summaryData = summaryBuilder.ToArray();
            var hasSummary = summaryData.Length > 0;
            var footerSummaryStart = hasSummary ? summaryStart : 0UL;
            if (!hasSummary)
                summaryOffsetStart = 0;
            var footerPrefix = McapWriter.BuildFooterCrcPrefix(footerSummaryStart, summaryOffsetStart);
            var crcInput = new byte[summaryData.Length + footerPrefix.Length];
            Buffer.BlockCopy(summaryData, 0, crcInput, 0, summaryData.Length);
            Buffer.BlockCopy(footerPrefix, 0, crcInput, summaryData.Length, footerPrefix.Length);
            var summaryCrc = Crc32Helper.Compute(crcInput);
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

            return McapWriterOptions.Normalize(new McapWriterOptions
            {
                UseChunking = features.Contains("ch"),
                IndexTypes = indexTypes,
                RepeatSchemas = features.Contains("rsh"),
                RepeatChannels = features.Contains("rch"),
                UseStatistics = features.Contains("st"),
                UseSummaryOffsets = features.Contains("sum"),
                EnableCrcs = true,
                EnableDataCrcs = true
            });
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
    }
}
