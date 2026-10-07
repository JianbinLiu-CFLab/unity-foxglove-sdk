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


public partial class McapAttachmentAndSummaryCrcTests
    {
        [Fact]
        public void SummaryAllocationHonorsCumulativeLimit()
        {
            using var stream = new MemoryStream();
            using (var recorder = new McapRecorder(stream, new ConsoleLogger()))
            {
                recorder.AddChannel(1, "/summary-limit", "json", "summary.Limit", "jsonschema", "{}");
                recorder.Close();
            }

            stream.Position = 0;
            Assert.Throws<InvalidDataException>(() =>
                new McapReader(stream).ReadSummary(summarySizeLimit: 1));

            stream.Position = 0;
            Assert.Throws<InvalidDataException>(() =>
                new McapIndexedReader(
                    stream,
                    leaveOpen: true,
                    sequentialReadLimits: null,
                    readOptions: new McapReadOptions { SummarySizeLimit = 1 }));

            stream.Position = 0;
            Assert.Throws<InvalidDataException>(() =>
                McapStrictValidator.Validate(
                    stream,
                    new McapStrictValidationOptions { SummarySizeLimit = 1 }));
        }

        [Fact]
        public void SummaryCrcOptOutIsHonoredForSummaryPresentFiles()
        {
            var bytes = CreateSummaryWithBadCrc();

            using var rejecting = new MemoryStream(bytes, writable: false);
            Assert.Throws<InvalidDataException>(() => new McapReader(rejecting).ReadSummary(validateCrcs: true));

            using var permissive = new MemoryStream(bytes, writable: false);
            var summary = new McapReader(permissive).ReadSummary(validateCrcs: false);
            Assert.NotEmpty(summary.Schemas);
        }

        [Fact]
        public void StrictSummaryCrcOptOutIsHonored()
        {
            var bytes = CreateSummaryWithBadCrc();

            using var rejecting = new MemoryStream(bytes, writable: false);
            Assert.Throws<InvalidDataException>(() => McapStrictValidator.Validate(rejecting));

            using var permissive = new MemoryStream(bytes, writable: false);
            var summary = McapStrictValidator.Validate(
                permissive,
                new McapStrictValidationOptions { ValidateCrcs = false });
            Assert.NotEmpty(summary.Schemas);
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

        private static byte[] CreateSummaryWithBadCrc()
        {
            using var stream = new MemoryStream();
            var logger = new ConsoleLogger();
            using (var recorder = new McapRecorder(stream, logger))
            {
                recorder.AddChannel(1, "/round4/f02/crc", "json", "round4.Crc", "jsonschema", "{}");
                recorder.WriteMessage(1, 1, Encoding.UTF8.GetBytes("{}"));
                recorder.Close();
            }

            var bytes = stream.ToArray();
            var footerOffset = bytes.Length - McapWriter.MagicLength - McapWriter.RecordHeaderLength - McapWriter.FooterContentLength;
            var summaryCrcOffset = footerOffset + 1 + sizeof(ulong) + sizeof(ulong) + sizeof(ulong);
            bytes[summaryCrcOffset] = 0x12;
            bytes[summaryCrcOffset + 1] = 0x34;
            bytes[summaryCrcOffset + 2] = 0x56;
            bytes[summaryCrcOffset + 3] = 0x78;
            return bytes;
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

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            if (haystack == null || needle == null || needle.Length == 0 || needle.Length > haystack.Length)
                return -1;

            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var matches = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return i;
            }

            return -1;
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteU32LE(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
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

        private static string RepoPath(string relativePath)
        {
            var explicitRoot = Environment.GetEnvironmentVariable("UNITY2FOXGLOVE_REPO_ROOT");
            if (!string.IsNullOrWhiteSpace(explicitRoot))
                return Path.Combine(Path.GetFullPath(explicitRoot), relativePath.Replace('/', Path.DirectorySeparatorChar));

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
