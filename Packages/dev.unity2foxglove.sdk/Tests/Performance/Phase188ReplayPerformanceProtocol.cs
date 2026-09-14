// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Unity.FoxgloveSDK.Performance
{
    /// <summary>Fail-closed validation for machine-readable Phase188 results.</summary>
    public static class Phase188ReplayPerformanceProtocol
    {
        public static void Validate(Phase188ReplayPerformanceResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(result.Scenario)
                || string.IsNullOrWhiteSpace(result.FixtureHashSha256)
                || result.FixtureHashSha256.Length != 64
                || !IsUpperHex(result.FixtureHashSha256))
                throw new ArgumentException("Scenario and uppercase fixture SHA-256 are required.", nameof(result));
            if (result.FixtureSeed <= 0 || result.WarmupIterations < 0 || result.MeasuredIterations <= 0)
                throw new ArgumentException("Fixture seed and iteration counts are invalid.", nameof(result));
            if (!FiniteOrdered(result.P50Milliseconds, result.P95Milliseconds, result.P99Milliseconds, result.MaxMilliseconds))
                throw new ArgumentException("Latency summaries must be finite and ordered.", nameof(result));
            if (result.EligibleChunks < 0 || result.SkippedChunks < 0 || result.DecompressedChunks < 0
                || result.HeadersScanned < 0 || result.CandidateUpdates < 0 || result.PayloadCopies < 0
                || result.PayloadBytesCopied < 0 || result.ReturnedMessages < 0)
                throw new ArgumentException("Structural counters must be non-negative.", nameof(result));
            if (result.PayloadCopies == 0 && result.PayloadBytesCopied != 0)
                throw new ArgumentException("Zero payload copies require zero copied bytes.", nameof(result));
            if (result.ReturnedMessages > result.PayloadCopies)
                throw new ArgumentException("Returned messages cannot exceed payload copies.", nameof(result));
            if (string.IsNullOrWhiteSpace(result.Runtime) || string.IsNullOrWhiteSpace(result.BuildType)
                || string.IsNullOrWhiteSpace(result.Compression) || string.IsNullOrWhiteSpace(result.CacheState)
                || string.IsNullOrWhiteSpace(result.CursorPosition))
                throw new ArgumentException("Runtime, build, compression, cache, and cursor fields are required.", nameof(result));
        }

        private static bool IsUpperHex(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static bool FiniteOrdered(double p50, double p95, double p99, double max)
        {
            return !double.IsNaN(p50) && !double.IsInfinity(p50)
                && !double.IsNaN(p95) && !double.IsInfinity(p95)
                && !double.IsNaN(p99) && !double.IsInfinity(p99)
                && !double.IsNaN(max) && !double.IsInfinity(max)
                && p50 >= 0 && p50 <= p95 && p95 <= p99 && p99 <= max;
        }
    }
}
