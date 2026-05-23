// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Low-level MCAP reader that parses the MCAP binary format:
// magic verification, footer/summary extraction, record iteration,
// and chunk decompression (LZ4/Zstd).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Low-level MCAP binary reader. Verifies magic bytes, locates the
    /// footer and summary sections, iterates records within chunks, and
    /// delegates chunk decompression to <see cref="McapCompression"/>.
    /// </summary>
    public class McapReader
    {
        private readonly Stream _stream;
        private readonly byte[] _buf = new byte[8];

        /// <summary>
        /// Default maximum size for a single MCAP record, set to 256 MiB.
        /// </summary>
        public const ulong DefaultRecordSizeLimit = 256UL * 1024 * 1024;
        /// <summary>
        /// Default maximum decompressed size for a single MCAP chunk, set to 64 MiB.
        /// </summary>
        public const ulong DefaultChunkUncompressedSizeLimit = 64UL * 1024 * 1024;

        public McapReader(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>
        /// Reads the MCAP file header, footer, and summary section, returning a parsed McapFileSummary.
        /// </summary>
        public McapFileSummary ReadSummary(ulong recordSizeLimit = DefaultRecordSizeLimit)
        {
            const int minFileBytes =
                McapWriter.MagicLength + McapWriter.RecordHeaderLength +
                McapWriter.FooterContentLength + McapWriter.MagicLength;
            if (_stream.CanSeek && _stream.Length < minFileBytes)
                throw new EndOfStreamException("MCAP stream is shorter than the minimum header/footer size");

            // Verify leading magic
            var magic = new byte[8];
            ReadExact(magic, 0, 8);
            var expectedMagic = McapWriter.MagicSpan;
            for (var i = 0; i < expectedMagic.Length; i++)
                if (magic[i] != expectedMagic[i])
                    throw new InvalidDataException("MCAP leading magic mismatch");

            // Verify trailing magic before trusting footer offsets.
            _stream.Seek(-8, SeekOrigin.End);
            var trailingMagic = new byte[8];
            ReadExact(trailingMagic, 0, 8);
            for (var i = 0; i < expectedMagic.Length; i++)
                if (trailingMagic[i] != expectedMagic[i])
                    throw new InvalidDataException("MCAP trailing magic mismatch");

            // Read Footer before trailing magic.
            _stream.Seek(-(McapWriter.MagicLength + McapWriter.RecordHeaderLength + McapWriter.FooterContentLength), SeekOrigin.End);
            var (opcode, footerContent) = ReadOneRecord(recordSizeLimit);
            if (opcode != McapWriter.OpcodeFooter)
                throw new InvalidDataException($"Expected Footer (0x02) at end of file, got 0x{opcode:X2}");

            var footer = DecodeFooter(footerContent);

            var footerOffset = (ulong)_stream.Length
                - McapWriter.MagicLength
                - McapWriter.RecordHeaderLength
                - McapWriter.FooterContentLength;
            if (footer.SummaryStart == 0)
                return ScanDataSection(
                    footerOffset,
                    recordSizeLimit,
                    collectInventory: true,
                    collectMessages: false,
                    sequentialLimits: null,
                    validateCrcs: true);
            if (footer.SummaryStart > footerOffset)
                throw new InvalidDataException("Footer summary_start is past the footer record");
            if (footer.SummaryOffsetStart != 0 &&
                (footer.SummaryOffsetStart < footer.SummaryStart || footer.SummaryOffsetStart > footerOffset))
                throw new InvalidDataException("Footer summary_offset_start is outside the summary section bounds");

            var summaryLen = footerOffset - footer.SummaryStart;
            if (summaryLen > int.MaxValue)
                throw new InvalidDataException("MCAP summary section size exceeds int.MaxValue");

            // Read summary section
            _stream.Seek((long)footer.SummaryStart, SeekOrigin.Begin);
            var schemas = new List<McapSchema>();
            var channels = new List<McapChannel>();
            McapStatistics stats = null;
            var chunkIndexes = new List<McapChunkIndex>();
            var metadataIndexes = new List<McapMetadataIndex>();
            var attachmentIndexes = new List<McapAttachmentIndex>();

            var summaryEnd = footerOffset;
            while ((ulong)_stream.Position < summaryEnd)
            {
                var recordStart = (ulong)_stream.Position;
                var (op, content) = ReadOneRecord(recordSizeLimit);
                if ((ulong)_stream.Position > summaryEnd)
                    throw new InvalidDataException($"MCAP summary record at offset {recordStart} extends past the footer.");
                switch (op)
                {
                    case McapWriter.OpcodeSchema:
                        schemas.Add(DecodeSchema(content));
                        break;
                    case McapWriter.OpcodeChannel:
                        channels.Add(DecodeChannel(content));
                        break;
                    case McapWriter.OpcodeChunkIndex:
                        chunkIndexes.Add(DecodeChunkIndex(content));
                        break;
                    case McapWriter.OpcodeStatistics:
                        stats = DecodeStatistics(content);
                        break;
                    case McapWriter.OpcodeMetadataIndex:
                        metadataIndexes.Add(DecodeMetadataIndex(content));
                        break;
                    case McapWriter.OpcodeAttachment:
                        // Attachment body records should not appear in summary,
                        // but skipping keeps older/malformed files from shifting
                        // the stream cursor incorrectly.
                        break;
                    case McapWriter.OpcodeAttachmentIndex:
                        attachmentIndexes.Add(DecodeAttachmentIndex(content));
                        break;
                    case McapWriter.OpcodeSummaryOffset:
                        break;
                    default:
                        break; // unknown, skip
                }
            }

            // Validate summary CRC when non-zero (backward compatible with older files).
            if (footer.SummaryCrc != 0)
            {
                // Summary section runs from summaryStart to the start of the Footer.
                _stream.Seek((long)footer.SummaryStart, SeekOrigin.Begin);
                var summaryBytes = new byte[(int)summaryLen];
                ReadExact(summaryBytes, 0, (int)summaryLen);

                var footerPrefix = McapWriter.BuildFooterCrcPrefix(footer.SummaryStart, footer.SummaryOffsetStart);

                var crcInput = new byte[summaryBytes.Length + footerPrefix.Length];
                Buffer.BlockCopy(summaryBytes, 0, crcInput, 0, summaryBytes.Length);
                Buffer.BlockCopy(footerPrefix, 0, crcInput, summaryBytes.Length, footerPrefix.Length);
                var recomputed = Crc32Helper.Compute(crcInput);
                if (recomputed != footer.SummaryCrc)
                    throw new InvalidDataException("MCAP summary CRC mismatch");
            }

            return new McapFileSummary
            {
                Schemas = schemas,
                Channels = channels,
                Statistics = stats,
                ChunkIndexes = chunkIndexes,
                MetadataIndexes = metadataIndexes,
                AttachmentIndexes = attachmentIndexes,
                DataSectionEndOffset = footer.SummaryStart
            };
        }

        /// <summary>
        /// Sequentially scans the data section and returns messages found outside the indexed path.
        /// </summary>
        public List<McapMessage> ReadSequentialMessages(
            ulong dataSectionEndOffset,
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            McapSequentialReadLimits sequentialLimits = null,
            bool validateCrcs = true)
        {
            return ScanDataSection(
                dataSectionEndOffset,
                recordSizeLimit,
                collectInventory: false,
                collectMessages: true,
                sequentialLimits: sequentialLimits,
                validateCrcs: validateCrcs).SequentialMessages ?? new List<McapMessage>();
        }

        /// <summary>
        /// Reads one MCAP record from the current stream position, returning its opcode and content bytes.
        /// </summary>
        public (byte opcode, byte[] content) ReadOneRecord(ulong sizeLimit = DefaultRecordSizeLimit)
        {
            var opcodeRaw = _stream.ReadByte();
            if (opcodeRaw < 0) throw new EndOfStreamException("MCAP stream ended before reading record opcode");
            var opcode = (byte)opcodeRaw;
            if (opcode == 0x00)
                throw new InvalidDataException("MCAP opcode 0x00 is invalid.");
            var contentLength = ReadU64();
            if (contentLength > sizeLimit)
                throw new InvalidDataException($"Record content length {contentLength} exceeds limit {sizeLimit}");
            if (contentLength > int.MaxValue)
                throw new InvalidDataException($"Record content length {contentLength} exceeds int.MaxValue");
            var content = new byte[contentLength];
            ReadExact(content, 0, (int)contentLength);
            return (opcode, content);
        }

        /// <summary>
        /// Reads and decompresses a chunk's record data from the given offset and length.
        /// Validates the uncompressed CRC32 if one is stored (non-zero).
        /// </summary>
        public byte[] ReadChunkRecords(
            ulong chunkStartOffset,
            ulong chunkLength,
            out bool crcValid,
            ulong uncompressedSizeLimit = DefaultChunkUncompressedSizeLimit)
        {
            _stream.Seek((long)chunkStartOffset, SeekOrigin.Begin);
            var (opcode, content) = ReadOneRecord();
            if (opcode != McapWriter.OpcodeChunk)
                throw new InvalidDataException($"Expected Chunk (0x06) at offset {chunkStartOffset}, got 0x{opcode:X2}");

            return DecodeChunkRecordsContent(content, out crcValid, uncompressedSizeLimit);
        }

        /// <summary>
        /// Reads and decompresses a chunk's record data (backward-compatible overload).
        /// CRC validation result is discarded.
        /// </summary>
        public byte[] ReadChunkRecords(ulong chunkStartOffset, ulong chunkLength)
        {
            return ReadChunkRecords(chunkStartOffset, chunkLength, out _);
        }

        /// <summary>
        /// Parses MCAP messages from decompressed chunk data, optionally filtering by channel ID.
        /// </summary>
        public List<McapMessage> ReadChunkMessages(byte[] uncompressedRecords, ushort? filterChannelId = null)
        {
            var messages = new List<McapMessage>();
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

                if (opcode == 0x05)
                {
                    var msg = DecodeMessage(uncompressedRecords, off, recordLength);
                    if (!filterChannelId.HasValue || msg.ChannelId == filterChannelId.Value)
                        messages.Add(msg);
                }
                off += recordLength;
            }
            return messages;
        }

        private McapFileSummary ScanDataSection(
            ulong dataSectionEndOffset,
            ulong recordSizeLimit,
            bool collectInventory,
            bool collectMessages,
            McapSequentialReadLimits sequentialLimits,
            bool validateCrcs)
        {
            if (collectMessages)
            {
                sequentialLimits = sequentialLimits ?? McapSequentialReadLimits.Default;
                sequentialLimits.Validate();
            }

            var summary = new McapFileSummary
            {
                DataSectionEndOffset = dataSectionEndOffset
            };
            var messageCount = 0UL;
            var retainedPayloadBytes = 0L;
            var attachmentCount = 0U;
            var metadataCount = 0U;
            var chunkCount = 0U;
            var messageStart = ulong.MaxValue;
            var messageEnd = 0UL;
            var channelMessageCounts = new Dictionary<ushort, ulong>();

            _stream.Seek(McapWriter.MagicLength, SeekOrigin.Begin);
            var isFirstRecord = true;
            while ((ulong)_stream.Position < dataSectionEndOffset)
            {
                var recordStart = (ulong)_stream.Position;
                var (opcode, content) = ReadOneRecord(recordSizeLimit);
                var recordEnd = (ulong)_stream.Position;
                if (recordEnd > dataSectionEndOffset)
                    throw new InvalidDataException("MCAP data-section record extends past the data section bounds.");

                if (isFirstRecord)
                {
                    if (opcode != McapWriter.OpcodeHeader)
                        throw new InvalidDataException($"Expected Header (0x01) after leading magic, got 0x{opcode:X2}");
                    DecodeHeader(content);
                    isFirstRecord = false;
                    continue;
                }

                switch (opcode)
                {
                    case McapWriter.OpcodeHeader:
                        DecodeHeader(content);
                        break;
                    case McapWriter.OpcodeSchema:
                        if (collectInventory)
                            AddSchema(summary.Schemas, DecodeSchema(content));
                        break;
                    case McapWriter.OpcodeChannel:
                        if (collectInventory)
                            AddChannel(summary.Channels, DecodeChannel(content));
                        break;
                    case McapWriter.OpcodeMessage:
                        if (collectMessages)
                        {
                            AddSequentialMessage(
                                summary,
                                DecodeMessage(content, 0, content.Length),
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
                                DecodeMessageHeader(content, 0, content.Length),
                                ref messageCount,
                                ref messageStart,
                                ref messageEnd,
                                channelMessageCounts);
                        }
                        break;
                    case McapWriter.OpcodeChunk:
                        chunkCount++;
                        var records = DecodeChunkRecordsContent(
                            content,
                            out var crcValid,
                            DefaultChunkUncompressedSizeLimit);
                        if (!crcValid && validateCrcs)
                            throw new InvalidDataException("MCAP chunk CRC mismatch.");
                        ScanChunkRecords(
                            records,
                            summary,
                            collectInventory,
                            collectMessages,
                            sequentialLimits,
                            ref retainedPayloadBytes,
                            ref messageCount,
                            ref messageStart,
                            ref messageEnd,
                            channelMessageCounts);
                        break;
                    case McapWriter.OpcodeAttachment:
                        attachmentCount++;
                        if (collectInventory)
                        {
                            var attachment = DecodeAttachment(content);
                            summary.AttachmentIndexes.Add(new McapAttachmentIndex
                            {
                                Offset = recordStart,
                                Length = recordEnd - recordStart,
                                LogTime = attachment.LogTime,
                                CreateTime = attachment.CreateTime,
                                DataSize = (ulong)(attachment.Data?.Length ?? 0),
                                Name = attachment.Name,
                                MediaType = attachment.MediaType
                            });
                        }
                        break;
                    case McapWriter.OpcodeMetadata:
                        metadataCount++;
                        if (collectInventory)
                        {
                            var metadata = DecodeMetadata(content);
                            summary.MetadataIndexes.Add(new McapMetadataIndex
                            {
                                Offset = recordStart,
                                Length = recordEnd - recordStart,
                                Name = metadata.Name
                            });
                        }
                        break;
                    case McapWriter.OpcodeDataEnd:
                        DecodeDataEnd(content);
                        goto Done;
                    default:
                        break;
                }
            }

        Done:
            if (messageCount > 0 || collectInventory)
            {
                summary.Statistics = new McapStatistics
                {
                    MessageCount = messageCount,
                    SchemaCount = (ushort)summary.Schemas.Count,
                    ChannelCount = (uint)summary.Channels.Count,
                    AttachmentCount = attachmentCount,
                    MetadataCount = metadataCount,
                    ChunkCount = chunkCount,
                    MessageStartTime = messageCount > 0 ? messageStart : 0,
                    MessageEndTime = messageCount > 0 ? messageEnd : 0,
                    ChannelMessageCounts = channelMessageCounts
                };
            }

            return summary;
        }

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

        private static void ScanChunkRecords(
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

        private static void AddSequentialMessage(
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

        private static void AddMessageStats(
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

        private static void AddSchema(List<McapSchema> schemas, McapSchema schema)
        {
            for (var i = 0; i < schemas.Count; i++)
            {
                if (schemas[i].Id == schema.Id)
                    return;
            }

            schemas.Add(schema);
        }

        private static void AddChannel(List<McapChannel> channels, McapChannel channel)
        {
            for (var i = 0; i < channels.Count; i++)
            {
                if (channels[i].Id == channel.Id)
                    return;
            }

            channels.Add(channel);
        }

        private static void DecodeDataEnd(byte[] content)
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

        private static int ValidateRecordSegment(byte[] content, int offset, int contentLen, string recordName)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (offset < 0 || contentLen < 0 || offset > content.Length || contentLen > content.Length - offset)
                throw new InvalidDataException("MCAP " + recordName + " record segment is outside the source buffer.");

            return offset + contentLen;
        }

        private static void RequireExactSegmentEnd(int off, int end, string recordName)
        {
            if (off != end)
                throw new InvalidDataException("MCAP " + recordName + " record segment has trailing bytes.");
        }

        private static ushort ReadU16LE(byte[] buf, ref int off, int end, string fieldName)
        {
            EnsureSegmentBytes(off, sizeof(ushort), end, fieldName);
            return McapBinaryReader.ReadU16LE(buf, ref off);
        }

        private static uint ReadU32LE(byte[] buf, ref int off, int end, string fieldName)
        {
            EnsureSegmentBytes(off, sizeof(uint), end, fieldName);
            return McapBinaryReader.ReadU32LE(buf, ref off);
        }

        private static string ReadString(byte[] buf, ref int off, int end, string fieldName)
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

        private static byte[] ReadPrefixed(byte[] buf, ref int off, int end, string fieldName)
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

        private static Dictionary<string, string> ReadMap(byte[] buf, ref int off, int end, string fieldName)
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

        private static void EnsureSegmentBytes(int off, int count, int end, string fieldName)
        {
            if (count < 0 || off > end || count > end - off)
                throw new InvalidDataException("Truncated MCAP " + fieldName + " within record segment.");
        }

        /// <summary>
        /// Decodes an MCAP message from the given byte buffer with offset and content length.
        /// </summary>
        public static McapMessage DecodeMessage(byte[] buf, int off, int contentLen)
        {
            var start = off;
            var channelId = McapBinaryReader.ReadU16LE(buf, ref off);
            var sequence = McapBinaryReader.ReadU32LE(buf, ref off);
            var logTime = McapBinaryReader.ReadU64LE(buf, ref off);
            var publishTime = McapBinaryReader.ReadU64LE(buf, ref off);
            var dataLen = contentLen - (off - start);
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

        private static McapMessage DecodeMessageHeader(byte[] buf, int off, int contentLen)
        {
            var start = off;
            var channelId = McapBinaryReader.ReadU16LE(buf, ref off);
            var sequence = McapBinaryReader.ReadU32LE(buf, ref off);
            var logTime = McapBinaryReader.ReadU64LE(buf, ref off);
            var publishTime = McapBinaryReader.ReadU64LE(buf, ref off);
            if (contentLen < off - start)
                throw new InvalidDataException("MCAP message content is truncated.");
            return new McapMessage
            {
                ChannelId = channelId,
                Sequence = sequence,
                LogTime = logTime,
                PublishTime = publishTime,
                Data = Array.Empty<byte>()
            };
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
            var mioCount = mioSize / 10;
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
            var cmsCount = cmsSize / 10;
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
            var off = 0;
            var name = McapBinaryReader.ReadString(content, ref off);
            var mapSize = McapBinaryReader.ReadU32LE(content, ref off);
            var mapEnd = off + (int)mapSize;
            var meta = new Dictionary<string, string>();
            while (off < mapEnd)
            {
                var k = McapBinaryReader.ReadString(content, ref off);
                var v = McapBinaryReader.ReadString(content, ref off);
                meta[k] = v;
            }
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
        /// Seeks to the given offset and reads a single attachment record.
        /// </summary>
        public McapAttachment ReadAttachmentAt(ulong offset)
        {
            _stream.Seek((long)offset, SeekOrigin.Begin);
            var (opcode, content) = ReadOneRecord();
            if (opcode != McapWriter.OpcodeAttachment)
                throw new InvalidDataException($"Expected Attachment (0x09) at offset {offset}, got 0x{opcode:X2}");
            return DecodeAttachment(content);
        }

        /// <summary>
        /// Seeks to the given offset and reads a single metadata record.
        /// </summary>
        public McapMetadata ReadMetadataAt(ulong offset)
        {
            _stream.Seek((long)offset, SeekOrigin.Begin);
            var (opcode, content) = ReadOneRecord();
            if (opcode != McapWriter.OpcodeMetadata)
                throw new InvalidDataException($"Expected Metadata (0x0C) at offset {offset}, got 0x{opcode:X2}");
            return DecodeMetadata(content);
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

        // Internal

        /// <summary>
        /// Reads 8 bytes from the stream and assembles them into a little-endian UInt64.
        /// </summary>
        private ulong ReadU64()
        {
            ReadExact(_buf, 0, 8);
            return (ulong)_buf[0] | ((ulong)_buf[1] << 8) | ((ulong)_buf[2] << 16) | ((ulong)_buf[3] << 24)
                 | ((ulong)_buf[4] << 32) | ((ulong)_buf[5] << 40) | ((ulong)_buf[6] << 48) | ((ulong)_buf[7] << 56);
        }

        /// <summary>
        /// Reads exactly <c>count</c> bytes from the stream into <c>buf</c> at the given offset.
        /// </summary>
        private void ReadExact(byte[] buf, int offset, int count)
        {
            var read = 0;
            while (read < count)
            {
                var n = _stream.Read(buf, offset + read, count - read);
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
        }
    }
}
