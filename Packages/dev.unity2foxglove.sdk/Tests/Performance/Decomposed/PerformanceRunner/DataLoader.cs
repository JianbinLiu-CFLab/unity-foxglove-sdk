// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Performance
// Purpose: Performance scenario runner source section.

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

        private static PerformanceScenarioResult RunMcapDataLoaderInitializeIndexed(
            DataLoaderFixture fixture,
            int iterations,
            PerformanceThresholdConfig thresholds)
        {
            return RunMcapDataLoaderInitialize("McapDataLoaderInitializeIndexed", fixture, iterations, thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderInitializeDirect(
            DataLoaderFixture fixture,
            int iterations,
            PerformanceThresholdConfig thresholds)
        {
            return RunMcapDataLoaderInitialize("McapDataLoaderInitializeDirect", fixture, iterations, thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderInitialize(
            string name,
            DataLoaderFixture fixture,
            int iterations,
            PerformanceThresholdConfig thresholds)
        {
            var result = TimedScenario(name, 1, iterations, () =>
            {
                using var loader = new McapDataLoader(fixture.Path);
                EnsureDataLoaderInitialization(fixture, loader.Initialize());
            }, count =>
            {
                for (var i = 0; i < count; i++)
                {
                    using var loader = new McapDataLoader(fixture.Path);
                    EnsureDataLoaderInitialization(fixture, loader.Initialize());
                }
            }, thresholds);

            ApplyDataLoaderFields(result, fixture, 0, 0, null, null, 0, 0);
            result.notes = result.notes + $"; initializeIterations={iterations}";
            return result;
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateAllIndexed(
            DataLoaderFixture fixture,
            PerformanceThresholdConfig thresholds)
        {
            return RunMcapDataLoaderIteration(
                "McapDataLoaderIterateAllIndexed",
                fixture,
                new McapDataLoaderQuery { MaxMessages = 0 },
                fixture.TotalMessageCount,
                fixture.ChannelCount,
                0,
                null,
                null,
                thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateTopicFilterIndexed(
            DataLoaderFixture fixture,
            PerformanceThresholdConfig thresholds)
        {
            return RunMcapDataLoaderIteration(
                "McapDataLoaderIterateTopicFilterIndexed",
                fixture,
                new McapDataLoaderQuery { Topics = new List<string> { fixture.FirstTopic } },
                fixture.MessagesPerChannel,
                0,
                1,
                null,
                null,
                thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateTimeWindowIndexed(
            DataLoaderFixture fixture,
            PerformanceThresholdConfig thresholds)
        {
            return RunMcapDataLoaderIteration(
                "McapDataLoaderIterateTimeWindowIndexed",
                fixture,
                new McapDataLoaderQuery
                {
                    StartTimeNs = fixture.WindowStartTimeNs,
                    EndTimeNs = fixture.WindowEndTimeNs
                },
                fixture.WindowMessageCount,
                fixture.ChannelCount,
                0,
                fixture.WindowStartTimeNs,
                fixture.WindowEndTimeNs,
                thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateAllDirect(
            DataLoaderFixture fixture,
            PerformanceThresholdConfig thresholds)
        {
            return RunMcapDataLoaderIteration(
                "McapDataLoaderIterateAllDirect",
                fixture,
                new McapDataLoaderQuery { MaxMessages = 0 },
                fixture.TotalMessageCount,
                fixture.ChannelCount,
                0,
                null,
                null,
                thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIteration(
            string name,
            DataLoaderFixture fixture,
            McapDataLoaderQuery query,
            int expectedCount,
            int selectedChannelCount,
            int selectedTopicCount,
            ulong? queryStartTimeNs,
            ulong? queryEndTimeNs,
            PerformanceThresholdConfig thresholds)
        {
            var returnedMessageCount = 0;
            var result = TimedScenario(name, expectedCount, expectedCount, () =>
            {
                using var loader = new McapDataLoader(fixture.Path);
                EnsureDataLoaderInitialization(fixture, loader.Initialize());
                var count = CountDataLoaderMessages(loader.CreateIterator(query));
                if (count != expectedCount)
                    throw new Exception($"{name}: expected {expectedCount} warmup messages, got {count}");
            }, _ =>
            {
                using var loader = new McapDataLoader(fixture.Path);
                EnsureDataLoaderInitialization(fixture, loader.Initialize());
                returnedMessageCount = CountDataLoaderMessages(loader.CreateIterator(query));
                if (returnedMessageCount != expectedCount)
                    throw new Exception($"{name}: expected {expectedCount} messages, got {returnedMessageCount}");
            }, thresholds);

            ApplyDataLoaderFields(result, fixture, selectedChannelCount, selectedTopicCount,
                queryStartTimeNs, queryEndTimeNs, returnedMessageCount, 0);
            return result;
        }

        private static PerformanceScenarioResult RunMcapDataLoaderBackfillIndexed(
            DataLoaderFixture fixture,
            PerformanceThresholdConfig thresholds)
        {
            var query = new McapDataLoaderBackfillQuery
            {
                TimeNs = fixture.EndTimeNs + 1000,
                ChannelIds = CreateChannelIds(fixture.ChannelCount)
            };
            return RunMcapDataLoaderBackfill(
                "McapDataLoaderBackfillIndexed",
                fixture,
                query,
                fixture.ChannelCount,
                fixture.ChannelCount,
                0,
                thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderBackfillSparse(
            DataLoaderFixture fixture,
            PerformanceThresholdConfig thresholds)
        {
            var query = new McapDataLoaderBackfillQuery
            {
                TimeNs = fixture.EndTimeNs + 1000,
                ChannelIds = CreateChannelIds(fixture.ChannelCount)
            };
            return RunMcapDataLoaderBackfill(
                "McapDataLoaderBackfillSparse",
                fixture,
                query,
                2,
                fixture.ChannelCount,
                0,
                thresholds);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderBackfill(
            string name,
            DataLoaderFixture fixture,
            McapDataLoaderBackfillQuery query,
            int expectedCount,
            int selectedChannelCount,
            int selectedTopicCount,
            PerformanceThresholdConfig thresholds)
        {
            var backfillHitCount = 0;
            var result = TimedScenario(name, fixture.TotalMessageCount, fixture.TotalMessageCount, () =>
            {
                using var loader = new McapDataLoader(fixture.Path);
                EnsureDataLoaderInitialization(fixture, loader.Initialize());
                var count = loader.GetBackfill(query).Count;
                if (count != expectedCount)
                    throw new Exception($"{name}: expected {expectedCount} warmup backfill hits, got {count}");
            }, _ =>
            {
                using var loader = new McapDataLoader(fixture.Path);
                EnsureDataLoaderInitialization(fixture, loader.Initialize());
                backfillHitCount = loader.GetBackfill(query).Count;
                if (backfillHitCount != expectedCount)
                    throw new Exception($"{name}: expected {expectedCount} backfill hits, got {backfillHitCount}");
            }, thresholds);

            ApplyDataLoaderFields(result, fixture, selectedChannelCount, selectedTopicCount,
                null, query.TimeNs, backfillHitCount, backfillHitCount);
            return result;
        }

    }
}
