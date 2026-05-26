// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Performance
// Purpose: Per-scenario result DTO serialized to JSON.

namespace Unity.FoxgloveSDK.Performance
{
    public sealed class PerformanceThresholdConfig
    {
        public bool enabled { get; set; } = true;
        public PerformanceScenarioThreshold defaults { get; set; }
        public System.Collections.Generic.Dictionary<string, PerformanceScenarioThreshold> scenarios { get; set; }
    }

    public sealed class PerformanceScenarioThreshold
    {
        public long? maxElapsedMs { get; set; }
        public double? minMessagesPerSecond { get; set; }
        public double? maxAllocatedBytesPerMessage { get; set; }
        public long? maxAllocatedBytesTotal { get; set; }
        public int? maxGen0Collections { get; set; }
        public int? maxGen1Collections { get; set; }
        public int? maxGen2Collections { get; set; }
    }

    public sealed class PerformanceScenarioResult
    {
        public string name { get; set; }
        public int warmupMessageCount { get; set; }
        public int messageCount { get; set; }
        public long elapsedMs { get; set; }
        public double messagesPerSecond { get; set; }
        public long allocatedBytesTotal { get; set; }
        public long allocatedBytesCurrentThread { get; set; }
        public double allocatedBytesPerMessage { get; set; }
        public int gen0Collections { get; set; }
        public int gen1Collections { get; set; }
        public int gen2Collections { get; set; }
        public string allocationNotes { get; set; }
        public long outputBytes { get; set; }
        public string fixtureKind { get; set; }
        public string fixturePath { get; set; }
        public int channelCount { get; set; }
        public int schemaCount { get; set; }
        public int selectedChannelCount { get; set; }
        public int selectedTopicCount { get; set; }
        public ulong? queryStartTimeNs { get; set; }
        public ulong? queryEndTimeNs { get; set; }
        public int returnedMessageCount { get; set; }
        public int backfillHitCount { get; set; }
        public long fixtureBytes { get; set; }
        public bool thresholdsEvaluated { get; set; }
        public string thresholdNotes { get; set; }
        public string notes { get; set; }
        public bool passed { get; set; }
    }
}
