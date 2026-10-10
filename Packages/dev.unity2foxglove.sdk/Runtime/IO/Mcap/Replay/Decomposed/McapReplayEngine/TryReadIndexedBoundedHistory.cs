// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Replay
// Purpose: MCAP replay engine - loads an .mcap file, seeks by timestamp,
// plays/pauses, and emits messages to FoxgloveSession in log-time order.
// Supports LZ4/Zstd compressed chunks via McapReader.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// MCAP replay engine. Loads an .mcap file via McapReader, extracts
    /// channels and messages, and replays them in log-time order into
    /// a live FoxgloveSession. Supports play, pause, and seek.
    /// Instances are not thread-safe; call Load, Tick, Seek, Play, Pause,
    /// Snapshot, and History from one owner thread, normally Unity's main
    /// thread.
    /// </summary>
    public partial class McapReplayEngine : IDisposable
    {
        private bool TryReadIndexedBoundedHistory(
            ulong clampedFrom,
            ulong clampedTo,
            List<McapMessage> result,
            int maxMessages,
            ISet<ushort> channelFilter)
        {
            if (_summary?.ChunkIndexes == null || _summary.ChunkIndexes.Count == 0)
                return false;

            var candidates = new List<IndexedHistoryCandidate>();
            var candidateLimit = maxMessages == int.MaxValue ? int.MaxValue : maxMessages + 1;
            long candidateCount = 0;
            long filteredRecords = 0;
            try
            {
                for (var chunkNumber = 0; chunkNumber < _summary.ChunkIndexes.Count; chunkNumber++)
                {
                    var chunkIndex = _summary.ChunkIndexes[chunkNumber];
                    if (chunkIndex.MessageIndexOffsets == null || chunkIndex.MessageIndexOffsets.Count == 0)
                        return false;

                    foreach (var indexOffset in chunkIndex.MessageIndexOffsets)
                    {
                        var messageIndex = _reader.ReadMessageIndex(
                            indexOffset.Value,
                            chunkIndex.MessageIndexLength);
                        if (messageIndex.ChannelId != indexOffset.Key)
                            return false;

                        foreach (var entry in messageIndex.Records)
                        {
                            if (entry.timestamp < clampedFrom || entry.timestamp > clampedTo)
                                continue;
                            if (channelFilter != null && !channelFilter.Contains(messageIndex.ChannelId))
                            {
                                filteredRecords++;
                                continue;
                            }

                            candidateCount++;
                            InsertBoundedIndexedHistoryCandidate(
                                candidates,
                                chunkNumber,
                                messageIndex.ChannelId,
                                entry.timestamp,
                                chunkIndex.ChunkStartOffset,
                                entry.offset,
                                candidateLimit);
                        }
                    }
                }
            }
            catch (Exception ex) when (
                ex is InvalidDataException
                || ex is EndOfStreamException
                || ex is IOException
                || ex is NotSupportedException)
            {
                return false;
            }
            candidates.Sort(CompareIndexedHistoryCandidates);
            if (candidates.Count > maxMessages)
            {
                var cut = candidates.Count - maxMessages;
                if (cut > 0
                    && candidates[cut - 1].LogTime == candidates[cut].LogTime
                    && candidates[cut - 1].ChannelId == candidates[cut].ChannelId)
                    return false;
                candidates.RemoveRange(0, cut);
            }

            var candidatesByChunk = new Dictionary<int, Dictionary<ulong, IndexedHistoryCandidate>>();
            foreach (var candidate in candidates)
            {
                if (!candidatesByChunk.TryGetValue(candidate.ChunkNumber, out var chunkCandidates))
                {
                    chunkCandidates = new Dictionary<ulong, IndexedHistoryCandidate>();
                    candidatesByChunk[candidate.ChunkNumber] = chunkCandidates;
                }

                if (chunkCandidates.ContainsKey(candidate.SourceRecordOffset))
                    return false;
                chunkCandidates[candidate.SourceRecordOffset] = candidate;
            }

            var indexedResult = new List<McapMessage>(candidates.Count);
            long payloadBytesCopied = 0;
            long decompressedChunkReads = 0;
            long peakDecompressedChunkBytes = 0;
            foreach (var chunkPair in candidatesByChunk)
            {
                var chunkIndex = _summary.ChunkIndexes[chunkPair.Key];
                var uncompressed = _reader.ReadChunkRecords(
                    chunkIndex.ChunkStartOffset,
                    chunkIndex.ChunkLength,
                    out var crcValid);
                decompressedChunkReads++;
                if (!ShouldUseChunkRecords("History indexed chunk", crcValid))
                    return false;

                peakDecompressedChunkBytes = Math.Max(peakDecompressedChunkBytes, uncompressed.LongLength);
                var offset = 0;
                var matched = 0;
                while (offset + 9 <= uncompressed.Length)
                {
                    var record = McapReplayChunkRecordReader.ReadNext(uncompressed, ref offset);
                    if (!record.IsMessage
                        || !chunkPair.Value.TryGetValue((ulong)record.RecordOffset, out var candidate))
                        continue;
                    if (record.ChannelId != candidate.ChannelId || record.LogTime != candidate.LogTime)
                        return false;

                    var data = CopyPayload(uncompressed, record.DataOffset, record.DataLength);
                    indexedResult.Add(new McapMessage
                    {
                        ChannelId = record.ChannelId,
                        Sequence = record.Sequence,
                        LogTime = record.LogTime,
                        PublishTime = record.PublishTime,
                        SourceOffset = candidate.SourceOffset,
                        SourceRecordOffset = candidate.SourceRecordOffset,
                        Data = data
                    });
                    payloadBytesCopied += record.DataLength;
                    matched++;
                }

                if (matched != chunkPair.Value.Count)
                    return false;
            }

            indexedResult.Sort(CompareMessages);
            result.AddRange(indexedResult);
            LastHistoryMetrics = new HistoryMetrics(
                candidateCount,
                indexedResult.Count,
                payloadBytesCopied,
                filteredRecords,
                peakDecompressedChunkBytes,
                candidatesByChunk.Count == 0 ? 0 : 1,
                peakDecompressedChunkBytes,
                indexedResult.Count,
                decompressedChunkReads);
            return true;
        }

        private sealed class IndexedHistoryCandidate
        {
            internal IndexedHistoryCandidate(
                int chunkNumber,
                ushort channelId,
                ulong logTime,
                ulong sourceOffset,
                ulong sourceRecordOffset)
            {
                ChunkNumber = chunkNumber;
                ChannelId = channelId;
                LogTime = logTime;
                SourceOffset = sourceOffset;
                SourceRecordOffset = sourceRecordOffset;
            }

            internal int ChunkNumber { get; }
            internal ushort ChannelId { get; }
            internal ulong LogTime { get; }
            internal ulong SourceOffset { get; }
            internal ulong SourceRecordOffset { get; }
        }

        private static int CompareIndexedHistoryCandidates(
            IndexedHistoryCandidate left,
            IndexedHistoryCandidate right)
        {
            var compare = left.LogTime.CompareTo(right.LogTime);
            if (compare != 0) return compare;
            compare = left.ChannelId.CompareTo(right.ChannelId);
            if (compare != 0) return compare;
            compare = left.SourceOffset.CompareTo(right.SourceOffset);
            return compare != 0 ? compare : left.SourceRecordOffset.CompareTo(right.SourceRecordOffset);
        }

        private static bool HasHistoryCandidateForChunk(
            List<HistoryCandidate> candidates,
            int chunkNumber)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.ChunkNumber == chunkNumber)
                    return true;
            }

            return false;
        }

        private static void InsertBoundedIndexedHistoryCandidate(
            List<IndexedHistoryCandidate> candidates,
            int chunkNumber,
            ushort channelId,
            ulong logTime,
            ulong sourceOffset,
            ulong sourceRecordOffset,
            int maxCandidates)
        {
            if (maxCandidates <= 0)
                return;

            if (candidates.Count >= maxCandidates
                && CompareIndexedHistoryCandidateToValues(
                    candidates[0],
                    channelId,
                    logTime,
                    sourceOffset,
                    sourceRecordOffset) >= 0)
                return;

            var candidate = new IndexedHistoryCandidate(
                chunkNumber,
                channelId,
                logTime,
                sourceOffset,
                sourceRecordOffset);
            if (candidates.Count < maxCandidates)
            {
                candidates.Add(candidate);
                SiftIndexedHistoryCandidateUp(candidates, candidates.Count - 1);
                return;
            }

            candidates[0] = candidate;
            SiftIndexedHistoryCandidateDown(candidates, 0);
        }

        private static int CompareIndexedHistoryCandidateToValues(
            IndexedHistoryCandidate existing,
            ushort channelId,
            ulong logTime,
            ulong sourceOffset,
            ulong sourceRecordOffset)
        {
            var compare = existing.LogTime.CompareTo(logTime);
            if (compare != 0) return compare;
            compare = existing.ChannelId.CompareTo(channelId);
            if (compare != 0) return compare;
            compare = existing.SourceOffset.CompareTo(sourceOffset);
            return compare != 0 ? compare : existing.SourceRecordOffset.CompareTo(sourceRecordOffset);
        }

        private static void SiftIndexedHistoryCandidateUp(
            List<IndexedHistoryCandidate> candidates,
            int index)
        {
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (CompareIndexedHistoryCandidates(candidates[parent], candidates[index]) <= 0)
                    break;
                var parentCandidate = candidates[parent];
                candidates[parent] = candidates[index];
                candidates[index] = parentCandidate;
                index = parent;
            }
        }

        private static void SiftIndexedHistoryCandidateDown(
            List<IndexedHistoryCandidate> candidates,
            int index)
        {
            while (true)
            {
                var left = index * 2 + 1;
                if (left >= candidates.Count)
                    return;
                var right = left + 1;
                var smallest = right < candidates.Count
                    && CompareIndexedHistoryCandidates(candidates[right], candidates[left]) < 0
                    ? right
                    : left;
                if (CompareIndexedHistoryCandidates(candidates[index], candidates[smallest]) <= 0)
                    return;
                var currentCandidate = candidates[index];
                candidates[index] = candidates[smallest];
                candidates[smallest] = currentCandidate;
                index = smallest;
            }
        }

        private static byte[] CopyPayload(byte[] source, int offset, int length)
        {
            var data = new byte[length];
            Buffer.BlockCopy(source, offset, data, 0, length);
            return data;
        }

        private readonly struct SnapshotCandidate
        {
            internal SnapshotCandidate(
                ulong chunkStartOffset,
                ulong chunkLength,
                int dataOffset,
                int dataLength,
                ushort channelId,
                uint sequence,
                ulong logTime,
                ulong publishTime,
                ulong sourceOffset,
                ulong sourceRecordOffset)
            {
                ChunkStartOffset = chunkStartOffset;
                ChunkLength = chunkLength;
                DataOffset = dataOffset;
                DataLength = dataLength;
                ChannelId = channelId;
                Sequence = sequence;
                LogTime = logTime;
                PublishTime = publishTime;
                SourceOffset = sourceOffset;
                SourceRecordOffset = sourceRecordOffset;
            }

            internal ulong ChunkStartOffset { get; }
            internal ulong ChunkLength { get; }
            internal int DataOffset { get; }
            internal int DataLength { get; }
            internal ushort ChannelId { get; }
            internal uint Sequence { get; }
            internal ulong LogTime { get; }
            internal ulong PublishTime { get; }
            internal ulong SourceOffset { get; }
            internal ulong SourceRecordOffset { get; }
        }
    }
}
