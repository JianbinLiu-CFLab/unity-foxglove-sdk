// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Bounded MPEG-TS/PES reader for timestamped FFmpeg access units.

using System;
using System.Collections.Generic;

namespace Foxglove.Schemas.Video
{
    internal readonly struct MpegTsAccessUnit
    {
        public MpegTsAccessUnit(byte[] data, long pts90k)
        {
            Data = data ?? Array.Empty<byte>();
            Pts90k = pts90k;
        }

        public byte[] Data { get; }
        public long Pts90k { get; }
    }

    /// <summary>
    /// Incrementally parses the bounded MPEG-TS stream emitted by the FFmpeg
    /// sidecars. It emits one PES payload and its 90 kHz PTS at a time.
    /// </summary>
    internal sealed class MpegTsVideoDemuxer
    {
        private const int PacketBytes = 188;
        private const int MaxPesBytes = 16 * 1024 * 1024;

        private readonly List<byte> _pending = new List<byte>(PacketBytes * 4);
        private readonly Queue<MpegTsAccessUnit> _outputs = new Queue<MpegTsAccessUnit>();
        private int _pendingOffset;
        private readonly int _maxPesBytes;
        private List<byte> _pesPayload;
        private long _pesPts90k;
        private int _pesRemaining;
        private bool _pesTruncated;
        private int _droppedOversizedPes;
        private int _pmtPid = -1;
        private int _videoPid = -1;

        internal MpegTsVideoDemuxer(int maxPesBytes = MaxPesBytes)
        {
            if (maxPesBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPesBytes));
            _maxPesBytes = maxPesBytes;
        }

        public void Append(byte[] data, int offset, int count)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            for (var i = 0; i < count; i++)
                _pending.Add(data[offset + i]);
            ParsePackets();
        }

        public void Flush()
        {
            FinalizePes();
            _pending.Clear();
            _pendingOffset = 0;
        }

        internal int DroppedOversizedPes => _droppedOversizedPes;

        public bool TryDequeue(out MpegTsAccessUnit accessUnit)
        {
            if (_outputs.Count == 0)
            {
                accessUnit = default;
                return false;
            }

            accessUnit = _outputs.Dequeue();
            return true;
        }

        private void ParsePackets()
        {
            while (_pending.Count - _pendingOffset >= PacketBytes)
            {
                if (_pending[_pendingOffset] != 0x47)
                {
                    var sync = _pending.IndexOf(0x47, _pendingOffset);
                    if (sync < 0)
                    {
                        _pendingOffset = Math.Max(_pendingOffset, _pending.Count - (PacketBytes - 1));
                        CompactPending();
                        return;
                    }

                    _pendingOffset = sync;
                    if (_pending.Count - _pendingOffset < PacketBytes)
                        return;
                }

                var packet = new byte[PacketBytes];
                _pending.CopyTo(_pendingOffset, packet, 0, PacketBytes);
                _pendingOffset += PacketBytes;
                ParsePacket(packet);
                CompactPending();
            }
        }

        private void CompactPending()
        {
            if (_pendingOffset == 0)
                return;
            if (_pendingOffset < PacketBytes * 8 && _pendingOffset * 2 < _pending.Count)
                return;

            var remaining = _pending.Count - _pendingOffset;
            if (remaining > 0)
            {
                var compacted = new byte[remaining];
                _pending.CopyTo(_pendingOffset, compacted, 0, remaining);
                _pending.Clear();
                _pending.AddRange(compacted);
            }
            else
            {
                _pending.Clear();
            }
            _pendingOffset = 0;
        }

        private void ParsePacket(byte[] packet)
        {
            if (packet.Length != PacketBytes || packet[0] != 0x47)
                return;

            var payloadUnitStart = (packet[1] & 0x40) != 0;
            var pid = ((packet[1] & 0x1F) << 8) | packet[2];
            var adaptationControl = (packet[3] >> 4) & 0x03;
            if (adaptationControl == 0)
                return;

            var payloadOffset = 4;
            if ((adaptationControl & 0x02) != 0)
            {
                var adaptationLength = packet[payloadOffset];
                payloadOffset += 1 + adaptationLength;
                if (payloadOffset > PacketBytes)
                    return;
            }

            if ((adaptationControl & 0x01) == 0 || payloadOffset >= PacketBytes)
                return;

            var payloadLength = PacketBytes - payloadOffset;
            if (pid == 0)
            {
                ParsePat(packet, payloadOffset, payloadLength);
                return;
            }

            if (pid == _pmtPid)
            {
                ParsePmt(packet, payloadOffset, payloadLength);
                return;
            }

            if (pid == _videoPid)
                ParsePesPayload(packet, payloadOffset, payloadLength, payloadUnitStart);
        }

        private void ParsePat(byte[] packet, int offset, int length)
        {
            if (length < 2)
                return;
            var sectionStart = offset + 1 + packet[offset];
            if (sectionStart < offset || sectionStart + 12 > offset + length || packet[sectionStart] != 0x00)
                return;
            var sectionLength = ((packet[sectionStart + 1] & 0x0F) << 8) | packet[sectionStart + 2];
            var sectionEnd = sectionStart + 3 + sectionLength;
            if (sectionEnd > offset + length || sectionEnd < sectionStart + 12)
                return;

            for (var cursor = sectionStart + 8; cursor + 4 <= sectionEnd - 4; cursor += 4)
            {
                var program = (packet[cursor] << 8) | packet[cursor + 1];
                if (program == 0)
                    continue;
                _pmtPid = ((packet[cursor + 2] & 0x1F) << 8) | packet[cursor + 3];
                return;
            }
        }

        private void ParsePmt(byte[] packet, int offset, int length)
        {
            if (length < 3)
                return;
            var sectionStart = offset + 1 + packet[offset];
            if (sectionStart < offset || sectionStart + 12 > offset + length || packet[sectionStart] != 0x02)
                return;
            var sectionLength = ((packet[sectionStart + 1] & 0x0F) << 8) | packet[sectionStart + 2];
            var sectionEnd = sectionStart + 3 + sectionLength;
            if (sectionEnd > offset + length || sectionEnd < sectionStart + 12)
                return;

            var programInfoLength = ((packet[sectionStart + 10] & 0x0F) << 8) | packet[sectionStart + 11];
            var cursor = sectionStart + 12 + programInfoLength;
            while (cursor + 5 <= sectionEnd - 4)
            {
                var streamType = packet[cursor];
                var elementaryPid = ((packet[cursor + 1] & 0x1F) << 8) | packet[cursor + 2];
                var descriptors = ((packet[cursor + 3] & 0x0F) << 8) | packet[cursor + 4];
                if (streamType == 0x1B || streamType == 0x24)
                {
                    _videoPid = elementaryPid;
                    return;
                }
                cursor += 5 + descriptors;
            }
        }

        private void ParsePesPayload(byte[] packet, int offset, int length, bool payloadUnitStart)
        {
            if (payloadUnitStart)
            {
                FinalizePes();
                if (length < 9 || packet[offset] != 0x00 || packet[offset + 1] != 0x00 || packet[offset + 2] != 0x01)
                    return;

                var flags = packet[offset + 7];
                var headerLength = packet[offset + 8];
                var dataOffset = offset + 9 + headerLength;
                if (dataOffset > offset + length)
                    return;

                var pesLength = (packet[offset + 4] << 8) | packet[offset + 5];
                _pesPts90k = (flags & 0x80) != 0 && headerLength >= 5
                    ? ReadPts(packet, offset + 9)
                    : -1;
                _pesRemaining = pesLength == 0 ? int.MaxValue : Math.Max(0, pesLength - 3 - headerLength);
                _pesTruncated = false;
                _pesPayload = new List<byte>(Math.Min(_maxPesBytes, Math.Max(0, Math.Min(_pesRemaining, offset + length - dataOffset))));
                AppendPesBytes(packet, dataOffset, offset + length - dataOffset);
                return;
            }

            if (_pesPayload != null)
                AppendPesBytes(packet, offset, length);
        }

        private void AppendPesBytes(byte[] packet, int offset, int count)
        {
            if (count <= 0 || _pesPayload == null)
                return;
            var capacity = _maxPesBytes - _pesPayload.Count;
            var remaining = _pesRemaining == int.MaxValue ? int.MaxValue : _pesRemaining;
            if (remaining <= 0)
                return;
            var copy = Math.Min(count, Math.Min(capacity, remaining));
            for (var i = 0; i < copy; i++)
                _pesPayload.Add(packet[offset + i]);
            if (_pesRemaining != int.MaxValue)
                _pesRemaining -= copy;
            if (copy < count && _pesRemaining != 0)
                _pesTruncated = true;
        }

        private void FinalizePes()
        {
            if (_pesPayload == null)
                return;

            var complete = _pesRemaining == 0 || _pesRemaining == int.MaxValue;
            if (_pesTruncated)
                _droppedOversizedPes++;
            else if (complete && _pesPts90k >= 0 && _pesPayload.Count > 0)
                _outputs.Enqueue(new MpegTsAccessUnit(_pesPayload.ToArray(), _pesPts90k));
            _pesPayload = null;
            _pesPts90k = -1;
            _pesRemaining = 0;
            _pesTruncated = false;
        }

        private static long ReadPts(byte[] data, int offset)
        {
            return ((long)(data[offset] >> 1) & 0x07) << 30
                | ((long)data[offset + 1]) << 22
                | ((long)(data[offset + 2] >> 1) & 0x7F) << 15
                | ((long)data[offset + 3]) << 7
                | ((long)data[offset + 4] >> 1);
        }
    }
}
