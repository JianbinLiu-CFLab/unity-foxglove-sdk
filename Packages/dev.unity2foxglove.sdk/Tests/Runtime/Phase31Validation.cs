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
            VerifyReplayLoadReportsUnfinalizedFile();
            VerifyRecorderCloseFinalizesAfterFailureFlag();
            VerifyRecorderOwnsFileStreamWhenRequested();
            VerifyCrcMismatchDetected();
            VerifyZeroLengthTopicHandled();
            VerifyAttachmentInSummaryRejected();
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
            Check(
                output.Contains(
                    "[\"x\"] = __foxRunCapture_0_0, [\"y\"] = __foxRunCapture_0_1"),
                "grouped fields in JSON use the one-sample capture");
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
                new(
                    "_pos",
                    "UnityEngine.Vector3",
                    "/pos",
                    10f,
                    "",
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    typeShape: FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector3)))
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(
                output.Contains(
                    "new Dictionary<string, object> { [\"x\"] = __foxRunCapture_0_0.x, [\"y\"] = __foxRunCapture_0_0.y, [\"z\"] = __foxRunCapture_0_0.z }"),
                "Vector3 decomp uses the one-sample capture");
        }

        static void VerifyQuaternionDecomp()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new(
                    "_rot",
                    "UnityEngine.Quaternion",
                    "/rot",
                    10f,
                    "",
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    typeShape: FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Quaternion)))
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(
                output.Contains(
                    "new Dictionary<string, object> { [\"x\"] = __foxRunCapture_0_0.x, [\"y\"] = __foxRunCapture_0_0.y, [\"z\"] = __foxRunCapture_0_0.z, [\"w\"] = __foxRunCapture_0_0.w }"),
                "Quaternion decomp uses the one-sample capture");
        }

        static void VerifyColorDecomp()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new(
                    "_color",
                    "UnityEngine.Color",
                    "/color",
                    10f,
                    "",
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    typeShape: FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Color)))
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(
                output.Contains(
                    "new Dictionary<string, object> { [\"r\"] = __foxRunCapture_0_0.r, [\"g\"] = __foxRunCapture_0_0.g, [\"b\"] = __foxRunCapture_0_0.b, [\"a\"] = __foxRunCapture_0_0.a }"),
                "Color decomp uses the one-sample capture");
        }

        static void VerifyUnderscoreStrip()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember>
            {
                new("___value", "System.Single", "/val", 10f, "")
            };
            var output = FoxgloveSourceEmitter.EmitClass("MyGame", "Player", members);
            Check(
                output.Contains("[\"value\"] = __foxRunCapture_0_0"),
                "underscore strip: value in captured JSON");
            Check(
                !output.Contains("[\"___value\"]"),
                "underscore not raw in JSON");
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
                new("_a", "System.Int32", "/t", 10f, "my.Schema"),
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

        static void VerifyReplayLoadReportsUnfinalizedFile()
        {
            var path = Path.Combine(Path.GetTempPath(), "unity2foxglove_unfinalized_replay.mcap");
            try
            {
                var bytes = new byte[64];
                var magic = McapWriter.Magic;
                Buffer.BlockCopy(magic, 0, bytes, 0, magic.Length);
                File.WriteAllBytes(path, bytes);

                var logger = new Phase31CaptureLogger();
                var controller = new ReplayController(logger);
                controller.Enable(path, playbackClock: null, recordingEnabled: false);

                Check(!controller.IsEnabled, "replay load rejects unfinalized file");
                Check(logger.LastError != null && logger.LastError.Contains(Path.GetFileName(path)),
                    "replay load error includes file name");
                Check(logger.LastError != null && logger.LastError.Contains("not finalized"),
                    "replay load error identifies unfinalized MCAP file");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        static void VerifyRecorderCloseFinalizesAfterFailureFlag()
        {
            using var ms = new MemoryStream();
            using var recorder = new McapRecorder(ms);

            var failedField = typeof(McapRecorder).GetField("_recordingFailed", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(failedField != null, "recorder exposes internal failure flag for close robustness test");
            failedField.SetValue(recorder, true);

            recorder.Close();

            var bytes = ms.ToArray();
            Check(HasTrailingMagic(bytes), "recorder close writes trailing magic even after recording failure");
        }
    }
}
