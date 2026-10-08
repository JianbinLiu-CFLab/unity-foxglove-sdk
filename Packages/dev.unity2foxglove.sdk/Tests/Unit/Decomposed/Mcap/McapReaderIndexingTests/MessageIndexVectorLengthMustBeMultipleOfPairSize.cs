// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP reader / indexing edge cases (migrated from Phase134_9Validation).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// MCAP reader and indexing edge cases. Ported from Phase134_9Validation
    /// (checks 134-9A .. 134-9G).
    /// </summary>


public partial class McapReaderIndexingTests
    {
        [Fact]
        public void MessageIndexVectorLengthMustBeMultipleOfPairSize()
        {
            var content = new MemoryStream();
            WriteU16LE(content, 1);
            WriteU32LE(content, 17);
            content.Write(new byte[17], 0, 17);

            Assert.True(ThrowsInvalidData(() => McapRecordReader.DecodeMessageIndex(content.ToArray())),
                "134-9C-4: malformed message index vector length is rejected");
        }

        [Fact]
        public void MessageIndexRejectsDeclaredRecordsPastPayloadBeforeLooping()
        {
            var content = new MemoryStream();
            WriteU16LE(content, 1);
            WriteU32LE(content, 0xFFFFFFF0);

            var ex = Assert.Throws<InvalidDataException>(() => McapRecordReader.DecodeMessageIndex(content.ToArray()));

            Assert.Contains("exceeds remaining payload length", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictValidatorRejectsIncompletePerChunkMessageIndexes()
        {
            using var stream = CreateIncompleteMessageIndexMcap();

            var error = Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(stream));

            Assert.Contains("Message Index", error.Message, StringComparison.Ordinal);
            Assert.Contains("channel 2", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void IndexedChannelFilterInspectsChunkWhenMessageIndexMapIsIncomplete()
        {
            using var stream = CreateIncompleteMessageIndexMcap();
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);

            var messages = reader.ReadMessages(new McapReadOptions
            {
                ChannelIds = new List<ushort> { 2 },
                Order = McapReadOrder.FileOrder
            });

            var message = Assert.Single(messages);
            Assert.Equal((ushort)2, message.ChannelId);
            Assert.Equal(20UL, message.LogTime);
        }

        [Fact]
        public void TestRecordParserRejectsBytesAfterTrailingMagic()
        {
            using var stream = new MemoryStream();
            stream.Write(McapWriter.Magic, 0, McapWriter.Magic.Length);
            stream.Write(McapWriter.Magic, 0, McapWriter.Magic.Length);
            stream.WriteByte(0xFF);

            Assert.True(ThrowsInvalidData(() => McapRecordReader.Parse(stream.ToArray())),
                "173-078A: test MCAP parser rejects bytes after trailing magic");
        }

        [Fact]
        public void StatisticsVectorLengthMustBeMultipleOfPairSize()
        {
            var content = new MemoryStream();
            WriteU64LE(content, 1);
            WriteU16LE(content, 1);
            WriteU32LE(content, 1);
            WriteU32LE(content, 0);
            WriteU32LE(content, 0);
            WriteU32LE(content, 0);
            WriteU64LE(content, 1);
            WriteU64LE(content, 2);
            WriteU32LE(content, 11);
            Assert.True(ThrowsInvalidData(() => McapRecordDecoder.DecodeStatistics(content.ToArray())),
                "134-9C-3: malformed statistics channel-count vector length is rejected");
        }

        [Fact]
        public void StatisticsAllowsTrailingFields()
        {
            var content = new MemoryStream();
            WriteU64LE(content, 1);
            WriteU16LE(content, 1);
            WriteU32LE(content, 1);
            WriteU32LE(content, 0);
            WriteU32LE(content, 0);
            WriteU32LE(content, 0);
            WriteU64LE(content, 1);
            WriteU64LE(content, 2);
            WriteU32LE(content, 0);
            content.WriteByte(0xFF);

            var decoded = McapRecordDecoder.DecodeStatistics(content.ToArray());
            Assert.Equal(1UL, decoded.MessageCount);
            Assert.Equal(1U, decoded.ChannelCount);
        }

        [Fact]
        public void ReadChunkRecordsValidatesIndexedChunkLength()
        {
            using var stream = CreateChunkMcap(out var chunkStart, out var chunkLength);
            Assert.True(ThrowsInvalidData(() =>
            {
                new McapReader(stream).ReadChunkRecords(chunkStart, chunkLength + 1, out _);
            }), "134-9D-1: chunk record length must match the indexed chunk length");
        }

        [Fact]
        public void ReadChunkRecordsHonorsCallerRecordSizeLimit()
        {
            using var stream = CreateChunkMcap(out var chunkStart, out var chunkLength);
            var reader = new McapReader(stream);

            var error = Assert.Throws<InvalidDataException>(() =>
                reader.ReadChunkRecords(
                    chunkStart,
                    chunkLength,
                    out _,
                    recordSizeLimit: 1));

            Assert.Contains("exceeds limit 1", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void SummarylessChunkCrcCanBeDisabled()
        {
            using (var rejecting = CreateSummarylessBadChunkCrcMcap())
            {
                Assert.True(ThrowsInvalidData(() => new McapReader(rejecting).ReadSummary()),
                    "134-9D-2: summaryless chunk CRC mismatch is rejected by default");
            }

            using (var permissive = CreateSummarylessBadChunkCrcMcap())
            {
                var summary = new McapReader(permissive).ReadSummary(validateCrcs: false);
                Assert.True(summary.Statistics != null && summary.Statistics.ChunkCount == 1,
                    "134-9D-3: summaryless chunk CRC validation can be disabled");
            }
        }

        [Fact]
        public void IndexedFileOrderMaxMessagesStopsBeforeReadingLaterChunks()
        {
            var bytes = CreateTwoChunkIndexedMcap(out var secondChunkOffset);
            using var stream = new CountingSeekStream(bytes, (long)secondChunkOffset);
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);
            stream.ResetTargetHits();

            var messages = reader.ReadMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 1
            });

            var message = Assert.Single(messages);
            Assert.Equal(1UL, message.LogTime);
            Assert.Equal(0, stream.TargetHits);
        }

        [Fact]
        public void IndexedReaderThrowsAfterDispose()
        {
            using var stream = CreateSimpleMessageMcap(3);
            var reader = new McapIndexedReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            reader.Dispose();
            Assert.True(ThrowsObjectDisposed(() => { var _ = reader.Summary; }),
                "134-9E-1: Summary rejects access after indexed reader disposal");
            Assert.True(ThrowsObjectDisposed(() => reader.ReadMessages()),
                "134-9E-2: ReadMessages rejects access after indexed reader disposal");
        }

        [Fact]
        public void IndexedReaderConcurrentDisposeDisposesOwnedStreamOnce()
        {
            var stream = new CountingDisposeStream(SimpleFiveMessageMcap);
            var reader = new McapIndexedReader(stream, leaveOpen: false, McapSequentialReadLimits.UnlimitedForTests);
            using var gate = new ManualResetEventSlim(false);
            Exception firstException = null;
            Exception secondException = null;

            var first = new Thread(() =>
            {
                try
                {
                    gate.Wait();
                    reader.Dispose();
                }
                catch (Exception ex)
                {
                    firstException = ex;
                }
            });
            var second = new Thread(() =>
            {
                try
                {
                    gate.Wait();
                    reader.Dispose();
                }
                catch (Exception ex)
                {
                    secondException = ex;
                }
            });

            first.Start();
            second.Start();
            gate.Set();
            first.Join();
            second.Join();

            Assert.Null(firstException);
            Assert.Null(secondException);
            Assert.Equal(1, stream.DisposeCount);
        }

        [Fact]
        public void CompositeReadersReleaseReusableBuffersOnDispose()
        {
            var bytes = CreateLargeRecordMcap();

            using (var indexedStream = new MemoryStream(bytes, writable: false))
            {
                var indexed = new McapIndexedReader(
                    indexedStream,
                    leaveOpen: true,
                    McapSequentialReadLimits.UnlimitedForTests);
                indexed.ReadMessages();
                var nestedReader = typeof(McapIndexedReader)
                    .GetField("_reader", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(indexed);
                var nestedBuffer = typeof(McapReader)
                    .GetField("_recordContentBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(nestedBuffer.GetValue(nestedReader));

                indexed.Dispose();

                Assert.Null(nestedBuffer.GetValue(nestedReader));
            }

            using (var streamingStream = new MemoryStream(bytes, writable: false))
            {
                var streaming = new McapStreamingReader(
                    streamingStream,
                    leaveOpen: true,
                    sequentialReadLimits: McapSequentialReadLimits.UnlimitedForTests);
                streaming.Read();
                var streamingBuffer = typeof(McapStreamingReader)
                    .GetField("_contentBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(streamingBuffer.GetValue(streaming));

                streaming.Dispose();

                Assert.Null(streamingBuffer.GetValue(streaming));
            }
        }

        [Fact]
        public void FileOrderMaxMessagesKeepsFirstMatches()
        {
            using var stream = OpenSimpleMessageMcap(SimpleFiveMessageMcap);
            using var indexed = new McapIndexedReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var messages = indexed.ReadMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 2
            });
            Assert.True(messages.Count == 2 && messages[0].Sequence == 1 && messages[1].Sequence == 2,
                "134-9F-1: indexed FileOrder + MaxMessages keeps the first file-order messages");
        }

        [Fact]
        public void StreamingFileOrderMaxMessagesKeepsFirstMatches()
        {
            using var stream = OpenSimpleMessageMcap(SimpleFiveMessageMcap);
            using var streaming = new McapStreamingReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var result = streaming.Read(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 2
            });
            Assert.True(result.Messages.Count == 2 && result.Messages[0].Sequence == 1 && result.Messages[1].Sequence == 2,
                "134-9F-2: streaming FileOrder + MaxMessages keeps the first file-order messages");
        }

        [Fact]
        public void StreamingAscendingMaxMessagesKeepsLatestLogTimes()
        {
            using var stream = OpenSimpleMessageMcap(SimpleFiveMessageMcap);
            using var streaming = new McapStreamingReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var result = streaming.Read(new McapReadOptions
            {
                Order = McapReadOrder.LogTimeAscending,
                MaxMessages = 2
            });
            Assert.True(result.Messages.Count == 2 && result.Messages[0].Sequence == 4 && result.Messages[1].Sequence == 5,
                "187-140: streaming LogTimeAscending + MaxMessages keeps the latest messages");
        }

        [Fact]
        public void AscendingMaxMessagesReturnsSameSubsetAcrossReaders()
        {
            var options = new McapReadOptions
            {
                Order = McapReadOrder.LogTimeAscending,
                MaxMessages = 2
            };

            using var indexedStream = OpenSimpleMessageMcap(SimpleFiveMessageMcap);
            using var indexed = new McapIndexedReader(
                indexedStream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);
            var indexedMessages = indexed.ReadMessages(options);

            using var streamingStream = OpenSimpleMessageMcap(SimpleFiveMessageMcap);
            using var streaming = new McapStreamingReader(
                streamingStream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);
            var streamingMessages = streaming.Read(options).Messages;

            Assert.Equal(
                indexedMessages.ConvertAll(message => message.Sequence),
                streamingMessages.ConvertAll(message => message.Sequence));
        }

    }
}
