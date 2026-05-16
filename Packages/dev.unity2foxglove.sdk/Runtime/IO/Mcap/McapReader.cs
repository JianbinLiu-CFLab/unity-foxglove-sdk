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
                throw new InvalidDataException("No summary section in MCAP file");
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
                var (op, content) = ReadOneRecord(recordSizeLimit);
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
                AttachmentIndexes = attachmentIndexes
            };
        }

        /// <summary>
        /// Reads one MCAP record from the current stream position, returning its opcode and content bytes.
        /// </summary>
        public (byte opcode, byte[] content) ReadOneRecord(ulong sizeLimit = DefaultRecordSizeLimit)
        {
            var opcodeRaw = _stream.ReadByte();
            if (opcodeRaw < 0) throw new EndOfStreamException("MCAP stream ended before reading record opcode");
            var opcode = (byte)opcodeRaw;
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

            int off = 0;
            var st = McapBinaryReader.ReadU64LE(content, ref off);
            var et = McapBinaryReader.ReadU64LE(content, ref off);
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
            {
                var computed = Util.Crc32Helper.Compute(uncompressed);
                crcValid = computed == crc;
            }
            else
            {
                crcValid = true; // CRC not present; spec allows 0 to mean "not available".
            }

            return uncompressed;
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
            while (off + 9 <= uncompressedRecords.Length)
            {
                var opcode = uncompressedRecords[off++];
                var len = McapBinaryReader.ReadU64LE(uncompressedRecords, ref off);
                if (len > int.MaxValue) break;
                var recordLength = (int)len;
                if (recordLength < 0 || recordLength > uncompressedRecords.Length - off) break;

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
        {
            var off = 0;
            return new McapSchema
            {
                Id = McapBinaryReader.ReadU16LE(content, ref off),
                Name = McapBinaryReader.ReadString(content, ref off),
                Encoding = McapBinaryReader.ReadString(content, ref off),
                Data = McapBinaryReader.ReadPrefixed(content, ref off)
            };
        }

        /// <summary>
        /// Decodes an MCAP channel record from raw content bytes.
        /// </summary>
        public static McapChannel DecodeChannel(byte[] content)
        {
            var off = 0;
            return new McapChannel
            {
                Id = McapBinaryReader.ReadU16LE(content, ref off),
                SchemaId = McapBinaryReader.ReadU16LE(content, ref off),
                Topic = McapBinaryReader.ReadString(content, ref off),
                MessageEncoding = McapBinaryReader.ReadString(content, ref off),
                Metadata = McapBinaryReader.ReadMap(content, ref off)
            };
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
