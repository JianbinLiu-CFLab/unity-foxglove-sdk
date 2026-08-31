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

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Low-level MCAP binary reader. Verifies magic bytes, locates the
    /// footer and summary sections, iterates records within chunks, and
    /// delegates chunk decompression to <see cref="McapCompression"/>. This
    /// reader borrows the supplied stream; callers retain ownership and are
    /// responsible for disposing the stream.
    /// </summary>
    public class McapReader : IDisposable
    {
        private readonly Stream _stream;
        private readonly byte[] _eightByteScratch = new byte[sizeof(ulong)];
        private byte[] _recordContentBuffer;

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
        /// Releases transient reader buffers. The supplied stream remains
        /// caller-owned and is not disposed.
        /// </summary>
        public void Dispose()
        {
            _recordContentBuffer = null;
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

            var (footer, footerOffset) = ReadAndValidateFooter(recordSizeLimit);
            if (footer.SummaryStart == 0)
                return ScanDataSection(
                    footerOffset,
                    recordSizeLimit,
                    collectInventory: true,
                    collectMessages: false,
                    sequentialLimits: null,
                    validateCrcs: validateCrcs,
                    chunkUncompressedSizeLimit: chunkUncompressedSizeLimit);

            var summaryLen = footerOffset - footer.SummaryStart;
            if (summaryLen > int.MaxValue)
                throw new InvalidDataException("MCAP summary section size exceeds int.MaxValue");

            // Read summary section once. The same buffer feeds record parsing and
            // the optional summary CRC, avoiding a second stream pass.
            _stream.Seek(ToSeekOffset(footer.SummaryStart, "summary_start"), SeekOrigin.Begin);
            var summaryBytes = new byte[(int)summaryLen];
            ReadExact(summaryBytes, 0, summaryBytes.Length);
            return McapSummaryBuilder.FromSummarySection(
                summaryBytes,
                footer.SummaryStart,
                footer.SummaryOffsetStart,
                footer.SummaryCrc,
                recordSizeLimit,
                validateCrcs);
        }

        internal McapTrailerInfo ReadTrailerInfo(
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            bool validateCrcs = true)
        {
            if (!_stream.CanSeek)
                throw new NotSupportedException("McapReader.ReadTrailerInfo requires a seekable stream.");

            var (footer, footerOffset) = ReadAndValidateFooter(recordSizeLimit);
            if (footer.SummaryStart == 0)
                throw new InvalidDataException("MCAP amendment requires a summary section.");

            var summaryBytes = ReadSummaryBytes(footer.SummaryStart, footerOffset);
            ValidateSummaryCrc(
                summaryBytes,
                footer.SummaryStart,
                footer.SummaryOffsetStart,
                footer.SummaryCrc,
                validateCrcs);

            var dataEndRecordLength = (ulong)(McapWriter.RecordHeaderLength + McapWriter.Crc32SizeBytes);
            if (footer.SummaryStart < dataEndRecordLength)
                throw new InvalidDataException("Footer summary_start is before the DataEnd record.");

            var dataEndOffset = footer.SummaryStart - dataEndRecordLength;
            _stream.Seek(ToSeekOffset(dataEndOffset, "DataEnd"), SeekOrigin.Begin);
            var (dataEndOpcode, dataEndContent, dataEndContentLength) = ReadOneRecordSegment(recordSizeLimit);
            if (dataEndOpcode != McapWriter.OpcodeDataEnd)
                throw new InvalidDataException($"Expected DataEnd (0x0F) before summary_start, got 0x{dataEndOpcode:X2}");
            var dataEndEndOffset = (ulong)_stream.Position;
            if (dataEndEndOffset != footer.SummaryStart)
                throw new InvalidDataException("MCAP DataEnd record does not end at summary_start.");

            return new McapTrailerInfo
            {
                FooterOffset = footerOffset,
                SummaryStart = footer.SummaryStart,
                SummaryOffsetStart = footer.SummaryOffsetStart,
                SummaryCrc = footer.SummaryCrc,
                DataEndOffset = dataEndOffset,
                DataEndEndOffset = dataEndEndOffset,
                DataSectionCrc = McapRecordDecoder.DecodeDataEnd(dataEndContent, 0, dataEndContentLength)
            };
        }

        internal McapFileSummary ReadDataSectionSummary(
            ulong dataSectionEndOffset,
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            bool validateCrcs = true,
            ulong chunkUncompressedSizeLimit = DefaultChunkUncompressedSizeLimit)
        {
            return ScanDataSection(
                dataSectionEndOffset,
                recordSizeLimit,
                collectInventory: true,
                collectMessages: false,
                sequentialLimits: null,
                validateCrcs: validateCrcs,
                chunkUncompressedSizeLimit: chunkUncompressedSizeLimit);
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
                chunkUncompressedSizeLimit: chunkUncompressedSizeLimit).SequentialMessages
                ?? throw new InvalidOperationException("MCAP sequential scan did not collect messages.");
        }

        internal void VisitSequentialMessages(
            ulong dataSectionEndOffset,
            Action<McapMessage> visitor,
            ulong recordSizeLimit = DefaultRecordSizeLimit,
            bool validateCrcs = true,
            ulong chunkUncompressedSizeLimit = DefaultChunkUncompressedSizeLimit)
        {
            if (visitor == null)
                throw new ArgumentNullException(nameof(visitor));

            _stream.Seek(McapWriter.MagicLength, SeekOrigin.Begin);
            var isFirstRecord = true;
            while ((ulong)_stream.Position < dataSectionEndOffset)
            {
                var (opcode, content, contentLength) = ReadOneRecordSegment(recordSizeLimit);
                var recordEnd = (ulong)_stream.Position;
                if (recordEnd > dataSectionEndOffset)
                    throw new InvalidDataException("MCAP data-section record extends past the message scan bounds.");

                if (isFirstRecord)
                {
                    if (opcode != McapWriter.OpcodeHeader)
                        throw new InvalidDataException($"Expected Header (0x01) after leading magic, got 0x{opcode:X2}");
                    McapRecordDecoder.DecodeHeader(content, 0, contentLength);
                    isFirstRecord = false;
                    continue;
                }

                if (opcode == McapWriter.OpcodeMessage)
                {
                    visitor(McapRecordDecoder.DecodeMessage(content, 0, contentLength));
                    continue;
                }

                if (opcode == McapWriter.OpcodeChunk)
                {
                    var records = McapChunkReader.DecodeChunkRecordsContent(
                        content,
                        0,
                        contentLength,
                        out var crcValid,
                        chunkUncompressedSizeLimit);
                    McapChunkReader.EnsureCrcValid(crcValid, validateCrcs);

                    foreach (var message in McapChunkReader.EnumerateMessages(records))
                        visitor(message);
                    continue;
                }

                if (opcode == McapWriter.OpcodeDataEnd)
                {
                    McapRecordDecoder.DecodeDataEnd(content, 0, contentLength);
                    return;
                }

                if (opcode == McapWriter.OpcodeHeader)
                    throw new InvalidDataException("MCAP Header record appeared after the first data-section record.");
            }
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
        /// Enumeration seeks the borrowed stream to the data-section start on first
        /// MoveNext; callers must not interleave other stream operations while
        /// iterating.
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
                var (opcode, content, contentLength) = ReadOneRecordSegment(recordSizeLimit);
                var recordEnd = (ulong)_stream.Position;
                if (recordEnd > dataSectionEndOffset)
                    throw new InvalidDataException("MCAP data-section record extends past the private record scan bounds.");

                if (isFirstRecord)
                {
                    if (opcode != McapWriter.OpcodeHeader)
                        throw new InvalidDataException($"Expected Header (0x01) after leading magic, got 0x{opcode:X2}");
                    McapRecordDecoder.DecodeHeader(content, 0, contentLength);
                    isFirstRecord = false;
                    continue;
                }

                if (McapWriter.IsPrivateOpcode(opcode))
                {
                    yield return new McapPrivateRecord
                    {
                        Opcode = opcode,
                        Data = CloneBytes(content, contentLength),
                        Offset = recordStart,
                        InChunk = false
                    };
                    continue;
                }

                if (opcode == McapWriter.OpcodeChunk && includeChunkRecords)
                {
                    var records = McapChunkReader.DecodeChunkRecordsContent(
                        content,
                        0,
                        contentLength,
                        out var crcValid,
                        chunkUncompressedSizeLimit);
                    McapChunkReader.EnsureCrcValid(crcValid, validateCrcs);

                    foreach (var privateRecord in McapChunkReader.EnumeratePrivateRecords(records, recordStart))
                        yield return privateRecord;
                    continue;
                }

                if (opcode == McapWriter.OpcodeDataEnd)
                {
                    McapRecordDecoder.DecodeDataEnd(content, 0, contentLength);
                    yield break;
                }
            }
        }

        private (McapFooter footer, ulong footerOffset) ReadAndValidateFooter(ulong recordSizeLimit)
        {
            const int minFileBytes =
                McapWriter.MagicLength + McapWriter.RecordHeaderLength +
                McapWriter.FooterContentLength + McapWriter.MagicLength;
            if (_stream.Length < minFileBytes)
                throw new EndOfStreamException("MCAP stream is shorter than the minimum header/footer size");

            _stream.Seek(0, SeekOrigin.Begin);
            ReadExact(_eightByteScratch, 0, _eightByteScratch.Length);
            ValidateMagic("leading");

            _stream.Seek(-McapWriter.MagicLength, SeekOrigin.End);
            ReadExact(_eightByteScratch, 0, _eightByteScratch.Length);
            ValidateMagic("trailing");

            var footerOffset = (ulong)_stream.Length
                - McapWriter.MagicLength
                - McapWriter.RecordHeaderLength
                - McapWriter.FooterContentLength;
            _stream.Seek(ToSeekOffset(footerOffset, "footer"), SeekOrigin.Begin);
            var (opcode, footerContent, footerContentLength) = ReadOneRecordSegment(recordSizeLimit);
            if (opcode != McapWriter.OpcodeFooter)
                throw new InvalidDataException($"Expected Footer (0x02) at end of file, got 0x{opcode:X2}");

            var footer = McapRecordDecoder.DecodeFooter(footerContent, 0, footerContentLength);
            if (footer.SummaryStart != 0)
            {
                if (footer.SummaryStart > footerOffset)
                    throw new InvalidDataException("Footer summary_start is past the footer record");
                if (footer.SummaryStart < (ulong)(McapWriter.MagicLength + McapWriter.RecordHeaderLength))
                    throw new InvalidDataException("Footer summary_start is before the data section");
                if (footer.SummaryOffsetStart != 0 &&
                    (footer.SummaryOffsetStart < footer.SummaryStart || footer.SummaryOffsetStart > footerOffset))
                    throw new InvalidDataException("Footer summary_offset_start is outside the summary section bounds");
            }

            return (footer, footerOffset);
        }

        private void ValidateMagic(string location)
        {
            var expectedMagic = McapWriter.MagicSpan;
            for (var i = 0; i < expectedMagic.Length; i++)
                if (_eightByteScratch[i] != expectedMagic[i])
                    throw new InvalidDataException($"MCAP {location} magic mismatch");
        }

        private byte[] ReadSummaryBytes(ulong summaryStart, ulong footerOffset)
        {
            var summaryLen = footerOffset - summaryStart;
            if (summaryLen > int.MaxValue)
                throw new InvalidDataException("MCAP summary section size exceeds int.MaxValue");

            _stream.Seek(ToSeekOffset(summaryStart, "summary_start"), SeekOrigin.Begin);
            var summaryBytes = new byte[(int)summaryLen];
            ReadExact(summaryBytes, 0, summaryBytes.Length);
            return summaryBytes;
        }

        private static void ValidateSummaryCrc(
            byte[] summaryBytes,
            ulong summaryStart,
            ulong summaryOffsetStart,
            uint summaryCrc,
            bool validateCrcs)
        {
            if (!validateCrcs || summaryCrc == 0)
                return;

            McapSummaryBuilder.ValidateSummaryCrc(summaryBytes, summaryStart, summaryOffsetStart, summaryCrc);
        }

        /// <summary>
        /// Reads one MCAP record from the current stream position, returning its opcode and content bytes.
        /// </summary>
        public (byte opcode, byte[] content) ReadOneRecord(ulong sizeLimit = DefaultRecordSizeLimit)
        {
            var (opcode, content, contentLength) = ReadOneRecordSegment(sizeLimit);
            return (opcode, CloneBytes(content, contentLength));
        }

        /// <summary>
        /// Reads one record into the internal reuse buffer. The returned content array
        /// is invalidated by the next call to this method; callers that need to retain
        /// data must clone the first <c>contentLength</c> bytes.
        /// </summary>
        private (byte opcode, byte[] content, int contentLength) ReadOneRecordSegment(ulong sizeLimit = DefaultRecordSizeLimit)
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
            if (_stream.CanSeek)
            {
                var remaining = _stream.Length - _stream.Position;
                if (remaining < 0 || contentLength > (ulong)remaining)
                {
                    throw new EndOfStreamException(
                        $"MCAP record declares {contentLength} content bytes but only {Math.Max(remaining, 0)} remain");
                }
            }
            var contentLengthInt = (int)contentLength;
            var content = EnsureRecordContentBuffer(contentLengthInt);
            ReadExact(content, 0, contentLengthInt);
            return (opcode, content, contentLengthInt);
        }

        /// <summary>
        /// Reads and decompresses a chunk's record data from the given offset and length.
        /// Validates the uncompressed CRC32 if one is stored (non-zero).
        /// </summary>
        public byte[] ReadChunkRecords(
            ulong chunkStartOffset,
            ulong chunkLength,
            out bool crcValid,
            ulong uncompressedSizeLimit = DefaultChunkUncompressedSizeLimit,
            ulong recordSizeLimit = DefaultRecordSizeLimit)
        {
            _stream.Seek(ToSeekOffset(chunkStartOffset, "chunk"), SeekOrigin.Begin);
            var recordStart = _stream.Position;
            var (opcode, content, contentLength) = ReadOneRecordSegment(recordSizeLimit);
            var recordEnd = _stream.Position;
            return McapChunkReader.ReadChunkRecords(
                opcode,
                content,
                contentLength,
                chunkStartOffset,
                chunkLength,
                recordStart,
                recordEnd,
                out crcValid,
                uncompressedSizeLimit);
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
            return McapChunkReader.ReadMessages(uncompressedRecords, filterChannelId);
        }

        /// <summary>
        /// Enumerates MCAP messages from decompressed chunk data, optionally filtering by channel ID.
        /// </summary>
        public IEnumerable<McapMessage> EnumerateChunkMessages(byte[] uncompressedRecords, ushort? filterChannelId = null)
        {
            return McapChunkReader.EnumerateMessages(uncompressedRecords, filterChannelId);
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
            var builder = new McapSummaryBuilder(
                dataSectionEndOffset,
                collectInventory,
                collectMessages,
                sequentialLimits);
            _stream.Seek(McapWriter.MagicLength, SeekOrigin.Begin);
            var isFirstRecord = true;
            while ((ulong)_stream.Position < dataSectionEndOffset)
            {
                var recordStart = (ulong)_stream.Position;
                var (opcode, content, contentLength) = ReadOneRecordSegment(recordSizeLimit);
                var recordEnd = (ulong)_stream.Position;
                if (recordEnd > dataSectionEndOffset)
                    throw new InvalidDataException("MCAP data-section record extends past the data section bounds.");

                if (isFirstRecord)
                {
                    if (opcode != McapWriter.OpcodeHeader)
                        throw new InvalidDataException($"Expected Header (0x01) after leading magic, got 0x{opcode:X2}");
                    McapRecordDecoder.DecodeHeader(content, 0, contentLength);
                    isFirstRecord = false;
                    continue;
                }

                if (!builder.ApplyRecord(
                        opcode,
                        content,
                        contentLength,
                        recordStart,
                        recordEnd,
                        validateCrcs,
                        chunkUncompressedSizeLimit))
                    break;
            }

            return builder.Build();
        }

        // Internal

        /// <summary>
        /// Seeks to the given offset and reads a single attachment record.
        /// </summary>
        public McapAttachment ReadAttachmentAt(ulong offset)
        {
            _stream.Seek(ToSeekOffset(offset, "attachment"), SeekOrigin.Begin);
            var (opcode, content, contentLength) = ReadOneRecordSegment();
            if (opcode != McapWriter.OpcodeAttachment)
                throw new InvalidDataException($"Expected Attachment (0x09) at offset {offset}, got 0x{opcode:X2}");
            return McapRecordDecoder.DecodeAttachment(content, 0, contentLength);
        }

        /// <summary>
        /// Seeks to the given offset and reads a single metadata record.
        /// </summary>
        public McapMetadata ReadMetadataAt(ulong offset)
        {
            _stream.Seek(ToSeekOffset(offset, "metadata"), SeekOrigin.Begin);
            var (opcode, content, contentLength) = ReadOneRecordSegment();
            if (opcode != McapWriter.OpcodeMetadata)
                throw new InvalidDataException($"Expected Metadata (0x0C) at offset {offset}, got 0x{opcode:X2}");
            return McapRecordDecoder.DecodeMetadata(content, 0, contentLength);
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

        private static byte[] CloneBytes(byte[] source, int count)
        {
            if (source == null || count <= 0)
                return Array.Empty<byte>();

            var copy = new byte[count];
            Buffer.BlockCopy(source, 0, copy, 0, count);
            return copy;
        }

        private byte[] EnsureRecordContentBuffer(int count)
        {
            if (_recordContentBuffer == null || _recordContentBuffer.Length < count)
                _recordContentBuffer = new byte[count];
            return _recordContentBuffer;
        }

        /// <summary>
        /// Reads 8 bytes from the stream and assembles them into a little-endian UInt64.
        /// </summary>
        private ulong ReadU64()
        {
            ReadExact(_eightByteScratch, 0, _eightByteScratch.Length);
            return (ulong)_eightByteScratch[0]
                 | ((ulong)_eightByteScratch[1] << 8)
                 | ((ulong)_eightByteScratch[2] << 16)
                 | ((ulong)_eightByteScratch[3] << 24)
                 | ((ulong)_eightByteScratch[4] << 32)
                 | ((ulong)_eightByteScratch[5] << 40)
                 | ((ulong)_eightByteScratch[6] << 48)
                 | ((ulong)_eightByteScratch[7] << 56);
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
