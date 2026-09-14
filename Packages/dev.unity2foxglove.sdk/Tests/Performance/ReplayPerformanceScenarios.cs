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
        internal static Phase188ReplayPerformanceResult Run(string outputDir, bool full)
        {
            var fixture = ReplayPerformanceFixtures.Ensure(outputDir, full);
            var warmup = full ? 10 : 3;
            var measured = full ? 50 : 15;
            var samples = new List<double>(measured);
            var resultBuffer = new List<McapMessage>();
            using var engine = new McapReplayEngine();
            engine.Load(fixture.Path);
            var target = (ulong)((fixture.MessageCount - fixture.ChannelCount) * 1000);

            for (var i = 0; i < warmup; i++)
                engine.Snapshot(target, resultBuffer);

            McapReplayEngine.SnapshotMetrics metrics = default;
            var stopwatch = new Stopwatch();
            for (var i = 0; i < measured; i++)
            {
                stopwatch.Restart();
                engine.Snapshot(target, resultBuffer);
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                metrics = engine.LastSnapshotMetrics;
            }

            return new Phase188ReplayPerformanceResult
            {
                Scenario = full ? "latest-at-full" : "latest-at-quick",
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
