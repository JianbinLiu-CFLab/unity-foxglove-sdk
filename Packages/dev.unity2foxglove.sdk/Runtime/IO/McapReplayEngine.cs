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
        private readonly List<McapMessage> _pending = new();

        // Per-chunk state
        private int _currentChunkIdx = -1;
        private byte[] _currentUncompressed;
        private int _readOffset;
        private ulong _lastEmitTime;
        private ulong _elapsedNs;

        public const ulong ReplayChannelIdBase = 0x80000000UL;
        public int MaxMessagesPerTick = 1000;

        public bool IsLoaded { get; private set; }
        public ulong StartTimeNs { get; private set; }
        public ulong EndTimeNs { get; private set; }
        public bool CanSeek { get; private set; }
        public ulong ElapsedNs => _elapsedNs;
        public IReadOnlyList<McapChannel> Channels => _summary?.Channels;

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
            IsLoaded = true;
            CurrentStatus = Status.Paused;
        }

        /// <summary>
        /// Emit messages due since last tick. Returns up to MaxMessagesPerTick.
        /// Advances internal time by a fixed step per tick (default ~16ms for ~60fps).
        /// </summary>
        public List<McapMessage> Tick()
        {
            var result = new List<McapMessage>();

            if (!IsLoaded || CurrentStatus == Status.Paused || CurrentStatus == Status.Ended)
                return result;

            // Advance by a fixed time step (~16.6ms at 60fps)
            const ulong tickStepNs = 16_666_667;
            _elapsedNs += tickStepNs;

            var nowNs = StartTimeNs + _elapsedNs;
            if (nowNs > EndTimeNs) nowNs = EndTimeNs;

            // Flush previously buffered messages that are now due
            while (_pending.Count > 0 && result.Count < MaxMessagesPerTick)
            {
                if (_pending[0].LogTime > nowNs) break;
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

                        if (logNs < _lastEmitTime) continue; // already emitted or before range
                        if (logNs > nowNs)
                        {
                            // Future message — buffer but keep reading chunk for earlier messages
                            _pending.Add(new McapMessage { ChannelId = chId, Sequence = seq, LogTime = logNs, PublishTime = pubNs, Data = data });
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
                            _pending.Add(msg);
                        }
                    }
                    else
                    {
                        _readOffset += (int)len; // skip non-message records
                    }
                }
                // Move to next chunk if current exhausted
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
            _elapsedNs = timeNs - StartTimeNs;

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
            var m = _pending[0];
            _pending.RemoveAt(0);
            _lastEmitTime = m.LogTime;
            return m;
        }
    }
}
