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

        private static byte[] MakeProtoPayload(Timestamp ts)
        {
            return new FrameTransform
            {
                Timestamp = ts,
                ParentFrameId = "map",
                ChildFrameId = "base",
                Translation = new Vector3 { X = 1, Y = 2, Z = 3 },
                Rotation = new Quaternion { W = 1 }
            }.ToByteArray();
        }

        private sealed class DataLoaderFixture
        {
            public string Path;
            public string Kind;
            public int ChannelCount;
            public int SchemaCount;
            public int MessagesPerChannel;
            public int TotalMessageCount;
            public string FirstTopic;
            public ulong StartTimeNs;
            public ulong EndTimeNs;
            public ulong WindowStartTimeNs;
            public ulong WindowEndTimeNs;
            public int WindowMessageCount;
        }

        private static string DataLoaderFixtureDirectory()
        {
            var fixtureDir = Path.Combine(RepoRoot, "build", "performance", "fixtures");
            Directory.CreateDirectory(fixtureDir);
            return fixtureDir;
        }

        private static DataLoaderFixture CreateDataLoaderIndexedFixture(int topics, int messagesPerChannel)
        {
            var path = Path.Combine(DataLoaderFixtureDirectory(),
                $"phase118_dataloader_indexed_v1_{topics}_{messagesPerChannel}.mcap");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var recorder = new McapRecorder(fs))
                {
                    for (var t = 0; t < topics; t++)
                        recorder.AddChannel((uint)(t + 1), $"/perf/dataloader/indexed/{t}", "json",
                            $"test.PerfDataLoaderIndexed{t}", "jsonschema", "{\"type\":\"object\"}");

                    for (var i = 0; i < messagesPerChannel; i++)
                    {
                        for (var t = 0; t < topics; t++)
                        {
                            var logTime = (ulong)i * 1000UL + (ulong)t;
                            recorder.WriteMessage((uint)(t + 1), logTime, MakeJsonPayload(t, i));
                        }
                    }

                    recorder.Close();
                }
            }

            return DescribeDataLoaderFixture(path, "indexed", topics, topics, messagesPerChannel,
                "/perf/dataloader/indexed/0");
        }

        private static DataLoaderFixture CreateDataLoaderDirectFixture(int topics, int messagesPerChannel)
        {
            var path = Path.Combine(DataLoaderFixtureDirectory(),
                $"phase118_dataloader_direct_v1_{topics}_{messagesPerChannel}.mcap");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var writer = new McapWriter(fs))
                {
                    writer.WriteMagic();
                    writer.WriteHeader("", "phase118-dataloader-direct");
                    for (var t = 0; t < topics; t++)
                        writer.WriteSchema((ushort)(t + 1), $"test.PerfDataLoaderDirect{t}", "jsonschema",
                            Encoding.UTF8.GetBytes("{\"type\":\"object\"}"));
                    for (var t = 0; t < topics; t++)
                        writer.WriteChannel((ushort)(t + 1), (ushort)(t + 1), $"/perf/dataloader/direct/{t}",
                            "json", new Dictionary<string, string>());

                    for (var i = 0; i < messagesPerChannel; i++)
                    {
                        for (var t = 0; t < topics; t++)
                        {
                            var logTime = (ulong)i * 1000UL + (ulong)t;
                            writer.WriteMessage((ushort)(t + 1), (uint)(i + 1), logTime, logTime,
                                MakeJsonPayload(t, i));
                        }
                    }

                    writer.WriteFooter(0, 0, 0);
                    writer.WriteMagic();
                }
            }

            return DescribeDataLoaderFixture(path, "direct", topics, topics, messagesPerChannel,
                "/perf/dataloader/direct/0");
        }

        private static DataLoaderFixture CreateDataLoaderSparseFixture()
        {
            var path = Path.Combine(DataLoaderFixtureDirectory(), "phase118_dataloader_sparse_v1.mcap");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var recorder = new McapRecorder(fs))
                {
                    recorder.AddChannel(1, "/perf/dataloader/sparse/a", "json",
                        "test.PerfDataLoaderSparseA", "jsonschema", "{\"type\":\"object\"}");
                    recorder.AddChannel(2, "/perf/dataloader/sparse/empty", "json",
                        "test.PerfDataLoaderSparseEmpty", "jsonschema", "{\"type\":\"object\"}");
                    recorder.AddChannel(3, "/perf/dataloader/sparse/c", "json",
                        "test.PerfDataLoaderSparseC", "jsonschema", "{\"type\":\"object\"}");
                    recorder.WriteMessage(1, 10, MakeJsonPayload(0, 0));
                    recorder.WriteMessage(3, 90, MakeJsonPayload(2, 0));
                    recorder.Close();
                }
            }

            return new DataLoaderFixture
            {
                Path = path,
                Kind = "sparse",
                ChannelCount = 3,
                SchemaCount = 3,
                MessagesPerChannel = 0,
                TotalMessageCount = 2,
                FirstTopic = "/perf/dataloader/sparse/a",
                StartTimeNs = 10,
                EndTimeNs = 90,
                WindowStartTimeNs = 10,
                WindowEndTimeNs = 90,
                WindowMessageCount = 2
            };
        }

        private static DataLoaderFixture DescribeDataLoaderFixture(
            string path,
            string kind,
            int channelCount,
            int schemaCount,
            int messagesPerChannel,
            string firstTopic)
        {
            var windowSpan = Math.Min(100, Math.Max(messagesPerChannel, 1));
            var windowStartIndex = messagesPerChannel > windowSpan
                ? Math.Min(100, messagesPerChannel - windowSpan)
                : 0;
            var windowEndIndex = windowStartIndex + windowSpan - 1;
            return new DataLoaderFixture
            {
                Path = path,
                Kind = kind,
                ChannelCount = channelCount,
                SchemaCount = schemaCount,
                MessagesPerChannel = messagesPerChannel,
                TotalMessageCount = channelCount * messagesPerChannel,
                FirstTopic = firstTopic,
                StartTimeNs = 0,
                EndTimeNs = (ulong)(messagesPerChannel - 1) * 1000UL + (ulong)(channelCount - 1),
                WindowStartTimeNs = (ulong)windowStartIndex * 1000UL,
                WindowEndTimeNs = (ulong)windowEndIndex * 1000UL + (ulong)(channelCount - 1),
                WindowMessageCount = windowSpan * channelCount
            };
        }

        private static void EnsureDataLoaderInitialization(DataLoaderFixture fixture, McapDataLoaderInitialization init)
        {
            if (init.Channels.Count != fixture.ChannelCount)
                throw new Exception($"{fixture.Kind}: expected {fixture.ChannelCount} channels, got {init.Channels.Count}");
            if (init.Schemas.Count != fixture.SchemaCount)
                throw new Exception($"{fixture.Kind}: expected {fixture.SchemaCount} schemas, got {init.Schemas.Count}");
        }

        private static int CountDataLoaderMessages(IEnumerable<McapDataLoaderMessage> messages)
        {
            var count = 0;
            foreach (var _ in messages)
                count++;
            return count;
        }

        private static List<ushort> CreateChannelIds(int channelCount)
        {
            var ids = new List<ushort>(channelCount);
            for (var i = 0; i < channelCount; i++)
                ids.Add((ushort)(i + 1));
            return ids;
        }

        private static void ApplyDataLoaderFields(
            PerformanceScenarioResult result,
            DataLoaderFixture fixture,
            int selectedChannelCount,
            int selectedTopicCount,
            ulong? queryStartTimeNs,
            ulong? queryEndTimeNs,
            int returnedMessageCount,
            int backfillHitCount)
        {
            result.fixtureKind = fixture.Kind;
            result.fixturePath = fixture.Path;
            result.channelCount = fixture.ChannelCount;
            result.schemaCount = fixture.SchemaCount;
            result.selectedChannelCount = selectedChannelCount;
            result.selectedTopicCount = selectedTopicCount;
            result.queryStartTimeNs = queryStartTimeNs;
            result.queryEndTimeNs = queryEndTimeNs;
            result.returnedMessageCount = returnedMessageCount;
            result.backfillHitCount = backfillHitCount;
            result.fixtureBytes = File.Exists(fixture.Path) ? new FileInfo(fixture.Path).Length : 0;

            var note = $"fixtureKind={fixture.Kind}; fixtureBytes={result.fixtureBytes}";
            result.notes = string.IsNullOrEmpty(result.notes) ? note : result.notes + "; " + note;
        }

        // Scenarios
    }
}
