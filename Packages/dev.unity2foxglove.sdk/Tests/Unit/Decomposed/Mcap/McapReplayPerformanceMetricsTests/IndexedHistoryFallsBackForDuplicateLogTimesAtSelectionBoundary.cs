// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed partial class McapReplayPerformanceMetricsTests
    {
        [Theory]
        [InlineData("lz4")]
        [InlineData("zstd")]
        public void IndexedHistoryFallsBackForDuplicateLogTimesAtSelectionBoundary(string compression)
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-duplicate-times-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        UseChunking = true,
                        ChunkSizeBytes = 4096,
                        Compression = compression,
                        IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-duplicate-times", "json", "phase188.HistoryDuplicateTimes", "jsonschema", "{}");
                    recorder.WriteMessagePreservingMcapMetadata(1, 2, 10, 10, new byte[] { 2 });
                    recorder.WriteMessagePreservingMcapMetadata(1, 1, 10, 10, new byte[] { 1 });
                    recorder.WriteMessagePreservingMcapMetadata(1, 3, 20, 20, new byte[] { 3 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.History(0, 20, new List<McapMessage>(), 2, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(new ulong[] { 10, 20 }, result.Select(message => message.LogTime).ToArray());
                Assert.Equal(new byte[] { 2 }, result[0].Data);
                Assert.Equal(new byte[] { 3 }, result[1].Data);
                Assert.Equal(1, metrics.DecompressedChunkReads);
                Assert.Equal(2, metrics.CandidatePayloadCopies);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Theory]
        [InlineData("lz4")]
        [InlineData("zstd")]
        public void IndexedHistoryKeepsNonBoundaryDuplicateTimesOnFastPath(string compression)
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-duplicate-times-non-boundary-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        UseChunking = true,
                        ChunkSizeBytes = 256,
                        Compression = compression,
                        IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-duplicate-times-fast", "json", "phase188.HistoryDuplicateTimesFast", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-duplicate-times-noise", "json", "phase188.HistoryDuplicateTimesNoise", "jsonschema", "{}");
                    recorder.WriteMessagePreservingMcapMetadata(1, 1, 10, 10, new byte[] { 1 });
                    recorder.WriteMessagePreservingMcapMetadata(1, 2, 10, 10, new byte[] { 2 });
                    recorder.AddAttachment("boundary-a", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(2, 15, new byte[] { 15 });
                    recorder.AddAttachment("boundary-b", "application/octet-stream", new byte[] { 0 }, 2);
                    recorder.WriteMessagePreservingMcapMetadata(1, 3, 20, 20, new byte[] { 3 });
                    recorder.WriteMessagePreservingMcapMetadata(1, 4, 30, 30, new byte[] { 4 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                Assert.Equal(3, engine.Summary.ChunkIndexes.Count);
                var result = engine.History(0, 30, new List<McapMessage>(), 2, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(new ulong[] { 20, 30 }, result.Select(message => message.LogTime).ToArray());
                Assert.Equal(new byte[] { 3 }, result[0].Data);
                Assert.Equal(new byte[] { 4 }, result[1].Data);
                Assert.Equal(4, metrics.CandidateCount);
                Assert.Equal(1, metrics.DecompressedChunkReads);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Theory]
        [InlineData("lz4")]
        [InlineData("zstd")]
        public void BoundedHistoryFallsBackWhenCompressedSpoolLimitIsExceeded(string compression)
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-compressed-spool-limit-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        UseChunking = true,
                        ChunkSizeBytes = 4096,
                        Compression = compression,
                        IndexTypes = McapIndexTypes.Chunk
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-spool-limit-target", "json", "phase188.HistorySpoolLimitTarget", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-spool-limit-noise", "json", "phase188.HistorySpoolLimitNoise", "jsonschema", "{}");
                    recorder.WriteMessage(1, 80, new byte[] { 80 });
                    recorder.WriteMessage(1, 100, new byte[] { 100 });
                    recorder.AddAttachment("boundary-a", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(1, 70, new byte[] { 70 });
                    recorder.WriteMessage(2, 80, new byte[] { 8 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                Assert.Equal(2, engine.Summary.ChunkIndexes.Count);
                engine.MaxHistorySpoolBytes = 1;
                var result = engine.History(0, 100, new List<McapMessage>(), 2, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(new ulong[] { 80, 100 }, result.Select(message => message.LogTime).ToArray());
                Assert.Equal(new byte[] { 80 }, result[0].Data);
                Assert.Equal(new byte[] { 100 }, result[1].Data);
                Assert.Equal(3, metrics.CandidateCount);
                Assert.Equal(3, metrics.DecompressedChunkReads);
                Assert.Equal(2, metrics.CandidatePayloadCopies);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void IndexedHistoryRetainsLatestCandidatesWithBoundedHeap()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-indexed-heap-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        UseChunking = true,
                        ChunkSizeBytes = 4096,
                        IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-indexed-heap", "json", "phase188.HistoryIndexedHeap", "jsonschema", "{}");
                    for (ulong time = 1; time <= 64; time++)
                        recorder.WriteMessage(1, time, new byte[] { (byte)time });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.History(0, 64, new List<McapMessage>(), 5, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(new ulong[] { 60, 61, 62, 63, 64 }, result.Select(message => message.LogTime).ToArray());
                Assert.Equal(64, metrics.CandidateCount);
                Assert.Equal(5, metrics.CandidatePayloadCopies);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        [Theory]
        [InlineData("lz4")]
        [InlineData("zstd")]
        public void BoundedHistoryUsesMixedCompressedSpoolBudget(string compression)
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-compressed-spool-mixed-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        UseChunking = true,
                        ChunkSizeBytes = 4096,
                        Compression = compression,
                        IndexTypes = McapIndexTypes.Chunk
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-spool-mixed-target", "json", "phase188.HistorySpoolMixedTarget", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-spool-mixed-noise", "json", "phase188.HistorySpoolMixedNoise", "jsonschema", "{}");
                    recorder.WriteMessage(1, 80, new byte[] { 80 });
                    recorder.WriteMessage(2, 200, new byte[128]);
                    recorder.AddAttachment("mixed-boundary-a", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(1, 90, new byte[] { 90 });
                    recorder.WriteMessage(2, 200, new byte[128]);
                    recorder.AddAttachment("mixed-boundary-b", "application/octet-stream", new byte[] { 0 }, 2);
                    recorder.WriteMessage(1, 100, new byte[] { 100 });
                    recorder.WriteMessage(2, 200, new byte[128]);
                    recorder.AddAttachment("mixed-boundary-c", "application/octet-stream", new byte[] { 0 }, 3);
                    recorder.Close();
                }

                int firstChunkBytes;
                int secondChunkBytes;
                using (var inspectStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var inspectReader = new McapReader(inspectStream))
                {
                    var summary = inspectReader.ReadSummary();
                    Assert.Equal(3, summary.ChunkIndexes.Count);
                    var ordered = summary.ChunkIndexes
                        .OrderByDescending(index => index.MessageEndTime)
                        .ThenByDescending(index => index.MessageStartTime)
                        .ThenByDescending(index => index.ChunkStartOffset)
                        .ToArray();
                    firstChunkBytes = inspectReader.ReadChunkRecords(
                        ordered[0].ChunkStartOffset,
                        ordered[0].ChunkLength,
                        out _).Length;
                    secondChunkBytes = inspectReader.ReadChunkRecords(
                        ordered[1].ChunkStartOffset,
                        ordered[1].ChunkLength,
                        out _).Length;
                    Assert.True(firstChunkBytes > 0);
                    Assert.True(secondChunkBytes > 0);
                }

                using var engine = new McapReplayEngine
                {
                    MaxHistorySpoolBytes = firstChunkBytes
                };
                engine.Load(path);
                var result = engine.History(0, 200, new List<McapMessage>(), 2, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(new ulong[] { 90, 100 }, result.Select(message => message.LogTime).ToArray());
                Assert.Equal(new byte[] { 90 }, result[0].Data);
                Assert.Equal(new byte[] { 100 }, result[1].Data);
                Assert.Equal(3, metrics.CandidateCount);
                Assert.Equal(4, metrics.DecompressedChunkReads);
                Assert.Equal(2, metrics.CandidatePayloadCopies);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        [Fact]
        public void HistoryFiltersIrrelevantHighRateChannelsBeforeAdmission()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-filter-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 256, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-target", "json", "phase188.Target", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-noise", "json", "phase188.Noise", "jsonschema", "{}");
                    for (ulong time = 1; time <= 6_000; time++)
                        recorder.WriteMessage(2, time, new byte[] { (byte)time });
                    for (ulong time = 1; time <= 5; time++)
                        recorder.WriteMessage(1, 10_000 + time, new byte[] { (byte)time });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.History(0, 20_000, new List<McapMessage>(), 5, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(5, result.Count);
                Assert.All(result, message => Assert.Equal((ushort)1, message.ChannelId));
                Assert.True(metrics.FilteredRecords >= 6_000);
                Assert.Equal(5, metrics.CandidateCount);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void SnapshotUsesDescendingChunkOrderForLateSingleChannelQueries()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-late-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 96, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/late", "json", "phase188.Late", "jsonschema", "{}");
                    for (ulong time = 1; time <= 20; time++)
                        recorder.WriteMessage(1, time * 1000, new byte[] { (byte)time });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                using (var inspectStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var inspectReader = new McapReader(inspectStream))
                {
                    var inspectSummary = inspectReader.ReadSummary();
                    Assert.True(inspectSummary.ChunkIndexes.Count > 1, $"chunks={inspectSummary.ChunkIndexes.Count}");
                }
                var result = engine.Snapshot(20000, new List<McapMessage>());

                Assert.Single(result);
                Assert.Equal(20000UL, result[0].LogTime);
                Assert.True(
                    engine.LastSnapshotMetrics.DecompressedChunks < engine.LastSnapshotMetrics.EligibleChunks,
                    $"eligible={engine.LastSnapshotMetrics.EligibleChunks}; decompressed={engine.LastSnapshotMetrics.DecompressedChunks}");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

    }
}
