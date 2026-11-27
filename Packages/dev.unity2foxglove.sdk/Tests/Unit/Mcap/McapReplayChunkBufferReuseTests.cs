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
        [Theory]
        [InlineData("")]
        [InlineData("lz4")]
        [InlineData("zstd")]
        /// <summary>Sequential chunks reuse buffers when possible and every payload stays intact.</summary>
        public void ReplayReusesTheChunkBufferWhenNoDeferredMessageOwnsIt(string compression)
        {
            const int messageCount = 40;
            var path = CreateChunkReuseMcap("chunk-reuse-" + compression, 64 * 1024, recorder =>
            {
                for (var index = 0; index < messageCount; index++)
                    recorder.WriteMessage(1, (ulong)(index + 1) * 10UL, Enumerable.Repeat((byte)index, 16 * 1024).ToArray());
            }, compression);
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
                Assert.True(
                    buffers.Count < chunkCount,
                    $"expected reuse across chunks, got {buffers.Count} buffers for {chunkCount} chunks");
            }
            finally
            {
                DeleteChunkReuseMcap(path);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("lz4")]
        [InlineData("zstd")]
        /// <summary>A deferred future payload keeps its chunk buffer while a later chunk is decoded.</summary>
        public void ReplayKeepsAChunkBufferOwnedByDeferredMessages(string compression)
        {
            var path = CreateChunkReuseMcap("chunk-reuse-deferred-" + compression, 128 * 1024, recorder =>
            {
                recorder.WriteMessage(1, 10UL, new byte[] { 1 });
                recorder.WriteMessage(1, 1000UL, Enumerable.Repeat((byte)0xAB, 100 * 1024).ToArray());
                recorder.WriteMessage(1, 20UL, Enumerable.Repeat((byte)0xCD, 60 * 1024).ToArray());
            }, compression);
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

        [Theory]
        [InlineData("")]
        [InlineData("lz4")]
        [InlineData("zstd")]
        /// <summary>A smaller later chunk reuses a larger buffer within the two-times bound.</summary>
        public void ReplayReusesLargerChunkBufferForSmallerChunkWithinBound(string compression)
        {
            var path = CreateChunkReuseMcap("chunk-reuse-smaller-" + compression, 64 * 1024, recorder =>
            {
                for (var index = 0; index < 5; index++)
                    recorder.WriteMessage(1, (ulong)(index + 1) * 10UL, Enumerable.Repeat((byte)index, 16 * 1024).ToArray());
            }, compression);
            try
            {
                AssertChunkCrcsValid(path);
                using var engine = new McapReplayEngine();
                engine.Load(path);
                engine.Play();

                var first = engine.Tick(30UL);
                var firstBuffer = CurrentReplayChunkBuffer(engine);
                var firstLength = CurrentReplayChunkLogicalLength(engine);
                Assert.Equal(new ulong[] { 10UL, 20UL, 30UL }, first.Select(message => message.LogTime).ToArray());
                Assert.All(first, message => Assert.All(message.Data, value => Assert.Equal((byte)(message.LogTime / 10UL - 1UL), value)));
                Assert.True(firstLength > 48 * 1024);

                var second = engine.Tick(50UL);
                var secondBuffer = CurrentReplayChunkBuffer(engine);
                var secondLength = CurrentReplayChunkLogicalLength(engine);
                Assert.Equal(new ulong[] { 40UL, 50UL }, second.Select(message => message.LogTime).ToArray());
                Assert.All(second, message => Assert.All(message.Data, value => Assert.Equal((byte)(message.LogTime / 10UL - 1UL), value)));
                Assert.True(firstLength > secondLength);
                Assert.True(firstLength <= secondLength * 2L);
                Assert.Same(firstBuffer, secondBuffer);
                Assert.Empty(engine.Tick(60UL));
                Assert.Equal(McapReplayEngine.Status.Ended, engine.CurrentStatus);
            }
            finally
            {
                DeleteChunkReuseMcap(path);
            }
        }

        [Fact]
        /// <summary>Do not retain a reusable buffer that is more than twice the decoded size.</summary>
        public void DecompressionDoesNotReuseAnOversizedBuffer()
        {
            var payload = Enumerable.Repeat((byte)0xA5, 64).ToArray();
            var reusable = new byte[payload.Length * 2 + 1];

            var output = McapCompression.Decompress(
                "",
                new ArraySegment<byte>(payload),
                payload.Length,
                0,
                reusable);

            Assert.NotSame(reusable, output);
            Assert.Equal(payload, output);
        }

        [Fact]
        /// <summary>Reject a message header that is shorter than the logical record boundary.</summary>
        public void ChunkRecordReaderRejectsMessageHeaderOutsideLogicalLength()
        {
            var bytes = new byte[32];
            bytes[0] = McapWriter.OpcodeMessage;
            WriteU64(bytes, 1, 21UL);
            var offset = 0;

            var error = Assert.Throws<InvalidDataException>(
                () => McapReplayChunkRecordReader.ReadNext(bytes, 30, ref offset));

            Assert.Equal("MCAP chunk message record is truncated.", error.Message);
            Assert.Equal(9, offset);
        }

        private static byte[] CurrentReplayChunkBuffer(McapReplayEngine engine)
        {
            var field = typeof(McapReplayEngine).GetField("_currentUncompressed", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (byte[])field.GetValue(engine);
        }

        private static int CurrentReplayChunkLogicalLength(McapReplayEngine engine)
        {
            var buffer = CurrentReplayChunkBuffer(engine);
            var tailField = typeof(McapReplayEngine).GetField("_currentUncompressedTail", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(tailField);
            return buffer.Length - (int)tailField.GetValue(engine);
        }

        private static void AssertChunkCrcsValid(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new McapReader(stream);
            var summary = reader.ReadSummary();
            foreach (var chunk in summary.ChunkIndexes)
            {
                reader.ReadChunkRecords(chunk.ChunkStartOffset, chunk.ChunkLength, out var crcValid);
                Assert.True(crcValid, $"chunk at {chunk.ChunkStartOffset} failed CRC validation");
            }
        }

        private static string CreateChunkReuseMcap(
            string name,
            int chunkSizeBytes,
            Action<McapRecorder> writeMessages,
            string compression = "")
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
                           Compression = compression,
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

        private static void WriteU64(byte[] bytes, int offset, ulong value)
        {
            for (var shift = 0; shift < 64; shift += 8)
                bytes[offset + shift / 8] = (byte)(value >> shift);
        }

        private static void DeleteChunkReuseMcap(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
