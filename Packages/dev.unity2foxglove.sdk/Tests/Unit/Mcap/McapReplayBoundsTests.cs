// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Reflection;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed class McapReplayBoundsTests
    {
        [Fact]
        public void FuturePayloadInDueChunkIsNotClonedIntoPending()
        {
            var path = CreateMcap(pathName: "r4-f03-004", chunkSizeBytes: 16 * 1024 * 1024,
                writeMessages: recorder =>
                {
                    recorder.WriteMessage(1, 10, new byte[] { 10 });
                    recorder.WriteMessage(1, 1_000_000_000, new byte[8 * 1024 * 1024]);
                });
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.MaxMessagesPerTick = 1;
                engine.Play();

                var first = engine.Tick(10);

                var emitted = Assert.Single(first);
                Assert.Equal(10UL, emitted.LogTime);
                Assert.Equal(0, PendingCount(engine));

                var future = engine.Tick(1_000_000_000);
                var futureMessage = Assert.Single(future);
                Assert.Equal(1_000_000_000UL, futureMessage.LogTime);
                Assert.Equal(8 * 1024 * 1024, futureMessage.Data.Length);
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static string CreateMcap(
            string pathName,
            int chunkSizeBytes,
            Action<McapRecorder> writeMessages)
        {
            var path = Path.Combine(Path.GetTempPath(), pathName + "-" + Guid.NewGuid().ToString("N") + ".mcap");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            using (var recorder = new McapRecorder(
                       stream,
                       null,
                       new McapWriterOptions
                       {
                           UseChunking = true,
                           ChunkSizeBytes = chunkSizeBytes,
                           IndexTypes = McapIndexTypes.Chunk,
                           UseStatistics = true
                       },
                       leaveOpen: true))
            {
                recorder.AddChannel(1, "/r4/replay", "json", "r4.Schema", "jsonschema", "{}");
                writeMessages(recorder);
                recorder.Close();
            }

            return path;
        }

        private static int PendingCount(McapReplayEngine engine)
        {
            var field = typeof(McapReplayEngine).GetField(
                "_pending",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var queue = field.GetValue(engine);
            var property = queue.GetType().GetProperty(
                "Count",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(property);
            return (int)property.GetValue(queue);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Test cleanup is best effort on platforms with delayed handles.
            }
        }
    }
}
