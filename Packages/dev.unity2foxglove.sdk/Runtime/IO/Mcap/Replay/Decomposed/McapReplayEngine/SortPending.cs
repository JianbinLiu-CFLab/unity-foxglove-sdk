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
        private void SortPending()
        {
            // The materialized-only fast path remains equivalent to
            // `=> _pending.Sort(CompareMessages)` for the established hot-path
            // contract; deferred views are sorted alongside it below.
            _pending.Sort(CompareMessages);
            CompactDeferredPending();
            if (!_deferredPendingSorted && DeferredPendingCount > 1)
            {
                _deferredPending.Sort(CompareDeferredMessages);
                _deferredPendingSorted = true;
            }
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
                SourceOffset = _currentChunkStartOffset,
                SourceRecordOffset = (ulong)record.RecordOffset,
                Owner = owner,
                DataOffset = record.DataOffset,
                DataLength = record.DataLength
            });
            _deferredPendingSorted = false;
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
            if (_deferredRetryOwnerReferences.TryGetValue(chunkIndex, out var ownerReferences))
                _deferredRetryOwnerReferences[chunkIndex] = ownerReferences + 1;
            else
                _deferredRetryOwnerReferences[chunkIndex] = 1;
            _deferredRetriesSorted = false;
            return true;
        }

        private void RemoveDeferredRetry(int chunkIndex, int recordOffset)
        {
            if (!_deferredRetryByKey.Remove(MakeDeferredRetryKey(chunkIndex, recordOffset)))
                return;

            if (!_deferredRetryOwnerReferences.TryGetValue(chunkIndex, out var ownerReferences))
                return;
            if (ownerReferences <= 1)
            {
                _deferredRetryOwnerReferences.Remove(chunkIndex);
                _deferredRetryOwners.Remove(chunkIndex);
            }
            else
                _deferredRetryOwnerReferences[chunkIndex] = ownerReferences - 1;
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

                var message = ReadDeferredRetry(retry);
                RemoveDeferredRetry(retry.ChunkIndex, retry.RecordOffset);
                if (message != null)
                    AddTickResult(result, message);
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
            if (!_deferredRetryOwners.TryGetValue(retry.ChunkIndex, out var owner))
            {
                owner = _reader.ReadChunkRecords(chunk.ChunkStartOffset, chunk.ChunkLength, out var crcValid);
                if (!ShouldUseChunkRecords($"Deferred retry chunk {retry.ChunkIndex}", crcValid))
                    return null;
                _deferredRetryOwners[retry.ChunkIndex] = owner;
            }

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
                SourceOffset = chunk.ChunkStartOffset,
                SourceRecordOffset = (ulong)record.RecordOffset,
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
            _deferredRetryOwners.Clear();
            _deferredRetryOwnerReferences.Clear();
            _deferredRetriesSorted = false;
            _deferredPendingSorted = true;
            _deferredOwnerReferences.Clear();
            _deferredOwnerBytes = 0;
        }

        private void AddTickResult(List<McapMessage> result, McapMessage message)
        {
            result.Add(message);
            if (MaxMessagesPerTick <= 0)
                return;

            var index = _scanBoundaryCandidates.BinarySearch(message, MessageComparer);
            if (index < 0)
                index = ~index;
            _scanBoundaryCandidates.Insert(index, message);
            if (_scanBoundaryCandidates.Count > MaxMessagesPerTick)
                _scanBoundaryCandidates.RemoveAt(_scanBoundaryCandidates.Count - 1);
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
            cmp = left.PublishTime.CompareTo(right.PublishTime);
            if (cmp != 0) return cmp;
            cmp = left.SourceOffset.CompareTo(right.SourceOffset);
            return cmp != 0 ? cmp : left.SourceRecordOffset.CompareTo(right.SourceRecordOffset);
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
            cmp = left.PublishTime.CompareTo(right.PublishTime);
            if (cmp != 0) return cmp;
            cmp = left.SourceOffset.CompareTo(right.SourceOffset);
            return cmp != 0 ? cmp : left.SourceRecordOffset.CompareTo(right.SourceRecordOffset);
        }

        private bool ShouldUseChunkRecords(string scope, bool crcValid, bool emitWarning = true)
        {
            if (crcValid)
                return true;

            var message = $"[McapReplayEngine] {scope} CRC mismatch; data may be corrupted.";
            if (emitWarning)
                _logger.LogWarning(message);

            if (CrcMismatchPolicy == CorruptChunkPolicy.Throw)
                throw new InvalidDataException(message);

            return CrcMismatchPolicy == CorruptChunkPolicy.UseWithWarning;
        }
    }
}
