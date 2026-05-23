// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Public indexed-reader facade for local MCAP summary and query APIs.

using System;
using System.Collections.Generic;
using System.IO;

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
        private bool _disposed;

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
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!_stream.CanSeek)
                throw new NotSupportedException("McapIndexedReader requires a seekable stream.");

            _ownsStream = !leaveOpen;
            _sequentialReadLimits = sequentialReadLimits ?? McapSequentialReadLimits.Default;
            _sequentialReadLimits.Validate();
            _reader = new McapReader(_stream);
            _summary = _reader.ReadSummary();
        }

        /// <summary>
        /// Opens a file-backed indexed reader and transfers ownership of the
        /// file stream to the returned reader.
        /// </summary>
        /// <param name="filePath">Path to a local MCAP file.</param>
        /// <returns>An indexed reader for the file.</returns>
        public static McapIndexedReader OpenRead(
            string filePath,
            McapSequentialReadLimits sequentialReadLimits = null)
        {
            var stream = File.OpenRead(filePath);
            try
            {
                return new McapIndexedReader(stream, false, sequentialReadLimits);
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
        public McapFileSummary Summary => _summary;

        /// <summary>
        /// Gets schemas from the cached summary.
        /// </summary>
        public IReadOnlyList<McapSchema> Schemas => _summary.Schemas;

        /// <summary>
        /// Gets channels from the cached summary.
        /// </summary>
        public IReadOnlyList<McapChannel> Channels => _summary.Channels;

        /// <summary>
        /// Gets metadata indexes from the cached summary.
        /// </summary>
        public IReadOnlyList<McapMetadataIndex> MetadataIndexes => _summary.MetadataIndexes;

        /// <summary>
        /// Gets attachment indexes from the cached summary.
        /// </summary>
        public IReadOnlyList<McapAttachmentIndex> AttachmentIndexes => _summary.AttachmentIndexes;

        /// <summary>
        /// Reads messages matching the supplied options into a result list.
        /// </summary>
        /// <param name="options">Optional query options. <c>null</c> means all indexed messages.</param>
        /// <param name="result">Optional reusable result list that will be cleared.</param>
        /// <returns>The filled result list.</returns>
        public List<McapMessage> ReadMessages(McapReadOptions options = null, List<McapMessage> result = null)
        {
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
                return ReadSequentialMessages(options, result);
            }

            var selectedChannelIds = ResolveSelectedChannelIds(options);
            if (selectedChannelIds != null && selectedChannelIds.Count == 0)
                return result;

            foreach (var chunkIndex in chunkIndexes)
            {
                if (chunkIndex.MessageEndTime < options.StartTimeNs || chunkIndex.MessageStartTime > options.EndTimeNs)
                    continue;

                if (selectedChannelIds != null &&
                    chunkIndex.MessageIndexOffsets != null &&
                    chunkIndex.MessageIndexOffsets.Count > 0 &&
                    !ContainsAnySelectedChannel(chunkIndex.MessageIndexOffsets, selectedChannelIds))
                    continue;

                var uncompressed = _reader.ReadChunkRecords(
                    chunkIndex.ChunkStartOffset,
                    chunkIndex.ChunkLength,
                    out var crcValid);
                if (!crcValid)
                    throw new InvalidDataException("MCAP chunk CRC mismatch.");

                var messages = _reader.ReadChunkMessages(uncompressed);
                for (var i = 0; i < messages.Count; i++)
                {
                    var message = messages[i];
                    if (message.LogTime < options.StartTimeNs || message.LogTime > options.EndTimeNs)
                        continue;
                    if (selectedChannelIds != null && !selectedChannelIds.Contains(message.ChannelId))
                        continue;

                    result.Add(message);
                }
            }

            result.Sort(CompareMessages);
            if (options.MaxMessages > 0 && result.Count > options.MaxMessages)
                result.RemoveRange(0, result.Count - options.MaxMessages);

            return result;
        }

        private List<McapMessage> ReadSequentialMessages(McapReadOptions options, List<McapMessage> result)
        {
            var selectedChannelIds = ResolveSelectedChannelIds(options);
            if (selectedChannelIds != null && selectedChannelIds.Count == 0)
                return result;

            var messages = _summary.SequentialMessages;
            if (messages == null || messages.Count == 0)
            {
                messages = _reader.ReadSequentialMessages(
                    _summary.DataSectionEndOffset,
                    sequentialLimits: _sequentialReadLimits);
                messages.Sort(CompareMessages);
                _summary.SequentialMessages = messages;
            }

            for (var i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (message.LogTime < options.StartTimeNs || message.LogTime > options.EndTimeNs)
                    continue;
                if (selectedChannelIds != null && !selectedChannelIds.Contains(message.ChannelId))
                    continue;

                result.Add(message);
            }

            result.Sort(CompareMessages);
            if (options.MaxMessages > 0 && result.Count > options.MaxMessages)
                result.RemoveRange(0, result.Count - options.MaxMessages);

            return result;
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
                var expectedCount = ExpectedLatestChannelCount(selectedChannelIds);
                ReadLatestBeforeSequential(options, selectedChannelIds, expectedCount, latestByChannel);
            }
            else
            {
                var orderedChunkIndexes = new List<McapChunkIndex>(chunkIndexes);
                orderedChunkIndexes.Sort((left, right) => right.MessageEndTime.CompareTo(left.MessageEndTime));
                var expectedCount = ExpectedLatestIndexedChannelCount(options, selectedChannelIds, orderedChunkIndexes);
                ReadLatestBeforeIndexed(options, selectedChannelIds, expectedCount, orderedChunkIndexes, latestByChannel);
            }

            result.AddRange(latestByChannel.Values);
            result.Sort(CompareLatestOutput);
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
                if (chunkIndex.MessageStartTime > options.EndTimeNs)
                    continue;
                if (chunkIndex.MessageEndTime < options.StartTimeNs)
                    continue;
                if (CanStopLatestScan(latestByChannel, expectedCount, chunkIndex.MessageEndTime))
                    break;
                if (selectedChannelIds != null &&
                    chunkIndex.MessageIndexOffsets != null &&
                    chunkIndex.MessageIndexOffsets.Count > 0 &&
                    !ContainsAnySelectedChannel(chunkIndex.MessageIndexOffsets, selectedChannelIds))
                    continue;

                var uncompressed = _reader.ReadChunkRecords(
                    chunkIndex.ChunkStartOffset,
                    chunkIndex.ChunkLength,
                    out var crcValid);
                if (!crcValid)
                    throw new InvalidDataException("MCAP chunk CRC mismatch.");

                var messages = _reader.ReadChunkMessages(uncompressed);
                for (var messageIndex = 0; messageIndex < messages.Count; messageIndex++)
                    ConsiderLatestCandidate(messages[messageIndex], options, selectedChannelIds, latestByChannel);
            }
        }

        private void ReadLatestBeforeSequential(
            McapReadOptions options,
            HashSet<ushort> selectedChannelIds,
            int expectedCount,
            Dictionary<ushort, McapMessage> latestByChannel)
        {
            var messages = _summary.SequentialMessages;
            if (messages == null || messages.Count == 0)
            {
                messages = _reader.ReadSequentialMessages(
                    _summary.DataSectionEndOffset,
                    sequentialLimits: _sequentialReadLimits);
                messages.Sort(CompareMessages);
                _summary.SequentialMessages = messages;
            }

            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                if (message.LogTime > options.EndTimeNs)
                    continue;
                if (message.LogTime < options.StartTimeNs)
                    break;
                if (CanStopLatestScan(latestByChannel, expectedCount, message.LogTime))
                    break;

                ConsiderLatestCandidate(message, options, selectedChannelIds, latestByChannel);
            }
        }

        /// <summary>
        /// Reads an attachment record using an attachment index entry.
        /// </summary>
        /// <param name="index">Attachment index entry from <see cref="AttachmentIndexes"/>.</param>
        /// <returns>The decoded attachment.</returns>
        public McapAttachment ReadAttachment(McapAttachmentIndex index)
        {
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
            if (index == null)
                throw new ArgumentNullException(nameof(index));

            return _reader.ReadMetadataAt(index.Offset);
        }

        /// <summary>
        /// Releases the owned stream when this reader owns it.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_ownsStream)
                _stream.Dispose();
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
                if (chunkIndex.MessageStartTime > options.EndTimeNs ||
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

        private static void ConsiderLatestCandidate(
            McapMessage message,
            McapReadOptions options,
            HashSet<ushort> selectedChannelIds,
            Dictionary<ushort, McapMessage> latestByChannel)
        {
            if (message.LogTime < options.StartTimeNs || message.LogTime > options.EndTimeNs)
                return;
            if (selectedChannelIds != null && !selectedChannelIds.Contains(message.ChannelId))
                return;
            if (!latestByChannel.TryGetValue(message.ChannelId, out var current) ||
                CompareLatestCandidate(message, current) > 0)
                latestByChannel[message.ChannelId] = message;
        }

        private static bool CanStopLatestScan(
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

        private static bool ContainsAnySelectedChannel(
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

        private static int CompareMessages(McapMessage left, McapMessage right)
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

        private static int CompareLatestCandidate(McapMessage left, McapMessage right)
        {
            var cmp = left.LogTime.CompareTo(right.LogTime);
            if (cmp != 0)
                return cmp;

            cmp = left.Sequence.CompareTo(right.Sequence);
            if (cmp != 0)
                return cmp;

            return left.PublishTime.CompareTo(right.PublishTime);
        }

        private static int CompareLatestOutput(McapMessage left, McapMessage right)
        {
            var cmp = left.ChannelId.CompareTo(right.ChannelId);
            if (cmp != 0)
                return cmp;

            return CompareLatestCandidate(left, right);
        }
    }
}
