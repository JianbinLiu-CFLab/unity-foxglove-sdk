// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Reader
// Purpose: Chunk-specific MCAP read helpers used by McapReader.

using System;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.IO
{
    internal static class McapChunkReader
    {
        internal static byte[] ReadChunkRecords(
            byte opcode,
            byte[] content,
            int contentLength,
            ulong chunkStartOffset,
            ulong chunkLength,
            long recordStart,
            long recordEnd,
            out bool crcValid,
            ulong uncompressedSizeLimit)
        {
            var actualChunkLength = (ulong)(recordEnd - recordStart);
            if (chunkLength != 0 && actualChunkLength != chunkLength)
                throw new InvalidDataException(
                    $"Chunk record at offset {chunkStartOffset} has length {actualChunkLength}, expected {chunkLength}.");
            if (opcode != McapWriter.OpcodeChunk)
                throw new InvalidDataException($"Expected Chunk (0x06) at offset {chunkStartOffset}, got 0x{opcode:X2}");

            return DecodeChunkRecordsContent(content, 0, contentLength, out crcValid, uncompressedSizeLimit);
        }

        internal static byte[] DecodeChunkRecordsContent(
            byte[] content,
            int offset,
            int contentLength,
            out bool crcValid,
            ulong uncompressedSizeLimit)
        {
            return McapRecordDecoder.DecodeChunkRecordsContent(
                content,
                offset,
                contentLength,
                out crcValid,
                uncompressedSizeLimit);
        }

        internal static void EnsureCrcValid(bool crcValid, bool validateCrcs)
        {
            if (!crcValid && validateCrcs)
                throw new InvalidDataException("MCAP chunk CRC mismatch.");
        }

        internal static List<McapMessage> ReadMessages(byte[] uncompressedRecords, ushort? filterChannelId = null)
        {
            var messages = new List<McapMessage>();
            foreach (var message in EnumerateMessages(uncompressedRecords, filterChannelId))
                messages.Add(message);
            return messages;
        }

        internal static IEnumerable<McapMessage> EnumerateMessages(byte[] uncompressedRecords, ushort? filterChannelId = null)
        {
            foreach (var record in EnumerateRawRecords(uncompressedRecords))
            {
                if (record.Opcode == McapWriter.OpcodeMessage)
                {
                    var msg = McapRecordDecoder.DecodeMessage(uncompressedRecords, record.Offset, record.Length);
                    if (!filterChannelId.HasValue || msg.ChannelId == filterChannelId.Value)
                        yield return msg;
                }
            }
        }

        internal static IEnumerable<McapPrivateRecord> EnumeratePrivateRecords(
            byte[] uncompressedRecords,
            ulong chunkStartOffset)
        {
            foreach (var record in EnumerateRawRecords(uncompressedRecords))
            {
                if (McapWriter.IsPrivateOpcode(record.Opcode))
                {
                    var data = new byte[record.Length];
                    if (record.Length > 0)
                        Buffer.BlockCopy(uncompressedRecords, record.Offset, data, 0, record.Length);
                    yield return new McapPrivateRecord
                    {
                        Opcode = record.Opcode,
                        Data = data,
                        Offset = chunkStartOffset,
                        InChunk = true
                    };
                }
            }
        }

        private static IEnumerable<RawChunkRecord> EnumerateRawRecords(byte[] uncompressedRecords)
        {
            var off = 0;
            while (off < uncompressedRecords.Length)
            {
                if (uncompressedRecords.Length - off < McapWriter.RecordHeaderLength)
                    throw new InvalidDataException("Chunk inner record is truncated.");

                var opcode = uncompressedRecords[off++];
                if (opcode == 0x00)
                    throw new InvalidDataException("MCAP opcode 0x00 is invalid inside chunk.");

                var len = McapBinaryReader.ReadU64LE(uncompressedRecords, ref off);
                if (len > int.MaxValue)
                    throw new InvalidDataException("Chunk inner record length exceeds int.MaxValue.");
                var recordLength = (int)len;
                if (recordLength < 0 || recordLength > uncompressedRecords.Length - off)
                    throw new InvalidDataException("Chunk inner record content is truncated.");

                yield return new RawChunkRecord(opcode, off, recordLength);

                off += recordLength;
            }
        }

        private readonly struct RawChunkRecord
        {
            public RawChunkRecord(byte opcode, int offset, int length)
            {
                Opcode = opcode;
                Offset = offset;
                Length = length;
            }

            public byte Opcode { get; }
            public int Offset { get; }
            public int Length { get; }
        }
    }
}
