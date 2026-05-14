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
        private bool _disposed;

        /// <summary>
        /// Initializes a new indexed reader over a seekable MCAP stream.
        /// </summary>
        /// <param name="stream">Seekable MCAP stream.</param>
        /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open when disposed.</param>
        public McapIndexedReader(Stream stream, bool leaveOpen = false)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!_stream.CanSeek)
                throw new NotSupportedException("McapIndexedReader requires a seekable stream.");

            _ownsStream = !leaveOpen;
            _reader = new McapReader(_stream);
            _summary = _reader.ReadSummary();
        }

        /// <summary>
        /// Opens a file-backed indexed reader and transfers ownership of the
        /// file stream to the returned reader.
        /// </summary>
        /// <param name="filePath">Path to a local MCAP file.</param>
        /// <returns>An indexed reader for the file.</returns>
        public static McapIndexedReader OpenRead(string filePath)
        {
            var stream = File.OpenRead(filePath);
            try
            {
                return new McapIndexedReader(stream);
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
                if (_summary.Statistics == null || _summary.Statistics.MessageCount == 0)
                    return result;

                throw new InvalidOperationException("MCAP message queries require chunk indexes.");
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
    }
}
