// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates MCAP compression, binary reader helpers, McapReader summary, replay engine load/tick, and replay channel ID mapping.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase11Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        /// <summary>
        /// Entry point: runs all Phase 11 tests covering MCAP
        /// compression, binary reader helpers, McapReader summary,
        /// replay engine load/tick, and replay channel ID mapping.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("--- Phase 11 Tests ---");

            TestCompressionRoundtrip();
            TestBinaryReaderHelpers();
            TestMcapReaderSummary();
            TestReplayEngineLoad();
            TestReplayEngineTick();
            TestChannelIdMapping();

            Console.WriteLine($"Phase 11: {_passCount} checks passed.\n");
        }

        static byte[] GenerateMinimalMcap()
        {
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            // Header
            var hdr = new MemoryStream();
            McapWriter.WriteString(hdr, ""); McapWriter.WriteString(hdr, "test");
            WriteRecord(ms, 0x01, hdr);
            // Schema id=1
            var sch = new MemoryStream();
            McapWriter.WriteU16(sch, 1);
            McapWriter.WriteString(sch, "TestSchema"); McapWriter.WriteString(sch, "jsonschema");
            McapWriter.WriteLengthPrefixedBytes(sch, Encoding.UTF8.GetBytes("{}"));
            WriteRecord(ms, 0x03, sch);
            // Channel id=1, sid=1
            var ch = new MemoryStream();
            McapWriter.WriteU16(ch, 1); McapWriter.WriteU16(ch, 1);
            McapWriter.WriteString(ch, "/test"); McapWriter.WriteString(ch, "json");
            McapWriter.WriteStringMap(ch, new Dictionary<string, string>());
            WriteRecord(ms, 0x04, ch);
            // Chunk with 2 messages
            var chunkMs = new MemoryStream();
            WriteMcapMessage(chunkMs, 1, 0, 1000, 1000, new byte[] { 1 });
            WriteMcapMessage(chunkMs, 1, 1, 2000, 2000, new byte[] { 2 });
            // MessageIndex
            var miMs = new MemoryStream();
            McapWriter.WriteU16(miMs, 1);
            McapWriter.WriteU32(miMs, 2 * 16); // 2 entries * 16 bytes
            McapWriter.WriteU64(miMs, 1000); McapWriter.WriteU64(miMs, 0);
            McapWriter.WriteU64(miMs, 2000); McapWriter.WriteU64(miMs, 22);
            // Write chunk
            var chunkOff = (ulong)ms.Position;
            var chunkData = new MemoryStream();
            McapWriter.WriteU64(chunkData, 1000); McapWriter.WriteU64(chunkData, 2000); // st/et
            McapWriter.WriteU64(chunkData, (ulong)chunkMs.Length); // uncompSize
            McapWriter.WriteU32(chunkData, 0); // crc
            McapWriter.WriteString(chunkData, ""); // compression
            McapWriter.WriteU64(chunkData, (ulong)chunkMs.Length); // compSize
            chunkMs.Position = 0; chunkMs.CopyTo(chunkData);
            WriteRecord(ms, 0x06, chunkData);
            // MessageIndex after chunk
            var miStart = (ulong)ms.Position;
            WriteRecord(ms, 0x07, miMs);
            // DataEnd
            var de = new MemoryStream();
            McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);
            // Summary
            var sumStart = (ulong)ms.Position;
            // Schema copy
            var schContent = sch.ToArray();
            WriteRecord(ms, 0x03, new MemoryStream(schContent));
            // Channel copy
            var chContent = ch.ToArray();
            WriteRecord(ms, 0x04, new MemoryStream(chContent));
            // Statistics
            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 2); // msgCount
            McapWriter.WriteU16(stats, 1); // schemaCount
            McapWriter.WriteU32(stats, 1); // chCount
            McapWriter.WriteU32(stats, 0); // att
            McapWriter.WriteU32(stats, 0); // meta
            McapWriter.WriteU32(stats, 1); // chunkCount
            McapWriter.WriteU64(stats, 1000); // st
            McapWriter.WriteU64(stats, 2000); // et
            McapWriter.WriteU32(stats, 10); // cms 1*10
            McapWriter.WriteU16(stats, 1); McapWriter.WriteU64(stats, 2);
            WriteRecord(ms, 0x0B, stats);
            // ChunkIndex
            var cix = new MemoryStream();
            McapWriter.WriteU64(cix, 1000); McapWriter.WriteU64(cix, 2000);
            McapWriter.WriteU64(cix, chunkOff);
            McapWriter.WriteU64(cix, (ulong)chunkData.Length);
            McapWriter.WriteU32(cix, 10); // 1 * 10
            McapWriter.WriteU16(cix, 1); McapWriter.WriteU64(cix, miStart);
            McapWriter.WriteU64(cix, (ulong)miMs.Length);
            McapWriter.WriteString(cix, "");
            McapWriter.WriteU64(cix, (ulong)chunkMs.Length);
            McapWriter.WriteU64(cix, (ulong)chunkMs.Length);
            WriteRecord(ms, 0x08, cix);
            // SummaryOffset
            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);
            // Footer
            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            // Trailing magic
            ms.Write(McapWriter.Magic, 0, 8);
            return ms.ToArray();
        }

        static void WriteRecord(Stream s, byte opcode, MemoryStream content)
        {
            s.WriteByte(opcode);
            var data = content.ToArray();
            McapWriter.WriteU64(s, (ulong)data.Length);
            s.Write(data, 0, data.Length);
        }

        static void WriteMcapMessage(Stream s, ushort ch, uint seq, ulong log, ulong pub, byte[] d)
        {
            var m = new MemoryStream();
            McapWriter.WriteU16(m, ch); McapWriter.WriteU32(m, seq);
            McapWriter.WriteU64(m, log); McapWriter.WriteU64(m, pub);
            if (d != null) m.Write(d, 0, d.Length);
            var c = m.ToArray();
            s.WriteByte(0x05);
            McapWriter.WriteU64(s, (ulong)c.Length);
            s.Write(c, 0, c.Length);
        }

        // ── Tests ──

        /// <summary>
        /// No-op decompress (empty compression) must return original
        /// data unchanged.
        /// </summary>
        static void TestCompressionRoundtrip()
        {
            var original = Encoding.UTF8.GetBytes("Hello MCAP compression test!");

            // No compression — data unchanged
            var result = McapCompression.Decompress("", original, original.Length);
            Assert(Encoding.UTF8.GetString(result) == "Hello MCAP compression test!", "No-op decompress returns original");
        }

        /// <summary>
        /// Verifies <c>ReadU16LE</c> and <c>ReadU32LE</c> correctly
        /// parse little-endian values from a byte buffer.
        /// </summary>
        static void TestBinaryReaderHelpers()
        {
            var buf = new byte[] { 0x01, 0x00, 0x02, 0x00, 0x00, 0x00 };
            var off = 0;
            var v16 = McapBinaryReader.ReadU16LE(buf, ref off);
            Assert(v16 == 1, "ReadU16LE ok");
            var v32 = McapBinaryReader.ReadU32LE(buf, ref off);
            Assert(v32 == 2, "ReadU32LE ok");
        }

        /// <summary>
        /// Generates a minimal MCAP file, reads the summary, and
        /// verifies schema, channel, chunk index, and statistics counts.
        /// </summary>
        static void TestMcapReaderSummary()
        {
            var data = GenerateMinimalMcap();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();
            Assert(summary.Schemas.Count == 1, "Has 1 schema");
            Assert(summary.Schemas[0].Name == "TestSchema", "Schema name");
            Assert(summary.Channels.Count == 1, "Has 1 channel");
            Assert(summary.Channels[0].Topic == "/test", "Channel topic");
            Assert(summary.ChunkIndexes.Count == 1, "Has 1 ChunkIndex");
            Assert(summary.Statistics.MessageCount == 2, "Statistics messageCount=2");
            Assert(summary.Statistics.ChunkCount == 1, "Statistics chunkCount=1");
        }

        /// <summary>
        /// Loads a minimal MCAP file into the replay engine and verifies
        /// load state, seek capability, time range, and channel count.
        /// </summary>
        static void TestReplayEngineLoad()
        {
            var data = GenerateMinimalMcap();
            var path = Path.Combine(Path.GetTempPath(), $"test_replay_load_{Guid.NewGuid()}.mcap");
            File.WriteAllBytes(path, data);
            try
            {
                var engine = new McapReplayEngine();
                engine.Load(path);
                Assert(engine.IsLoaded, "Replay engine loaded");
                Assert(engine.CanSeek, "CanSeek true");
                Assert(engine.StartTimeNs == 1000, "StartTime correct");
                Assert(engine.EndTimeNs == 2000, "EndTime correct");
                Assert(engine.Channels.Count == 1, "Has 1 channel");
                engine.Dispose();
            }
            finally { File.Delete(path); }
        }

        /// <summary>
        /// Loads a file, seeks to start, plays, then ticks past the last
        /// message time and verifies messages are emitted.
        /// </summary>
        static void TestReplayEngineTick()
        {
            var data = GenerateMinimalMcap();
            var path = Path.Combine(Path.GetTempPath(), $"test_replay_tick_{Guid.NewGuid()}.mcap");
            File.WriteAllBytes(path, data);
            try
            {
                var engine = new McapReplayEngine();
                engine.Load(path);
                // Seek to start to set _lastEmitTime and _elapsedNs correctly
                engine.Seek(0);
                engine.Play();

                // Tick with a time past the last message: all messages should emit
                var msgs = engine.Tick(2000);
                Assert(msgs != null, "Tick returns list");
                Assert(msgs.Count > 0, "Tick emits messages when nowNs past messages");
                Assert(engine.IsLoaded, "Engine still loaded after Tick");

                engine.Dispose();
            }
            finally { File.Delete(path); }
        }

        /// <summary>
        /// Verifies the replay channel ID constant has the high bit set
        /// (<c>0x80000001</c>) to distinguish replay channels from live.
        /// </summary>
        static void TestChannelIdMapping()
        {
            var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | 1);
            Assert(replayId == 0x80000001, "Replay channel ID base | 1 = 0x80000001");
            Assert((replayId & 0x80000000) != 0, "Replay channel ID has high bit set");
        }
    }
}
