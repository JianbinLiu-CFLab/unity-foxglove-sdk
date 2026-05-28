// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Reader
// Purpose: Static MCAP record decode methods extracted from McapReader.
// These pure functions have no instance state or stream access.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Util;
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
        {
            int off = 0;
            McapBinaryReader.ReadU64LE(content, ref off);
            McapBinaryReader.ReadU64LE(content, ref off);
            var uncompSize = McapBinaryReader.ReadU64LE(content, ref off);
            var crc = McapBinaryReader.ReadU32LE(content, ref off);
            var compression = McapBinaryReader.ReadString(content, ref off);
            var compSize = McapBinaryReader.ReadU64LE(content, ref off);

            if (compSize > int.MaxValue || uncompSize > int.MaxValue)
                throw new InvalidDataException($"Chunk compressed/uncompressed size exceeds int.MaxValue");
            if (uncompressedSizeLimit > 0 && uncompSize > uncompressedSizeLimit)
                throw new InvalidDataException($"Chunk uncompressed size {uncompSize} exceeds limit {uncompressedSizeLimit}");
            if (off + (int)compSize > content.Length)
                throw new InvalidDataException("Chunk compressed data is truncated");

            var compressed = new byte[(int)compSize];
            Buffer.BlockCopy(content, off, compressed, 0, (int)compSize);

            var uncompressed = McapCompression.Decompress(compression, compressed, (int)uncompSize);
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
            for (var i = 0; i < schemas.Count; i++)
            {
                if (schemas[i].Id == schema.Id)
                    return;
            }

            schemas.Add(schema);
        }

        internal static void AddChannel(List<McapChannel> channels, McapChannel channel)
        {
            for (var i = 0; i < channels.Count; i++)
            {
                if (channels[i].Id == channel.Id)
                    return;
            }

            channels.Add(channel);
        }

        internal static void DecodeDataEnd(byte[] content)
        {
            if (content == null || content.Length != McapWriter.Crc32SizeBytes)
                throw new InvalidDataException("MCAP DataEnd content length must be 4 bytes.");

            var off = 0;
            McapBinaryReader.ReadU32LE(content, ref off);
        }

        // Decode helpers

        /// <summary>
        /// Decodes an MCAP header record from raw content bytes.
        /// </summary>
        public static McapHeader DecodeHeader(byte[] content)
        {
            var off = 0;
            return new McapHeader
            {
                Profile = McapBinaryReader.ReadString(content, ref off),
                Library = McapBinaryReader.ReadString(content, ref off)
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
            RequireExactSegmentEnd(off, end, "schema");
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
            RequireExactSegmentEnd(off, end, "channel");
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
        {
            var off = 0;
            var ci = new McapChunkIndex
            {
                MessageStartTime = McapBinaryReader.ReadU64LE(content, ref off),
                MessageEndTime = McapBinaryReader.ReadU64LE(content, ref off),
                ChunkStartOffset = McapBinaryReader.ReadU64LE(content, ref off),
                ChunkLength = McapBinaryReader.ReadU64LE(content, ref off)
            };
            var mioSize = McapBinaryReader.ReadU32LE(content, ref off);
            ValidateSizedU16U64VectorLength(mioSize, "message_index_offsets");
            var mioCount = mioSize / U16U64PairSize;
            for (var i = 0; i < mioCount; i++)
            {
                var cid = McapBinaryReader.ReadU16LE(content, ref off);
                var offset = McapBinaryReader.ReadU64LE(content, ref off);
                ci.MessageIndexOffsets[cid] = offset;
            }
            ci.MessageIndexLength = McapBinaryReader.ReadU64LE(content, ref off);
            ci.Compression = McapBinaryReader.ReadString(content, ref off);
            ci.CompressedSize = McapBinaryReader.ReadU64LE(content, ref off);
            ci.UncompressedSize = McapBinaryReader.ReadU64LE(content, ref off);
            return ci;
        }

        /// <summary>
        /// Decodes an MCAP statistics record from raw content bytes.
        /// </summary>
        public static McapStatistics DecodeStatistics(byte[] content)
        {
            var off = 0;
            var s = new McapStatistics
            {
                MessageCount = McapBinaryReader.ReadU64LE(content, ref off),
                SchemaCount = McapBinaryReader.ReadU16LE(content, ref off),
                ChannelCount = McapBinaryReader.ReadU32LE(content, ref off),
                AttachmentCount = McapBinaryReader.ReadU32LE(content, ref off),
                MetadataCount = McapBinaryReader.ReadU32LE(content, ref off),
                ChunkCount = McapBinaryReader.ReadU32LE(content, ref off),
                MessageStartTime = McapBinaryReader.ReadU64LE(content, ref off),
                MessageEndTime = McapBinaryReader.ReadU64LE(content, ref off)
            };
            var cmsSize = McapBinaryReader.ReadU32LE(content, ref off);
            ValidateSizedU16U64VectorLength(cmsSize, "channel_message_counts");
            var cmsCount = cmsSize / U16U64PairSize;
            for (var i = 0; i < cmsCount; i++)
            {
                var cid = McapBinaryReader.ReadU16LE(content, ref off);
                var count = McapBinaryReader.ReadU64LE(content, ref off);
                s.ChannelMessageCounts[cid] = count;
            }
            return s;
        }

        /// <summary>
        /// Decodes an MCAP metadata index record from raw content bytes.
        /// </summary>
        public static McapMetadataIndex DecodeMetadataIndex(byte[] content)
        {
            var off = 0;
            return new McapMetadataIndex
            {
                Offset = McapBinaryReader.ReadU64LE(content, ref off),
                Length = McapBinaryReader.ReadU64LE(content, ref off),
                Name = McapBinaryReader.ReadString(content, ref off)
            };
        }

        /// <summary>
        /// Decodes an MCAP metadata record from raw content bytes.
        /// </summary>
        public static McapMetadata DecodeMetadata(byte[] content)
        {
            var end = ValidateRecordSegment(content, 0, content?.Length ?? 0, "metadata");
            var off = 0;
            var name = ReadString(content, ref off, end, "metadata name");
            var meta = ReadMap(content, ref off, end, "metadata");
            RequireExactSegmentEnd(off, end, "metadata");
            return new McapMetadata { Name = name, Metadata = meta };
        }

        /// <summary>
        /// Decodes an MCAP attachment record from raw content bytes.
        /// </summary>
        public static McapAttachment DecodeAttachment(byte[] content)
        {
            var off = 0;
            var logTime = McapBinaryReader.ReadU64LE(content, ref off);
            var createTime = McapBinaryReader.ReadU64LE(content, ref off);
            var name = McapBinaryReader.ReadString(content, ref off);
            var mediaType = McapBinaryReader.ReadString(content, ref off);
            var dataSize = McapBinaryReader.ReadU64LE(content, ref off);
            if (dataSize > int.MaxValue)
                throw new InvalidDataException($"Attachment data size {dataSize} exceeds int.MaxValue");
            if (content.Length - off < McapWriter.Crc32SizeBytes)
                throw new InvalidDataException("Attachment content is truncated: CRC field extends past record");
            var remaining = content.Length - off - McapWriter.Crc32SizeBytes;
            if (dataSize > (ulong)remaining)
                throw new InvalidDataException("Attachment content is truncated: data extends past CRC field");
            var data = new byte[dataSize];
            if (dataSize > 0)
                Buffer.BlockCopy(content, off, data, 0, (int)dataSize);
            off += (int)dataSize;
            var storedCrc = (uint)(content[off] | (content[off + 1] << 8) | (content[off + 2] << 16) | (content[off + 3] << 24));
            var crcValid = true;
            if (storedCrc != 0)
            {
                var computed = Crc32Helper.Compute(new ReadOnlySpan<byte>(content, 0, content.Length - McapWriter.Crc32SizeBytes));
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
        {
            var off = 0;
            return new McapAttachmentIndex
            {
                Offset = McapBinaryReader.ReadU64LE(content, ref off),
                Length = McapBinaryReader.ReadU64LE(content, ref off),
                LogTime = McapBinaryReader.ReadU64LE(content, ref off),
                CreateTime = McapBinaryReader.ReadU64LE(content, ref off),
                DataSize = McapBinaryReader.ReadU64LE(content, ref off),
                Name = McapBinaryReader.ReadString(content, ref off),
                MediaType = McapBinaryReader.ReadString(content, ref off)
            };
        }

        /// <summary>
        /// Decodes an MCAP footer record from raw content bytes.
        /// </summary>
        public static McapFooter DecodeFooter(byte[] content)
        {
            var off = 0;
            return new McapFooter
            {
                SummaryStart = McapBinaryReader.ReadU64LE(content, ref off),
                SummaryOffsetStart = McapBinaryReader.ReadU64LE(content, ref off),
                SummaryCrc = McapBinaryReader.ReadU32LE(content, ref off)
            };
        }
    }
}
