// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Mcap
// Purpose: Reuse the replay chunk buffer only when no deferred message still owns it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    /// <summary>Reuses the replay chunk buffer only when no deferred message still owns it.</summary>
    public sealed class McapReplayChunkBufferReuseTests
    {
        [Fact]
        /// <summary>Sequential chunks of equal size decode into one buffer and every payload stays intact.</summary>
        public void ReplayReusesTheChunkBufferWhenNoDeferredMessageOwnsIt()
        {
            const int messageCount = 40;
            var path = CreateChunkReuseMcap("chunk-reuse", 64 * 1024, recorder =>
            {
                for (var index = 0; index < messageCount; index++)
                    recorder.WriteMessage(1, (ulong)(index + 1) * 10UL, Enumerable.Repeat((byte)index, 16 * 1024).ToArray());
            });
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.Play();
                var buffers = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
                var emitted = 0;
                for (var index = 0; index < messageCount; index++)
                {
                    foreach (var message in engine.Tick((ulong)(index + 1) * 10UL))
                    {
                        Assert.All(message.Data, value => Assert.Equal((byte)(message.LogTime / 10UL - 1UL), value));
                        emitted++;
                    }

                    var buffer = CurrentReplayChunkBuffer(engine);
                    if (buffer != null && buffer.Length > 0)
                        buffers.Add(buffer);
                }

                Assert.Equal(messageCount, emitted);
                var chunkCount = engine.Summary.ChunkIndexes.Count;
                Assert.True(chunkCount > 2, $"fixture produced {chunkCount} chunks");
                Assert.Single(buffers);
            }
            finally
            {
                DeleteChunkReuseMcap(path);
            }
        }

        [Fact]
        /// <summary>A deferred future payload keeps its chunk buffer while a later chunk is decoded.</summary>
        public void ReplayKeepsAChunkBufferOwnedByDeferredMessages()
        {
            var path = CreateChunkReuseMcap("chunk-reuse-deferred", 128 * 1024, recorder =>
            {
                recorder.WriteMessage(1, 10UL, new byte[] { 1 });
                recorder.WriteMessage(1, 1000UL, Enumerable.Repeat((byte)0xAB, 100 * 1024).ToArray());
                recorder.WriteMessage(1, 20UL, Enumerable.Repeat((byte)0xCD, 60 * 1024).ToArray());
            });
            try
            {
                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.Play();

                var due = engine.Tick(20UL);
                Assert.Equal(new[] { 10UL, 20UL }, due.Select(message => message.LogTime).ToArray());

                var future = Assert.Single(engine.Tick(1000UL));
                Assert.Equal(1000UL, future.LogTime);
                Assert.Equal(100 * 1024, future.Data.Length);
                Assert.All(future.Data, value => Assert.Equal((byte)0xAB, value));
            }
            finally
            {
                DeleteChunkReuseMcap(path);
            }
        }

        private static byte[] CurrentReplayChunkBuffer(McapReplayEngine engine)
        {
            var field = typeof(McapReplayEngine).GetField("_currentUncompressed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (byte[])field.GetValue(engine);
        }

        private static string CreateChunkReuseMcap(string name, int chunkSizeBytes, Action<McapRecorder> writeMessages)
        {
            var path = Path.Combine(Path.GetTempPath(), name + "-" + Guid.NewGuid().ToString("N") + ".mcap");
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
                recorder.AddChannel(1, "/chunk/reuse", "json", "chunk.Reuse", "jsonschema", "{}");
                writeMessages(recorder);
                recorder.Close();
            }

            return path;
        }

        private static void DeleteChunkReuseMcap(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
