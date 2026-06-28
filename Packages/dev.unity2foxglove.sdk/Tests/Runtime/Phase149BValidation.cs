// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 149B validation for post-recording MCAP metadata and attachment amendment.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase149BValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 149B Tests ---");
            _passCount = 0;

            VerifyPublicApiShape();
            VerifyMetadataAndAttachmentAmendment();
            VerifyAmendmentCreatesBackupAndKeepsDataCrc();
            VerifyFailedCloseDoesNotRetryWithNullSourceStream();
            VerifyStatlessSummaryGetsAmendedStatistics();
            VerifyNoOpCloseLeavesFileUnchanged();
            VerifySummarylessFilesAreRejected();
            VerifySourceShape();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 149B: " + _passCount + " checks passed.\n");
        }

        private static void VerifyPublicApiShape()
        {
            var type = typeof(McapAmendmentWriter);
            Check(type.GetConstructor(new[] { typeof(string) }) != null,
                "149B-1: McapAmendmentWriter opens file paths");
            Check(type.GetMethod("AddAttachment", new[]
                {
                    typeof(string),
                    typeof(string),
                    typeof(byte[]),
                    typeof(ulong),
                    typeof(ulong)
                }) != null,
                "149B-2: McapAmendmentWriter exposes attachment amendment");
            Check(type.GetMethod("AddMetadata", new[] { typeof(string), typeof(Dictionary<string, string>) }) != null,
                "149B-3: McapAmendmentWriter exposes metadata amendment");
        }

        private static void VerifyMetadataAndAttachmentAmendment()
        {
            var path = CreateIndexedFixture();
            try
            {
                var beforeMessages = ReadMessagePayloads(path);

                using (var writer = new McapAmendmentWriter(path))
                {
                    writer.AddMetadata("phase149b.new.metadata", new Dictionary<string, string>
                    {
                        ["value"] = "{\"added\":true}"
                    });
                    writer.AddAttachment(
                        "phase149b-new.txt",
                        "text/plain",
                        Encoding.UTF8.GetBytes("phase149b attachment"),
                        logTimeNs: 14920,
                        createTimeNs: 14921);
                    writer.Close();
                }

                using var reader = McapIndexedReader.OpenRead(path, McapSequentialReadLimits.UnlimitedForTests);
                var afterMessages = ReadMessagePayloads(path);
                Check(afterMessages.SequenceEqual(beforeMessages),
                    "149B-4: amendment preserves existing messages");
                Check(reader.MetadataIndexes.Select(index => index.Name).OrderBy(name => name, StringComparer.Ordinal)
                        .SequenceEqual(new[] { "phase149b.existing.metadata", "phase149b.new.metadata" }),
                    "149B-5: amendment keeps old and new metadata indexes");
                Check(reader.AttachmentIndexes.Select(index => index.Name).OrderBy(name => name, StringComparer.Ordinal)
                        .SequenceEqual(new[] { "phase149b-existing.txt", "phase149b-new.txt" }),
                    "149B-6: amendment keeps old and new attachment indexes");

                var metadata = reader.ReadMetadata(reader.MetadataIndexes.Single(index => index.Name == "phase149b.new.metadata"));
                Check(metadata.Metadata.TryGetValue("value", out var metadataValue)
                        && metadataValue == "{\"added\":true}",
                    "149B-7: appended metadata payload roundtrips");

                var attachment = reader.ReadAttachment(reader.AttachmentIndexes.Single(index => index.Name == "phase149b-new.txt"));
                Check(Encoding.UTF8.GetString(attachment.Data) == "phase149b attachment",
                    "149B-8: appended attachment payload roundtrips");
                Check(reader.Summary.Statistics != null
                        && reader.Summary.Statistics.MetadataCount == 2
                        && reader.Summary.Statistics.AttachmentCount == 2,
                    "149B-9: summary statistics include amended metadata and attachments");
                Check(FindBackups(path).Count == 1,
                    "149B-10: amendment keeps a unique backup of the original file");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifyAmendmentCreatesBackupAndKeepsDataCrc()
        {
            var path = CreateDataCrcFixture();
            try
            {
                var originalCrc = ReadDataSectionCrc(path);
                Check(originalCrc != 0,
                    "149B-11: CRC fixture starts with a non-zero DataEnd CRC");

                using (var writer = new McapAmendmentWriter(path))
                {
                    writer.AddMetadata("phase149b.crc.metadata", new Dictionary<string, string>
                    {
                        ["value"] = "crc"
                    });
                    writer.Close();
                }

                var amendedCrc = ReadDataSectionCrc(path);
                Check(amendedCrc != 0 && amendedCrc != originalCrc,
                    "149B-12: amendment recomputes non-zero DataEnd CRC after appending records");
                var backups = FindBackups(path);
                Check(backups.Count == 1 && ReadDataSectionCrc(backups[0]) == originalCrc,
                    "149B-13: unique backup preserves the original DataEnd CRC");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifyFailedCloseDoesNotRetryWithNullSourceStream()
        {
            var path = CreateIndexedFixture();
            try
            {
                var writer = new McapAmendmentWriter(path);
                writer.AddMetadata("phase149b.failed.close", new Dictionary<string, string>
                {
                    ["value"] = "failed-close"
                });

                var field = typeof(McapAmendmentWriter).GetField("_sourceStream", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null)
                    throw new InvalidOperationException("Could not find McapAmendmentWriter._sourceStream.");
                if (!typeof(IDisposable).IsAssignableFrom(field.FieldType))
                    throw new InvalidOperationException("McapAmendmentWriter._sourceStream no longer implements IDisposable.");

                if (field.GetValue(writer) is not IDisposable sourceStream)
                    throw new InvalidOperationException("McapAmendmentWriter._sourceStream was unexpectedly null.");
                sourceStream.Dispose();
                field.SetValue(writer, null);

                Check(Throws<InvalidOperationException>(() => writer.Close()),
                    "149B-14: failed amendment close reports a clear terminal-state error instead of NullReferenceException");
                Check(DoesNotThrow(writer.Dispose),
                    "149B-15: disposing after a failed explicit close does not retry and mask the original error");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifyStatlessSummaryGetsAmendedStatistics()
        {
            var path = CreateStatlessSummaryFixture();
            try
            {
                using (var writer = new McapAmendmentWriter(path))
                {
                    writer.AddMetadata("phase149b.statless.metadata", new Dictionary<string, string>
                    {
                        ["value"] = "statless"
                    });
                    writer.AddAttachment(
                        "phase149b-statless.txt",
                        "text/plain",
                        Encoding.UTF8.GetBytes("statless attachment"),
                        logTimeNs: 14930,
                        createTimeNs: 14931);
                    writer.Close();
                }

                using var reader = McapIndexedReader.OpenRead(path, McapSequentialReadLimits.UnlimitedForTests);
                var statistics = reader.Summary.Statistics;
                Check(statistics != null
                        && statistics.MessageCount == 1
                        && statistics.SchemaCount == 1
                        && statistics.ChannelCount == 1
                        && statistics.MetadataCount == 1
                        && statistics.AttachmentCount == 1,
                    "149B-16: amendment backfills statistics when the original summary omitted them");
                Check(reader.MetadataIndexes.Single().Name == "phase149b.statless.metadata"
                        && reader.AttachmentIndexes.Single().Name == "phase149b-statless.txt",
                    "149B-17: statless summary amendment indexes new metadata and attachment");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifyNoOpCloseLeavesFileUnchanged()
        {
            var path = CreateIndexedFixture();
            try
            {
                var beforeLength = new FileInfo(path).Length;
                using (var writer = new McapAmendmentWriter(path))
                {
                    writer.Close();
                }

                Check(new FileInfo(path).Length == beforeLength,
                    "149B-18: no-op amendment leaves file length unchanged");
                Check(FindBackups(path).Count == 0,
                    "149B-19: no-op amendment does not create a backup");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifySummarylessFilesAreRejected()
        {
            var path = CreateSummarylessFixture();
            try
            {
                Check(Throws<InvalidDataException>(() => new McapAmendmentWriter(path)),
                    "149B-20: summaryless MCAP files are rejected");
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static void VerifySourceShape()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapAmendmentWriter.cs");
            Check(source.Contains("DataEndOffset", StringComparison.Ordinal)
                    && source.Contains("File.Replace", StringComparison.Ordinal)
                    && source.Contains("Flush(true)", StringComparison.Ordinal)
                    && !source.Contains("SetLength", StringComparison.Ordinal),
                "149B-21: amendment writes a durable temp file before replacing the original");
            Check(source.Contains("WriteDataEnd", StringComparison.Ordinal)
                    && source.Contains("McapSummarySerializer.WriteSummaryAndFooter", StringComparison.Ordinal)
                    && source.Contains("WriteMagic", StringComparison.Ordinal),
                "149B-22: amendment writes a fresh summary, footer, and magic");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase149b"),
                "149B-23: validation registry exposes Phase 149B");
        }

        private static string CreateIndexedFixture()
        {
            var path = TempPath();
            using (var fs = File.Create(path))
            using (var recorder = new McapRecorder(fs, null, chunkSizeBytes: 128, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase149b/a", "json", "phase149b.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase149b/b", "json", "phase149b.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Payload("a10"));
                recorder.WriteMessage(2, 20, Payload("b20"));
                recorder.WriteMetadata("phase149b.existing.metadata", "{\"existing\":true}");
                recorder.AddAttachment(
                    "phase149b-existing.txt",
                    "text/plain",
                    Encoding.UTF8.GetBytes("existing attachment"),
                    logTimeNs: 14910,
                    createTimeNs: 14911);
                recorder.Close();
            }

            return path;
        }

        private static string CreateDataCrcFixture()
        {
            var path = TempPath();
            using (var fs = File.Create(path))
            using (var recorder = new McapRecorder(fs, null, new McapWriterOptions
                {
                    ChunkSizeBytes = 128,
                    EnableDataCrcs = true
                }, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase149b/crc", "json", "phase149b.Crc", "jsonschema", "{}");
                recorder.WriteMessage(1, 10, Payload("crc"));
                recorder.Close();
            }

            return path;
        }

        private static string CreateStatlessSummaryFixture()
        {
            var path = TempPath();
            using (var fs = File.Create(path))
            using (var writer = new McapWriter(fs, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("phase149b", "unity2foxglove-tests");
                writer.WriteSchema(1, "phase149b.Statless", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase149b/statless", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 0, 14930, 14930, Payload("statless"));
                writer.WriteDataEnd();

                var summary = new McapFileSummary();
                summary.Schemas.Add(new McapSchema
                {
                    Id = 1,
                    Name = "phase149b.Statless",
                    Encoding = "jsonschema",
                    Data = Encoding.UTF8.GetBytes("{}")
                });
                summary.Channels.Add(new McapChannel
                {
                    Id = 1,
                    SchemaId = 1,
                    Topic = "/phase149b/statless",
                    MessageEncoding = "json",
                    Metadata = new Dictionary<string, string>()
                });
                McapSummarySerializer.WriteSummaryAndFooter(
                    writer,
                    summary,
                    writeSummaryOffsets: true,
                    enableSummaryCrc: true);
                writer.WriteMagic();
                writer.Flush();
            }

            return path;
        }

        private static string CreateSummarylessFixture()
        {
            var path = TempPath();
            using (var fs = File.Create(path))
            using (var recorder = new McapRecorder(fs, null, new McapWriterOptions
                {
                    UseChunking = false,
                    RepeatSchemas = false,
                    RepeatChannels = false,
                    UseStatistics = false,
                    UseSummaryOffsets = false,
                    IndexTypes = McapIndexTypes.None
                }, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase149b/summaryless", "json", "phase149b.Summaryless", "jsonschema", "{}");
                recorder.WriteMessage(1, 1, Payload("summaryless"));
                recorder.Close();
            }

            return path;
        }

        private static IReadOnlyList<string> ReadMessagePayloads(string path)
        {
            using var reader = McapIndexedReader.OpenRead(path, McapSequentialReadLimits.UnlimitedForTests);
            return reader.ReadMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 0
            }).Select(message => Encoding.UTF8.GetString(message.Data)).ToList();
        }

        private static uint ReadDataSectionCrc(string path)
        {
            using var stream = File.OpenRead(path);
            var reader = new McapReader(stream);
            return reader.ReadTrailerInfo().DataSectionCrc;
        }

        private static byte[] Payload(string value)
            => Encoding.UTF8.GetBytes("{\"value\":\"" + value + "\"}");

        private static string TempPath()
            => Path.Combine(Path.GetTempPath(), "unity2foxglove-phase149b-" + Guid.NewGuid().ToString("N") + ".mcap");

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
                if (!string.IsNullOrEmpty(path) && File.Exists(path + ".bak"))
                    File.Delete(path + ".bak");
                foreach (var backup in FindBackups(path))
                    File.Delete(backup);
            }
            catch
            {
                // Best-effort cleanup for validation temp files.
            }
        }

        private static List<string> FindBackups(string path)
        {
            if (string.IsNullOrEmpty(path))
                return new List<string>();

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return new List<string>();

            var fileName = Path.GetFileName(path);
            return Directory.GetFiles(directory, fileName + "*.bak").ToList();
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath));
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

        private static bool DoesNotThrow(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch
            {
                return false;
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
