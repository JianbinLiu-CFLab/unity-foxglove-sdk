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
    [Trait("Phase", "134-9")]
    [Trait("Domain", "Mcap")]
    public partial class McapReaderIndexingTests
    {
        private static readonly byte[] SimpleFiveMessageMcap = CreateSimpleMessageMcapBytes(5);

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

    }
}
