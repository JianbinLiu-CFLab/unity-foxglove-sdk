// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests
// Purpose: Phase 31 validation — Source Generator consistency test and
// MCAP chunk CRC32 integrity test.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase31Validation
    {
        static void VerifyRecorderOwnsFileStreamWhenRequested()
        {
            var path = Path.Combine(Path.GetTempPath(), "unity2foxglove_owned_recorder_stream.mcap");
            try
            {
                var fileStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                using (var recorder = new McapRecorder(fileStream, leaveOpen: false))
                {
                    recorder.WriteMetadata("test", "{}");
                }

                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // Opening with FileShare.None proves the recorder released its owned FileStream.
                }

                var bytes = File.ReadAllBytes(path);
                Check(HasTrailingMagic(bytes), "owned recorder stream is finalized and closed for exclusive reopen");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        static bool HasTrailingMagic(byte[] bytes)
        {
            var magic = McapWriter.Magic;
            if (bytes == null || bytes.Length < magic.Length)
                return false;
            for (var i = 0; i < magic.Length; i++)
                if (bytes[bytes.Length - magic.Length + i] != magic[i])
                    return false;
            return true;
        }

        static void VerifyCrcMismatchDetected()
        {
            var bytes = BuildChunkMcap(crcOverride: 0xDEADBEEF);
            var ms = new MemoryStream(bytes);
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            var ci = summary.ChunkIndexes[0];
            reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength, out var crcValid);
            Check(!crcValid, "CRC mismatch detected without throwing");
        }

        static void VerifyZeroLengthTopicHandled()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms, leaveOpen: true);
            w.WriteMagic();
            w.WriteHeader("", "test");
            w.WriteDataEnd();
            var sumStart = (ulong)ms.Position;
            // Channel with zero-length topic
            w.WriteChannel(1, 0, "", "json", new Dictionary<string, string>());
            var sumOffStart = (ulong)ms.Position;
            w.WriteSummaryOffset(0x04, sumStart, (ulong)ms.Position - sumStart);
            w.WriteFooter(sumStart, sumOffStart, 0);
            w.WriteMagic();
            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Check(summary.Channels.Count == 1, "zero topic: channel decoded");
            Check(summary.Channels[0].Topic == "", "zero topic: empty string preserved");
        }

        static void VerifyAttachmentInSummaryRejected()
        {
            // Craft a minimal MCAP with an attachment record in the summary section.
            var ms = new MemoryStream();
            var w = new McapWriter(ms, leaveOpen: true);
            w.WriteMagic();
            w.WriteHeader("", "test");
            w.WriteDataEnd();

            // Write an Attachment record (opcode 0x09) in summary
            var sumStart = (ulong)ms.Position;
            var attInner = new MemoryStream();
            McapWriter.WriteU64(attInner, 100); // log time
            McapWriter.WriteU64(attInner, 100); // create time
            McapWriter.WriteString(attInner, "calibration"); // name
            McapWriter.WriteString(attInner, "application/json"); // media type
            McapWriter.WriteLengthPrefixedBytes(attInner, new byte[] { 1, 2, 3 }); // data
            var attBytes = attInner.ToArray();
            ms.WriteByte(0x09);
            McapWriter.WriteU64(ms, (ulong)attBytes.Length);
            ms.Write(attBytes, 0, attBytes.Length);

            // Also have a valid schema so summary section isn't completely empty
            var schemaGrpStart = (ulong)ms.Position;
            w.WriteSchema(1, "test", "jsonschema", new byte[] { 1 });
            var schemaGrpLen = (ulong)ms.Position - schemaGrpStart;

            var sumOffStart = (ulong)ms.Position;
            w.WriteSummaryOffset(0x03, schemaGrpStart, schemaGrpLen);

            w.WriteFooter(sumStart, sumOffStart, 0);
            w.WriteMagic();
            ms.Position = 0;

            var reader = new McapReader(ms);
            try
            {
                reader.ReadSummary();
                Check(false, "attachment in summary: should have thrown");
            }
            catch (InvalidDataException)
            {
                // Attachment records belong in the data section, never the summary.
            }
        }

        static void VerifyAttachmentIndexSafeSkip()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms, leaveOpen: true);
            w.WriteMagic();
            w.WriteHeader("", "test");
            w.WriteDataEnd();

            var sumStart = (ulong)ms.Position;
            var idxInner = new MemoryStream();
            McapWriter.WriteU64(idxInner, 0); // attachment offset
            McapWriter.WriteU64(idxInner, 0); // attachment length
            McapWriter.WriteU64(idxInner, 100); // log time
            McapWriter.WriteU64(idxInner, 100); // create time
            McapWriter.WriteU64(idxInner, 3); // data size
            McapWriter.WriteString(idxInner, "calibration");
            McapWriter.WriteString(idxInner, "application/json");
            var idxBytes = idxInner.ToArray();
            ms.WriteByte(0x0A);
            McapWriter.WriteU64(ms, (ulong)idxBytes.Length);
            ms.Write(idxBytes, 0, idxBytes.Length);

            var schemaGrpStart = (ulong)ms.Position;
            w.WriteSchema(1, "test", "jsonschema", new byte[] { 1 });
            var schemaGrpLen = (ulong)ms.Position - schemaGrpStart;
            var sumOffStart = (ulong)ms.Position;
            w.WriteSummaryOffset(0x03, schemaGrpStart, schemaGrpLen);
            w.WriteFooter(sumStart, sumOffStart, 0);
            w.WriteMagic();
            ms.Position = 0;

            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Check(summary.Schemas.Count == 1, "attachment index skip: schema still decoded");
        }

        static void VerifyReplayLoadDisposesPreviousStream()
        {
            var first = Path.Combine(Path.GetTempPath(), "phase31-replay-first-" + Guid.NewGuid().ToString("N") + ".mcap");
            var second = Path.Combine(Path.GetTempPath(), "phase31-replay-second-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(first, BuildMinimalSummaryOnlyMcap());
                File.WriteAllBytes(second, BuildMinimalSummaryOnlyMcap());

                using (var engine = new McapReplayEngine())
                {
                    var streamField = typeof(McapReplayEngine).GetField(
                        "_stream",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Check(streamField != null, "replay load: stream field remains observable by the disposal regression");

                    engine.Load(first);
                    var previousStream = streamField.GetValue(engine) as Stream;
                    Check(previousStream != null, "replay load: first file stream captured before replacement");

                    engine.Load(second);
                    var previousStreamDisposed = false;
                    try
                    {
                        _ = previousStream.Position;
                    }
                    catch (ObjectDisposedException)
                    {
                        previousStreamDisposed = true;
                    }
                    Check(previousStreamDisposed, "replay load: previous file stream disposed");
                }
            }
            finally
            {
                if (File.Exists(first)) File.Delete(first);
                if (File.Exists(second)) File.Delete(second);
            }
        }

        static byte[] BuildMinimalSummaryOnlyMcap()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms, leaveOpen: true);
            w.WriteMagic();
            w.WriteHeader("", "test");
            w.WriteDataEnd();
            var sumStart = (ulong)ms.Position;
            var schemaGrpStart = (ulong)ms.Position;
            w.WriteSchema(1, "test", "jsonschema", new byte[] { 1 });
            var schemaGrpLen = (ulong)ms.Position - schemaGrpStart;
            var sumOffStart = (ulong)ms.Position;
            w.WriteSummaryOffset(0x03, schemaGrpStart, schemaGrpLen);
            w.WriteFooter(sumStart, sumOffStart, 0);
            w.WriteMagic();
            w.Flush();
            return ms.ToArray();
        }

        static byte[] BuildChunkMcap(uint? crcOverride = null)
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms, leaveOpen: true);
            w.WriteMagic();
            w.WriteHeader("", "test");

            var chunkData = new MemoryStream();
            chunkData.WriteByte(0x05);
            var msgInner = new MemoryStream();
            McapWriter.WriteU16(msgInner, 1);
            McapWriter.WriteU32(msgInner, 1);
            McapWriter.WriteU64(msgInner, 100);
            McapWriter.WriteU64(msgInner, 100);
            msgInner.WriteByte(42);
            var msgBytes = msgInner.ToArray();
            McapWriter.WriteU64(chunkData, (ulong)msgBytes.Length);
            chunkData.Write(msgBytes, 0, msgBytes.Length);
            var raw = chunkData.ToArray();

            var chunkOffset = (ulong)ms.Position;
            var crc = crcOverride ?? Crc32Helper.Compute(raw);
            w.WriteChunk(100, 100, (ulong)raw.Length, crc, "", (ulong)raw.Length, raw);
            var chunkLen = (ulong)ms.Position - chunkOffset;

            var sumStart = (ulong)ms.Position;
            var chunkIdxGrpStart = (ulong)ms.Position;
            w.WriteChunkIndex(100, 100, chunkOffset, chunkLen,
                new Dictionary<ushort, ulong>(), 0, "", (ulong)raw.Length, (ulong)raw.Length);
            var chunkIdxGrpLen = (ulong)ms.Position - chunkIdxGrpStart;
            var sumOffStart = (ulong)ms.Position;
            w.WriteSummaryOffset(0x08, chunkIdxGrpStart, chunkIdxGrpLen);
            w.WriteFooter(sumStart, sumOffStart, 0);
            w.WriteMagic();
            w.Flush();
            return ms.ToArray();
        }

        private sealed class Phase31CaptureLogger : IFoxgloveLogger
        {
            public string LastError { get; private set; }

            public void LogWarning(string message) { }

            public void LogError(string message)
            {
                LastError = message ?? string.Empty;
            }
        }

        static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception($"[Phase31] Check failed: {description}");
        }
    }
}
