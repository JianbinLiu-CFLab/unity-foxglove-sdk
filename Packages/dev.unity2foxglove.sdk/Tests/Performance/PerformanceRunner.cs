// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Performance
// Purpose: Performance scenario runner with warmup, timing, and GC metrics.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Foxglove;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Util;
using static Unity.FoxgloveSDK.Util.CameraBackpressurePolicy;

namespace Unity.FoxgloveSDK.Performance
{
    public static partial class PerformanceRunner
    {
        public const string DefaultTransportScope = "FakePerformanceTransport serialization/dispatch path only; excludes ManagedWsBackend sockets and TLS";

        internal readonly struct AllocationMetricSample
        {
            public readonly long AllocatedBytesTotal;
            public readonly long AllocatedBytesCurrentThread;
            public readonly double AllocatedBytesPerMessage;
            public readonly int Gen0Collections;
            public readonly int Gen1Collections;
            public readonly int Gen2Collections;
            public readonly string AllocationNotes;

            public AllocationMetricSample(
                long allocatedBytesTotal,
                long allocatedBytesCurrentThread,
                double allocatedBytesPerMessage,
                int gen0Collections,
                int gen1Collections,
                int gen2Collections,
                string allocationNotes)
            {
                AllocatedBytesTotal = allocatedBytesTotal;
                AllocatedBytesCurrentThread = allocatedBytesCurrentThread;
                AllocatedBytesPerMessage = allocatedBytesPerMessage;
                Gen0Collections = gen0Collections;
                Gen1Collections = gen1Collections;
                Gen2Collections = gen2Collections;
                AllocationNotes = allocationNotes;
            }
        }

        // The production path always uses the runtime collector. The internal
        // override makes metric transfer tests deterministic without replacing
        // the scenario body or treating a naturally observed zero as evidence.
        internal static Func<int, AllocationMetricSample> AllocationMetricsOverrideForTests { get; set; }
        internal static Func<long, long> ElapsedMillisecondsOverrideForTests { get; set; }

        private static string RepoRoot
        {
            get
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var candidate = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
                var sentinel = Path.Combine(candidate, "Packages", "dev.unity2foxglove.sdk", "package.json");
                if (!File.Exists(sentinel))
                    throw new InvalidOperationException($"RepoRoot resolution failed: {candidate}");
                return candidate;
            }
        }
        private const int QuickWarmup = 500;
        private const int FullWarmup = 5000;
        private const int QuickTopics = 10;
        private const int FullTopics = 50;
        private const int QuickClients = 3;
        private const int FullClients = 5;
        private const int QuickMessages = 2000;
        private const int FullMessages = 50000;

        public static PerformanceThresholdConfig CreateDefaultThresholds(string mode)
        {
            var isFull = mode == "full";
            return new PerformanceThresholdConfig
            {
                enabled = true,
                transportScope = DefaultTransportScope,
                calibratedOn = "Built-in conservative defaults; review thresholds when runner class or workload changes.",
                defaults = new PerformanceScenarioThreshold
                {
                    maxElapsedMs = isFull ? 600000 : 120000,
                    minMessagesPerSecond = 1,
                    maxAllocatedBytesPerMessage = 64 * 1024 * 1024,
                    maxGen0Collections = isFull ? 10000 : 1000,
                    maxGen1Collections = isFull ? 1000 : 100,
                    maxGen2Collections = isFull ? 50 : 10
                },
                scenarios = new Dictionary<string, PerformanceScenarioThreshold>
                {
                    ["CameraBackpressurePolicyMicro"] = new PerformanceScenarioThreshold
                    {
                        maxElapsedMs = isFull ? 30000 : 10000,
                        minMessagesPerSecond = 10000,
                        maxAllocatedBytesPerMessage = 64,
                        maxGen0Collections = 0,
                        maxGen1Collections = 0,
                        maxGen2Collections = 0
                    },
                    ["McapRecordAttachmentSummary"] = new PerformanceScenarioThreshold
                    {
                        maxElapsedMs = 10000,
                        minMessagesPerSecond = 0,
                        maxAllocatedBytesPerMessage = 64 * 1024 * 1024,
                        maxGen2Collections = 0
                    }
                }
            };
        }

        public static bool RunThresholdSelfTest()
        {
            var failingThresholds = new PerformanceThresholdConfig
            {
                enabled = true,
                defaults = new PerformanceScenarioThreshold
                {
                    maxElapsedMs = 1,
                    minMessagesPerSecond = 1000,
                    maxAllocatedBytesPerMessage = 1,
                    maxGen0Collections = 0
                }
            };

            var failResult = new PerformanceScenarioResult
            {
                name = "ThresholdSelfTest",
                messageCount = 1,
                elapsedMs = 10,
                messagesPerSecond = 10,
                allocatedBytesPerMessage = 2,
                gen0Collections = 1,
                passed = true
            };

            ApplyThresholds(failResult, failingThresholds);
            var failPathOk = failResult.thresholdsEvaluated
                && !failResult.passed
                && failResult.thresholdNotes.Contains("elapsedMs", StringComparison.Ordinal)
                && failResult.thresholdNotes.Contains("messagesPerSecond", StringComparison.Ordinal)
                && failResult.thresholdNotes.Contains("allocatedBytesPerMessage", StringComparison.Ordinal)
                && failResult.thresholdNotes.Contains("gen0Collections", StringComparison.Ordinal);

            var passResult = new PerformanceScenarioResult
            {
                name = "ThresholdSelfTest",
                messageCount = 1,
                elapsedMs = 0,
                messagesPerSecond = 1000,
                allocatedBytesPerMessage = 0,
                gen0Collections = 0,
                passed = true
            };

            ApplyThresholds(passResult, failingThresholds);
            var passPathOk = passResult.thresholdsEvaluated
                && passResult.passed
                && string.Equals(passResult.thresholdNotes, "thresholds passed", StringComparison.Ordinal);

            var disabledBase = new PerformanceThresholdConfig
            {
                enabled = false,
                modes = new Dictionary<string, PerformanceThresholdConfig>
                {
                    ["full"] = new PerformanceThresholdConfig { enabled = true }
                }
            };
            var globalDisableWins = !ResolveThresholdConfigForMode(disabledBase, "full").enabled;

            return failPathOk && passPathOk && globalDisableWins;
        }

        public static PerformanceThresholdConfig ResolveThresholdConfigForMode(
            PerformanceThresholdConfig config,
            string mode)
        {
            config ??= CreateDefaultThresholds(mode);
            var resolved = CloneThresholdConfig(config);
            if (!string.IsNullOrWhiteSpace(mode)
                && config.modes != null
                && config.modes.TryGetValue(mode, out var modeConfig)
                && modeConfig != null)
            {
                if (modeConfig.defaults != null)
                    resolved.defaults = MergeThresholds(resolved.defaults, modeConfig.defaults);

                if (modeConfig.scenarios != null)
                {
                    resolved.scenarios ??= new Dictionary<string, PerformanceScenarioThreshold>();
                    foreach (var pair in modeConfig.scenarios)
                    {
                        resolved.scenarios.TryGetValue(pair.Key, out var baseScenario);
                        resolved.scenarios[pair.Key] = MergeThresholds(baseScenario, pair.Value);
                    }
                }

                // The base enabled flag is the global kill switch; a mode can disable thresholds
                // for itself, but it cannot re-enable thresholds disabled at the top level.
                resolved.enabled = modeConfig.enabled && config.enabled;
                resolved.transportScope = string.IsNullOrWhiteSpace(modeConfig.transportScope)
                    ? resolved.transportScope
                    : modeConfig.transportScope;
                resolved.calibratedOn = string.IsNullOrWhiteSpace(modeConfig.calibratedOn)
                    ? resolved.calibratedOn
                    : modeConfig.calibratedOn;
            }

            resolved.modes = null;
            return resolved;
        }

        private static PerformanceThresholdConfig CloneThresholdConfig(PerformanceThresholdConfig source)
        {
            return new PerformanceThresholdConfig
            {
                enabled = source?.enabled ?? true,
                transportScope = source?.transportScope ?? DefaultTransportScope,
                calibratedOn = source?.calibratedOn,
                defaults = CloneThreshold(source?.defaults),
                scenarios = CloneThresholdMap(source?.scenarios)
            };
        }

        private static Dictionary<string, PerformanceScenarioThreshold> CloneThresholdMap(
            Dictionary<string, PerformanceScenarioThreshold> source)
        {
            if (source == null)
                return null;

            var clone = new Dictionary<string, PerformanceScenarioThreshold>();
            foreach (var pair in source)
                clone[pair.Key] = CloneThreshold(pair.Value);
            return clone;
        }

        private static PerformanceScenarioThreshold CloneThreshold(PerformanceScenarioThreshold source)
            => source == null ? null : MergeThresholds(null, source);

        private static PerformanceScenarioThreshold MergeThresholds(
            PerformanceScenarioThreshold baseThreshold,
            PerformanceScenarioThreshold overrideThreshold)
        {
            if (baseThreshold == null && overrideThreshold == null)
                return null;

            return new PerformanceScenarioThreshold
            {
                maxElapsedMs = overrideThreshold?.maxElapsedMs ?? baseThreshold?.maxElapsedMs,
                minMessagesPerSecond = overrideThreshold?.minMessagesPerSecond ?? baseThreshold?.minMessagesPerSecond,
                maxAllocatedBytesPerMessage = overrideThreshold?.maxAllocatedBytesPerMessage ?? baseThreshold?.maxAllocatedBytesPerMessage,
                maxAllocatedBytesTotal = overrideThreshold?.maxAllocatedBytesTotal ?? baseThreshold?.maxAllocatedBytesTotal,
                maxGen0Collections = overrideThreshold?.maxGen0Collections ?? baseThreshold?.maxGen0Collections,
                maxGen1Collections = overrideThreshold?.maxGen1Collections ?? baseThreshold?.maxGen1Collections,
                maxGen2Collections = overrideThreshold?.maxGen2Collections ?? baseThreshold?.maxGen2Collections
            };
        }

        public static List<PerformanceScenarioResult> RunAll(string mode, PerformanceThresholdConfig thresholds = null)
        {
            bool isFull = mode == "full";
            var results = new List<PerformanceScenarioResult>();

            int warmup = isFull ? FullWarmup : QuickWarmup;
            int topics = isFull ? FullTopics : QuickTopics;
            int clients = isFull ? FullClients : QuickClients;
            int messages = isFull ? FullMessages : QuickMessages;
            var activeThresholds = ResolveThresholdConfigForMode(thresholds ?? CreateDefaultThresholds(mode), mode);

            results.Add(RunPublishJsonFanout(warmup, topics, clients, messages, activeThresholds));
            results.Add(RunPublishJsonFanoutPrebuiltPayload(warmup, topics, clients, messages, activeThresholds));
            results.Add(RunPublishProtoFanout(warmup, topics, clients, messages, activeThresholds));
            results.Add(RunMcapRecord(warmup, topics, messages, "", "McapRecordNone", activeThresholds));
            results.Add(RunMcapRecord(warmup, topics, messages, "lz4", "McapRecordLz4", activeThresholds));
            results.Add(RunMcapRecord(warmup, topics, messages, "zstd", "McapRecordZstd", activeThresholds));
            results.Add(RunMcapRecordNonePrebuiltPayload(warmup, topics, messages, activeThresholds));
            results.Add(RunMcapReplayTick(warmup, topics, messages, isFull, activeThresholds));
            var dataLoaderMessages = isFull ? Math.Min(messages, 10000) : Math.Min(messages, 1000);
            var dataLoaderInitializeIterations = isFull ? 20 : 5;
            var indexedFixture = CreateDataLoaderIndexedFixture(topics, dataLoaderMessages);
            var directFixture = CreateDataLoaderDirectFixture(topics, dataLoaderMessages);
            var sparseFixture = CreateDataLoaderSparseFixture();
            results.Add(RunMcapDataLoaderInitializeIndexed(indexedFixture, dataLoaderInitializeIterations, activeThresholds));
            results.Add(RunMcapDataLoaderInitializeDirect(directFixture, dataLoaderInitializeIterations, activeThresholds));
            results.Add(RunMcapDataLoaderIterateAllIndexed(indexedFixture, activeThresholds));
            results.Add(RunMcapDataLoaderIterateTopicFilterIndexed(indexedFixture, activeThresholds));
            results.Add(RunMcapDataLoaderIterateTimeWindowIndexed(indexedFixture, activeThresholds));
            results.Add(RunMcapDataLoaderIterateAllDirect(directFixture, activeThresholds));
            results.Add(RunMcapDataLoaderBackfillIndexed(indexedFixture, activeThresholds));
            results.Add(RunMcapDataLoaderBackfillSparse(sparseFixture, activeThresholds));
            results.Add(RunMcapRecordAttachmentSummary(activeThresholds));
            results.Add(RunCameraBackpressurePolicyMicro(activeThresholds));
            results.Add(RunTransportQueueMicro(activeThresholds));

            return results;
        }

        // Helpers

        private static void PrepareAllocMeasurement(out long gcBeforeTotal, out long gcBeforeThread,
            out int gen0Before, out int gen1Before, out int gen2Before)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            gcBeforeTotal = GC.GetTotalAllocatedBytes(false);
            gcBeforeThread = GC.GetAllocatedBytesForCurrentThread();
            gen0Before = GC.CollectionCount(0);
            gen1Before = GC.CollectionCount(1);
            gen2Before = GC.CollectionCount(2);
        }

        private static void CollectAllocMetrics(long gcBeforeTotal, long gcBeforeThread,
            int gen0Before, int gen1Before, int gen2Before,
            int messageCount, out long allocTotal, out long allocThread, out double allocPerMsg,
            out int gen0, out int gen1, out int gen2, out string notes)
        {
            var gcAfterTotal = GC.GetTotalAllocatedBytes(false);
            var gcAfterThread = GC.GetAllocatedBytesForCurrentThread();
            allocTotal = gcAfterTotal - gcBeforeTotal;
            allocThread = gcAfterThread - gcBeforeThread;
            allocPerMsg = messageCount > 0 ? (double)allocTotal / messageCount : 0;
            gen0 = GC.CollectionCount(0) - gen0Before;
            gen1 = GC.CollectionCount(1) - gen1Before;
            gen2 = GC.CollectionCount(2) - gen2Before;
            notes = null;
        }

        private static AllocationMetricSample CollectAllocMetricSample(
            long gcBeforeTotal,
            long gcBeforeThread,
            int gen0Before,
            int gen1Before,
            int gen2Before,
            int messageCount)
        {
            var overrideCollector = AllocationMetricsOverrideForTests;
            if (overrideCollector != null)
                return overrideCollector(messageCount);

            CollectAllocMetrics(
                gcBeforeTotal,
                gcBeforeThread,
                gen0Before,
                gen1Before,
                gen2Before,
                messageCount,
                out var allocTotal,
                out var allocThread,
                out var allocPerMsg,
                out var gen0,
                out var gen1,
                out var gen2,
                out var notes);
            return new AllocationMetricSample(
                allocTotal,
                allocThread,
                allocPerMsg,
                gen0,
                gen1,
                gen2,
                notes);
        }

        private static long ResolveElapsedMilliseconds(long actualElapsedMilliseconds)
        {
            var overrideElapsed = ElapsedMillisecondsOverrideForTests;
            return overrideElapsed == null
                ? actualElapsedMilliseconds
                : overrideElapsed(actualElapsedMilliseconds);
        }

        private static PerformanceScenarioResult TimedScenario(string name, int warmupCount, int msgCount,
            Action warmup, Action<int> measured, PerformanceThresholdConfig thresholds)
        {
            // Warmup
            warmup();

            // GC + measure
            PrepareAllocMeasurement(out var gcBeforeTotal, out var gcBeforeThread,
                out var gen0Before, out var gen1Before, out var gen2Before);
            var sw = Stopwatch.StartNew();
            measured(msgCount);
            sw.Stop();
            CollectAllocMetrics(gcBeforeTotal, gcBeforeThread, gen0Before, gen1Before, gen2Before,
                msgCount, out var allocTotal, out var allocThread, out var allocPerMsg,
                out var gen0, out var gen1, out var gen2, out var allocNotes);

            var result = new PerformanceScenarioResult
            {
                name = name,
                warmupMessageCount = warmupCount,
                messageCount = msgCount,
                elapsedMs = sw.ElapsedMilliseconds,
                messagesPerSecond = sw.Elapsed.TotalSeconds > 0 ? msgCount / sw.Elapsed.TotalSeconds : 0,
                allocatedBytesTotal = allocTotal,
                allocatedBytesCurrentThread = allocThread,
                allocatedBytesPerMessage = allocPerMsg,
                gen0Collections = gen0,
                gen1Collections = gen1,
                gen2Collections = gen2,
                allocationNotes = allocNotes,
                passed = true
            };
            ApplyThresholds(result, thresholds);
            return result;
        }

        private static PerformanceScenarioThreshold ResolveThreshold(string scenarioName, PerformanceThresholdConfig config)
        {
            if (config == null || !config.enabled)
                return null;

            PerformanceScenarioThreshold scenario = null;
            config.scenarios?.TryGetValue(scenarioName, out scenario);
            if (config.defaults == null && scenario == null)
                return null;

            return new PerformanceScenarioThreshold
            {
                maxElapsedMs = scenario?.maxElapsedMs ?? config.defaults?.maxElapsedMs,
                minMessagesPerSecond = scenario?.minMessagesPerSecond ?? config.defaults?.minMessagesPerSecond,
                maxAllocatedBytesPerMessage = scenario?.maxAllocatedBytesPerMessage ?? config.defaults?.maxAllocatedBytesPerMessage,
                maxAllocatedBytesTotal = scenario?.maxAllocatedBytesTotal ?? config.defaults?.maxAllocatedBytesTotal,
                maxGen0Collections = scenario?.maxGen0Collections ?? config.defaults?.maxGen0Collections,
                maxGen1Collections = scenario?.maxGen1Collections ?? config.defaults?.maxGen1Collections,
                maxGen2Collections = scenario?.maxGen2Collections ?? config.defaults?.maxGen2Collections
            };
        }

        private static void ApplyThresholds(PerformanceScenarioResult result, PerformanceThresholdConfig thresholds)
        {
            var threshold = ResolveThreshold(result.name, thresholds);
            if (threshold == null)
                return;

            var failures = new List<string>();
            if (threshold.maxElapsedMs.HasValue && result.elapsedMs > threshold.maxElapsedMs.Value)
                failures.Add($"elapsedMs {result.elapsedMs} > max {threshold.maxElapsedMs.Value}");
            if (threshold.minMessagesPerSecond.HasValue
                && result.messageCount > 0
                && result.messagesPerSecond < threshold.minMessagesPerSecond.Value)
                failures.Add($"messagesPerSecond {result.messagesPerSecond:F3} < min {threshold.minMessagesPerSecond.Value:F3}");
            if (threshold.maxAllocatedBytesPerMessage.HasValue
                && result.allocatedBytesPerMessage > threshold.maxAllocatedBytesPerMessage.Value)
                failures.Add($"allocatedBytesPerMessage {result.allocatedBytesPerMessage:F3} > max {threshold.maxAllocatedBytesPerMessage.Value:F3}");
            if (threshold.maxAllocatedBytesTotal.HasValue && result.allocatedBytesTotal > threshold.maxAllocatedBytesTotal.Value)
                failures.Add($"allocatedBytesTotal {result.allocatedBytesTotal} > max {threshold.maxAllocatedBytesTotal.Value}");
            if (threshold.maxGen0Collections.HasValue && result.gen0Collections > threshold.maxGen0Collections.Value)
                failures.Add($"gen0Collections {result.gen0Collections} > max {threshold.maxGen0Collections.Value}");
            if (threshold.maxGen1Collections.HasValue && result.gen1Collections > threshold.maxGen1Collections.Value)
                failures.Add($"gen1Collections {result.gen1Collections} > max {threshold.maxGen1Collections.Value}");
            if (threshold.maxGen2Collections.HasValue && result.gen2Collections > threshold.maxGen2Collections.Value)
                failures.Add($"gen2Collections {result.gen2Collections} > max {threshold.maxGen2Collections.Value}");

            result.thresholdsEvaluated = true;
            if (failures.Count == 0)
            {
                result.thresholdNotes = result.passed
                    ? "thresholds passed"
                    : "thresholds passed; scenario result already failed";
                return;
            }

            result.passed = false;
            result.thresholdNotes = string.Join("; ", failures);
        }


        /// <remarks>Phase192 source sections: DataLoader.cs, PublishingAndMcap.cs, MicroScenarios.cs</remarks>
    }
}
