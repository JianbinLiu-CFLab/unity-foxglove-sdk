// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Normalizes H.264 encoder samples to Foxglove-compatible Annex B access units.

using System;
using System.Collections.Generic;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Converts H.264 samples from Annex B or length-prefixed NAL containers
    /// into one Annex B access unit suitable for foxglove.CompressedVideo.
    /// This lightweight normalizer caches the latest SPS and PPS only; streams
    /// with multiple simultaneous parameter-set ids should use a full decoder
    /// aware normalizer before publishing.
    /// </summary>
    public sealed class H264AccessUnitNormalizer
    {
        private const byte NonIdrSlice = 1;
        private const byte IdrSlice = 5;
        private const byte Sps = 7;
        private const byte Pps = 8;

        private byte[] _cachedSps;
        private byte[] _cachedPps;
        private readonly List<byte[]> _parsedNals = new List<byte[]>();
        private readonly List<byte[]> _parameterSetNals = new List<byte[]>();
        private readonly List<byte[]> _outputNals = new List<byte[]>();

        /// <summary>Caches SPS/PPS NAL units from a sequence header or sample.</summary>
        public void CacheParameterSets(byte[] data)
        {
            if (!TryParseNalUnits(data, _parameterSetNals))
                return;

            CacheParameterSets(_parameterSetNals);
        }

        /// <summary>
        /// Normalizes one encoder sample into one Annex B access unit. Returns
        /// false for empty, non-VCL, or non-decodable samples.
        /// </summary>
        public bool TryNormalizeSample(byte[] sample, out byte[] accessUnit)
        {
            accessUnit = null;
            var nals = _parsedNals;
            if (!TryParseNalUnits(sample, nals) || nals.Count == 0)
                return false;

            CacheParameterSets(nals);

            var hasVcl = false;
            var hasIdr = false;
            var hasSps = false;
            var hasPps = false;
            foreach (var nal in nals)
            {
                var type = NalType(nal);
                hasVcl |= IsVcl(nal);
                hasIdr |= type == IdrSlice;
                hasSps |= type == Sps;
                hasPps |= type == Pps;
            }

            if (!hasVcl)
                return false;

            _outputNals.Clear();
            if (hasIdr && (!hasSps || !hasPps))
            {
                if (_cachedSps != null && !hasSps)
                    _outputNals.Add(_cachedSps);
                if (_cachedPps != null && !hasPps)
                    _outputNals.Add(_cachedPps);
            }

            _outputNals.AddRange(nals);

            var candidate = BuildAnnexB(_outputNals);
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

        private static bool TryParseNalUnits(byte[] data, List<byte[]> nals)
        {
            nals.Clear();
            if (data == null || data.Length == 0)
                return false;

            if (H264AnnexBAccessUnitPacketizer.HasAnnexBStartCode(data))
                return TryParseAnnexBNalUnits(data, nals);

            return TryParseLengthPrefixedNalUnits(data, nals);
        }

        private static bool TryParseAnnexBNalUnits(byte[] data, List<byte[]> nals)
        {
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

        private static bool TryParseLengthPrefixedNalUnits(byte[] data, List<byte[]> nals)
        {
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
            var length = 0;
            foreach (var nal in nals)
            {
                if (nal != null && nal.Length > 0)
                    length += 4 + nal.Length;
            }
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
            => H264StartCodeScanner.Find(data, startIndex, out index, out length);
    }
}
