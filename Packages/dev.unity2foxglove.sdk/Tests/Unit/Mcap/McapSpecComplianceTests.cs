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
    [Trait("Domain", "Mcap")]
    [Trait("Evidence", "Conformance")]
    public partial class McapSpecComplianceTests
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
        public void PrimitiveStreamWritersBatchEachValueIntoOneWrite()
        {
            using var stream = new WriteCallTrackingStream();

            McapWriter.WriteU16(stream, 0x1234);
            McapWriter.WriteU32(stream, 0x89ABCDEF);
            McapWriter.WriteU64(stream, 0x0123456789ABCDEF);

            Assert.Equal(0, stream.ByteWriteCalls);
            Assert.Equal(3, stream.BulkWriteCalls);
            Assert.Equal(
                new byte[]
                {
                    0x34, 0x12,
                    0xEF, 0xCD, 0xAB, 0x89,
                    0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01
                },
                stream.ToArray());
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
        public void StrictValidatorAcceptsChunkIndexWithoutMessageIndexes()
        {
            using var stream = new MemoryStream();
            using (var recorder = new McapRecorder(
                       stream,
                       null,
                       new McapWriterOptions
                       {
                           UseChunking = true,
                           IndexTypes = McapIndexTypes.Chunk,
                           UseStatistics = true,
                           ChunkSizeBytes = 1024
                       },
                       leaveOpen: true))
            {
                recorder.AddChannel(1, "/mcap/coarse", "json", "mcap.Schema", "jsonschema", "{}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{}"));
                recorder.Close();
            }

            stream.Position = 0;
            var summary = McapStrictValidator.Validate(stream);

            Assert.Single(summary.ChunkIndexes);
            Assert.Empty(summary.ChunkIndexes[0].MessageIndexOffsets);
        }

        [Fact]
        public void StrictValidatorRejectsFilesBeyondConfiguredChunkStateBudget()
        {
            using var stream = new MemoryStream(CreateTwoChunkIndexedMcap(), writable: false);

            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(
                stream,
                new McapStrictValidationOptions
                {
                    ValidateCrcs = false,
                    MaxValidatedChunks = 1
                }));

            Assert.Contains("validated chunk state budget", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictValidatorRejectsChunkIndexMessageOffsetWithoutMessageIndexRecord()
        {
            var bytes = CreateTwoChunkIndexedMcap();
            var chunkIndexOffset = FindRecordOffsets(bytes, McapWriter.OpcodeChunkIndex)[0];
            var messageIndexValueOffset = checked((int)chunkIndexOffset + McapWriter.RecordHeaderLength + 38);
            WriteU64LittleEndian(bytes, messageIndexValueOffset, 0xDEADBEEFUL);
            using var stream = new MemoryStream(bytes, writable: false);

            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(
                stream,
                new McapStrictValidationOptions { ValidateCrcs = false }));

            Assert.Contains("Message Index", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictValidatorRejectsChunkIndexMessageOffsetFromAnotherChunk()
        {
            var bytes = CreateTwoChunkIndexedMcap();
            var chunkIndexOffsets = FindRecordOffsets(bytes, McapWriter.OpcodeChunkIndex);
            var messageIndexOffsets = FindRecordOffsets(bytes, McapWriter.OpcodeMessageIndex);
            Assert.Equal(2, chunkIndexOffsets.Count);
            Assert.Equal(2, messageIndexOffsets.Count);

            var firstMapValueOffset = checked((int)chunkIndexOffsets[0] + McapWriter.RecordHeaderLength + 38);
            WriteU64LittleEndian(bytes, firstMapValueOffset, (ulong)messageIndexOffsets[1]);
            using var stream = new MemoryStream(bytes, writable: false);

            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(
                stream,
                new McapStrictValidationOptions { ValidateCrcs = false }));

            Assert.Contains("does not match its data Message Index", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictValidatorRejectsNonMultipleMessageIndexLengthWhenTrailingFieldsAllowed()
        {
            var bytes = CreateTwoChunkIndexedMcap();
            var messageIndexOffset = FindRecordOffsets(bytes, McapWriter.OpcodeMessageIndex)[0];
            var recordsLengthOffset = checked((int)messageIndexOffset + McapWriter.RecordHeaderLength + sizeof(ushort));
            WriteU32LittleEndian(bytes, recordsLengthOffset, 15);
            using var stream = new MemoryStream(bytes, writable: false);

            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(
                stream,
                new McapStrictValidationOptions
                {
                    ValidateCrcs = false,
                    RequireCurrentVersionRecordLengths = false
                }));

            Assert.Contains("divisible by 16", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictValidatorReportsOversizedMessageIndexLengthAsInvalidData()
        {
            var bytes = CreateTwoChunkIndexedMcap();
            var messageIndexOffset = FindRecordOffsets(bytes, McapWriter.OpcodeMessageIndex)[0];
            var recordsLengthOffset = checked((int)messageIndexOffset + McapWriter.RecordHeaderLength + sizeof(ushort));
            WriteU32LittleEndian(bytes, recordsLengthOffset, 0xFFFFFFF0);
            using var stream = new MemoryStream(bytes, writable: false);

            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(
                stream,
                new McapStrictValidationOptions
                {
                    ValidateCrcs = false,
                    RequireCurrentVersionRecordLengths = false
                }));

            Assert.Contains("exceeds remaining payload length", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictValidatorRejectsChunkAndIndexTimeBoundsThatDisagreeWithMessages()
        {
            var bytes = CreateTwoChunkIndexedMcap();
            var chunkOffset = FindRecordOffsets(bytes, McapWriter.OpcodeChunk)[0];
            var chunkIndexOffset = FindRecordOffsets(bytes, McapWriter.OpcodeChunkIndex)[0];

            // Keep the Chunk header and its summary Chunk Index mutually
            // consistent, but make both disagree with the message timestamps
            // decoded from the chunk payload.
            WriteU64LittleEndian(bytes,
                checked((int)chunkOffset + McapWriter.RecordHeaderLength), 999);
            WriteU64LittleEndian(bytes,
                checked((int)chunkOffset + McapWriter.RecordHeaderLength + sizeof(ulong)), 1000);
            WriteU64LittleEndian(bytes,
                checked((int)chunkIndexOffset + McapWriter.RecordHeaderLength), 999);
            WriteU64LittleEndian(bytes,
                checked((int)chunkIndexOffset + McapWriter.RecordHeaderLength + sizeof(ulong)), 1000);

            using var stream = new MemoryStream(bytes, writable: false);
            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(
                stream,
                new McapStrictValidationOptions { ValidateCrcs = false }));

            Assert.Contains("message time bounds", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictValidatorRejectsChunkWithoutMatchingChunkIndex()
        {
            var bytes = CreateTwoChunkIndexedMcap();
            var chunkIndexOffsets = FindRecordOffsets(bytes, McapWriter.OpcodeChunkIndex);
            Assert.Equal(2, chunkIndexOffsets.Count);

            var first = checked((int)chunkIndexOffsets[0]);
            var lengthOffset = checked(first + 1);
            var contentLength = checked((int)McapBinaryReader.ReadU64LE(bytes, ref lengthOffset));
            var recordLength = McapWriter.RecordHeaderLength + contentLength;
            var shortened = new byte[bytes.Length - recordLength];
            Buffer.BlockCopy(bytes, 0, shortened, 0, first);
            Buffer.BlockCopy(bytes, first + recordLength, shortened, first, bytes.Length - first - recordLength);

            using var stream = new MemoryStream(shortened, writable: false);
            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(
                stream,
                new McapStrictValidationOptions { ValidateCrcs = false }));

            Assert.Contains("exactly one Chunk Index for every data Chunk", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReadSummaryDoesNotDependOnCallerStreamPosition()
        {
            using var stream = CreateMcap(writer =>
                writer.WriteChannel(1, 0, "/mcap/topic", "json", new Dictionary<string, string>()));
            stream.Position = 3;

            var summary = new McapReader(stream).ReadSummary();

            Assert.Single(summary.Channels);
        }

        [Fact]
        public void ReplayWithoutChunkIndexesRemainsPausedAndWarns()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "u2f-mcap-unindexed-replay-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(
                           path,
                           FileMode.CreateNew,
                           FileAccess.ReadWrite,
                           FileShare.Read))
                using (var writer = new McapWriter(stream, leaveOpen: true))
                {
                    writer.WriteMagic();
                    writer.WriteHeader("", "unindexed-replay");
                    writer.WriteSchema(
                        1,
                        "mcap.Unindexed",
                        "jsonschema",
                        Encoding.UTF8.GetBytes("{}"));
                    writer.WriteChannel(
                        1,
                        1,
                        "/mcap/unindexed",
                        "json",
                        new Dictionary<string, string>());
                    writer.WriteMessage(1, 0, 10, 10, Encoding.UTF8.GetBytes("{}"));
                    writer.WriteDataEnd();
                    writer.WriteFooter(0, 0, 0);
                    writer.WriteMagic();
                }

                var logger = new RecordingLogger();
                using var engine = new McapReplayEngine(logger);
                engine.Load(path);
                Assert.False(engine.CanSeek);

                engine.Play();

                Assert.Equal(McapReplayEngine.Status.Paused, engine.CurrentStatus);
                Assert.Contains(
                    logger.Warnings,
                    warning => warning.Contains(
                        "Statistics and ChunkIndex",
                        StringComparison.Ordinal));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

    }
}
