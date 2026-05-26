// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/McapConformance
// Purpose: Converts Unity2Foxglove MCAP reader output into official conformance JSON records.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests.McapConformance
{
    internal static class McapConformanceReader
    {
        public static List<SerializableMcapRecord> ReadStreamed(string filePath)
        {
            using (var file = File.OpenRead(filePath))
            using (var nonSeekable = new NonSeekableReadStream(file))
            using (var streaming = new McapStreamingReader(nonSeekable, leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests))
            {
                streaming.Read(new McapReadOptions
                {
                    Order = McapReadOrder.FileOrder,
                    UseOfficialEndTimeSemantics = true,
                    ValidateCrcs = true
                });
            }

            var scanner = new Scanner(File.ReadAllBytes(filePath));
            return scanner.ReadStreamed();
        }

        public static IndexedReadResult ReadIndexed(string filePath)
        {
            var result = new IndexedReadResult();
            using (var indexed = McapIndexedReader.OpenRead(filePath, McapSequentialReadLimits.UnlimitedForTests))
            {
                for (var i = 0; i < indexed.Summary.Schemas.Count; i++)
                    result.Schemas.Add(ToRecord(indexed.Summary.Schemas[i]));
                for (var i = 0; i < indexed.Summary.Channels.Count; i++)
                    result.Channels.Add(ToRecord(indexed.Summary.Channels[i]));
                if (indexed.Summary.Statistics != null)
                    result.Statistics.Add(ToRecord(indexed.Summary.Statistics));

                var messages = indexed.ReadMessages(new McapReadOptions
                {
                    AllowLinearFallback = false,
                    ValidateCrcs = true,
                    Order = McapReadOrder.LogTimeAscending
                });
                for (var i = 0; i < messages.Count; i++)
                    result.Messages.Add(ToRecord(messages[i]));
            }

            result.Schemas.Sort((left, right) => CompareNumberField(left, right, "id"));
            result.Channels.Sort((left, right) => CompareNumberField(left, right, "id"));
            result.Messages.Sort((left, right) => CompareNumberField(left, right, "log_time"));
            return result;
        }

        private static string FindField(SerializableMcapRecord record, string fieldName)
        {
            for (var i = 0; i < record.Fields.Count; i++)
            {
                var field = record.Fields[i];
                if ((string)field[0] == fieldName)
                    return (string)field[1];
            }

            return "0";
        }

        private static int CompareNumberField(SerializableMcapRecord left, SerializableMcapRecord right, string fieldName)
        {
            var leftValue = ulong.Parse(FindField(left, fieldName), System.Globalization.CultureInfo.InvariantCulture);
            var rightValue = ulong.Parse(FindField(right, fieldName), System.Globalization.CultureInfo.InvariantCulture);
            return leftValue.CompareTo(rightValue);
        }

        private sealed class Scanner
        {
            private readonly byte[] _data;
            private int _offset;

            public Scanner(byte[] data)
            {
                _data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public List<SerializableMcapRecord> ReadStreamed()
            {
                ValidateMagic(0, "leading");
                ValidateMagic(_data.Length - McapWriter.MagicLength, "trailing");

                _offset = McapWriter.MagicLength;
                var records = new List<SerializableMcapRecord>();
                var end = _data.Length - McapWriter.MagicLength;
                while (_offset < end)
                {
                    var record = ReadRecord(_data, ref _offset, end);
                    AddRecord(records, record.Opcode, record.Content);
                }

                return records;
            }

            private void AddRecord(List<SerializableMcapRecord> records, byte opcode, byte[] content)
            {
                switch (opcode)
                {
                    case McapWriter.OpcodeHeader:
                        records.Add(ToRecord(McapReader.DecodeHeader(content)));
                        break;
                    case McapWriter.OpcodeFooter:
                        records.Add(ToRecord(McapReader.DecodeFooter(content)));
                        break;
                    case McapWriter.OpcodeSchema:
                        records.Add(ToRecord(McapReader.DecodeSchema(content)));
                        break;
                    case McapWriter.OpcodeChannel:
                        records.Add(ToRecord(McapReader.DecodeChannel(content)));
                        break;
                    case McapWriter.OpcodeMessage:
                        records.Add(ToRecord(McapReader.DecodeMessage(content, 0, content.Length)));
                        break;
                    case McapWriter.OpcodeChunk:
                        AddChunkRecords(records, content);
                        break;
                    case McapWriter.OpcodeMessageIndex:
                        break;
                    case McapWriter.OpcodeChunkIndex:
                        records.Add(ToRecord(McapReader.DecodeChunkIndex(content)));
                        break;
                    case McapWriter.OpcodeAttachment:
                        records.Add(ToRecord(McapReader.DecodeAttachment(content)));
                        break;
                    case McapWriter.OpcodeAttachmentIndex:
                        records.Add(ToRecord(McapReader.DecodeAttachmentIndex(content)));
                        break;
                    case McapWriter.OpcodeStatistics:
                        records.Add(ToRecord(McapReader.DecodeStatistics(content)));
                        break;
                    case McapWriter.OpcodeMetadata:
                        records.Add(ToRecord(McapReader.DecodeMetadata(content)));
                        break;
                    case McapWriter.OpcodeMetadataIndex:
                        records.Add(ToRecord(McapReader.DecodeMetadataIndex(content)));
                        break;
                    case McapWriter.OpcodeSummaryOffset:
                        records.Add(ToRecord(DecodeSummaryOffset(content)));
                        break;
                    case McapWriter.OpcodeDataEnd:
                        records.Add(ToDataEndRecord(content));
                        break;
                    default:
                        break;
                }
            }

            private void AddChunkRecords(List<SerializableMcapRecord> records, byte[] content)
            {
                var off = 0;
                McapBinaryReader.ReadU64LE(content, ref off);
                McapBinaryReader.ReadU64LE(content, ref off);
                var uncompressedSize = McapBinaryReader.ReadU64LE(content, ref off);
                var uncompressedCrc = McapBinaryReader.ReadU32LE(content, ref off);
                var compression = McapBinaryReader.ReadString(content, ref off);
                var compressedSize = McapBinaryReader.ReadU64LE(content, ref off);
                if (uncompressedSize > int.MaxValue || compressedSize > int.MaxValue)
                    throw new InvalidDataException("MCAP conformance chunk size exceeds supported in-memory limit.");
                if ((int)compressedSize > content.Length - off)
                    throw new InvalidDataException("MCAP conformance chunk content is truncated.");

                var compressed = new byte[(int)compressedSize];
                if (compressed.Length > 0)
                    Buffer.BlockCopy(content, off, compressed, 0, compressed.Length);
                var uncompressed = McapCompression.Decompress(compression, compressed, (int)uncompressedSize);
                if (uncompressedCrc != 0 && Crc32Helper.Compute(uncompressed) != uncompressedCrc)
                    throw new InvalidDataException("MCAP chunk CRC mismatch.");

                var innerOffset = 0;
                while (innerOffset < uncompressed.Length)
                {
                    var record = ReadRecord(uncompressed, ref innerOffset, uncompressed.Length);
                    AddRecord(records, record.Opcode, record.Content);
                }
            }

            private void ValidateMagic(int offset, string name)
            {
                if (offset < 0 || offset + McapWriter.MagicLength > _data.Length)
                    throw new InvalidDataException("MCAP " + name + " magic is outside file bounds.");

                var magic = McapWriter.Magic;
                for (var i = 0; i < magic.Length; i++)
                {
                    if (_data[offset + i] != magic[i])
                        throw new InvalidDataException("MCAP " + name + " magic mismatch.");
                }
            }
        }

        private static RawRecord ReadRecord(byte[] data, ref int offset, int end)
        {
            if (offset + McapWriter.RecordHeaderLength > end)
                throw new InvalidDataException("MCAP record header is truncated.");

            var opcode = data[offset++];
            if (opcode == 0)
                throw new InvalidDataException("MCAP opcode 0x00 is invalid.");

            var len = McapBinaryReader.ReadU64LE(data, ref offset);
            if (len > int.MaxValue)
                throw new InvalidDataException("MCAP record content length exceeds int.MaxValue.");
            if ((int)len > end - offset)
                throw new InvalidDataException("MCAP record content is truncated.");

            var content = new byte[(int)len];
            if (content.Length > 0)
                Buffer.BlockCopy(data, offset, content, 0, content.Length);
            offset += content.Length;
            return new RawRecord(opcode, content);
        }

        private static SerializableMcapRecord ToRecord(McapHeader header)
            => McapConformanceJson.Record(
                "Header",
                McapConformanceJson.Field("library", header.Library ?? string.Empty),
                McapConformanceJson.Field("profile", header.Profile ?? string.Empty));

        private static SerializableMcapRecord ToRecord(McapFooter footer)
            => McapConformanceJson.Record(
                "Footer",
                McapConformanceJson.Field("summary_crc", McapConformanceJson.Number(footer.SummaryCrc)),
                McapConformanceJson.Field("summary_offset_start", McapConformanceJson.Number(footer.SummaryOffsetStart)),
                McapConformanceJson.Field("summary_start", McapConformanceJson.Number(footer.SummaryStart)));

        private static SerializableMcapRecord ToRecord(McapSchema schema)
            => McapConformanceJson.Record(
                "Schema",
                McapConformanceJson.Field("data", McapConformanceJson.ByteArray(schema.Data)),
                McapConformanceJson.Field("encoding", schema.Encoding ?? string.Empty),
                McapConformanceJson.Field("id", McapConformanceJson.Number(schema.Id)),
                McapConformanceJson.Field("name", schema.Name ?? string.Empty));

        private static SerializableMcapRecord ToRecord(McapChannel channel)
            => McapConformanceJson.Record(
                "Channel",
                McapConformanceJson.Field("id", McapConformanceJson.Number(channel.Id)),
                McapConformanceJson.Field("message_encoding", channel.MessageEncoding ?? string.Empty),
                McapConformanceJson.Field("metadata", McapConformanceJson.StringMap(channel.Metadata)),
                McapConformanceJson.Field("schema_id", McapConformanceJson.Number(channel.SchemaId)),
                McapConformanceJson.Field("topic", channel.Topic ?? string.Empty));

        private static SerializableMcapRecord ToRecord(McapMessage message)
            => McapConformanceJson.Record(
                "Message",
                McapConformanceJson.Field("channel_id", McapConformanceJson.Number(message.ChannelId)),
                McapConformanceJson.Field("data", McapConformanceJson.ByteArray(message.Data)),
                McapConformanceJson.Field("log_time", McapConformanceJson.Number(message.LogTime)),
                McapConformanceJson.Field("publish_time", McapConformanceJson.Number(message.PublishTime)),
                McapConformanceJson.Field("sequence", McapConformanceJson.Number(message.Sequence)));

        private static SerializableMcapRecord ToRecord(McapAttachment attachment)
            => McapConformanceJson.Record(
                "Attachment",
                McapConformanceJson.Field("create_time", McapConformanceJson.Number(attachment.CreateTime)),
                McapConformanceJson.Field("data", McapConformanceJson.ByteArray(attachment.Data)),
                McapConformanceJson.Field("log_time", McapConformanceJson.Number(attachment.LogTime)),
                McapConformanceJson.Field("media_type", attachment.MediaType ?? string.Empty),
                McapConformanceJson.Field("name", attachment.Name ?? string.Empty));

        private static SerializableMcapRecord ToRecord(McapAttachmentIndex index)
            => McapConformanceJson.Record(
                "AttachmentIndex",
                McapConformanceJson.Field("create_time", McapConformanceJson.Number(index.CreateTime)),
                McapConformanceJson.Field("data_size", McapConformanceJson.Number(index.DataSize)),
                McapConformanceJson.Field("length", McapConformanceJson.Number(index.Length)),
                McapConformanceJson.Field("log_time", McapConformanceJson.Number(index.LogTime)),
                McapConformanceJson.Field("media_type", index.MediaType ?? string.Empty),
                McapConformanceJson.Field("name", index.Name ?? string.Empty),
                McapConformanceJson.Field("offset", McapConformanceJson.Number(index.Offset)));

        private static SerializableMcapRecord ToRecord(McapMetadata metadata)
            => McapConformanceJson.Record(
                "Metadata",
                McapConformanceJson.Field("metadata", McapConformanceJson.StringMap(metadata.Metadata)),
                McapConformanceJson.Field("name", metadata.Name ?? string.Empty));

        private static SerializableMcapRecord ToRecord(McapMetadataIndex index)
            => McapConformanceJson.Record(
                "MetadataIndex",
                McapConformanceJson.Field("length", McapConformanceJson.Number(index.Length)),
                McapConformanceJson.Field("name", index.Name ?? string.Empty),
                McapConformanceJson.Field("offset", McapConformanceJson.Number(index.Offset)));

        private static SerializableMcapRecord ToRecord(McapStatistics statistics)
            => McapConformanceJson.Record(
                "Statistics",
                McapConformanceJson.Field("attachment_count", McapConformanceJson.Number(statistics.AttachmentCount)),
                McapConformanceJson.Field("channel_count", McapConformanceJson.Number(statistics.ChannelCount)),
                McapConformanceJson.Field("channel_message_counts", McapConformanceJson.UshortUlongMap(statistics.ChannelMessageCounts)),
                McapConformanceJson.Field("chunk_count", McapConformanceJson.Number(statistics.ChunkCount)),
                McapConformanceJson.Field("message_count", McapConformanceJson.Number(statistics.MessageCount)),
                McapConformanceJson.Field("message_end_time", McapConformanceJson.Number(statistics.MessageEndTime)),
                McapConformanceJson.Field("message_start_time", McapConformanceJson.Number(statistics.MessageStartTime)),
                McapConformanceJson.Field("metadata_count", McapConformanceJson.Number(statistics.MetadataCount)),
                McapConformanceJson.Field("schema_count", McapConformanceJson.Number(statistics.SchemaCount)));

        private static SerializableMcapRecord ToRecord(McapChunkIndex index)
            => McapConformanceJson.Record(
                "ChunkIndex",
                McapConformanceJson.Field("chunk_length", McapConformanceJson.Number(index.ChunkLength)),
                McapConformanceJson.Field("chunk_start_offset", McapConformanceJson.Number(index.ChunkStartOffset)),
                McapConformanceJson.Field("compressed_size", McapConformanceJson.Number(index.CompressedSize)),
                McapConformanceJson.Field("compression", index.Compression ?? string.Empty),
                McapConformanceJson.Field("message_end_time", McapConformanceJson.Number(index.MessageEndTime)),
                McapConformanceJson.Field("message_index_length", McapConformanceJson.Number(index.MessageIndexLength)),
                McapConformanceJson.Field("message_index_offsets", McapConformanceJson.UshortUlongMap(index.MessageIndexOffsets)),
                McapConformanceJson.Field("message_start_time", McapConformanceJson.Number(index.MessageStartTime)),
                McapConformanceJson.Field("uncompressed_size", McapConformanceJson.Number(index.UncompressedSize)));

        private static SerializableMcapRecord ToRecord(SummaryOffsetRecord record)
            => McapConformanceJson.Record(
                "SummaryOffset",
                McapConformanceJson.Field("group_length", McapConformanceJson.Number(record.GroupLength)),
                McapConformanceJson.Field("group_opcode", McapConformanceJson.Number(record.GroupOpcode)),
                McapConformanceJson.Field("group_start", McapConformanceJson.Number(record.GroupStart)));

        private static SerializableMcapRecord ToDataEndRecord(byte[] content)
        {
            var off = 0;
            var crc = McapBinaryReader.ReadU32LE(content, ref off);
            if (off != content.Length)
                throw new InvalidDataException("MCAP DataEnd record has trailing bytes.");

            return McapConformanceJson.Record(
                "DataEnd",
                McapConformanceJson.Field("data_section_crc", McapConformanceJson.Number(crc)));
        }

        private static SummaryOffsetRecord DecodeSummaryOffset(byte[] content)
        {
            var off = 0;
            var groupOpcode = content[off++];
            var groupStart = McapBinaryReader.ReadU64LE(content, ref off);
            var groupLength = McapBinaryReader.ReadU64LE(content, ref off);
            if (off != content.Length)
                throw new InvalidDataException("MCAP SummaryOffset record has trailing bytes.");
            return new SummaryOffsetRecord(groupOpcode, groupStart, groupLength);
        }

        private readonly struct RawRecord
        {
            public readonly byte Opcode;
            public readonly byte[] Content;

            public RawRecord(byte opcode, byte[] content)
            {
                Opcode = opcode;
                Content = content;
            }
        }

        private readonly struct SummaryOffsetRecord
        {
            public readonly byte GroupOpcode;
            public readonly ulong GroupStart;
            public readonly ulong GroupLength;

            public SummaryOffsetRecord(byte groupOpcode, ulong groupStart, ulong groupLength)
            {
                GroupOpcode = groupOpcode;
                GroupStart = groupStart;
                GroupLength = groupLength;
            }
        }

        private sealed class NonSeekableReadStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableReadStream(Stream inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
