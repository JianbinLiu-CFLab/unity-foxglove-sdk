// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP specification conformance regressions for writer policy,
//          durable chunk recovery, record extensions, and reader structure.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{


public partial class McapSpecComplianceTests
    {
        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void MiddleChunkFailurePreservesOnlyDurableEarlierChunks()
        {
            var clean = CreateFaultMatrixFixture();
            var target = FindRecordOffsets(clean, McapWriter.OpcodeChunk).Skip(1).First() + 3;
            using var stream = RecordFaultMatrixFixture(target, out var writeError);

            Assert.IsType<IOException>(writeError);
            var summary = McapStrictValidator.Validate(stream);
            Assert.Null(summary.Statistics);
            Assert.Single(summary.ChunkIndexes);
            Assert.Equal(new[] { 10UL }, ReadMessages(stream).Select(message => message.LogTime));
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void MessageIndexFailureDropsItsOwningChunkAndPreservesEarlierChunks()
        {
            var clean = CreateFaultMatrixFixture();
            var target = FindRecordOffsets(clean, McapWriter.OpcodeMessageIndex).Skip(1).First() + 3;
            using var stream = RecordFaultMatrixFixture(target, out var writeError);

            Assert.IsType<IOException>(writeError);
            var summary = McapStrictValidator.Validate(stream);
            Assert.Null(summary.Statistics);
            Assert.Single(summary.ChunkIndexes);
            Assert.Equal(new[] { 10UL }, ReadMessages(stream).Select(message => message.LogTime));
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void SummaryFailureIsReportedAndNeverProducesAFalseValidFile()
        {
            var clean = CreateFaultMatrixFixture();
            var target = FindFirstSummaryRecordOffset(clean) + 3;
            using var stream = RecordCloseFaultMatrixFixture(target, throwOnFlush: false, out var closeError);

            Assert.IsType<IOException>(closeError);
            Assert.ThrowsAny<Exception>(() => McapStrictValidator.Validate(stream));
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void FooterFailureIsReportedAndNeverProducesAFalseValidFile()
        {
            var clean = CreateFaultMatrixFixture();
            var target = FindRecordOffsets(clean, McapWriter.OpcodeFooter).Single() + 3;
            using var stream = RecordCloseFaultMatrixFixture(target, throwOnFlush: false, out var closeError);

            Assert.IsType<IOException>(closeError);
            Assert.ThrowsAny<Exception>(() => McapStrictValidator.Validate(stream));
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void FlushFailureIsReportedAfterCompleteBytesAreWritten()
        {
            using var stream = RecordCloseFaultMatrixFixture(-1, throwOnFlush: true, out var closeError);

            Assert.IsType<IOException>(closeError);
            McapStrictValidator.Validate(stream);
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void AmendmentReplacementFailureRestoresOriginalFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "u2f-mcap-replace-" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, CreateFaultMatrixFixture());
            var original = File.ReadAllBytes(path);
            var operations = new RestoreExercisingFileOperations(path);
            try
            {
                using var amendment = new McapAmendmentWriter(path, enableCrcs: true, operations);
                amendment.AddMetadata("fault", new Dictionary<string, string> { ["stage"] = "replace" });

                var error = Assert.Throws<IOException>(() => amendment.Close());

                Assert.Contains("restored", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.True(operations.RestoreAttempted);
                Assert.Equal(original, File.ReadAllBytes(path));
                using var restored = File.OpenRead(path);
                McapStrictValidator.Validate(restored);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                foreach (var backup in Directory.EnumerateFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".*.bak"))
                    File.Delete(backup);
            }
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

        private static MemoryStream CreateMcapWithSummary(Action<McapWriter> writeSummary)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "mcap-spec-test");
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writeSummary(writer);
                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] CreateTwoChunkIndexedMcap()
        {
            using var stream = new MemoryStream();
            using (var recorder = new McapRecorder(
                       stream,
                       null,
                       new McapWriterOptions
                       {
                           UseChunking = true,
                           IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message,
                           UseStatistics = true,
                           UseSummaryOffsets = false,
                           EnableCrcs = false,
                           ChunkSizeBytes = 32
                       },
                       leaveOpen: true))
            {
                recorder.AddChannel(1, "/mcap/indexed", "json", "mcap.Schema", "jsonschema", "{}");
                recorder.WriteMessage(1, 10, new byte[] { 1 });
                recorder.WriteMessage(1, 20, new byte[] { 2 });
                recorder.Close();
            }

            return stream.ToArray();
        }

    }
}
