// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP specification conformance regressions for writer policy,
//          durable chunk recovery, record extensions, and reader structure.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Domain", "Mcap")]
    public class McapSpecComplianceTests
    {
        [Fact]
        public void ChunkIndexesForceSchemaAndChannelSummaryCopies()
        {
            var options = McapWriterOptions.Normalize(new McapWriterOptions
            {
                UseChunking = true,
                IndexTypes = McapIndexTypes.Chunk,
                RepeatSchemas = false,
                RepeatChannels = false,
                UseStatistics = false
            });

            Assert.True(options.RepeatSchemas);
            Assert.True(options.RepeatChannels);
        }

        [Fact]
        public void StatisticsForceChannelSummaryCopies()
        {
            var options = McapWriterOptions.Normalize(new McapWriterOptions
            {
                IndexTypes = McapIndexTypes.None,
                RepeatChannels = false,
                UseStatistics = true
            });

            Assert.True(options.RepeatChannels);
        }

        [Fact]
        public void AutomaticChunkFlushFailureDropsPartialChunkAndStatistics()
        {
            using var stream = new FailOnceWriteStream();
            using var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions { ChunkSizeBytes = 64 },
                leaveOpen: true);

            recorder.AddChannel(1, "/mcap/fault", "json", "mcap.Fault", "jsonschema", "{}");
            recorder.WriteMessage(1, 10, new byte[64]);

            stream.ThrowOnceAfterWrittenBytes(3);
            Assert.Throws<IOException>(() => recorder.WriteMessage(1, 20, new byte[64]));
            recorder.Close();

            stream.Position = 0;
            var summary = new McapReader(stream).ReadSummary();
            Assert.Null(summary.Statistics);
            Assert.Single(summary.ChunkIndexes);

            stream.Position = 0;
            using var indexed = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);
            var messages = indexed.ReadMessages();
            Assert.Single(messages);
            Assert.Equal(10UL, messages[0].LogTime);
        }

        [Fact]
        public void SchemaDecoderIgnoresUnknownTrailingFields()
        {
            var content = RecordContent(writer =>
                writer.WriteSchema(1, "mcap.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}")));

            var schema = McapRecordDecoder.DecodeSchema(AppendExtension(content));

            Assert.Equal((ushort)1, schema.Id);
            Assert.Equal("mcap.Schema", schema.Name);
        }

        [Fact]
        public void ChannelDecoderIgnoresUnknownTrailingFields()
        {
            var content = RecordContent(writer =>
                writer.WriteChannel(1, 0, "/mcap/channel", "json", new Dictionary<string, string>()));

            var channel = McapRecordDecoder.DecodeChannel(AppendExtension(content));

            Assert.Equal((ushort)1, channel.Id);
            Assert.Equal("/mcap/channel", channel.Topic);
        }

        [Fact]
        public void MetadataDecoderIgnoresUnknownTrailingFields()
        {
            var content = RecordContent(writer =>
                writer.WriteMetadata("mcap", new Dictionary<string, string> { ["key"] = "value" }));

            var metadata = McapRecordDecoder.DecodeMetadata(AppendExtension(content));

            Assert.Equal("mcap", metadata.Name);
            Assert.Equal("value", metadata.Metadata["key"]);
        }

        [Fact]
        public void StreamingReaderAcceptsIdenticalDuplicateDefinitions()
        {
            using var stream = CreateMcap(writer =>
            {
                var schema = Encoding.UTF8.GetBytes("{}");
                writer.WriteSchema(1, "mcap.Schema", "jsonschema", schema);
                writer.WriteSchema(1, "mcap.Schema", "jsonschema", schema);
                writer.WriteChannel(1, 1, "/mcap/topic", "json", new Dictionary<string, string>());
                writer.WriteChannel(1, 1, "/mcap/topic", "json", new Dictionary<string, string>());
            });

            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();

            Assert.Single(result.Summary.Schemas);
            Assert.Single(result.Summary.Channels);
        }

        [Fact]
        public void StreamingReaderRejectsConflictingDuplicateSchemaIds()
        {
            using var stream = CreateMcap(writer =>
            {
                writer.WriteSchema(1, "mcap.First", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteSchema(1, "mcap.Second", "jsonschema", Encoding.UTF8.GetBytes("{}"));
            });

            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            Assert.Throws<InvalidDataException>(() => reader.Read());
        }

        [Fact]
        public void StreamingReaderRejectsConflictingDuplicateChannelIds()
        {
            using var stream = CreateMcap(writer =>
            {
                writer.WriteChannel(1, 0, "/mcap/first", "json", new Dictionary<string, string>());
                writer.WriteChannel(1, 0, "/mcap/second", "json", new Dictionary<string, string>());
            });

            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            Assert.Throws<InvalidDataException>(() => reader.Read());
        }

        [Fact]
        public void StreamingReaderIgnoresSchemaIdZero()
        {
            using var stream = CreateMcap(writer =>
                writer.WriteSchema(0, "mcap.Invalid", "jsonschema", Encoding.UTF8.GetBytes("{}")));

            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();

            Assert.Empty(result.Summary.Schemas);
        }

        [Fact]
        public void StreamingReaderRejectsAttachmentInsideChunk()
        {
            using var chunkRecords = new MemoryStream();
            using (var chunkWriter = new McapWriter(chunkRecords, leaveOpen: true))
            {
                chunkWriter.WriteAttachment(1, 1, "inside.txt", "text/plain", new byte[] { 1 }, enableCrc: false);
            }

            using var stream = CreateMcap(writer =>
            {
                var records = chunkRecords.ToArray();
                writer.WriteChunk(0, 0, (ulong)records.Length, 0, "", (ulong)records.Length, records);
            });

            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            Assert.Throws<InvalidDataException>(() => reader.Read());
        }

        [Fact]
        public void SummaryRejectsInterleavedOpcodeGroups()
        {
            var summaryBytes = SummaryBytes(writer =>
            {
                writer.WriteSchema(1, "mcap.First", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/mcap/topic", "json", new Dictionary<string, string>());
                writer.WriteSchema(2, "mcap.Second", "jsonschema", Encoding.UTF8.GetBytes("{}"));
            });

            Assert.Throws<InvalidDataException>(() => McapSummaryBuilder.FromSummarySection(
                summaryBytes, 100, 0, 0, McapReader.DefaultRecordSizeLimit));
        }

        [Fact]
        public void SummaryRejectsMultipleStatisticsRecords()
        {
            var summaryBytes = SummaryBytes(writer =>
            {
                writer.WriteStatistics(0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<ushort, ulong>());
                writer.WriteStatistics(0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<ushort, ulong>());
            });

            Assert.Throws<InvalidDataException>(() => McapSummaryBuilder.FromSummarySection(
                summaryBytes, 100, 0, 0, McapReader.DefaultRecordSizeLimit));
        }

        [Fact]
        public void SummaryRejectsMismatchedSummaryOffsetRecord()
        {
            const ulong summaryStart = 100;
            using var stream = new MemoryStream();
            using var writer = new McapWriter(stream, leaveOpen: true);
            writer.WriteSchema(1, "mcap.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
            var groupLength = (ulong)stream.Position;
            var summaryOffsetStart = summaryStart + groupLength;
            writer.WriteSummaryOffset(McapWriter.OpcodeSchema, summaryStart + 1, groupLength);
            writer.Flush();

            Assert.Throws<InvalidDataException>(() => McapSummaryBuilder.FromSummarySection(
                stream.ToArray(),
                summaryStart,
                summaryOffsetStart,
                0,
                McapReader.DefaultRecordSizeLimit));
        }

        private static byte[] RecordContent(Action<McapWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
                write(writer);

            var record = stream.ToArray();
            var content = new byte[record.Length - McapWriter.RecordHeaderLength];
            Buffer.BlockCopy(record, McapWriter.RecordHeaderLength, content, 0, content.Length);
            return content;
        }

        private static byte[] AppendExtension(byte[] content)
        {
            var extended = new byte[content.Length + 2];
            Buffer.BlockCopy(content, 0, extended, 0, content.Length);
            extended[content.Length] = 0xA5;
            extended[content.Length + 1] = 0x5A;
            return extended;
        }

        private static MemoryStream CreateMcap(Action<McapWriter> writeData)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "mcap-spec-test");
                writeData(writer);
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] SummaryBytes(Action<McapWriter> writeSummary)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
                writeSummary(writer);
            return stream.ToArray();
        }

        private sealed class FailOnceWriteStream : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream();
            private long _remainingBeforeThrow = -1;

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public void ThrowOnceAfterWrittenBytes(long byteCount)
            {
                _remainingBeforeThrow = byteCount;
            }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_remainingBeforeThrow >= 0 && count > _remainingBeforeThrow)
                {
                    var writable = (int)_remainingBeforeThrow;
                    if (writable > 0)
                        _inner.Write(buffer, offset, writable);
                    _remainingBeforeThrow = -1;
                    throw new IOException("Injected partial MCAP write failure.");
                }

                if (_remainingBeforeThrow >= 0)
                    _remainingBeforeThrow -= count;
                _inner.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (_remainingBeforeThrow == 0)
                {
                    _remainingBeforeThrow = -1;
                    throw new IOException("Injected partial MCAP write failure.");
                }

                if (_remainingBeforeThrow > 0)
                    _remainingBeforeThrow--;
                _inner.WriteByte(value);
            }
        }
    }
}
