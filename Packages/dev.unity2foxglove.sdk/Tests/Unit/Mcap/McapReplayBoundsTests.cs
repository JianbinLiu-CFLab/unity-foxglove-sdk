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

        [Fact]
        public void IntraChunkDueRecordsRespectPerTickScanBudget()
        {
            var path = CreateMcap(pathName: "r4-f03-005", chunkSizeBytes: 4096,
                writeMessages: recorder =>
                {
                    for (var i = 0; i < 100; i++)
                        recorder.WriteMessage(1, (ulong)(i + 1), new byte[] { (byte)i });
                });
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.MaxMessagesPerTick = 1;
                engine.Play();

                var first = engine.Tick(100);
                var firstMessage = Assert.Single(first);
                Assert.Equal(1UL, firstMessage.LogTime);
                Assert.Equal(0, PendingCount(engine));
                Assert.InRange(
                    engine.LastTickScannedRecordCount,
                    1,
                    2);

                var emitted = 1;
                var previousTime = firstMessage.LogTime;
                while (emitted < 100)
                {
                    var tick = engine.Tick(100);
                    foreach (var message in tick)
                    {
                        Assert.True(message.LogTime >= previousTime);
                        previousTime = message.LogTime;
                        emitted++;
                    }
                }

                Assert.Equal(100, emitted);
                Assert.Equal(100UL, previousTime);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void DeferredFutureOwnersRespectByteAndMessageBounds()
        {
            var path = CreateMcap(pathName: "r4-f04-deferred-bound", chunkSizeBytes: 128,
                writeMessages: recorder =>
                {
                    for (var i = 0; i < 24; i++)
                    {
                        recorder.WriteMessage(1, (ulong)(1_000_000 + i), new byte[16]);
                        recorder.WriteMessage(1, (ulong)(10 + i), new byte[16]);
                    }
                });
            try
            {
                using var engine = new McapReplayEngine
                {
                    MaxMessagesPerTick = 0,
                    MaxDeferredOwnerBytes = 256,
                    MaxDeferredMessages = 3
                };
                engine.Load(path);
                engine.Play();

                engine.Tick(100);

                var deferred = DeferredStats(engine);
                Assert.InRange(deferred.messageCount, 1, 3);
                Assert.True(deferred.ownerBytes > 0);
                Assert.InRange(deferred.ownerBytes, 0, 256);

                engine.Tick(2_000_000);
                var drained = DeferredStats(engine);
                Assert.Equal(0, drained.messageCount);
                Assert.Equal(0L, drained.ownerBytes);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void DeferredFutureMessageCountBoundIsIndependentOfOwnerByteBound()
        {
            var path = CreateMcap(pathName: "r4-f04-deferred-message-cap", chunkSizeBytes: 4096,
                writeMessages: recorder =>
                {
                    // Keep the first chunk eligible at Tick(0), then force
                    // the remaining records into the deferred-future path.
                    recorder.WriteMessage(1, 0, new byte[1]);
                    for (var i = 0; i < 11; i++)
                        recorder.WriteMessage(1, (ulong)(1_000_000 + i), new byte[1]);
                });
            try
            {
                using var engine = new McapReplayEngine
                {
                    MaxMessagesPerTick = 0,
                    MaxDeferredOwnerBytes = 1_000_000,
                    MaxDeferredMessages = 3
                };
                engine.Load(path);
                engine.Play();

                engine.Tick(0);

                var deferred = DeferredStats(engine);
                Assert.Equal(3, deferred.messageCount);
                Assert.True(deferred.ownerBytes > 0);
                Assert.InRange(deferred.ownerBytes, 0, 1_000_000);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void OversizedDeferredOwnerDoesNotStarveLaterDueRecord()
        {
            var path = CreateMcap(pathName: "r4-f04-deferred-starvation", chunkSizeBytes: 4096,
                writeMessages: recorder =>
                {
                    recorder.WriteMessage(1, 100, new byte[] { 100 });
                    recorder.WriteMessage(1, 10, new byte[] { 10 });
                });
            try
            {
                using var engine = new McapReplayEngine
                {
                    MaxMessagesPerTick = 1,
                    MaxDeferredOwnerBytes = 1,
                    MaxDeferredMessages = 8
                };
                engine.Load(path);
                engine.Play();

                var tick = engine.Tick(10);

                var due = Assert.Single(tick);
                Assert.Equal(10UL, due.LogTime);

                // The rejected future record remains retryable, but the due
                // record must not be replayed while the clock is unchanged.
                Assert.Empty(engine.Tick(10));

                var future = Assert.Single(engine.Tick(100));
                Assert.Equal(100UL, future.LogTime);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void DeferredRetryDoesNotReplayDueRecordsAfterBlockedFuture()
        {
            var path = CreateMcap(pathName: "r4-f04-deferred-retry-order", chunkSizeBytes: 4096,
                writeMessages: recorder =>
                {
                    recorder.WriteMessage(1, 10, new byte[] { 1 });
                    recorder.WriteMessage(1, 100, new byte[] { 100 });
                    recorder.WriteMessage(1, 10, new byte[] { 2 });
                });
            try
            {
                using var engine = new McapReplayEngine
                {
                    MaxMessagesPerTick = 1,
                    MaxDeferredOwnerBytes = 1,
                    MaxDeferredMessages = 8
                };
                engine.Load(path);
                engine.Play();

                var first = engine.Tick(10);
                Assert.Equal(2, first.Count);
                Assert.Equal(new byte[] { 1 }, first[0].Data);
                Assert.Equal(new byte[] { 2 }, first[1].Data);

                // Rewinding to the blocked future must not emit the second
                // same-time record a second time.
                Assert.Empty(engine.Tick(10));

                var future = Assert.Single(engine.Tick(100));
                Assert.Equal(100UL, future.LogTime);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void DeferredRetryAdmissionRemainsResponsiveAtMetadataBound()
        {
            const int futureMessageCount = 50_000;
            var path = CreateMcap(pathName: "r4-f04-deferred-retry-scale", chunkSizeBytes: 4 * 1024 * 1024,
                writeMessages: recorder =>
                {
                    recorder.WriteMessage(1, 1_000_000, new byte[] { 1 });
                    for (var i = 0; i < futureMessageCount; i++)
                        recorder.WriteMessage(1, (ulong)(2_000_000 + i), new byte[] { 1 });
                });
            try
            {
                using var engine = new McapReplayEngine
                {
                    MaxMessagesPerTick = 0,
                    MaxDeferredOwnerBytes = 1,
                    MaxDeferredMessages = futureMessageCount
                };
                engine.Load(path);
                engine.Play();

                var tickTask = System.Threading.Tasks.Task.Run(() => engine.Tick(1_000_000));
                Assert.True(
                    tickTask.Wait(TimeSpan.FromSeconds(10)),
                    "Deferred retry admission exceeded the bounded review window.");
                Assert.Single(tickTask.GetAwaiter().GetResult());

                var retriesField = typeof(McapReplayEngine).GetField(
                    "_deferredRetries",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(retriesField);
                var retries = (System.Collections.ICollection)retriesField.GetValue(engine);
                Assert.Equal(futureMessageCount, retries.Count);
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

        private static (long ownerBytes, int messageCount) DeferredStats(McapReplayEngine engine)
        {
            var ownerBytesField = typeof(McapReplayEngine).GetField(
                "_deferredOwnerBytes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var deferredField = typeof(McapReplayEngine).GetField(
                "_deferredPending",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var headField = typeof(McapReplayEngine).GetField(
                "_deferredPendingHead",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(ownerBytesField);
            Assert.NotNull(deferredField);
            Assert.NotNull(headField);
            var entries = (System.Collections.ICollection)deferredField.GetValue(engine);
            var head = (int)headField.GetValue(engine);
            return ((long)ownerBytesField.GetValue(engine), entries.Count - head);
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
