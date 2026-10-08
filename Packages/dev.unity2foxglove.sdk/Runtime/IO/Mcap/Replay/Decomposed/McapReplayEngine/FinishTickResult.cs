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
            cmp = a.PublishTime.CompareTo(b.PublishTime);
            if (cmp != 0) return cmp;
            return McapLatestAtQuery.CompareSourcePosition(a, b);
        }

        private static void SortChunkIndexes(List<McapChunkIndex> chunkIndexes)
        {
            chunkIndexes?.Sort(CompareChunkIndexes);
        }

        private List<int> GetHistoryChunkNumbersByDescendingEndTime()
        {
            var ordered = new List<int>(_summary.ChunkIndexes.Count);
            for (var i = 0; i < _summary.ChunkIndexes.Count; i++)
                ordered.Add(i);

            ordered.Sort((leftNumber, rightNumber) =>
            {
                var left = _summary.ChunkIndexes[leftNumber];
                var right = _summary.ChunkIndexes[rightNumber];
                var compare = right.MessageEndTime.CompareTo(left.MessageEndTime);
                if (compare != 0)
                    return compare;
                compare = right.MessageStartTime.CompareTo(left.MessageStartTime);
                if (compare != 0)
                    return compare;
                return right.ChunkStartOffset.CompareTo(left.ChunkStartOffset);
            });
            return ordered;
        }

        private List<McapChunkIndex> GetSnapshotChunkIndexesByDescendingEndTime()
        {
            if (_snapshotChunkIndexesByDescendingEndTime != null)
                return _snapshotChunkIndexesByDescendingEndTime;

            var ordered = McapLatestAtQuery.OrderChunkIndexesByDescendingEndTime(_summary.ChunkIndexes);
            _snapshotChunkIndexesByDescendingEndTime = ordered;
            return ordered;
        }

        private static bool CanStopSnapshotScan(
            Dictionary<ushort, SnapshotCandidate> latestByChannel,
            ulong nextOlderChunkEndTime)
        {
            var oldestSelected = ulong.MaxValue;
            foreach (var candidate in latestByChannel.Values)
            {
                if (candidate.LogTime < oldestSelected)
                    oldestSelected = candidate.LogTime;
            }
            return nextOlderChunkEndTime < oldestSelected;
        }

        private static int CompareSnapshotCandidates(SnapshotCandidate left, SnapshotCandidate right)
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
            internal ulong SourceOffset;
            internal ulong SourceRecordOffset;
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
                    SourceOffset = SourceOffset,
                    SourceRecordOffset = SourceRecordOffset,
                    Data = data
                };
            }
        }

        private sealed class HistoryChunkSpool : IDisposable
        {
            private readonly long _maxBytes;
            private readonly Dictionary<int, SpoolChunk> _chunks = new Dictionary<int, SpoolChunk>();
            private string _path;
            private FileStream _stream;
            private long _storedBytes;
            private bool _flushed;
            private bool _disposed;

            internal HistoryChunkSpool(long maxBytes)
            {
                _maxBytes = maxBytes;
            }

            internal bool Store(int chunkNumber, byte[] uncompressed)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(HistoryChunkSpool));
                if (uncompressed == null)
                    throw new ArgumentNullException(nameof(uncompressed));
                if (uncompressed.LongLength > _maxBytes - _storedBytes)
                    return false;

                if (_stream == null)
                {
                    _path = Path.Combine(
                        Path.GetTempPath(),
                        "foxglove-history-" + Guid.NewGuid().ToString("N") + ".tmp");
                    _stream = new FileStream(
                        _path,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.Read,
                        64 * 1024,
                        FileOptions.SequentialScan);
                }

                var offset = _stream.Length;
                _stream.Seek(offset, SeekOrigin.Begin);
                _stream.Write(uncompressed, 0, uncompressed.Length);
                _chunks[chunkNumber] = new SpoolChunk(offset, uncompressed.Length);
                _storedBytes += uncompressed.LongLength;
                _flushed = false;
                return true;
            }

            internal bool TryCopyPayload(
                int chunkNumber,
                int dataOffset,
                int dataLength,
                out byte[] payload)
            {
                payload = null;
                if (_disposed
                    || _stream == null
                    || dataOffset < 0
                    || dataLength < 0
                    || !_chunks.TryGetValue(chunkNumber, out var chunk)
                    || dataOffset > chunk.Length
                    || dataLength > chunk.Length - dataOffset)
                    return false;

                if (!_flushed)
                {
                    _stream.Flush();
                    _flushed = true;
                }

                payload = new byte[dataLength];
                _stream.Seek(chunk.Offset + dataOffset, SeekOrigin.Begin);
                var read = 0;
                while (read < dataLength)
                {
                    var count = _stream.Read(payload, read, dataLength - read);
                    if (count <= 0)
                    {
                        payload = null;
                        return false;
                    }
                    read += count;
                }
                return true;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _stream?.Dispose();
                if (_path == null)
                    return;
                try
                {
                    File.Delete(_path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            private readonly struct SpoolChunk
            {
                internal SpoolChunk(long offset, int length)
                {
                    Offset = offset;
                    Length = length;
                }

                internal long Offset { get; }
                internal int Length { get; }
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
