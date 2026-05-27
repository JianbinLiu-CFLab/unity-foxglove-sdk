// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 117 validation for MCAP spec parity matrix and local direct-message fallback.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase117Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 117: MCAP Spec Parity And Direct Fallback ===");
            _passed = 0;

            VerifyParityMatrix();
            VerifySummarylessDirectMessages();
            VerifySummaryPresentDirectFallback();
            VerifyUnknownPrivateAndInvalidRecords();
            VerifyMalformedRecordBoundaries();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 117: {_passed} checks passed.");
        }

        private static void VerifyParityMatrix()
        {
            const string matrixPath = "Developer/102 Phase117 MCAP Spec Parity Matrix.md";
            if (!File.Exists(RepoPath(matrixPath)))
            {
                Console.WriteLine("[INFO] 117-A1..A4 skipped: local Developer parity matrix is absent; direct MCAP behavior checks continue.");
                return;
            }

            var matrix = ReadRepoText(matrixPath);
            foreach (var opcode in new[]
            {
                "0x01", "0x02", "0x03", "0x04", "0x05", "0x06", "0x07", "0x08",
                "0x09", "0x0A", "0x0B", "0x0C", "0x0D", "0x0E", "0x0F",
                "0x80-0xFF", "0x00"
            })
            {
                Check(matrix.Contains(opcode, StringComparison.Ordinal),
                    "117-A1: parity matrix covers opcode " + opcode);
            }

            Check(matrix.Contains("chunk_size", StringComparison.Ordinal)
                  && matrix.Contains("compression", StringComparison.Ordinal)
                  && matrix.Contains("enable_data_crcs", StringComparison.Ordinal),
                "117-A2: parity matrix records official writer option stance");
            Check(matrix.Contains("https://mcap.dev/spec", StringComparison.Ordinal)
                  && matrix.Contains("https://mcap.dev/docs/python/mcap-apidoc/mcap.writer", StringComparison.Ordinal),
                "117-A3: parity matrix links official MCAP references");
            Check(matrix.Contains("No remote DataLoader", StringComparison.Ordinal),
                "117-A4: parity matrix records non-goals");
        }

        private static void VerifySummarylessDirectMessages()
        {
            using var ms = CreateDirectMcap(includeSummary: false, includeDataEnd: false, includeUnknownRecords: true);
            using var indexed = new McapIndexedReader(ms, leaveOpen: true);

            Check(indexed.Summary != null, "117-B1: summary-less direct MCAP opens with synthetic summary");
            Check(indexed.Channels.Count == 2, "117-B2: direct scan collects channels");
            Check(indexed.Schemas.Count == 1, "117-B3: direct scan collects schemas");
            Check(indexed.MetadataIndexes.Count == 1, "117-B4: direct scan synthesizes metadata index");
            Check(indexed.AttachmentIndexes.Count == 1, "117-B5: direct scan synthesizes attachment index");
            Check(indexed.Summary.Statistics != null && indexed.Summary.Statistics.MessageCount == 4,
                "117-B6: direct scan synthesizes message statistics");

            CheckTimes(indexed.ReadMessages(), new ulong[] { 10, 20, 30, 40 },
                "117-C1: direct fallback reads all summary-less messages");
            CheckTimes(indexed.ReadMessages(new McapReadOptions { Topics = new List<string> { "/phase117/a" } }),
                new ulong[] { 10, 30 },
                "117-C2: direct fallback filters by topic");
            CheckTimes(indexed.ReadMessages(new McapReadOptions { ChannelIds = new List<ushort> { 2 } }),
                new ulong[] { 20, 40 },
                "117-C3: direct fallback filters by channel ID");
            CheckTimes(indexed.ReadMessages(new McapReadOptions { StartTimeNs = 20, EndTimeNs = 30 }),
                new ulong[] { 20, 30 },
                "117-C4: direct fallback filters by inclusive time range");
            CheckTimes(indexed.ReadMessages(new McapReadOptions { MaxMessages = 2 }),
                new ulong[] { 30, 40 },
                "117-C5: direct fallback MaxMessages keeps latest chronological messages");

            ms.Position = 0;
            using var loader = new McapDataLoader(ms, leaveOpen: true);
            var initialization = loader.Initialize();
            Check(initialization.Channels.Count == 2 && initialization.HasTotalMessageCount,
                "117-C6: DataLoader initializes summary-less direct MCAP");
            CheckTimes(loader.CreateIterator(new McapDataLoaderQuery { Topics = new List<string> { "/phase117/b" } }).ToList(),
                new ulong[] { 20, 40 },
                "117-C7: DataLoader iterates direct fallback messages without API changes");
        }

        private static void VerifySummaryPresentDirectFallback()
        {
            using var ms = CreateDirectMcap(includeSummary: true, includeDataEnd: true, includeUnknownRecords: true);
            using var indexed = new McapIndexedReader(ms, leaveOpen: true);

            Check(indexed.Summary.ChunkIndexes.Count == 0, "117-D1: direct fixture has no chunk indexes");
            Check(indexed.Summary.Statistics != null && indexed.Summary.Statistics.MessageCount == 4,
                "117-D2: direct fixture summary statistics are available");
            CheckTimes(indexed.ReadMessages(new McapReadOptions
            {
                Topics = new List<string> { "/phase117/a" },
                ChannelIds = new List<ushort> { 2 }
            }), new ulong[] { 10, 20, 30, 40 },
                "117-D3: direct fallback preserves topic/channel union semantics");
        }

        private static void VerifyUnknownPrivateAndInvalidRecords()
        {
            using (var privateSummary = CreateDirectMcap(includeSummary: true, includeDataEnd: true, includeUnknownRecords: true))
            using (var indexed = new McapIndexedReader(privateSummary, leaveOpen: true))
            {
                Check(indexed.ReadMessages().Count == 4,
                    "117-E1: private and unknown official-range records are skipped safely");
            }

            using var invalid = CreateInvalidOpcodeMcap();
            Check(Throws<InvalidDataException>(() => new McapIndexedReader(invalid, leaveOpen: true)),
                "117-E2: opcode 0x00 is rejected as malformed");
        }

        private static void VerifyMalformedRecordBoundaries()
        {
            Check(Throws<InvalidDataException>(() => new McapIndexedReader(CreateDataRecordCrossingFooterMcap(), leaveOpen: false)),
                "117-F1: data-section record crossing footer bounds is rejected");
            Check(Throws<InvalidDataException>(() => new McapIndexedReader(CreateOversizedRecordMcap(), leaveOpen: false)),
                "117-F2: oversized data-section record is rejected");
            Check(Throws<InvalidDataException>(() => new McapIndexedReader(CreateBadSummaryStartMcap(), leaveOpen: false)),
                "117-F3: footer summary_start outside file bounds remains rejected");
        }

        private static void VerifyValidationWiring()
        {
            var validationRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(validationRegistry.Contains("--phase117", StringComparison.Ordinal)
                  && validationRegistry.Contains("Phase117Validation.Validate", StringComparison.Ordinal),
                "117-G1: validation registry wires --phase117");
            Check(project.Contains("Phase117Validation.cs", StringComparison.Ordinal),
                "117-G2: runtime test project compiles Phase117Validation");
        }

        private static MemoryStream CreateDirectMcap(bool includeSummary, bool includeDataEnd, bool includeUnknownRecords)
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase117-direct");
                if (includeUnknownRecords)
                    writer.WriteRecord(0x80, Encoding.UTF8.GetBytes("private-data"));
                writer.WriteSchema(1, "phase117.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase117/a", "json", new Dictionary<string, string>());
                writer.WriteChannel(2, 1, "/phase117/b", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                writer.WriteMessage(2, 1, 20, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                writer.WriteMetadata("phase117.metadata", new Dictionary<string, string> { ["value"] = "ok" });
                writer.WriteAttachment(25, 0, "phase117.txt", "text/plain", Encoding.UTF8.GetBytes("phase117"));
                if (includeUnknownRecords)
                    writer.WriteRecord(0x10, Encoding.UTF8.GetBytes("future-official"));
                writer.WriteMessage(1, 2, 30, 30, Encoding.UTF8.GetBytes("{\"a\":30}"));
                writer.WriteMessage(2, 2, 40, 40, Encoding.UTF8.GetBytes("{\"b\":40}"));
                if (includeDataEnd)
                    writer.WriteDataEnd();

                var summaryStart = includeSummary ? (ulong)writer.Position : 0UL;
                if (includeSummary)
                {
                    writer.WriteSchema(1, "phase117.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                    writer.WriteChannel(1, 1, "/phase117/a", "json", new Dictionary<string, string>());
                    writer.WriteChannel(2, 1, "/phase117/b", "json", new Dictionary<string, string>());
                    writer.WriteStatistics(
                        4,
                        1,
                        2,
                        1,
                        1,
                        0,
                        10,
                        40,
                        new Dictionary<ushort, ulong> { [1] = 2, [2] = 2 });
                    if (includeUnknownRecords)
                        writer.WriteRecord(0x80, Encoding.UTF8.GetBytes("summary-private"));
                }

                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateInvalidOpcodeMcap()
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase117-invalid");
                writer.WriteRecord(0x00, new byte[0]);
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateDataRecordCrossingFooterMcap()
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase117-crossing");
                writer.WriteBytes(new byte[] { 0x80 });
                WriteU64(ms, 8);
                writer.WriteBytes(new byte[] { 1, 2, 3, 4 });
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateOversizedRecordMcap()
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase117-oversized");
                writer.WriteBytes(new byte[] { 0x80 });
                WriteU64(ms, McapReader.DefaultRecordSizeLimit + 1);
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            ms.Position = 0;
            return ms;
        }

        private static MemoryStream CreateBadSummaryStartMcap()
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase117-bad-summary");
                writer.WriteFooter(ulong.MaxValue, 0, 0);
                writer.WriteMagic();
            }

            ms.Position = 0;
            return ms;
        }

        private static void CheckTimes(List<McapMessage> messages, ulong[] expected, string name)
        {
            var actual = messages.Select(m => m.LogTime).ToArray();
            Check(actual.SequenceEqual(expected), name);
        }

        private static void CheckTimes(List<McapDataLoaderMessage> messages, ulong[] expected, string name)
        {
            var actual = messages.Select(m => m.LogTime).ToArray();
            Check(actual.SequenceEqual(expected), name);
        }

        private static void WriteU64(Stream stream, ulong value)
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

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase117 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
