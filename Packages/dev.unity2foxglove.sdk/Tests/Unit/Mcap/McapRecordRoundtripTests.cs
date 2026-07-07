// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP magic/record roundtrips, recorder operations, dual-write
//          (migrated from Phase10Validation).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Tests; // McapRecordReader (shared test helper)
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// MCAP magic bytes, record roundtrips (header/schema/channel/message/chunk),
    /// full pipeline, recorder operations, dual-write to session, and close
    /// idempotency. Ported from Phase10Validation.
    /// </summary>
    [Trait("Phase", "10")]
    [Trait("Domain", "Mcap")]
    public class McapRecordRoundtripTests
    {
        [Fact]
        public void MagicBytes()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteMagic();
            var data = ms.ToArray();
            Assert.Equal(8, data.Length);
            Assert.Equal(0x89, data[0]);
            Assert.Equal((byte)'M', data[1]);
        }

        [Fact]
        public void MinimalValidFile()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteMagic();
            w.WriteHeader("", "test");
            w.WriteDataEnd();
            w.WriteFooter(0, 0, 0);
            w.WriteMagic();
            var data = ms.ToArray();
            Assert.True(data.Length >= 75, $"Minimal file >= 75 bytes (got {data.Length})");
            var (hasLeading, records, hasTrailing) = McapRecordReader.Parse(data);
            Assert.True(hasLeading, "Has leading magic");
            Assert.True(hasTrailing, "Has trailing magic");
            var ops = records.Select(r => r.Opcode).ToList();
            Assert.True(ops.Contains(0x01), "Has header");
            Assert.True(ops.Contains(0x0F), "Has DataEnd");
            Assert.True(ops.Contains(0x02), "Has Footer");
        }

        [Fact]
        public void HeaderRoundtrip()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteHeader("test-profile", "test-lib");
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var hdr = records[0];
            Assert.Equal(0x01, hdr.Opcode);
            var (profile, lib) = McapRecordReader.DecodeHeader(hdr.Content);
            Assert.Equal("test-profile", profile);
            Assert.Equal("test-lib", lib);
        }

        [Fact]
        public void SchemaRoundtrip()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteSchema(1, "foxglove.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var (id, name, enc, d) = McapRecordReader.DecodeSchema(records[0].Content);
            Assert.Equal((ushort)1, id);
            Assert.Equal("foxglove.Schema", name);
            Assert.Equal("jsonschema", enc);
            Assert.Equal(Encoding.UTF8.GetBytes("{}"), d);
        }

        [Fact]
        public void ChannelRoundtrip()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteChannel(1, 0, "/topic", "json", new Dictionary<string, string>());
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var (id, sid, topic, enc) = McapRecordReader.DecodeChannel(records[0].Content);
            Assert.Equal((ushort)1, id);
            Assert.Equal((ushort)0, sid);
            Assert.Equal("/topic", topic);
            Assert.Equal("json", enc);
        }

        [Fact]
        public void MessageRoundtrip()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteMessage(1, 10, 123456789, 123456789, Encoding.UTF8.GetBytes("hello"));
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var (chId, seq, log, pub, pl) = McapRecordReader.DecodeMessage(records[0].Content);
            Assert.Equal((ushort)1, chId);
            Assert.Equal(10u, seq);
            Assert.Equal(123456789UL, log);
            Assert.Equal(123456789UL, pub);
            Assert.Equal("hello", Encoding.UTF8.GetString(pl));
        }

        [Fact]
        public void ChunkRoundtrip()
        {
            var innerMs = new MemoryStream();
            var iw = new McapWriter(innerMs);
            iw.WriteMessage(1, 1, 100, 100, new byte[] { 1 });
            var innerBytes = innerMs.ToArray();

            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteChunk(100, 200, (ulong)innerBytes.Length, 0, "", (ulong)innerBytes.Length, innerBytes);
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var (st, et, size, crc, comp, _, recs) = McapRecordReader.DecodeChunk(records[0].Content);
            Assert.True(st == 100, "chunk start");
            Assert.True(et == 200, "chunk end");
            Assert.True(recs.Length > 0, "chunk has inner records");
        }

        [Fact]
        public void FullPipeline()
        {
            var ms = new MemoryStream();
            var w = new McapWriter(ms);
            w.WriteMagic();
            w.WriteHeader("", "test");
            w.WriteSchema(1, "s", "json", new byte[] { 1 });
            w.WriteChannel(1, 1, "/t", "json", new());
            w.WriteMessage(1, 0, 100, 100, new byte[] { 2 });
            w.WriteDataEnd();
            w.WriteFooter(0, 0, 0);
            w.WriteMagic();
            var data = ms.ToArray();
            var (hl, records, ht) = McapRecordReader.Parse(data);
            Assert.True(hl && ht, "Full pipeline magic OK");
            Assert.True(records.Count >= 5, $"Full pipeline has >=5 records (got {records.Count})");
        }

        [Fact]
        public void RecorderMinimal()
        {
            var ms = new MemoryStream();
            var r = new McapRecorder(ms);
            r.Close();
            Assert.True(ms.Length > 0, "Recorder produces output");
        }

        [Fact]
        public void RecorderSingleChannel()
        {
            var ms = new MemoryStream();
            var r = new McapRecorder(ms);
            r.AddChannel(1, "/t", "json", "", "", "");
            r.Close();
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            Assert.True(records.Any(x => x.Opcode == 0x04), "Has channel record");
        }

        [Fact]
        public void RecorderMultipleMessages()
        {
            var ms = new MemoryStream();
            var r = new McapRecorder(ms, chunkSizeBytes: 256);
            r.AddChannel(1, "/t", "json", "", "", "");
            for (var i = 0; i < 5; i++)
                r.WriteMessage(1, (ulong)(i * 1_000_000), new byte[] { (byte)i });
            r.Close();
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var chunkRecs = records.Where(x => x.Opcode == 0x06).ToList();
            Assert.True(chunkRecs.Count >= 1, $"Has chunk records (got {chunkRecs.Count})");
        }

        [Fact]
        public void RecorderSchemaContentIsStableAcrossChannels()
        {
            var ms = new MemoryStream();
            var r = new McapRecorder(ms);
            r.AddChannel(1, "/t1", "json", "foxglove.FrameTransform", "jsonschema", "{}");
            r.AddChannel(2, "/t2", "json", "foxglove.FrameTransform", "jsonschema", "{}");
            r.Close();
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var schemas = records
                .Where(x => x.Opcode == 0x03)
                .Select(x => McapRecordReader.DecodeSchema(x.Content))
                .ToList();
            Assert.Equal(2, schemas.Count);
            Assert.Single(schemas.Select(schema => schema.id).Distinct());

            foreach (var schema in schemas)
            {
                Assert.Equal("foxglove.FrameTransform", schema.name);
                Assert.Equal("jsonschema", schema.encoding);
                Assert.Equal(Encoding.UTF8.GetBytes("{}"), schema.data);
            }

            var schemaId = schemas[0].id;
            var uniqueChannels = records
                .Where(x => x.Opcode == 0x04)
                .Select(x => McapRecordReader.DecodeChannel(x.Content))
                .GroupBy(channel => channel.id)
                .Select(group => group.First())
                .ToList();

            Assert.Equal(2, uniqueChannels.Count);
            Assert.All(uniqueChannels, channel => Assert.Equal(schemaId, channel.schemaId));
            Assert.Contains(uniqueChannels, channel => channel.topic == "/t1");
            Assert.Contains(uniqueChannels, channel => channel.topic == "/t2");
        }

        [Fact]
        public void DualWrite()
        {
            var ms = new MemoryStream();
            var r = new McapRecorder(ms);
            var fake = new Phase10FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.SetRecorder(r);
            session.RegisterChannel(new Protocol.AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            session.Publish(1, new byte[] { 42 }, 123456789UL);
            r.Close();
            var data = ms.ToArray();
            var (_, records, _) = McapRecordReader.Parse(data);
            var found = records
                .Where(rec => rec.Opcode == 0x06)
                .SelectMany(rec => McapRecordReader.Parse(McapRecordReader.DecodeChunk(rec.Content).records).records)
                .Count(rec => rec.Opcode == 0x05);
            Assert.True(found >= 1, $"Dual write: {found} messages in MCAP");
        }

        [Fact]
        public void CloseIdempotent()
        {
            var ms = new MemoryStream();
            var r = new McapRecorder(ms);
            r.Close();
            r.Close(); // must not throw
            Assert.True(ms.Length > 0, "Close writes a valid MCAP payload");
        }

        /// <summary>Minimal no-op transport for the dual-write test.</summary>
        private sealed class Phase10FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string h, int p) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint id, string json) { }
            public void SendBinary(uint id, byte[] data) { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
        }
    }
}
