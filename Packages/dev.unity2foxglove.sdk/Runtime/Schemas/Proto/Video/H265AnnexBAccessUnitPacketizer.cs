// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Incremental H.265/HEVC Annex B access-unit packetizer for foxglove.CompressedVideo.

using System;
using System.Collections.Generic;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Splits an FFmpeg Annex B HEVC elementary stream into access units.
    /// The configured encoder emits AUD NALs, so AUD boundaries can be used
    /// to produce one decodable frame payload per Foxglove message.
    /// </summary>
    public sealed class H265AnnexBAccessUnitPacketizer
    {
        private const byte VclMin = 0;
        private const byte VclMax = 31;
        private const byte IrapMin = 16;
        private const byte IrapMax = 23;
        private const byte Vps = 32;
        private const byte Sps = 33;
        private const byte Pps = 34;
        private const byte AccessUnitDelimiter = 35;

        private readonly List<byte> _buffer = new List<byte>();
        private readonly List<byte> _currentAccessUnit = new List<byte>();
        private readonly Queue<byte[]> _completedAccessUnits = new Queue<byte[]>();
        private bool _currentHasVcl;

        /// <summary>
        /// Appends a chunk of bytes from an Annex B HEVC stream.
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

            return TryDequeueAccessUnit(out accessUnit);
        }

        /// <summary>Returns true when the byte sequence contains an Annex B start code.</summary>
        public static bool HasAnnexBStartCode(byte[] data)
            => FindStartCode(data, 0, out _, out _);

        /// <summary>Returns true when the access unit contains any VCL slice NAL.</summary>
        public static bool ContainsVclNal(byte[] accessUnit)
            => ContainsNalTypeInRange(accessUnit, VclMin, VclMax);

        /// <summary>Returns true when the access unit contains an IRAP/keyframe slice NAL.</summary>
        public static bool ContainsIrapNal(byte[] accessUnit)
            => ContainsNalTypeInRange(accessUnit, IrapMin, IrapMax);

        /// <summary>Returns true when the access unit contains a VPS NAL.</summary>
        public static bool ContainsVpsNal(byte[] accessUnit)
            => ContainsNalType(accessUnit, Vps);

        /// <summary>Returns true when the access unit contains an SPS NAL.</summary>
        public static bool ContainsSpsNal(byte[] accessUnit)
            => ContainsNalType(accessUnit, Sps);

        /// <summary>Returns true when the access unit contains a PPS NAL.</summary>
        public static bool ContainsPpsNal(byte[] accessUnit)
            => ContainsNalType(accessUnit, Pps);

        /// <summary>
        /// Performs a lightweight sanity check for a frame-sized HEVC access unit.
        /// IRAP frames are expected to include VPS/SPS/PPS because FFmpeg repeats headers.
        /// </summary>
        public static bool LooksLikeDecodableH265AccessUnit(byte[] accessUnit)
        {
            if (!ContainsVclNal(accessUnit))
                return false;

            return !ContainsIrapNal(accessUnit)
                || (ContainsVpsNal(accessUnit) && ContainsSpsNal(accessUnit) && ContainsPpsNal(accessUnit));
        }

        private void ParseBufferedBytes(bool flush)
        {
            while (_buffer.Count > 0)
            {
                if (!FindStartCode(_buffer, 0, out var start, out var startLength))
                {
                    TrimNonAnnexBTail();
                    return;
                }

                if (start > 0)
                    _buffer.RemoveRange(0, start);

                if (!FindStartCode(_buffer, startLength, out var nextStart, out _))
                {
                    if (TryProcessTrailingAudBoundary(startLength))
                        continue;

                    if (flush && _buffer.Count > startLength + 1)
                    {
                        ProcessNal(_buffer.ToArray(), startLength);
                        _buffer.Clear();
                    }

                    return;
                }

                var nal = _buffer.GetRange(0, nextStart).ToArray();
                ProcessNal(nal, startLength);
                _buffer.RemoveRange(0, nextStart);
            }
        }

        private bool TryProcessTrailingAudBoundary(int startLength)
        {
            var headerIndex = startLength;
            if (_buffer.Count <= headerIndex + 1)
                return false;

            var type = ExtractNalType(_buffer[headerIndex]);
            if (type != AccessUnitDelimiter)
                return false;

            ProcessNal(_buffer.ToArray(), startLength);
            _buffer.Clear();
            return true;
        }

        private void ProcessNal(byte[] annexBNal, int startLength)
        {
            if (annexBNal == null || annexBNal.Length <= startLength + 1)
                return;

            var nalType = ExtractNalType(annexBNal[startLength]);
            if (nalType == AccessUnitDelimiter)
            {
                if (_currentHasVcl)
                    EnqueueCurrentAccessUnit();

                _currentAccessUnit.Clear();
                _currentHasVcl = false;
            }

            _currentAccessUnit.AddRange(annexBNal);
            if (nalType >= VclMin && nalType <= VclMax)
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
            if (_buffer.Count <= MaxStartCodeTail)
                return;

            _buffer.RemoveRange(0, _buffer.Count - MaxStartCodeTail);
        }

        private static bool ContainsNalType(byte[] data, byte type)
            => ContainsNalTypeInRange(data, type, type);

        private static bool ContainsNalTypeInRange(byte[] data, byte minType, byte maxType)
        {
            if (data == null)
                return false;

            var searchFrom = 0;
            while (FindStartCode(data, searchFrom, out var start, out var length))
            {
                var headerIndex = start + length;
                if (headerIndex + 1 < data.Length)
                {
                    var nalType = ExtractNalType(data[headerIndex]);
                    if (nalType >= minType && nalType <= maxType)
                        return true;
                }

                searchFrom = Math.Max(headerIndex + 2, start + 1);
            }

            return false;
        }

        private static byte ExtractNalType(byte firstHeaderByte)
            => (byte)((firstHeaderByte >> 1) & 0x3F);

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
