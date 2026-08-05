// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP reader / indexing edge cases (migrated from Phase134_9Validation).

using System;
using System.Collections.Generic;
using System.IO;
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
    public class McapReaderIndexingTests
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
                ChunkUncompressedSizeLimit = 123
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

        private static MemoryStream CreateSimpleMessageMcap(int messageCount)
        {
            return new MemoryStream(CreateSimpleMessageMcapBytes(messageCount), writable: false);
        }

        private static MemoryStream OpenSimpleMessageMcap(byte[] bytes)
        {
            return new MemoryStream(bytes, writable: false);
        }

        private static MemoryStream CreatePayloadMessageMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase173-040-payload-copy");
                writer.WriteSchema(1, "phase173_040.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase173_040", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 1, 1, Encoding.UTF8.GetBytes("first"));
                writer.WriteMessage(1, 2, 2, 2, Encoding.UTF8.GetBytes("second"));
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] CreateSimpleMessageMcapBytes(int messageCount)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-file-order");
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_9", "json", new Dictionary<string, string>());
                for (var i = 1; i <= messageCount; i++)
                    writer.WriteMessage(1, (uint)i, (ulong)i, (ulong)i, Encoding.UTF8.GetBytes("{}"));
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            return stream.ToArray();
        }

        private static McapMessage Message(ushort channelId, uint sequence, ulong logTime)
        {
            return new McapMessage
            {
                ChannelId = channelId,
                Sequence = sequence,
                LogTime = logTime,
                PublishTime = logTime,
                Data = Array.Empty<byte>()
            };
        }

        private static MemoryStream CreateChunkMcap(out ulong chunkStart, out ulong chunkLength)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-chunk-length");
                chunkStart = (ulong)writer.Position;
                writer.WriteChunk(1, 1, 0, 0, "", 0, Array.Empty<byte>());
                chunkLength = (ulong)writer.Position - chunkStart;
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSummarylessBadChunkCrcMcap()
        {
            var stream = new MemoryStream();
            var records = CreateMessageRecord(1);
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-bad-chunk-crc");
                writer.WriteChunk(1, 1, (ulong)records.Length, 0x12345678, "", (ulong)records.Length, records);
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSecondHeaderMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-first-header");
                writer.WriteHeader("", "phase134-9-second-header");
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] CreateMessageRecord(uint sequence)
        {
            var content = new MemoryStream();
            WriteU16LE(content, 1);
            WriteU32LE(content, sequence);
            WriteU64LE(content, sequence);
            WriteU64LE(content, sequence);
            var payload = Encoding.UTF8.GetBytes("{}");
            content.Write(payload, 0, payload.Length);

            var record = new MemoryStream();
            record.WriteByte(McapWriter.OpcodeMessage);
            WriteU64LE(record, (ulong)content.Length);
            var contentBytes = content.ToArray();
            record.Write(contentBytes, 0, contentBytes.Length);
            return record.ToArray();
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteU32LE(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteU16LE(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteU32LE(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void WriteU64LE(Stream stream, ulong value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 32));
            stream.WriteByte((byte)(value >> 40));
            stream.WriteByte((byte)(value >> 48));
            stream.WriteByte((byte)(value >> 56));
        }

        private static bool ThrowsInvalidData(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        private static bool ThrowsObjectDisposed(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }
}
