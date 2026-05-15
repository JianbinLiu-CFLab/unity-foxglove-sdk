// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Normalizes H.264 encoder samples to Foxglove-compatible Annex B access units.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Converts H.264 samples from Annex B or length-prefixed NAL containers
    /// into one Annex B access unit suitable for foxglove.CompressedVideo.
    /// </summary>
    public sealed class H264AccessUnitNormalizer
    {
        private const byte NonIdrSlice = 1;
        private const byte IdrSlice = 5;
        private const byte Sps = 7;
        private const byte Pps = 8;

        private byte[] _cachedSps;
        private byte[] _cachedPps;

        /// <summary>Caches SPS/PPS NAL units from a sequence header or sample.</summary>
        public void CacheParameterSets(byte[] data)
        {
            if (!TryParseNalUnits(data, out var nals))
                return;

            CacheParameterSets(nals);
        }

        /// <summary>
        /// Normalizes one encoder sample into one Annex B access unit. Returns
        /// false for empty, non-VCL, or non-decodable samples.
        /// </summary>
        public bool TryNormalizeSample(byte[] sample, out byte[] accessUnit)
        {
            accessUnit = null;
            if (!TryParseNalUnits(sample, out var nals) || nals.Count == 0)
                return false;

            CacheParameterSets(nals);

            var hasVcl = nals.Any(IsVcl);
            if (!hasVcl)
                return false;

            var hasIdr = nals.Any(n => NalType(n) == IdrSlice);
            var hasSps = nals.Any(n => NalType(n) == Sps);
            var hasPps = nals.Any(n => NalType(n) == Pps);

            var outputNals = new List<byte[]>();
            if (hasIdr && (!hasSps || !hasPps))
            {
                if (_cachedSps != null && !hasSps)
                    outputNals.Add(_cachedSps);
                if (_cachedPps != null && !hasPps)
                    outputNals.Add(_cachedPps);
            }

            outputNals.AddRange(nals);

            var candidate = BuildAnnexB(outputNals);
            if (!H264AnnexBAccessUnitPacketizer.LooksLikeDecodableH264AccessUnit(candidate))
                return false;

            accessUnit = candidate;
            return true;
        }

        private void CacheParameterSets(IEnumerable<byte[]> nals)
        {
            foreach (var nal in nals)
            {
                switch (NalType(nal))
                {
                    case Sps:
                        _cachedSps = Copy(nal);
                        break;
                    case Pps:
                        _cachedPps = Copy(nal);
                        break;
                }
            }
        }

        private static bool TryParseNalUnits(byte[] data, out List<byte[]> nals)
        {
            nals = null;
            if (data == null || data.Length == 0)
                return false;

            if (H264AnnexBAccessUnitPacketizer.HasAnnexBStartCode(data))
                return TryParseAnnexBNalUnits(data, out nals);

            return TryParseLengthPrefixedNalUnits(data, out nals);
        }

        private static bool TryParseAnnexBNalUnits(byte[] data, out List<byte[]> nals)
        {
            nals = new List<byte[]>();
            var search = 0;
            while (FindStartCode(data, search, out var start, out var length))
            {
                var payloadStart = start + length;
                var nextSearch = Math.Max(payloadStart, start + 1);
                if (FindStartCode(data, nextSearch, out var nextStart, out _))
                {
                    AddRawNal(data, payloadStart, nextStart - payloadStart, nals);
                    search = nextStart;
                    continue;
                }

                AddRawNal(data, payloadStart, data.Length - payloadStart, nals);
                break;
            }

            return nals.Count > 0;
        }

        private static bool TryParseLengthPrefixedNalUnits(byte[] data, out List<byte[]> nals)
        {
            nals = new List<byte[]>();
            var offset = 0;
            while (offset < data.Length)
            {
                if (offset + 4 > data.Length)
                    return false;

                var length = (data[offset] << 24)
                    | (data[offset + 1] << 16)
                    | (data[offset + 2] << 8)
                    | data[offset + 3];
                offset += 4;
                if (length <= 0 || offset + length > data.Length)
                    return false;

                AddRawNal(data, offset, length, nals);
                offset += length;
            }

            return nals.Count > 0;
        }

        private static void AddRawNal(byte[] data, int offset, int length, List<byte[]> nals)
        {
            if (length <= 0 || offset < 0 || offset + length > data.Length)
                return;

            var nal = new byte[length];
            Buffer.BlockCopy(data, offset, nal, 0, length);
            nals.Add(nal);
        }

        private static byte[] BuildAnnexB(IReadOnlyList<byte[]> nals)
        {
            var length = nals.Sum(nal => 4 + (nal?.Length ?? 0));
            var result = new byte[length];
            var offset = 0;
            foreach (var nal in nals)
            {
                if (nal == null || nal.Length == 0)
                    continue;

                result[offset] = 0;
                result[offset + 1] = 0;
                result[offset + 2] = 0;
                result[offset + 3] = 1;
                offset += 4;
                Buffer.BlockCopy(nal, 0, result, offset, nal.Length);
                offset += nal.Length;
            }

            return result;
        }

        private static bool IsVcl(byte[] nal)
        {
            var type = NalType(nal);
            return type == NonIdrSlice || type == IdrSlice;
        }

        private static byte NalType(byte[] nal)
            => nal == null || nal.Length == 0 ? (byte)0 : (byte)(nal[0] & 0x1F);

        private static byte[] Copy(byte[] source)
        {
            if (source == null)
                return null;

            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
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
    }
}
