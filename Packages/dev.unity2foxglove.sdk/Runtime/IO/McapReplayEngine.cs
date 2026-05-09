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
        private readonly Queue<McapMessage> _pending = new();

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
        public int MaxMessagesPerTick = 1000;

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

            if (!IsLoaded || CurrentStatus == Status.Paused || CurrentStatus == Status.Ended)
                return result;

            var clampedNow = nowNs > EndTimeNs ? EndTimeNs : nowNs;
            _currentTimeNs = clampedNow;

            // Flush previously buffered messages that are now due
            while (_pending.Count > 0 && result.Count < MaxMessagesPerTick)
            {
                if (_pending.Peek().LogTime > clampedNow) break;
                result.Add(PopPending());
            }

            if (_pending.Count > 0)
            {
                CurrentStatus = Status.Buffering;
                return result;
            }

            if (!CanSeek)
                return result;

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
                    if (_readOffset + (int)len > _currentUncompressed.Length) break;

                    if (opcode == McapWriter.OpcodeMessage)
                    {
                        var startOff = _readOffset;
                        var chId = McapBinaryReader.ReadU16LE(_currentUncompressed, ref _readOffset);
                        var seq = McapBinaryReader.ReadU32LE(_currentUncompressed, ref _readOffset);
                        var logNs = McapBinaryReader.ReadU64LE(_currentUncompressed, ref _readOffset);
                        var pubNs = McapBinaryReader.ReadU64LE(_currentUncompressed, ref _readOffset);
                        var dataLen = (int)len - (_readOffset - startOff);
                        var data = new byte[dataLen];
                        Buffer.BlockCopy(_currentUncompressed, _readOffset, data, 0, dataLen);
                        _readOffset += dataLen;

                        if (logNs < _lastEmitTime) continue;
                        if (logNs > clampedNow)
                        {
                            _pending.Enqueue(new McapMessage { ChannelId = chId, Sequence = seq, LogTime = logNs, PublishTime = pubNs, Data = data });
                            continue;
                        }

                        var msg = new McapMessage { ChannelId = chId, Sequence = seq, LogTime = logNs, PublishTime = pubNs, Data = data };
                        if (result.Count < MaxMessagesPerTick)
                        {
                            result.Add(msg);
                            _lastEmitTime = logNs;
                        }
                        else
                        {
                            _pending.Enqueue(msg);
                        }
                    }
                    else
                    {
                        _readOffset += (int)len;
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
            var m = _pending.Dequeue();
            _lastEmitTime = m.LogTime;
            return m;
        }
    }
}
