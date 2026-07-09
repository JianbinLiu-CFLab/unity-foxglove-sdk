// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Pure query/order helpers for McapIndexedReader.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    internal static class McapIndexedReaderHelpers
    {
        internal static void ConsiderLatestCandidate(
            McapMessage message,
            McapReadOptions options,
            HashSet<ushort> selectedChannelIds,
            Dictionary<ushort, McapMessage> latestByChannel)
        {
            if (!IsInTimeRange(message.LogTime, options))
                return;
            if (selectedChannelIds != null && !selectedChannelIds.Contains(message.ChannelId))
                return;
            if (!latestByChannel.TryGetValue(message.ChannelId, out var current) ||
                CompareLatestCandidate(message, current) > 0)
                latestByChannel[message.ChannelId] = message;
        }

        internal static bool CanStopLatestScan(
            Dictionary<ushort, McapMessage> latestByChannel,
            int expectedCount,
            ulong nextOlderTime)
        {
            if (expectedCount <= 0 || latestByChannel.Count < expectedCount)
                return false;

            var oldestSelected = ulong.MaxValue;
            foreach (var message in latestByChannel.Values)
            {
                if (message.LogTime < oldestSelected)
                    oldestSelected = message.LogTime;
            }

            return nextOlderTime < oldestSelected;
        }

        internal static bool ContainsAnySelectedChannel(
            Dictionary<ushort, ulong> messageIndexOffsets,
            HashSet<ushort> selectedChannelIds)
        {
            foreach (var channelId in selectedChannelIds)
            {
                if (messageIndexOffsets.ContainsKey(channelId))
                    return true;
            }

            return false;
        }

        internal static int CompareMessages(McapMessage left, McapMessage right)
        {
            var cmp = left.LogTime.CompareTo(right.LogTime);
            if (cmp != 0)
                return cmp;

            cmp = left.ChannelId.CompareTo(right.ChannelId);
            if (cmp != 0)
                return cmp;

            cmp = left.Sequence.CompareTo(right.Sequence);
            if (cmp != 0)
                return cmp;

            return left.PublishTime.CompareTo(right.PublishTime);
        }

        internal static int CompareLatestCandidate(McapMessage left, McapMessage right)
        {
            var cmp = left.LogTime.CompareTo(right.LogTime);
            if (cmp != 0)
                return cmp;

            cmp = left.Sequence.CompareTo(right.Sequence);
            if (cmp != 0)
                return cmp;

            return left.PublishTime.CompareTo(right.PublishTime);
        }

        internal static int CompareLatestOutput(McapMessage left, McapMessage right)
        {
            var cmp = left.ChannelId.CompareTo(right.ChannelId);
            if (cmp != 0)
                return cmp;

            return CompareLatestCandidate(left, right);
        }

        internal static bool IsInTimeRange(ulong logTime, McapReadOptions options)
        {
            if (logTime < options.StartTimeNs)
                return false;
            return !IsAtOrPastEnd(logTime, options);
        }

        internal static McapReadOptions CreateLazyReadOptions(McapReadOptions source)
        {
            var options = source == null
                ? new McapReadOptions { Order = McapReadOrder.FileOrder }
                : CopyReadOptions(source);
            if (options.Order != McapReadOrder.FileOrder)
                throw new NotSupportedException("Lazy MCAP message enumeration supports FileOrder only.");
            return options;
        }

        internal static bool IsAtOrPastEnd(ulong logTime, McapReadOptions options)
        {
            return options.UseOfficialEndTimeSemantics
                ? logTime >= options.EndTimeNs
                : logTime > options.EndTimeNs;
        }

        internal static void ApplyOrderingAndLimit(List<McapMessage> result, McapReadOptions options)
        {
            if (options.Order == McapReadOrder.LogTimeAscending)
                result.Sort(CompareMessages);
            else if (options.Order == McapReadOrder.LogTimeDescending)
                result.Sort((left, right) => CompareMessages(right, left));

            if (options.MaxMessages <= 0 || result.Count <= options.MaxMessages)
                return;

            if (options.Order == McapReadOrder.LogTimeDescending || options.Order == McapReadOrder.FileOrder)
                result.RemoveRange(options.MaxMessages, result.Count - options.MaxMessages);
            else
                result.RemoveRange(0, result.Count - options.MaxMessages);
        }

        private static McapReadOptions CopyReadOptions(McapReadOptions source)
        {
            return new McapReadOptions
            {
                StartTimeNs = source.StartTimeNs,
                EndTimeNs = source.EndTimeNs,
                Topics = source.Topics == null ? null : new List<string>(source.Topics),
                ChannelIds = source.ChannelIds == null ? null : new List<ushort>(source.ChannelIds),
                MaxMessages = source.MaxMessages,
                Order = source.Order,
                UseOfficialEndTimeSemantics = source.UseOfficialEndTimeSemantics,
                AllowLinearFallback = source.AllowLinearFallback,
                ValidateCrcs = source.ValidateCrcs,
                ChunkUncompressedSizeLimit = source.ChunkUncompressedSizeLimit
            };
        }
    }
}
