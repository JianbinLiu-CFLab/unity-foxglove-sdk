// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP specification conformance regressions for writer policy,
//          durable chunk recovery, record extensions, and reader structure.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Domain", "Mcap")]
    [Trait("Evidence", "Conformance")]
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
        public void StrictWriterOptionsRejectConfigurationsThatRequireNormalization()
        {
            var options = new McapWriterOptions
            {
                UseChunking = true,
                IndexTypes = McapIndexTypes.Chunk,
                RepeatSchemas = false,
                RepeatChannels = false,
                UseStatistics = false
            };

            var error = Assert.Throws<InvalidOperationException>(() => options.ValidateStrict());

            Assert.Contains("RepeatSchemas", error.Message, StringComparison.Ordinal);
            Assert.Contains("RepeatChannels", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictWriterOptionsAcceptCanonicalConfiguration()
        {
            var options = McapWriterOptions.Normalize(new McapWriterOptions());

            options.ValidateStrict();
        }

        [Fact]
        public void StrictValidatorAcceptsCanonicalFile()
        {
            using var stream = CreateMcap(writer =>
            {
                writer.WriteSchema(1, "mcap.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/mcap/topic", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 0, 10, 10, Encoding.UTF8.GetBytes("{}"));
            });

            var summary = McapStrictValidator.Validate(stream);

            Assert.Single(summary.Schemas);
            Assert.Single(summary.Channels);
        }

        [Fact]
        public void StrictValidatorRejectsCurrentVersionTrailingFieldsThatTolerantReaderAccepts()
        {
            var schemaContent = RecordContent(writer =>
                writer.WriteSchema(1, "mcap.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}")));
            using var stream = CreateMcap(writer =>
                writer.WriteRecord(McapWriter.OpcodeSchema, AppendExtension(schemaContent)));

            using (var tolerant = new McapStreamingReader(stream, leaveOpen: true))
                Assert.Single(tolerant.Read().Summary.Schemas);

            stream.Position = 0;
            Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(stream));
        }

        [Fact]
        public void StrictValidatorRejectsReservedOpcode()
        {
            using var stream = CreateMcap(writer => WriteUncheckedRecord(writer, 0x10, Array.Empty<byte>()));

            Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(stream));
        }

        [Fact]
        public void StrictValidatorRejectsSchemaIdZero()
        {
            using var stream = CreateMcap(writer =>
                writer.WriteSchema(0, "mcap.Invalid", "jsonschema", Encoding.UTF8.GetBytes("{}")));

            Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(stream));
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
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
        [Trait("Evidence", "FaultInjection")]
        public void FirstChunkFailureRecoversCanonicalEmptyRecording()
        {
            var clean = CreateFaultMatrixFixture();
            var target = FindRecordOffsets(clean, McapWriter.OpcodeChunk).First() + 3;
            using var stream = RecordFaultMatrixFixture(target, out var writeError);

            Assert.IsType<IOException>(writeError);
            var summary = McapStrictValidator.Validate(stream);
            Assert.Null(summary.Statistics);
            Assert.Empty(summary.ChunkIndexes);
            Assert.Empty(ReadMessages(stream));
        }

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

        private static void WriteUncheckedRecord(McapWriter writer, byte opcode, byte[] content)
        {
            var streamField = typeof(McapWriter).GetField("_stream", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var stream = (Stream)streamField?.GetValue(writer)
                ?? throw new InvalidOperationException("Could not access the test writer stream.");
            stream.WriteByte(opcode);
            McapWriter.WriteU64(stream, (ulong)(content?.Length ?? 0));
            if (content != null && content.Length > 0)
                stream.Write(content, 0, content.Length);
        }

        private static byte[] SummaryBytes(Action<McapWriter> writeSummary)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
                writeSummary(writer);
            return stream.ToArray();
        }

        private static byte[] CreateFaultMatrixFixture()
        {
            using var stream = new MemoryStream();
            WriteFaultMatrixFixture(stream, out _);
            return stream.ToArray();
        }

        private static AbsoluteFaultStream RecordFaultMatrixFixture(long target, out Exception writeError)
        {
            var stream = new AbsoluteFaultStream(target);
            WriteFaultMatrixFixture(stream, out writeError);
            return stream;
        }

        private static AbsoluteFaultStream RecordCloseFaultMatrixFixture(
            long target,
            bool throwOnFlush,
            out Exception closeError)
        {
            var stream = new AbsoluteFaultStream(target) { ThrowOnNextFlush = throwOnFlush };
            var recorder = CreateFaultMatrixRecorder(stream);
            recorder.WriteMessage(1, 10, new byte[64]);
            recorder.WriteMessage(1, 20, new byte[64]);
            recorder.WriteMessage(1, 30, new byte[64]);
            closeError = RecordException(recorder.Close);
            recorder.Dispose();
            stream.Position = 0;
            return stream;
        }

        private static void WriteFaultMatrixFixture(Stream stream, out Exception writeError)
        {
            var recorder = CreateFaultMatrixRecorder(stream);
            writeError = null;
            try
            {
                recorder.WriteMessage(1, 10, new byte[64]);
                recorder.WriteMessage(1, 20, new byte[64]);
                recorder.WriteMessage(1, 30, new byte[64]);
            }
            catch (Exception ex)
            {
                writeError = ex;
            }

            recorder.Close();
            recorder.Dispose();
            stream.Position = 0;
        }

        private static McapRecorder CreateFaultMatrixRecorder(Stream stream)
        {
            var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions
                {
                    ChunkSizeBytes = 32,
                    Compression = "",
                    UseChunking = true,
                    IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message,
                    RepeatSchemas = true,
                    RepeatChannels = true,
                    UseStatistics = true,
                    UseSummaryOffsets = true,
                    EnableCrcs = true
                },
                leaveOpen: true);
            recorder.AddChannel(1, "/mcap/fault-matrix", "json", "mcap.FaultMatrix", "jsonschema", "{}");
            return recorder;
        }

        private static List<McapMessage> ReadMessages(Stream stream)
        {
            stream.Position = 0;
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);
            return reader.ReadMessages();
        }

        private static List<long> FindRecordOffsets(byte[] bytes, byte opcode)
        {
            var result = new List<long>();
            var off = McapWriter.MagicLength;
            var bodyEnd = bytes.Length - McapWriter.MagicLength;
            while (off < bodyEnd)
            {
                var recordStart = off;
                var recordOpcode = bytes[off++];
                var length = McapBinaryReader.ReadU64LE(bytes, ref off);
                if (recordOpcode == opcode)
                    result.Add(recordStart);
                off = checked(off + (int)length);
            }
            return result;
        }

        private static long FindFirstSummaryRecordOffset(byte[] bytes)
        {
            var dataEnd = FindRecordOffsets(bytes, McapWriter.OpcodeDataEnd).Single();
            var off = checked((int)dataEnd + McapWriter.RecordHeaderLength + McapWriter.Crc32SizeBytes);
            if (bytes[off] == McapWriter.OpcodeFooter)
                throw new InvalidOperationException("Fault fixture unexpectedly has no summary records.");
            return off;
        }

        private static Exception RecordException(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private sealed class AbsoluteFaultStream : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream();
            private long _faultPosition;

            public AbsoluteFaultStream(long faultPosition)
            {
                _faultPosition = faultPosition;
            }

            public bool ThrowOnNextFlush { get; set; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }

            public override void Flush()
            {
                if (ThrowOnNextFlush)
                {
                    ThrowOnNextFlush = false;
                    throw new IOException("Injected MCAP flush failure.");
                }
                _inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (_faultPosition >= Position && _faultPosition < Position + count)
                {
                    var writable = checked((int)(_faultPosition - Position));
                    if (writable > 0)
                        _inner.Write(buffer, offset, writable);
                    _faultPosition = -1;
                    throw new IOException("Injected MCAP write failure.");
                }
                _inner.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (_faultPosition == Position)
                {
                    _faultPosition = -1;
                    throw new IOException("Injected MCAP write failure.");
                }
                _inner.WriteByte(value);
            }
        }

        private sealed class RestoreExercisingFileOperations : IMcapAmendmentFileOperations
        {
            private readonly string _destination;
            private bool _failedTempPromotion;

            public RestoreExercisingFileOperations(string destination)
            {
                _destination = Path.GetFullPath(destination);
            }

            public bool RestoreAttempted { get; private set; }

            public void Replace(string source, string destination, string backup)
                => throw new PlatformNotSupportedException("Exercise portable replacement fallback.");

            public void Move(string source, string destination)
            {
                var fullSource = Path.GetFullPath(source);
                var fullDestination = Path.GetFullPath(destination);
                if (!_failedTempPromotion && fullDestination == _destination && fullSource.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    _failedTempPromotion = true;
                    throw new IOException("Injected temp promotion failure.");
                }
                if (_failedTempPromotion && fullDestination == _destination && fullSource.EndsWith(".bak", StringComparison.Ordinal))
                    RestoreAttempted = true;
                File.Move(source, destination);
            }

            public bool Exists(string path) => File.Exists(path);
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
