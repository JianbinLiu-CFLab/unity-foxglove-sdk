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
        public void StrictValidatorAcceptsPrivateRecordInSummarySection()
        {
            using var stream = CreateMcapWithSummary(writer =>
                WriteUncheckedRecord(writer, 0x80, Array.Empty<byte>()));

            McapStrictValidator.Validate(stream);
        }

        [Fact]
        public void StrictValidatorAcceptsPrivateRecordInDataSection()
        {
            using var stream = CreateMcap(writer =>
                writer.WritePrivateRecord(0x80, new byte[] { 0x12, 0x34 }));

            McapStrictValidator.Validate(stream);
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
        public void ChunkPreflushFailureDoesNotAdvanceRejectedMessageCounters()
        {
            using var stream = new FailOnceWriteStream();
            using var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions { ChunkSizeBytes = 256 },
                leaveOpen: true);
            recorder.AddChannel(
                1,
                "/mcap/preflush-fault",
                "json",
                "mcap.PreflushFault",
                "jsonschema",
                "{}");
            recorder.WriteMessage(1, 10, new byte[64]);
            stream.ThrowOnceAfterWrittenBytes(3);

            Assert.Throws<IOException>(() =>
                recorder.WriteMessage(1, 20, new byte[160]));

            var counters = ReadOnlyServerChannelCounters(recorder);
            Assert.Equal(1U, counters.Sequence);
            Assert.Equal(1UL, counters.MessageCount);
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void UnchunkedPartialMessageWriteRollsBackBeforeRetry()
        {
            using var stream = new FailOnceWriteStream();
            using var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions
                {
                    UseChunking = false,
                    RepeatSchemas = true,
                    RepeatChannels = true,
                    UseStatistics = true,
                    UseSummaryOffsets = true
                },
                leaveOpen: true);

            recorder.AddChannel(1, "/mcap/direct-fault", "json", "mcap.DirectFault", "jsonschema", "{}");
            stream.ThrowOnceAfterWrittenBytes(3);
            Assert.Throws<IOException>(() => recorder.WriteMessage(1, 10, new byte[] { 1, 2, 3, 4 }));

            recorder.WriteMessage(1, 20, new byte[] { 5, 6, 7, 8 });
            recorder.Close();

            stream.Position = 0;
            var summary = McapStrictValidator.Validate(stream);
            Assert.NotNull(summary.Statistics);
            Assert.Equal(1UL, summary.Statistics.MessageCount);
            Assert.Equal(1UL, summary.Statistics.ChannelMessageCounts[1]);

            var messages = ReadMessages(stream);
            var message = Assert.Single(messages);
            Assert.Equal(0U, message.Sequence);
            Assert.Equal(20UL, message.LogTime);
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void TopLevelPartialMetadataWriteRollsBackBeforeRecoverableClose()
        {
            using var stream = new FailOnceWriteStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);

            stream.ThrowOnceAfterWrittenBytes(3);
            var failure = Assert.Throws<IOException>(() =>
                recorder.WriteMetadata("phase187.partial", "{}"));
            Assert.Equal("Injected partial MCAP write failure.", failure.Message);

            recorder.Close();
            stream.Position = 0;
            var summary = McapStrictValidator.Validate(stream);
            Assert.Empty(summary.MetadataIndexes);
            Assert.True(summary.Statistics == null || summary.Statistics.MessageCount == 0);
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void TopLevelWriteFailureFlushesPreviouslyBufferedMessagesOnClose()
        {
            using var stream = new FailOnceWriteStream();
            using var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions
                {
                    UseChunking = true,
                    ChunkSizeBytes = 1024,
                    IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message,
                    UseStatistics = true,
                    UseSummaryOffsets = true,
                    EnableCrcs = true,
                    EnableDataCrcs = true
                },
                leaveOpen: true);

            recorder.AddChannel(1, "/mcap/top-level-failure", "json", "mcap.TopLevelFailure", "jsonschema", "{}");
            recorder.WriteMessage(1, 10, new byte[] { 1, 2, 3 });
            var bytesBeforeFailure = SnapshotBytes(stream);

            stream.ThrowOnceAfterWrittenBytes(3);
            Assert.Throws<IOException>(() => recorder.WriteMetadata("phase187.failed", "{}"));
            Assert.Equal(bytesBeforeFailure, SnapshotBytes(stream));

            recorder.Close();
            stream.Position = 0;
            var summary = McapStrictValidator.Validate(stream);
            Assert.NotNull(summary.Statistics);
            Assert.Equal(1UL, summary.Statistics.MessageCount);
            Assert.Equal(1UL, summary.Statistics.ChannelMessageCounts[1]);
            Assert.Single(summary.ChunkIndexes);

            var message = Assert.Single(ReadMessages(stream));
            Assert.Equal(10UL, message.LogTime);
            Assert.Equal(new byte[] { 1, 2, 3 }, message.Data);
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void TopLevelPartialSchemaWriteRollsBackBeforeRecoverableClose()
        {
            using var stream = new FailOnceWriteStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);

            stream.ThrowOnceAfterWrittenBytes(3);
            var failure = Assert.Throws<IOException>(() => recorder.AddChannel(
                1,
                "/phase187/partial-schema",
                "json",
                "phase187.PartialSchema",
                "jsonschema",
                "{}"));
            Assert.Equal("Injected partial MCAP write failure.", failure.Message);

            recorder.Close();
            stream.Position = 0;
            var summary = McapStrictValidator.Validate(stream);
            Assert.Empty(summary.Schemas);
            Assert.Empty(summary.Channels);
            Assert.True(summary.Statistics == null || summary.Statistics.MessageCount == 0);
        }

        [Fact]
        [Trait("Evidence", "FaultInjection")]
        public void TopLevelPartialChannelWriteRollsBackBeforeRecoverableClose()
        {
            using var stream = new FailOnceWriteStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);

            stream.ThrowOnceAfterWrittenBytes(3);
            var failure = Assert.Throws<IOException>(() => recorder.AddChannel(
                1,
                "/phase187/partial-channel",
                "json",
                "",
                "",
                ""));
            Assert.Equal("Injected partial MCAP write failure.", failure.Message);

            recorder.Close();
            stream.Position = 0;
            var summary = McapStrictValidator.Validate(stream);
            Assert.Empty(summary.Channels);
            Assert.True(summary.Statistics == null || summary.Statistics.MessageCount == 0);
        }

        [Fact]
        public void ChunkBoundaryWriterDoesNotExceedPairedReaderLimit()
        {
            const int chunkLimit = 64;
            const int messageRecordOverhead = McapWriter.RecordHeaderLength + 2 + 4 + 8 + 8;
            var exactPayloadLength = chunkLimit - messageRecordOverhead;

            Assert.True(McapRecorder.IsChunkedMessageRecordWithinLimit(
                exactPayloadLength,
                chunkLimit));
            Assert.False(McapRecorder.IsChunkedMessageRecordWithinLimit(
                exactPayloadLength + 1,
                chunkLimit));
            Assert.Equal(
                (ulong)McapWriterOptions.MaxChunkSizeBytes,
                McapReader.DefaultChunkUncompressedSizeLimit);
        }

        [Fact]
        public void ReplaySnapshotKeepsGreatestSameChannelMessageKey()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "u2f-mcap-snapshot-order-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                           stream,
                           null,
                           new McapWriterOptions
                           {
                               UseChunking = true,
                               ChunkSizeBytes = 1024 * 1024,
                               IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                           },
                           leaveOpen: true))
                {
                    recorder.AddChannel(1, "/mcap/snapshot", "json", "mcap.Snapshot", "jsonschema", "{}");
                    recorder.WriteMessage(1, 100, new byte[] { 100 });
                    recorder.WriteMessage(1, 50, new byte[] { 50 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var snapshot = engine.Snapshot(100, new List<McapMessage>());

                var message = Assert.Single(snapshot);
                Assert.Equal(100UL, message.LogTime);
                Assert.Equal(new byte[] { 100 }, message.Data);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
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

    }
}
