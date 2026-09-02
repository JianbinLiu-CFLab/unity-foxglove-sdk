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
    public class McapReplayEngine : IDisposable
    {
        /// <summary>
        /// Underlying MCAP binary reader.
        /// </summary>
        private McapReader _reader;
        /// <summary>
        /// File stream for the loaded .mcap file.
        /// </summary>
        private Stream _stream;
        /// <summary>
        /// Parsed summary of the loaded MCAP file.
        /// </summary>
        private McapFileSummary _summary;
        private readonly McapReplayPendingQueue _pending = new();
        // Future records retain a view into their owning decompressed chunk
        // instead of allocating a second payload-sized array.
        private readonly List<DeferredReplayMessage> _deferredPending = new();
        private int _deferredPendingHead;
        // Records that could not be admitted because the owner/count bound was
        // reached keep only a chunk/record cursor. Their payload is re-read
        // when the record becomes due, so the scan can continue without
        // retaining another decompressed chunk owner.
        private readonly List<DeferredReplayRetry> _deferredRetries = new();
        private readonly Dictionary<ulong, DeferredReplayRetry> _deferredRetryByKey = new();
        private bool _deferredRetriesSorted;
        private readonly Dictionary<byte[], int> _deferredOwnerReferences = new();
        private long _deferredOwnerBytes;
        private readonly List<McapMessage> _defaultTickBuffer = new();
        private readonly Dictionary<ushort, McapMessage> _snapshotLatestByChannel = new();
        private readonly IFoxgloveLogger _logger;

        // Per-chunk state
        /// <summary>
        /// Index of the chunk currently being read, or -1 if none loaded.
        /// </summary>
        private int _currentChunkIdx = -1;
        /// <summary>
        /// Decompressed record data for the current chunk.
        /// </summary>
        private byte[] _currentUncompressed;
        /// <summary>
        /// Read cursor position within the current decompressed chunk.
        /// </summary>
        private int _readOffset;
        /// <summary>
        /// Log time of the most recently emitted message, used to skip out-of-order records.
        /// </summary>
        private ulong _lastEmitTime;
        /// <summary>
        /// Current replay time in nanoseconds.
        /// </summary>
        private ulong _currentTimeNs;
        private bool _disposed;

        /// <summary>
        /// Base value for replay-generated channel IDs to avoid collisions with original IDs.
        /// </summary>
        public const ulong ReplayChannelIdBase = 0x80000000UL;
        /// <summary>
        /// Best-effort maximum number of messages emitted per Tick call.
        /// Set to <c>0</c> or a negative value to preserve the legacy
        /// unlimited-per-tick behavior.
        /// A single log-time group may exceed this soft cap so logically
        /// simultaneous scene and transform messages are not split across ticks;
        /// pathological files with very large same-timestamp groups can therefore
        /// exceed this value in one tick by design.
        /// </summary>
        private int _maxMessagesPerTick = 8;

        private const long DefaultMaxDeferredOwnerBytes = (long)McapReader.DefaultChunkUncompressedSizeLimit;
        private const int DefaultMaxDeferredMessages = 100000;
        // Retry entries contain only bounded scalar metadata and are not
        // counted as owner-retained payloads. Keep a separate hard ceiling so
        // a file with an unbounded number of rejected future records cannot
        // grow the metadata queue indefinitely.
        private const int DefaultMaxDeferredRetryRecords = 100000;
        private long _maxDeferredOwnerBytes = DefaultMaxDeferredOwnerBytes;
        private int _maxDeferredMessages = DefaultMaxDeferredMessages;

        public int MaxMessagesPerTick
        {
            get => _maxMessagesPerTick;
            set => _maxMessagesPerTick = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Maximum decompressed chunk-owner bytes retained by deferred future
        /// replay messages. A non-positive value restores the default bound.
        /// </summary>
        public long MaxDeferredOwnerBytes
        {
            get => _maxDeferredOwnerBytes;
            set => _maxDeferredOwnerBytes = value > 0 ? value : DefaultMaxDeferredOwnerBytes;
        }

        /// <summary>
        /// Maximum number of future replay messages retained as deferred views.
        /// A non-positive value restores the default bound.
        /// </summary>
        public int MaxDeferredMessages
        {
            get => _maxDeferredMessages;
            set => _maxDeferredMessages = value > 0 ? value : DefaultMaxDeferredMessages;
        }

        /// <summary>
        /// Whether a file has been loaded successfully.
        /// </summary>
        public bool IsLoaded { get; private set; }
        /// <summary>
        /// Earliest message timestamp in nanoseconds.
        /// </summary>
        public ulong StartTimeNs { get; private set; }
        /// <summary>
        /// Latest message timestamp in nanoseconds.
        /// </summary>
        public ulong EndTimeNs { get; private set; }
        /// <summary>
        /// Whether seeking is supported (requires statistics and chunk indexes).
        /// </summary>
        public bool CanSeek { get; private set; }
        /// <summary>
        /// Current replay timestamp in nanoseconds.
        /// </summary>
        public ulong CurrentTimeNs => _currentTimeNs;
        /// <summary>
        /// Number of chunk records inspected by the most recent Tick call.
        /// This diagnostic is intentionally observable so callers can verify
        /// that the per-tick scan budget, rather than only the emitted-message
        /// cap, bounds work inside a large chunk.
        /// </summary>
        public int LastTickScannedRecordCount { get; private set; }
        /// <summary>
        /// Channels defined in the loaded MCAP file.
        /// </summary>
        public IReadOnlyList<McapChannel> Channels => _summary?.Channels;
        /// <summary>
        /// Full summary of the loaded MCAP file.
        /// </summary>
        public McapFileSummary Summary => _summary;

        /// <summary>
        /// Reads the first metadata record with the given name from the loaded
        /// MCAP summary. Intended for pre-playback guards before the replay
        /// cursor starts consuming chunk data.
        /// </summary>
        public McapMetadata FindMetadata(string name)
        {
            ThrowIfDisposed();
            if (!IsLoaded || _reader == null || _summary?.MetadataIndexes == null || string.IsNullOrEmpty(name))
                return null;

            foreach (var index in _summary.MetadataIndexes)
            {
                if (!string.Equals(index?.Name, name, StringComparison.Ordinal))
                    continue;

                var metadata = _reader.ReadMetadataAt(index.Offset);
                if (metadata != null && string.Equals(metadata.Name, name, StringComparison.Ordinal))
                    return metadata;
            }

            return null;
        }

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

        public CorruptChunkPolicy CrcMismatchPolicy { get; set; } = CorruptChunkPolicy.UseWithWarning;
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
                result.Add(PopPending());
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
                                throw new InvalidOperationException(
                                    "Replay deferred retry metadata bound was exceeded.");
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
                    result.Add(new McapMessage
                    {
                        ChannelId = record.ChannelId,
                        Sequence = record.Sequence,
                        LogTime = logNs,
                        PublishTime = record.PublishTime,
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

            if (!IsLoaded || !CanSeek)
                return result;

            var clampedTime = timeNs > EndTimeNs ? EndTimeNs : timeNs;
            if (clampedTime < StartTimeNs)
                clampedTime = StartTimeNs;

            var latestByChannel = _snapshotLatestByChannel;
            latestByChannel.Clear();
            foreach (var chunkIndex in _summary.ChunkIndexes)
            {
                if (chunkIndex.MessageStartTime > clampedTime)
                    break;

                var uncompressed = _reader.ReadChunkRecords(chunkIndex.ChunkStartOffset, chunkIndex.ChunkLength, out var crcValid);
                if (!ShouldUseChunkRecords("Snapshot chunk", crcValid))
                    continue;

                var offset = 0;
                while (offset + 9 <= uncompressed.Length)
                {
                    var record = McapReplayChunkRecordReader.ReadNext(uncompressed, ref offset);
                    if (!record.IsMessage)
                        continue;

                    var logNs = record.LogTime;
                    var dataLen = record.DataLength;
                    if (logNs > clampedTime)
                        continue;

                    var candidate = new McapMessage
                    {
                        ChannelId = record.ChannelId,
                        Sequence = record.Sequence,
                        LogTime = logNs,
                        PublishTime = record.PublishTime
                    };
                    if (latestByChannel.TryGetValue(record.ChannelId, out var current)
                        && McapIndexedReaderHelpers.CompareLatestCandidate(candidate, current) <= 0)
                        continue;

                    var data = new byte[dataLen];
                    Buffer.BlockCopy(uncompressed, record.DataOffset, data, 0, dataLen);
                    candidate.Data = data;
                    latestByChannel[record.ChannelId] = candidate;
                }
            }

            result.AddRange(latestByChannel.Values);
            if (result.Count > 1)
                result.Sort(CompareMessages);
            return result;
        }

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
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            ThrowIfDisposed();
            result.Clear();

            if (!IsLoaded || !CanSeek)
                return result;

            var clampedFrom = fromTimeNs < StartTimeNs ? StartTimeNs : fromTimeNs;
            var clampedTo = toTimeNs > EndTimeNs ? EndTimeNs : toTimeNs;
            if (clampedTo < clampedFrom)
                return result;

            foreach (var chunkIndex in _summary.ChunkIndexes)
            {
                if (chunkIndex.MessageStartTime > clampedTo)
                    break;
                if (chunkIndex.MessageEndTime < clampedFrom)
                    continue;

                var uncompressed = _reader.ReadChunkRecords(chunkIndex.ChunkStartOffset, chunkIndex.ChunkLength, out var crcValid);
                if (!ShouldUseChunkRecords("History chunk", crcValid))
                    continue;

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

                    var data = new byte[dataLen];
                    Buffer.BlockCopy(uncompressed, record.DataOffset, data, 0, dataLen);

                    result.Add(new McapMessage
                    {
                        ChannelId = record.ChannelId,
                        Sequence = record.Sequence,
                        LogTime = logNs,
                        PublishTime = record.PublishTime,
                        Data = data
                    });
                }
            }

            if (result.Count > 1)
                result.Sort(CompareMessages);

            TrimHistoryToLatestMessages(result, maxMessages);
            return result;
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
            _summary = null;
            _pending.Clear();
            ClearDeferredPending();
            _currentChunkIdx = -1;
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
            if (MaxMessagesPerTick <= 0 || result.Count < MaxMessagesPerTick)
                return ulong.MaxValue;
            if (result.Count > 1)
                result.Sort(CompareMessages);
            return result[MaxMessagesPerTick - 1].LogTime;
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

        private void SortPending()
        {
            // The materialized-only fast path remains equivalent to
            // `=> _pending.Sort(CompareMessages)` for the established hot-path
            // contract; deferred views are sorted alongside it below.
            _pending.Sort(CompareMessages);
            CompactDeferredPending();
            if (DeferredPendingCount > 1)
                _deferredPending.Sort(CompareDeferredMessages);
        }

        private bool TryAddDeferred(McapReplayChunkRecord record, byte[] owner)
        {
            if (owner == null)
                return false;
            if (MaxDeferredMessages > 0 && DeferredPendingCount >= MaxDeferredMessages)
                return false;

            if (!_deferredOwnerReferences.TryGetValue(owner, out var ownerReferences))
            {
                var ownerBytes = owner.LongLength;
                if (MaxDeferredOwnerBytes > 0 &&
                    (_deferredOwnerBytes > MaxDeferredOwnerBytes - ownerBytes))
                    return false;

                _deferredOwnerReferences[owner] = 1;
                _deferredOwnerBytes += ownerBytes;
            }
            else
            {
                _deferredOwnerReferences[owner] = ownerReferences + 1;
            }

            _deferredPending.Add(new DeferredReplayMessage
            {
                ChannelId = record.ChannelId,
                Sequence = record.Sequence,
                LogTime = record.LogTime,
                PublishTime = record.PublishTime,
                Owner = owner,
                DataOffset = record.DataOffset,
                DataLength = record.DataLength
            });
            return true;
        }

        private bool TryQueueDeferredRetry(
            int chunkIndex,
            int recordOffset,
            ushort channelId,
            uint sequence,
            ulong logTime,
            ulong publishTime)
        {
            var key = MakeDeferredRetryKey(chunkIndex, recordOffset);
            if (_deferredRetryByKey.ContainsKey(key))
                return true;

            if (_deferredRetryByKey.Count >= DefaultMaxDeferredRetryRecords)
                return false;

            var retry = new DeferredReplayRetry
            {
                ChunkIndex = chunkIndex,
                RecordOffset = recordOffset,
                ChannelId = channelId,
                Sequence = sequence,
                LogTime = logTime,
                PublishTime = publishTime
            };
            _deferredRetryByKey.Add(key, retry);
            _deferredRetries.Add(retry);
            _deferredRetriesSorted = false;
            return true;
        }

        private void RemoveDeferredRetry(int chunkIndex, int recordOffset)
        {
            _deferredRetryByKey.Remove(MakeDeferredRetryKey(chunkIndex, recordOffset));
        }

        private int DeferredRetryCount => _deferredRetryByKey.Count;

        private static ulong MakeDeferredRetryKey(int chunkIndex, int recordOffset)
            => ((ulong)(uint)chunkIndex << 32) | (uint)recordOffset;

        private void FlushDeferredRetries(
            ulong clampedNow,
            ulong emitAfter,
            List<McapMessage> result)
        {
            if (DeferredRetryCount == 0)
            {
                _deferredRetries.Clear();
                _deferredRetriesSorted = true;
                return;
            }

            if (!_deferredRetriesSorted && _deferredRetries.Count > 1)
            {
                _deferredRetries.Sort(CompareDeferredRetries);
                _deferredRetriesSorted = true;
            }

            var writeIndex = 0;
            for (var readIndex = 0; readIndex < _deferredRetries.Count; readIndex++)
            {
                var retry = _deferredRetries[readIndex];
                var key = MakeDeferredRetryKey(retry.ChunkIndex, retry.RecordOffset);
                if (!_deferredRetryByKey.TryGetValue(key, out var activeRetry) ||
                    !ReferenceEquals(activeRetry, retry))
                    continue;

                if (retry.LogTime < emitAfter)
                {
                    _deferredRetryByKey.Remove(key);
                    continue;
                }

                if (retry.LogTime > clampedNow || ShouldStopBeforeDueRecord(retry.LogTime, result))
                {
                    _deferredRetries[writeIndex++] = retry;
                    continue;
                }

                _deferredRetryByKey.Remove(key);
                var message = ReadDeferredRetry(retry);
                if (message != null)
                    result.Add(message);
            }

            if (writeIndex < _deferredRetries.Count)
                _deferredRetries.RemoveRange(writeIndex, _deferredRetries.Count - writeIndex);
        }

        private static int CompareDeferredRetries(DeferredReplayRetry left, DeferredReplayRetry right)
        {
            var cmp = left.LogTime.CompareTo(right.LogTime);
            if (cmp != 0) return cmp;
            cmp = left.ChunkIndex.CompareTo(right.ChunkIndex);
            if (cmp != 0) return cmp;
            return left.RecordOffset.CompareTo(right.RecordOffset);
        }

        private McapMessage ReadDeferredRetry(DeferredReplayRetry retry)
        {
            if (_summary?.ChunkIndexes == null ||
                retry.ChunkIndex < 0 || retry.ChunkIndex >= _summary.ChunkIndexes.Count)
                throw new InvalidDataException("Deferred replay retry references an invalid chunk.");

            var chunk = _summary.ChunkIndexes[retry.ChunkIndex];
            var owner = _reader.ReadChunkRecords(chunk.ChunkStartOffset, chunk.ChunkLength, out var crcValid);
            if (!ShouldUseChunkRecords($"Deferred retry chunk {retry.ChunkIndex}", crcValid))
                return null;

            var offset = retry.RecordOffset;
            var record = McapReplayChunkRecordReader.ReadNext(owner, ref offset);
            if (!record.IsMessage ||
                record.ChannelId != retry.ChannelId ||
                record.Sequence != retry.Sequence ||
                record.LogTime != retry.LogTime ||
                record.PublishTime != retry.PublishTime)
                throw new InvalidDataException("Deferred replay retry no longer matches its source record.");

            var data = new byte[record.DataLength];
            if (record.DataLength > 0)
                Buffer.BlockCopy(owner, record.DataOffset, data, 0, record.DataLength);
            return new McapMessage
            {
                ChannelId = record.ChannelId,
                Sequence = record.Sequence,
                LogTime = record.LogTime,
                PublishTime = record.PublishTime,
                Data = data
            };
        }

        private McapMessage PopDeferred()
        {
            var deferred = _deferredPending[_deferredPendingHead++];
            var owner = deferred.Owner;
            var message = deferred.Materialize();
            ReleaseDeferredOwner(owner);
            CompactDeferredPendingIfUseful();
            return message;
        }

        private void DropDeferred()
        {
            var deferred = _deferredPending[_deferredPendingHead++];
            var owner = deferred.Owner;
            deferred.Owner = null;
            ReleaseDeferredOwner(owner);
            CompactDeferredPendingIfUseful();
        }

        private void ClearDeferredPending()
        {
            _deferredPending.Clear();
            _deferredPendingHead = 0;
            _deferredRetries.Clear();
            _deferredRetryByKey.Clear();
            _deferredRetriesSorted = false;
            _deferredOwnerReferences.Clear();
            _deferredOwnerBytes = 0;
        }

        private void ReleaseDeferredOwner(byte[] owner)
        {
            if (owner == null || !_deferredOwnerReferences.TryGetValue(owner, out var references))
                return;

            if (references <= 1)
            {
                _deferredOwnerReferences.Remove(owner);
                _deferredOwnerBytes -= owner.LongLength;
            }
            else
            {
                _deferredOwnerReferences[owner] = references - 1;
            }
        }

        private void CompactDeferredPending()
        {
            if (_deferredPendingHead <= 0)
                return;
            if (_deferredPendingHead >= _deferredPending.Count)
            {
                _deferredPending.Clear();
                _deferredPendingHead = 0;
                return;
            }

            _deferredPending.RemoveRange(0, _deferredPendingHead);
            _deferredPendingHead = 0;
        }

        private void CompactDeferredPendingIfUseful()
        {
            if (_deferredPendingHead > 32
                && _deferredPendingHead * 2 >= _deferredPending.Count)
                CompactDeferredPending();
        }

        private static int CompareDeferredMessages(
            DeferredReplayMessage left,
            DeferredReplayMessage right)
        {
            var cmp = left.LogTime.CompareTo(right.LogTime);
            if (cmp != 0) return cmp;
            cmp = left.ChannelId.CompareTo(right.ChannelId);
            if (cmp != 0) return cmp;
            cmp = left.Sequence.CompareTo(right.Sequence);
            if (cmp != 0) return cmp;
            return left.PublishTime.CompareTo(right.PublishTime);
        }

        private static int CompareDeferredToMessage(
            DeferredReplayMessage left,
            McapMessage right)
        {
            var cmp = left.LogTime.CompareTo(right.LogTime);
            if (cmp != 0) return cmp;
            cmp = left.ChannelId.CompareTo(right.ChannelId);
            if (cmp != 0) return cmp;
            cmp = left.Sequence.CompareTo(right.Sequence);
            if (cmp != 0) return cmp;
            return left.PublishTime.CompareTo(right.PublishTime);
        }

        private bool ShouldUseChunkRecords(string scope, bool crcValid)
        {
            if (crcValid)
                return true;

            var message = $"[McapReplayEngine] {scope} CRC mismatch; data may be corrupted.";
            _logger.LogWarning(message);

            if (CrcMismatchPolicy == CorruptChunkPolicy.Throw)
                throw new InvalidDataException(message);

            return CrcMismatchPolicy == CorruptChunkPolicy.UseWithWarning;
        }

        private List<McapMessage> FinishTickResult(List<McapMessage> result)
        {
            if (result.Count <= 0)
                return result;

            if (result.Count > 1)
                result.Sort(CompareMessages);

            // Cap at MaxMessagesPerTick without splitting a single log-time
            // group. Replay pose ownership treats one log timestamp as one
            // logical batch, so scene and frame-transform messages sharing the
            // same timestamp must reach listeners before batch-completed fires.
            var takeCount = CountTickResultPrefixPreservingLogTimeGroup(result, MaxMessagesPerTick);
            if (takeCount < result.Count)
            {
                for (int i = takeCount; i < result.Count; i++)
                    AddPending(result[i]);
                result.RemoveRange(takeCount, result.Count - takeCount);
            }

            _lastEmitTime = result[result.Count - 1].LogTime;
            return result;
        }

        private List<McapMessage> FinishTickResultAndUpdateStatus(List<McapMessage> result)
        {
            var finished = FinishTickResult(result);
            UpdatePostTickStatus(finished);
            return finished;
        }

        private void UpdatePostTickStatus(List<McapMessage> result)
        {
            if (PendingCount > 0 || DeferredRetryCount > 0)
            {
                CurrentStatus = Status.Buffering;
                return;
            }

            if (CanSeek &&
                result.Count == 0 &&
                _summary?.ChunkIndexes != null &&
                DeferredRetryCount == 0 &&
                _currentChunkIdx >= _summary.ChunkIndexes.Count - 1 &&
                _readOffset >= (_currentUncompressed?.Length ?? 0))
            {
                CurrentStatus = Status.Ended;
                return;
            }

            if (CurrentStatus == Status.Buffering)
                CurrentStatus = Status.Playing;
        }

        private ulong ClampReplayTime(ulong timeNs)
        {
            return timeNs > EndTimeNs ? EndTimeNs : timeNs;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McapReplayEngine));
        }

        internal static int CountTickResultPrefixPreservingLogTimeGroup(IReadOnlyList<McapMessage> result, int maxMessagesPerTick)
            => McapReplayTickThrottler.CountPrefixPreservingLogTimeGroup(result, maxMessagesPerTick);

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

        private static void SortChunkIndexes(List<McapChunkIndex> chunkIndexes)
        {
            chunkIndexes?.Sort(CompareChunkIndexes);
        }

        private static int CompareChunkIndexes(McapChunkIndex a, McapChunkIndex b)
        {
            var cmp = a.MessageStartTime.CompareTo(b.MessageStartTime);
            if (cmp != 0) return cmp;
            cmp = a.MessageEndTime.CompareTo(b.MessageEndTime);
            if (cmp != 0) return cmp;
            return a.ChunkStartOffset.CompareTo(b.ChunkStartOffset);
        }

        private sealed class DeferredReplayMessage
        {
            internal ushort ChannelId;
            internal uint Sequence;
            internal ulong LogTime;
            internal ulong PublishTime;
            internal byte[] Owner;
            internal int DataOffset;
            internal int DataLength;

            internal McapMessage Materialize()
            {
                var data = new byte[DataLength];
                if (DataLength > 0)
                    Buffer.BlockCopy(Owner, DataOffset, data, 0, DataLength);
                Owner = null;
                return new McapMessage
                {
                    ChannelId = ChannelId,
                    Sequence = Sequence,
                    LogTime = LogTime,
                    PublishTime = PublishTime,
                    Data = data
                };
            }
        }

        private sealed class DeferredReplayRetry
        {
            internal int ChunkIndex;
            internal int RecordOffset;
            internal ushort ChannelId;
            internal uint Sequence;
            internal ulong LogTime;
            internal ulong PublishTime;
        }
    }
}
