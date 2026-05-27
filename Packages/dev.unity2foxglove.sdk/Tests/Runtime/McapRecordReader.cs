// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test-only sequential MCAP record parser and decode helpers for verifying generated files.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Test-only sequential MCAP reader for verification of generated files.
    /// Uses McapBinaryReader for LE helpers.
    /// </summary>
    public static class McapRecordReader
    {
        public struct McapRecord
        {
            public byte Opcode;
            public byte[] Content;
        }

        /// <summary>
        /// Parses a raw MCAP byte array into leading magic flag, a
        /// list of records, and trailing magic flag.
        /// </summary>
        public static (bool hasLeadingMagic, List<McapRecord> records, bool hasTrailingMagic) Parse(byte[] data)
        {
            var records = new List<McapRecord>();
            var offset = 0;
            var hasLeadingMagic = false;
            var hasTrailingMagic = false;

            if (offset + 8 <= data.Length)
            {
                hasLeadingMagic = McapBinaryReader.MatchesMagic(data, offset);
                if (hasLeadingMagic) offset += 8;
            }

            while (offset + 9 <= data.Length)
            {
                var opcode = data[offset++];
                var len = McapBinaryReader.ReadU64LE(data, ref offset);
                if (len > int.MaxValue)
                    throw new InvalidDataException(
                        $"MCAP record length {len} exceeds the test parser limit {int.MaxValue}.");

                var remaining = data.Length - offset;
                if (len > (ulong)remaining)
                    throw new InvalidDataException(
                        $"MCAP record length {len} exceeds remaining buffer length {remaining}.");

                var contentLength = (int)len;
                var content = new byte[contentLength];
                if (contentLength > 0) Buffer.BlockCopy(data, offset, content, 0, contentLength);
                offset += contentLength;
                records.Add(new McapRecord { Opcode = opcode, Content = content });
            }

            if (offset + 8 <= data.Length)
            {
                hasTrailingMagic = McapBinaryReader.MatchesMagic(data, offset);
            }

            return (hasLeadingMagic, records, hasTrailingMagic);
        }

        // ── Decode helpers ──

        /// <summary>
        /// Decodes a Header record into profile and library strings.
        /// </summary>
        public static (string profile, string library) DecodeHeader(byte[] content)
        {
            var off = 0;
            return (McapBinaryReader.ReadString(content, ref off), McapBinaryReader.ReadString(content, ref off));
        }

        /// <summary>
        /// Decodes a Schema record into id, name, encoding, and data.
        /// </summary>
        public static (ushort id, string name, string encoding, byte[] data) DecodeSchema(byte[] content)
        {
            var off = 0;
            var id = McapBinaryReader.ReadU16LE(content, ref off);
            var name = McapBinaryReader.ReadString(content, ref off);
            var encoding = McapBinaryReader.ReadString(content, ref off);
            var data = McapBinaryReader.ReadPrefixed(content, ref off);
            return (id, name, encoding, data);
        }

        /// <summary>
        /// Decodes a Channel record into id, schemaId, topic, and encoding.
        /// </summary>
        public static (ushort id, ushort schemaId, string topic, string encoding) DecodeChannel(byte[] content)
        {
            var decoded = DecodeChannelWithMetadata(content);
            return (decoded.id, decoded.schemaId, decoded.topic, decoded.encoding);
        }

        /// <summary>
        /// Decodes a Channel record into id, schemaId, topic, encoding, and metadata.
        /// </summary>
        public static (ushort id, ushort schemaId, string topic, string encoding, Dictionary<string, string> metadata) DecodeChannelWithMetadata(byte[] content)
        {
            var off = 0;
            var id = McapBinaryReader.ReadU16LE(content, ref off);
            var schemaId = McapBinaryReader.ReadU16LE(content, ref off);
            var topic = McapBinaryReader.ReadString(content, ref off);
            var encoding = McapBinaryReader.ReadString(content, ref off);
            var metadata = McapBinaryReader.ReadMap(content, ref off);
            if (off != content.Length)
                throw new InvalidDataException($"Channel record has {content.Length - off} trailing byte(s).");
            return (id, schemaId, topic, encoding, metadata);
        }

        /// <summary>
        /// Decodes a Message record into channelId, sequence, logTime,
        /// publishTime, and data.
        /// </summary>
        public static (ushort channelId, uint sequence, ulong logTime, ulong publishTime, byte[] data) DecodeMessage(byte[] content)
        {
            var off = 0;
            var chId = McapBinaryReader.ReadU16LE(content, ref off);
            var seq = McapBinaryReader.ReadU32LE(content, ref off);
            var logTime = McapBinaryReader.ReadU64LE(content, ref off);
            var pubTime = McapBinaryReader.ReadU64LE(content, ref off);
            var data = new byte[content.Length - off];
            Buffer.BlockCopy(content, off, data, 0, content.Length - off);
            return (chId, seq, logTime, pubTime, data);
        }

        /// <summary>
        /// Decodes a Chunk record into startTime, endTime,
        /// uncompressedSize, crc, compression, compressedSize, and
        /// inner records.
        /// </summary>
        public static (ulong startTime, ulong endTime, ulong uncompressedSize, uint crc, string compression, ulong compressedSize, byte[] records) DecodeChunk(byte[] content)
        {
            var off = 0;
            var st = McapBinaryReader.ReadU64LE(content, ref off);
            var et = McapBinaryReader.ReadU64LE(content, ref off);
            var size = McapBinaryReader.ReadU64LE(content, ref off);
            var crc = McapBinaryReader.ReadU32LE(content, ref off);
            var comp = McapBinaryReader.ReadString(content, ref off);
            var compSize = McapBinaryReader.ReadU64LE(content, ref off);
            if (compSize > int.MaxValue)
                throw new InvalidDataException(
                    $"Chunk compressed size {compSize} exceeds the test parser limit {int.MaxValue}.");

            var remaining = content.Length - off;
            if (compSize > (ulong)remaining)
                throw new InvalidDataException(
                    $"Chunk compressed size {compSize} exceeds remaining record payload length {remaining}.");

            var compressedLength = (int)compSize;
            var trailing = remaining - compressedLength;
            if (trailing != 0)
                throw new InvalidDataException($"Chunk record has {trailing} trailing byte(s) after compressed records.");

            var recs = new byte[compressedLength];
            if (compressedLength > 0) Buffer.BlockCopy(content, off, recs, 0, compressedLength);
            return (st, et, size, crc, comp, compSize, recs);
        }

        /// <summary>
        /// Decodes a MessageIndex record into channelId and a list of
        /// (timestamp, offset) entries.
        /// </summary>
        public static (ushort channelId, List<(ulong, ulong)> entries) DecodeMessageIndex(byte[] content)
        {
            var off = 0;
            var chId = McapBinaryReader.ReadU16LE(content, ref off);
            var recsLen = McapBinaryReader.ReadU32LE(content, ref off);
            var count = recsLen / 16;
            var entries = new List<(ulong, ulong)>();
            for (uint i = 0; i < count; i++)
            {
                var ts = McapBinaryReader.ReadU64LE(content, ref off);
                var o = McapBinaryReader.ReadU64LE(content, ref off);
                entries.Add((ts, o));
            }
            return (chId, entries);
        }

        /// <summary>
        /// Decodes a Footer record into summary start offset, summary
        /// offset start, and summary CRC.
        /// </summary>
        public static (ulong summaryStart, ulong summaryOffsetStart, uint summaryCrc) DecodeFooter(byte[] content)
        {
            var off = 0;
            return (McapBinaryReader.ReadU64LE(content, ref off), McapBinaryReader.ReadU64LE(content, ref off), McapBinaryReader.ReadU32LE(content, ref off));
        }
    }
}
