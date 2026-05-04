using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.IO
{
    public class McapReplayEngine : IDisposable
    {
        private McapReader _reader;
        private Stream _stream;
        private McapFileSummary _summary;
        private readonly Queue<McapMessage> _pending = new();

        // Per-chunk state
        private int _currentChunkIdx = -1;
        private byte[] _currentUncompressed;
        private int _readOffset;
        private ulong _lastEmitTime;
        private ulong _currentTimeNs;

        public const ulong ReplayChannelIdBase = 0x80000000UL;
        public int MaxMessagesPerTick = 1000;

        public bool IsLoaded { get; private set; }
        public ulong StartTimeNs { get; private set; }
        public ulong EndTimeNs { get; private set; }
        public bool CanSeek { get; private set; }
        public ulong CurrentTimeNs => _currentTimeNs;
        public IReadOnlyList<McapChannel> Channels => _summary?.Channels;
        public McapFileSummary Summary => _summary;

        public enum Status { Playing, Paused, Buffering, Ended }
        public Status CurrentStatus { get; private set; } = Status.Paused;

        public void Load(string filePath)
        {
            _stream = File.OpenRead(filePath);
            _reader = new McapReader(_stream);
            _summary = _reader.ReadSummary();
            CanSeek = _summary.Statistics != null && _summary.ChunkIndexes.Count > 0;
            StartTimeNs = _summary.Statistics?.MessageStartTime ?? 0;
            EndTimeNs = _summary.Statistics?.MessageEndTime ?? 0;
            _currentTimeNs = StartTimeNs;
            IsLoaded = true;
            CurrentStatus = Status.Paused;
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

                    if (opcode == 0x05)
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

        public void Play()
        {
            if (!IsLoaded) return;
            if (CurrentStatus == Status.Ended)
            {
                Seek(StartTimeNs);
            }
            CurrentStatus = Status.Playing;
        }

        public void Pause()
        {
            if (!IsLoaded) return;
            CurrentStatus = Status.Paused;
        }

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

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
            _reader = null;
            IsLoaded = false;
        }

        // ── Internal ──

        private bool LoadNextChunk()
        {
            _currentChunkIdx++;
            if (_currentChunkIdx >= _summary.ChunkIndexes.Count) return false;

            var ci = _summary.ChunkIndexes[_currentChunkIdx];
            _currentUncompressed = _reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength);
            _readOffset = 0;
            return true;
        }

        private McapMessage PopPending()
        {
            var m = _pending.Dequeue();
            _lastEmitTime = m.LogTime;
            return m;
        }
    }
}
