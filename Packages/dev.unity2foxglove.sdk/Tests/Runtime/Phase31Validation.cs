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
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase31Validation
    {
        /// <summary>
        /// Entry point: runs all Phase 31 checks. Throws on first failure.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("=== Phase 31: Source Generator Consistency ===");
            VerifySingleTopic();
            VerifyTwoFieldsSameTopic();
            VerifyTwoTopics();
            VerifyVector3Decomp();
            VerifyQuaternionDecomp();
            VerifyColorDecomp();
            VerifyUnderscoreStrip();
            VerifyGlobalNamespace();
            VerifySchemaName();
            VerifyOutputStructure();

            // MCAP CRC32 integrity
            Console.WriteLine("=== Phase 31: MCAP Chunk CRC32 ===");
            VerifyCrc32Deterministic();
            VerifyCrc32ZeroOnEmpty();
            VerifyCrc32Roundtrip();

            // MCAP robustness
            Console.WriteLine("=== Phase 31: MCAP Robustness ===");
            VerifyMalformedMagicRejected();
            VerifyTruncatedStreamRejected();
            VerifyTrailingMagicMismatchRejected();
            VerifyCrcMismatchDetected();
            VerifyZeroLengthTopicHandled();
            VerifyAttachmentSafeSkip();
            VerifyAttachmentIndexSafeSkip();
            VerifyReplayLoadDisposesPreviousStream();
            Console.WriteLine("Phase 31: All checks passed.\n");
        }

        static void VerifySingleTopic()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_health", "System.Int32", "/debug/health", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(output.Contains("FoxgloveLog_TopicCount => 1"), "topic count");
            Check(output.Contains("case 0: return new FoxgloveLogTopicInfo"), "GetTopic case 0");
        }

        static void VerifyTwoFieldsSameTopic()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_x", "System.Single", "/pos", 10f, ""),
                new("_y", "System.Single", "/pos", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(output.Contains("FoxgloveLog_TopicCount => 1"), "grouped topic count");
            Check(output.Contains("x = this._x, y = this._y"), "grouped fields in JSON");
        }

        static void VerifyTwoTopics()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_hp", "System.Int32", "/hp", 5f, ""),
                new("_mana", "System.Int32", "/mana", 5f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(output.Contains("FoxgloveLog_TopicCount => 2"), "two-topic count");
            Check(output.Contains("case 0:") && output.Contains("case 1:"), "two switch cases");
            Check(!output.Contains("case 2:"), "no extra case");
        }

        static void VerifyVector3Decomp()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_pos", "UnityEngine.Vector3", "/pos", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(output.Contains("new { x = this._pos.x, y = this._pos.y, z = this._pos.z }"), "Vector3 decomp");
        }

        static void VerifyQuaternionDecomp()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_rot", "UnityEngine.Quaternion", "/rot", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(output.Contains("new { x = this._rot.x, y = this._rot.y, z = this._rot.z, w = this._rot.w }"), "Quaternion decomp");
        }

        static void VerifyColorDecomp()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_color", "UnityEngine.Color", "/color", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(output.Contains("new { r = this._color.r, g = this._color.g, b = this._color.b, a = this._color.a }"), "Color decomp");
        }

        static void VerifyUnderscoreStrip()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("___value", "System.Single", "/val", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(output.Contains("value = this.___value"), "underscore strip: value in JSON");
            Check(!output.Contains("___value = this.___value"), "underscore not raw in JSON");
        }

        static void VerifyGlobalNamespace()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_val", "System.Int32", "/val", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("", "GlobalClass", members);
            Check(!output.Contains("namespace "), "no namespace block");
            Check(output.Contains("partial class GlobalClass"), "class declaration");
        }

        static void VerifySchemaName()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_a", "System.Int32", "/t", 10f, ""),
                new("_b", "System.Int32", "/t", 10f, "my.Schema")
            };
            var output = FoxgloveSourceEmitter.EmitClass("NS", "C", members);
            Check(output.Contains("PublishJson(\"/t\", \"my.Schema\""), "schema name in PublishJson");
        }

        static void VerifyOutputStructure()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("_hp", "System.Int32", "/hp", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("Foo", "Bar", members);
            Check(output.Contains("// <auto-generated/>"), "auto-gen header");
            Check(output.Contains("#pragma warning disable"), "pragma");
            Check(output.Contains("[Preserve]"), "[Preserve] attribute");
            Check(output.Contains("IFoxgloveLogSource"), "interface impl");
            Check(output.Contains("FoxgloveLog_TopicCount"), "TopicCount");
            Check(output.Contains("FoxgloveLog_GetTopic"), "GetTopic");
            Check(output.Contains("FoxgloveLog_Publish"), "Publish");
        }

        static void VerifyCrc32Deterministic()
        {
            var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var c1 = Crc32Helper.Compute(data);
            var c2 = Crc32Helper.Compute(data);
            Check(c1 == c2, "CRC32 deterministic");
            Check(c1 != 0, "CRC32 non-zero for non-empty input");
        }

        static void VerifyCrc32ZeroOnEmpty()
        {
            var crc = Crc32Helper.Compute(Array.Empty<byte>());
            Check(crc == 0, "CRC32 zero for empty input (0xFFFFFFFF XOR 0xFFFFFFFF = 0)");
        }

        static void VerifyCrc32Roundtrip()
        {
            // Write a minimal MCAP with one chunk, verify CRC matches on read.
            var ms = new MemoryStream();
            var w = new McapWriter(ms, leaveOpen: true);
            w.WriteMagic();
            w.WriteHeader("", "test");

            // Schema + one message in raw chunk buffer
            var chunkData = new MemoryStream();
            chunkData.WriteByte(0x05); // Message opcode
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

            var rawCrc = Crc32Helper.Compute(raw);
            var chunkOffset = (ulong)ms.Position;
            w.WriteChunk(100, 100, (ulong)raw.Length, rawCrc, "", (ulong)raw.Length, raw);
            var chunkLen = (ulong)ms.Position - chunkOffset;

            // Summary section: schema, channel, statistics, chunk index, summary offsets
            var sumStart = (ulong)ms.Position;

            var schemaGrpStart = (ulong)ms.Position;
            w.WriteSchema(1, "test", "jsonschema", new byte[] { 1 });
            var schemaGrpLen = (ulong)ms.Position - schemaGrpStart;

            var channelGrpStart = (ulong)ms.Position;
            w.WriteChannel(1, 1, "/test", "json", new Dictionary<string, string>());
            var channelGrpLen = (ulong)ms.Position - channelGrpStart;

            var statsGrpStart = (ulong)ms.Position;
            w.WriteStatistics(1, 1, 1, 0, 0, 1, 100, 100,
                new Dictionary<ushort, ulong> { [1] = 1 });
            var statsGrpLen = (ulong)ms.Position - statsGrpStart;

            var chunkIdxGrpStart = (ulong)ms.Position;
            w.WriteChunkIndex(100, 100, chunkOffset, chunkLen,
                new Dictionary<ushort, ulong>(), 0, "", (ulong)raw.Length, (ulong)raw.Length);
            var chunkIdxGrpLen = (ulong)ms.Position - chunkIdxGrpStart;

            var sumOffStart = (ulong)ms.Position;
            w.WriteSummaryOffset(0x03, schemaGrpStart, schemaGrpLen);
            w.WriteSummaryOffset(0x04, channelGrpStart, channelGrpLen);
            w.WriteSummaryOffset(0x0B, statsGrpStart, statsGrpLen);
            w.WriteSummaryOffset(0x08, chunkIdxGrpStart, chunkIdxGrpLen);

            w.WriteFooter(sumStart, sumOffStart, 0);
            w.WriteMagic();
            w.Flush();

            // Read back
            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Check(summary.ChunkIndexes.Count == 1, "roundtrip: one chunk index");
            var ci = summary.ChunkIndexes[0];
            var uncompressed = reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength, out var crcValid);
            Check(crcValid, "roundtrip: chunk CRC passes");
            Check(uncompressed.Length == raw.Length, "roundtrip: uncompressed size matches");
        }

        static void VerifyMalformedMagicRejected()
        {
            var ms = new MemoryStream(new byte[45]);
            var reader = new McapReader(ms);
            try { reader.ReadSummary(); Check(false, "magic: should have thrown"); }
            catch (InvalidDataException) { /* expected */ }
        }

        static void VerifyTruncatedStreamRejected()
        {
            // Stream shorter than the minimum valid MCAP (magic + footer + trailing magic = 45 bytes).
            var ms = new MemoryStream(new byte[16]);
            var reader = new McapReader(ms);
            try { reader.ReadSummary(); Check(false, "truncated stream: should have thrown"); }
            catch (EndOfStreamException) { /* expected — too short to read anything */ }
            catch (InvalidDataException) { /* also acceptable */ }
        }

        static void VerifyTrailingMagicMismatchRejected()
        {
            var bytes = BuildMinimalSummaryOnlyMcap();
            bytes[bytes.Length - 1] ^= 0xFF;
            var reader = new McapReader(new MemoryStream(bytes));
            try { reader.ReadSummary(); Check(false, "trailing magic mismatch: should have thrown"); }
            catch (InvalidDataException) { /* expected */ }
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
            w.WriteSummaryOffset(0x04, sumStart, (ulong)ms.Position - sumStart);
            w.WriteFooter(sumStart, (ulong)ms.Position, 0);
            w.WriteMagic();
            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            Check(summary.Channels.Count == 1, "zero topic: channel decoded");
            Check(summary.Channels[0].Topic == "", "zero topic: empty string preserved");
        }

        static void VerifyAttachmentSafeSkip()
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
            McapWriter.WriteString(attInner, "calibration"); // name
            McapWriter.WriteString(attInner, "application/json"); // content type
            McapWriter.WriteU64(attInner, 100); // log time
            McapWriter.WriteU64(attInner, 100); // create time
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
            // Should not throw — attachment opcode is safely skipped
            var summary = reader.ReadSummary();
            Check(summary.Schemas.Count == 1, "attachment skip: schema still decoded");
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
                    engine.Load(first);
                    engine.Load(second);
                    File.Delete(first);
                    Check(!File.Exists(first), "replay load: previous file stream disposed");
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

        static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception($"[Phase31] Check failed: {description}");
        }
    }
}
