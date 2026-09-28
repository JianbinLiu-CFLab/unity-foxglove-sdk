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
    public sealed class McapReplayPerformanceMetricsTests
    {
        [Fact]
        public void SnapshotReportsStructuralCountersAndOwnedCopyCount()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-metrics-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(
                    stream,
                    null,
                    new McapWriterOptions
                    {
                        UseChunking = true,
                        ChunkSizeBytes = 128,
                        IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/metrics", "json", "phase188.Metrics", "jsonschema", "{}");
                    for (ulong time = 10; time <= 100; time += 10)
                        recorder.WriteMessage(1, time, new byte[] { (byte)time });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine
                {
                    CrcMismatchPolicy = McapReplayEngine.CorruptChunkPolicy.UseWithWarning
                };
                engine.Load(path);
                var result = engine.Snapshot(50, new List<McapMessage>());
                var metrics = engine.LastSnapshotMetrics;

                Assert.Single(result);
                Assert.Equal(50UL, result[0].LogTime);
                Assert.True(metrics.EligibleChunks > 0);
                Assert.True(metrics.DecompressedChunks > 0);
                Assert.True(metrics.HeadersScanned >= 1);
                Assert.True(metrics.CandidateUpdates >= 1);
                Assert.True(metrics.PayloadCopies >= metrics.ReturnedMessages);
                Assert.True(metrics.PayloadBytesCopied >= metrics.ReturnedMessages);
                Assert.Equal(1, metrics.ReturnedMessages);
                Assert.True(
                    metrics.CandidateUpdates > metrics.PayloadCopies,
                    $"snapshot candidate updates should exceed final payload copies; updates={metrics.CandidateUpdates}; copies={metrics.PayloadCopies}");
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void HistoryMaterializesOnlyBoundedFinalCandidates()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-metrics-" + Guid.NewGuid().ToString("N") + ".mcap");
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
                        IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                    },
                    leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-metrics", "json", "phase188.History", "jsonschema", "{}");
                    for (ulong time = 1; time <= 200; time++)
                        recorder.WriteMessage(1, time, new byte[] { (byte)time });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                Assert.True(engine.Summary.ChunkIndexes.Count > 1);
                var result = engine.History(1, 200, new List<McapMessage>(), 10, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(10, result.Count);
                Assert.Equal(200, metrics.CandidateCount);
                Assert.Equal(10, metrics.PayloadCopies);
                Assert.True(metrics.FilteredRecords == 0);
                Assert.True(metrics.MaxObservedDecompressedChunkBytes > 0);
                var maxChunkBytes = engine.Summary.ChunkIndexes
                    .Select(index => checked((long)index.UncompressedSize))
                    .Max();
                Assert.True(
                    metrics.MaxObservedDecompressedChunkBytes <= maxChunkBytes,
                    $"history observed more than one decompressed chunk: peak={metrics.MaxObservedDecompressedChunkBytes}; maxChunk={maxChunkBytes}");
                Assert.Equal(1, metrics.PeakDecompressedChunkCount);
                Assert.Equal(10, metrics.CandidatePayloadCopies);
                Assert.True(
                    metrics.PeakRetainedDecompressedBytes <= maxChunkBytes,
                    $"history retained more than one decompressed chunk: peak={metrics.PeakRetainedDecompressedBytes}; maxChunk={maxChunkBytes}");
                var unbounded = engine.History(1, 200, new List<McapMessage>(), 0, new HashSet<ushort> { 1 });
                Assert.Equal(200, unbounded.Count);
                Assert.Equal(1, engine.LastHistoryMetrics.PeakDecompressedChunkCount);
                var replaySource = File.ReadAllText(
                    RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs"));
                Assert.DoesNotContain("Dictionary<int, byte[]> payloadChunks", replaySource, StringComparison.Ordinal);
                Assert.DoesNotContain("payloadChunks = new Dictionary", replaySource, StringComparison.Ordinal);
                Assert.DoesNotContain("retainedCandidateChunks", replaySource, StringComparison.Ordinal);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void BoundedHistoryCopiesOnlyFinalCandidatesAcrossOverlappingChunks()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-final-candidates-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 4096, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-final", "json", "phase188.HistoryFinal", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-boundary", "json", "phase188.HistoryBoundary", "jsonschema", "{}");
                    recorder.WriteMessage(1, 10, new byte[] { 10 });
                    recorder.WriteMessage(2, 100, new byte[] { 100 });
                    recorder.AddAttachment("boundary", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(1, 80, new byte[] { 80 });
                    recorder.WriteMessage(2, 90, new byte[] { 90 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.History(0, 100, new List<McapMessage>(), 1, new HashSet<ushort> { 1 });

                Assert.Equal(80UL, Assert.Single(result).LogTime);
                Assert.Equal(1, engine.LastHistoryMetrics.CandidatePayloadCopies);
                Assert.Equal(2, engine.LastHistoryMetrics.CandidateCount);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void BoundedHistoryDoesNotMaterializeTransientCandidatesAcrossOverlappingFilteredChunks()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-transient-filtered-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 4096, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-transient-target", "json", "phase188.HistoryTransientTarget", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-transient-noise", "json", "phase188.HistoryTransientNoise", "jsonschema", "{}");
                    recorder.WriteMessage(1, 1, new byte[] { 1 });
                    recorder.WriteMessage(1, 100, new byte[] { 100 });
                    recorder.AddAttachment("boundary-a", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(1, 2, new byte[] { 2 });
                    recorder.WriteMessage(2, 99, new byte[] { 99 });
                    recorder.AddAttachment("boundary-b", "application/octet-stream", new byte[] { 0 }, 2);
                    recorder.WriteMessage(1, 3, new byte[] { 3 });
                    recorder.WriteMessage(2, 98, new byte[] { 98 });
                    recorder.AddAttachment("boundary-c", "application/octet-stream", new byte[] { 0 }, 3);
                    recorder.WriteMessage(1, 4, new byte[] { 4 });
                    recorder.WriteMessage(2, 97, new byte[] { 97 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.History(0, 100, new List<McapMessage>(), 2, new HashSet<ushort> { 1 });
                var metrics = engine.LastHistoryMetrics;

                Assert.Equal(new ulong[] { 4, 100 }, result.Select(message => message.LogTime).ToArray());
                Assert.Equal(2, metrics.CandidatePayloadCopies);
                Assert.Equal(2, metrics.PayloadCopies);
                Assert.Equal(5, metrics.CandidateCount);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void BoundedHistoryKeepsMessagesAcrossOverlappingChunks()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-overlap-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 4096, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-overlap", "json", "phase188.HistoryOverlap", "jsonschema", "{}");
                    recorder.WriteMessage(1, 0, new byte[] { 0 });
                    recorder.WriteMessage(1, 80, new byte[] { 80 });
                    recorder.WriteMessage(1, 100, new byte[] { 100 });
                    recorder.AddAttachment("boundary", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(1, 50, new byte[] { 50 });
                    recorder.WriteMessage(1, 60, new byte[] { 60 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.History(70, 100, new List<McapMessage>(), 2, new HashSet<ushort> { 1 });

                Assert.Equal(new ulong[] { 80, 100 }, result.Select(message => message.LogTime).ToArray());
                Assert.Equal(2, engine.LastHistoryMetrics.CandidatePayloadCopies);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void BoundedHistoryDoesNotDecompressUncompressedOverlapSurvivorsTwice()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-uncompressed-overlap-read-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 4096, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-uncompressed-overlap-read", "json", "phase188.HistoryUncompressedOverlapRead", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-uncompressed-overlap-noise", "json", "phase188.HistoryUncompressedOverlapNoise", "jsonschema", "{}");
                    recorder.WriteMessage(1, 80, new byte[] { 80 });
                    recorder.WriteMessage(2, 100, new byte[] { 100 });
                    recorder.AddAttachment("boundary-a", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(1, 70, new byte[] { 70 });
                    recorder.WriteMessage(2, 90, new byte[] { 90 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = engine.History(0, 100, new List<McapMessage>(), 1, new HashSet<ushort> { 1 });

                Assert.Equal(80UL, Assert.Single(result).LogTime);
                Assert.Equal(1, engine.LastHistoryMetrics.DecompressedChunkReads);
                Assert.Equal(1, engine.LastHistoryMetrics.CandidatePayloadCopies);
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
        public void BoundedHistoryDoesNotDecompressCompressedOverlapSurvivorsTwice(string compression)
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-history-compressed-overlap-read-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 4096, compression: compression, leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/history-compressed-overlap-read", "json", "phase188.HistoryCompressedOverlapRead", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/history-compressed-overlap-noise", "json", "phase188.HistoryCompressedOverlapNoise", "jsonschema", "{}");
                    recorder.WriteMessage(1, 80, new byte[] { 80 });
                    recorder.WriteMessage(2, 100, new byte[] { 100 });
                    recorder.AddAttachment("boundary-a", "application/octet-stream", new byte[] { 0 }, 1);
                    recorder.WriteMessage(1, 70, new byte[] { 70 });
                    recorder.WriteMessage(2, 90, new byte[] { 90 });
                    recorder.Close();
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                Assert.Equal(2, engine.Summary.ChunkIndexes.Count);
                var result = engine.History(0, 100, new List<McapMessage>(), 1, new HashSet<ushort> { 1 });

                Assert.Equal(80UL, Assert.Single(result).LogTime);
                Assert.Equal(1, engine.LastHistoryMetrics.DecompressedChunkReads);
                Assert.Equal(1, engine.LastHistoryMetrics.CandidatePayloadCopies);
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

        [Fact]
        public void SnapshotMatchesIndependentLinearCanonicalOracleAcrossChunks()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-oracle-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 96, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/oracle/a", "json", "phase188.A", "jsonschema", "{}");
                    recorder.AddChannel(2, "/phase188/oracle/b", "json", "phase188.B", "jsonschema", "{}");
                    for (ulong time = 1; time <= 20; time++)
                    {
                        var channel = (uint)(time % 2) + 1;
                        recorder.WriteMessage(channel, time * 1000, new byte[] { (byte)time });
                    }
                    recorder.Close();
                }

                var expected = new Dictionary<ushort, McapMessage>();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new McapReader(stream))
                {
                    var summary = reader.ReadSummary();
                    foreach (var index in summary.ChunkIndexes)
                    {
                        var records = reader.ReadChunkRecords(index.ChunkStartOffset, index.ChunkLength, out _);
                        foreach (var message in reader.ReadChunkMessages(records))
                        {
                            if (message.LogTime > 20000)
                                continue;
                            if (!expected.TryGetValue(message.ChannelId, out var current)
                                || CompareCanonical(message, current) > 0)
                                expected[message.ChannelId] = message;
                        }
                    }
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var actual = engine.Snapshot(20000, new List<McapMessage>());

                Assert.Equal(expected.Count, actual.Count);
                foreach (var message in actual)
                {
                    Assert.True(expected.TryGetValue(message.ChannelId, out var oracle));
                    Assert.Equal(oracle.LogTime, message.LogTime);
                    Assert.Equal(oracle.Sequence, message.Sequence);
                    Assert.Equal(oracle.PublishTime, message.PublishTime);
                    Assert.Equal(oracle.Data, message.Data);
                }
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void SnapshotDoesNotLetUndeclaredNewestChannelHideDeclaredWinner()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase188-unknown-channel-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
                using (var recorder = new McapRecorder(stream, null, chunkSizeBytes: 64, compression: "", leaveOpen: true))
                {
                    recorder.AddChannel(1, "/phase188/declared", "json", "phase188.Declared", "jsonschema", "{}");
                    for (ulong time = 1; time <= 12; time++)
                        recorder.WriteMessage(1, time * 1000, new byte[] { (byte)time });
                    recorder.Close();
                }

                // Corrupt only the newest chunk's message channel IDs. The
                // stale chunk CRC intentionally exercises the existing
                // UseWithWarning policy while preserving the summary's one
                // declared channel.
                byte[] bytes = File.ReadAllBytes(path);
                McapChunkIndex newest;
                byte[] newestRecords;
                using (var inspectStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var inspectReader = new McapReader(inspectStream))
                {
                    var summary = inspectReader.ReadSummary();
                    newest = summary.ChunkIndexes.OrderByDescending(index => index.MessageEndTime).First();
                    newestRecords = inspectReader.ReadChunkRecords(
                        newest.ChunkStartOffset, newest.ChunkLength, out _);
                }
                var records = FindSubsequence(bytes, newestRecords);
                Assert.True(records >= 0, "newest chunk records were not found in the file");
                var end = records + newestRecords.Length;
                var mutated = 0;
                for (var offset = records; offset + 9 <= end;)
                {
                    var opcode = bytes[offset];
                    var length = checked((int)BitConverter.ToUInt64(bytes, offset + 1));
                    if (opcode == 0x05 && length >= 2)
                    {
                        bytes[offset + 9] = 0xE7;
                        bytes[offset + 10] = 0x03; // 999, not a declared ID
                        mutated++;
                    }
                    offset += 9 + length;
                }
                Assert.True(mutated > 0, $"newest chunk contained no message records (records={records}, end={end})");
                File.WriteAllBytes(path, bytes);

                using var engine = new McapReplayEngine
                {
                    CrcMismatchPolicy = McapReplayEngine.CorruptChunkPolicy.UseWithWarning
                };
                engine.Load(path);
                var result = engine.Snapshot(engine.EndTimeNs, new List<McapMessage>());

                Assert.Single(result);
                Assert.Equal((ushort)1, result[0].ChannelId);
                Assert.Equal(11000UL, result[0].LogTime);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static int CompareCanonical(McapMessage left, McapMessage right)
        {
            var comparison = left.LogTime.CompareTo(right.LogTime);
            if (comparison != 0)
                return comparison;
            comparison = left.Sequence.CompareTo(right.Sequence);
            if (comparison != 0)
                return comparison;
            return left.PublishTime.CompareTo(right.PublishTime);
        }

        private static int FindSubsequence(byte[] haystack, byte[] needle)
        {
            for (var start = 0; start <= haystack.Length - needle.Length; start++)
            {
                var match = true;
                for (var i = 0; i < needle.Length; i++)
                {
                    if (haystack[start + i] != needle[i])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return start;
            }
            return -1;
        }

        private static string RepoPath(string relativePath)
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ".git"))
                    || Directory.Exists(Path.Combine(directory.FullName, ".git")))
                    return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
