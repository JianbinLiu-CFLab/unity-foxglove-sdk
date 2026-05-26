// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Local-first MCAP DataLoader facade over McapIndexedReader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Local file-backed DataLoader-shaped facade for summary, query, and
    /// backfill access over one indexed MCAP file.
    /// </summary>
    public sealed class McapDataLoader : IDisposable
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
            _sourceLengthBytes = stream != null && stream.CanSeek ? stream.Length : -1L;
            _sequentialReadLimits = sequentialReadLimits ?? McapSequentialReadLimits.Default;
            _reader = new McapIndexedReader(stream, leaveOpen, _sequentialReadLimits);
        }

        /// <summary>Reads and caches summary-derived initialization metadata.</summary>
        public McapDataLoaderInitialization Initialize()
        {
            ThrowIfDisposed();
            if (_initialization != null)
                return _initialization;

            _schemaMap = BuildSchemaMap(_reader.Schemas);
            _channelMap = BuildChannelMap(_reader.Channels);
            BuildQueryMaps(_reader.Channels, out _topicChannelMap, out _knownChannelIds);
            _initialization = new McapDataLoaderInitialization();
            AddSchemas(_initialization, _reader.Schemas);
            AddChannels(_initialization, _reader.Channels, _reader.Summary?.Statistics);
            AddTimeRange(_initialization, _reader.Summary);
            AddMetadataIndexes(_initialization, _reader.MetadataIndexes);
            AddAttachmentIndexes(_initialization, _reader.AttachmentIndexes);
            AddSummaryCounts(_initialization, _reader.Summary?.Statistics);
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
            Initialize();
            if (!QueryCanMatch(query?.ChannelIds, query?.Topics))
                return new List<McapDataLoaderMessage>();

            var messages = _reader.ReadMessages(ToReadOptions(query));
            var result = new List<McapDataLoaderMessage>(messages.Count);
            for (var i = 0; i < messages.Count; i++)
                result.Add(ToDataLoaderMessage(messages[i]));
            return result;
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
            Initialize();
            var registry = CreateDecodeRegistry(options);
            foreach (var raw in CreateIterator(query))
            {
                registry.TryDecode(raw, out var decoded);
                yield return decoded;
            }
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
            Initialize();
            return GetDecodeRegistry(options).TryDecode(message, out decoded);
        }

        /// <summary>Gets the latest message per selected channel at or before the requested time.</summary>
        public IReadOnlyList<McapDataLoaderMessage> GetBackfill(McapDataLoaderBackfillQuery query)
        {
            ThrowIfDisposed();
            Initialize();

            query = query ?? new McapDataLoaderBackfillQuery();
            if (!QueryCanMatch(query.ChannelIds, query.Topics))
                return new List<McapDataLoaderMessage>();

            var selected = _reader.ReadLatestBefore(new McapReadOptions
            {
                EndTimeNs = query.TimeNs,
                ChannelIds = CopyUShorts(query.ChannelIds),
                Topics = CopyStrings(query.Topics)
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

        private static Dictionary<ushort, McapChannel> BuildChannelMap(IReadOnlyList<McapChannel> channels)
        {
            var map = new Dictionary<ushort, McapChannel>();
            if (channels == null)
                return map;

            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel != null)
                    map[channel.Id] = channel;
            }

            return map;
        }

        private static void BuildQueryMaps(
            IReadOnlyList<McapChannel> channels,
            out Dictionary<string, List<ushort>> topicChannelMap,
            out HashSet<ushort> knownChannelIds)
        {
            topicChannelMap = new Dictionary<string, List<ushort>>(StringComparer.Ordinal);
            knownChannelIds = new HashSet<ushort>();
            if (channels == null)
                return;

            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel == null)
                    continue;

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

        private static void AddSchemas(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapSchema> schemas)
        {
            if (schemas == null)
                return;

            for (var i = 0; i < schemas.Count; i++)
            {
                var schema = schemas[i];
                if (schema == null)
                    continue;

                initialization.Schemas.Add(new McapDataLoaderSchema
                {
                    SchemaId = schema.Id,
                    Name = schema.Name ?? string.Empty,
                    Encoding = schema.Encoding ?? string.Empty,
                    Data = schema.Data ?? new byte[0]
                });
            }
        }

        private static void AddChannels(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapChannel> channels,
            McapStatistics statistics)
        {
            if (channels == null)
                return;

            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel == null)
                    continue;

                var dto = new McapDataLoaderChannel
                {
                    ChannelId = channel.Id,
                    SchemaId = channel.SchemaId,
                    Topic = channel.Topic ?? string.Empty,
                    MessageEncoding = channel.MessageEncoding ?? string.Empty
                };

                if (statistics?.ChannelMessageCounts != null &&
                    statistics.ChannelMessageCounts.TryGetValue(channel.Id, out var count))
                {
                    dto.HasMessageCount = true;
                    dto.MessageCount = count;
                }

                initialization.Channels.Add(dto);
            }
        }

        private static void AddTimeRange(
            McapDataLoaderInitialization initialization,
            McapFileSummary summary)
        {
            if (summary?.Statistics != null && summary.Statistics.MessageCount > 0)
            {
                initialization.TimeRange.HasRange = true;
                initialization.TimeRange.StartTimeNs = summary.Statistics.MessageStartTime;
                initialization.TimeRange.EndTimeNs = summary.Statistics.MessageEndTime;
                return;
            }

            if (summary?.ChunkIndexes == null || summary.ChunkIndexes.Count == 0)
                return;

            var hasRange = false;
            var start = ulong.MaxValue;
            var end = 0UL;
            for (var i = 0; i < summary.ChunkIndexes.Count; i++)
            {
                var chunk = summary.ChunkIndexes[i];
                if (chunk == null)
                    continue;

                hasRange = true;
                if (chunk.MessageStartTime < start)
                    start = chunk.MessageStartTime;
                if (chunk.MessageEndTime > end)
                    end = chunk.MessageEndTime;
            }

            if (!hasRange)
                return;

            initialization.TimeRange.HasRange = true;
            initialization.TimeRange.StartTimeNs = start;
            initialization.TimeRange.EndTimeNs = end;
        }

        private static void AddMetadataIndexes(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapMetadataIndex> metadataIndexes)
        {
            if (metadataIndexes == null)
                return;

            for (var i = 0; i < metadataIndexes.Count; i++)
            {
                var index = metadataIndexes[i];
                if (index == null)
                    continue;

                initialization.MetadataIndexes.Add(new McapDataLoaderMetadataIndex
                {
                    Name = index.Name ?? string.Empty,
                    Offset = index.Offset,
                    Length = index.Length
                });
            }
        }

        private static void AddAttachmentIndexes(
            McapDataLoaderInitialization initialization,
            IReadOnlyList<McapAttachmentIndex> attachmentIndexes)
        {
            if (attachmentIndexes == null)
                return;

            for (var i = 0; i < attachmentIndexes.Count; i++)
            {
                var index = attachmentIndexes[i];
                if (index == null)
                    continue;

                initialization.AttachmentIndexes.Add(new McapDataLoaderAttachmentIndex
                {
                    Name = index.Name ?? string.Empty,
                    MediaType = index.MediaType ?? string.Empty,
                    Offset = index.Offset,
                    Length = index.Length,
                    LogTime = index.LogTime,
                    CreateTime = index.CreateTime,
                    DataSize = index.DataSize
                });
            }
        }

        private static void AddSummaryCounts(
            McapDataLoaderInitialization initialization,
            McapStatistics statistics)
        {
            if (statistics == null)
                return;

            initialization.HasTotalMessageCount = true;
            initialization.TotalMessageCount = statistics.MessageCount;
        }

        private void AddSequentialFallbackProblems(McapDataLoaderInitialization initialization)
        {
            var chunkIndexes = _reader.Summary?.ChunkIndexes;
            if (chunkIndexes != null && chunkIndexes.Count > 0)
                return;

            initialization.Problems.Add(new McapDataLoaderProblem(
                McapDataLoaderProblemSeverity.Warning,
                "MCAP file has no chunk indexes; queries will use bounded sequential fallback.",
                "UnindexedSequentialFallback",
                "Large unindexed files may require adding MCAP chunk indexes or increasing explicit fallback limits."));

            if (_sourceLengthBytes >= 0 &&
                _sequentialReadLimits.MaxPayloadBytes > 0 &&
                _sourceLengthBytes > _sequentialReadLimits.MaxPayloadBytes)
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "MCAP file size exceeds the sequential fallback payload limit.",
                    "UnindexedFileExceedsSequentialPayloadLimit",
                    "Queries may fail with MaxPayloadBytes unless the file is indexed or the limit is explicitly raised."));
            }

            var messageCount = _reader.Summary?.Statistics?.MessageCount ?? 0UL;
            if (_sequentialReadLimits.MaxMessages > 0 &&
                messageCount > (ulong)_sequentialReadLimits.MaxMessages)
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "MCAP message count exceeds the sequential fallback message limit.",
                    "UnindexedFileExceedsSequentialMessageLimit",
                    "Queries may fail with MaxMessages unless the file is indexed or the limit is explicitly raised."));
            }
        }

        private void AddSchemaReferenceProblems(McapDataLoaderInitialization initialization)
        {
            var schemaIds = new HashSet<ushort>();
            for (var i = 0; i < initialization.Schemas.Count; i++)
                schemaIds.Add(initialization.Schemas[i].SchemaId);

            for (var i = 0; i < initialization.Channels.Count; i++)
            {
                var channel = initialization.Channels[i];
                if (channel.SchemaId != 0 && !schemaIds.Contains(channel.SchemaId))
                {
                    initialization.Problems.Add(new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "MCAP channel references a schema ID that is not present in the summary.",
                        "UnknownSchemaId",
                        "The raw message payload is still available; typed decoding may not be possible."));
                }
            }
        }

        private void AddFoxRunSchemaMetadataProblems(McapDataLoaderInitialization initialization)
        {
            var metadataIndex = FindMetadataIndex(FoxRunSchemaMcapMetadata.MetadataName);
            if (metadataIndex == null)
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "Recorded MCAP does not contain FoxRun schema metadata; local raw loading will continue.",
                    "FoxRunSchemaMetadataMissing"));
                return;
            }

            var metadata = _reader.ReadMetadata(metadataIndex);
            if (metadata?.Metadata == null || !metadata.Metadata.TryGetValue("value", out var value))
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "Recorded FoxRun schema metadata is missing its value entry; local raw loading will continue.",
                    "FoxRunSchemaMetadataMalformed"));
                return;
            }

            var result = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(value, FoxRunSchemaInfoRegistry.Current);
            initialization.Problems.Add(ToProblem(result));
        }

        private McapMetadataIndex FindMetadataIndex(string name)
        {
            var indexes = _reader.MetadataIndexes;
            if (indexes == null || string.IsNullOrEmpty(name))
                return null;

            for (var i = 0; i < indexes.Count; i++)
            {
                var index = indexes[i];
                if (index != null && string.Equals(index.Name, name, StringComparison.Ordinal))
                    return index;
            }

            return null;
        }

        private static McapDataLoaderProblem ToProblem(FoxRunReplaySchemaGuardResult result)
        {
            if (result == null)
                return new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "Recorded MCAP does not contain usable FoxRun schema metadata; local raw loading will continue.",
                    "FoxRunSchemaMetadataMissing");

            switch (result.State)
            {
                case FoxRunReplaySchemaGuardState.Match:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Info,
                        "Recorded FoxRun schema metadata matches the current runtime manifest hash.",
                        "FoxRunSchemaMetadataMatch");
                case FoxRunReplaySchemaGuardState.MissingRecorded:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "Recorded MCAP does not contain FoxRun schema metadata; local raw loading will continue.",
                        "FoxRunSchemaMetadataMissing");
                case FoxRunReplaySchemaGuardState.MissingCurrent:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "Current runtime does not expose generated FoxRun schema info; local raw loading will continue.",
                        "FoxRunSchemaMetadataMissingCurrent");
                case FoxRunReplaySchemaGuardState.MalformedRecorded:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "Recorded FoxRun schema metadata is malformed; local raw loading will continue.",
                        "FoxRunSchemaMetadataMalformed");
                case FoxRunReplaySchemaGuardState.Mismatch:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Error,
                        "Recorded FoxRun schema metadata does not match the current runtime manifest; local raw loading will continue.",
                        "FoxRunSchemaMetadataMismatch",
                        "Replay may still be blocked by Phase 114 strict schema identity policy.");
                default:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        result.Message ?? string.Empty,
                        "FoxRunSchemaMetadataUnknown");
            }
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
                Data = message.Data ?? new byte[0]
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

        private static List<ushort> CopyUShorts(List<ushort> source)
            => source == null ? new List<ushort>() : new List<ushort>(source);

        private static List<string> CopyStrings(List<string> source)
            => source == null ? new List<string>() : new List<string>(source);

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McapDataLoader));
        }
    }
}
