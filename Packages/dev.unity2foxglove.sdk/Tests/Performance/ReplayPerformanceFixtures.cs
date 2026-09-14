// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Performance
{
    internal sealed class ReplayPerformanceFixture
    {
        public string Path { get; set; }
        public string HashSha256 { get; set; }
        public long Bytes { get; set; }
        public int Seed { get; set; }
        public int MessageCount { get; set; }
        public int ChannelCount { get; set; }
        public string GeneratorVersion { get; set; }
        public int ChunkSizeBytes { get; set; }
        public string Compression { get; set; }
        public string Density { get; set; }
        public string[] EncodingMix { get; set; }
        public ulong TimeStartNs { get; set; }
        public ulong TimeEndNs { get; set; }
        public int[] MessagesPerChannel { get; set; }
    }

    internal static class ReplayPerformanceFixtures
    {
        internal const int Seed = 188042;
        internal const string GeneratorVersion = "phase188-fixture-v2";
        internal const int ChunkSizeBytes = 4096;
        internal const string Compression = "none";
        internal const string Density = "dense";
        private static readonly string[] EncodingMix = { "json" };

        internal static ReplayPerformanceFixture Ensure(string outputDir, bool full)
        {
            Directory.CreateDirectory(outputDir);
            var fixturePath = System.IO.Path.Combine(outputDir, full ? "replay-full.mcap" : "replay-quick.mcap");
            var manifestPath = fixturePath + ".manifest.json";
            var messageCount = full ? 12000 : 1200;
            const int channelCount = 4;
            var expectedTimeEndNs = (ulong)((messageCount - 1) * 1000);
            var expectedMessagesPerChannel = BuildMessagesPerChannel(messageCount, channelCount);

            if (File.Exists(fixturePath) && File.Exists(manifestPath))
            {
                var existing = JsonConvert.DeserializeObject<ReplayPerformanceFixture>(File.ReadAllText(manifestPath));
                if (existing != null && existing.Seed == Seed && existing.MessageCount == messageCount
                    && existing.ChannelCount == channelCount && existing.Bytes == new FileInfo(fixturePath).Length
                    && string.Equals(existing.HashSha256, ComputeSha256(fixturePath), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.GeneratorVersion, GeneratorVersion, StringComparison.Ordinal)
                    && existing.ChunkSizeBytes == ChunkSizeBytes
                    && string.Equals(existing.Compression, Compression, StringComparison.Ordinal)
                    && string.Equals(existing.Density, Density, StringComparison.Ordinal)
                    && HasEncoding(existing.EncodingMix, "json")
                    && existing.TimeStartNs == 0
                    && existing.TimeEndNs == expectedTimeEndNs
                    && ArraysEqual(existing.MessagesPerChannel, expectedMessagesPerChannel))
                    return existing;
            }

            var tempPath = fixturePath + ".tmp-" + Guid.NewGuid().ToString("N");
            var manifestTempPath = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: ChunkSizeBytes, compression: "", leaveOpen: true))
                {
                    for (uint channel = 1; channel <= channelCount; channel++)
                        recorder.AddChannel(channel, "/phase188/channel/" + channel, "json", "phase188.Channel", "jsonschema", "{}");

                    for (var i = 0; i < messageCount; i++)
                    {
                        var channel = (uint)(i % channelCount) + 1;
                        var payload = new byte[32];
                        for (var j = 0; j < payload.Length; j++)
                            payload[j] = (byte)((Seed + i * 17 + j * 31) & 0xff);
                        recorder.WriteMessage(channel, (ulong)(i * 1000), payload);
                    }
                    recorder.Close();
                }

                File.Move(tempPath, fixturePath, overwrite: true);
                var fixture = new ReplayPerformanceFixture
                {
                    Path = fixturePath,
                    HashSha256 = ComputeSha256(fixturePath),
                    Bytes = new FileInfo(fixturePath).Length,
                    Seed = Seed,
                    MessageCount = messageCount,
                    ChannelCount = channelCount,
                    GeneratorVersion = GeneratorVersion,
                    ChunkSizeBytes = ChunkSizeBytes,
                    Compression = Compression,
                    Density = Density,
                    EncodingMix = (string[])EncodingMix.Clone(),
                    TimeStartNs = 0,
                    TimeEndNs = expectedTimeEndNs,
                    MessagesPerChannel = expectedMessagesPerChannel
                };
                // Finalize the manifest atomically as well as the fixture. A
                // killed generator must never leave a plausible but partial
                // manifest that a later timed run would accept.
                File.WriteAllText(manifestTempPath, JsonConvert.SerializeObject(fixture, Formatting.Indented));
                File.Move(manifestTempPath, manifestPath, overwrite: true);
                return fixture;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                if (File.Exists(manifestTempPath))
                    File.Delete(manifestTempPath);
            }
        }

        private static int[] BuildMessagesPerChannel(int messageCount, int channelCount)
        {
            var counts = new int[channelCount];
            for (var i = 0; i < messageCount; i++)
                counts[i % channelCount]++;
            return counts;
        }

        private static bool HasEncoding(string[] encodings, string expected)
        {
            if (encodings == null)
                return false;
            for (var i = 0; i < encodings.Length; i++)
                if (string.Equals(encodings[i], expected, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool ArraysEqual(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;
            return true;
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
