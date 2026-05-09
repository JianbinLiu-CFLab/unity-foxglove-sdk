// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Performance
// Purpose: Per-scenario result DTO serialized to JSON.

namespace Unity.FoxgloveSDK.Performance
{
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
        public string notes { get; set; }
        public bool passed { get; set; }
    }
}
