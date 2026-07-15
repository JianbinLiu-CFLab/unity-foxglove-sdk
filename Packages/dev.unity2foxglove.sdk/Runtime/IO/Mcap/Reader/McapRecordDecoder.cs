// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Reader
// Purpose: Static MCAP record decode methods extracted from McapReader.
// These pure functions have no instance state or stream access.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    public static class McapRecordDecoder
    {
        private const int MessageFixedHeaderLength =
            sizeof(ushort) + sizeof(uint) + sizeof(ulong) + sizeof(ulong);
        private const int U16U64PairSize = sizeof(ushort) + sizeof(ulong);

        public static byte[] DecodeChunkRecordsContent(
            byte[] content,
            out bool crcValid,
            ulong uncompressedSizeLimit)
            => DecodeChunkRecordsContent(content, 0, content?.Length ?? 0, out crcValid, uncompressedSizeLimit);

        internal static byte[] DecodeChunkRecordsContent(
            byte[] content,
            int offset,
            int contentLen,
            out bool crcValid,
            ulong uncompressedSizeLimit)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "chunk");
            var off = offset;
            ReadU64LE(content, ref off, end, "chunk message_start_time");
            ReadU64LE(content, ref off, end, "chunk message_end_time");
            var uncompSize = ReadU64LE(content, ref off, end, "chunk uncompressed_size");
            var crc = ReadU32LE(content, ref off, end, "chunk uncompressed_crc");
            var compression = ReadString(content, ref off, end, "chunk compression");
            var compSize = ReadU64LE(content, ref off, end, "chunk compressed_size");

            if (compSize > int.MaxValue || uncompSize > int.MaxValue)
                throw new InvalidDataException($"Chunk compressed/uncompressed size exceeds int.MaxValue");
            if (uncompressedSizeLimit > 0 && uncompSize > uncompressedSizeLimit)
                throw new InvalidDataException($"Chunk uncompressed size {uncompSize} exceeds limit {uncompressedSizeLimit}");
            if ((int)compSize > end - off)
                throw new InvalidDataException("Chunk compressed data is truncated");

            // McapCompression uses 0 as its explicit "unbounded output" sentinel.
            var maxOutputBytes = uncompressedSizeLimit == 0
                ? 0
                : uncompressedSizeLimit > int.MaxValue
                    ? int.MaxValue
                    : (int)uncompressedSizeLimit;
            var uncompressed = McapCompression.Decompress(
                compression,
                new ArraySegment<byte>(content, off, (int)compSize),
                (int)uncompSize,
                maxOutputBytes);
            if (crc != 0)
                crcValid = Crc32Helper.Compute(uncompressed) == crc;
            else
                crcValid = true;

            return uncompressed;
        }

        internal static void ScanChunkRecords(
            byte[] uncompressedRecords,
            McapFileSummary summary,
            bool collectInventory,
            bool collectMessages,
            McapSequentialReadLimits sequentialLimits,
            ref long retainedPayloadBytes,
            ref ulong messageCount,
            ref ulong messageStart,
            ref ulong messageEnd,
            Dictionary<ushort, ulong> channelMessageCounts)
        {
            var off = 0;
            while (off < uncompressedRecords.Length)
            {
                if (uncompressedRecords.Length - off < McapWriter.RecordHeaderLength)
                    throw new InvalidDataException("Chunk inner record is truncated.");

                var opcode = uncompressedRecords[off++];
                if (opcode == 0x00)
                    throw new InvalidDataException("MCAP opcode 0x00 is invalid inside chunk.");

                var len = McapBinaryReader.ReadU64LE(uncompressedRecords, ref off);
                if (len > int.MaxValue)
                    throw new InvalidDataException("Chunk inner record length exceeds int.MaxValue.");
                var recordLength = (int)len;
                if (recordLength < 0 || recordLength > uncompressedRecords.Length - off)
                    throw new InvalidDataException("Chunk inner record content is truncated.");

                switch (opcode)
                {
                    case McapWriter.OpcodeSchema:
                        if (collectInventory)
                        {
                            AddSchema(summary.Schemas, DecodeSchema(uncompressedRecords, off, recordLength));
                        }
                        break;
                    case McapWriter.OpcodeChannel:
                        if (collectInventory)
                        {
                            AddChannel(summary.Channels, DecodeChannel(uncompressedRecords, off, recordLength));
                        }
                        break;
                    case McapWriter.OpcodeMessage:
                        if (collectMessages)
                        {
                            AddSequentialMessage(
                                summary,
                                DecodeMessage(uncompressedRecords, off, recordLength),
                                sequentialLimits,
                                ref retainedPayloadBytes,
                                ref messageCount,
                                ref messageStart,
                                ref messageEnd,
                                channelMessageCounts);
                        }
                        else
                        {
                            AddMessageStats(
                                DecodeMessageHeader(uncompressedRecords, off, recordLength),
                                ref messageCount,
                                ref messageStart,
                                ref messageEnd,
                                channelMessageCounts);
                        }
                        break;
                    case McapWriter.OpcodeAttachment:
                        throw new InvalidDataException("MCAP Attachment records must not appear inside a Chunk.");
                    default:
                        break;
                }

                off += recordLength;
            }
        }

        internal static void AddSequentialMessage(
            McapFileSummary summary,
            McapMessage message,
            McapSequentialReadLimits sequentialLimits,
            ref long retainedPayloadBytes,
            ref ulong messageCount,
            ref ulong messageStart,
            ref ulong messageEnd,
            Dictionary<ushort, ulong> channelMessageCounts)
        {
            if (summary.SequentialMessages == null)
                summary.SequentialMessages = new List<McapMessage>();

            if (sequentialLimits != null && sequentialLimits.MaxMessages > 0 &&
                summary.SequentialMessages.Count >= sequentialLimits.MaxMessages)
                throw new InvalidOperationException(
                    "Unindexed MCAP sequential fallback exceeded MaxMessages=" + sequentialLimits.MaxMessages + ".");

            var payloadBytes = message?.Data?.LongLength ?? 0L;
            if (sequentialLimits != null && sequentialLimits.MaxPayloadBytes > 0 &&
                retainedPayloadBytes + payloadBytes > sequentialLimits.MaxPayloadBytes)
                throw new InvalidOperationException(
                    "Unindexed MCAP sequential fallback exceeded MaxPayloadBytes=" + sequentialLimits.MaxPayloadBytes + ".");

            summary.SequentialMessages.Add(message);
            retainedPayloadBytes += payloadBytes;
            AddMessageStats(message, ref messageCount, ref messageStart, ref messageEnd, channelMessageCounts);
        }

        internal static void AddMessageStats(
            McapMessage message,
            ref ulong messageCount,
            ref ulong messageStart,
            ref ulong messageEnd,
            Dictionary<ushort, ulong> channelMessageCounts)
        {
            messageCount++;
            if (message.LogTime < messageStart)
                messageStart = message.LogTime;
            if (message.LogTime > messageEnd)
                messageEnd = message.LogTime;

            channelMessageCounts.TryGetValue(message.ChannelId, out var current);
            channelMessageCounts[message.ChannelId] = current + 1;
        }

        internal static void AddSchema(List<McapSchema> schemas, McapSchema schema)
        {
            if (schema == null || schema.Id == 0)
                return;

            for (var i = 0; i < schemas.Count; i++)
            {
                if (schemas[i].Id == schema.Id)
                {
                    if (!SchemasAreIdentical(schemas[i], schema))
                        throw new InvalidDataException($"MCAP Schema id {schema.Id} has conflicting definitions.");
                    return;
                }
            }

            schemas.Add(schema);
        }

        internal static void AddChannel(List<McapChannel> channels, McapChannel channel)
        {
            for (var i = 0; i < channels.Count; i++)
            {
                if (channels[i].Id == channel.Id)
                {
                    if (!ChannelsAreIdentical(channels[i], channel))
                        throw new InvalidDataException($"MCAP Channel id {channel.Id} has conflicting definitions.");
                    return;
                }
            }

            channels.Add(channel);
        }

        private static bool SchemasAreIdentical(McapSchema left, McapSchema right)
        {
            if (left.Id != right.Id ||
                !string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
                !string.Equals(left.Encoding, right.Encoding, StringComparison.Ordinal))
                return false;

            var leftData = left.Data ?? Array.Empty<byte>();
            var rightData = right.Data ?? Array.Empty<byte>();
            if (leftData.Length != rightData.Length)
                return false;
            for (var i = 0; i < leftData.Length; i++)
            {
                if (leftData[i] != rightData[i])
                    return false;
            }

            return true;
        }

        private static bool ChannelsAreIdentical(McapChannel left, McapChannel right)
        {
            return left.Id == right.Id &&
                   left.SchemaId == right.SchemaId &&
                   string.Equals(left.Topic, right.Topic, StringComparison.Ordinal) &&
                   string.Equals(left.MessageEncoding, right.MessageEncoding, StringComparison.Ordinal) &&
                   MapsAreIdentical(left.Metadata, right.Metadata);
        }

        private static bool MapsAreIdentical(
            Dictionary<string, string> left,
            Dictionary<string, string> right)
        {
            var leftCount = left?.Count ?? 0;
            if (leftCount != (right?.Count ?? 0))
                return false;
            if (leftCount == 0)
                return true;

            foreach (var pair in left)
            {
                if (!right.TryGetValue(pair.Key, out var value) ||
                    !string.Equals(pair.Value, value, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        internal static uint DecodeDataEnd(byte[] content)
            => DecodeDataEnd(content, 0, content?.Length ?? 0);

        internal static uint DecodeDataEnd(byte[] content, int offset, int contentLen)
        {
            if (contentLen != McapWriter.Crc32SizeBytes)
                throw new InvalidDataException("MCAP DataEnd content length must be 4 bytes.");

            var end = ValidateRecordSegment(content, offset, contentLen, "DataEnd");
            var off = offset;
            return ReadU32LE(content, ref off, end, "DataEnd CRC");
        }

        // Decode helpers

        /// <summary>
        /// Decodes an MCAP header record from raw content bytes.
        /// </summary>
        public static McapHeader DecodeHeader(byte[] content)
            => DecodeHeader(content, 0, content?.Length ?? 0);

        internal static McapHeader DecodeHeader(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "header");
            var off = offset;
            return new McapHeader
            {
                Profile = ReadString(content, ref off, end, "header profile"),
                Library = ReadString(content, ref off, end, "header library")
            };
        }

        /// <summary>
        /// Decodes an MCAP schema record from raw content bytes.
        /// </summary>
        public static McapSchema DecodeSchema(byte[] content)
            => DecodeSchema(content, 0, content?.Length ?? 0);

        /// <summary>
        /// Decodes an MCAP schema record from a segment of a larger byte buffer.
        /// </summary>
        public static McapSchema DecodeSchema(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "schema");
            var off = offset;
            var schema = new McapSchema
            {
                Id = ReadU16LE(content, ref off, end, "schema id"),
                Name = ReadString(content, ref off, end, "schema name"),
                Encoding = ReadString(content, ref off, end, "schema encoding"),
                Data = ReadPrefixed(content, ref off, end, "schema data")
            };
            return schema;
        }

        /// <summary>
        /// Decodes an MCAP channel record from raw content bytes.
        /// </summary>
        public static McapChannel DecodeChannel(byte[] content)
            => DecodeChannel(content, 0, content?.Length ?? 0);

        /// <summary>
        /// Decodes an MCAP channel record from a segment of a larger byte buffer.
        /// </summary>
        public static McapChannel DecodeChannel(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "channel");
            var off = offset;
            var channel = new McapChannel
            {
                Id = ReadU16LE(content, ref off, end, "channel id"),
                SchemaId = ReadU16LE(content, ref off, end, "channel schema id"),
                Topic = ReadString(content, ref off, end, "channel topic"),
                MessageEncoding = ReadString(content, ref off, end, "channel message encoding"),
                Metadata = ReadMap(content, ref off, end, "channel metadata")
            };
            return channel;
        }

        internal static int ValidateRecordSegment(byte[] content, int offset, int contentLen, string recordName)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (offset < 0 || contentLen < 0 || offset > content.Length || contentLen > content.Length - offset)
                throw new InvalidDataException("MCAP " + recordName + " record segment is outside the source buffer.");

            return offset + contentLen;
        }

        internal static void RequireExactSegmentEnd(int off, int end, string recordName)
        {
            if (off != end)
                throw new InvalidDataException("MCAP " + recordName + " record segment has trailing bytes.");
        }

        internal static ushort ReadU16LE(byte[] buf, ref int off, int end, string fieldName)
        {
            EnsureSegmentBytes(off, sizeof(ushort), end, fieldName);
            return McapBinaryReader.ReadU16LE(buf, ref off);
        }

        internal static uint ReadU32LE(byte[] buf, ref int off, int end, string fieldName)
        {
            EnsureSegmentBytes(off, sizeof(uint), end, fieldName);
            return McapBinaryReader.ReadU32LE(buf, ref off);
        }

        internal static ulong ReadU64LE(byte[] buf, ref int off, int end, string fieldName)
        {
            EnsureSegmentBytes(off, sizeof(ulong), end, fieldName);
            return McapBinaryReader.ReadU64LE(buf, ref off);
        }

        internal static string ReadString(byte[] buf, ref int off, int end, string fieldName)
        {
            var len = ReadU32LE(buf, ref off, end, fieldName + " length");
            if (len > int.MaxValue)
                throw new InvalidDataException("MCAP " + fieldName + " length exceeds supported size.");

            var count = (int)len;
            EnsureSegmentBytes(off, count, end, fieldName);
            var value = Encoding.UTF8.GetString(buf, off, count);
            off += count;
            return value;
        }

        internal static byte[] ReadPrefixed(byte[] buf, ref int off, int end, string fieldName)
        {
            var len = ReadU32LE(buf, ref off, end, fieldName + " length");
            if (len > int.MaxValue)
                throw new InvalidDataException("MCAP " + fieldName + " length exceeds supported size.");

            var count = (int)len;
            EnsureSegmentBytes(off, count, end, fieldName);
            var data = new byte[count];
            if (count > 0)
                Buffer.BlockCopy(buf, off, data, 0, count);
            off += count;
            return data;
        }

        internal static Dictionary<string, string> ReadMap(byte[] buf, ref int off, int end, string fieldName)
        {
            var totalBytes = ReadU32LE(buf, ref off, end, fieldName + " length");
            if (totalBytes > int.MaxValue)
                throw new InvalidDataException("MCAP " + fieldName + " length exceeds supported size.");

            var count = (int)totalBytes;
            EnsureSegmentBytes(off, count, end, fieldName);
            var mapEnd = off + count;
            var map = new Dictionary<string, string>();
            while (off < mapEnd)
            {
                var key = ReadString(buf, ref off, mapEnd, fieldName + " key");
                var value = ReadString(buf, ref off, mapEnd, fieldName + " value");
                map[key] = value;
            }

            RequireExactSegmentEnd(off, mapEnd, fieldName + " map");
            return map;
        }

        internal static void EnsureSegmentBytes(int off, int count, int end, string fieldName)
        {
            if (count < 0 || off > end || count > end - off)
                throw new InvalidDataException("Truncated MCAP " + fieldName + " within record segment.");
        }

        /// <summary>
        /// Decodes an MCAP message from the given byte buffer with offset and content length.
        /// </summary>
        public static McapMessage DecodeMessage(byte[] buf, int off, int contentLen)
        {
            var end = ValidateRecordSegment(buf, off, contentLen, "message");
            EnsureSegmentBytes(off, MessageFixedHeaderLength, end, "message fixed header");
            var channelId = McapBinaryReader.ReadU16LE(buf, ref off);
            var sequence = McapBinaryReader.ReadU32LE(buf, ref off);
            var logTime = McapBinaryReader.ReadU64LE(buf, ref off);
            var publishTime = McapBinaryReader.ReadU64LE(buf, ref off);
            var dataLen = end - off;
            var data = new byte[dataLen];
            if (dataLen > 0)
                Buffer.BlockCopy(buf, off, data, 0, dataLen);
            return new McapMessage
            {
                ChannelId = channelId,
                Sequence = sequence,
                LogTime = logTime,
                PublishTime = publishTime,
                Data = data
            };
        }

        internal static McapMessage DecodeMessageHeader(byte[] buf, int off, int contentLen)
        {
            var end = ValidateRecordSegment(buf, off, contentLen, "message");
            EnsureSegmentBytes(off, MessageFixedHeaderLength, end, "message fixed header");
            var channelId = McapBinaryReader.ReadU16LE(buf, ref off);
            var sequence = McapBinaryReader.ReadU32LE(buf, ref off);
            var logTime = McapBinaryReader.ReadU64LE(buf, ref off);
            var publishTime = McapBinaryReader.ReadU64LE(buf, ref off);
            return new McapMessage
            {
                ChannelId = channelId,
                Sequence = sequence,
                LogTime = logTime,
                PublishTime = publishTime,
                Data = Array.Empty<byte>()
            };
        }

        internal static void ValidateSizedU16U64VectorLength(uint sizeBytes, string fieldName)
        {
            if (sizeBytes % U16U64PairSize != 0)
                throw new InvalidDataException(
                    "MCAP " + fieldName + " byte length must be a multiple of " + U16U64PairSize + ".");
        }

        /// <summary>
        /// Decodes an MCAP chunk index record from raw content bytes.
        /// </summary>
        public static McapChunkIndex DecodeChunkIndex(byte[] content)
            => DecodeChunkIndex(content, 0, content?.Length ?? 0);

        internal static McapChunkIndex DecodeChunkIndex(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "chunk index");
            var off = offset;
            var ci = new McapChunkIndex
            {
                MessageStartTime = ReadU64LE(content, ref off, end, "chunk index message_start_time"),
                MessageEndTime = ReadU64LE(content, ref off, end, "chunk index message_end_time"),
                ChunkStartOffset = ReadU64LE(content, ref off, end, "chunk index chunk_start_offset"),
                ChunkLength = ReadU64LE(content, ref off, end, "chunk index chunk_length")
            };
            var mioSize = ReadU32LE(content, ref off, end, "chunk index message_index_offsets length");
            ValidateSizedU16U64VectorLength(mioSize, "message_index_offsets");
            var mioCount = mioSize / U16U64PairSize;
            for (var i = 0; i < mioCount; i++)
            {
                var cid = ReadU16LE(content, ref off, end, "chunk index channel id");
                var messageIndexOffset = ReadU64LE(content, ref off, end, "chunk index message index offset");
                ci.MessageIndexOffsets[cid] = messageIndexOffset;
            }
            ci.MessageIndexLength = ReadU64LE(content, ref off, end, "chunk index message_index_length");
            ci.Compression = ReadString(content, ref off, end, "chunk index compression");
            ci.CompressedSize = ReadU64LE(content, ref off, end, "chunk index compressed_size");
            ci.UncompressedSize = ReadU64LE(content, ref off, end, "chunk index uncompressed_size");
            return ci;
        }

        /// <summary>
        /// Decodes an MCAP statistics record from raw content bytes.
        /// </summary>
        public static McapStatistics DecodeStatistics(byte[] content)
            => DecodeStatistics(content, 0, content?.Length ?? 0);

        internal static McapStatistics DecodeStatistics(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "statistics");
            var off = offset;
            var s = new McapStatistics
            {
                MessageCount = ReadU64LE(content, ref off, end, "statistics message_count"),
                SchemaCount = ReadU16LE(content, ref off, end, "statistics schema_count"),
                ChannelCount = ReadU32LE(content, ref off, end, "statistics channel_count"),
                AttachmentCount = ReadU32LE(content, ref off, end, "statistics attachment_count"),
                MetadataCount = ReadU32LE(content, ref off, end, "statistics metadata_count"),
                ChunkCount = ReadU32LE(content, ref off, end, "statistics chunk_count"),
                MessageStartTime = ReadU64LE(content, ref off, end, "statistics message_start_time"),
                MessageEndTime = ReadU64LE(content, ref off, end, "statistics message_end_time")
            };
            var cmsSize = ReadU32LE(content, ref off, end, "statistics channel_message_counts length");
            ValidateSizedU16U64VectorLength(cmsSize, "channel_message_counts");
            var cmsCount = cmsSize / U16U64PairSize;
            for (var i = 0; i < cmsCount; i++)
            {
                var cid = ReadU16LE(content, ref off, end, "statistics channel id");
                var count = ReadU64LE(content, ref off, end, "statistics channel message count");
                s.ChannelMessageCounts[cid] = count;
            }
            return s;
        }

        /// <summary>
        /// Decodes an MCAP metadata index record from raw content bytes.
        /// </summary>
        public static McapMetadataIndex DecodeMetadataIndex(byte[] content)
            => DecodeMetadataIndex(content, 0, content?.Length ?? 0);

        internal static McapMetadataIndex DecodeMetadataIndex(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "metadata index");
            var off = offset;
            return new McapMetadataIndex
            {
                Offset = ReadU64LE(content, ref off, end, "metadata index offset"),
                Length = ReadU64LE(content, ref off, end, "metadata index length"),
                Name = ReadString(content, ref off, end, "metadata index name")
            };
        }

        /// <summary>
        /// Decodes an MCAP metadata record from raw content bytes.
        /// </summary>
        public static McapMetadata DecodeMetadata(byte[] content)
            => DecodeMetadata(content, 0, content?.Length ?? 0);

        internal static McapMetadata DecodeMetadata(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "metadata");
            var off = offset;
            var name = ReadString(content, ref off, end, "metadata name");
            var meta = ReadMap(content, ref off, end, "metadata");
            return new McapMetadata { Name = name, Metadata = meta };
        }

        internal static (byte GroupOpcode, ulong GroupStart, ulong GroupLength) DecodeSummaryOffset(
            byte[] content,
            int offset,
            int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "summary offset");
            var off = offset;
            EnsureSegmentBytes(off, 1, end, "summary offset group opcode");
            var groupOpcode = content[off++];
            var groupStart = ReadU64LE(content, ref off, end, "summary offset group start");
            var groupLength = ReadU64LE(content, ref off, end, "summary offset group length");
            return (groupOpcode, groupStart, groupLength);
        }

        /// <summary>
        /// Decodes an MCAP attachment record from raw content bytes.
        /// </summary>
        public static McapAttachment DecodeAttachment(byte[] content)
            => DecodeAttachment(content, 0, content?.Length ?? 0);

        internal static McapAttachment DecodeAttachment(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "attachment");
            var off = offset;
            var logTime = ReadU64LE(content, ref off, end, "attachment log_time");
            var createTime = ReadU64LE(content, ref off, end, "attachment create_time");
            var name = ReadString(content, ref off, end, "attachment name");
            var mediaType = ReadString(content, ref off, end, "attachment media_type");
            var dataSize = ReadU64LE(content, ref off, end, "attachment data size");
            if (dataSize > int.MaxValue)
                throw new InvalidDataException($"Attachment data size {dataSize} exceeds int.MaxValue");
            if (end - off < McapWriter.Crc32SizeBytes)
                throw new InvalidDataException("Attachment content is truncated: CRC field extends past record");
            var remaining = end - off - McapWriter.Crc32SizeBytes;
            if (dataSize > (ulong)remaining)
                throw new InvalidDataException("Attachment content is truncated: data extends past CRC field");
            var data = new byte[dataSize];
            if (dataSize > 0)
                Buffer.BlockCopy(content, off, data, 0, (int)dataSize);
            off += (int)dataSize;
            var storedCrc = ReadU32LE(content, ref off, end, "attachment CRC");
            var crcValid = true;
            if (storedCrc != 0)
            {
                var computed = Crc32Helper.Compute(new ReadOnlySpan<byte>(content, offset, contentLen - McapWriter.Crc32SizeBytes));
                crcValid = computed == storedCrc;
            }
            return new McapAttachment
            {
                LogTime = logTime,
                CreateTime = createTime,
                Name = name,
                MediaType = mediaType,
                Data = data,
                Crc = storedCrc,
                CrcValid = crcValid
            };
        }

        /// <summary>
        /// Decodes an MCAP attachment index record from raw content bytes.
        /// </summary>
        public static McapAttachmentIndex DecodeAttachmentIndex(byte[] content)
            => DecodeAttachmentIndex(content, 0, content?.Length ?? 0);

        internal static McapAttachmentIndex DecodeAttachmentIndex(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "attachment index");
            var off = offset;
            return new McapAttachmentIndex
            {
                Offset = ReadU64LE(content, ref off, end, "attachment index offset"),
                Length = ReadU64LE(content, ref off, end, "attachment index length"),
                LogTime = ReadU64LE(content, ref off, end, "attachment index log_time"),
                CreateTime = ReadU64LE(content, ref off, end, "attachment index create_time"),
                DataSize = ReadU64LE(content, ref off, end, "attachment index data size"),
                Name = ReadString(content, ref off, end, "attachment index name"),
                MediaType = ReadString(content, ref off, end, "attachment index media_type")
            };
        }

        /// <summary>
        /// Decodes an MCAP footer record from raw content bytes.
        /// </summary>
        public static McapFooter DecodeFooter(byte[] content)
            => DecodeFooter(content, 0, content?.Length ?? 0);

        internal static McapFooter DecodeFooter(byte[] content, int offset, int contentLen)
        {
            var end = ValidateRecordSegment(content, offset, contentLen, "footer");
            var off = offset;
            var footer = new McapFooter
            {
                SummaryStart = ReadU64LE(content, ref off, end, "footer summary_start"),
                SummaryOffsetStart = ReadU64LE(content, ref off, end, "footer summary_offset_start"),
                SummaryCrc = ReadU32LE(content, ref off, end, "footer summary_crc")
            };
            RequireExactSegmentEnd(off, end, "footer");
            return footer;
        }
    }
}
