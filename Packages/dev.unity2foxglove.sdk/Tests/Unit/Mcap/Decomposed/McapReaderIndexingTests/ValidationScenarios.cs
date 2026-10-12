// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Mcap/Decomposed/McapReaderIndexingTests
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
    public partial class McapReaderIndexingTests
    {
        [Fact]
        public void SummaryOffsetOutsideSummarySectionThrows()
        {
            using var stream = CreateMcapWithSummaryOffsetBeforeSummaryStart();
            Assert.True(ThrowsInvalidData(() => new McapReader(stream).ReadSummary()),
                "134-9A-1: summary_offset_start before summary section is rejected");
        }

        [Fact]
        public void SummaryRecordCrossingFooterThrows()
        {
            using var stream = CreateSummaryRecordCrossingFooterMcap();
            Assert.True(ThrowsInvalidData(() => new McapReader(stream).ReadSummary()),
                "134-9A-2: summary record crossing footer is rejected");
        }

        [Fact]
        public void WrongSummaryOpcodeIsRejected()
        {
            using var stream = CreateSummaryWithWrongOpcodeMcap();
            Assert.Throws<InvalidDataException>(() => new McapReader(stream).ReadSummary());
        }

        [Fact]
        public void StreamingDataEndCrcMismatchThrowsWhenValidationEnabled()
        {
            using var stream = CreateStreamingCrcMismatchMcap();
            Assert.True(ThrowsInvalidData(() =>
            {
                using var reader = new McapStreamingReader(stream, leaveOpen: true);
                reader.Read(new McapReadOptions { ValidateCrcs = true });
            }), "134-9B-1: streaming DataEnd CRC mismatch is rejected when validation is enabled");
        }

        [Fact]
        public void StreamingDataEndCrcMismatchCanBeIgnoredWhenValidationDisabled()
        {
            using var stream = CreateStreamingCrcMismatchMcap();
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read(new McapReadOptions { ValidateCrcs = false });
            Assert.True(result.Summary.Schemas.Count == 1,
                "134-9B-2: streaming DataEnd CRC mismatch can be ignored for compatibility");
        }

        [Fact]
        public void StreamingReaderRejectsMessageAfterDataEnd()
        {
            using var stream = CreatePostDataEndMessageMcap();
            using var reader = new McapStreamingReader(stream, leaveOpen: true);

            var error = Assert.Throws<InvalidDataException>(() => reader.Read());

            Assert.Contains("after DataEnd", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void IndexedLinearFallbackRejectsMessageAfterDataEnd()
        {
            using var stream = CreatePostDataEndMessageMcap();
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);

            var error = Assert.Throws<InvalidDataException>(() => reader.ReadMessages());

            Assert.Contains("after DataEnd", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void IndexedLinearFallbackSupportsSummarylessFooterWithoutDataEnd()
        {
            using var stream = CreateSummarylessFooterWithoutDataEndMcap();
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);

            var messages = reader.ReadMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder
            });

            var message = Assert.Single(messages);
            Assert.Equal(42UL, message.LogTime);
            Assert.Equal("summaryless", Encoding.UTF8.GetString(message.Data));
        }

        [Fact]
        public void PublicStreamingReaderStillRequiresDataEndForSummarylessFooter()
        {
            using var stream = CreateSummarylessFooterWithoutDataEndMcap();
            using var reader = new McapStreamingReader(stream, leaveOpen: true);

            var error = Assert.Throws<InvalidDataException>(() => reader.Read());

            Assert.Contains("before DataEnd", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void LazyLinearFallbackDoesNotApplyEagerRetentionLimits()
        {
            using var stream = CreateSimpleMessageMcap(2);
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                new McapSequentialReadLimits
                {
                    MaxMessages = 1,
                    MaxPayloadBytes = 1
                });

            var messages = new List<McapMessage>();
            foreach (var message in reader.EnumerateMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder
            }))
            {
                messages.Add(message);
            }

            Assert.Equal(2, messages.Count);
        }

        [Fact]
        public void StreamingReaderRejectsDuplicateDataEnd()
        {
            using var stream = CreateDuplicateDataEndMcap();
            using var reader = new McapStreamingReader(stream, leaveOpen: true);

            var error = Assert.Throws<InvalidDataException>(() => reader.Read());

            Assert.Contains("DataEnd", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StreamingReaderRequiresFooterAndTrailingMagic()
        {
            using var stream = CreateIncompleteMcapEnvelope();
            using var reader = new McapStreamingReader(stream, leaveOpen: true);

            var error = Assert.Throws<InvalidDataException>(() => reader.Read());

            Assert.Contains("Footer", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void FileOrderBoundedHelperRejectsTheFirstMessageAtTheConfiguredLimit()
        {
            var result = new List<McapMessage>();
            var options = new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 1
            };

            Assert.True(McapIndexedReaderHelpers.TryAddBoundedMessage(
                result, Message(1, 1, 1), options, out _));
            Assert.False(McapIndexedReaderHelpers.TryAddBoundedMessage(
                result, Message(1, 2, 2), options, out _));
            Assert.Single(result);
            Assert.Equal(1UL, result[0].LogTime);
        }

        [Fact]
        public void LogTimeBoundedHelperMaintainsOrderWithoutResortingTheWindow()
        {
            var ascending = new List<McapMessage>();
            var ascendingOptions = new McapReadOptions
            {
                Order = McapReadOrder.LogTimeAscending,
                MaxMessages = 2
            };

            McapIndexedReaderHelpers.TryAddBoundedMessage(ascending, Message(1, 1, 5), ascendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(ascending, Message(1, 2, 1), ascendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(ascending, Message(1, 3, 3), ascendingOptions, out _);

            Assert.Equal(new[] { 3UL, 5UL }, ascending.Select(message => message.LogTime));

            var descending = new List<McapMessage>();
            var descendingOptions = new McapReadOptions
            {
                Order = McapReadOrder.LogTimeDescending,
                MaxMessages = 2
            };

            McapIndexedReaderHelpers.TryAddBoundedMessage(descending, Message(1, 1, 5), descendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(descending, Message(1, 2, 1), descendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(descending, Message(1, 3, 3), descendingOptions, out _);

            Assert.Equal(new[] { 5UL, 3UL }, descending.Select(message => message.LogTime));
        }

        [Fact]
        public void LogTimeBoundedHelperRetainsTheLatestMessagesInBothOrders()
        {
            var ascending = new List<McapMessage>();
            var descending = new List<McapMessage>();
            var ascendingOptions = new McapReadOptions
            {
                Order = McapReadOrder.LogTimeAscending,
                MaxMessages = 3
            };
            var descendingOptions = new McapReadOptions
            {
                Order = McapReadOrder.LogTimeDescending,
                MaxMessages = 3
            };

            for (uint sequence = 1; sequence <= 10; sequence++)
            {
                McapIndexedReaderHelpers.TryAddBoundedMessage(ascending, Message(1, sequence, sequence), ascendingOptions, out _);
                McapIndexedReaderHelpers.TryAddBoundedMessage(descending, Message(1, sequence, sequence), descendingOptions, out _);
            }

            Assert.Equal(new[] { 8UL, 9UL, 10UL }, ascending.Select(message => message.LogTime));
            Assert.Equal(new[] { 10UL, 9UL, 8UL }, descending.Select(message => message.LogTime));
        }

        [Fact]
        public void LogTimeBoundedHelperEvictsTheOldestMessageInBothOrders()
        {
            var ascending = new List<McapMessage>();
            var descending = new List<McapMessage>();
            var ascendingOptions = new McapReadOptions
            {
                Order = McapReadOrder.LogTimeAscending,
                MaxMessages = 2
            };
            var descendingOptions = new McapReadOptions
            {
                Order = McapReadOrder.LogTimeDescending,
                MaxMessages = 2
            };

            McapIndexedReaderHelpers.TryAddBoundedMessage(ascending, Message(1, 1, 5), ascendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(ascending, Message(1, 2, 7), ascendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(ascending, Message(1, 3, 9), ascendingOptions, out var ascendingEvicted);

            McapIndexedReaderHelpers.TryAddBoundedMessage(descending, Message(1, 1, 5), descendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(descending, Message(1, 2, 7), descendingOptions, out _);
            McapIndexedReaderHelpers.TryAddBoundedMessage(descending, Message(1, 3, 9), descendingOptions, out var descendingEvicted);

            Assert.Equal(5UL, ascendingEvicted?.LogTime);
            Assert.Equal(5UL, descendingEvicted?.LogTime);
        }

        [Fact]
        public void LatestScanStopsOnlyWhenOlderChunksCannotImproveTheSelection()
        {
            var latestByChannel = new Dictionary<ushort, McapMessage>
            {
                [1] = Message(1, 1, 10),
                [2] = Message(2, 1, 4)
            };

            Assert.False(McapIndexedReaderHelpers.CanStopLatestScan(latestByChannel, 2, 6));
            Assert.False(McapIndexedReaderHelpers.CanStopLatestScan(latestByChannel, 2, 4));
            Assert.True(McapIndexedReaderHelpers.CanStopLatestScan(latestByChannel, 2, 3));

            var incomplete = new Dictionary<ushort, McapMessage>
            {
                [1] = Message(1, 1, 10)
            };

            Assert.False(McapIndexedReaderHelpers.CanStopLatestScan(incomplete, 2, 0));
            Assert.False(McapIndexedReaderHelpers.CanStopLatestScan(latestByChannel, 0, 0));
        }

        [Fact]
        public void OfficialEndTimeSemanticsExcludeTheEndBoundaryOnlyWhenEnabled()
        {
            var inclusive = new McapReadOptions { EndTimeNs = 100 };
            var official = new McapReadOptions { EndTimeNs = 100, UseOfficialEndTimeSemantics = true };

            Assert.False(McapIndexedReaderHelpers.IsAtOrPastEnd(99, inclusive));
            Assert.False(McapIndexedReaderHelpers.IsAtOrPastEnd(100, inclusive));
            Assert.True(McapIndexedReaderHelpers.IsAtOrPastEnd(101, inclusive));

            Assert.False(McapIndexedReaderHelpers.IsAtOrPastEnd(99, official));
            Assert.True(McapIndexedReaderHelpers.IsAtOrPastEnd(100, official));
            Assert.True(McapIndexedReaderHelpers.IsAtOrPastEnd(101, official));
        }

        [Fact]
        public void StreamingReaderRejectsFooterWithoutTrailingMagic()
        {
            var bytes = CreateSimpleMessageMcapBytes(1);
            Array.Resize(ref bytes, bytes.Length - McapWriter.MagicLength);
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new McapStreamingReader(stream, leaveOpen: true);

            var error = Assert.Throws<InvalidDataException>(() => reader.Read());
            Assert.Contains("trailing magic", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TruncatedMessageContentThrowsInvalidData()
        {
            Assert.True(ThrowsInvalidData(() => McapRecordDecoder.DecodeMessage(new byte[10], 0, 10)),
                "134-9C-1: truncated message fixed header throws InvalidDataException");
        }

        [Fact]
        public void ChunkIndexVectorLengthMustBeMultipleOfPairSize()
        {
            var content = new MemoryStream();
            WriteU64LE(content, 1);
            WriteU64LE(content, 2);
            WriteU64LE(content, 3);
            WriteU64LE(content, 4);
            WriteU32LE(content, 11);
            Assert.True(ThrowsInvalidData(() => McapRecordDecoder.DecodeChunkIndex(content.ToArray())),
                "134-9C-2: malformed chunk index vector length is rejected");
        }

        [Fact]
        public void ChunkIndexAllowsTrailingFields()
        {
            var content = new MemoryStream();
            WriteU64LE(content, 1);
            WriteU64LE(content, 2);
            WriteU64LE(content, 3);
            WriteU64LE(content, 4);
            WriteU32LE(content, 0);
            WriteU64LE(content, 0);
            WriteString(content, "");
            WriteU64LE(content, 0);
            WriteU64LE(content, 0);
            content.WriteByte(0xFF);

            var decoded = McapRecordDecoder.DecodeChunkIndex(content.ToArray());
            Assert.Equal(1UL, decoded.MessageStartTime);
            Assert.Equal(2UL, decoded.MessageEndTime);
        }

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

        [Fact]
        public void StreamingMessagesOwnPayloadCopies()
        {
            using var stream = CreatePayloadMessageMcap();
            using var streaming = new McapStreamingReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var result = streaming.Read(new McapReadOptions { Order = McapReadOrder.FileOrder });

            Assert.Equal(2, result.Messages.Count);
            Assert.Equal("first", Encoding.UTF8.GetString(result.Messages[0].Data));
            Assert.Equal("second", Encoding.UTF8.GetString(result.Messages[1].Data));
            Assert.NotSame(result.Messages[0].Data, result.Messages[1].Data);
        }

        [Fact]
        public void IndexedReaderHelperLazyOptionsCopyMutableFiltersAndRejectSortedOrders()
        {
            var topics = new List<string> { "/phase174/a" };
            var channelIds = new List<ushort> { 7 };
            var source = new McapReadOptions
            {
                Topics = topics,
                ChannelIds = channelIds,
                StartTimeNs = 10,
                EndTimeNs = 20,
                MaxMessages = 3,
                Order = McapReadOrder.FileOrder,
                AllowLinearFallback = false,
                UseOfficialEndTimeSemantics = true,
                ValidateCrcs = false,
                ChunkUncompressedSizeLimit = 123,
                SummarySizeLimit = 456
            };

            var copy = McapIndexedReaderHelpers.CreateLazyReadOptions(source);
            topics.Add("/phase174/mutated");
            channelIds.Add(9);

            Assert.NotSame(source, copy);
            Assert.NotSame(source.Topics, copy.Topics);
            Assert.NotSame(source.ChannelIds, copy.ChannelIds);
            Assert.Equal(new[] { "/phase174/a" }, copy.Topics);
            Assert.Equal(new ushort[] { 7 }, copy.ChannelIds);
            Assert.Equal(10UL, copy.StartTimeNs);
            Assert.Equal(20UL, copy.EndTimeNs);
            Assert.Equal(3, copy.MaxMessages);
            Assert.Equal(McapReadOrder.FileOrder, copy.Order);
            Assert.False(copy.AllowLinearFallback);
            Assert.True(copy.UseOfficialEndTimeSemantics);
            Assert.False(copy.ValidateCrcs);
            Assert.Equal(123UL, copy.ChunkUncompressedSizeLimit);
            Assert.Equal(456UL, copy.SummarySizeLimit);

            Assert.Throws<NotSupportedException>(() =>
                McapIndexedReaderHelpers.CreateLazyReadOptions(new McapReadOptions
                {
                    Order = McapReadOrder.LogTimeAscending
                }));
        }

        [Fact]
        public void IndexedReaderHelperOrderingAndLimitMatchReaderModes()
        {
            var fileOrder = new List<McapMessage>
            {
                Message(channelId: 1, sequence: 1, logTime: 30),
                Message(channelId: 1, sequence: 2, logTime: 10),
                Message(channelId: 1, sequence: 3, logTime: 20)
            };
            McapIndexedReaderHelpers.ApplyOrderingAndLimit(fileOrder, new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 2
            });
            Assert.Equal(new uint[] { 1, 2 }, new[] { fileOrder[0].Sequence, fileOrder[1].Sequence });

            var ascending = new List<McapMessage>
            {
                Message(channelId: 1, sequence: 1, logTime: 30),
                Message(channelId: 1, sequence: 2, logTime: 10),
                Message(channelId: 1, sequence: 3, logTime: 20)
            };
            McapIndexedReaderHelpers.ApplyOrderingAndLimit(ascending, new McapReadOptions
            {
                Order = McapReadOrder.LogTimeAscending,
                MaxMessages = 2
            });
            Assert.Equal(new uint[] { 3, 1 }, new[] { ascending[0].Sequence, ascending[1].Sequence });

            var descending = new List<McapMessage>
            {
                Message(channelId: 1, sequence: 1, logTime: 30),
                Message(channelId: 1, sequence: 2, logTime: 10),
                Message(channelId: 1, sequence: 3, logTime: 20)
            };
            McapIndexedReaderHelpers.ApplyOrderingAndLimit(descending, new McapReadOptions
            {
                Order = McapReadOrder.LogTimeDescending,
                MaxMessages = 2
            });
            Assert.Equal(new uint[] { 1, 3 }, new[] { descending[0].Sequence, descending[1].Sequence });
        }

        [Fact]
        public void IndexedReaderHelperLatestOutputSortsByChannel()
        {
            var latest = new List<McapMessage>
            {
                Message(channelId: 9, sequence: 1, logTime: 30),
                Message(channelId: 2, sequence: 2, logTime: 30),
                Message(channelId: 5, sequence: 3, logTime: 30)
            };

            latest.Sort(McapIndexedReaderHelpers.CompareLatestOutput);

            Assert.Equal(new ushort[] { 2, 5, 9 }, new[] { latest[0].ChannelId, latest[1].ChannelId, latest[2].ChannelId });
        }

        [Fact]
        public void MalformedMetadataMapLengthThrowsInvalidData()
        {
            var content = new MemoryStream();
            WriteString(content, "phase134_9");
            WriteU32LE(content, 0x80000000);
            Assert.True(ThrowsInvalidData(() => McapRecordDecoder.DecodeMetadata(content.ToArray())),
                "134-9G-1: oversized metadata map length is rejected");
        }

        [Fact]
        public void SecondHeaderInDataSectionThrows()
        {
            using var stream = CreateSecondHeaderMcap();
            Assert.True(ThrowsInvalidData(() => new McapReader(stream).ReadSummary()),
                "134-9G-2: second Header in data section is rejected");
        }

    }
}
