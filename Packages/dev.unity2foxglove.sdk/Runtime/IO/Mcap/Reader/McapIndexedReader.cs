// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Public indexed-reader facade for local MCAP summary and query APIs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Summary-first local MCAP reader that exposes indexed records and
    /// filtered message queries.
    /// </summary>
    public sealed class McapIndexedReader : IDisposable
    {
        private readonly Stream _stream;
        private readonly McapReader _reader;
        private readonly McapFileSummary _summary;
        private readonly bool _ownsStream;
        private readonly McapSequentialReadLimits _sequentialReadLimits;
        private readonly object _chunkIndexCacheGate = new object();
        private List<McapChunkIndex> _chunkIndexesByDescendingEndTime;
        private int _disposed;

        /// <summary>
        /// Initializes a new indexed reader over a seekable MCAP stream.
        /// </summary>
        /// <param name="stream">Seekable MCAP stream.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when disposed.</param>
        public McapIndexedReader(Stream stream, bool leaveOpen = false)
            : this(stream, leaveOpen, null)
        {
        }

        /// <summary>
        /// Initializes a new indexed reader with explicit memory limits for no-index sequential fallback.
        /// </summary>
        /// <param name="stream">Seekable MCAP stream.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when disposed.</param>
        /// <param name="sequentialReadLimits">Memory limits for no-index sequential fallback.</param>
        public McapIndexedReader(
            Stream stream,
            bool leaveOpen,
            McapSequentialReadLimits sequentialReadLimits)
            : this(stream, leaveOpen, sequentialReadLimits, null)
        {
        }

        /// <summary>
        /// Initializes a new indexed reader with explicit memory and summary scan options.
        /// </summary>
        /// <param name="stream">Seekable MCAP stream.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when disposed.</param>
        /// <param name="sequentialReadLimits">Memory limits for no-index sequential fallback.</param>
        /// <param name="readOptions">Options used while reading summaryless inventories.</param>
        public McapIndexedReader(
            Stream stream,
            bool leaveOpen,
            McapSequentialReadLimits sequentialReadLimits,
            McapReadOptions readOptions)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!_stream.CanSeek)
                throw new NotSupportedException("McapIndexedReader requires a seekable stream.");

            _ownsStream = !leaveOpen;
            _sequentialReadLimits = sequentialReadLimits ?? McapSequentialReadLimits.Default;
            _sequentialReadLimits.Validate();
            _reader = new McapReader(_stream);
            var summaryOptions = readOptions ?? new McapReadOptions();
            _summary = _reader.ReadSummary(
                validateCrcs: summaryOptions.ValidateCrcs,
                chunkUncompressedSizeLimit: summaryOptions.ChunkUncompressedSizeLimit);
        }

        /// <summary>
        /// Opens a file-backed indexed reader and transfers ownership of the
        /// file stream to the returned reader.
        /// </summary>
        /// <param name="filePath">Path to a local MCAP file.</param>
        /// <returns>An indexed reader for the file.</returns>
        public static McapIndexedReader OpenRead(string filePath)
            => OpenRead(filePath, null);

        /// <summary>
        /// Opens a file-backed indexed reader with explicit memory limits for no-index sequential fallback.
        /// </summary>
        /// <param name="filePath">Path to a local MCAP file.</param>
        /// <param name="sequentialReadLimits">Memory limits for no-index sequential fallback.</param>
        /// <returns>An indexed reader for the file.</returns>
        public static McapIndexedReader OpenRead(
            string filePath,
            McapSequentialReadLimits sequentialReadLimits)
            => OpenRead(filePath, sequentialReadLimits, null);

        /// <summary>
        /// Opens a file-backed indexed reader with explicit memory and summary scan options.
        /// </summary>
        /// <param name="filePath">Path to a local MCAP file.</param>
        /// <param name="sequentialReadLimits">Memory limits for no-index sequential fallback.</param>
        /// <param name="readOptions">Options used while reading summaryless inventories.</param>
        /// <returns>An indexed reader for the file.</returns>
        public static McapIndexedReader OpenRead(
            string filePath,
            McapSequentialReadLimits sequentialReadLimits,
            McapReadOptions readOptions)
        {
            var stream = File.OpenRead(filePath);
            try
            {
                return new McapIndexedReader(stream, false, sequentialReadLimits, readOptions);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the cached MCAP file summary.
        /// </summary>
        public McapFileSummary Summary
        {
            get
            {
                ThrowIfDisposed();
                return _summary;
            }
        }

        /// <summary>
        /// Gets schemas from the cached summary.
        /// </summary>
        public IReadOnlyList<McapSchema> Schemas
        {
            get
            {
                ThrowIfDisposed();
                return _summary.Schemas;
            }
        }

        /// <summary>
        /// Gets channels from the cached summary.
        /// </summary>
        public IReadOnlyList<McapChannel> Channels
        {
            get
            {
                ThrowIfDisposed();
                return _summary.Channels;
            }
        }

        /// <summary>
        /// Gets metadata indexes from the cached summary.
        /// </summary>
        public IReadOnlyList<McapMetadataIndex> MetadataIndexes
        {
            get
            {
                ThrowIfDisposed();
                return _summary.MetadataIndexes;
            }
        }

        /// <summary>
        /// Gets attachment indexes from the cached summary.
        /// </summary>
        public IReadOnlyList<McapAttachmentIndex> AttachmentIndexes
        {
            get
            {
                ThrowIfDisposed();
                return _summary.AttachmentIndexes;
            }
        }

        /// <summary>
        /// Reads messages matching the supplied options into a result list.
        /// </summary>
        /// <param name="options">Optional query options. <c>null</c> means all indexed messages.</param>
        /// <param name="result">Optional reusable result list that will be cleared.</param>
        /// <returns>The filled result list.</returns>
        public List<McapMessage> ReadMessages(McapReadOptions options = null, List<McapMessage> result = null)
        {
            ThrowIfDisposed();
            options = options ?? new McapReadOptions();
            if (result == null)
                result = new List<McapMessage>();
            else
                result.Clear();

            if (options.EndTimeNs < options.StartTimeNs)
                return result;

            var chunkIndexes = _summary.ChunkIndexes;
            if (chunkIndexes == null || chunkIndexes.Count == 0)
            {
                if (!options.AllowLinearFallback)
                    throw new InvalidOperationException("MCAP message query requires chunk indexes when AllowLinearFallback=false.");
                return ReadSequentialMessages(options, result);
            }

            foreach (var message in EnumerateIndexedMessagesInFileOrder(options))
                result.Add(message);

            McapIndexedReaderHelpers.ApplyOrderingAndLimit(result, options);

            return result;
        }

        /// <summary>
        /// Lazily enumerates indexed messages in file/chunk order.
        /// The returned enumerable is forward-only and can be enumerated once.
        /// Do not interleave this enumeration with other read calls on the same
        /// reader instance; all indexed reads share one seekable stream.
        /// </summary>
        /// <param name="options">Optional query options. Only <see cref="McapReadOrder.FileOrder"/> is supported.</param>
        /// <returns>A single-pass enumerable over matching messages.</returns>
        public IEnumerable<McapMessage> EnumerateMessages(McapReadOptions options = null)
        {
            ThrowIfDisposed();
            var lazyOptions = McapIndexedReaderHelpers.CreateLazyReadOptions(options);
            return new McapSinglePassEnumerable<McapMessage>(
                nameof(McapIndexedReader) + "." + nameof(EnumerateMessages),
                () => EnumerateMessagesCore(lazyOptions).GetEnumerator());
        }

        /// <summary>
        /// Lazily enumerates private records in file order.
        /// Do not interleave this enumeration with other read calls on the same
        /// reader instance; all indexed reads share one seekable stream.
        /// </summary>
        /// <param name="includeChunkRecords">Whether to include private records stored inside chunks.</param>
        /// <returns>A single-pass enumerable over private records.</returns>
        public IEnumerable<McapPrivateRecord> EnumeratePrivateRecords(bool includeChunkRecords = true)
        {
            ThrowIfDisposed();
            return new McapSinglePassEnumerable<McapPrivateRecord>(
                nameof(McapIndexedReader) + "." + nameof(EnumeratePrivateRecords),
                () => EnumeratePrivateRecordsCore(includeChunkRecords).GetEnumerator());
        }

        private List<McapMessage> ReadSequentialMessages(McapReadOptions options, List<McapMessage> result)
        {
            var selectedChannelIds = ResolveSelectedChannelIds(options);
            if (selectedChannelIds != null && selectedChannelIds.Count == 0)
                return result;

            var messages = ReadLinearMessages(options);
            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (!McapIndexedReaderHelpers.IsInTimeRange(message.LogTime, options))
                    continue;
                if (selectedChannelIds != null && !selectedChannelIds.Contains(message.ChannelId))
                    continue;

                result.Add(message);
            }

            McapIndexedReaderHelpers.ApplyOrderingAndLimit(result, options);

            return result;
        }

        private IEnumerable<McapMessage> EnumerateMessagesCore(McapReadOptions options)
        {
            ThrowIfDisposed();

            var yielded = 0;
            foreach (var message in EnumerateMessagesInFileOrder(options))
            {
                ThrowIfDisposed();
                yield return message;
                yielded++;
                if (options.MaxMessages > 0 && yielded >= options.MaxMessages)
                    yield break;
            }
        }

        private IEnumerable<McapMessage> EnumerateMessagesInFileOrder(McapReadOptions options)
        {
            var chunkIndexes = _summary.ChunkIndexes;
            if (chunkIndexes == null || chunkIndexes.Count == 0)
            {
                if (!options.AllowLinearFallback)
                    throw new InvalidOperationException("Lazy MCAP message enumeration requires chunk indexes when AllowLinearFallback=false.");

                return EnumerateSequentialMessagesInFileOrder(options);
            }

            return EnumerateIndexedMessagesInFileOrder(options);
        }

        private IEnumerable<McapMessage> EnumerateSequentialMessagesInFileOrder(McapReadOptions options)
        {
            var result = ReadSequentialMessages(options, new List<McapMessage>());
            for (var i = 0; i < result.Count; i++)
            {
                ThrowIfDisposed();
                yield return result[i];
            }
        }

        private IEnumerable<McapMessage> EnumerateIndexedMessagesInFileOrder(McapReadOptions options)
        {
            ThrowIfDisposed();
            if (options.EndTimeNs < options.StartTimeNs)
                yield break;

            var chunkIndexes = _summary.ChunkIndexes;
            if (chunkIndexes == null || chunkIndexes.Count == 0)
                throw new InvalidOperationException("Lazy MCAP message enumeration requires chunk indexes.");

            var selectedChannelIds = ResolveSelectedChannelIds(options);
            if (selectedChannelIds != null && selectedChannelIds.Count == 0)
                yield break;

            foreach (var chunkIndex in chunkIndexes)
            {
                ThrowIfDisposed();
                if (chunkIndex.MessageEndTime < options.StartTimeNs || McapIndexedReaderHelpers.IsAtOrPastEnd(chunkIndex.MessageStartTime, options))
                    continue;

                if (selectedChannelIds != null &&
                    chunkIndex.MessageIndexOffsets != null &&
                    chunkIndex.MessageIndexOffsets.Count > 0 &&
                    !McapIndexedReaderHelpers.ContainsAnySelectedChannel(chunkIndex.MessageIndexOffsets, selectedChannelIds))
                    continue;

                var uncompressed = _reader.ReadChunkRecords(
                    chunkIndex.ChunkStartOffset,
                    chunkIndex.ChunkLength,
                    out var crcValid,
                    options.ChunkUncompressedSizeLimit);
                if (!crcValid && options.ValidateCrcs)
                    throw new InvalidDataException("MCAP chunk CRC mismatch.");

                foreach (var message in _reader.EnumerateChunkMessages(uncompressed))
                {
                    ThrowIfDisposed();
                    if (!McapIndexedReaderHelpers.IsInTimeRange(message.LogTime, options))
                        continue;
                    if (selectedChannelIds != null && !selectedChannelIds.Contains(message.ChannelId))
                        continue;

                    yield return message;
                }
            }
        }

        private IEnumerable<McapPrivateRecord> EnumeratePrivateRecordsCore(bool includeChunkRecords)
        {
            ThrowIfDisposed();
            foreach (var record in _reader.EnumeratePrivateRecords(
                         _summary.DataSectionEndOffset,
                         includeChunkRecords: includeChunkRecords))
            {
                ThrowIfDisposed();
                yield return record;
            }
        }

        /// <summary>
        /// Reads the latest message at or before <see cref="McapReadOptions.EndTimeNs"/>
        /// for each selected channel.
        /// </summary>
        /// <param name="options">Topic/channel filters plus the target end time.</param>
        /// <param name="result">Optional reusable result list that will be cleared.</param>
        /// <returns>One latest-at message per matched channel, ordered by channel ID.</returns>
        public List<McapMessage> ReadLatestBefore(McapReadOptions options = null, List<McapMessage> result = null)
        {
            ThrowIfDisposed();
            options = options ?? new McapReadOptions();
            if (result == null)
                result = new List<McapMessage>();
            else
                result.Clear();

            if (options.EndTimeNs < options.StartTimeNs)
                return result;

            var selectedChannelIds = ResolveSelectedChannelIds(options);
            if (selectedChannelIds != null && selectedChannelIds.Count == 0)
                return result;

            var chunkIndexes = _summary.ChunkIndexes;
            var latestByChannel = new Dictionary<ushort, McapMessage>();
            if (chunkIndexes == null || chunkIndexes.Count == 0)
            {
                if (!options.AllowLinearFallback)
                    throw new InvalidOperationException("MCAP latest-at query requires chunk indexes when AllowLinearFallback=false.");
                var expectedCount = ExpectedLatestChannelCount(selectedChannelIds);
                ReadLatestBeforeSequential(options, selectedChannelIds, expectedCount, latestByChannel);
            }
            else
            {
                var orderedChunkIndexes = GetChunkIndexesByDescendingEndTime(chunkIndexes);
                var expectedCount = ExpectedLatestIndexedChannelCount(options, selectedChannelIds, orderedChunkIndexes);
                ReadLatestBeforeIndexed(options, selectedChannelIds, expectedCount, orderedChunkIndexes, latestByChannel);
            }

            result.AddRange(latestByChannel.Values);
            result.Sort(McapIndexedReaderHelpers.CompareLatestOutput);
            return result;
        }

        private void ReadLatestBeforeIndexed(
            McapReadOptions options,
            HashSet<ushort> selectedChannelIds,
            int expectedCount,
            List<McapChunkIndex> chunkIndexes,
            Dictionary<ushort, McapMessage> latestByChannel)
        {
            for (var i = 0; i < chunkIndexes.Count; i++)
            {
                var chunkIndex = chunkIndexes[i];
                if (McapIndexedReaderHelpers.IsAtOrPastEnd(chunkIndex.MessageStartTime, options))
                    continue;
                if (chunkIndex.MessageEndTime < options.StartTimeNs)
                    continue;
                if (McapIndexedReaderHelpers.CanStopLatestScan(latestByChannel, expectedCount, chunkIndex.MessageEndTime))
                    break;
                if (selectedChannelIds != null &&
                    chunkIndex.MessageIndexOffsets != null &&
                    chunkIndex.MessageIndexOffsets.Count > 0 &&
                    !McapIndexedReaderHelpers.ContainsAnySelectedChannel(chunkIndex.MessageIndexOffsets, selectedChannelIds))
                    continue;

                var uncompressed = _reader.ReadChunkRecords(
                    chunkIndex.ChunkStartOffset,
                    chunkIndex.ChunkLength,
                    out var crcValid,
                    options.ChunkUncompressedSizeLimit);
                if (!crcValid && options.ValidateCrcs)
                    throw new InvalidDataException("MCAP chunk CRC mismatch.");

                foreach (var message in _reader.EnumerateChunkMessages(uncompressed))
                    McapIndexedReaderHelpers.ConsiderLatestCandidate(message, options, selectedChannelIds, latestByChannel);
            }
        }

        private void ReadLatestBeforeSequential(
            McapReadOptions options,
            HashSet<ushort> selectedChannelIds,
            int expectedCount,
            Dictionary<ushort, McapMessage> latestByChannel)
        {
            _reader.VisitSequentialMessages(
                _summary.DataSectionEndOffset,
                message =>
                {
                    if (expectedCount > 0 && latestByChannel.Count >= expectedCount &&
                        latestByChannel.TryGetValue(message.ChannelId, out var current) &&
                        McapIndexedReaderHelpers.CompareLatestCandidate(current, message) >= 0)
                        return;

                    McapIndexedReaderHelpers.ConsiderLatestCandidate(message, options, selectedChannelIds, latestByChannel);
                },
                validateCrcs: options.ValidateCrcs,
                chunkUncompressedSizeLimit: options.ChunkUncompressedSizeLimit);
        }

        private IReadOnlyList<McapMessage> ReadLinearMessages(McapReadOptions options)
        {
            var scanOptions = new McapReadOptions
            {
                EndTimeNs = ulong.MaxValue,
                MaxMessages = 0,
                Order = McapReadOrder.FileOrder,
                AllowLinearFallback = true,
                ValidateCrcs = options.ValidateCrcs,
                ChunkUncompressedSizeLimit = options.ChunkUncompressedSizeLimit
            };

            _stream.Seek(0, SeekOrigin.Begin);
            using var streamingReader = new McapStreamingReader(_stream, leaveOpen: true, _sequentialReadLimits);
            return streamingReader.Read(scanOptions).Messages;
        }

        /// <summary>
        /// Reads an attachment record using an attachment index entry.
        /// </summary>
        /// <param name="index">Attachment index entry from <see cref="AttachmentIndexes"/>.</param>
        /// <returns>The decoded attachment.</returns>
        public McapAttachment ReadAttachment(McapAttachmentIndex index)
        {
            ThrowIfDisposed();
            if (index == null)
                throw new ArgumentNullException(nameof(index));

            return _reader.ReadAttachmentAt(index.Offset);
        }

        /// <summary>
        /// Reads a metadata record using a metadata index entry.
        /// </summary>
        /// <param name="index">Metadata index entry from <see cref="MetadataIndexes"/>.</param>
        /// <returns>The decoded metadata.</returns>
        public McapMetadata ReadMetadata(McapMetadataIndex index)
        {
            ThrowIfDisposed();
            if (index == null)
                throw new ArgumentNullException(nameof(index));

            return _reader.ReadMetadataAt(index.Offset);
        }

        /// <summary>
        /// Releases the owned stream when this reader owns it.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (_ownsStream)
                _stream.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(McapIndexedReader));
        }

        private List<McapChunkIndex> GetChunkIndexesByDescendingEndTime(IReadOnlyList<McapChunkIndex> chunkIndexes)
        {
            var cached = Volatile.Read(ref _chunkIndexesByDescendingEndTime);
            if (cached != null)
                return cached;

            lock (_chunkIndexCacheGate)
            {
                if (_chunkIndexesByDescendingEndTime != null)
                    return _chunkIndexesByDescendingEndTime;

                var ordered = new List<McapChunkIndex>(chunkIndexes);
                ordered.Sort((left, right) => right.MessageEndTime.CompareTo(left.MessageEndTime));
                Volatile.Write(ref _chunkIndexesByDescendingEndTime, ordered);
                return ordered;
            }
        }

        private HashSet<ushort> ResolveSelectedChannelIds(McapReadOptions options)
        {
            var hasTopics = options.Topics != null && options.Topics.Count > 0;
            var hasChannelIds = options.ChannelIds != null && options.ChannelIds.Count > 0;
            if (!hasTopics && !hasChannelIds)
                return null;

            var selected = new HashSet<ushort>();
            if (hasChannelIds)
            {
                for (var i = 0; i < options.ChannelIds.Count; i++)
                    selected.Add(options.ChannelIds[i]);
            }

            if (hasTopics)
            {
                var topicSet = new HashSet<string>(options.Topics, StringComparer.Ordinal);
                for (var i = 0; i < _summary.Channels.Count; i++)
                {
                    var channel = _summary.Channels[i];
                    if (topicSet.Contains(channel.Topic))
                        selected.Add(channel.Id);
                }
            }

            return selected;
        }

        private int ExpectedLatestChannelCount(HashSet<ushort> selectedChannelIds)
        {
            if (selectedChannelIds != null)
                return selectedChannelIds.Count;

            return _summary.Channels?.Count ?? 0;
        }

        private int ExpectedLatestIndexedChannelCount(
            McapReadOptions options,
            HashSet<ushort> selectedChannelIds,
            List<McapChunkIndex> chunkIndexes)
        {
            var expected = new HashSet<ushort>();
            for (var i = 0; i < chunkIndexes.Count; i++)
            {
                var chunkIndex = chunkIndexes[i];
                if (McapIndexedReaderHelpers.IsAtOrPastEnd(chunkIndex.MessageStartTime, options) ||
                    chunkIndex.MessageEndTime < options.StartTimeNs)
                    continue;

                if (chunkIndex.MessageIndexOffsets == null || chunkIndex.MessageIndexOffsets.Count == 0)
                    return ExpectedLatestChannelCount(selectedChannelIds);

                foreach (var channelId in chunkIndex.MessageIndexOffsets.Keys)
                {
                    if (selectedChannelIds == null || selectedChannelIds.Contains(channelId))
                        expected.Add(channelId);
                }
            }

            return expected.Count;
        }

    }
}
