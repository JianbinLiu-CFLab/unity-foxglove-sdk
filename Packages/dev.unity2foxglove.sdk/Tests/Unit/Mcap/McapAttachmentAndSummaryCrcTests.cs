// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP attachment records and summary CRC behavior (migrated from Phase34Validation).

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    public partial class McapAttachmentAndSummaryCrcTests
    {
        [Fact]
        public void AmendmentWriterAllowsConcurrentReadersAndDisposeSwallowsCloseFailures()
        {
            var source = File.ReadAllText(RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapAmendmentWriter.cs"))
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains("FileMode.Open, FileAccess.Read, FileShare.Read", source, StringComparison.Ordinal);
            Assert.Matches(
                new Regex(@"public void Dispose\(\).*?try\s*\{\s*Close\(\);\s*\}\s*catch\s*\{", RegexOptions.Singleline),
                source);
        }

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
            var allBytes = CreateCorruptAttachmentFile(out var index);
            using var ms2 = new MemoryStream(allBytes);
            var reader = new McapReader(ms2);
            var attachment = reader.ReadAttachmentAt(index.Offset, validateCrcs: false);

            Assert.True(!attachment.CrcValid, "34A-3: corrupted attachment CRC is detected as invalid");
        }

        [Fact]
        public void AttachmentCrcMismatchIsRejectedByDefault()
        {
            var bytes = CreateCorruptAttachmentFile(out var index);
            using var stream = new MemoryStream(bytes, writable: false);
            Assert.Throws<InvalidDataException>(() => new McapReader(stream).ReadAttachmentAt(index.Offset));
        }

        private static byte[] CreateCorruptAttachmentFile(out McapAttachmentIndex index)
        {
            byte[] allBytes;
            using (var ms = new MemoryStream())
            {
                var writer = new McapWriter(ms, leaveOpen: true);
                writer.WriteMagic();
                writer.WriteHeader("", "test");
                index = writer.WriteAttachment(
                    100,
                    200,
                    "x.bin",
                    "application/octet-stream",
                    Encoding.UTF8.GetBytes("corrupt me"));
                writer.WriteDataEnd();
                writer.WriteFooter((ulong)ms.Position, (ulong)ms.Position, 0);
                writer.WriteMagic();
                writer.Flush();
                allBytes = ms.ToArray();
            }

            using (var cleanMs = new MemoryStream(allBytes))
            {
                var cleanAttachment = new McapReader(cleanMs).ReadAttachmentAt(index.Offset);
                var payloadOffset = IndexOf(allBytes, cleanAttachment.Data);
                Assert.True(payloadOffset >= 0, "34A-3 precondition: attachment payload bytes are present in the MCAP file");
                allBytes[payloadOffset + 1] ^= 0xFF;
            }

            return allBytes;
        }

        [Fact]
        public void AttachmentCrcReadsHighBitWithoutManualShift()
        {
            using var content = new MemoryStream();
            WriteU64LE(content, 100);
            WriteU64LE(content, 200);
            WriteString(content, "x.bin");
            WriteString(content, "application/octet-stream");
            WriteU64LE(content, 0);
            WriteU32LE(content, 0x80000000);

            var attachment = McapRecordDecoder.DecodeAttachment(content.ToArray());

            Assert.Equal(0x80000000u, attachment.Crc);
            Assert.False(attachment.CrcValid);
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
            WriteU64LE(allBytes, dsOff, fakeSize);

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
                WriteU64LE(allBytes, dsOff, maxValue);
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
            Assert.True(footer.SummaryStart > 0, "34C-2 precondition: SummaryStart must be non-zero for a recording with messages");
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

    }
}
