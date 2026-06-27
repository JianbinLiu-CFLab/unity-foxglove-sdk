// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-10 review regression checks for MCAP reader parsing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_10Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-10: MCAP Reader Parsing Review ===");
            _passed = 0;

            StreamingReaderRejectsNonHeaderFirstRecord();
            FooterDecoderRejectsTrailingBytesAndNullContent();
            NoOpDecompressionReturnsOwnedCopy();
            LazyIndexedEnumerationFallsBackToSequentialMessages();
            SinglePassEnumerableCanRetryAfterFactoryFailure();
            SourceRejectsZeroOpcodeInsideReplayChunks();
            SourceRemovesRedundantSeekAndSeekabilityChecks();
            PhaseRegistryWiresPhase163_10();

            Console.WriteLine($"Phase 163-10: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void StreamingReaderRejectsNonHeaderFirstRecord()
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteSchema(1, "phase163_10.BadFirst", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            Check(Throws<InvalidDataException>(() => reader.Read()),
                "163-10A: streaming MCAP reader rejects a non-Header first record");
        }

        private static void FooterDecoderRejectsTrailingBytesAndNullContent()
        {
            var footerWithTrailingByte = new byte[McapWriter.FooterContentLength + 1];
            Check(Throws<InvalidDataException>(() => McapRecordDecoder.DecodeFooter(footerWithTrailingByte)),
                "163-10B-1: footer decoder rejects trailing bytes after the fixed footer fields");
            Check(Throws<ArgumentNullException>(() => McapRecordDecoder.DecodeFooter(null)),
                "163-10B-2: footer decoder rejects null content explicitly");
            Check(Throws<ArgumentNullException>(() => McapRecordDecoder.DecodeChunkIndex(null))
                  && Throws<ArgumentNullException>(() => McapRecordDecoder.DecodeStatistics(null))
                  && Throws<ArgumentNullException>(() => McapRecordDecoder.DecodeMetadataIndex(null))
                  && Throws<ArgumentNullException>(() => McapRecordDecoder.DecodeAttachmentIndex(null)),
                "163-10B-3: summary index decoders reject null content explicitly");
        }

        private static void NoOpDecompressionReturnsOwnedCopy()
        {
            var original = new byte[] { 1, 2, 3, 4 };
            var decompressed = McapCompression.Decompress("", original, original.Length);
            decompressed[0] = 99;

            Check(!ReferenceEquals(original, decompressed) && original[0] == 1,
                "163-10C: no-op decompression returns an owned copy, not the source buffer");
        }

        private static void LazyIndexedEnumerationFallsBackToSequentialMessages()
        {
            using var stream = CreateSummarylessMessageFile();
            using var reader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);

            var messages = reader.EnumerateMessages(new McapReadOptions
            {
                AllowLinearFallback = true,
                Order = McapReadOrder.FileOrder
            }).ToList();

            Check(messages.Count == 1
                  && messages[0].ChannelId == 1
                  && Encoding.UTF8.GetString(messages[0].Data) == "{\"value\":16310}",
                "163-10D-1: lazy message enumeration falls back to sequential messages when chunk indexes are absent");

            stream.Position = 0;
            using var strictReader = new McapIndexedReader(
                stream,
                leaveOpen: true,
                McapSequentialReadLimits.UnlimitedForTests);
            Check(Throws<InvalidOperationException>(() => strictReader.EnumerateMessages(new McapReadOptions
                  {
                      AllowLinearFallback = false,
                      Order = McapReadOrder.FileOrder
                  }).ToList()),
                "163-10D-2: lazy message enumeration preserves AllowLinearFallback=false strictness");
        }

        private static void SinglePassEnumerableCanRetryAfterFactoryFailure()
        {
            var attempts = 0;
            var enumerable = new McapSinglePassEnumerable<int>(
                "phase163-10 retryable enumerable",
                () =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new IOException("Injected factory failure");

                    return ((IEnumerable<int>)new[] { 42 }).GetEnumerator();
                });

            Check(Throws<IOException>(() => enumerable.GetEnumerator()),
                "163-10E-1: single-pass enumerable surfaces the first factory failure");

            using var enumerator = enumerable.GetEnumerator();
            Check(enumerator.MoveNext() && enumerator.Current == 42,
                "163-10E-2: single-pass enumerable can be retried after a factory failure");
        }

        private static void SourceRejectsZeroOpcodeInsideReplayChunks()
        {
            var replaySource = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs");
            Check(Count(replaySource, "MCAP opcode 0x00 is invalid inside chunk.") >= 3,
                "163-10F: replay tick, snapshot, and history chunk loops reject opcode 0x00");
        }

        private static void SourceRemovesRedundantSeekAndSeekabilityChecks()
        {
            var readerSource = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");
            var indexedReaderSource = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapIndexedReader.cs");
            var decoderSource = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapRecordDecoder.cs");

            Check(!readerSource.Contains("_stream.CanSeek && _stream.Length < minFileBytes", StringComparison.Ordinal),
                "163-10G-1: ReadSummary removes the dead seekability clause after the explicit seekable-stream guard");
            Check(!indexedReaderSource.Contains("private void ReadLatestBeforeSequential(\r\n            McapReadOptions options,\r\n            HashSet<ushort> selectedChannelIds,\r\n            int expectedCount,\r\n            Dictionary<ushort, McapMessage> latestByChannel)\r\n        {\r\n            _stream.Seek(0, SeekOrigin.Begin);", StringComparison.Ordinal),
                "163-10G-2: ReadLatestBeforeSequential avoids the redundant seek before VisitSequentialMessages");
            Check(decoderSource.Contains("RequireExactSegmentEnd(off, mapEnd, fieldName + \" map\");", StringComparison.Ordinal),
                "163-10G-3: McapRecordDecoder.ReadMap keeps exact map segment accounting");
        }

        private static void PhaseRegistryWiresPhase163_10()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase163-10\", \"Phase 163-10\", Phase163_10Validation.Validate", StringComparison.Ordinal),
                "163-10H: PhaseValidationRegistry wires --phase163-10");
        }

        private static MemoryStream CreateSummarylessMessageFile()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase163-10");
                writer.WriteChannel(1, 0, "/phase163_10/message", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 0, 16310, 16310, Encoding.UTF8.GetBytes("{\"value\":16310}"));
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static int Count(string source, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
