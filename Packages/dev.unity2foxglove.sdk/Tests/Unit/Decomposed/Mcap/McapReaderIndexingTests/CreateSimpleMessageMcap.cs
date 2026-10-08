// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP reader / indexing edge cases (migrated from Phase134_9Validation).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// MCAP reader and indexing edge cases. Ported from Phase134_9Validation
    /// (checks 134-9A .. 134-9G).
    /// </summary>


public partial class McapReaderIndexingTests
    {
        private static MemoryStream CreateSimpleMessageMcap(int messageCount)
        {
            return new MemoryStream(CreateSimpleMessageMcapBytes(messageCount), writable: false);
        }

        private static MemoryStream OpenSimpleMessageMcap(byte[] bytes)
        {
            return new MemoryStream(bytes, writable: false);
        }

        private static MemoryStream CreatePayloadMessageMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase173-040-payload-copy");
                writer.WriteSchema(1, "phase173_040.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase173_040", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 1, 1, Encoding.UTF8.GetBytes("first"));
                writer.WriteMessage(1, 2, 2, 2, Encoding.UTF8.GetBytes("second"));
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] CreateLargeRecordMcap()
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "round4-f02-dispose-buffer");
                writer.WriteSchema(1, "round4.F02", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/round4/f02/buffer", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 1, 1, new byte[4096]);
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            return stream.ToArray();
        }

        private static byte[] CreateTwoChunkIndexedMcap(out ulong secondChunkOffset)
        {
            byte[] firstRecords;
            byte[] secondRecords;
            using (var records = new MemoryStream())
            {
                using (var writer = new McapWriter(records, leaveOpen: true))
                    writer.WriteMessage(1, 1, 1, 1, Encoding.UTF8.GetBytes("first"));
                firstRecords = records.ToArray();
            }
            using (var records = new MemoryStream())
            {
                using (var writer = new McapWriter(records, leaveOpen: true))
                    writer.WriteMessage(1, 2, 2, 2, Encoding.UTF8.GetBytes("second"));
                secondRecords = records.ToArray();
            }

            var stream = new MemoryStream();
            ulong firstChunkOffset;
            ulong firstChunkLength;
            ulong firstIndexOffset;
            ulong firstIndexLength;
            ulong secondChunkLength;
            ulong secondIndexOffset;
            ulong secondIndexLength;
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "round4-f02-bounded-read");
                writer.WriteSchema(1, "round4.F02", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/round4/f02/bounded", "json", new Dictionary<string, string>());

                firstChunkOffset = (ulong)writer.Position;
                writer.WriteChunk(1, 1, (ulong)firstRecords.Length, 0, "", (ulong)firstRecords.Length, firstRecords);
                firstChunkLength = (ulong)writer.Position - firstChunkOffset;
                firstIndexOffset = (ulong)writer.Position;
                writer.WriteMessageIndex(1, new List<(ulong, ulong)> { (1, 0) });
                firstIndexLength = (ulong)writer.Position - firstIndexOffset;

                secondChunkOffset = (ulong)writer.Position;
                writer.WriteChunk(2, 2, (ulong)secondRecords.Length, 0, "", (ulong)secondRecords.Length, secondRecords);
                secondChunkLength = (ulong)writer.Position - secondChunkOffset;
                secondIndexOffset = (ulong)writer.Position;
                writer.WriteMessageIndex(1, new List<(ulong, ulong)> { (2, 0) });
                secondIndexLength = (ulong)writer.Position - secondIndexOffset;

                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteSchema(1, "round4.F02", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/round4/f02/bounded", "json", new Dictionary<string, string>());
                writer.WriteChunkIndex(1, 1, firstChunkOffset, firstChunkLength,
                    new Dictionary<ushort, ulong> { [1] = firstIndexOffset }, firstIndexLength, "", (ulong)firstRecords.Length, (ulong)firstRecords.Length);
                writer.WriteChunkIndex(2, 2, secondChunkOffset, secondChunkLength,
                    new Dictionary<ushort, ulong> { [1] = secondIndexOffset }, secondIndexLength, "", (ulong)secondRecords.Length, (ulong)secondRecords.Length);
                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            return stream.ToArray();
        }

        private static byte[] CreateSimpleMessageMcapBytes(int messageCount)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-file-order");
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_9", "json", new Dictionary<string, string>());
                for (var i = 1; i <= messageCount; i++)
                    writer.WriteMessage(1, (uint)i, (ulong)i, (ulong)i, Encoding.UTF8.GetBytes("{}"));
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            return stream.ToArray();
        }

        private static McapMessage Message(ushort channelId, uint sequence, ulong logTime)
        {
            return new McapMessage
            {
                ChannelId = channelId,
                Sequence = sequence,
                LogTime = logTime,
                PublishTime = logTime,
                Data = Array.Empty<byte>()
            };
        }

        private static MemoryStream CreateChunkMcap(out ulong chunkStart, out ulong chunkLength)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-chunk-length");
                chunkStart = (ulong)writer.Position;
                writer.WriteChunk(1, 1, 0, 0, "", 0, Array.Empty<byte>());
                chunkLength = (ulong)writer.Position - chunkStart;
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateIncompleteMessageIndexMcap()
        {
            using var chunkRecords = new MemoryStream();
            ulong channel1Offset;
            using (var chunkWriter = new McapWriter(chunkRecords, leaveOpen: true))
            {
                channel1Offset = (ulong)chunkWriter.Position;
                chunkWriter.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("one"));
                chunkWriter.WriteMessage(2, 1, 20, 20, Encoding.UTF8.GetBytes("two"));
            }

            var records = chunkRecords.ToArray();
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "round4-f02-incomplete-index");
                writer.WriteSchema(1, "round4.F02", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/round4/f02/one", "json", new Dictionary<string, string>());
                writer.WriteChannel(2, 1, "/round4/f02/two", "json", new Dictionary<string, string>());

                var chunkOffset = (ulong)writer.Position;
                writer.WriteChunk(10, 20, (ulong)records.Length, 0, "", (ulong)records.Length, records);
                var chunkLength = (ulong)writer.Position - chunkOffset;

                var channel1IndexOffset = (ulong)writer.Position;
                writer.WriteMessageIndex(1, new List<(ulong, ulong)> { (10, channel1Offset) });
                var messageIndexLength = (ulong)writer.Position - channel1IndexOffset;

                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteSchema(1, "round4.F02", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/round4/f02/one", "json", new Dictionary<string, string>());
                writer.WriteChannel(2, 1, "/round4/f02/two", "json", new Dictionary<string, string>());
                writer.WriteChunkIndex(
                    10,
                    20,
                    chunkOffset,
                    chunkLength,
                    new Dictionary<ushort, ulong> { [1] = channel1IndexOffset },
                    messageIndexLength,
                    "",
                    (ulong)records.Length,
                    (ulong)records.Length);
                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSummarylessBadChunkCrcMcap()
        {
            var stream = new MemoryStream();
            var records = CreateMessageRecord(1);
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-bad-chunk-crc");
                writer.WriteChunk(1, 1, (ulong)records.Length, 0x12345678, "", (ulong)records.Length, records);
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSecondHeaderMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-first-header");
                writer.WriteHeader("", "phase134-9-second-header");
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] CreateMessageRecord(uint sequence)
        {
            var content = new MemoryStream();
            WriteU16LE(content, 1);
            WriteU32LE(content, sequence);
            WriteU64LE(content, sequence);
            WriteU64LE(content, sequence);
            var payload = Encoding.UTF8.GetBytes("{}");
            content.Write(payload, 0, payload.Length);

            var record = new MemoryStream();
            record.WriteByte(McapWriter.OpcodeMessage);
            WriteU64LE(record, (ulong)content.Length);
            var contentBytes = content.ToArray();
            record.Write(contentBytes, 0, contentBytes.Length);
            return record.ToArray();
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteU32LE(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteU16LE(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteU32LE(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void WriteU64LE(Stream stream, ulong value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 32));
            stream.WriteByte((byte)(value >> 40));
            stream.WriteByte((byte)(value >> 48));
            stream.WriteByte((byte)(value >> 56));
        }

        private static bool ThrowsInvalidData(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        private static bool ThrowsObjectDisposed(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }
    }
}
