// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 122 validation for MCAP writer option parity.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase122Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 122: MCAP Writer Options Parity ===");
            _passed = 0;

            VerifyOptionSurface();
            VerifyDefaultLayout();
            VerifyDirectLayout();
            VerifySummaryAndIndexGates();
            VerifyCrcGates();
            VerifyConformanceWriterMapping();

            Console.WriteLine($"Phase 122: {_passed} checks passed.");
        }

        private static void VerifyOptionSurface()
        {
            Check(McapWriterOptions.DefaultChunkSizeBytes == McapRecorder.DefaultChunkSizeBytes,
                "122-A1: writer option default chunk size matches recorder default");

            var defaults = McapWriterOptions.Normalize(null);
            Check(defaults.UseChunking
                  && defaults.IndexTypes == McapIndexTypes.All
                  && defaults.RepeatChannels
                  && defaults.RepeatSchemas
                  && defaults.UseStatistics
                  && defaults.UseSummaryOffsets
                  && defaults.EnableCrcs
                  && !defaults.EnableDataCrcs,
                "122-A2: writer option defaults preserve current recording layout");

            var normalized = McapWriterOptions.Normalize(new McapWriterOptions
            {
                ChunkSizeBytes = -1,
                Compression = null,
                UseChunking = false,
                IndexTypes = McapIndexTypes.All
            });
            Check(normalized.ChunkSizeBytes == McapRecorder.DefaultChunkSizeBytes
                  && normalized.Compression == ""
                  && !normalized.HasIndex(McapIndexTypes.Chunk)
                  && !normalized.HasIndex(McapIndexTypes.Message)
                  && normalized.HasIndex(McapIndexTypes.Attachment)
                  && normalized.HasIndex(McapIndexTypes.Metadata),
                "122-A3: normalization copies values, fixes chunk size, and masks chunk-only indexes");

            var threw = false;
            try
            {
                McapWriterOptions.Normalize(new McapWriterOptions { Compression = "brotli" });
            }
            catch (NotSupportedException)
            {
                threw = true;
            }
            Check(threw, "122-A4: invalid compression fails clearly");

            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var controller = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs");
            Check(runtime.Contains("EnableRecording(string filePath, McapWriterOptions options", StringComparison.Ordinal)
                  && controller.Contains("Enable(string filePath, McapWriterOptions options", StringComparison.Ordinal),
                "122-A5: runtime and recording controller expose options overloads");
        }

        private static void VerifyDefaultLayout()
        {
            var records = Parse(CreateSample(new McapWriterOptions { ChunkSizeBytes = 96 }));
            Check(Count(records, McapWriter.OpcodeChunk) == 1,
                "122-B1: default recorder still writes chunk records");
            Check(Count(records, McapWriter.OpcodeMessageIndex) == 1,
                "122-B2: default recorder still writes message indexes");
            Check(Count(records, McapWriter.OpcodeChunkIndex) == 1,
                "122-B3: default recorder still writes chunk indexes");
            Check(Count(records, McapWriter.OpcodeSchema) == 2
                  && Count(records, McapWriter.OpcodeChannel) == 2
                  && Count(records, McapWriter.OpcodeStatistics) == 1
                  && Count(records, McapWriter.OpcodeSummaryOffset) >= 4,
                "122-B4: default recorder still writes repeated schema/channel, statistics, and summary offsets");
            Check(ReadDataEndCrc(records) == 0,
                "122-B5: default recorder preserves zero DataEnd CRC");
            Check(McapRecordReader.DecodeFooter(records.Last(r => r.Opcode == McapWriter.OpcodeFooter).Content).summaryCrc != 0,
                "122-B6: default recorder keeps summary CRC enabled");
        }

        private static void VerifyDirectLayout()
        {
            var options = new McapWriterOptions
            {
                UseChunking = false,
                IndexTypes = McapIndexTypes.All,
                EnableDataCrcs = true,
                UseStatistics = true
            };
            var bytes = CreateSample(options);
            var records = Parse(bytes);
            Check(Count(records, McapWriter.OpcodeMessage) == 1
                  && Count(records, McapWriter.OpcodeChunk) == 0
                  && Count(records, McapWriter.OpcodeMessageIndex) == 0
                  && Count(records, McapWriter.OpcodeChunkIndex) == 0,
                "122-C1: UseChunking=false writes direct Message records without chunk-only indexes");
            Check(ReadDataEndCrc(records) != 0,
                "122-C2: EnableDataCrcs writes non-zero DataEnd CRC");

            var path = Path.Combine(Path.GetTempPath(), "phase122-direct-" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllBytes(path, bytes);
            using var reader = McapIndexedReader.OpenRead(path);
            var messages = reader.ReadMessages(new McapReadOptions { Topics = new System.Collections.Generic.List<string> { "/phase122/direct" } });
            Check(messages.Count == 1 && Encoding.UTF8.GetString(messages[0].Data) == "{\"ok\":true}",
                "122-C3: direct-message files remain readable through local MCAP reader");
        }

        private static void VerifySummaryAndIndexGates()
        {
            var noIndexes = Parse(CreateSample(new McapWriterOptions
            {
                ChunkSizeBytes = 96,
                IndexTypes = McapIndexTypes.None
            }));
            Check(Count(noIndexes, McapWriter.OpcodeMessageIndex) == 0
                  && Count(noIndexes, McapWriter.OpcodeChunkIndex) == 0,
                "122-D1: IndexTypes.None disables message and chunk indexes");

            var emptySummary = Parse(CreateSample(new McapWriterOptions
            {
                ChunkSizeBytes = 96,
                IndexTypes = McapIndexTypes.None,
                RepeatSchemas = false,
                RepeatChannels = false,
                UseStatistics = false,
                UseSummaryOffsets = false
            }));
            var footer = McapRecordReader.DecodeFooter(emptySummary.Last(r => r.Opcode == McapWriter.OpcodeFooter).Content);
            Check(footer.summaryStart == 0 && footer.summaryOffsetStart == 0,
                "122-D2: empty summary writes zero summary_start and summary_offset_start");
            Check(Count(emptySummary, McapWriter.OpcodeSummaryOffset) == 0,
                "122-D3: UseSummaryOffsets=false omits summary offset records");

            var partial = Parse(CreateSample(new McapWriterOptions
            {
                ChunkSizeBytes = 96,
                IndexTypes = McapIndexTypes.Chunk,
                RepeatSchemas = false,
                RepeatChannels = true,
                UseStatistics = false,
                UseSummaryOffsets = true
            }));
            Check(Count(partial, McapWriter.OpcodeSchema) == 1
                  && Count(partial, McapWriter.OpcodeChannel) == 2
                  && Count(partial, McapWriter.OpcodeStatistics) == 0
                  && Count(partial, McapWriter.OpcodeChunkIndex) == 1,
                "122-D4: repeated schema/channel, statistics, and chunk index gates are independent");
        }

        private static void VerifyCrcGates()
        {
            var crcOff = Parse(CreateSample(new McapWriterOptions
            {
                ChunkSizeBytes = 96,
                EnableCrcs = false,
                EnableDataCrcs = true
            }, recorder => recorder.AddAttachment("phase122.bin", "application/octet-stream", new byte[] { 1, 2, 3 }, 20)));

            var chunk = McapRecordReader.DecodeChunk(crcOff.First(r => r.Opcode == McapWriter.OpcodeChunk).Content);
            var footer = McapRecordReader.DecodeFooter(crcOff.Last(r => r.Opcode == McapWriter.OpcodeFooter).Content);
            Check(chunk.crc == 0 && footer.summaryCrc == 0,
                "122-E1: EnableCrcs=false zeroes chunk and summary CRCs");
            Check(ReadAttachmentCrc(crcOff.First(r => r.Opcode == McapWriter.OpcodeAttachment).Content) == 0,
                "122-E2: EnableCrcs=false zeroes attachment CRC");
            Check(ReadDataEndCrc(crcOff) != 0,
                "122-E3: EnableDataCrcs remains independent of EnableCrcs");
        }

        private static void VerifyConformanceWriterMapping()
        {
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceWriter.cs");
            var runner = ReadRepoText("Scripts/mcap/conformance/csharp-runners/CsharpWriterTestRunner.ts");
            Check(writer.Contains("CreateOptionsFromFeatures", StringComparison.Ordinal)
                  && writer.Contains("UseChunking = features.Contains(\"ch\")", StringComparison.Ordinal)
                  && writer.Contains("IndexTypes", StringComparison.Ordinal)
                  && writer.Contains("EnableDataCrcs = true", StringComparison.Ordinal),
                "122-F1: C# conformance writer maps official feature flags to writer options");
            Check(runner.Contains("TestFeatures.UseChunks", StringComparison.Ordinal)
                  && runner.Contains("TestFeatures.AddExtraDataToRecords", StringComparison.Ordinal)
                  && runner.Contains("return true;", StringComparison.Ordinal)
                  && !runner.Contains("return false;\n  }\n\n  async runWriteTest", StringComparison.Ordinal),
                "122-F2: writer runner supports a measured direct/no-padding subset instead of skipping all variants");
            Check(File.Exists(RepoPath("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/Unity2Foxglove.McapConformance.csproj")),
                "122-F3: C# conformance console project remains present for writer byte checks");
        }

        private static byte[] CreateSample(McapWriterOptions options, Action<McapRecorder> extra = null)
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, options, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase122/direct", "json", "phase122.Sample", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"ok\":true}"));
                extra?.Invoke(recorder);
            }
            return ms.ToArray();
        }

        private static McapRecordReader.McapRecord[] Parse(byte[] data)
        {
            var parsed = McapRecordReader.Parse(data);
            Check(parsed.hasLeadingMagic && parsed.hasTrailingMagic,
                "122-Z: generated MCAP has leading and trailing magic");
            return parsed.records.ToArray();
        }

        private static int Count(McapRecordReader.McapRecord[] records, byte opcode)
            => records.Count(r => r.Opcode == opcode);

        private static uint ReadDataEndCrc(McapRecordReader.McapRecord[] records)
        {
            var dataEnd = records.First(r => r.Opcode == McapWriter.OpcodeDataEnd);
            var off = 0;
            return McapBinaryReader.ReadU32LE(dataEnd.Content, ref off);
        }

        private static uint ReadAttachmentCrc(byte[] content)
        {
            var off = content.Length - 4;
            return McapBinaryReader.ReadU32LE(content, ref off);
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase122 file: " + relativePath, path);
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
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
