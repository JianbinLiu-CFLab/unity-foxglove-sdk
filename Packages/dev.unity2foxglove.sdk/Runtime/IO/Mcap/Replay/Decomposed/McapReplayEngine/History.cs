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
        /// <summary>
        /// Reads every message in the inclusive range [<paramref name="fromTimeNs"/>,
        /// <paramref name="toTimeNs"/>] in chronological order without changing the
        /// active replay cursor. Used to rebuild Foxglove time-series panels after
        /// a seek while paused.
        /// </summary>
        public List<McapMessage> History(ulong fromTimeNs, ulong toTimeNs, List<McapMessage> result)
            => History(fromTimeNs, toTimeNs, result, maxMessages: 0);

        /// <summary>
        /// Reads messages in [fromTimeNs, toTimeNs], retaining only the latest
        /// <paramref name="maxMessages"/> when a positive cap is supplied.
        /// </summary>
        public List<McapMessage> History(ulong fromTimeNs, ulong toTimeNs, List<McapMessage> result, int maxMessages)
            => History(fromTimeNs, toTimeNs, result, maxMessages, null);

        /// <summary>Reads bounded history, optionally retaining only subscribed replay channels.</summary>
        public List<McapMessage> History(
            ulong fromTimeNs,
            ulong toTimeNs,
            List<McapMessage> result,
            int maxMessages,
            ISet<ushort> channelFilter)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            ThrowIfDisposed();
            result.Clear();

            if (!IsLoaded || !CanSeek)
            {
                LastHistoryMetrics = new HistoryMetrics(0, 0, 0, 0);
                return result;
            }

            var clampedFrom = fromTimeNs < StartTimeNs ? StartTimeNs : fromTimeNs;
            var clampedTo = toTimeNs > EndTimeNs ? EndTimeNs : toTimeNs;
            if (clampedTo < clampedFrom)
            {
                LastHistoryMetrics = new HistoryMetrics(0, 0, 0, 0);
                return result;
            }

            if (maxMessages > 0
                && TryReadIndexedBoundedHistory(clampedFrom, clampedTo, result, maxMessages, channelFilter))
                return result;

            var boundedCandidates = maxMessages > 0
                ? new List<HistoryCandidate>(maxMessages)
                : null;
            long filteredRecords = 0;
            long candidateCount = 0;
            long payloadCopies = 0;
            long payloadBytesCopied = 0;
            long peakDecompressedChunkBytes = 0;
            long peakDecompressedChunkCount = 0;
            long peakRetainedDecompressedBytes = 0;
            long candidatePayloadCopies = 0;
            long decompressedChunkReads = 0;
            var finalizedCandidateCount = 0;
            using var historySpool = boundedCandidates != null
                ? new HistoryChunkSpool(MaxHistorySpoolBytes)
                : null;
            var boundedChunkNumbers = boundedCandidates != null
                ? GetHistoryChunkNumbersByDescendingEndTime()
                : null;
            var chunkIterations = boundedChunkNumbers != null
                ? boundedChunkNumbers.Count
                : _summary.ChunkIndexes.Count;
            for (var chunkIteration = 0; chunkIteration < chunkIterations; chunkIteration++)
            {
                var chunkNumber = boundedChunkNumbers != null
                    ? boundedChunkNumbers[chunkIteration]
                    : chunkIteration;
                var chunkIndex = _summary.ChunkIndexes[chunkNumber];
                if (boundedCandidates != null)
                {
                    if (chunkIndex.MessageEndTime < clampedFrom)
                        break;
                    if (chunkIndex.MessageStartTime > clampedTo)
                        continue;
                }
                else
                {
                    if (chunkIndex.MessageStartTime > clampedTo)
                        break;
                    if (chunkIndex.MessageEndTime < clampedFrom)
                        continue;
                }

                var uncompressed = _reader.ReadChunkRecords(chunkIndex.ChunkStartOffset, chunkIndex.ChunkLength, out var crcValid);
                decompressedChunkReads++;
                if (!ShouldUseChunkRecords("History chunk", crcValid))
                    continue;
                peakDecompressedChunkBytes = Math.Max(peakDecompressedChunkBytes, uncompressed.LongLength);
                peakDecompressedChunkCount = Math.Max(peakDecompressedChunkCount, 1);
                peakRetainedDecompressedBytes = Math.Max(peakRetainedDecompressedBytes, uncompressed.LongLength);

                var offset = 0;
                while (offset + 9 <= uncompressed.Length)
                {
                    var record = McapReplayChunkRecordReader.ReadNext(uncompressed, ref offset);
                    if (!record.IsMessage)
                        continue;

                    var logNs = record.LogTime;
                    var dataLen = record.DataLength;
                    if (logNs < clampedFrom || logNs > clampedTo)
                        continue;
                    if (channelFilter != null && !channelFilter.Contains(record.ChannelId))
                    {
                        filteredRecords++;
                        continue;
                    }

                    candidateCount++;

                    if (boundedCandidates != null)
                    {
                        var candidate = new HistoryCandidate(
                            chunkNumber,
                            record.DataOffset,
                            dataLen,
                            record.ChannelId,
                            record.Sequence,
                            logNs,
                            record.PublishTime,
                            chunkIndex.ChunkStartOffset,
                            (ulong)record.RecordOffset);
                        var remainingCandidateSlots = maxMessages - finalizedCandidateCount;
                        if (remainingCandidateSlots > 0)
                            InsertBoundedHistoryCandidateCore(
                                boundedCandidates,
                                candidate,
                                remainingCandidateSlots,
                                out _);
                        continue;
                    }

                    var data = new byte[dataLen];
                    Buffer.BlockCopy(uncompressed, record.DataOffset, data, 0, dataLen);
                    result.Add(new McapMessage
                    {
                        ChannelId = record.ChannelId,
                        Sequence = record.Sequence,
                        LogTime = logNs,
                        PublishTime = record.PublishTime,
                        SourceOffset = chunkIndex.ChunkStartOffset,
                        SourceRecordOffset = (ulong)record.RecordOffset,
                        Data = data
                    });
                    payloadCopies++;
                    payloadBytesCopied += dataLen;
                }

                if (boundedCandidates != null)
                {
                    var nextMaxEndTime = chunkIteration + 1 < chunkIterations
                        ? _summary.ChunkIndexes[boundedChunkNumbers[chunkIteration + 1]].MessageEndTime
                        : 0UL;
                    var noRelevantChunksRemain = chunkIteration + 1 >= chunkIterations
                        || nextMaxEndTime < clampedFrom;
                    for (var candidateIndex = boundedCandidates.Count - 1;
                         candidateIndex >= 0;
                         candidateIndex--)
                    {
                        var candidate = boundedCandidates[candidateIndex];
                        if (candidate.ChunkNumber != chunkNumber
                            || (!noRelevantChunksRemain && candidate.LogTime <= nextMaxEndTime))
                            continue;

                        candidate.Data = CopyPayload(uncompressed, candidate.DataOffset, candidate.DataLength);
                        candidatePayloadCopies++;
                        payloadCopies++;
                        payloadBytesCopied += candidate.DataLength;
                        result.Add(new McapMessage
                        {
                            ChannelId = candidate.ChannelId,
                            Sequence = candidate.Sequence,
                            LogTime = candidate.LogTime,
                            PublishTime = candidate.PublishTime,
                            SourceOffset = candidate.SourceOffset,
                            SourceRecordOffset = candidate.SourceRecordOffset,
                            Data = candidate.Data
                        });
                        boundedCandidates.RemoveAt(candidateIndex);
                        finalizedCandidateCount++;
                    }

                    if (finalizedCandidateCount >= maxMessages)
                        break;
                    if (!string.IsNullOrEmpty(chunkIndex.Compression)
                        && HasHistoryCandidateForChunk(boundedCandidates, chunkNumber))
                        historySpool?.Store(chunkNumber, uncompressed);
                }
            }

            if (boundedCandidates != null && boundedCandidates.Count > 0)
            {
                var candidatesByChunk = new Dictionary<int, List<HistoryCandidate>>();
                foreach (var candidate in boundedCandidates)
                {
                    if (!candidatesByChunk.TryGetValue(candidate.ChunkNumber, out var chunkCandidates))
                    {
                        chunkCandidates = new List<HistoryCandidate>();
                        candidatesByChunk[candidate.ChunkNumber] = chunkCandidates;
                    }

                    chunkCandidates.Add(candidate);
                }

                foreach (var chunkPair in candidatesByChunk)
                {
                    var chunkIndex = _summary.ChunkIndexes[chunkPair.Key];
                    var candidates = chunkPair.Value;
                    var firstCandidate = candidates[0];
                    if (string.IsNullOrEmpty(chunkIndex.Compression)
                        && _reader.TryReadUncompressedChunkPayload(
                            chunkIndex.ChunkStartOffset,
                            chunkIndex.ChunkLength,
                            firstCandidate.DataOffset,
                            firstCandidate.DataLength,
                            out var firstPayload,
                            out var directCrcValid))
                    {
                        if (!ShouldUseChunkRecords("History payload chunk", directCrcValid))
                            continue;

                        firstCandidate.Data = firstPayload;
                        candidatePayloadCopies++;
                        payloadCopies++;
                        payloadBytesCopied += firstCandidate.DataLength;
                        result.Add(new McapMessage
                        {
                            ChannelId = firstCandidate.ChannelId,
                            Sequence = firstCandidate.Sequence,
                            LogTime = firstCandidate.LogTime,
                            PublishTime = firstCandidate.PublishTime,
                            SourceOffset = firstCandidate.SourceOffset,
                            SourceRecordOffset = firstCandidate.SourceRecordOffset,
                            Data = firstCandidate.Data
                        });

                        for (var candidateIndex = 1; candidateIndex < candidates.Count; candidateIndex++)
                        {
                            var candidate = candidates[candidateIndex];
                            if (!_reader.TryReadUncompressedChunkPayload(
                                    chunkIndex.ChunkStartOffset,
                                    chunkIndex.ChunkLength,
                                    candidate.DataOffset,
                                    candidate.DataLength,
                                    out var payload,
                                    out var crcValid))
                            {
                                throw new InvalidDataException("Chunk compression changed while materializing History candidates.");
                            }

                            if (!ShouldUseChunkRecords("History payload chunk", crcValid, emitWarning: false))
                                continue;
                            candidate.Data = payload;
                            candidatePayloadCopies++;
                            payloadCopies++;
                            payloadBytesCopied += candidate.DataLength;
                            result.Add(new McapMessage
                            {
                                ChannelId = candidate.ChannelId,
                                Sequence = candidate.Sequence,
                                LogTime = candidate.LogTime,
                                PublishTime = candidate.PublishTime,
                                SourceOffset = candidate.SourceOffset,
                                SourceRecordOffset = candidate.SourceRecordOffset,
                                Data = candidate.Data
                            });
                        }

                        continue;
                    }

                    if (string.IsNullOrEmpty(chunkIndex.Compression))
                    {
                        var uncompressed = _reader.ReadChunkRecords(
                            chunkIndex.ChunkStartOffset,
                            chunkIndex.ChunkLength,
                            out var fallbackCrcValid);
                        decompressedChunkReads++;
                        if (!ShouldUseChunkRecords("History chunk", fallbackCrcValid))
                            continue;

                        foreach (var candidate in candidates)
                        {
                            candidate.Data = CopyPayload(uncompressed, candidate.DataOffset, candidate.DataLength);
                            candidatePayloadCopies++;
                            payloadCopies++;
                            payloadBytesCopied += candidate.DataLength;
                            result.Add(new McapMessage
                            {
                                ChannelId = candidate.ChannelId,
                                Sequence = candidate.Sequence,
                                LogTime = candidate.LogTime,
                                PublishTime = candidate.PublishTime,
                                SourceOffset = candidate.SourceOffset,
                                SourceRecordOffset = candidate.SourceRecordOffset,
                                Data = candidate.Data
                            });
                        }

                        continue;
                    }

                    var spooledPayloads = new List<byte[]>(candidates.Count);
                    var useSpool = historySpool != null;
                    if (useSpool)
                    {
                        foreach (var candidate in candidates)
                        {
                            if (!historySpool.TryCopyPayload(
                                    chunkPair.Key,
                                    candidate.DataOffset,
                                    candidate.DataLength,
                                    out var payload))
                            {
                                useSpool = false;
                                break;
                            }
                            spooledPayloads.Add(payload);
                        }
                    }

                    if (useSpool)
                    {
                        for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                        {
                            var candidate = candidates[candidateIndex];
                            candidate.Data = spooledPayloads[candidateIndex];
                            candidatePayloadCopies++;
                            payloadCopies++;
                            payloadBytesCopied += candidate.DataLength;
                            result.Add(new McapMessage
                            {
                                ChannelId = candidate.ChannelId,
                                Sequence = candidate.Sequence,
                                LogTime = candidate.LogTime,
                                PublishTime = candidate.PublishTime,
                                SourceOffset = candidate.SourceOffset,
                                SourceRecordOffset = candidate.SourceRecordOffset,
                                Data = candidate.Data
                            });
                        }

                        continue;
                    }

                    var fallbackUncompressed = _reader.ReadChunkRecords(
                        chunkIndex.ChunkStartOffset,
                        chunkIndex.ChunkLength,
                        out var compressedFallbackCrcValid);
                    decompressedChunkReads++;
                    if (!ShouldUseChunkRecords("History chunk", compressedFallbackCrcValid))
                        continue;
                    peakDecompressedChunkBytes = Math.Max(
                        peakDecompressedChunkBytes,
                        fallbackUncompressed.LongLength);
                    peakDecompressedChunkCount = Math.Max(peakDecompressedChunkCount, 1);
                    peakRetainedDecompressedBytes = Math.Max(
                        peakRetainedDecompressedBytes,
                        fallbackUncompressed.LongLength);
                    foreach (var candidate in candidates)
                    {
                        candidate.Data = CopyPayload(
                            fallbackUncompressed,
                            candidate.DataOffset,
                            candidate.DataLength);
                        candidatePayloadCopies++;
                        payloadCopies++;
                        payloadBytesCopied += candidate.DataLength;
                        result.Add(new McapMessage
                        {
                            ChannelId = candidate.ChannelId,
                            Sequence = candidate.Sequence,
                            LogTime = candidate.LogTime,
                            PublishTime = candidate.PublishTime,
                            SourceOffset = candidate.SourceOffset,
                            SourceRecordOffset = candidate.SourceRecordOffset,
                            Data = candidate.Data
                        });
                    }
                }
            }

            if (boundedCandidates != null)
                boundedCandidates.Sort(CompareHistoryCandidates);

            if (result.Count > 1)
                result.Sort(CompareMessages);

            TrimHistoryToLatestMessages(result, maxMessages);
            LastHistoryMetrics = new HistoryMetrics(
                candidateCount,
                payloadCopies,
                payloadBytesCopied,
                filteredRecords,
                peakDecompressedChunkBytes,
                peakDecompressedChunkCount,
                peakRetainedDecompressedBytes,
                candidatePayloadCopies,
                decompressedChunkReads);
            return result;
        }
    }
}
