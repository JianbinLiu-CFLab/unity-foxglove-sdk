// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP attachment records and summary CRC behavior (migrated from Phase34Validation).

using System;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// MCAP attachment records, attachment index summary, CRC validation, and
    /// footer summary CRC behavior. Ported from Phase34Validation (34A .. 34C).
    /// </summary>
    [Trait("Phase", "34")]
    [Trait("Domain", "Mcap")]
    public class McapAttachmentAndSummaryCrcTests
    {
        [Fact]
        public void AttachmentRoundtrip()
        {
            using var ms = new MemoryStream();
            var writer = new McapWriter(ms, leaveOpen: true);
            writer.WriteMagic();
            writer.WriteHeader("", "test");

            var data = Encoding.UTF8.GetBytes("hello attachment");
            var index = writer.WriteAttachment(1000, 2000, "test.txt", "text/plain", data);

            writer.WriteDataEnd();
            writer.WriteMagic();
            writer.Flush();
            ms.Position = 0;

            Assert.True(index.DataSize == (ulong)data.Length, "34A-1: attachment index records correct data size");
            Assert.True(index.LogTime == 1000 && index.CreateTime == 2000, "34A-1b: attachment index records correct times");
            Assert.True(index.Name == "test.txt" && index.MediaType == "text/plain",
                "34A-1c: attachment index records correct name and media type");

            var reader = new McapReader(ms);
            var attachment = reader.ReadAttachmentAt(index.Offset);

            Assert.True(attachment.LogTime == 1000, "34A-1d: read-back log time matches");
            Assert.True(attachment.CreateTime == 2000, "34A-1e: read-back create time matches");
            Assert.True(attachment.Name == "test.txt", "34A-1f: read-back name matches");
            Assert.True(attachment.MediaType == "text/plain", "34A-1g: read-back media type matches");
            Assert.True(attachment.Data.Length == data.Length, "34A-1h: read-back data size matches");
            Assert.True(Encoding.UTF8.GetString(attachment.Data) == "hello attachment", "34A-1i: read-back data content matches");
            Assert.True(attachment.Crc != 0, "34A-1j: attachment CRC is non-zero");
            Assert.True(attachment.CrcValid, "34A-1k: attachment CRC is valid");
        }

        [Fact]
        public void AttachmentIndexSummary()
        {
            using var ms = new MemoryStream();
            var writer = new McapWriter(ms, leaveOpen: true);
            writer.WriteMagic();
            writer.WriteHeader("", "test");

            var data = Encoding.UTF8.GetBytes("summary test");
            var wIndex = writer.WriteAttachment(500, 600, "info.json", "application/json", data);

            writer.WriteDataEnd();
            var sumStart = (ulong)ms.Position;
            var attIdxGrpRelStart = 0UL;
            writer.WriteAttachmentIndex(wIndex);
            var attIdxGrpLen = (ulong)ms.Position - sumStart - attIdxGrpRelStart;

            var sumOffStart = (ulong)ms.Position;
            if (attIdxGrpLen > 0)
                writer.WriteSummaryOffset(0x0A, sumStart + attIdxGrpRelStart, attIdxGrpLen);

            writer.WriteFooter(sumStart, sumOffStart, 0);
            writer.WriteMagic();
            writer.Flush();
            ms.Position = 0;

            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.True(summary.AttachmentIndexes.Count == 1, "34A-2: summary contains one attachment index");
            var ai = summary.AttachmentIndexes[0];
            Assert.True(ai.Name == "info.json", "34A-2b: attachment index name matches");
            Assert.True(ai.DataSize == (ulong)data.Length, "34A-2c: attachment index data size matches");
        }

        [Fact]
        public void AttachmentCrcMismatch()
        {
            var allBytes = Array.Empty<byte>();
            McapAttachmentIndex index;
            using (var ms = new MemoryStream())
            {
                var writer = new McapWriter(ms, leaveOpen: true);
                writer.WriteMagic();
                writer.WriteHeader("", "test");

                var data = Encoding.UTF8.GetBytes("corrupt me");
                index = writer.WriteAttachment(100, 200, "x.bin", "application/octet-stream", data);

                writer.WriteDataEnd();
                writer.WriteFooter((ulong)ms.Position, (ulong)ms.Position, 0);
                writer.WriteMagic();
                writer.Flush();
                allBytes = ms.ToArray();
            }

            using (var cleanMs = new MemoryStream(allBytes))
            {
                var cleanReader = new McapReader(cleanMs);
                var cleanAttachment = cleanReader.ReadAttachmentAt(index.Offset);
                var prefixSize = 8 + 8
                    + (4 + Encoding.UTF8.GetByteCount(cleanAttachment.Name))
                    + (4 + Encoding.UTF8.GetByteCount(cleanAttachment.MediaType))
                    + 8;
                var contentStart = (int)index.Offset + 1 + 8;
                allBytes[contentStart + prefixSize + 1] ^= 0xFF;
            }

            using var ms2 = new MemoryStream(allBytes);
            var reader = new McapReader(ms2);
            var attachment = reader.ReadAttachmentAt(index.Offset);

            Assert.True(!attachment.CrcValid, "34A-3: corrupted attachment CRC is detected as invalid");
        }

        [Fact]
        public void AttachmentTruncatedContentRejected()
        {
            byte[] allBytes;
            McapAttachmentIndex index;
            using (var wrMs = new MemoryStream())
            {
                var writer = new McapWriter(wrMs, leaveOpen: true);
                writer.WriteMagic();
                writer.WriteHeader("", "test");
                index = writer.WriteAttachment(100, 200, "x", "t", new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE });
                writer.WriteDataEnd();
                writer.WriteMagic();
                writer.Flush();
                allBytes = wrMs.ToArray();
            }

            var contentStart = (int)index.Offset + 1 + 8;
            var dsOff = contentStart + 8 + 8 + 4 + 1 + 4 + 1;
            var fakeSize = (ulong)100;
            allBytes[dsOff] = (byte)fakeSize;
            allBytes[dsOff + 1] = (byte)(fakeSize >> 8);
            allBytes[dsOff + 7] = (byte)(fakeSize >> 56);

            using var ms = new MemoryStream(allBytes);
            var reader = new McapReader(ms);
            try
            {
                reader.ReadAttachmentAt(index.Offset);
                Assert.Fail("34A-4: truncated attachment should throw");
            }
            catch (InvalidDataException ex)
            {
                Assert.True(ex.Message.Contains("data extends past"),
                    "34A-4: truncated attachment content rejected");
            }
        }

        [Fact]
        public void AttachmentOversizedDataRejected()
        {
            byte[] allBytes;
            McapAttachmentIndex index;
            using (var ms = new MemoryStream())
            {
                var writer = new McapWriter(ms, leaveOpen: true);
                writer.WriteMagic();
                writer.WriteHeader("", "test");
                index = writer.WriteAttachment(100, 200, "big", "application/octet-stream", new byte[3]);
                writer.WriteDataEnd();
                writer.WriteMagic();
                writer.Flush();
                allBytes = ms.ToArray();
            }

            using (var cleanMs = new MemoryStream(allBytes))
            {
                var cleanReader = new McapReader(cleanMs);
                var cleanAtt = cleanReader.ReadAttachmentAt(index.Offset);
                var contentStart = (int)index.Offset + 1 + 8;
                var dsOff = contentStart + 8 + 8
                    + 4 + Encoding.UTF8.GetByteCount(cleanAtt.Name)
                    + 4 + Encoding.UTF8.GetByteCount(cleanAtt.MediaType);

                var maxValue = (ulong)int.MaxValue;
                allBytes[dsOff] = (byte)maxValue;
                allBytes[dsOff + 1] = (byte)(maxValue >> 8);
                allBytes[dsOff + 2] = (byte)(maxValue >> 16);
                allBytes[dsOff + 3] = (byte)(maxValue >> 24);
                allBytes[dsOff + 4] = (byte)(maxValue >> 32);
                allBytes[dsOff + 5] = (byte)(maxValue >> 40);
                allBytes[dsOff + 6] = (byte)(maxValue >> 48);
                allBytes[dsOff + 7] = (byte)(maxValue >> 56);
            }

            using var ms2 = new MemoryStream(allBytes);
            var reader = new McapReader(ms2);
            try
            {
                reader.ReadAttachmentAt(index.Offset);
                Assert.Fail("34A-5: oversized attachment data_size should throw");
            }
            catch (InvalidDataException ex)
            {
                Assert.True(ex.Message.Contains("data extends past"),
                    "34A-5: oversized attachment data_size rejected");
            }
        }

        [Fact]
        public void RecorderAttachmentStatistics()
        {
            using var ms = new MemoryStream();
            var logger = new ConsoleLogger();
            using var recorder = new McapRecorder(ms, logger);

            var data = Encoding.UTF8.GetBytes("recorder attachment data");
            recorder.AddAttachment("recorder.txt", "text/plain", data, 3000, 0);
            recorder.Close();

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.True(summary.Statistics != null, "34B-1: summary has statistics");
            Assert.True(summary.Statistics.AttachmentCount == 1, "34B-1b: attachment count is 1");
            Assert.True(summary.AttachmentIndexes.Count == 1, "34B-1c: summary has attachment index");
            Assert.True(summary.AttachmentIndexes[0].Name == "recorder.txt", "34B-1d: attachment index name matches");
        }

        [Fact]
        public void EmptyMessageStatistics()
        {
            using var ms = new MemoryStream();
            var logger = new ConsoleLogger();
            using var recorder = new McapRecorder(ms, logger);

            var data = Encoding.UTF8.GetBytes("attachment only, no messages");
            recorder.AddAttachment("empty.txt", "text/plain", data, 100, 200);
            recorder.Close();

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.True(summary.Statistics.MessageCount == 0, "34B-2: message count is 0");
            Assert.True(summary.Statistics.MessageStartTime == 0, "34B-2b: message start time is 0 for empty recording");
            Assert.True(summary.Statistics.MessageEndTime == 0, "34B-2c: message end time is 0 for empty recording");
        }

        [Fact]
        public void MixedRecordingReplayStillWorks()
        {
            using var ms = new MemoryStream();
            var logger = new ConsoleLogger();
            using var recorder = new McapRecorder(ms, logger);

            recorder.AddChannel(1, "/test", "json", "test.Schema", "jsonschema", "{\"type\":\"object\"}");
            var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
            recorder.WriteMessage(1, 1000, payload);
            recorder.WriteMessage(1, 2000, payload);

            var data = Encoding.UTF8.GetBytes("mixed attachment");
            recorder.AddAttachment("mixed.txt", "text/plain", data, 1500, 0);

            recorder.Close();

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.True(summary.Statistics.MessageCount == 2, "34B-3: mixed recording still has 2 messages");
            Assert.True(summary.Statistics.AttachmentCount == 1, "34B-3b: mixed recording has 1 attachment");
            Assert.True(summary.AttachmentIndexes.Count == 1, "34B-3c: mixed recording summary has attachment index");
            Assert.True(summary.ChunkIndexes.Count > 0, "34B-3d: mixed recording has chunk indexes");

            var ci = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength, out _);
            var messages = reader.ReadChunkMessages(records);
            Assert.True(messages.Count == 2, "34B-3e: chunk still has 2 messages");
        }

        [Fact]
        public void SummaryCrcRoundtrip()
        {
            using var ms = new MemoryStream();
            var logger = new ConsoleLogger();
            using var recorder = new McapRecorder(ms, logger);

            recorder.AddChannel(1, "/crc_test", "json", "test.Crc", "jsonschema", "{\"type\":\"object\"}");
            var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
            recorder.WriteMessage(1, 1000, payload);
            recorder.Close();

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.True(summary.Schemas.Count > 0, "34C-1: summary CRC roundtrip succeeds — schemas loaded");
            Assert.True(summary.Statistics != null, "34C-1b: summary CRC roundtrip succeeds — statistics loaded");
        }

        [Fact]
        public void SummaryCrcMismatchRejected()
        {
            using var ms = new MemoryStream();
            var logger = new ConsoleLogger();
            using (var recorder = new McapRecorder(ms, logger))
            {
                recorder.AddChannel(1, "/bad_crc", "json", "test.CrcFail", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 1000, Encoding.UTF8.GetBytes("{}"));
                recorder.Close();
            }

            var allBytes = ms.ToArray();
            var footerIdx = allBytes.Length
                - McapWriter.MagicLength
                - McapWriter.RecordHeaderLength
                - McapWriter.FooterContentLength;
            var footer = new McapFooter
            {
                SummaryStart = allBytes[footerIdx + 1 + 8 + 0] | ((ulong)allBytes[footerIdx + 1 + 8 + 1] << 8)
                    | ((ulong)allBytes[footerIdx + 1 + 8 + 2] << 16) | ((ulong)allBytes[footerIdx + 1 + 8 + 3] << 24)
                    | ((ulong)allBytes[footerIdx + 1 + 8 + 4] << 32) | ((ulong)allBytes[footerIdx + 1 + 8 + 5] << 40)
                    | ((ulong)allBytes[footerIdx + 1 + 8 + 6] << 48) | ((ulong)allBytes[footerIdx + 1 + 8 + 7] << 56)
            };
            if (footer.SummaryStart > 0)
                allBytes[(int)footer.SummaryStart + 2] ^= 0xFF;

            ms.Position = 0;
            ms.Write(allBytes, 0, allBytes.Length);
            ms.SetLength(allBytes.Length);
            ms.Position = 0;

            try
            {
                var reader = new McapReader(ms);
                reader.ReadSummary();
                Assert.Fail("34C-2: corrupted summary CRC should throw");
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is EndOfStreamException)
            {
                Assert.True(true, "34C-2: corrupted summary is rejected");
            }
        }

        [Fact]
        public void ZeroSummaryCrcBackwardCompatibility()
        {
            using var ms = new MemoryStream();
            var writer = new McapWriter(ms, leaveOpen: true);
            writer.WriteMagic();
            writer.WriteHeader("", "test");
            writer.WriteDataEnd();

            var sumStart = (ulong)ms.Position;
            var schemaRelStart = 0UL;
            writer.WriteSchema(1, "test.OldSchema", "jsonschema", Encoding.UTF8.GetBytes("{\"type\":\"object\"}"));
            var schemaLen = (ulong)ms.Position - sumStart - schemaRelStart;
            var sumOffStart = (ulong)ms.Position;
            if (schemaLen > 0) writer.WriteSummaryOffset(0x03, sumStart + schemaRelStart, schemaLen);
            writer.WriteFooter(sumStart, sumOffStart, 0);
            writer.WriteMagic();
            writer.Flush();
            ms.Position = 0;

            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Assert.True(summary.Schemas.Count == 1, "34C-3: zero-CRC file loads without error");
            Assert.True(summary.Schemas[0].Name == "test.OldSchema", "34C-3b: zero-CRC file schema name is correct");
        }

        [Fact]
        public void ZeroSummaryCrcRejectsInvalidSummaryStart()
        {
            using var ms = new MemoryStream();
            var writer = new McapWriter(ms, leaveOpen: true);
            writer.WriteMagic();
            writer.WriteHeader("", "test");
            writer.WriteDataEnd();

            var sumStart = (ulong)ms.Position;
            var schemaRelStart = 0UL;
            writer.WriteSchema(1, "test.BadFooter", "jsonschema", Encoding.UTF8.GetBytes("{\"type\":\"object\"}"));
            var schemaLen = (ulong)ms.Position - sumStart - schemaRelStart;
            var sumOffStart = (ulong)ms.Position;
            writer.WriteSummaryOffset(0x03, sumStart + schemaRelStart, schemaLen);
            writer.WriteFooter(sumStart, sumOffStart, 0);
            writer.WriteMagic();
            writer.Flush();

            var allBytes = ms.ToArray();
            var footerIdx = allBytes.Length
                - McapWriter.MagicLength
                - McapWriter.RecordHeaderLength
                - McapWriter.FooterContentLength;
            var invalidSummaryStart = (ulong)(allBytes.Length - 1);
            WriteU64LE(allBytes, footerIdx + 1 + 8, invalidSummaryStart);

            using var corrupt = new MemoryStream(allBytes);
            try
            {
                var reader = new McapReader(corrupt);
                reader.ReadSummary();
                Assert.Fail("34C-4: zero-CRC invalid summary_start should throw");
            }
            catch (InvalidDataException ex)
            {
                Assert.True(ex.Message.Contains("summary_start"),
                    "34C-4: zero-CRC invalid summary_start rejected");
            }
        }

        private static void WriteU64LE(byte[] bytes, int offset, ulong value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
            bytes[offset + 4] = (byte)(value >> 32);
            bytes[offset + 5] = (byte)(value >> 40);
            bytes[offset + 6] = (byte)(value >> 48);
            bytes[offset + 7] = (byte)(value >> 56);
        }
    }
}
