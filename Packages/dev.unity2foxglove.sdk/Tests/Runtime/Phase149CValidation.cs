// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 149C validation for MCAP private record writing and enumeration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase149CValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 149C Tests ---");
            _passCount = 0;

            VerifyApiShape();
            VerifyWriterOpcodeValidation();
            VerifyTopLevelPrivateRecordRoundtrip();
            VerifyIndexedReaderPrivateRecordEnumeration();
            VerifyChunkPrivateRecordEnumeration();
            VerifyAmendmentPrivateRecordRoundtrip();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 149C: " + _passCount + " checks passed.\n");
        }

        private static void VerifyApiShape()
        {
            Check(typeof(McapWriter).GetMethod("WritePrivateRecord", new[] { typeof(byte), typeof(byte[]) }) != null,
                "149C-1: McapWriter exposes WritePrivateRecord");
            Check(typeof(McapReader).GetMethod("ReadPrivateRecords") != null,
                "149C-2: McapReader exposes private record reading");
            Check(typeof(McapIndexedReader).GetMethod("EnumeratePrivateRecords", new[] { typeof(bool) }) != null,
                "149C-3: McapIndexedReader exposes private record enumeration");
            Check(typeof(McapAmendmentWriter).GetMethod("AddPrivateRecord", new[] { typeof(byte), typeof(byte[]) }) != null,
                "149C-4: McAP amendment writer exposes private record amendment");
        }

        private static void VerifyWriterOpcodeValidation()
        {
            using var stream = new MemoryStream();
            using var writer = new McapWriter(stream, leaveOpen: true);
            Check(Throws<ArgumentOutOfRangeException>(() => writer.WritePrivateRecord(0x0F, Array.Empty<byte>())),
                "149C-5: WritePrivateRecord rejects standard opcodes");
            writer.WritePrivateRecord(0x80, null);
            writer.WritePrivateRecord(0xFF, Array.Empty<byte>());
            Check(stream.Length == McapWriter.RecordHeaderLength * 2,
                "149C-6: WritePrivateRecord accepts private opcode bounds");
        }

        private static void VerifyTopLevelPrivateRecordRoundtrip()
        {
            var bytes = BuildTopLevelPrivateFixture();
            using var stream = new MemoryStream(bytes);
            var reader = new McapReader(stream);
            var records = reader.ReadPrivateRecords(dataSectionEndOffset: (ulong)bytes.Length - McapWriter.MagicLength);
            Check(records.Count == 2,
                "149C-7: top-level private record count roundtrips");
            Check(records[0].Opcode == 0x80
                    && Encoding.UTF8.GetString(records[0].Data) == "phase149c-top"
                    && !records[0].InChunk
                    && records[0].Offset > 0,
                "149C-8: top-level private record payload and offset roundtrip");
            Check(records[1].Opcode == 0xFF && records[1].Data.Length == 0,
                "149C-9: empty private payload roundtrips");
        }

        private static void VerifyIndexedReaderPrivateRecordEnumeration()
        {
            var path = CreateAmendableFixture();
            try
            {
                using (var amendment = new McapAmendmentWriter(path))
                {
                    amendment.AddPrivateRecord(0x81, Encoding.UTF8.GetBytes("phase149c-indexed"));
                    amendment.Close();
                }

                using var reader = McapIndexedReader.OpenRead(path, McapSequentialReadLimits.UnlimitedForTests);
                var privateRecords = reader.EnumeratePrivateRecords().ToList();
                var messages = reader.ReadMessages(new McapReadOptions { Order = McapReadOrder.FileOrder });

                Check(privateRecords.Count == 1 && privateRecords[0].Opcode == 0x81,
                    "149C-10: indexed reader surfaces amended private records");
                Check(messages.Count == 2
                        && Encoding.UTF8.GetString(messages[0].Data).Contains("one", StringComparison.Ordinal)
                        && Encoding.UTF8.GetString(messages[1].Data).Contains("two", StringComparison.Ordinal),
                    "149C-11: message readers skip private records");
                Check(reader.Summary.Statistics != null
                        && reader.Summary.Statistics.MessageCount == 2
                        && reader.Summary.Statistics.MetadataCount == 0
                        && reader.Summary.Statistics.AttachmentCount == 0,
                    "149C-12: private records do not alter summary statistics");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifyChunkPrivateRecordEnumeration()
        {
            var path = CreateChunkPrivateFixture();
            try
            {
                using var reader = McapIndexedReader.OpenRead(path, McapSequentialReadLimits.UnlimitedForTests);
                var privateRecords = reader.EnumeratePrivateRecords(includeChunkRecords: true).ToList();
                Check(privateRecords.Count == 1
                        && privateRecords[0].Opcode == 0x82
                        && privateRecords[0].InChunk
                        && Encoding.UTF8.GetString(privateRecords[0].Data) == "phase149c-chunk",
                    "149C-13: private records inside chunks roundtrip");
                Check(!reader.EnumeratePrivateRecords(includeChunkRecords: false).Any(),
                    "149C-14: private record enumeration can skip chunk records");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifyAmendmentPrivateRecordRoundtrip()
        {
            var path = CreateAmendableFixture();
            try
            {
                using (var amendment = new McapAmendmentWriter(path))
                {
                    amendment.AddPrivateRecord(0x83, Encoding.UTF8.GetBytes("phase149c-amended"));
                    amendment.Close();
                }

                using var reader = McapIndexedReader.OpenRead(path, McapSequentialReadLimits.UnlimitedForTests);
                var privateRecords = reader.EnumeratePrivateRecords().ToList();
                var messages = reader.ReadMessages(new McapReadOptions { Order = McapReadOrder.FileOrder });
                Check(privateRecords.Single().Opcode == 0x83
                        && Encoding.UTF8.GetString(privateRecords.Single().Data) == "phase149c-amended",
                    "149C-15: amendment private record payload roundtrips");
                Check(messages.Count == 2,
                    "149C-16: amendment preserves existing messages");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase149c"),
                "149C-17: validation registry exposes Phase 149C");
        }

        private static byte[] BuildTopLevelPrivateFixture()
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("phase149c", "unity2foxglove-tests");
                writer.WritePrivateRecord(0x80, Encoding.UTF8.GetBytes("phase149c-top"));
                writer.WritePrivateRecord(0xFF, null);
                writer.WriteDataEnd();
                writer.WriteMagic();
            }

            return stream.ToArray();
        }

        private static string CreateAmendableFixture()
        {
            var path = TempPath();
            using (var fs = File.Create(path))
            using (var recorder = new McapRecorder(fs, null, chunkSizeBytes: 128, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase149c/a", "json", "phase149c.A", "jsonschema", "{}");
                recorder.WriteMessage(1, 1, Payload("one"));
                recorder.WriteMessage(1, 2, Payload("two"));
                recorder.Close();
            }

            return path;
        }

        private static string CreateChunkPrivateFixture()
        {
            var path = TempPath();
            using (var fs = File.Create(path))
            using (var writer = new McapWriter(fs, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("phase149c", "unity2foxglove-tests");
                using var chunkRecords = new MemoryStream();
                using (var chunkWriter = new McapWriter(chunkRecords, leaveOpen: true))
                {
                    chunkWriter.WritePrivateRecord(0x82, Encoding.UTF8.GetBytes("phase149c-chunk"));
                    chunkWriter.Flush();
                }

                var chunkBytes = chunkRecords.ToArray();
                writer.WriteChunk(0, 0, (ulong)chunkBytes.Length, 0, "", (ulong)chunkBytes.Length, chunkBytes);
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteFooter(summaryStart, summaryStart, 0);
                writer.WriteMagic();
            }

            return path;
        }

        private static byte[] Payload(string value)
            => Encoding.UTF8.GetBytes("{\"value\":\"" + value + "\"}");

        private static string TempPath()
            => Path.Combine(Path.GetTempPath(), "unity2foxglove-phase149c-" + Guid.NewGuid().ToString("N") + ".mcap");

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup for validation temp files.
            }
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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
