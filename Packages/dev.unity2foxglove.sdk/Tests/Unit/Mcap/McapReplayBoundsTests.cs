// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public void ScanBudgetBoundaryFollowsTheOldestRetainedCandidates()
        {
            // Out-of-order records inside one chunk: after the budget is reached the boundary must
            // track the oldest retained candidates, so a record past that boundary ends the tick.
            var times = new ulong[] { 10, 20, 15, 18, 40 };
            var path = CreateMcap(pathName: "mseries-m2-r2-boundary", chunkSizeBytes: 4096,
                writeMessages: recorder =>
                {
                    for (var i = 0; i < times.Length; i++)
                        recorder.WriteMessage(1, times[i], new byte[] { (byte)i });
                });
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.MaxMessagesPerTick = 2;
                engine.Play();

                // Tick reuses its result buffer, so each tick is copied before the next one runs.
                var first = engine.Tick(100).Select(message => message.LogTime).ToArray();
                var scanned = engine.LastTickScannedRecordCount;
                var second = engine.Tick(100).Select(message => message.LogTime).ToArray();

                // The tick emits the two oldest due records and stops at the boundary they define;
                // the out-of-order records behind that boundary follow in the next tick.
                Assert.Equal(new ulong[] { 10, 15 }, first);
                Assert.Equal(new ulong[] { 18, 20 }, second);
                // Retaining the newest candidates instead of the oldest would raise the boundary and
                // scan past it, so the scanned-record count is part of the contract.
                Assert.Equal(4, scanned);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void SnapshotKeepsScanningOverlappingOlderChunksForABetterChannelWinner()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "mseries-m2-r3-overlap-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 4096, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/mseries/first", "json", "mseries.First", "jsonschema", "{}");
                    recorder.AddChannel(2, "/mseries/second", "json", "mseries.Second", "jsonschema", "{}");
                    // Chunk 1 spans [1000, 6000] and already holds a candidate for both channels.
                    recorder.WriteMessage(1, 6000, new byte[] { 1 });
                    recorder.WriteMessage(2, 1000, new byte[] { 2 });
                    // The attachment closes the chunk, so the newer channel-2 message lands in an
                    // older-ending chunk that still overlaps the first one.
                    recorder.AddAttachment("boundary", "application/octet-stream", new byte[] { 0 }, 1000);
                    recorder.WriteMessage(2, 5000, new byte[] { 3 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var snapshot = engine.Snapshot(engine.EndTimeNs, new List<McapMessage>());

                Assert.Equal(2, snapshot.Count);
                Assert.Equal(6000UL, snapshot.Single(message => message.ChannelId == 1).LogTime);
                Assert.Equal(5000UL, snapshot.Single(message => message.ChannelId == 2).LogTime);
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
                Assert.Equal(0, DeferredRetryOwnerCount(engine));
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
            const int futureMessageCount = 100_001;
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
                Assert.Equal(futureMessageCount - 1, retries.Count);
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

        private static int DeferredRetryOwnerCount(McapReplayEngine engine)
        {
            var ownersField = typeof(McapReplayEngine).GetField(
                "_deferredRetryOwners",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(ownersField);
            return ((System.Collections.IDictionary)ownersField.GetValue(engine)).Count;
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
