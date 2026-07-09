// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-7 review regression checks for recording controller and MCAP recorder orchestration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_7Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-7: Recording Controller and MCAP Recorder Orchestration Review ===");
            _passed = 0;

            AmendmentAttachmentsPreserveCrcPolicy();
            AmendmentKeepsExistingBackupAndCreatesUniqueBackup();
            McapRecorderCloseFinalizationIsTerminal();
            McapRecorderDroppedFinalChunkRecoveryKeepsIndexesWithoutStatistics();
            RecordingControllerUsesAtomicConfigurationSnapshot();
            RuntimeStopDetachesRecordingBeforeSessionDispose();
            ChannelWriteStateScratchBufferDocumentsLockOwnership();
            Phase149BUsesUniqueBackupAssertions();
            PhaseRegistryWiresPhase163_7();

            Console.WriteLine($"Phase 163-7: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void AmendmentAttachmentsPreserveCrcPolicy()
        {
            var crcPath = CreateIndexedFixture();
            var noCrcPath = CreateIndexedFixture();
            try
            {
                using (var writer = new McapAmendmentWriter(crcPath))
                {
                    writer.AddAttachment(
                        "phase163-7-crc.txt",
                        "text/plain",
                        Encoding.UTF8.GetBytes("crc-on"),
                        logTimeNs: 16370,
                        createTimeNs: 16371);
                    writer.Close();
                }

                using (var writer = new McapAmendmentWriter(noCrcPath, enableCrcs: false))
                {
                    writer.AddAttachment(
                        "phase163-7-no-crc.txt",
                        "text/plain",
                        Encoding.UTF8.GetBytes("crc-off"),
                        logTimeNs: 16372,
                        createTimeNs: 16373);
                    writer.Close();
                }

                using var crcReader = McapIndexedReader.OpenRead(crcPath, McapSequentialReadLimits.UnlimitedForTests);
                var crcAttachment = crcReader.ReadAttachment(
                    crcReader.AttachmentIndexes.Single(index => index.Name == "phase163-7-crc.txt"));
                Check(crcAttachment.Crc != 0 && crcAttachment.CrcValid,
                    "163-7A-1: amendment attachments default to non-zero CRCs");

                using var noCrcReader = McapIndexedReader.OpenRead(noCrcPath, McapSequentialReadLimits.UnlimitedForTests);
                var noCrcAttachment = noCrcReader.ReadAttachment(
                    noCrcReader.AttachmentIndexes.Single(index => index.Name == "phase163-7-no-crc.txt"));
                Check(noCrcAttachment.Crc == 0 && noCrcAttachment.CrcValid,
                    "163-7A-2: amendment attachments can explicitly follow EnableCrcs=false");
            }
            finally
            {
                TryDeleteFixture(crcPath);
                TryDeleteFixture(noCrcPath);
            }
        }

        private static void AmendmentKeepsExistingBackupAndCreatesUniqueBackup()
        {
            var path = CreateIndexedFixture();
            try
            {
                var staleBackup = path + ".bak";
                File.WriteAllText(staleBackup, "stale backup must survive");

                using (var writer = new McapAmendmentWriter(path))
                {
                    writer.AddMetadata("phase163-7.backup", new Dictionary<string, string> { ["value"] = "backup" });
                    writer.Close();
                }

                var backups = FindBackups(path);
                Check(File.ReadAllText(staleBackup) == "stale backup must survive",
                    "163-7B-1: amendment does not delete a pre-existing fixed .bak file");
                Check(backups.Count >= 2 && backups.Any(item => !string.Equals(item, staleBackup, StringComparison.Ordinal)),
                    "163-7B-2: amendment writes a separate unique backup path");
            }
            finally
            {
                TryDeleteFixture(path);
            }
        }

        private static void McapRecorderCloseFinalizationIsTerminal()
        {
            var source = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            var close = Slice(source, "public void Close()", "private McapFileSummary BuildFinalSummary");

            Check(close.Contains("finally", StringComparison.Ordinal)
                  && close.Contains("_closed = true;", StringComparison.Ordinal)
                  && close.Contains("McapSummarySerializer.WriteSummaryAndFooter", StringComparison.Ordinal),
                "163-7C: McapRecorder.Close enters a terminal closed state even when finalization writes fail");
        }

        private static void McapRecorderDroppedFinalChunkRecoveryKeepsIndexesWithoutStatistics()
        {
            var source = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            var recovery = Slice(source, "private void WriteRecoverableTrailerAfterDroppedFinalChunk()", "/// <summary>");

            Check(source.Contains("BuildFinalSummary(bool includeStatistics)", StringComparison.Ordinal)
                  && recovery.Contains("McapSummarySerializer.WriteSummaryAndFooter", StringComparison.Ordinal)
                  && recovery.Contains("BuildFinalSummary(includeStatistics: false)", StringComparison.Ordinal)
                  && !recovery.Contains("WriteFooter(0, 0, 0)", StringComparison.Ordinal),
                "163-7D: dropped final chunk recovery keeps prior indexes without writing misleading statistics");
        }

        private static void RecordingControllerUsesAtomicConfigurationSnapshot()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs");
            Check(source.Contains("private RecordingConfiguration _recordingConfiguration;", StringComparison.Ordinal)
                  && source.Contains("Volatile.Write(", StringComparison.Ordinal)
                  && source.Contains("ref _recordingConfiguration", StringComparison.Ordinal)
                  && source.Contains("var configuration = Volatile.Read(ref _recordingConfiguration);", StringComparison.Ordinal)
                  && source.Contains("new McapRecorder(fileStream, _logger, configuration.WriterOptions", StringComparison.Ordinal)
                  && !source.Contains("private string _recordingPath;", StringComparison.Ordinal)
                  && !source.Contains("private bool _recordingEnabled;", StringComparison.Ordinal),
                "163-7E: RecordingController publishes path/options/coordinate mode as one atomic configuration snapshot");
        }

        private static void RuntimeStopDetachesRecordingBeforeSessionDispose()
        {
            var runtime = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var stop = Slice(runtime, "public void Stop()", "//");
            Check(stop.Contains("_recording.DetachFromSession();", StringComparison.Ordinal)
                  && stop.Contains("session?.Dispose();", StringComparison.Ordinal)
                  && !stop.Contains("session?.SetRecorder(null);", StringComparison.Ordinal)
                  && stop.IndexOf("_recording.DetachFromSession();", StringComparison.Ordinal)
                     < stop.IndexOf("session?.Dispose();", StringComparison.Ordinal),
                "163-7F: runtime stop detaches recording before disposing the session");
        }

        private static void ChannelWriteStateScratchBufferDocumentsLockOwnership()
        {
            var source = PhaseValidationSourceHelpers.ReadMcapRecorderSources();
            Check(source.Contains("Caller must hold _lock", StringComparison.Ordinal)
                  && source.Contains("must not be retained after the locked operation finishes", StringComparison.Ordinal),
                "163-7G: reused channel-write-state scratch list documents its lock ownership contract");
        }

        private static void Phase149BUsesUniqueBackupAssertions()
        {
            var phase149b = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase149BValidation.cs");
            var amendment = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapAmendmentWriter.cs");
            Check(phase149b.Contains("FindBackups(path)", StringComparison.Ordinal)
                  && amendment.Contains("private static string CreateBackupPath", StringComparison.Ordinal)
                  && amendment.Contains("Guid.NewGuid().ToString(\"N\") + \".bak\"", StringComparison.Ordinal)
                  && !amendment.Contains("TryDelete(backupPath)", StringComparison.Ordinal),
                "163-7H: amendment backup validation no longer depends on a destructive fixed .bak path");
        }

        private static void PhaseRegistryWiresPhase163_7()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase163-7\",", StringComparison.Ordinal)
                  && registry.Contains("Phase163_7Validation.Validate", StringComparison.Ordinal),
                "163-7I: PhaseValidationRegistry wires --phase163-7");
        }

        private static string CreateIndexedFixture()
        {
            var path = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase163-7-" + Guid.NewGuid().ToString("N") + ".mcap");
            using (var fs = File.Create(path))
            using (var recorder = new McapRecorder(fs, null, chunkSizeBytes: 128, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase163_7/a", "json", "phase163_7.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"value\":\"a10\"}"));
                recorder.Close();
            }

            return path;
        }

        private static IReadOnlyList<string> FindBackups(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();

            return Directory.GetFiles(directory, Path.GetFileName(path) + "*.bak");
        }

        private static void TryDeleteFixture(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);

                foreach (var backup in FindBackups(path))
                    File.Delete(backup);
            }
            catch
            {
                // Best-effort cleanup for validation temp files.
            }
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string Slice(string text, string startMarker, string endMarker)
        {
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
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
