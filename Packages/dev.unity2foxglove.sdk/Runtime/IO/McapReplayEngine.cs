// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO
// Purpose: MCAP replay engine - loads an .mcap file, seeks by timestamp,
// plays/pauses, and emits messages to FoxgloveSession in log-time order.
// Supports LZ4/Zstd compressed chunks via McapReader.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// MCAP replay engine. Loads an .mcap file via McapReader, extracts
    /// channels and messages, and replays them in log-time order into
    /// a live FoxgloveSession. Supports play, pause, and seek.
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
        /// <summary>
        /// Messages read ahead of their emission time, waiting to be flushed.
        /// </summary>
        private readonly List<McapMessage> _pending = new();

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

        /// <summary>
        /// Base value for replay-generated channel IDs to avoid collisions with original IDs.
        /// </summary>
        public const ulong ReplayChannelIdBase = 0x80000000UL;
        /// <summary>
        /// Maximum number of messages emitted per Tick call.
        /// </summary>
        public int MaxMessagesPerTick = 8;

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
        /// <summary>
        /// Current replay engine state.
        /// </summary>
        public Status CurrentStatus { get; private set; } = Status.Paused;

        /// <summary>
        /// Opens an .mcap file and reads its summary section, preparing for replay.
        /// </summary>
        public void Load(string filePath)
        {
            ResetLoadedState(disposeStream: true);

            _stream = File.OpenRead(filePath);
            try
            {
                _reader = new McapReader(_stream);
                _summary = _reader.ReadSummary();
                CanSeek = _summary.Statistics != null && _summary.ChunkIndexes.Count > 0;
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
        public List<McapMessage> Tick(ulong nowNs)
        {
            var result = new List<McapMessage>();
            return Tick(nowNs, result);
        }

        /// <summary>
        /// Emit messages due between last tick time and nowNs into a caller-owned
        /// result buffer. The buffer is cleared before use to avoid per-frame
        /// list allocation in replay controllers.
        /// </summary>
        public List<McapMessage> Tick(ulong nowNs, List<McapMessage> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
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
            while (_pending.Count > 0)
            {
                if (_pending[0].LogTime > clampedNow) break;
                if (_pending[0].LogTime < emitAfter) { _pending.RemoveAt(0); continue; }
                result.Add(PopPending());
            }

            if (_pending.Count > 0 && _pending[0].LogTime <= clampedNow)
            {
                CurrentStatus = Status.Buffering;
                return FinishTickResult(result);
            }

            if (!CanSeek)
                return FinishTickResult(result);

            // Advance through chunks
            while (_currentChunkIdx < _summary.ChunkIndexes.Count - 1 || _readOffset < (_currentUncompressed?.Length ?? 0))
            {
                // Need next chunk?
                if (_currentChunkIdx < 0 || _readOffset >= (_currentUncompressed?.Length ?? 0))
                {
                    if (!LoadNextChunk()) break;
                }

                // Read messages from current chunk
                while (_readOffset + 9 <= _currentUncompressed.Length)
                {
                    var opcode = _currentUncompressed[_readOffset++];
                    var len = McapBinaryReader.ReadU64LE(_currentUncompressed, ref _readOffset);
                    if (len > int.MaxValue)
                        throw new InvalidDataException("MCAP chunk inner record length exceeds supported size.");
                    var recordLength = (int)len;
                    if (recordLength > _currentUncompressed.Length - _readOffset) break;

                    if (opcode == McapWriter.OpcodeMessage)
                    {
                        var startOff = _readOffset;
                        var chId = McapBinaryReader.ReadU16LE(_currentUncompressed, ref _readOffset);
                        var seq = McapBinaryReader.ReadU32LE(_currentUncompressed, ref _readOffset);
                        var logNs = McapBinaryReader.ReadU64LE(_currentUncompressed, ref _readOffset);
                        var pubNs = McapBinaryReader.ReadU64LE(_currentUncompressed, ref _readOffset);
                        var dataLen = recordLength - (_readOffset - startOff);
                        if (dataLen < 0 || dataLen > _currentUncompressed.Length - _readOffset)
                            throw new InvalidDataException("MCAP chunk message record is truncated.");
                        var data = new byte[dataLen];
                        Buffer.BlockCopy(_currentUncompressed, _readOffset, data, 0, dataLen);
                        _readOffset += dataLen;

                        if (logNs < emitAfter) continue;
                        if (logNs > clampedNow)
                        {
                            AddPending(new McapMessage { ChannelId = chId, Sequence = seq, LogTime = logNs, PublishTime = pubNs, Data = data });
                            continue;
                        }

                        // Collect all eligible messages; FinishTickResult caps
                        // at MaxMessagesPerTick and moves the sorted tail to
                        // pending so overflow never violates _lastEmitTime.
                        result.Add(new McapMessage { ChannelId = chId, Sequence = seq, LogTime = logNs, PublishTime = pubNs, Data = data });
                    }
                    else
                    {
                        _readOffset += recordLength;
                    }
                }
            }

            if (result.Count == 0 && _pending.Count == 0 && _currentChunkIdx >= _summary.ChunkIndexes.Count - 1
                && _readOffset >= (_currentUncompressed?.Length ?? 0))
            {
                CurrentStatus = Status.Ended;
            }
            else if (_pending.Count > 0)
            {
                CurrentStatus = Status.Buffering;
            }

            SortPending();
            return FinishTickResult(result);
        }

        /// <summary>
        /// Reads the latest message at or before <paramref name="timeNs"/> for
        /// each channel without changing the active replay cursor. Used to
        /// refresh Foxglove panels after paused seek/pause commands.
        /// </summary>
        public List<McapMessage> Snapshot(ulong timeNs, List<McapMessage> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();

            if (!IsLoaded || !CanSeek)
                return result;

            var clampedTime = timeNs > EndTimeNs ? EndTimeNs : timeNs;
            if (clampedTime < StartTimeNs)
                clampedTime = StartTimeNs;

            var latestByChannel = new Dictionary<ushort, McapMessage>();
            foreach (var chunkIndex in _summary.ChunkIndexes)
            {
                if (chunkIndex.MessageStartTime > clampedTime)
                    break;

                var uncompressed = _reader.ReadChunkRecords(chunkIndex.ChunkStartOffset, chunkIndex.ChunkLength, out var crcValid);
                if (!crcValid)
                    Console.Error.WriteLine("[McapReplayEngine] Snapshot chunk CRC mismatch; data may be corrupted.");

                var offset = 0;
                while (offset + 9 <= uncompressed.Length)
                {
                    var opcode = uncompressed[offset++];
                    var len = McapBinaryReader.ReadU64LE(uncompressed, ref offset);
                    if (len > int.MaxValue)
                        throw new InvalidDataException("MCAP chunk inner record length exceeds supported size.");
                    var recordLength = (int)len;
                    if (recordLength > uncompressed.Length - offset) break;

                    if (opcode != McapWriter.OpcodeMessage)
                    {
                        offset += recordLength;
                        continue;
                    }

                    var startOff = offset;
                    var chId = McapBinaryReader.ReadU16LE(uncompressed, ref offset);
                    var seq = McapBinaryReader.ReadU32LE(uncompressed, ref offset);
                    var logNs = McapBinaryReader.ReadU64LE(uncompressed, ref offset);
                    var pubNs = McapBinaryReader.ReadU64LE(uncompressed, ref offset);
                    var dataLen = recordLength - (offset - startOff);
                    if (dataLen < 0 || dataLen > uncompressed.Length - offset)
                        throw new InvalidDataException("MCAP chunk message record is truncated.");
                    var data = new byte[dataLen];
                    Buffer.BlockCopy(uncompressed, offset, data, 0, dataLen);
                    offset += dataLen;

                    if (logNs <= clampedTime)
                    {
                        latestByChannel[chId] = new McapMessage
                        {
                            ChannelId = chId,
                            Sequence = seq,
                            LogTime = logNs,
                            PublishTime = pubNs,
                            Data = data
                        };
                    }
                }
            }

            result.AddRange(latestByChannel.Values.OrderBy(m => m.LogTime).ThenBy(m => m.ChannelId));
            return result;
        }

        /// <summary>
        /// Reads every message in the inclusive range [<paramref name="fromTimeNs"/>,
        /// <paramref name="toTimeNs"/>] in chronological order without changing the
        /// active replay cursor. Used to rebuild Foxglove time-series panels after
        /// a seek while paused.
        /// </summary>
        public List<McapMessage> History(ulong fromTimeNs, ulong toTimeNs, List<McapMessage> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
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
                if (!crcValid)
                    Console.Error.WriteLine("[McapReplayEngine] History chunk CRC mismatch; data may be corrupted.");

                var offset = 0;
                while (offset + 9 <= uncompressed.Length)
                {
                    var opcode = uncompressed[offset++];
                    var len = McapBinaryReader.ReadU64LE(uncompressed, ref offset);
                    if (len > int.MaxValue)
                        throw new InvalidDataException("MCAP chunk inner record length exceeds supported size.");
                    var recordLength = (int)len;
                    if (recordLength > uncompressed.Length - offset) break;

                    if (opcode != McapWriter.OpcodeMessage)
                    {
                        offset += recordLength;
                        continue;
                    }

                    var startOff = offset;
                    var chId = McapBinaryReader.ReadU16LE(uncompressed, ref offset);
                    var seq = McapBinaryReader.ReadU32LE(uncompressed, ref offset);
                    var logNs = McapBinaryReader.ReadU64LE(uncompressed, ref offset);
                    var pubNs = McapBinaryReader.ReadU64LE(uncompressed, ref offset);
                    var dataLen = recordLength - (offset - startOff);
                    if (dataLen < 0 || dataLen > uncompressed.Length - offset)
                        throw new InvalidDataException("MCAP chunk message record is truncated.");
                    var data = new byte[dataLen];
                    Buffer.BlockCopy(uncompressed, offset, data, 0, dataLen);
                    offset += dataLen;

                    if (logNs < clampedFrom || logNs > clampedTo)
                        continue;

                    result.Add(new McapMessage
                    {
                        ChannelId = chId,
                        Sequence = seq,
                        LogTime = logNs,
                        PublishTime = pubNs,
                        Data = data
                    });
                }
            }

            if (result.Count > 1)
                result.Sort(CompareMessages);
            return result;
        }

        /// <summary>
        /// Starts or resumes replay. If already ended, seeks back to start first.
        /// </summary>
        public void Play()
        {
            if (!IsLoaded) return;
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
            if (!IsLoaded) return;
            CurrentStatus = Status.Paused;
        }

        /// <summary>
        /// Seeks to the given timestamp, clearing pending messages and repositioning the chunk cursor.
        /// </summary>
        public void Seek(ulong timeNs)
        {
            if (!IsLoaded || !CanSeek) return;

            _pending.Clear();
            _lastEmitTime = timeNs;
            _currentTimeNs = timeNs;

            // Find first chunk that contains or is after timeNs
            _currentChunkIdx = -1;
            for (var i = 0; i < _summary.ChunkIndexes.Count; i++)
            {
                if (timeNs <= _summary.ChunkIndexes[i].MessageEndTime)
                {
                    _currentChunkIdx = i - 1; // LoadNextChunk will advance to i
                    break;
                }
            }
            if (_currentChunkIdx < -1)
                _currentChunkIdx = _summary.ChunkIndexes.Count - 2;

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
            ResetLoadedState(disposeStream: true);
        }

        // Internal

        /// <summary>
        /// Clears replay cursors and optionally disposes the currently open MCAP stream.
        /// Used by both Dispose and repeated Load calls to avoid leaked file handles.
        /// </summary>
        private void ResetLoadedState(bool disposeStream)
        {
            if (disposeStream)
                _stream?.Dispose();
            _stream = null;
            _reader = null;
            _summary = null;
            _pending.Clear();
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
            if (!crcValid)
                Console.Error.WriteLine($"[McapReplayEngine] Chunk {_currentChunkIdx} CRC mismatch; data may be corrupted.");
            _readOffset = 0;
            return true;
        }

        /// <summary>
        /// Dequeues the oldest pending message and updates the last emitted time.
        /// </summary>
        private McapMessage PopPending()
        {
            var m = _pending[0];
            _pending.RemoveAt(0);
            return m;
        }

        private void AddPending(McapMessage message)
        {
            _pending.Add(message);
        }

        private void SortPending()
        {
            if (_pending.Count > 1)
                _pending.Sort(CompareMessages);
        }

        private List<McapMessage> FinishTickResult(List<McapMessage> result)
        {
            if (result.Count <= 0)
                return result;

            if (result.Count > 1)
                result.Sort(CompareMessages);

            // Cap at MaxMessagesPerTick. Because the list is sorted, the
            // tail (highest logTimes) is moved to pending. This guarantees
            // every pending message has logTime >= _lastEmitTime, preventing
            // cross-frame time regression ("Data went back in time").
            if (result.Count > MaxMessagesPerTick)
            {
                for (int i = MaxMessagesPerTick; i < result.Count; i++)
                    AddPending(result[i]);
                result.RemoveRange(MaxMessagesPerTick, result.Count - MaxMessagesPerTick);
            }

            _lastEmitTime = result[result.Count - 1].LogTime;
            return result;
        }

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
    }
}
