// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;

namespace Unity.FoxgloveSDK.Performance.Tests
{
    public sealed class Phase188ReplayPerformanceProtocolTests
    {
        [Fact]
        public void PercentilesUseDeterministicNearestRankOnUnsortedSamples()
        {
            var samples = new[] { 9d, 1d, 5d, 3d, 7d };

            Assert.Equal(5d, Phase188ReplayPerformanceMetrics.Percentile(samples, 50));
            Assert.Equal(9d, Phase188ReplayPerformanceMetrics.Percentile(samples, 95));
            Assert.Equal(9d, Phase188ReplayPerformanceMetrics.Percentile(samples, 99));
        }

        [Fact]
        public void PercentileRejectsInvalidInputs()
        {
            Assert.Throws<ArgumentException>(() =>
                Phase188ReplayPerformanceMetrics.Percentile(Array.Empty<double>(), 50));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Phase188ReplayPerformanceMetrics.Percentile(new[] { 1d }, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Phase188ReplayPerformanceMetrics.Percentile(new[] { 1d }, 101));
        }

        [Fact]
        public void ResultSchemaSerializesStructuralCountersAndStageTimings()
        {
            var result = new Phase188ReplayPerformanceResult
            {
                Scenario = "latest-at-middle",
                WarmupIterations = 10,
                MeasuredIterations = 50,
                P50Milliseconds = 1.25,
                P95Milliseconds = 2.5,
                P99Milliseconds = 3.75,
                MaxMilliseconds = 4,
                EligibleChunks = 12,
                SkippedChunks = 8,
                DecompressedChunks = 4,
                HeadersScanned = 120,
                CandidateUpdates = 6,
                PayloadCopies = 3,
                PayloadBytesCopied = 12,
                ReturnedMessages = 3,
                QueryMilliseconds = 0.5,
                DecodeMilliseconds = 0.25,
                PrepareMilliseconds = 0.125,
                ApplyMilliseconds = 0.375
            };

            var json = JsonConvert.SerializeObject(result);

            Assert.Contains("\"eligibleChunks\":12", json);
            Assert.Contains("\"payloadCopies\":3", json);
            Assert.Contains("\"payloadBytesCopied\":12", json);
            Assert.Contains("\"returnedMessages\":3", json);
            Assert.Contains("\"p95Milliseconds\":2.5", json);
            Assert.Contains("\"applyMilliseconds\":0.375", json);
        }

        [Fact]
        public void ValidationRejectsNonFiniteAndInconsistentResults()
        {
            var result = new Phase188ReplayPerformanceResult
            {
                Scenario = "latest-at",
                FixtureHashSha256 = new string('A', 64),
                FixtureSeed = 188042,
                Runtime = "net10",
                BuildType = "dotnet",
                Compression = "none",
                CacheState = "cold",
                CursorPosition = "middle",
                WarmupIterations = 1,
                MeasuredIterations = 1,
                P50Milliseconds = 1,
                P95Milliseconds = 2,
                P99Milliseconds = 3,
                MaxMilliseconds = 4,
                PayloadCopies = 0,
                PayloadBytesCopied = 1
            };

            Assert.Throws<ArgumentException>(() => Phase188ReplayPerformanceProtocol.Validate(result));
            result.PayloadBytesCopied = 0;
            result.P99Milliseconds = double.NaN;
            Assert.Throws<ArgumentException>(() => Phase188ReplayPerformanceProtocol.Validate(result));
            result.P99Milliseconds = 3;
            result.ApplyMilliseconds = -1;
            Assert.Throws<ArgumentException>(() => Phase188ReplayPerformanceProtocol.Validate(result));
        }
    }
}
