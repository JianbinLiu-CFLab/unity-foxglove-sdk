// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 37 regression tests for direct-write MCAP message records.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase37Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 37 Tests ---");
            _passCount = 0;

            VerifyDirectMessageRoundtrip();
            VerifyNullAndEmptyPayloads();
            VerifyInterleavedChannelsKeepCounts();
            VerifySmallChunksRemainReadable();

            Console.WriteLine("Phase 37: All checks passed.");
        }

        private static void VerifyDirectMessageRoundtrip()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/rt", "json", "test.RT", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 100, Encoding.UTF8.GetBytes("{\"s\":0}"));
                recorder.WriteMessage(1, 200, Encoding.UTF8.GetBytes("{\"s\":1}"));
                recorder.WriteMessage(1, 300, Encoding.UTF8.GetBytes("{\"s\":2}"));
                recorder.Close();
            }

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Check(summary.Channels.Count == 1, "37A-1: one channel in summary");
            Check(summary.Statistics.MessageCount == 3, "37A-1b: message count = 3");

            var chunkIdx = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunkIdx.ChunkStartOffset, chunkIdx.ChunkLength, out var crcValid);
            Check(crcValid, "37A-1c: chunk CRC valid");
            var messages = reader.ReadChunkMessages(records);

            Check(messages.Count == 3, "37A-1d: decoded messages = 3");
            Check(messages[0].ChannelId == 1, "37A-1e: channel id correct");
            Check(messages[0].Sequence == 0, "37A-1f: sequence 0");
            Check(messages[0].LogTime == 100, "37A-1g: log time 100");
            Check(messages[0].PublishTime == 100, "37A-1h: publish time matches");
            Check(Encoding.UTF8.GetString(messages[0].Data) == "{\"s\":0}", "37A-1i: payload roundtrip");

            Check(messages[1].Sequence == 1, "37A-1j: sequence 1");
            Check(messages[1].LogTime == 200, "37A-1k: log time 200");

            Check(messages[2].Sequence == 2, "37A-1l: sequence 2");
            Check(messages[2].LogTime == 300, "37A-1m: log time 300");
        }

        private static void VerifyNullAndEmptyPayloads()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/null", "json", "test.Null", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 1000, null);
                recorder.WriteMessage(1, 2000, Array.Empty<byte>());
                recorder.Close();
            }

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            var chunkIdx = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunkIdx.ChunkStartOffset, chunkIdx.ChunkLength, out _);
            var messages = reader.ReadChunkMessages(records);

            Check(messages.Count == 2, "37A-2: two messages decoded");
            Check(messages[0].Data.Length == 0, "37A-2b: null payload decodes as zero-length");
            Check(messages[1].Data.Length == 0, "37A-2c: empty payload decodes as zero-length");
        }

        private static void VerifyInterleavedChannelsKeepCounts()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/a", "json", "test.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/b", "json", "test.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(3, "/c", "json", "test.C", "jsonschema", "{\"type\":\"object\"}");

                recorder.WriteMessage(1, 100, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(2, 200, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(3, 300, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(1, 400, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(2, 500, Encoding.UTF8.GetBytes("{}"));
                recorder.Close();
            }

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Check(summary.Statistics.MessageCount == 5, "37A-3: total message count = 5");
            Check(summary.Statistics.ChannelMessageCounts[1] == 2, "37A-3b: channel 1 has 2 messages");
            Check(summary.Statistics.ChannelMessageCounts[2] == 2, "37A-3c: channel 2 has 2 messages");
            Check(summary.Statistics.ChannelMessageCounts[3] == 1, "37A-3d: channel 3 has 1 message");

            var chunkIdx = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunkIdx.ChunkStartOffset, chunkIdx.ChunkLength, out _);
            var messages = reader.ReadChunkMessages(records);
            Check(messages.Count == 5, "37A-3e: decoded all 5 interleaved messages");
        }

        private static void VerifySmallChunksRemainReadable()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, chunkSizeBytes: 96))
            {
                recorder.AddChannel(1, "/chunk", "json", "test.Chunk", "jsonschema", "{\"type\":\"object\"}");
                for (var i = 0; i < 20; i++)
                    recorder.WriteMessage(1, (ulong)i * 1000, Encoding.UTF8.GetBytes($"{{\"i\":{i}}}"));
                recorder.Close();
            }

            ms.Position = 0;
            var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Check(summary.Statistics.MessageCount == 20, "37A-4: total message count across chunks");
            Check(summary.ChunkIndexes.Count > 1, "37A-4b: multiple chunks written");

            var totalMessages = 0;
            foreach (var ci in summary.ChunkIndexes)
            {
                var records = reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength, out var crcValid);
                Check(crcValid, "37A-4c: chunk CRC valid");
                totalMessages += reader.ReadChunkMessages(records).Count;
            }

            Check(totalMessages == 20, "37A-4d: all 20 messages recovered across chunks");
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }
            throw new Exception($"[FAIL] {label}");
        }
    }
}
