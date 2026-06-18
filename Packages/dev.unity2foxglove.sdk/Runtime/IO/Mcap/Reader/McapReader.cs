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
    /// delegates chunk decompression to <see cref="McapCompression"/>. This
    /// reader borrows the supplied stream; callers retain ownership and are
    /// responsible for disposing the stream.
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
        private const int MessageFixedHeaderLength =
            sizeof(ushort) + sizeof(uint) + sizeof(ulong) + sizeof(ulong);
        private const int U16U64PairSize = sizeof(ushort) + sizeof(ulong);

        public McapReader(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>
        /// Reads the MCAP file header, footer, and summary section, returning a parsed McapFileSummary.
        /// </summary>
        public McapFileSummary ReadSummary(
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            bool validateCrcs = true,
            ulong chunkUncompressedSizeLimit = DefaultChunkUncompressedSizeLimit)
        {
            if (!_stream.CanSeek)
                throw new NotSupportedException("McapReader.ReadSummary requires a seekable stream; use McapStreamingReader for non-seekable streams.");

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

            var footer = McapRecordDecoder.DecodeFooter(footerContent);

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
                    validateCrcs: validateCrcs,
                    chunkUncompressedSizeLimit: chunkUncompressedSizeLimit);
            if (footer.SummaryStart > footerOffset)
                throw new InvalidDataException("Footer summary_start is past the footer record");
            if (footer.SummaryStart < (ulong)(McapWriter.MagicLength + McapWriter.RecordHeaderLength))
                throw new InvalidDataException("Footer summary_start is before the data section");
            if (footer.SummaryOffsetStart != 0 &&
                (footer.SummaryOffsetStart < footer.SummaryStart || footer.SummaryOffsetStart > footerOffset))
                throw new InvalidDataException("Footer summary_offset_start is outside the summary section bounds");

            var summaryLen = footerOffset - footer.SummaryStart;
            if (summaryLen > int.MaxValue)
                throw new InvalidDataException("MCAP summary section size exceeds int.MaxValue");

            // Read summary section
            _stream.Seek(ToSeekOffset(footer.SummaryStart, "summary_start"), SeekOrigin.Begin);
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
                        schemas.Add(McapRecordDecoder.DecodeSchema(content));
                        break;
                    case McapWriter.OpcodeChannel:
                        channels.Add(McapRecordDecoder.DecodeChannel(content));
                        break;
                    case McapWriter.OpcodeChunkIndex:
                        chunkIndexes.Add(McapRecordDecoder.DecodeChunkIndex(content));
                        break;
                    case McapWriter.OpcodeStatistics:
                        stats = McapRecordDecoder.DecodeStatistics(content);
                        break;
                    case McapWriter.OpcodeMetadataIndex:
                        metadataIndexes.Add(McapRecordDecoder.DecodeMetadataIndex(content));
                        break;
                    case McapWriter.OpcodeAttachment:
                        // Attachment body records should not appear in summary,
                        // but skipping keeps older/malformed files from shifting
                        // the stream cursor incorrectly.
                        break;
                    case McapWriter.OpcodeAttachmentIndex:
                        attachmentIndexes.Add(McapRecordDecoder.DecodeAttachmentIndex(content));
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
                _stream.Seek(ToSeekOffset(footer.SummaryStart, "summary_start"), SeekOrigin.Begin);
                var summaryBytes = new byte[(int)summaryLen];
                ReadExact(summaryBytes, 0, (int)summaryLen);

                var footerPrefix = McapWriter.BuildFooterCrcPrefix(footer.SummaryStart, footer.SummaryOffsetStart);

                var crc = Crc32Helper.Initialize();
                crc = Crc32Helper.Update(crc, summaryBytes);
                crc = Crc32Helper.Update(crc, footerPrefix);
                var recomputed = Crc32Helper.Finalize(crc);
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

        internal McapTrailerInfo ReadTrailerInfo(
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            bool validateCrcs = true)
        {
            if (!_stream.CanSeek)
                throw new NotSupportedException("McapReader.ReadTrailerInfo requires a seekable stream.");

            const int minFileBytes =
                McapWriter.MagicLength + McapWriter.RecordHeaderLength +
                McapWriter.FooterContentLength + McapWriter.MagicLength;
            if (_stream.Length < minFileBytes)
                throw new EndOfStreamException("MCAP stream is shorter than the minimum header/footer size");

            var magic = new byte[McapWriter.MagicLength];
            _stream.Seek(0, SeekOrigin.Begin);
            ReadExact(magic, 0, magic.Length);
            var expectedMagic = McapWriter.MagicSpan;
            for (var i = 0; i < expectedMagic.Length; i++)
                if (magic[i] != expectedMagic[i])
                    throw new InvalidDataException("MCAP leading magic mismatch");

            _stream.Seek(-McapWriter.MagicLength, SeekOrigin.End);
            var trailingMagic = new byte[McapWriter.MagicLength];
            ReadExact(trailingMagic, 0, trailingMagic.Length);
            for (var i = 0; i < expectedMagic.Length; i++)
                if (trailingMagic[i] != expectedMagic[i])
                    throw new InvalidDataException("MCAP trailing magic mismatch");

            var footerOffset = (ulong)_stream.Length
                - McapWriter.MagicLength
                - McapWriter.RecordHeaderLength
                - McapWriter.FooterContentLength;
            _stream.Seek(ToSeekOffset(footerOffset, "footer"), SeekOrigin.Begin);
            var (opcode, footerContent) = ReadOneRecord(recordSizeLimit);
            if (opcode != McapWriter.OpcodeFooter)
                throw new InvalidDataException($"Expected Footer (0x02) at end of file, got 0x{opcode:X2}");

            var footer = McapRecordDecoder.DecodeFooter(footerContent);
            if (footer.SummaryStart == 0)
                throw new InvalidDataException("MCAP amendment requires a summary section.");
            if (footer.SummaryStart > footerOffset)
                throw new InvalidDataException("Footer summary_start is past the footer record");
            if (footer.SummaryStart < (ulong)(McapWriter.MagicLength + McapWriter.RecordHeaderLength))
                throw new InvalidDataException("Footer summary_start is before the data section");
            if (footer.SummaryOffsetStart != 0 &&
                (footer.SummaryOffsetStart < footer.SummaryStart || footer.SummaryOffsetStart > footerOffset))
                throw new InvalidDataException("Footer summary_offset_start is outside the summary section bounds");

            ValidateSummaryCrc(
                footer.SummaryStart,
                footer.SummaryOffsetStart,
                footer.SummaryCrc,
                footerOffset,
                validateCrcs);

            var info = new McapTrailerInfo
            {
                FooterOffset = footerOffset,
                SummaryStart = footer.SummaryStart,
                SummaryOffsetStart = footer.SummaryOffsetStart,
                SummaryCrc = footer.SummaryCrc
            };

            _stream.Seek(McapWriter.MagicLength, SeekOrigin.Begin);
            var isFirstRecord = true;
            while ((ulong)_stream.Position < footer.SummaryStart)
            {
                var recordStart = (ulong)_stream.Position;
                var (recordOpcode, content) = ReadOneRecord(recordSizeLimit);
                var recordEnd = (ulong)_stream.Position;
                if (recordEnd > footer.SummaryStart)
                    throw new InvalidDataException("MCAP data-section record extends past summary_start.");

                if (isFirstRecord)
                {
                    if (recordOpcode != McapWriter.OpcodeHeader)
                        throw new InvalidDataException($"Expected Header (0x01) after leading magic, got 0x{recordOpcode:X2}");
                    McapRecordDecoder.DecodeHeader(content);
                    isFirstRecord = false;
                    continue;
                }

                if (recordOpcode == McapWriter.OpcodeDataEnd)
                {
                    McapRecordDecoder.DecodeDataEnd(content);
                    info.DataEndOffset = recordStart;
                    info.DataEndEndOffset = recordEnd;
                    return info;
                }
            }

            throw new InvalidDataException("MCAP amendment requires a DataEnd record before the summary section.");
        }

        /// <summary>
        /// Sequentially scans the data section and returns messages found outside the indexed path.
        /// </summary>
        public List<McapMessage> ReadSequentialMessages(
            ulong dataSectionEndOffset,
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            McapSequentialReadLimits sequentialLimits = null,
            bool validateCrcs = true,
            ulong chunkUncompressedSizeLimit = DefaultChunkUncompressedSizeLimit)
        {
            return ScanDataSection(
                dataSectionEndOffset,
                recordSizeLimit,
                collectInventory: false,
                collectMessages: true,
                sequentialLimits: sequentialLimits,
                validateCrcs: validateCrcs,
                chunkUncompressedSizeLimit: chunkUncompressedSizeLimit).SequentialMessages ?? new List<McapMessage>();
        }

        /// <summary>
        /// Reads private records from the data section into a list.
        /// </summary>
        public List<McapPrivateRecord> ReadPrivateRecords(
            ulong dataSectionEndOffset,
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            bool includeChunkRecords = true,
            bool validateCrcs = true,
            ulong chunkUncompressedSizeLimit = DefaultChunkUncompressedSizeLimit)
        {
            var records = new List<McapPrivateRecord>();
            foreach (var record in EnumeratePrivateRecords(
                         dataSectionEndOffset,
                         recordSizeLimit,
                         includeChunkRecords,
                         validateCrcs,
                         chunkUncompressedSizeLimit))
            {
                records.Add(record);
            }

            return records;
        }

        /// <summary>
        /// Enumerates application-defined private records from the data section.
        /// </summary>
        public IEnumerable<McapPrivateRecord> EnumeratePrivateRecords(
            ulong dataSectionEndOffset,
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            bool includeChunkRecords = true,
            bool validateCrcs = true,
            ulong chunkUncompressedSizeLimit = DefaultChunkUncompressedSizeLimit)
        {
            _stream.Seek(McapWriter.MagicLength, SeekOrigin.Begin);
            var isFirstRecord = true;
            while ((ulong)_stream.Position < dataSectionEndOffset)
            {
                var recordStart = (ulong)_stream.Position;
                var (opcode, content) = ReadOneRecord(recordSizeLimit);
                var recordEnd = (ulong)_stream.Position;
                if (recordEnd > dataSectionEndOffset)
                    throw new InvalidDataException("MCAP data-section record extends past the private record scan bounds.");

                if (isFirstRecord)
                {
                    if (opcode != McapWriter.OpcodeHeader)
                        throw new InvalidDataException($"Expected Header (0x01) after leading magic, got 0x{opcode:X2}");
                    McapRecordDecoder.DecodeHeader(content);
                    isFirstRecord = false;
                    continue;
                }

                if (McapWriter.IsPrivateOpcode(opcode))
                {
                    yield return new McapPrivateRecord
                    {
                        Opcode = opcode,
                        Data = CloneBytes(content),
                        Offset = recordStart,
                        InChunk = false
                    };
                    continue;
                }

                if (opcode == McapWriter.OpcodeChunk && includeChunkRecords)
                {
                    var records = McapRecordDecoder.DecodeChunkRecordsContent(
                        content,
                        out var crcValid,
                        chunkUncompressedSizeLimit);
                    if (!crcValid && validateCrcs)
                        throw new InvalidDataException("MCAP chunk CRC mismatch.");

                    foreach (var privateRecord in EnumerateChunkPrivateRecords(records, recordStart))
                        yield return privateRecord;
                    continue;
                }

                if (opcode == McapWriter.OpcodeDataEnd)
                {
                    McapRecordDecoder.DecodeDataEnd(content);
                    yield break;
                }
            }
        }

        private void ValidateSummaryCrc(
            ulong summaryStart,
            ulong summaryOffsetStart,
            uint summaryCrc,
            ulong footerOffset,
            bool validateCrcs)
        {
            if (!validateCrcs || summaryCrc == 0)
                return;

            var summaryLen = footerOffset - summaryStart;
            if (summaryLen > int.MaxValue)
                throw new InvalidDataException("MCAP summary section size exceeds int.MaxValue");

            _stream.Seek(ToSeekOffset(summaryStart, "summary_start"), SeekOrigin.Begin);
            var summaryBytes = new byte[(int)summaryLen];
            ReadExact(summaryBytes, 0, (int)summaryLen);

            var footerPrefix = McapWriter.BuildFooterCrcPrefix(summaryStart, summaryOffsetStart);
            var crc = Crc32Helper.Initialize();
            crc = Crc32Helper.Update(crc, summaryBytes);
            crc = Crc32Helper.Update(crc, footerPrefix);
            var recomputed = Crc32Helper.Finalize(crc);
            if (recomputed != summaryCrc)
                throw new InvalidDataException("MCAP summary CRC mismatch");
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
            _stream.Seek(ToSeekOffset(chunkStartOffset, "chunk"), SeekOrigin.Begin);
            var recordStart = _stream.Position;
            var (opcode, content) = ReadOneRecord();
            var recordEnd = _stream.Position;
            var actualChunkLength = (ulong)(recordEnd - recordStart);
            if (chunkLength != 0 && actualChunkLength != chunkLength)
                throw new InvalidDataException(
                    $"Chunk record at offset {chunkStartOffset} has length {actualChunkLength}, expected {chunkLength}.");
            if (opcode != McapWriter.OpcodeChunk)
                throw new InvalidDataException($"Expected Chunk (0x06) at offset {chunkStartOffset}, got 0x{opcode:X2}");

            return McapRecordDecoder.DecodeChunkRecordsContent(content, out crcValid, uncompressedSizeLimit);
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
                    var msg = McapRecordDecoder.DecodeMessage(uncompressedRecords, off, recordLength);
                    if (!filterChannelId.HasValue || msg.ChannelId == filterChannelId.Value)
                        messages.Add(msg);
                }
                off += recordLength;
            }
            return messages;
        }

        private static IEnumerable<McapPrivateRecord> EnumerateChunkPrivateRecords(
            byte[] uncompressedRecords,
            ulong chunkStartOffset)
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

                if (McapWriter.IsPrivateOpcode(opcode))
                {
                    var data = new byte[recordLength];
                    if (recordLength > 0)
                        Buffer.BlockCopy(uncompressedRecords, off, data, 0, recordLength);
                    yield return new McapPrivateRecord
                    {
                        Opcode = opcode,
                        Data = data,
                        Offset = chunkStartOffset,
                        InChunk = true
                    };
                }

                off += recordLength;
            }
        }

        private McapFileSummary ScanDataSection(
            ulong dataSectionEndOffset,
            ulong recordSizeLimit,
            bool collectInventory,
            bool collectMessages,
            McapSequentialReadLimits sequentialLimits,
            bool validateCrcs,
            ulong chunkUncompressedSizeLimit)
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
                    McapRecordDecoder.DecodeHeader(content);
                    isFirstRecord = false;
                    continue;
                }

                switch (opcode)
                {
                    case McapWriter.OpcodeHeader:
                        throw new InvalidDataException("MCAP Header record appeared after the first data-section record.");
                    case McapWriter.OpcodeSchema:
                        if (collectInventory)
                            McapRecordDecoder.AddSchema(summary.Schemas, McapRecordDecoder.DecodeSchema(content));
                        break;
                    case McapWriter.OpcodeChannel:
                        if (collectInventory)
                            McapRecordDecoder.AddChannel(summary.Channels, McapRecordDecoder.DecodeChannel(content));
                        break;
                    case McapWriter.OpcodeMessage:
                        if (collectMessages)
                        {
                            McapRecordDecoder.AddSequentialMessage(
                                summary,
                                McapRecordDecoder.DecodeMessage(content, 0, content.Length),
                                sequentialLimits,
                                ref retainedPayloadBytes,
                                ref messageCount,
                                ref messageStart,
                                ref messageEnd,
                                channelMessageCounts);
                        }
                        else
                        {
                            McapRecordDecoder.AddMessageStats(
                                McapRecordDecoder.DecodeMessageHeader(content, 0, content.Length),
                                ref messageCount,
                                ref messageStart,
                                ref messageEnd,
                                channelMessageCounts);
                        }
                        break;
                    case McapWriter.OpcodeChunk:
                        chunkCount++;
                        var records = McapRecordDecoder.DecodeChunkRecordsContent(
                            content,
                            out var crcValid,
                            chunkUncompressedSizeLimit);
                        if (!crcValid && validateCrcs)
                            throw new InvalidDataException("MCAP chunk CRC mismatch.");
                        McapRecordDecoder.ScanChunkRecords(
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
                            var attachment = McapRecordDecoder.DecodeAttachment(content);
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
                            var metadata = McapRecordDecoder.DecodeMetadata(content);
                            summary.MetadataIndexes.Add(new McapMetadataIndex
                            {
                                Offset = recordStart,
                                Length = recordEnd - recordStart,
                                Name = metadata.Name
                            });
                        }
                        break;
                    case McapWriter.OpcodeDataEnd:
                        McapRecordDecoder.DecodeDataEnd(content);
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

        // Internal

        /// <summary>
        /// Seeks to the given offset and reads a single attachment record.
        /// </summary>
        public McapAttachment ReadAttachmentAt(ulong offset)
        {
            _stream.Seek(ToSeekOffset(offset, "attachment"), SeekOrigin.Begin);
            var (opcode, content) = ReadOneRecord();
            if (opcode != McapWriter.OpcodeAttachment)
                throw new InvalidDataException($"Expected Attachment (0x09) at offset {offset}, got 0x{opcode:X2}");
            return McapRecordDecoder.DecodeAttachment(content);
        }

        /// <summary>
        /// Seeks to the given offset and reads a single metadata record.
        /// </summary>
        public McapMetadata ReadMetadataAt(ulong offset)
        {
            _stream.Seek(ToSeekOffset(offset, "metadata"), SeekOrigin.Begin);
            var (opcode, content) = ReadOneRecord();
            if (opcode != McapWriter.OpcodeMetadata)
                throw new InvalidDataException($"Expected Metadata (0x0C) at offset {offset}, got 0x{opcode:X2}");
            return McapRecordDecoder.DecodeMetadata(content);
        }

        private static long ToSeekOffset(ulong offset, string context)
        {
            if (offset > long.MaxValue)
                throw new InvalidDataException($"MCAP {context} offset {offset} exceeds seekable range.");

            return (long)offset;
        }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<byte>();

            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }

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
