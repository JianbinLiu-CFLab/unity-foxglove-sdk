// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Reflection;
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
            Assert.Equal(1, McapReplayTickThrottler.CountPrefixPreservingLogTimeGroup(
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
            Assert.True(ReadHeadIndex(queue) > 0);

            Assert.Equal(39UL, queue.Pop().LogTime);

            Assert.Equal(40, queue.Count);
            Assert.Equal(0, ReadHeadIndex(queue));
            Assert.Equal(40UL, queue.Pop().LogTime);
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

        private static int ReadHeadIndex(McapReplayPendingQueue queue)
        {
            var field = typeof(McapReplayPendingQueue).GetField(
                "_headIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (int)field.GetValue(queue);
        }
    }
}
