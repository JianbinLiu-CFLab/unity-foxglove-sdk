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

        private static MemoryStream CreateMcapWithSummaryOffsetBeforeSummaryStart()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-summary-offset");
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteFooter(summaryStart, summaryStart - 1, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private sealed class CountingDisposeStream : MemoryStream
        {
            private int _disposeCount;

            public CountingDisposeStream(byte[] buffer)
                : base(buffer, writable: false)
            {
            }

            public int DisposeCount => Volatile.Read(ref _disposeCount);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    Interlocked.Increment(ref _disposeCount);
                base.Dispose(disposing);
            }
        }

        private sealed class CountingSeekStream : MemoryStream
        {
            private readonly long _target;
            private int _targetHits;

            public CountingSeekStream(byte[] buffer, long target)
                : base(buffer, writable: false)
            {
                _target = target;
            }

            public int TargetHits => Volatile.Read(ref _targetHits);

            public void ResetTargetHits() => Interlocked.Exchange(ref _targetHits, 0);

            public override long Seek(long offset, SeekOrigin loc)
            {
                var position = base.Seek(offset, loc);
                if (position == _target)
                    Interlocked.Increment(ref _targetHits);
                return position;
            }
        }

        private static MemoryStream CreateSummaryRecordCrossingFooterMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-summary-crossing");
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteBytes(new[] { McapWriter.OpcodeSchema });
                WriteU64LE(stream, 32);
                writer.WriteBytes(new byte[] { 1, 2, 3, 4 });
                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSummaryWithWrongOpcodeMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-summary-wrong-opcode");
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{}"));
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_9", "json", new Dictionary<string, string>());
                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateStreamingCrcMismatchMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-streaming-crc");
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_9", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{}"));
                writer.WriteDataEnd(0x12345678);
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreatePostDataEndMessageMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "round4-f02-post-data-end");
                writer.WriteSchema(1, "round4.F02", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/round4/f02", "json", new Dictionary<string, string>());
                writer.WriteDataEnd();
                writer.WriteMessage(1, 1, 99, 99, Encoding.UTF8.GetBytes("late"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSummarylessFooterWithoutDataEndMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "round4-f-review-summaryless");
                writer.WriteSchema(1, "round4.Review", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/round4/review", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 42, 42, Encoding.UTF8.GetBytes("summaryless"));
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateDuplicateDataEndMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "round4-f02-duplicate-data-end");
                writer.WriteDataEnd();
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateIncompleteMcapEnvelope()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "round4-f02-incomplete-envelope");
                writer.WriteDataEnd();
            }

            stream.Position = 0;
            return stream;
        }

    }
}
