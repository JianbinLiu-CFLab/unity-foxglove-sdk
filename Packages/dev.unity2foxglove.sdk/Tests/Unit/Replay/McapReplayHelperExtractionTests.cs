// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Replay
{
    [Trait("Phase", "170A")]
    [Trait("Domain", "Replay")]
    public sealed class McapReplayHelperExtractionTests
    {
        [Fact]
        public void TickThrottlerHandlesEmptySingleUnlimitedAndNegativeBudgets()
        {
            Assert.Equal(0, McapReplayTickThrottler.CountPrefixPreservingLogTimeGroup(
                new List<McapMessage>(),
                8));
            Assert.Equal(1, McapReplayTickThrottler.CountPrefixPreservingLogTimeGroup(
                new List<McapMessage> { Message(1) },
                8));
            Assert.Equal(2, McapReplayTickThrottler.CountPrefixPreservingLogTimeGroup(
                new List<McapMessage> { Message(1), Message(2) },
                0));
            Assert.Equal(2, McapReplayTickThrottler.CountPrefixPreservingLogTimeGroup(
                new List<McapMessage> { Message(1), Message(2) },
                -5));
        }

        [Fact]
        public void TickThrottlerPreservesBoundaryLogTimeGroup()
        {
            var messages = new List<McapMessage>
            {
                Message(100, channelId: 1),
                Message(101, channelId: 2),
                Message(101, channelId: 3),
                Message(101, channelId: 4),
                Message(102, channelId: 5)
            };

            Assert.Equal(4, McapReplayTickThrottler.CountPrefixPreservingLogTimeGroup(messages, 2));
        }

        [Fact]
        public void PendingQueueAppendsPeeksDrainsAndClearsWithoutLosingCount()
        {
            var queue = new McapReplayPendingQueue();

            queue.Add(Message(20, channelId: 2));
            queue.Add(Message(10, channelId: 1));

            Assert.Equal(2, queue.Count);
            Assert.Equal(20UL, queue.Peek().LogTime);
            Assert.Equal(20UL, queue.Pop().LogTime);
            Assert.Equal(1, queue.Count);

            queue.Drop();
            Assert.Equal(0, queue.Count);

            queue.Add(Message(30));
            queue.Clear();
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void PendingQueueReportsEmptyPeekAndPopWithContext()
        {
            var queue = new McapReplayPendingQueue();

            var peek = Assert.Throws<System.InvalidOperationException>(() => queue.Peek());
            Assert.Contains("Peek", peek.Message);

            var pop = Assert.Throws<System.InvalidOperationException>(() => queue.Pop());
            Assert.Contains("Pop", pop.Message);
        }

        [Fact]
        public void PendingQueueSortsOnlyActiveMessagesAfterHeadAdvances()
        {
            var queue = new McapReplayPendingQueue();
            queue.Add(Message(30, channelId: 3));
            queue.Add(Message(10, channelId: 1));
            queue.Add(Message(20, channelId: 2));
            queue.Pop();

            queue.Sort(CompareMessages);

            Assert.Equal(10UL, queue.Pop().LogTime);
            Assert.Equal(20UL, queue.Pop().LogTime);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void PendingQueueSortsAgainAfterSortedQueueReceivesMoreMessages()
        {
            var queue = new McapReplayPendingQueue();
            queue.Add(Message(10, channelId: 1));
            queue.Add(Message(30, channelId: 3));
            queue.Sort(CompareMessages);

            queue.Add(Message(20, channelId: 2));
            queue.Sort(CompareMessages);

            Assert.Equal(10UL, queue.Pop().LogTime);
            Assert.Equal(20UL, queue.Pop().LogTime);
            Assert.Equal(30UL, queue.Pop().LogTime);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void PendingQueueCompactsAfterLargeHeadAdvanceWithoutLosingOrder()
        {
            var queue = new McapReplayPendingQueue();
            for (ushort i = 0; i < 80; i++)
                queue.Add(Message(i, channelId: (ushort)(i + 1)));

            for (var i = 0; i < 39; i++)
                Assert.Equal((ulong)i, queue.Pop().LogTime);

            Assert.Equal(41, queue.Count);
            Assert.True(queue.DebugHeadIndex > 0);

            Assert.Equal(39UL, queue.Pop().LogTime);

            Assert.Equal(40, queue.Count);
            Assert.Equal(0, queue.DebugHeadIndex);
            Assert.Equal(40UL, queue.Pop().LogTime);
        }

        [Fact]
        [Trait("Phase", "174-013")]
        public void ChunkRecordReaderSkipsNonMessageRecordAndAdvancesCursor()
        {
            var bytes = CreateRecord(opcode: 0x03, declaredLength: 3, 0xAA, 0xBB, 0xCC);
            var offset = 0;

            var record = McapReplayChunkRecordReader.ReadNext(bytes, ref offset);

            Assert.False(record.IsMessage);
            Assert.Equal(bytes.Length, offset);
        }

        [Fact]
        [Trait("Phase", "174-013")]
        public void ChunkRecordReaderReturnsMessageHeaderAndPayloadWindowWithoutCopyingPayload()
        {
            var content = new List<byte>();
            WriteU16(content, 7);
            WriteU32(content, 9);
            WriteU64(content, 10);
            WriteU64(content, 11);
            content.AddRange(new byte[] { 0xCA, 0xFE, 0x01 });
            var bytes = CreateRecord(McapWriter.OpcodeMessage, (ulong)content.Count, content.ToArray());
            var offset = 0;

            var record = McapReplayChunkRecordReader.ReadNext(bytes, ref offset);

            Assert.True(record.IsMessage);
            Assert.Equal((ushort)7, record.ChannelId);
            Assert.Equal(9U, record.Sequence);
            Assert.Equal(10UL, record.LogTime);
            Assert.Equal(11UL, record.PublishTime);
            Assert.Equal(3, record.DataLength);
            Assert.Equal(0xCA, bytes[record.DataOffset]);
            Assert.Equal(0xFE, bytes[record.DataOffset + 1]);
            Assert.Equal(bytes.Length, offset);
        }

        [Theory]
        [Trait("Phase", "174-013")]
        [InlineData("zero opcode", "MCAP opcode 0x00 is invalid inside chunk.")]
        [InlineData("oversized record", "MCAP chunk inner record length exceeds supported size.")]
        [InlineData("truncated record", "MCAP chunk inner record is truncated.")]
        [InlineData("truncated message", "MCAP chunk message record is truncated.")]
        public void ChunkRecordReaderRejectsMalformedRecords(string kind, string expectedMessage)
        {
            var bytes = CreateMalformedRecord(kind);
            var offset = 0;

            var error = Assert.Throws<InvalidDataException>(() => McapReplayChunkRecordReader.ReadNext(bytes, ref offset));

            Assert.Equal(expectedMessage, error.Message);
        }

        private static McapMessage Message(ulong logTime, ushort channelId = 1)
            => new McapMessage
            {
                ChannelId = channelId,
                Sequence = channelId,
                LogTime = logTime,
                PublishTime = logTime,
                Data = new byte[] { (byte)channelId }
            };

        private static byte[] CreateRecord(byte opcode, ulong declaredLength, params byte[] content)
        {
            var bytes = new List<byte> { opcode };
            WriteU64(bytes, declaredLength);
            bytes.AddRange(content);
            return bytes.ToArray();
        }

        private static byte[] CreateMalformedRecord(string kind)
        {
            switch (kind)
            {
                case "zero opcode":
                    return CreateRecord(0x00, 0);
                case "oversized record":
                    return CreateRecord(0x03, (ulong)int.MaxValue + 1);
                case "truncated record":
                    return CreateRecord(0x03, 1);
                case "truncated message":
                    var content = new List<byte>();
                    WriteU16(content, 1);
                    WriteU32(content, 2);
                    WriteU64(content, 3);
                    WriteU64(content, 4);
                    return CreateRecord(McapWriter.OpcodeMessage, 1, content.ToArray());
                default:
                    throw new InvalidDataException("Unknown malformed record fixture: " + kind);
            }
        }

        private static void WriteU16(List<byte> bytes, ushort value)
        {
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
        }

        private static void WriteU32(List<byte> bytes, uint value)
        {
            for (var shift = 0; shift < 32; shift += 8)
                bytes.Add((byte)(value >> shift));
        }

        private static void WriteU64(List<byte> bytes, ulong value)
        {
            for (var shift = 0; shift < 64; shift += 8)
                bytes.Add((byte)(value >> shift));
        }

        private static int CompareMessages(McapMessage a, McapMessage b)
        {
            var cmp = a.LogTime.CompareTo(b.LogTime);
            if (cmp != 0) return cmp;
            cmp = a.ChannelId.CompareTo(b.ChannelId);
            if (cmp != 0) return cmp;
            cmp = a.Sequence.CompareTo(b.Sequence);
            if (cmp != 0) return cmp;
            return a.PublishTime.CompareTo(b.PublishTime);
        }

    }
}
