// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Local-first MCAP DataLoader facade over McapIndexedReader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Local file-backed DataLoader-shaped facade for summary, query, and
    /// backfill access over one indexed MCAP file.
    /// </summary>
    public sealed partial class McapDataLoader : IDisposable
    {
        private readonly McapIndexedReader _reader;
        private readonly McapSequentialReadLimits _sequentialReadLimits;
        private readonly long _sourceLengthBytes;
        private McapDataLoaderInitialization _initialization;
        private Dictionary<ushort, McapSchema> _schemaMap;
        private Dictionary<ushort, McapChannel> _channelMap;
        private Dictionary<string, List<ushort>> _topicChannelMap;
        private HashSet<ushort> _knownChannelIds;
        private bool _hasCachedDecodeRegistry;
        private McapDecodeOptions _cachedDecodeOptions;
        private int _cachedDecodeOptionsFingerprint;
        private McapDecodeRegistry _cachedDecodeRegistry;
        private int _lazyEnumerationActive;
        private bool _disposed;

        /// <summary>Opens a local MCAP file and owns the file stream.</summary>
        public McapDataLoader(string path)
            : this(path, null)
        {
        }

        /// <summary>Opens a local MCAP file with explicit sequential fallback limits.</summary>
        public McapDataLoader(string path, McapSequentialReadLimits sequentialReadLimits)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            _sequentialReadLimits = sequentialReadLimits ?? McapSequentialReadLimits.Default;
            var stream = File.OpenRead(path);
            _sourceLengthBytes = stream.CanSeek ? stream.Length : -1L;
            try
            {
                _reader = new McapIndexedReader(stream, false, _sequentialReadLimits);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Wraps a seekable MCAP stream with the Phase 68 indexed-reader boundary.</summary>
        public McapDataLoader(Stream stream, bool leaveOpen = false)
            : this(stream, leaveOpen, null)
        {
        }

        /// <summary>Wraps a seekable MCAP stream with explicit sequential fallback limits.</summary>
        public McapDataLoader(
            Stream stream,
            bool leaveOpen,
            McapSequentialReadLimits sequentialReadLimits)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            _sourceLengthBytes = stream.CanSeek ? stream.Length : -1L;
            _sequentialReadLimits = sequentialReadLimits ?? McapSequentialReadLimits.Default;
            _reader = new McapIndexedReader(stream, leaveOpen, _sequentialReadLimits);
        }

        /// <summary>Reads and caches summary-derived initialization metadata.</summary>
        public McapDataLoaderInitialization Initialize()
        {
            ThrowIfDisposed();
            ThrowIfLazyEnumerationActive();
            if (_initialization != null)
                return _initialization;

            _schemaMap = BuildSchemaMap(_reader.Schemas);
            BuildChannelAndQueryMaps(_reader.Channels, out _channelMap, out _topicChannelMap, out _knownChannelIds);
            _initialization = new McapDataLoaderInitialization();
            McapDataLoaderInitializationBuilder.AddSchemas(_initialization, _reader.Schemas);
            McapDataLoaderInitializationBuilder.AddChannels(_initialization, _reader.Channels, _reader.Summary?.Statistics);
            McapDataLoaderInitializationBuilder.AddTimeRange(_initialization, _reader.Summary);
            McapDataLoaderInitializationBuilder.AddMetadataIndexes(_initialization, _reader.MetadataIndexes);
            McapDataLoaderInitializationBuilder.AddAttachmentIndexes(_initialization, _reader.AttachmentIndexes);
            McapDataLoaderInitializationBuilder.AddSummaryCounts(_initialization, _reader.Summary?.Statistics);
            AddSequentialFallbackProblems(_initialization);
            AddSchemaReferenceProblems(_initialization);
            AddFoxRunSchemaMetadataProblems(_initialization);
            return _initialization;
        }

        /// <summary>
        /// Creates a deterministic log-time ordered iterator over matching raw messages.
        /// This is an eager snapshot API: matching messages are materialized before the
        /// returned enumerable is exposed, not streamed lazily from the MCAP reader.
        /// </summary>
        public IEnumerable<McapDataLoaderMessage> CreateIterator(McapDataLoaderQuery query)
        {
            ThrowIfDisposed();
            ThrowIfLazyEnumerationActive();
            Initialize();
            if (!QueryCanMatch(query?.ChannelIds, query?.Topics))
                return Array.Empty<McapDataLoaderMessage>();

            var messages = _reader.ReadMessages(ToReadOptions(query));
            var result = new List<McapDataLoaderMessage>(messages.Count);
            for (var i = 0; i < messages.Count; i++)
                result.Add(ToDataLoaderMessage(messages[i]));
            return result;
        }

        /// <summary>
        /// Creates a forward-only lazy iterator over matching raw messages in
        /// indexed file/chunk order. The returned enumerable can be enumerated
        /// only once and does not provide the eager iterator's log-time sorting.
        /// Do not interleave lazy enumeration with other reads on this loader;
        /// the underlying indexed reader shares one seekable stream. Dispose
        /// this loader to release the file handle, even if the returned lazy
        /// enumerable is never consumed.
        /// </summary>
        public IEnumerable<McapDataLoaderMessage> CreateLazyIterator(McapDataLoaderQuery query)
        {
            ThrowIfDisposed();
            Initialize();
            if (!QueryCanMatch(query?.ChannelIds, query?.Topics))
                return CreateEmptyLazyIterator();

            return new McapLazyMessageEnumerable(this, ToLazyReadOptions(query));
        }

        /// <summary>
        /// Creates a forward-only lazy decoded iterator over matching messages.
        /// The returned enumerable can be enumerated only once and keeps the
        /// same ordering and stream-ownership constraints as
        /// <see cref="CreateLazyIterator"/>.
        /// </summary>
        public IEnumerable<McapDecodedMessage> CreateLazyDecodedIterator(
            McapDataLoaderQuery query,
            McapDecodeOptions options = null)
        {
            ThrowIfDisposed();
            Initialize();
            if (!QueryCanMatch(query?.ChannelIds, query?.Topics))
                return CreateEmptyLazyDecodedIterator();

            return new McapSinglePassEnumerable<McapDecodedMessage>(
                nameof(McapDataLoader) + "." + nameof(CreateLazyDecodedIterator),
                () => EnumerateLazyDecodedMessages(ToLazyReadOptions(query), options).GetEnumerator());
        }

        /// <summary>
        /// Creates an opt-in decoded iterator over matching messages while
        /// preserving each raw MCAP payload as the source of truth.
        /// Like <see cref="CreateIterator"/>, this materializes the raw result set
        /// before returning the decoded enumerable.
        /// </summary>
        public IEnumerable<McapDecodedMessage> CreateDecodedIterator(
            McapDataLoaderQuery query,
            McapDecodeOptions options = null)
        {
            ThrowIfDisposed();
            ThrowIfLazyEnumerationActive();
            Initialize();
            var registry = GetDecodeRegistry(options);
            var decodedMessages = new List<McapDecodedMessage>();
            foreach (var raw in CreateIterator(query))
            {
                registry.TryDecode(raw, out var decoded);
                decodedMessages.Add(decoded);
            }

            return decodedMessages;
        }

        /// <summary>
        /// Try to decode one raw DataLoader message with the configured decoder
        /// factories. The raw message is returned inside <paramref name="decoded"/>.
        /// </summary>
        public bool TryDecodeMessage(
            McapDataLoaderMessage message,
            McapDecodeOptions options,
            out McapDecodedMessage decoded)
        {
            ThrowIfDisposed();
            ThrowIfLazyEnumerationActive();
            Initialize();
            return GetDecodeRegistry(options).TryDecode(message, out decoded);
        }

        /// <summary>Gets the latest message per selected channel at or before the requested time.</summary>
        public IReadOnlyList<McapDataLoaderMessage> GetBackfill(McapDataLoaderBackfillQuery query)
        {
            ThrowIfDisposed();
            ThrowIfLazyEnumerationActive();
            Initialize();

            if (!QueryCanMatch(query?.ChannelIds, query?.Topics))
                return Array.Empty<McapDataLoaderMessage>();

            var selected = _reader.ReadLatestBefore(new McapReadOptions
            {
                EndTimeNs = query?.TimeNs ?? ulong.MaxValue,
                ChannelIds = CopyUShorts(query?.ChannelIds),
                Topics = CopyStrings(query?.Topics)
            });
            var result = new List<McapDataLoaderMessage>(selected.Count);
            for (var i = 0; i < selected.Count; i++)
                result.Add(ToDataLoaderMessage(selected[i]));
            return result;
        }

        /// <summary>Disposes the underlying indexed reader and any owned stream.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cachedDecodeRegistry = null;
            _cachedDecodeOptions = null;
            _hasCachedDecodeRegistry = false;
            _reader.Dispose();
        }

        private static Dictionary<ushort, McapSchema> BuildSchemaMap(IReadOnlyList<McapSchema> schemas)
        {
            var map = new Dictionary<ushort, McapSchema>();
            if (schemas == null)
                return map;

            for (var i = 0; i < schemas.Count; i++)
            {
                var schema = schemas[i];
                if (schema != null)
                    map[schema.Id] = schema;
            }

            return map;
        }

        private static void BuildChannelAndQueryMaps(
            IReadOnlyList<McapChannel> channels,
            out Dictionary<ushort, McapChannel> channelMap,
            out Dictionary<string, List<ushort>> topicChannelMap,
            out HashSet<ushort> knownChannelIds)
        {
            channelMap = new Dictionary<ushort, McapChannel>();
            topicChannelMap = new Dictionary<string, List<ushort>>(StringComparer.Ordinal);
            knownChannelIds = new HashSet<ushort>();
            if (channels == null)
                return;

            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel == null)
                    continue;

                channelMap[channel.Id] = channel;
                knownChannelIds.Add(channel.Id);
                var topic = channel.Topic ?? string.Empty;
                if (!topicChannelMap.TryGetValue(topic, out var ids))
                {
                    ids = new List<ushort>();
                    topicChannelMap[topic] = ids;
                }

                ids.Add(channel.Id);
            }
        }

        private bool QueryCanMatch(List<ushort> channelIds, List<string> topics)
        {
            var hasChannelFilter = channelIds != null && channelIds.Count > 0;
            var hasTopicFilter = topics != null && topics.Count > 0;
            if (!hasChannelFilter && !hasTopicFilter)
                return true;

            if (hasChannelFilter && hasTopicFilter)
                return QueryCanMatchChannelAndTopic(channelIds, topics);

            if (hasChannelFilter && _knownChannelIds != null)
            {
                for (var i = 0; i < channelIds.Count; i++)
                {
                    if (_knownChannelIds.Contains(channelIds[i]))
                        return true;
                }
            }

            if (hasTopicFilter && _topicChannelMap != null)
            {
                for (var i = 0; i < topics.Count; i++)
                {
                    var topic = topics[i] ?? string.Empty;
                    if (_topicChannelMap.TryGetValue(topic, out var ids) && ids.Count > 0)
                        return true;
                }
            }

            return false;
        }

        private bool QueryCanMatchChannelAndTopic(List<ushort> channelIds, List<string> topics)
        {
            if (_channelMap == null)
                return false;

            for (var i = 0; i < channelIds.Count; i++)
            {
                if (!_channelMap.TryGetValue(channelIds[i], out var channel))
                    continue;

                var channelTopic = channel.Topic ?? string.Empty;
                for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
                {
                    if (string.Equals(channelTopic, topics[topicIndex] ?? string.Empty, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        private McapDataLoaderMessage ToDataLoaderMessage(McapMessage message)
        {
            var channel = _channelMap != null && _channelMap.TryGetValue(message.ChannelId, out var found)
                ? found
                : null;

            return new McapDataLoaderMessage
            {
                ChannelId = message.ChannelId,
                SchemaId = channel?.SchemaId ?? 0,
                Topic = channel?.Topic ?? string.Empty,
                MessageEncoding = channel?.MessageEncoding ?? string.Empty,
                Sequence = message.Sequence,
                LogTime = message.LogTime,
                PublishTime = message.PublishTime,
                Data = message.Data ?? Array.Empty<byte>()
            };
        }

        private McapDecodeRegistry CreateDecodeRegistry(McapDecodeOptions options)
        {
            return new McapDecodeRegistry(
                options ?? new McapDecodeOptions(),
                _schemaMap,
                _channelMap);
        }

        private McapDecodeRegistry GetDecodeRegistry(McapDecodeOptions options)
        {
            var fingerprint = ComputeDecodeOptionsFingerprint(options);
            if (_hasCachedDecodeRegistry
                && ReferenceEquals(_cachedDecodeOptions, options)
                && _cachedDecodeOptionsFingerprint == fingerprint)
                return _cachedDecodeRegistry;

            _cachedDecodeRegistry = CreateDecodeRegistry(options);
            _cachedDecodeOptions = options;
            _cachedDecodeOptionsFingerprint = fingerprint;
            _hasCachedDecodeRegistry = true;
            return _cachedDecodeRegistry;
        }

        internal IEnumerable<McapDataLoaderMessage> EnumerateLazyMessages(McapReadOptions options)
        {
            ThrowIfDisposed();
            BeginLazyEnumeration();
            try
            {
                foreach (var message in _reader.EnumerateMessages(options))
                {
                    ThrowIfDisposed();
                    yield return ToDataLoaderMessage(message);
                }
            }
            finally
            {
                EndLazyEnumeration();
            }
        }

        private IEnumerable<McapDecodedMessage> EnumerateLazyDecodedMessages(McapReadOptions options, McapDecodeOptions decodeOptions)
        {
            var registry = GetDecodeRegistry(decodeOptions);
            foreach (var raw in EnumerateLazyMessages(options))
            {
                registry.TryDecode(raw, out var decoded);
                yield return decoded;
            }
        }

        private static int ComputeDecodeOptionsFingerprint(McapDecodeOptions options)
        {
            if (options == null)
                return 0;

            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (options.UseBuiltInDecoders ? 1 : 0);
                hash = hash * 31 + (int)options.FailurePolicy;
                var factories = options.DecoderFactories;
                if (factories == null)
                    return hash * 31;

                hash = hash * 31 + factories.Count;
                for (var i = 0; i < factories.Count; i++)
                    hash = hash * 31 + (factories[i] == null ? 0 : RuntimeHelpers.GetHashCode(factories[i]));
                return hash;
            }
        }

        private static McapReadOptions ToReadOptions(McapDataLoaderQuery query)
        {
            query = query ?? new McapDataLoaderQuery();
            return new McapReadOptions
            {
                StartTimeNs = query.StartTimeNs,
                EndTimeNs = query.EndTimeNs,
                ChannelIds = CopyUShorts(query.ChannelIds),
                Topics = CopyStrings(query.Topics),
                MaxMessages = query.MaxMessages
            };
        }

        private static McapReadOptions ToLazyReadOptions(McapDataLoaderQuery query)
        {
            var options = ToReadOptions(query);
            options.Order = McapReadOrder.FileOrder;
            return options;
        }

        private static IEnumerable<McapDataLoaderMessage> CreateEmptyLazyIterator()
            => new McapSinglePassEnumerable<McapDataLoaderMessage>(
                nameof(McapDataLoader) + "." + nameof(CreateLazyIterator),
                () => ((IEnumerable<McapDataLoaderMessage>)Array.Empty<McapDataLoaderMessage>()).GetEnumerator());

        private static IEnumerable<McapDecodedMessage> CreateEmptyLazyDecodedIterator()
            => new McapSinglePassEnumerable<McapDecodedMessage>(
                nameof(McapDataLoader) + "." + nameof(CreateLazyDecodedIterator),
                () => ((IEnumerable<McapDecodedMessage>)Array.Empty<McapDecodedMessage>()).GetEnumerator());

        private static List<ushort> CopyUShorts(List<ushort> source)
            => source == null || source.Count == 0 ? null : new List<ushort>(source);

        private static List<string> CopyStrings(List<string> source)
            => source == null || source.Count == 0 ? null : new List<string>(source);

        private void BeginLazyEnumeration()
        {
            if (Interlocked.Exchange(ref _lazyEnumerationActive, 1) != 0)
                throw new InvalidOperationException("McapDataLoader allows only one active lazy enumeration per loader.");
        }

        private void EndLazyEnumeration()
        {
            Volatile.Write(ref _lazyEnumerationActive, 0);
        }

        private void ThrowIfLazyEnumerationActive()
        {
            if (Volatile.Read(ref _lazyEnumerationActive) != 0)
                throw new InvalidOperationException("Cannot start another MCAP read while a lazy enumeration is active on this loader.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McapDataLoader));
        }
    }
}
