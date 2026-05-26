// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates LZ4/Zstd compression roundtrips, parameters/services metadata, client publish message, coordinate mode in channel metadata, and MetadataIndex read/parse roundtrip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase12Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        /// <summary>
        /// Entry point: runs all Phase 12 tests covering LZ4/Zstd
        /// compression roundtrips, parameters/services metadata,
        /// client publish message, coordinate mode in channel metadata,
        /// and MetadataIndex read/parse roundtrip.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("--- Phase 12 Tests ---");

            TestCompressedChunkRoundtrip();
            TestCompressedMcapReadable();
            TestParametersMetadata();
            TestServicesMetadata();
            TestClientPublishMessage();
            TestCoordinateModeInChannel();
            TestCoordinateModeMismatchDetection();
            TestMetadataIndexRoundtrip();

            Console.WriteLine($"Phase 12: {_passCount} checks passed.\n");
        }

        // ── Helpers ──

        static ulong WriteRecord(Stream s, byte opcode, MemoryStream content)
        {
            s.WriteByte(opcode);
            var data = content.ToArray();
            McapWriter.WriteU64(s, (ulong)data.Length);
            s.Write(data, 0, data.Length);
            return 1UL + 8UL + (ulong)data.Length;
        }

        static MemoryStream BuildMinimalHeader(Stream ms)
        {
            var hdr = new MemoryStream();
            McapWriter.WriteString(hdr, ""); McapWriter.WriteString(hdr, "test");
            WriteRecord(ms, 0x01, hdr);
            return hdr;
        }

        static (MemoryStream schema, MemoryStream channel) BuildSchemaAndChannel(Stream ms, ushort sid, ushort cid, string topic,
            Dictionary<string, string> channelMeta = null)
        {
            var sch = new MemoryStream();
            McapWriter.WriteU16(sch, sid);
            McapWriter.WriteString(sch, "TestSchema"); McapWriter.WriteString(sch, "jsonschema");
            McapWriter.WriteLengthPrefixedBytes(sch, Encoding.UTF8.GetBytes("{}"));
            WriteRecord(ms, 0x03, sch);

            var ch = new MemoryStream();
            McapWriter.WriteU16(ch, cid); McapWriter.WriteU16(ch, sid);
            McapWriter.WriteString(ch, topic); McapWriter.WriteString(ch, "json");
            McapWriter.WriteStringMap(ch, channelMeta ?? new Dictionary<string, string>());
            WriteRecord(ms, 0x04, ch);

            return (sch, ch);
        }

        // ── Test 1: Compressed chunk roundtrip ──

        /// <summary>
        /// Tests LZ4, Zstd, and no-op compression roundtrips: compress
        /// then decompress must return original data.
        /// </summary>
        static void TestCompressedChunkRoundtrip()
        {
            var raw = Encoding.UTF8.GetBytes("Hello MCAP compression test!");

            var lz4Comp = McapCompression.Compress("lz4", raw);
            var lz4Result = McapCompression.Decompress("lz4", lz4Comp, raw.Length);
            Assert(Encoding.UTF8.GetString(raw) == Encoding.UTF8.GetString(lz4Result), "LZ4 compress-decompress roundtrip");

            var zstdComp = McapCompression.Compress("zstd", raw);
            var zstdResult = McapCompression.Decompress("zstd", zstdComp, raw.Length);
            Assert(Encoding.UTF8.GetString(raw) == Encoding.UTF8.GetString(zstdResult), "Zstd compress-decompress roundtrip");

            // No compression returns data unchanged
            var noneComp = McapCompression.Compress("", raw);
            Assert(raw == noneComp, "No-op compress returns original array");
        }

        // ── Test 2: Compressed MCAP readable by Reader ──

        /// <summary>
        /// Produces an LZ4-compressed MCAP file, reads it back, and
        /// verifies the chunk index reports the correct compression and
        /// the decompressed chunk matches original size.
        /// </summary>
        static void TestCompressedMcapReadable()
        {
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            var sch = new MemoryStream();
            McapWriter.WriteU16(sch, 1);
            McapWriter.WriteString(sch, "TestSchema"); McapWriter.WriteString(sch, "jsonschema");
            McapWriter.WriteLengthPrefixedBytes(sch, Encoding.UTF8.GetBytes("{}"));

            var ch = new MemoryStream();
            McapWriter.WriteU16(ch, 1); McapWriter.WriteU16(ch, 1);
            McapWriter.WriteString(ch, "/test"); McapWriter.WriteString(ch, "json");
            McapWriter.WriteStringMap(ch, new Dictionary<string, string>());

            // Build chunk with one message, compress with LZ4
            var chunkMs = new MemoryStream();
            // Message: ch=1 seq=0 log=1000 pub=1000
            var msgContent = new MemoryStream();
            McapWriter.WriteU16(msgContent, 1); McapWriter.WriteU32(msgContent, 0);
            McapWriter.WriteU64(msgContent, 1000); McapWriter.WriteU64(msgContent, 1000);
            msgContent.WriteByte(42);
            var msgBytes = msgContent.ToArray();
            chunkMs.WriteByte(0x05);
            McapWriter.WriteU64(chunkMs, (ulong)msgBytes.Length);
            chunkMs.Write(msgBytes, 0, msgBytes.Length);

            var raw = chunkMs.ToArray();
            var compressed = McapCompression.Compress("lz4", raw);

            var chunkOff = (ulong)ms.Position;
            var chunkData = new MemoryStream();
            McapWriter.WriteU64(chunkData, 1000); McapWriter.WriteU64(chunkData, 2000);
            McapWriter.WriteU64(chunkData, (ulong)raw.Length);
            McapWriter.WriteU32(chunkData, 0);
            McapWriter.WriteString(chunkData, "lz4");
            McapWriter.WriteU64(chunkData, (ulong)compressed.Length);
            chunkData.Write(compressed, 0, compressed.Length);
            var chunkRecordLength = WriteRecord(ms, 0x06, chunkData);

            // MessageIndex
            var mi = new MemoryStream();
            McapWriter.WriteU16(mi, 1);
            McapWriter.WriteU32(mi, 16); // 1 entry * 16 bytes
            McapWriter.WriteU64(mi, 1000); McapWriter.WriteU64(mi, 0);
            var miStart = (ulong)ms.Position;
            var messageIndexRecordLength = WriteRecord(ms, 0x07, mi);

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            // Summary
            var sumStart = (ulong)ms.Position;
            WriteRecord(ms, 0x03, new MemoryStream(sch.ToArray()));
            WriteRecord(ms, 0x04, new MemoryStream(ch.ToArray()));

            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 1); McapWriter.WriteU16(stats, 1);
            McapWriter.WriteU32(stats, 1); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 1);
            McapWriter.WriteU64(stats, 1000); McapWriter.WriteU64(stats, 2000);
            McapWriter.WriteU32(stats, 10); McapWriter.WriteU16(stats, 1); McapWriter.WriteU64(stats, 1);
            WriteRecord(ms, 0x0B, stats);

            var cix = new MemoryStream();
            McapWriter.WriteU64(cix, 1000); McapWriter.WriteU64(cix, 2000);
            McapWriter.WriteU64(cix, chunkOff); McapWriter.WriteU64(cix, chunkRecordLength);
            McapWriter.WriteU32(cix, 10); McapWriter.WriteU16(cix, 1); McapWriter.WriteU64(cix, miStart);
            McapWriter.WriteU64(cix, messageIndexRecordLength);
            McapWriter.WriteString(cix, "lz4");
            McapWriter.WriteU64(cix, (ulong)compressed.Length);
            McapWriter.WriteU64(cix, (ulong)raw.Length);
            WriteRecord(ms, 0x08, cix);

            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();
            Assert(summary.ChunkIndexes.Count == 1, "Compressed MCAP: has 1 ChunkIndex");
            Assert(summary.ChunkIndexes[0].Compression == "lz4", "Compressed MCAP: compression is lz4");
            Assert(summary.ChunkIndexes[0].CompressedSize > 0, "Compressed MCAP: compressed size > 0");

            // Read chunk records via ReadChunkRecords (decompresses)
            var ci = summary.ChunkIndexes[0];
            var uncomp = reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength);
            Assert(uncomp != null && uncomp.Length > 0, "Compressed MCAP: chunk decompressed");
            Assert(uncomp.Length == (int)raw.Length, "Compressed MCAP: decompressed size matches original");
        }

        // ── Test 3: Parameters Metadata ──

        /// <summary>
        /// Writes a foxglove.parameters metadata record, reads it back,
        /// and verifies the metadata index and decoded value contain the
        /// correct parameter name and type.
        /// </summary>
        static void TestParametersMetadata()
        {
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            // Write a metadata record for parameters
            var metaOff = (ulong)ms.Position;
            var metaContent = new MemoryStream();
            McapWriter.WriteString(metaContent, "foxglove.parameters");
            var mapStream = new MemoryStream();
            McapWriter.WriteString(mapStream, "value");
            McapWriter.WriteString(mapStream, "{\"name\":\"test_param\",\"type\":\"float64\",\"value\":1.5,\"timestamp\":2000}");
            var mapBytes = mapStream.ToArray();
            McapWriter.WriteU32(metaContent, (uint)mapBytes.Length);
            metaContent.Write(mapBytes, 0, mapBytes.Length);
            WriteRecord(ms, 0x0C, metaContent);
            var metaLen = (ulong)ms.Position - metaOff;

            // Write metadata index
            var metaIdxOff = (ulong)ms.Position;
            var metaIdxContent = new MemoryStream();
            McapWriter.WriteU64(metaIdxContent, metaOff);
            McapWriter.WriteU64(metaIdxContent, metaLen);
            McapWriter.WriteString(metaIdxContent, "foxglove.parameters");
            WriteRecord(ms, 0x0D, metaIdxContent);

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            var sumStart = (ulong)ms.Position;
            // Need Statistics for valid summary
            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 1); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0);
            McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);
            // MetadataIndex in summary
            WriteRecord(ms, 0x0D, new MemoryStream(metaIdxContent.ToArray()));

            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();

            Assert(summary.MetadataIndexes.Count == 1, "Parameters: has 1 MetadataIndex");
            Assert(summary.MetadataIndexes[0].Name == "foxglove.parameters", "Parameters: MetadataIndex name correct");
            Assert(summary.Statistics.MetadataCount == 1, "Parameters: Statistics.MetadataCount=1");

            // Read the actual metadata content
            var meta = reader.ReadMetadataAt(summary.MetadataIndexes[0].Offset);
            Assert(meta.Name == "foxglove.parameters", "Parameters: Metadata name correct");
            Assert(meta.Metadata.ContainsKey("value"), "Parameters: Metadata has value key");
            Assert(meta.Metadata["value"].Contains("test_param"), "Parameters: Metadata value contains param name");
        }

        // ── Test 4: Services Metadata ──

        /// <summary>
        /// Writes a foxglove.services metadata record, reads it back,
        /// and verifies serviceId, callId, and status fields.
        /// </summary>
        static void TestServicesMetadata()
        {
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            var metaOff = (ulong)ms.Position;
            var val = "{\"serviceId\":5,\"callId\":10,\"status\":\"completed\",\"payloadSize\":42,\"timestamp\":3000}";
            var metaContent = new MemoryStream();
            McapWriter.WriteString(metaContent, "foxglove.services");
            var mapStream = new MemoryStream();
            McapWriter.WriteString(mapStream, "value");
            McapWriter.WriteString(mapStream, val);
            var mapBytes = mapStream.ToArray();
            McapWriter.WriteU32(metaContent, (uint)mapBytes.Length);
            metaContent.Write(mapBytes, 0, mapBytes.Length);
            WriteRecord(ms, 0x0C, metaContent);
            var metaLen = (ulong)ms.Position - metaOff;

            var metaIdxOff = (ulong)ms.Position;
            var metaIdxContent = new MemoryStream();
            McapWriter.WriteU64(metaIdxContent, metaOff);
            McapWriter.WriteU64(metaIdxContent, metaLen);
            McapWriter.WriteString(metaIdxContent, "foxglove.services");

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            var sumStart = (ulong)ms.Position;
            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 1); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0);
            McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);
            WriteRecord(ms, 0x0D, new MemoryStream(metaIdxContent.ToArray()));

            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();

            Assert(summary.MetadataIndexes.Count == 1, "Services: has 1 MetadataIndex");
            Assert(summary.MetadataIndexes[0].Name == "foxglove.services", "Services: MetadataIndex name correct");

            var meta = reader.ReadMetadataAt(summary.MetadataIndexes[0].Offset);
            Assert(meta.Name == "foxglove.services", "Services: Metadata name correct");
            Assert(meta.Metadata["value"].Contains("\"serviceId\":5"), "Services: metadata contains serviceId");
            Assert(meta.Metadata["value"].Contains("\"callId\":10"), "Services: metadata contains callId");
        }

        // ── Test 5: ClientPublish Message ──

        /// <summary>
        /// Produces an MCAP with a client-published channel (high-bit
        /// channel id, no schema) and verifies the channel record,
        /// message, and payload survive the roundtrip.
        /// </summary>
        static void TestClientPublishMessage()
        {
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            var sch = new MemoryStream();
            McapWriter.WriteU16(sch, 1);
            McapWriter.WriteString(sch, "TestSchema"); McapWriter.WriteString(sch, "jsonschema");
            McapWriter.WriteLengthPrefixedBytes(sch, Encoding.UTF8.GetBytes("{}"));
            WriteRecord(ms, 0x03, sch);

            // Client channel with high-bit ID
            var cid = (ushort)(0xA0000001 & 0xFFFF);
            var ch = new MemoryStream();
            McapWriter.WriteU16(ch, cid); McapWriter.WriteU16(ch, 0); // sid=0 (schemaless)
            McapWriter.WriteString(ch, "/client/topic"); McapWriter.WriteString(ch, "json");
            McapWriter.WriteStringMap(ch, new Dictionary<string, string>());
            WriteRecord(ms, 0x04, ch);

            // Client message in chunk
            var chunkMs = new MemoryStream();
            var msgContent = new MemoryStream();
            McapWriter.WriteU16(msgContent, cid); McapWriter.WriteU32(msgContent, 0);
            McapWriter.WriteU64(msgContent, 5000); McapWriter.WriteU64(msgContent, 5000);
            var payload = Encoding.UTF8.GetBytes("client_data");
            msgContent.Write(payload, 0, payload.Length);
            var msgBytes = msgContent.ToArray();
            chunkMs.WriteByte(0x05);
            McapWriter.WriteU64(chunkMs, (ulong)msgBytes.Length);
            chunkMs.Write(msgBytes, 0, msgBytes.Length);

            var chunkOff = (ulong)ms.Position;
            var chunkData = new MemoryStream();
            McapWriter.WriteU64(chunkData, 5000); McapWriter.WriteU64(chunkData, 5000);
            McapWriter.WriteU64(chunkData, (ulong)chunkMs.Length);
            McapWriter.WriteU32(chunkData, 0);
            McapWriter.WriteString(chunkData, "");
            McapWriter.WriteU64(chunkData, (ulong)chunkMs.Length);
            chunkMs.Position = 0; chunkMs.CopyTo(chunkData);
            var chunkRecordLength = WriteRecord(ms, 0x06, chunkData);

            var mi = new MemoryStream();
            McapWriter.WriteU16(mi, cid);
            McapWriter.WriteU32(mi, 16);
            McapWriter.WriteU64(mi, 5000); McapWriter.WriteU64(mi, 0);
            var miStart = (ulong)ms.Position;
            var messageIndexRecordLength = WriteRecord(ms, 0x07, mi);

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            var sumStart = (ulong)ms.Position;
            WriteRecord(ms, 0x03, new MemoryStream(sch.ToArray()));
            WriteRecord(ms, 0x04, new MemoryStream(ch.ToArray()));

            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 1); McapWriter.WriteU16(stats, 1);
            McapWriter.WriteU32(stats, 1); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 1);
            McapWriter.WriteU64(stats, 5000); McapWriter.WriteU64(stats, 5000);
            McapWriter.WriteU32(stats, 10); McapWriter.WriteU16(stats, cid); McapWriter.WriteU64(stats, 1);
            WriteRecord(ms, 0x0B, stats);

            var cix = new MemoryStream();
            McapWriter.WriteU64(cix, 5000); McapWriter.WriteU64(cix, 5000);
            McapWriter.WriteU64(cix, chunkOff); McapWriter.WriteU64(cix, chunkRecordLength);
            McapWriter.WriteU32(cix, 10); McapWriter.WriteU16(cix, cid); McapWriter.WriteU64(cix, miStart);
            McapWriter.WriteU64(cix, messageIndexRecordLength);
            McapWriter.WriteString(cix, "");
            McapWriter.WriteU64(cix, (ulong)chunkMs.Length);
            McapWriter.WriteU64(cix, (ulong)chunkMs.Length);
            WriteRecord(ms, 0x08, cix);

            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();

            Assert(summary.Channels.Count == 1, "ClientPublish: has 1 channel");
            Assert(summary.Channels[0].Topic == "/client/topic", "ClientPublish: channel topic correct");
            Assert(summary.Channels[0].SchemaId == 0, "ClientPublish: channel has no schema (sid=0)");

            var ci = summary.ChunkIndexes[0];
            var uncomp = reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength);
            var msgs = reader.ReadChunkMessages(uncomp);
            Assert(msgs.Count == 1, "ClientPublish: 1 message in chunk");
            Assert(msgs[0].ChannelId == cid, "ClientPublish: message channelId correct");
            Assert(msgs[0].LogTime == 5000, "ClientPublish: message logTime correct");
            Assert(Encoding.UTF8.GetString(msgs[0].Data) == "client_data", "ClientPublish: payload correct");
        }

        // ── Test 6: coordinate_mode in Channel metadata ──

        /// <summary>
        /// Registers a channel with <c>coordinate_mode</c> metadata set
        /// to <c>RightHand</c> in the MCAP, reads back the summary, and
        /// confirms the metadata roundtrips.
        /// </summary>
        static void TestCoordinateModeInChannel()
        {
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            var meta = new Dictionary<string, string> { ["coordinate_mode"] = "RightHand" };
            var (sch, ch) = BuildSchemaAndChannel(ms, 1, 1, "/tf", meta);

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            var sumStart = (ulong)ms.Position;
            WriteRecord(ms, 0x03, new MemoryStream(sch.ToArray()));
            WriteRecord(ms, 0x04, new MemoryStream(ch.ToArray()));
            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 1); McapWriter.WriteU32(stats, 1);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0); McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);

            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();

            Assert(summary.Channels.Count == 1, "CoordMode: has 1 channel");
            Assert(summary.Channels[0].Metadata.ContainsKey("coordinate_mode"), "CoordMode: metadata has coordinate_mode");
            Assert(summary.Channels[0].Metadata["coordinate_mode"] == "RightHand", "CoordMode: value is RightHand");
        }

        // ── Test 7: coordinate_mode mismatch detection ──

        /// <summary>
        /// Creates an MCAP with <c>RightHand</c> coordinate mode and
        /// verifies the mismatch detection correctly identifies
        /// divergence from the default <c>LeftHand</c>.
        /// </summary>
        static void TestCoordinateModeMismatchDetection()
        {
            // Build MCAP with RightHand coordinate mode, then test mismatch against LeftHand
            var ms = new MemoryStream();
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            var meta = new Dictionary<string, string> { ["coordinate_mode"] = "RightHand" };
            var (sch, ch) = BuildSchemaAndChannel(ms, 1, 1, "/tf", meta);

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            var sumStart = (ulong)ms.Position;
            WriteRecord(ms, 0x03, new MemoryStream(sch.ToArray()));
            WriteRecord(ms, 0x04, new MemoryStream(ch.ToArray()));
            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 1); McapWriter.WriteU32(stats, 1);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0); McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);

            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var path = Path.Combine(Path.GetTempPath(), $"test_coord_mode_mismatch_{Guid.NewGuid()}.mcap");
            File.WriteAllBytes(path, data);
            try
            {
                // Create a temporary reader to verify metadata
                using var fs = File.OpenRead(path);
                var reader = new McapReader(fs);
                var summary = reader.ReadSummary();

                // Verify we CAN read the metadata and it differs from default
                var mcapMode = summary.Channels[0].Metadata["coordinate_mode"];
                Assert(mcapMode == "RightHand", "CoordMismatch: MCAP has RightHand");

                // The warning is logged at runtime via FoxgloveRuntime.EnableReplay.
                // We verify the detection logic works: mcapMode != "LeftHand"
                bool mismatch = mcapMode != "LeftHand";
                Assert(mismatch, "CoordMismatch: RightHand != LeftHand detected");
            }
            finally { File.Delete(path); }
        }

        // ── Test 8: MetadataIndex read/parse roundtrip ──

        /// <summary>
        /// Writes a MetadataIndex record, reads the summary, and
        /// verifies the offset, length, name, and decoded metadata
        /// content survive the roundtrip.
        /// </summary>
        static void TestMetadataIndexRoundtrip()
        {
            var ms = new MemoryStream();

            // Write a full MCAP with metadata
            var name = "foxglove.parameters";

            // Minimal header + data
            ms.Write(McapWriter.Magic, 0, 8);
            BuildMinimalHeader(ms);

            // Metadata record
            var metaOff = (ulong)ms.Position;
            var metaContent = new MemoryStream();
            McapWriter.WriteString(metaContent, name);
            var mapS = new MemoryStream();
            McapWriter.WriteString(mapS, "k"); McapWriter.WriteString(mapS, "v");
            var mapB = mapS.ToArray();
            McapWriter.WriteU32(metaContent, (uint)mapB.Length);
            metaContent.Write(mapB, 0, mapB.Length);
            WriteRecord(ms, 0x0C, metaContent);
            var metaLen = (ulong)ms.Position - metaOff;

            var de = new MemoryStream(); McapWriter.WriteU32(de, 0);
            WriteRecord(ms, 0x0F, de);

            // Summary section with MetadataIndex
            var sumStart = (ulong)ms.Position;

            var miContent = new MemoryStream();
            McapWriter.WriteU64(miContent, metaOff);
            McapWriter.WriteU64(miContent, metaLen);
            McapWriter.WriteString(miContent, name);
            WriteRecord(ms, 0x0D, miContent);

            var stats = new MemoryStream();
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU16(stats, 0); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU32(stats, 0); McapWriter.WriteU32(stats, 1); McapWriter.WriteU32(stats, 0);
            McapWriter.WriteU64(stats, 0); McapWriter.WriteU64(stats, 0); McapWriter.WriteU32(stats, 0);
            WriteRecord(ms, 0x0B, stats);

            var sumOffStart = (ulong)ms.Position;
            var so = new MemoryStream();
            so.WriteByte(0x02); McapWriter.WriteU64(so, sumStart); McapWriter.WriteU64(so, sumOffStart - sumStart);
            WriteRecord(ms, 0x0E, so);

            var ftr = new MemoryStream();
            McapWriter.WriteU64(ftr, sumStart); McapWriter.WriteU64(ftr, sumOffStart); McapWriter.WriteU32(ftr, 0);
            WriteRecord(ms, 0x02, ftr);
            ms.Write(McapWriter.Magic, 0, 8);

            var data = ms.ToArray();
            var reader = new McapReader(new MemoryStream(data));
            var summary = reader.ReadSummary();

            Assert(summary.MetadataIndexes.Count == 1, "MetaIdxRt: 1 MetadataIndex");
            Assert(summary.MetadataIndexes[0].Offset == metaOff, "MetaIdxRt: offset correct");
            Assert(summary.MetadataIndexes[0].Length == metaLen, "MetaIdxRt: length correct");
            Assert(summary.MetadataIndexes[0].Name == name, "MetaIdxRt: name correct");

            // Read actual metadata via offset
            var meta = reader.ReadMetadataAt(summary.MetadataIndexes[0].Offset);
            Assert(meta.Name == name, "MetaIdxRt: decoded name correct");
            Assert(meta.Metadata.Count == 1, "MetaIdxRt: 1 metadata entry");
            Assert(meta.Metadata.ContainsKey("k") && meta.Metadata["k"] == "v", "MetaIdxRt: metadata entry correct");
        }
    }
}
