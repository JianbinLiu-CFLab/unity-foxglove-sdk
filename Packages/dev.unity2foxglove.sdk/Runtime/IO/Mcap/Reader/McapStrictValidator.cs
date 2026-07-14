// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Reader
// Purpose: Opt-in MCAP current-version structural conformance validation.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Options for <see cref="McapStrictValidator"/>.</summary>
    public sealed class McapStrictValidationOptions
    {
        /// <summary>Validate every non-zero data, chunk, attachment, and summary CRC.</summary>
        public bool ValidateCrcs = true;

        /// <summary>
        /// Reject trailing fields not defined by the current MCAP record
        /// version. Disable this only when structural validation must remain
        /// forward-compatible with additive record fields.
        /// </summary>
        public bool RequireCurrentVersionRecordLengths = true;

        /// <summary>Maximum accepted top-level record content size.</summary>
        public ulong RecordSizeLimit = McapReader.DefaultRecordSizeLimit;

        /// <summary>Maximum accepted uncompressed Chunk records payload size.</summary>
        public ulong ChunkUncompressedSizeLimit = McapReader.DefaultChunkUncompressedSizeLimit;
    }

    /// <summary>
    /// Explicit MCAP conformance validator. Normal replay readers remain
    /// forward-compatible and tolerant; this entry point additionally rejects
    /// reserved opcodes, current-version trailing fields, invalid placement,
    /// and unresolved schema/channel references.
    /// </summary>
    public static class McapStrictValidator
    {
        /// <summary>
        /// Validates the complete seekable stream and returns its parsed
        /// summary. The caller retains ownership of the stream and its original
        /// position is restored.
        /// </summary>
        public static McapFileSummary Validate(
            Stream stream,
            McapStrictValidationOptions options = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek)
                throw new NotSupportedException("Strict MCAP validation requires a readable, seekable stream.");

            options ??= new McapStrictValidationOptions();
            if (options.RecordSizeLimit == 0)
                throw new ArgumentOutOfRangeException(nameof(options), "RecordSizeLimit must be greater than zero.");
            if (options.ChunkUncompressedSizeLimit == 0)
                throw new ArgumentOutOfRangeException(nameof(options), "ChunkUncompressedSizeLimit must be greater than zero.");

            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                McapFileSummary summary;
                using (var reader = new McapReader(stream))
                {
                    summary = reader.ReadSummary(
                        options.RecordSizeLimit,
                        options.ValidateCrcs,
                        options.ChunkUncompressedSizeLimit);
                }

                stream.Position = 0;
                new StrictScanner(stream, options).Validate();
                return summary;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        private sealed class StrictScanner
        {
            private readonly Stream _stream;
            private readonly McapStrictValidationOptions _options;
            private readonly List<McapSchema> _schemas = new List<McapSchema>();
            private readonly List<McapChannel> _channels = new List<McapChannel>();
            private readonly byte[] _u64Buffer = new byte[sizeof(ulong)];
            private bool _sawHeader;
            private bool _sawDataEnd;
            private bool _sawFooter;
            private bool _messageIndexMayFollow;
            private long _firstSummaryRecordOffset = -1;
            private long _dataEndEndOffset = -1;
            private McapFooter _footer;

            public StrictScanner(Stream stream, McapStrictValidationOptions options)
            {
                _stream = stream;
                _options = options;
            }

            public void Validate()
            {
                var magic = McapWriter.Magic;
                if (_stream.Length < magic.Length * 2L + McapWriter.RecordHeaderLength + McapWriter.FooterContentLength)
                    throw new InvalidDataException("MCAP stream is shorter than the strict minimum file size.");

                RequireMagic(magic, leading: true);
                var bodyEnd = _stream.Length - magic.Length;
                while (_stream.Position < bodyEnd)
                {
                    var recordStart = _stream.Position;
                    var record = ReadRecord(bodyEnd);
                    ValidateRecord(recordStart, record.Opcode, record.Content);
                }

                if (_stream.Position != bodyEnd)
                    throw new InvalidDataException("MCAP record data overlaps the trailing magic.");
                RequireMagic(magic, leading: false);
                if (!_sawHeader || !_sawDataEnd || !_sawFooter)
                    throw new InvalidDataException("MCAP strict validation requires one Header, DataEnd, and Footer record.");

                var expectedSummaryStart = _firstSummaryRecordOffset >= 0
                    ? (ulong)_firstSummaryRecordOffset
                    : 0UL;
                if (_footer.SummaryStart != expectedSummaryStart)
                    throw new InvalidDataException(
                        $"Footer summary_start {_footer.SummaryStart} does not match the strict summary boundary {expectedSummaryStart}.");
                if (_firstSummaryRecordOffset >= 0 && _dataEndEndOffset != _firstSummaryRecordOffset)
                    throw new InvalidDataException("The summary section must begin immediately after DataEnd.");
            }

            private void ValidateRecord(long recordStart, byte opcode, byte[] content)
            {
                if (opcode == 0)
                    throw new InvalidDataException("MCAP opcode 0x00 is invalid.");
                if (McapWriter.IsReservedOpcode(opcode))
                    throw new InvalidDataException($"MCAP opcode 0x{opcode:X2} is reserved.");
                if (_sawFooter)
                    throw new InvalidDataException("No records may follow the Footer.");
                if (!_sawHeader && opcode != McapWriter.OpcodeHeader)
                    throw new InvalidDataException("Header must be the first MCAP record.");

                if (McapWriter.IsPrivateOpcode(opcode))
                {
                    if (_sawDataEnd && _firstSummaryRecordOffset < 0)
                        _firstSummaryRecordOffset = recordStart;
                    _messageIndexMayFollow = false;
                    return;
                }

                if (!_sawDataEnd)
                    ValidateDataRecord(recordStart, opcode, content);
                else
                    ValidateSummaryOrFooterRecord(recordStart, opcode, content);
            }

            private void ValidateDataRecord(long recordStart, byte opcode, byte[] content)
            {
                if (opcode != McapWriter.OpcodeMessageIndex)
                    _messageIndexMayFollow = false;

                switch (opcode)
                {
                    case McapWriter.OpcodeHeader:
                        if (_sawHeader)
                            throw new InvalidDataException("MCAP contains more than one Header record.");
                        ValidateCurrentVersionLength(opcode, content);
                        McapRecordDecoder.DecodeHeader(content);
                        _sawHeader = true;
                        break;
                    case McapWriter.OpcodeSchema:
                        AddSchema(content);
                        break;
                    case McapWriter.OpcodeChannel:
                        AddChannel(content);
                        break;
                    case McapWriter.OpcodeMessage:
                        ValidateMessage(content);
                        break;
                    case McapWriter.OpcodeChunk:
                        ValidateChunk(content);
                        _messageIndexMayFollow = true;
                        break;
                    case McapWriter.OpcodeMessageIndex:
                        if (!_messageIndexMayFollow)
                            throw new InvalidDataException("Message Index records must immediately follow a Chunk.");
                        ValidateMessageIndex(content);
                        _messageIndexMayFollow = true;
                        break;
                    case McapWriter.OpcodeAttachment:
                    {
                        ValidateCurrentVersionLength(opcode, content);
                        var attachment = McapRecordDecoder.DecodeAttachment(content);
                        if (_options.ValidateCrcs && !attachment.CrcValid)
                            throw new InvalidDataException("MCAP attachment CRC mismatch.");
                        break;
                    }
                    case McapWriter.OpcodeMetadata:
                        ValidateCurrentVersionLength(opcode, content);
                        McapRecordDecoder.DecodeMetadata(content);
                        break;
                    case McapWriter.OpcodeDataEnd:
                    {
                        ValidateCurrentVersionLength(opcode, content);
                        var expectedCrc = McapRecordDecoder.DecodeDataEnd(content);
                        if (_options.ValidateCrcs && expectedCrc != 0)
                        {
                            var position = _stream.Position;
                            _stream.Position = 0;
                            var actualCrc = Crc32Helper.Compute(_stream, recordStart);
                            _stream.Position = position;
                            if (actualCrc != expectedCrc)
                                throw new InvalidDataException("MCAP data section CRC mismatch.");
                        }
                        _sawDataEnd = true;
                        _dataEndEndOffset = _stream.Position;
                        break;
                    }
                    default:
                        throw new InvalidDataException($"MCAP opcode 0x{opcode:X2} is not allowed in the data section.");
                }
            }

            private void ValidateSummaryOrFooterRecord(long recordStart, byte opcode, byte[] content)
            {
                if (opcode == McapWriter.OpcodeFooter)
                {
                    ValidateCurrentVersionLength(opcode, content);
                    _footer = McapRecordDecoder.DecodeFooter(content);
                    _sawFooter = true;
                    return;
                }

                if (_firstSummaryRecordOffset < 0)
                    _firstSummaryRecordOffset = recordStart;

                switch (opcode)
                {
                    case McapWriter.OpcodeSchema:
                        AddSchema(content);
                        break;
                    case McapWriter.OpcodeChannel:
                        AddChannel(content);
                        break;
                    case McapWriter.OpcodeChunkIndex:
                        ValidateCurrentVersionLength(opcode, content);
                        McapRecordDecoder.DecodeChunkIndex(content);
                        break;
                    case McapWriter.OpcodeAttachmentIndex:
                        ValidateCurrentVersionLength(opcode, content);
                        McapRecordDecoder.DecodeAttachmentIndex(content);
                        break;
                    case McapWriter.OpcodeStatistics:
                        ValidateCurrentVersionLength(opcode, content);
                        McapRecordDecoder.DecodeStatistics(content);
                        break;
                    case McapWriter.OpcodeMetadataIndex:
                        ValidateCurrentVersionLength(opcode, content);
                        McapRecordDecoder.DecodeMetadataIndex(content);
                        break;
                    case McapWriter.OpcodeSummaryOffset:
                        ValidateCurrentVersionLength(opcode, content);
                        McapRecordDecoder.DecodeSummaryOffset(content, 0, content.Length);
                        break;
                    default:
                        throw new InvalidDataException($"MCAP opcode 0x{opcode:X2} is not allowed in the summary section.");
                }
            }

            private void AddSchema(byte[] content)
            {
                ValidateCurrentVersionLength(McapWriter.OpcodeSchema, content);
                var schema = McapRecordDecoder.DecodeSchema(content);
                if (schema.Id == 0)
                    throw new InvalidDataException("Schema id 0 is reserved for channels without a schema.");
                McapRecordDecoder.AddSchema(_schemas, schema);
            }

            private void AddChannel(byte[] content)
            {
                ValidateCurrentVersionLength(McapWriter.OpcodeChannel, content);
                var channel = McapRecordDecoder.DecodeChannel(content);
                if (channel.SchemaId != 0 && !ContainsSchema(channel.SchemaId))
                    throw new InvalidDataException($"Channel {channel.Id} references unknown schema {channel.SchemaId}.");
                McapRecordDecoder.AddChannel(_channels, channel);
            }

            private void ValidateMessage(byte[] content)
            {
                var message = McapRecordDecoder.DecodeMessage(content, 0, content.Length);
                if (!ContainsChannel(message.ChannelId))
                    throw new InvalidDataException($"Message references unknown channel {message.ChannelId}.");
            }

            private void ValidateChunk(byte[] content)
            {
                ValidateCurrentVersionLength(McapWriter.OpcodeChunk, content);
                var records = McapRecordDecoder.DecodeChunkRecordsContent(
                    content,
                    out var crcValid,
                    _options.ChunkUncompressedSizeLimit);
                if (_options.ValidateCrcs && !crcValid)
                    throw new InvalidDataException("MCAP chunk CRC mismatch.");

                var off = 0;
                while (off < records.Length)
                {
                    if (records.Length - off < McapWriter.RecordHeaderLength)
                        throw new InvalidDataException("Chunk inner record header is truncated.");
                    var opcode = records[off++];
                    var length = McapBinaryReader.ReadU64LE(records, ref off);
                    if (length > int.MaxValue || (int)length > records.Length - off)
                        throw new InvalidDataException("Chunk inner record content is truncated.");
                    var contentLength = (int)length;
                    var inner = new byte[contentLength];
                    if (contentLength > 0)
                        Buffer.BlockCopy(records, off, inner, 0, contentLength);

                    if (McapWriter.IsReservedOpcode(opcode) || opcode == 0)
                        throw new InvalidDataException($"Chunk contains invalid opcode 0x{opcode:X2}.");
                    if (McapWriter.IsPrivateOpcode(opcode))
                    {
                        off += contentLength;
                        continue;
                    }

                    switch (opcode)
                    {
                        case McapWriter.OpcodeSchema:
                            AddSchema(inner);
                            break;
                        case McapWriter.OpcodeChannel:
                            AddChannel(inner);
                            break;
                        case McapWriter.OpcodeMessage:
                            ValidateMessage(inner);
                            break;
                        case McapWriter.OpcodeMetadata:
                            ValidateCurrentVersionLength(opcode, inner);
                            McapRecordDecoder.DecodeMetadata(inner);
                            break;
                        default:
                            throw new InvalidDataException($"MCAP opcode 0x{opcode:X2} is not allowed inside a Chunk.");
                    }
                    off += contentLength;
                }
            }

            private void ValidateMessageIndex(byte[] content)
            {
                ValidateCurrentVersionLength(McapWriter.OpcodeMessageIndex, content);
                var off = 0;
                var channelId = McapBinaryReader.ReadU16LE(content, ref off);
                if (!ContainsChannel(channelId))
                    throw new InvalidDataException($"Message Index references unknown channel {channelId}.");
            }

            private void ValidateCurrentVersionLength(byte opcode, byte[] content)
            {
                if (!_options.RequireCurrentVersionRecordLengths || McapWriter.IsPrivateOpcode(opcode))
                    return;

                var off = 0;
                switch (opcode)
                {
                    case McapWriter.OpcodeHeader:
                        ReadString(content, ref off);
                        ReadString(content, ref off);
                        break;
                    case McapWriter.OpcodeSchema:
                        ReadU16(content, ref off);
                        ReadString(content, ref off);
                        ReadString(content, ref off);
                        ReadPrefixed(content, ref off);
                        break;
                    case McapWriter.OpcodeChannel:
                        ReadU16(content, ref off);
                        ReadU16(content, ref off);
                        ReadString(content, ref off);
                        ReadString(content, ref off);
                        ReadMap(content, ref off);
                        break;
                    case McapWriter.OpcodeMessage:
                        EnsureAvailable(content, off, sizeof(ushort) + sizeof(uint) + sizeof(ulong) * 2, "message header");
                        off = content.Length;
                        break;
                    case McapWriter.OpcodeChunk:
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        ReadU32(content, ref off);
                        ReadString(content, ref off);
                        Advance(content, ref off, ReadU64(content, ref off), "chunk records");
                        break;
                    case McapWriter.OpcodeMessageIndex:
                        ReadU16(content, ref off);
                        var messageIndexBytes = ReadU32(content, ref off);
                        if (messageIndexBytes % 16 != 0)
                            throw new InvalidDataException("Message Index records length must be divisible by 16.");
                        Advance(content, ref off, messageIndexBytes, "message index records");
                        break;
                    case McapWriter.OpcodeChunkIndex:
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        var messageIndexOffsetsBytes = ReadU32(content, ref off);
                        if (messageIndexOffsetsBytes % 10 != 0)
                            throw new InvalidDataException("Chunk Index message_index_offsets length must be divisible by 10.");
                        Advance(content, ref off, messageIndexOffsetsBytes, "chunk index offsets");
                        ReadU64(content, ref off);
                        ReadString(content, ref off);
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        break;
                    case McapWriter.OpcodeAttachment:
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        ReadString(content, ref off);
                        ReadString(content, ref off);
                        Advance(content, ref off, ReadU64(content, ref off), "attachment data");
                        Advance(content, ref off, sizeof(uint), "attachment CRC");
                        break;
                    case McapWriter.OpcodeAttachmentIndex:
                        for (var i = 0; i < 5; i++) ReadU64(content, ref off);
                        ReadString(content, ref off);
                        ReadString(content, ref off);
                        break;
                    case McapWriter.OpcodeStatistics:
                        ReadU64(content, ref off);
                        ReadU16(content, ref off);
                        for (var i = 0; i < 4; i++) ReadU32(content, ref off);
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        var channelCountsBytes = ReadU32(content, ref off);
                        if (channelCountsBytes % 10 != 0)
                            throw new InvalidDataException("Statistics channel_message_counts length must be divisible by 10.");
                        Advance(content, ref off, channelCountsBytes, "statistics channel counts");
                        break;
                    case McapWriter.OpcodeMetadata:
                        ReadString(content, ref off);
                        ReadMap(content, ref off);
                        break;
                    case McapWriter.OpcodeMetadataIndex:
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        ReadString(content, ref off);
                        break;
                    case McapWriter.OpcodeSummaryOffset:
                        Advance(content, ref off, 1, "summary group opcode");
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        break;
                    case McapWriter.OpcodeDataEnd:
                        ReadU32(content, ref off);
                        break;
                    case McapWriter.OpcodeFooter:
                        ReadU64(content, ref off);
                        ReadU64(content, ref off);
                        ReadU32(content, ref off);
                        break;
                }

                if (off != content.Length)
                    throw new InvalidDataException(
                        $"MCAP opcode 0x{opcode:X2} has {content.Length - off} trailing current-version bytes.");
            }

            private bool ContainsSchema(ushort id)
            {
                for (var i = 0; i < _schemas.Count; i++)
                    if (_schemas[i].Id == id) return true;
                return false;
            }

            private bool ContainsChannel(ushort id)
            {
                for (var i = 0; i < _channels.Count; i++)
                    if (_channels[i].Id == id) return true;
                return false;
            }

            private Record ReadRecord(long bodyEnd)
            {
                if (bodyEnd - _stream.Position < McapWriter.RecordHeaderLength)
                    throw new InvalidDataException("MCAP top-level record header is truncated.");
                var opcodeValue = _stream.ReadByte();
                if (opcodeValue < 0)
                    throw new EndOfStreamException("MCAP ended before a record opcode.");
                ReadExact(_u64Buffer, 0, _u64Buffer.Length);
                var off = 0;
                var length = McapBinaryReader.ReadU64LE(_u64Buffer, ref off);
                if (length > _options.RecordSizeLimit)
                    throw new InvalidDataException($"MCAP record length {length} exceeds strict limit {_options.RecordSizeLimit}.");
                if (length > int.MaxValue || length > (ulong)(bodyEnd - _stream.Position))
                    throw new InvalidDataException("MCAP top-level record content is truncated.");
                var content = new byte[(int)length];
                ReadExact(content, 0, content.Length);
                return new Record((byte)opcodeValue, content);
            }

            private void RequireMagic(byte[] magic, bool leading)
            {
                var actual = new byte[magic.Length];
                ReadExact(actual, 0, actual.Length);
                for (var i = 0; i < magic.Length; i++)
                {
                    if (actual[i] != magic[i])
                        throw new InvalidDataException($"MCAP {(leading ? "leading" : "trailing")} magic mismatch.");
                }
            }

            private void ReadExact(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    var read = _stream.Read(buffer, offset, count);
                    if (read <= 0)
                        throw new EndOfStreamException("Unexpected end of MCAP stream.");
                    offset += read;
                    count -= read;
                }
            }

            private static ushort ReadU16(byte[] content, ref int off)
            {
                EnsureAvailable(content, off, sizeof(ushort), "uint16");
                return McapBinaryReader.ReadU16LE(content, ref off);
            }

            private static uint ReadU32(byte[] content, ref int off)
            {
                EnsureAvailable(content, off, sizeof(uint), "uint32");
                return McapBinaryReader.ReadU32LE(content, ref off);
            }

            private static ulong ReadU64(byte[] content, ref int off)
            {
                EnsureAvailable(content, off, sizeof(ulong), "uint64");
                return McapBinaryReader.ReadU64LE(content, ref off);
            }

            private static void ReadString(byte[] content, ref int off)
            {
                McapBinaryReader.ReadString(content, ref off);
            }

            private static void ReadPrefixed(byte[] content, ref int off)
            {
                McapBinaryReader.ReadPrefixed(content, ref off);
            }

            private static void ReadMap(byte[] content, ref int off)
            {
                McapBinaryReader.ReadMap(content, ref off);
            }

            private static void Advance(byte[] content, ref int off, ulong count, string fieldName)
            {
                if (count > int.MaxValue)
                    throw new InvalidDataException($"MCAP {fieldName} length exceeds int.MaxValue.");
                EnsureAvailable(content, off, (int)count, fieldName);
                off += (int)count;
            }

            private static void EnsureAvailable(byte[] content, int off, int count, string fieldName)
            {
                if (off < 0 || count < 0 || count > content.Length - off)
                    throw new InvalidDataException($"MCAP {fieldName} is truncated.");
            }

            private readonly struct Record
            {
                public Record(byte opcode, byte[] content)
                {
                    Opcode = opcode;
                    Content = content;
                }

                public byte Opcode { get; }
                public byte[] Content { get; }
            }
        }
    }
}
