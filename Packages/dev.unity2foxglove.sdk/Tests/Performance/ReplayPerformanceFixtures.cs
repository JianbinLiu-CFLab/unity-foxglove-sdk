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
    }

    internal static class ReplayPerformanceFixtures
    {
        internal const int Seed = 188042;

        internal static ReplayPerformanceFixture Ensure(string outputDir, bool full)
        {
            Directory.CreateDirectory(outputDir);
            var fixturePath = System.IO.Path.Combine(outputDir, full ? "replay-full.mcap" : "replay-quick.mcap");
            var manifestPath = fixturePath + ".manifest.json";
            var messageCount = full ? 12000 : 1200;
            const int channelCount = 4;

            if (File.Exists(fixturePath) && File.Exists(manifestPath))
            {
                var existing = JsonConvert.DeserializeObject<ReplayPerformanceFixture>(File.ReadAllText(manifestPath));
                if (existing != null && existing.Seed == Seed && existing.MessageCount == messageCount
                    && existing.ChannelCount == channelCount && existing.Bytes == new FileInfo(fixturePath).Length
                    && string.Equals(existing.HashSha256, ComputeSha256(fixturePath), StringComparison.OrdinalIgnoreCase))
                    return existing;
            }

            var tempPath = fixturePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 4096, compression: "", leaveOpen: true))
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
                    ChannelCount = channelCount
                };
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(fixture, Formatting.Indented));
                return fixture;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
