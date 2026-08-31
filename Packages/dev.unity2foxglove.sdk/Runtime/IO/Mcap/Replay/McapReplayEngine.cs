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

        public int MaxMessagesPerTick
        {
            get => _maxMessagesPerTick;
            set => _maxMessagesPerTick = value < 0 ? 0 : value;
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

            if (!IsLoaded || CurrentStatus == Status.Paused || CurrentStatus == Status.Ended)
                return result;

            var clampedNow = nowNs > EndTimeNs ? EndTimeNs : nowNs;
            _currentTimeNs = clampedNow;
            var emitAfter = _lastEmitTime;

            // Flush previously buffered messages that are now due.
            // Filter against emitAfter to drop stale overflow messages
            // whose logTime fell below _lastEmitTime after sort-based capping.
            SortPending();
            while (PendingCount > 0)
            {
                var pendingLogTime = PeekPendingLogTime();
                if (pendingLogTime > clampedNow) break;
                if (pendingLogTime < emitAfter) { DropPending(); continue; }
                result.Add(PopPending());
            }

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
                    if (nextChunkIdx < chunkIndexes.Count && chunkIndexes[nextChunkIdx].MessageStartTime > clampedNow)
                        break;
                    if (!LoadNextChunk()) break;
                }

                // Read messages from current chunk
                while (_readOffset + 9 <= _currentUncompressed.Length)
                {
                    var record = McapReplayChunkRecordReader.ReadNext(_currentUncompressed, ref _readOffset);
                    if (!record.IsMessage)
                        continue;

                    var logNs = record.LogTime;
                    if (logNs < emitAfter)
                        continue;

                    if (logNs > clampedNow)
                    {
                        // Keep metadata plus a view into the current chunk. The
                        // payload is copied only when the message is emitted.
                        AddDeferred(record, _currentUncompressed);
                        continue;
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

        private void AddDeferred(McapReplayChunkRecord record, byte[] owner)
        {
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
        }

        private McapMessage PopDeferred()
        {
            var deferred = _deferredPending[_deferredPendingHead++];
            var message = deferred.Materialize();
            CompactDeferredPendingIfUseful();
            return message;
        }

        private void DropDeferred()
        {
            _deferredPending[_deferredPendingHead++].Owner = null;
            CompactDeferredPendingIfUseful();
        }

        private void ClearDeferredPending()
        {
            _deferredPending.Clear();
            _deferredPendingHead = 0;
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
            if (PendingCount > 0)
            {
                CurrentStatus = Status.Buffering;
                return;
            }

            if (CanSeek &&
                result.Count == 0 &&
                _summary?.ChunkIndexes != null &&
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
    }
}
