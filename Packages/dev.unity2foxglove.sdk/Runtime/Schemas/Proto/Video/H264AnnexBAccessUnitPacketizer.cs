// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Incremental H.264 Annex B access-unit packetizer for foxglove.CompressedVideo.

using System;
using System.Collections.Generic;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Splits an FFmpeg Annex B H.264 elementary stream into access units.
    /// The configured encoder emits AUD NALs, so AUD boundaries can be used
    /// to produce one decodable frame payload per Foxglove message.
    /// </summary>
    public sealed class H264AnnexBAccessUnitPacketizer
    {
        private const byte NonIdrSlice = 1;
        private const byte IdrSlice = 5;
        private const byte Sps = 7;
        private const byte Pps = 8;
        private const byte AccessUnitDelimiter = 9;

        private readonly List<byte> _buffer = new List<byte>();
        private readonly List<byte> _currentAccessUnit = new List<byte>();
        private readonly Queue<byte[]> _completedAccessUnits = new Queue<byte[]>();
        private int _bufferStart;
        private bool _currentHasVcl;

        /// <summary>
        /// Appends a chunk of bytes from an Annex B H.264 stream.
        /// </summary>
        public void Append(byte[] data)
        {
            if (data == null || data.Length == 0)
                return;

            _buffer.AddRange(data);
            ParseBufferedBytes(flush: false);
        }

        /// <summary>
        /// Dequeues the next completed access unit, if one is available.
        /// </summary>
        public bool TryDequeueAccessUnit(out byte[] accessUnit)
        {
            if (_completedAccessUnits.Count == 0)
            {
                accessUnit = null;
                return false;
            }

            accessUnit = _completedAccessUnits.Dequeue();
            return true;
        }

        /// <summary>
        /// Flushes the final buffered access unit when the stream ends.
        /// </summary>
        public bool Flush(out byte[] accessUnit)
        {
            ParseBufferedBytes(flush: true);
            if (_currentHasVcl)
                EnqueueCurrentAccessUnit();

            _currentAccessUnit.Clear();
            _currentHasVcl = false;
            _buffer.Clear();
            _bufferStart = 0;

            return TryDequeueAccessUnit(out accessUnit);
        }

        /// <summary>Returns true when the byte sequence contains an Annex B start code.</summary>
        public static bool HasAnnexBStartCode(byte[] data)
            => FindStartCode(data, 0, out _, out _);

        /// <summary>Returns true when the access unit contains any VCL slice NAL.</summary>
        public static bool ContainsVclNal(byte[] accessUnit)
            => ContainsNalType(accessUnit, NonIdrSlice) || ContainsNalType(accessUnit, IdrSlice);

        /// <summary>Returns true when the access unit contains an IDR slice NAL.</summary>
        public static bool ContainsIdrNal(byte[] accessUnit)
            => ContainsNalType(accessUnit, IdrSlice);

        /// <summary>Returns true when the access unit contains an SPS NAL.</summary>
        public static bool ContainsSpsNal(byte[] accessUnit)
            => ContainsNalType(accessUnit, Sps);

        /// <summary>Returns true when the access unit contains a PPS NAL.</summary>
        public static bool ContainsPpsNal(byte[] accessUnit)
            => ContainsNalType(accessUnit, Pps);

        /// <summary>
        /// Performs a lightweight sanity check for a frame-sized H.264 access unit.
        /// IDR frames are expected to include SPS/PPS because FFmpeg repeats headers.
        /// </summary>
        public static bool LooksLikeDecodableH264AccessUnit(byte[] accessUnit)
        {
            if (!ContainsVclNal(accessUnit))
                return false;

            return !ContainsIdrNal(accessUnit)
                || (ContainsSpsNal(accessUnit) && ContainsPpsNal(accessUnit));
        }

        private void ParseBufferedBytes(bool flush)
        {
            while (AvailableBufferBytes > 0)
            {
                if (!FindStartCode(_buffer, _bufferStart, out var start, out var startLength))
                {
                    TrimNonAnnexBTail();
                    return;
                }

                _bufferStart = start;

                if (!FindStartCode(_buffer, _bufferStart + startLength, out var nextStart, out _))
                {
                    if (TryProcessTrailingAudBoundary(startLength))
                        continue;

                    if (flush && AvailableBufferBytes > startLength)
                    {
                        ProcessNal(CopyBufferRange(_bufferStart, AvailableBufferBytes), startLength);
                        _buffer.Clear();
                        _bufferStart = 0;
                    }

                    return;
                }

                var nal = CopyBufferRange(_bufferStart, nextStart - _bufferStart);
                ProcessNal(nal, startLength);
                _bufferStart = nextStart;
                CompactBufferIfNeeded();
            }
        }

        private bool TryProcessTrailingAudBoundary(int startLength)
        {
            var headerIndex = _bufferStart + startLength;
            if (_buffer.Count <= headerIndex)
                return false;

            var type = (byte)(_buffer[headerIndex] & 0x1F);
            if (type != AccessUnitDelimiter)
                return false;

            ProcessNal(CopyBufferRange(_bufferStart, AvailableBufferBytes), startLength);
            _buffer.Clear();
            _bufferStart = 0;
            return true;
        }

        private void ProcessNal(byte[] annexBNal, int startLength)
        {
            if (annexBNal == null || annexBNal.Length <= startLength)
                return;

            var nalType = (byte)(annexBNal[startLength] & 0x1F);
            if (nalType == AccessUnitDelimiter)
            {
                if (_currentHasVcl)
                    EnqueueCurrentAccessUnit();

                _currentAccessUnit.Clear();
                _currentHasVcl = false;
            }

            _currentAccessUnit.AddRange(annexBNal);
            if (nalType == NonIdrSlice || nalType == IdrSlice)
                _currentHasVcl = true;
        }

        private void EnqueueCurrentAccessUnit()
        {
            if (_currentAccessUnit.Count == 0)
                return;

            _completedAccessUnits.Enqueue(_currentAccessUnit.ToArray());
        }

        private void TrimNonAnnexBTail()
        {
            const int MaxStartCodeTail = 3;
            if (AvailableBufferBytes <= MaxStartCodeTail)
                return;

            var keep = Math.Min(MaxStartCodeTail, AvailableBufferBytes);
            var tail = CopyBufferRange(_buffer.Count - keep, keep);
            _buffer.Clear();
            _buffer.AddRange(tail);
            _bufferStart = 0;
        }

        private int AvailableBufferBytes => _buffer.Count - _bufferStart;

        private byte[] CopyBufferRange(int offset, int count)
        {
            var copy = new byte[Math.Max(0, count)];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = _buffer[offset + i];
            return copy;
        }

        private void CompactBufferIfNeeded()
        {
            if (_bufferStart <= 0)
                return;

            if (_bufferStart >= _buffer.Count)
            {
                _buffer.Clear();
                _bufferStart = 0;
                return;
            }

            if (_bufferStart < 4096 || _bufferStart < _buffer.Count / 2)
                return;

            _buffer.RemoveRange(0, _bufferStart);
            _bufferStart = 0;
        }

        private static bool ContainsNalType(byte[] data, byte type)
        {
            if (data == null)
                return false;

            var searchFrom = 0;
            while (FindStartCode(data, searchFrom, out var start, out var length))
            {
                var headerIndex = start + length;
                if (headerIndex < data.Length && (data[headerIndex] & 0x1F) == type)
                    return true;

                searchFrom = Math.Max(headerIndex + 1, start + 1);
            }

            return false;
        }

        private static bool FindStartCode(byte[] data, int startIndex, out int index, out int length)
        {
            index = -1;
            length = 0;
            if (data == null)
                return false;

            for (var i = Math.Max(0, startIndex); i <= data.Length - 3; i++)
            {
                if (i <= data.Length - 4
                    && data[i] == 0
                    && data[i + 1] == 0
                    && data[i + 2] == 0
                    && data[i + 3] == 1)
                {
                    index = i;
                    length = 4;
                    return true;
                }

                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
                {
                    index = i;
                    length = 3;
                    return true;
                }
            }

            return false;
        }

        private static bool FindStartCode(List<byte> data, int startIndex, out int index, out int length)
        {
            index = -1;
            length = 0;
            if (data == null)
                return false;

            for (var i = Math.Max(0, startIndex); i <= data.Count - 3; i++)
            {
                if (i <= data.Count - 4
                    && data[i] == 0
                    && data[i + 1] == 0
                    && data[i + 2] == 0
                    && data[i + 3] == 1)
                {
                    index = i;
                    length = 4;
                    return true;
                }

                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
                {
                    index = i;
                    length = 3;
                    return true;
                }
            }

            return false;
        }
    }
}
