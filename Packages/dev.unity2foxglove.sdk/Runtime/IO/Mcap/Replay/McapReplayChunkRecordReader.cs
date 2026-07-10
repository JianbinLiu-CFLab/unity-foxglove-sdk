// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.IO;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// A decoded MCAP message header with a payload window in the source chunk.
    /// </summary>
    internal readonly struct McapReplayChunkRecord
    {
        internal McapReplayChunkRecord(
            ushort channelId,
            uint sequence,
            ulong logTime,
            ulong publishTime,
            int dataOffset,
            int dataLength)
        {
            IsMessage = true;
            ChannelId = channelId;
            Sequence = sequence;
            LogTime = logTime;
            PublishTime = publishTime;
            DataOffset = dataOffset;
            DataLength = dataLength;
        }

        internal bool IsMessage { get; }
        internal ushort ChannelId { get; }
        internal uint Sequence { get; }
        internal ulong LogTime { get; }
        internal ulong PublishTime { get; }
        internal int DataOffset { get; }
        internal int DataLength { get; }
    }

    /// <summary>
    /// Reads one record header from a decompressed replay chunk without copying payload bytes.
    /// </summary>
    internal static class McapReplayChunkRecordReader
    {
        internal static McapReplayChunkRecord ReadNext(byte[] chunk, ref int offset)
        {
            var opcode = chunk[offset++];
            if (opcode == 0x00)
                throw new InvalidDataException("MCAP opcode 0x00 is invalid inside chunk.");

            var len = McapBinaryReader.ReadU64LE(chunk, ref offset);
            if (len > int.MaxValue)
                throw new InvalidDataException("MCAP chunk inner record length exceeds supported size.");
            var recordLength = (int)len;
            if (recordLength > chunk.Length - offset)
                throw new InvalidDataException("MCAP chunk inner record is truncated.");

            if (opcode != McapWriter.OpcodeMessage)
            {
                offset += recordLength;
                return default;
            }

            var startOffset = offset;
            var channelId = McapBinaryReader.ReadU16LE(chunk, ref offset);
            var sequence = McapBinaryReader.ReadU32LE(chunk, ref offset);
            var logTime = McapBinaryReader.ReadU64LE(chunk, ref offset);
            var publishTime = McapBinaryReader.ReadU64LE(chunk, ref offset);
            var dataLength = recordLength - (offset - startOffset);
            if (dataLength < 0 || dataLength > chunk.Length - offset)
                throw new InvalidDataException("MCAP chunk message record is truncated.");

            var dataOffset = offset;
            offset += dataLength;
            return new McapReplayChunkRecord(
                channelId,
                sequence,
                logTime,
                publishTime,
                dataOffset,
                dataLength);
        }
    }
}
