// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Performance
{
    /// <summary>
    /// Machine-readable Phase188 replay result. Structural counters are kept
    /// beside timings so a faster but vacuous benchmark cannot pass.
    /// </summary>
    public sealed class Phase188ReplayPerformanceResult
    {
        [JsonProperty("scenario")] public string Scenario { get; set; }
        [JsonProperty("warmupIterations")] public int WarmupIterations { get; set; }
        [JsonProperty("measuredIterations")] public int MeasuredIterations { get; set; }
        [JsonProperty("p50Milliseconds")] public double P50Milliseconds { get; set; }
        [JsonProperty("p95Milliseconds")] public double P95Milliseconds { get; set; }
        [JsonProperty("p99Milliseconds")] public double P99Milliseconds { get; set; }
        [JsonProperty("maxMilliseconds")] public double MaxMilliseconds { get; set; }
        [JsonProperty("eligibleChunks")] public long EligibleChunks { get; set; }
        [JsonProperty("skippedChunks")] public long SkippedChunks { get; set; }
        [JsonProperty("decompressedChunks")] public long DecompressedChunks { get; set; }
        [JsonProperty("headersScanned")] public long HeadersScanned { get; set; }
        [JsonProperty("candidateUpdates")] public long CandidateUpdates { get; set; }
        [JsonProperty("payloadCopies")] public long PayloadCopies { get; set; }
        [JsonProperty("payloadBytesCopied")] public long PayloadBytesCopied { get; set; }
        [JsonProperty("returnedMessages")] public long ReturnedMessages { get; set; }
        [JsonProperty("queryMilliseconds")] public double QueryMilliseconds { get; set; }
        [JsonProperty("decodeMilliseconds")] public double DecodeMilliseconds { get; set; }
        [JsonProperty("prepareMilliseconds")] public double PrepareMilliseconds { get; set; }
        [JsonProperty("applyMilliseconds")] public double ApplyMilliseconds { get; set; }
    }
}
