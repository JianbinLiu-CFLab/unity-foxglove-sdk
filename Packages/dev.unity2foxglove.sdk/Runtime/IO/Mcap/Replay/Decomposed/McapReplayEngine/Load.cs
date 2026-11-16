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
        /// Replay engine state.
        /// </summary>
        public enum Status
        {
            /// <summary>Actively emitting messages.</summary>
            Playing,
            /// <summary>Paused by user, not emitting.</summary>
            Paused,
            /// <summary>Messages are queued ahead of the current time but not yet due.</summary>
            Buffering,
            /// <summary>All messages have been emitted.</summary>
            Ended
        }

        public enum CorruptChunkPolicy
        {
            Skip,
            UseWithWarning,
            Throw
        }

        public CorruptChunkPolicy CrcMismatchPolicy { get; set; } = CorruptChunkPolicy.Throw;
        /// <summary>
        /// Current replay engine state.
        /// </summary>
        public Status CurrentStatus { get; private set; } = Status.Paused;

        public McapReplayEngine()
            : this(null)
        {
        }

        public McapReplayEngine(IFoxgloveLogger logger)
        {
            _logger = logger ?? new ConsoleLogger();
        }

        /// <summary>
        /// Opens an .mcap file and reads its summary section, preparing for replay.
        /// </summary>
        public void Load(string filePath)
        {
            ThrowIfDisposed();
            ResetLoadedState(disposeStream: true);

            _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                _reader = new McapReader(_stream);
                _summary = _reader.ReadSummary();
                var chunkIndexes = _summary?.ChunkIndexes;
                SortChunkIndexes(chunkIndexes);
                var chunkCount = chunkIndexes?.Count ?? 0;
                CanSeek = _summary?.Statistics != null && chunkCount > 0;
                StartTimeNs = _summary.Statistics?.MessageStartTime ?? 0;
                EndTimeNs = _summary.Statistics?.MessageEndTime ?? 0;
                _currentTimeNs = StartTimeNs;
                IsLoaded = true;
                CurrentStatus = Status.Paused;
            }
            catch
            {
                ResetLoadedState(disposeStream: true);
                throw;
            }
        }

        /// <summary>
        /// Emit messages due between last tick time and nowNs.
        /// Returns up to MaxMessagesPerTick. Time is driven externally by PlaybackClock.
        /// </summary>
        /// <remarks>
        /// The returned list is owned and reused by this engine. Consume it
        /// before calling Tick again, or use the caller-owned overload for any
        /// deferred processing path.
        /// </remarks>
        public List<McapMessage> Tick(ulong nowNs)
        {
            return Tick(nowNs, _defaultTickBuffer);
        }

        /// <summary>
        /// Emit messages due between last tick time and nowNs into a caller-owned
        /// result buffer. The buffer is cleared before use to avoid per-frame
        /// list allocation in replay controllers.
        /// </summary>
        public List<McapMessage> Tick(ulong nowNs, List<McapMessage> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            ThrowIfDisposed();
            result.Clear();
            _scanBoundaryCandidates.Clear();
            LastTickScannedRecordCount = 0;

            if (!IsLoaded || CurrentStatus == Status.Paused || CurrentStatus == Status.Ended)
                return result;

            var clampedNow = nowNs > EndTimeNs ? EndTimeNs : nowNs;
            _currentTimeNs = clampedNow;
            var emitAfter = _lastEmitTime;
            var stopScanning = false;

            // Flush previously buffered messages that are now due.
            // Filter against emitAfter to drop stale overflow messages
            // whose logTime fell below _lastEmitTime after sort-based capping.
            SortPending();
            while (PendingCount > 0)
            {
                var pendingLogTime = PeekPendingLogTime();
                if (pendingLogTime > clampedNow) break;
                if (pendingLogTime < emitAfter) { DropPending(); continue; }
                if (ShouldStopBeforeDueRecord(pendingLogTime, result))
                    break;
                AddTickResult(result, PopPending());
            }

            // Once the returned batch has reached its scan boundary, do not
            // materialize later due pending entries into the result just to move
            // them back into another queue. Same-time groups remain intact.
            if (PendingCount > 0 && HasReachedScanBudget(result))
            {
                var pendingLogTime = PeekPendingLogTime();
                if (pendingLogTime <= clampedNow &&
                    pendingLogTime > ScanBudgetBoundaryTime(result))
                    stopScanning = true;
            }

            // Retry records whose owner/count admission was previously
            // blocked. They are materialized only once due and only while the
            // normal per-tick boundary permits them.
            FlushDeferredRetries(clampedNow, emitAfter, result);

            if (!CanSeek)
                return FinishTickResultAndUpdateStatus(result);

            // Advance through chunks
            var chunkIndexes = _summary.ChunkIndexes;
            while (_currentChunkIdx < chunkIndexes.Count - 1 || _readOffset < (_currentUncompressed?.Length ?? 0))
            {
                // Need next chunk?
                if (_currentChunkIdx < 0 || _readOffset >= (_currentUncompressed?.Length ?? 0))
                {
                    var nextChunkIdx = _currentChunkIdx + 1;
                    if (nextChunkIdx < chunkIndexes.Count
                        && ShouldStopBeforeNextChunk(chunkIndexes[nextChunkIdx], clampedNow, result))
                    {
                        break;
                    }
                    if (!LoadNextChunk()) break;
                }

                // Read messages from current chunk. If a future record cannot
                // be retained under the owner bound, retain only its cursor
                // and keep scanning for due records. The cursor is retried
                // from the source once it becomes due.
                while (_readOffset + 9 <= _currentUncompressed.Length)
                {
                    var recordStart = _readOffset;
                    var record = McapReplayChunkRecordReader.ReadNext(_currentUncompressed, ref _readOffset);
                    LastTickScannedRecordCount++;
                    if (!record.IsMessage)
                        continue;

                    var logNs = record.LogTime;
                    if (logNs < emitAfter)
                        continue;

                    if (logNs > clampedNow)
                    {
                        // Keep metadata plus a view into the current chunk. The
                        // payload is copied only when the message is emitted.
                        if (!TryAddDeferred(record, _currentUncompressed))
                        {
                    if (!TryQueueDeferredRetry(
                                    _currentChunkIdx,
                                    recordStart,
                                    record.ChannelId,
                                    record.Sequence,
                                    record.LogTime,
                                    record.PublishTime))
                            {
                                _readOffset = recordStart;
                                stopScanning = true;
                                break;
                            }
                        }
                        else
                            RemoveDeferredRetry(_currentChunkIdx, recordStart);
                        continue;
                    }

                    if (ShouldStopBeforeDueRecord(logNs, result))
                    {
                        // The record belongs to a later scan window. Rewind so
                        // the next Tick can consume it without materializing a
                        // large all-due backlog into pending.
                        _readOffset = recordStart;
                        stopScanning = true;
                        break;
                    }

                    var dataLen = record.DataLength;
                    var data = new byte[dataLen];
                    Buffer.BlockCopy(_currentUncompressed, record.DataOffset, data, 0, dataLen);

                    // Collect all eligible messages; FinishTickResult caps
                    // at MaxMessagesPerTick and moves the sorted tail to
                    // pending so overflow never violates _lastEmitTime.
                    AddTickResult(result, new McapMessage
                    {
                        ChannelId = record.ChannelId,
                        Sequence = record.Sequence,
                        LogTime = logNs,
                        PublishTime = record.PublishTime,
                        SourceOffset = _currentChunkStartOffset,
                        SourceRecordOffset = (ulong)record.RecordOffset,
                        Data = data
                    });
                }

                if (stopScanning)
                    break;
            }

            SortPending();
            return FinishTickResultAndUpdateStatus(result);
        }

        /// <summary>
        /// Reads the latest message at or before <paramref name="timeNs"/> for
        /// each channel without changing the active replay cursor. Used to
        /// refresh Foxglove panels after paused seek/pause commands.
        /// </summary>
        public List<McapMessage> Snapshot(ulong timeNs, List<McapMessage> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            ThrowIfDisposed();
            result.Clear();
            long eligibleChunks = 0;
            long skippedChunks = 0;
            long decompressedChunks = 0;
            long headersScanned = 0;
            long candidateUpdates = 0;
            long payloadCopies = 0;
            long payloadBytesCopied = 0;

            if (!IsLoaded || !CanSeek)
            {
                LastSnapshotMetrics = new SnapshotMetrics(0, 0, 0, 0, 0, 0, 0, 0);
                return result;
            }

            var clampedTime = timeNs > EndTimeNs ? EndTimeNs : timeNs;
            if (clampedTime < StartTimeNs)
                clampedTime = StartTimeNs;

            var latestByChannel = _snapshotLatestByChannel;
            latestByChannel.Clear();
            var chunkIndexes = GetSnapshotChunkIndexesByDescendingEndTime();
            // Use the declared channel IDs, rather than only the aggregate
            // statistics count. A malformed chunk can contain a message for an
            // undeclared channel; admitting it would satisfy the count early
            // and allow the scan to stop before an older declared channel is
            // considered.
            var declaredChannelIds = new HashSet<ushort>();
            if (_summary.Channels != null)
            {
                foreach (var channel in _summary.Channels)
                {
                    if (channel != null)
                        declaredChannelIds.Add(channel.Id);
                }
            }
            var expectedChannelCount = declaredChannelIds.Count;
            foreach (var chunkIndex in chunkIndexes)
            {
                if (chunkIndex.MessageStartTime > clampedTime)
                {
                    skippedChunks++;
                    continue;
                }
                if (chunkIndex.MessageEndTime < StartTimeNs)
                    break;

                eligibleChunks++;

                if (expectedChannelCount > 0 && latestByChannel.Count >= expectedChannelCount
                    && CanStopSnapshotScan(latestByChannel, chunkIndex.MessageEndTime))
                    break;

                var uncompressed = _reader.ReadChunkRecords(chunkIndex.ChunkStartOffset, chunkIndex.ChunkLength, out var crcValid);
                decompressedChunks++;
                if (!ShouldUseChunkRecords("Snapshot chunk", crcValid))
                    continue;

                var offset = 0;
                while (offset + 9 <= uncompressed.Length)
                {
                    var record = McapReplayChunkRecordReader.ReadNext(uncompressed, ref offset);
                    headersScanned++;
                    if (!record.IsMessage)
                        continue;

                    var logNs = record.LogTime;
                    var dataLen = record.DataLength;
                    if (logNs > clampedTime)
                        continue;
                    if (expectedChannelCount > 0 && !declaredChannelIds.Contains(record.ChannelId))
                        continue;

                    var candidate = new SnapshotCandidate(
                        chunkIndex.ChunkStartOffset,
                        chunkIndex.ChunkLength,
                        record.DataOffset,
                        dataLen,
                        record.ChannelId,
                        record.Sequence,
                        logNs,
                        record.PublishTime,
                        chunkIndex.ChunkStartOffset,
                        (ulong)record.RecordOffset);
                    if (latestByChannel.TryGetValue(record.ChannelId, out var current)
                        && CompareSnapshotCandidates(candidate, current) <= 0)
                        continue;
                    latestByChannel[record.ChannelId] = candidate;
                    candidateUpdates++;
                }
            }

            var snapshotPayloadChunks = new Dictionary<ulong, byte[]>();
            foreach (var candidate in latestByChannel.Values)
            {
                if (!snapshotPayloadChunks.TryGetValue(candidate.ChunkStartOffset, out var payloadChunk))
                {
                    payloadChunk = _reader.ReadChunkRecords(
                        candidate.ChunkStartOffset,
                        candidate.ChunkLength,
                        out var crcValid);
                    if (!ShouldUseChunkRecords("Snapshot payload chunk", crcValid, emitWarning: false))
                        continue;
                    snapshotPayloadChunks[candidate.ChunkStartOffset] = payloadChunk;
                }

                var data = new byte[candidate.DataLength];
                Buffer.BlockCopy(payloadChunk, candidate.DataOffset, data, 0, candidate.DataLength);
                result.Add(new McapMessage
                {
                    ChannelId = candidate.ChannelId,
                    Sequence = candidate.Sequence,
                    LogTime = candidate.LogTime,
                    PublishTime = candidate.PublishTime,
                    SourceOffset = candidate.SourceOffset,
                    SourceRecordOffset = candidate.SourceRecordOffset,
                    Data = data
                });
                payloadCopies++;
                payloadBytesCopied += candidate.DataLength;
            }

            if (result.Count > 1)
                result.Sort(CompareMessages);
            LastSnapshotMetrics = new SnapshotMetrics(
                eligibleChunks,
                skippedChunks,
                decompressedChunks,
                headersScanned,
                candidateUpdates,
                payloadCopies,
                payloadBytesCopied,
                result.Count);
            return result;
        }
    }
}
