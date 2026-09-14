// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Performance
{
    internal static class ReplayPerformanceScenarios
    {
        internal static Phase188ReplayPerformanceResult Run(string outputDir, bool full, bool candidate = true)
        {
            var fixture = ReplayPerformanceFixtures.Ensure(outputDir, full);
            var warmup = full ? 10 : 3;
            var measured = full ? 50 : 15;
            var samples = new List<double>(measured);
            var resultBuffer = new List<McapMessage>();
            using var engine = candidate ? new McapReplayEngine() : null;
            if (candidate)
                engine.Load(fixture.Path);
            using var referenceStream = candidate ? null : new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var referenceReader = candidate ? null : new McapReader(referenceStream);
            var referenceSummary = candidate ? null : referenceReader.ReadSummary();
            var target = (ulong)((fixture.MessageCount - fixture.ChannelCount) * 1000);

            for (var i = 0; i < warmup; i++)
                RunQuery(candidate, engine, referenceReader, referenceSummary, target, resultBuffer);

            McapReplayEngine.SnapshotMetrics metrics = default;
            var stopwatch = new Stopwatch();
            for (var i = 0; i < measured; i++)
            {
                stopwatch.Restart();
                var queryMetrics = RunQuery(candidate, engine, referenceReader, referenceSummary, target, resultBuffer);
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                metrics = queryMetrics;
            }

            var result = new Phase188ReplayPerformanceResult
            {
                Scenario = full ? "latest-at-full" : "latest-at-quick",
                Implementation = candidate ? "optimized-indexed-latest-at" : "reference-linear-scan",
                FixtureHashSha256 = fixture.HashSha256,
                FixtureSeed = fixture.Seed,
                Runtime = Environment.Version.ToString(),
                BuildType = "dotnet",
                Compression = "none",
                CacheState = "snapshot-dictionary-reused;descending-index-cache-reused",
                CursorPosition = "late-target",
                WarmupIterations = warmup,
                MeasuredIterations = measured,
                P50Milliseconds = Phase188ReplayPerformanceMetrics.Percentile(samples, 50),
                P95Milliseconds = Phase188ReplayPerformanceMetrics.Percentile(samples, 95),
                P99Milliseconds = Phase188ReplayPerformanceMetrics.Percentile(samples, 99),
                MaxMilliseconds = Max(samples),
                EligibleChunks = metrics.EligibleChunks,
                SkippedChunks = metrics.SkippedChunks,
                DecompressedChunks = metrics.DecompressedChunks,
                HeadersScanned = metrics.HeadersScanned,
                CandidateUpdates = metrics.CandidateUpdates,
                PayloadCopies = metrics.PayloadCopies,
                PayloadBytesCopied = metrics.PayloadBytesCopied,
                ReturnedMessages = metrics.ReturnedMessages,
                QueryMilliseconds = Phase188ReplayPerformanceMetrics.Percentile(samples, 50)
            };
            Phase188ReplayPerformanceProtocol.Validate(result);
            return result;
        }

        private static McapReplayEngine.SnapshotMetrics RunQuery(
            bool candidate,
            McapReplayEngine engine,
            McapReader referenceReader,
            McapFileSummary referenceSummary,
            ulong target,
            List<McapMessage> result)
        {
            if (candidate)
            {
                engine.Snapshot(target, result);
                return engine.LastSnapshotMetrics;
            }

            result.Clear();
            var latest = new Dictionary<ushort, McapMessage>();
            long eligible = 0, skipped = 0, decompressed = 0, headers = 0;
            long updates = 0, copies = 0, bytes = 0;
            foreach (var index in referenceSummary.ChunkIndexes)
            {
                if (index.MessageStartTime > target)
                {
                    skipped++;
                    continue;
                }
                eligible++;
                var records = referenceReader.ReadChunkRecords(index.ChunkStartOffset, index.ChunkLength, out var crcValid);
                decompressed++;
                if (!crcValid)
                    continue;
                foreach (var message in referenceReader.ReadChunkMessages(records))
                {
                    headers++;
                    if (message.LogTime > target)
                        continue;
                    if (latest.TryGetValue(message.ChannelId, out var current)
                        && CompareCanonical(message, current) <= 0)
                        continue;
                    latest[message.ChannelId] = message;
                    updates++;
                    copies++;
                    bytes += message.Data?.Length ?? 0;
                }
            }
            result.AddRange(latest.Values);
            if (result.Count > 1)
                result.Sort(CompareCanonical);
            return new McapReplayEngine.SnapshotMetrics(eligible, skipped, decompressed, headers, updates, copies, bytes, result.Count);
        }

        private static int CompareCanonical(McapMessage left, McapMessage right)
        {
            var comparison = left.LogTime.CompareTo(right.LogTime);
            if (comparison != 0) return comparison;
            comparison = left.ChannelId.CompareTo(right.ChannelId);
            if (comparison != 0) return comparison;
            comparison = left.Sequence.CompareTo(right.Sequence);
            if (comparison != 0) return comparison;
            return left.PublishTime.CompareTo(right.PublishTime);
        }

        private static double Max(IReadOnlyList<double> values)
        {
            var max = double.MinValue;
            for (var i = 0; i < values.Count; i++)
                if (values[i] > max)
                    max = values[i];
            return max;
        }
    }
}
