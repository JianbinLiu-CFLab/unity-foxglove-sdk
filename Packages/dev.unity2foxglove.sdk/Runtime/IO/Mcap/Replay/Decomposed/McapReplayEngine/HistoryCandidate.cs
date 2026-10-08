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
        private sealed class HistoryCandidate
        {
            internal HistoryCandidate(
                int chunkNumber,
                int dataOffset,
                int dataLength,
                ushort channelId,
                uint sequence,
                ulong logTime,
                ulong publishTime,
                ulong sourceOffset,
                ulong sourceRecordOffset)
                : this(
                    chunkNumber,
                    dataOffset,
                    dataLength,
                    channelId,
                    sequence,
                    logTime,
                    publishTime,
                    sourceOffset,
                    sourceRecordOffset,
                    null)
            {
            }

            internal HistoryCandidate(
                int chunkNumber,
                int dataOffset,
                int dataLength,
                ushort channelId,
                uint sequence,
                ulong logTime,
                ulong publishTime,
                ulong sourceOffset,
                ulong sourceRecordOffset,
                byte[] data)
            {
                ChunkNumber = chunkNumber;
                DataOffset = dataOffset;
                DataLength = dataLength;
                ChannelId = channelId;
                Sequence = sequence;
                LogTime = logTime;
                PublishTime = publishTime;
                SourceOffset = sourceOffset;
                SourceRecordOffset = sourceRecordOffset;
                Data = data;
            }

            internal int ChunkNumber { get; }
            internal int DataOffset { get; }
            internal int DataLength { get; }
            internal ushort ChannelId { get; }
            internal uint Sequence { get; }
            internal ulong LogTime { get; }
            internal ulong PublishTime { get; }
            internal ulong SourceOffset { get; }
            internal ulong SourceRecordOffset { get; }
            internal byte[] Data { get; set; }
        }

        private static bool InsertBoundedHistoryCandidate(List<HistoryCandidate> candidates, HistoryCandidate candidate, int maxMessages)
        {
            InsertBoundedHistoryCandidateCore(candidates, candidate, maxMessages, out _);
            return candidates.Contains(candidate);
        }

        private static void InsertBoundedHistoryCandidateCore(
            List<HistoryCandidate> candidates,
            HistoryCandidate candidate,
            int maxMessages,
            out HistoryCandidate evicted)
        {
            var insertAt = candidates.Count;
            while (insertAt > 0
                   && CompareHistoryCandidates(candidates[insertAt - 1], candidate) > 0)
                insertAt--;
            candidates.Insert(insertAt, candidate);
            evicted = null;
            if (candidates.Count > maxMessages)
            {
                evicted = candidates[0];
                candidates.RemoveAt(0);
            }
        }

        private static int CompareHistoryCandidates(
            HistoryCandidate left,
            HistoryCandidate right)
        {
            var compare = left.LogTime.CompareTo(right.LogTime);
            if (compare != 0) return compare;
            compare = left.ChannelId.CompareTo(right.ChannelId);
            if (compare != 0) return compare;
            compare = left.Sequence.CompareTo(right.Sequence);
            if (compare != 0) return compare;
            compare = left.PublishTime.CompareTo(right.PublishTime);
            if (compare != 0) return compare;
            compare = left.SourceOffset.CompareTo(right.SourceOffset);
            return compare != 0 ? compare : left.SourceRecordOffset.CompareTo(right.SourceRecordOffset);
        }

        /// <summary>
        /// Starts or resumes replay. If already ended, seeks back to start first.
        /// </summary>
        public void Play()
        {
            ThrowIfDisposed();
            if (!IsLoaded) return;
            if (!CanSeek)
            {
                _logger.LogWarning(
                    "MCAP replay requires Statistics and ChunkIndex records; playback remains paused.");
                CurrentStatus = Status.Paused;
                return;
            }
            if (CurrentStatus == Status.Ended)
            {
                Seek(StartTimeNs);
            }
            CurrentStatus = Status.Playing;
        }

        /// <summary>
        /// Pauses replay, stopping message emission until Play is called.
        /// </summary>
        public void Pause()
        {
            ThrowIfDisposed();
            if (!IsLoaded) return;
            CurrentStatus = Status.Paused;
        }

        /// <summary>
        /// Seeks to the given timestamp, clearing pending messages and repositioning the chunk cursor.
        /// </summary>
        public void Seek(ulong timeNs)
        {
            ThrowIfDisposed();
            if (!IsLoaded || !CanSeek) return;

            var clampedTimeNs = ClampReplayTime(timeNs);
            _pending.Clear();
            ClearDeferredPending();
            _lastEmitTime = clampedTimeNs;
            _currentTimeNs = clampedTimeNs;

            // Find first chunk that contains or is after clampedTimeNs
            _currentChunkIdx = -1;
            var foundChunk = false;
            for (var i = 0; i < _summary.ChunkIndexes.Count; i++)
            {
                if (clampedTimeNs <= _summary.ChunkIndexes[i].MessageEndTime)
                {
                    _currentChunkIdx = i - 1; // LoadNextChunk will advance to i
                    foundChunk = true;
                    break;
                }
            }
            if (!foundChunk)
                _currentChunkIdx = _summary.ChunkIndexes.Count - 1;

            // Force reload on next tick by marking current chunk exhausted
            _readOffset = int.MaxValue;

            if (CurrentStatus == Status.Ended)
                CurrentStatus = Status.Paused;
        }

        /// <summary>
        /// Releases the underlying file stream and resets loaded state.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            ResetLoadedState(disposeStream: true);
            _disposed = true;
        }

        // Internal

        /// <summary>
        /// Clears replay cursors and optionally disposes the currently open MCAP stream.
        /// Used by both Dispose and repeated Load calls to avoid leaked file handles.
        /// </summary>
        private void ResetLoadedState(bool disposeStream)
        {
            if (disposeStream)
            {
                _reader?.Dispose();
                _stream?.Dispose();
            }
            _stream = null;
            // McapReader borrows the stream; disposing it releases reader-owned
            // scratch buffers without closing the stream.
            _reader = null;
            _metadataFallbackCache = null;
            _metadataFallbackScanComplete = false;
            _summary = null;
            _snapshotChunkIndexesByDescendingEndTime = null;
            _pending.Clear();
            ClearDeferredPending();
            _currentChunkIdx = -1;
            _currentChunkStartOffset = 0;
            _currentUncompressed = null;
            _readOffset = 0;
            _lastEmitTime = 0;
            _currentTimeNs = 0;
            LastTickScannedRecordCount = 0;
            StartTimeNs = 0;
            EndTimeNs = 0;
            CanSeek = false;
            IsLoaded = false;
            CurrentStatus = Status.Paused;
        }

        /// <summary>
        /// Advances to the next chunk, decompresses it, and resets the read cursor.
        /// Returns false if no more chunks remain.
        /// </summary>
        private bool LoadNextChunk()
        {
            _currentChunkIdx++;
            if (_currentChunkIdx >= _summary.ChunkIndexes.Count) return false;

            var ci = _summary.ChunkIndexes[_currentChunkIdx];
            _currentChunkStartOffset = ci.ChunkStartOffset;
            _currentUncompressed = _reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength, out var crcValid);
            if (!ShouldUseChunkRecords($"Chunk {_currentChunkIdx}", crcValid))
                _currentUncompressed = Array.Empty<byte>();
            _readOffset = 0;
            return true;
        }

        private bool ShouldStopBeforeNextChunk(
            McapChunkIndex nextChunk,
            ulong clampedNow,
            List<McapMessage> result)
        {
            if (nextChunk.MessageStartTime > clampedNow)
                return true;
            if (!HasReachedScanBudget(result))
                return false;
            return nextChunk.MessageStartTime > ScanBudgetBoundaryTime(result);
        }

        private bool ShouldStopBeforeDueRecord(ulong logTime, List<McapMessage> result)
        {
            if (!HasReachedScanBudget(result))
                return false;
            return logTime > ScanBudgetBoundaryTime(result);
        }

        private bool HasReachedScanBudget(List<McapMessage> result)
            => MaxMessagesPerTick > 0 && result.Count >= MaxMessagesPerTick;

        private ulong ScanBudgetBoundaryTime(List<McapMessage> result)
        {
            if (MaxMessagesPerTick <= 0 || _scanBoundaryCandidates.Count < MaxMessagesPerTick)
                return ulong.MaxValue;
            return _scanBoundaryCandidates[MaxMessagesPerTick - 1].LogTime;
        }

        private int PendingCount => _pending.Count + DeferredPendingCount;

        private int DeferredPendingCount => _deferredPending.Count - _deferredPendingHead;

        private ulong PeekPendingLogTime()
        {
            if (DeferredPendingCount <= 0)
                return _pending.Peek().LogTime;
            if (_pending.Count <= 0)
                return _deferredPending[_deferredPendingHead].LogTime;

            var deferred = _deferredPending[_deferredPendingHead];
            return CompareDeferredToMessage(deferred, _pending.Peek()) <= 0
                ? deferred.LogTime
                : _pending.Peek().LogTime;
        }

        /// <summary>
        /// Dequeues the oldest pending message.
        /// </summary>
        private McapMessage PopPending()
        {
            if (DeferredPendingCount <= 0)
                return _pending.Pop();
            if (_pending.Count <= 0)
                return PopDeferred();

            return CompareDeferredToMessage(
                       _deferredPending[_deferredPendingHead],
                       _pending.Peek()) <= 0
                ? PopDeferred()
                : _pending.Pop();
        }

        private void DropPending()
        {
            if (DeferredPendingCount <= 0)
            {
                _pending.Drop();
                return;
            }
            if (_pending.Count <= 0)
            {
                DropDeferred();
                return;
            }

            if (CompareDeferredToMessage(
                    _deferredPending[_deferredPendingHead],
                    _pending.Peek()) <= 0)
                DropDeferred();
            else
                _pending.Drop();
        }

        private void AddPending(McapMessage message)
            => _pending.Add(message);

        private static void TrimHistoryToLatestMessages(List<McapMessage> result, int maxMessages)
        {
            if (maxMessages <= 0 || result.Count <= maxMessages)
                return;

            result.RemoveRange(0, result.Count - maxMessages);
        }
    }
}
