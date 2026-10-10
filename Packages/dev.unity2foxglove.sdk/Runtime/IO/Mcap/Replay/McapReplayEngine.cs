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
        /// Structural counters collected by the most recent <see cref="Snapshot"/>
        /// call. These counters are diagnostic only and do not alter replay
        /// selection or payload ownership semantics.
        /// </summary>
        public readonly struct SnapshotMetrics
        {
            public SnapshotMetrics(
                long eligibleChunks,
                long skippedChunks,
                long decompressedChunks,
                long headersScanned,
                long candidateUpdates,
                long payloadCopies,
                long payloadBytesCopied,
                long returnedMessages)
            {
                EligibleChunks = eligibleChunks;
                SkippedChunks = skippedChunks;
                DecompressedChunks = decompressedChunks;
                HeadersScanned = headersScanned;
                CandidateUpdates = candidateUpdates;
                PayloadCopies = payloadCopies;
                PayloadBytesCopied = payloadBytesCopied;
                ReturnedMessages = returnedMessages;
            }

            public long EligibleChunks { get; }
            public long SkippedChunks { get; }
            public long DecompressedChunks { get; }
            public long HeadersScanned { get; }
            public long CandidateUpdates { get; }
            public long PayloadCopies { get; }
            public long PayloadBytesCopied { get; }
            public long ReturnedMessages { get; }
        }

        /// <summary>Structural counters collected by the most recent <see cref="History"/> call.</summary>
        public readonly struct HistoryMetrics
        {
            public HistoryMetrics(
                long candidateCount,
                long payloadCopies,
                long payloadBytesCopied,
                long filteredRecords,
                long peakDecompressedChunkBytes = 0,
                long peakDecompressedChunkCount = 0,
                long peakRetainedDecompressedBytes = 0,
                long candidatePayloadCopies = 0,
                long decompressedChunkReads = 0)
            {
                CandidateCount = candidateCount;
                PayloadCopies = payloadCopies;
                PayloadBytesCopied = payloadBytesCopied;
                FilteredRecords = filteredRecords;
                MaxObservedDecompressedChunkBytes = peakDecompressedChunkBytes;
                PeakDecompressedChunkCount = peakDecompressedChunkCount;
                PeakRetainedDecompressedBytes = peakRetainedDecompressedBytes;
                CandidatePayloadCopies = candidatePayloadCopies;
                DecompressedChunkReads = decompressedChunkReads;
            }

            public long CandidateCount { get; }
            public long PayloadCopies { get; }
            public long PayloadBytesCopied { get; }
            public long FilteredRecords { get; }
            /// <summary>Largest single decompressed History chunk observed during the query.</summary>
            public long MaxObservedDecompressedChunkBytes { get; }
            /// <summary>Compatibility alias for the historical metric name.</summary>
            public long PeakDecompressedChunkBytes => MaxObservedDecompressedChunkBytes;
            /// <summary>Maximum number of decompressed History chunks retained concurrently.</summary>
            public long PeakDecompressedChunkCount { get; }
            /// <summary>Peak bytes retained by decompressed History chunks at one time.</summary>
            public long PeakRetainedDecompressedBytes { get; }
            /// <summary>Number of candidate payload copies made before final bounded selection.</summary>
            public long CandidatePayloadCopies { get; }
            /// <summary>Number of chunk reads that performed decompression during the query.</summary>
            public long DecompressedChunkReads { get; }
        }

        /// <summary>
        /// Underlying MCAP binary reader.
        /// </summary>
        private McapReader _reader;
        private Dictionary<string, McapReader.McapMetadataRecordIndex> _metadataFallbackCache;
        private bool _metadataFallbackScanComplete;
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
        private readonly Dictionary<int, byte[]> _deferredRetryOwners = new();
        private readonly Dictionary<int, int> _deferredRetryOwnerReferences = new();
        private bool _deferredRetriesSorted;
        private bool _deferredPendingSorted = true;
        private readonly Dictionary<byte[], int> _deferredOwnerReferences = new();
        private long _deferredOwnerBytes;
        private readonly List<McapMessage> _defaultTickBuffer = new();
        private readonly List<McapMessage> _scanBoundaryCandidates = new();
        private static readonly IComparer<McapMessage> MessageComparer =
            Comparer<McapMessage>.Create(CompareMessages);
        private readonly Dictionary<ushort, SnapshotCandidate> _snapshotLatestByChannel = new();
        private List<McapChunkIndex> _snapshotChunkIndexesByDescendingEndTime;
        private readonly IFoxgloveLogger _logger;

        /// <summary>
        /// Counters from the most recent <see cref="Snapshot"/> call.
        /// </summary>
        public SnapshotMetrics LastSnapshotMetrics { get; private set; }
        /// <summary>Counters from the most recent <see cref="History"/> call.</summary>
        public HistoryMetrics LastHistoryMetrics { get; private set; }

        // Per-chunk state
        /// <summary>
        /// Index of the chunk currently being read, or -1 if none loaded.
        /// </summary>
        private int _currentChunkIdx = -1;
        private ulong _currentChunkStartOffset;
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
        private const long DefaultMaxHistorySpoolBytes = (long)McapReader.DefaultChunkUncompressedSizeLimit;
        private const int DefaultMaxDeferredMessages = 100000;
        // Retry entries contain only bounded scalar metadata and are not
        // counted as owner-retained payloads. Keep a separate hard ceiling so
        // a file with an unbounded number of rejected future records cannot
        // grow the metadata queue indefinitely.
        private const int DefaultMaxDeferredRetryRecords = 100000;
        private long _maxDeferredOwnerBytes = DefaultMaxDeferredOwnerBytes;
        private long _maxHistorySpoolBytes = DefaultMaxHistorySpoolBytes;
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
        /// Maximum compressed-history spool bytes retained for bounded queries.
        /// A non-positive value restores the default bound.
        /// </summary>
        public long MaxHistorySpoolBytes
        {
            get => _maxHistorySpoolBytes;
            set => _maxHistorySpoolBytes = value > 0 ? value : DefaultMaxHistorySpoolBytes;
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
            if (!IsLoaded || _reader == null || _summary == null || string.IsNullOrEmpty(name))
                return null;

            if (_summary.MetadataIndexes != null && _summary.MetadataIndexes.Count > 0)
            {
                var matchingIndex = false;
                foreach (var index in _summary.MetadataIndexes)
                {
                    if (!string.Equals(index?.Name, name, StringComparison.Ordinal))
                        continue;

                    matchingIndex = true;
                    var metadata = _reader.ReadMetadataAt(index.Offset);
                    if (metadata != null && string.Equals(metadata.Name, name, StringComparison.Ordinal))
                        return metadata;
                }

                if (!matchingIndex && HasCompleteMetadataIndex())
                    return null;

                return FindMetadataAfterIndexMiss(name);
            }

            if (HasCompleteMetadataIndex())
                return null;

            return FindMetadataAfterIndexMiss(name);
        }

        private McapMetadata FindMetadataAfterIndexMiss(string name)
        {
            if (!_metadataFallbackScanComplete)
            {
                _metadataFallbackCache = _reader.BuildMetadataIndexInDataSection(_summary.DataSectionEndOffset);
                _metadataFallbackScanComplete = true;
            }

            return _metadataFallbackCache.TryGetValue(name, out var fallback)
                ? _reader.ReadMetadataAt(fallback.Offset)
                : null;
        }

        private bool HasCompleteMetadataIndex()
        {
            var metadataCount = _summary.Statistics?.MetadataCount;
            var indexes = _summary.MetadataIndexes;
            if (!metadataCount.HasValue
                || (ulong)(indexes?.Count ?? 0) != metadataCount.Value)
                return false;
            if (indexes == null || indexes.Count == 0)
                return true;

            var offsets = new HashSet<ulong>();
            try
            {
                for (var i = 0; i < indexes.Count; i++)
                {
                    var index = indexes[i];
                    if (index == null
                        || !offsets.Add(index.Offset))
                        return false;
                    var metadata = _reader.ReadMetadataAt(index.Offset);
                    if (metadata == null
                        || !string.Equals(metadata.Name, index.Name, StringComparison.Ordinal))
                        return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
