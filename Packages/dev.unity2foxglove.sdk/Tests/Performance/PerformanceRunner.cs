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
    public static class PerformanceRunner
    {
        private static string RepoRoot
        {
            get
            {
                var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
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

        public static List<PerformanceScenarioResult> RunAll(string mode)
        {
            bool isFull = mode == "full";
            var results = new List<PerformanceScenarioResult>();

            int warmup = isFull ? FullWarmup : QuickWarmup;
            int topics = isFull ? FullTopics : QuickTopics;
            int clients = isFull ? FullClients : QuickClients;
            int messages = isFull ? FullMessages : QuickMessages;

            results.Add(RunPublishJsonFanout(warmup, topics, clients, messages));
            results.Add(RunPublishProtoFanout(warmup, topics, clients, messages));
            results.Add(RunMcapRecord(warmup, topics, messages, "", "McapRecordNone"));
            results.Add(RunMcapRecord(warmup, topics, messages, "lz4", "McapRecordLz4"));
            results.Add(RunMcapRecord(warmup, topics, messages, "zstd", "McapRecordZstd"));
            results.Add(RunMcapRecordNonePrebuiltPayload(warmup, topics, messages));
            results.Add(RunMcapReplayTick(warmup, topics, messages, isFull));
            var dataLoaderMessages = isFull ? Math.Min(messages, 10000) : Math.Min(messages, 1000);
            var dataLoaderInitializeIterations = isFull ? 20 : 5;
            var indexedFixture = CreateDataLoaderIndexedFixture(topics, dataLoaderMessages);
            var directFixture = CreateDataLoaderDirectFixture(topics, dataLoaderMessages);
            var sparseFixture = CreateDataLoaderSparseFixture();
            results.Add(RunMcapDataLoaderInitializeIndexed(indexedFixture, dataLoaderInitializeIterations));
            results.Add(RunMcapDataLoaderInitializeDirect(directFixture, dataLoaderInitializeIterations));
            results.Add(RunMcapDataLoaderIterateAllIndexed(indexedFixture));
            results.Add(RunMcapDataLoaderIterateTopicFilterIndexed(indexedFixture));
            results.Add(RunMcapDataLoaderIterateTimeWindowIndexed(indexedFixture));
            results.Add(RunMcapDataLoaderIterateAllDirect(directFixture));
            results.Add(RunMcapDataLoaderBackfillIndexed(indexedFixture));
            results.Add(RunMcapDataLoaderBackfillSparse(sparseFixture));
            results.Add(RunMcapRecordAttachmentSummary());
            results.Add(RunCameraBackpressurePolicyMicro());
            results.Add(RunTransportQueueMicro());

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

        private static PerformanceScenarioResult TimedScenario(string name, int warmupCount, int msgCount,
            Action warmup, Action<int> measured)
        {
            // GC before warmup
            GC.Collect();
            GC.WaitForPendingFinalizers();

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

            return new PerformanceScenarioResult
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
        }

        private static byte[] MakeJsonPayload(int topicIdx, int msgIdx)
        {
            var obj = new { seq = msgIdx, topic = topicIdx, message = "perf baseline payload data" };
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));
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
                $"phase118_dataloader_indexed_{topics}_{messagesPerChannel}.mcap");
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

            return DescribeDataLoaderFixture(path, "indexed", topics, topics, messagesPerChannel,
                "/perf/dataloader/indexed/0");
        }

        private static DataLoaderFixture CreateDataLoaderDirectFixture(int topics, int messagesPerChannel)
        {
            var path = Path.Combine(DataLoaderFixtureDirectory(),
                $"phase118_dataloader_direct_{topics}_{messagesPerChannel}.mcap");
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

            return DescribeDataLoaderFixture(path, "direct", topics, topics, messagesPerChannel,
                "/perf/dataloader/direct/0");
        }

        private static DataLoaderFixture CreateDataLoaderSparseFixture()
        {
            var path = Path.Combine(DataLoaderFixtureDirectory(), "phase118_dataloader_sparse.mcap");
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
            int iterations)
        {
            return RunMcapDataLoaderInitialize("McapDataLoaderInitializeIndexed", fixture, iterations);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderInitializeDirect(
            DataLoaderFixture fixture,
            int iterations)
        {
            return RunMcapDataLoaderInitialize("McapDataLoaderInitializeDirect", fixture, iterations);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderInitialize(
            string name,
            DataLoaderFixture fixture,
            int iterations)
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
            });

            ApplyDataLoaderFields(result, fixture, 0, 0, null, null, 0, 0);
            result.notes = result.notes + $"; initializeIterations={iterations}";
            return result;
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateAllIndexed(DataLoaderFixture fixture)
        {
            return RunMcapDataLoaderIteration(
                "McapDataLoaderIterateAllIndexed",
                fixture,
                new McapDataLoaderQuery(),
                fixture.TotalMessageCount,
                fixture.ChannelCount,
                0,
                null,
                null);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateTopicFilterIndexed(DataLoaderFixture fixture)
        {
            return RunMcapDataLoaderIteration(
                "McapDataLoaderIterateTopicFilterIndexed",
                fixture,
                new McapDataLoaderQuery { Topics = new List<string> { fixture.FirstTopic } },
                fixture.MessagesPerChannel,
                0,
                1,
                null,
                null);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateTimeWindowIndexed(DataLoaderFixture fixture)
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
                fixture.WindowEndTimeNs);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIterateAllDirect(DataLoaderFixture fixture)
        {
            return RunMcapDataLoaderIteration(
                "McapDataLoaderIterateAllDirect",
                fixture,
                new McapDataLoaderQuery(),
                fixture.TotalMessageCount,
                fixture.ChannelCount,
                0,
                null,
                null);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderIteration(
            string name,
            DataLoaderFixture fixture,
            McapDataLoaderQuery query,
            int expectedCount,
            int selectedChannelCount,
            int selectedTopicCount,
            ulong? queryStartTimeNs,
            ulong? queryEndTimeNs)
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
            });

            ApplyDataLoaderFields(result, fixture, selectedChannelCount, selectedTopicCount,
                queryStartTimeNs, queryEndTimeNs, returnedMessageCount, 0);
            return result;
        }

        private static PerformanceScenarioResult RunMcapDataLoaderBackfillIndexed(DataLoaderFixture fixture)
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
                0);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderBackfillSparse(DataLoaderFixture fixture)
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
                0);
        }

        private static PerformanceScenarioResult RunMcapDataLoaderBackfill(
            string name,
            DataLoaderFixture fixture,
            McapDataLoaderBackfillQuery query,
            int expectedCount,
            int selectedChannelCount,
            int selectedTopicCount)
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
            });

            ApplyDataLoaderFields(result, fixture, selectedChannelCount, selectedTopicCount,
                null, query.TimeNs, backfillHitCount, backfillHitCount);
            return result;
        }

        private static PerformanceScenarioResult RunPublishJsonFanout(int warmup, int topics, int clients, int messages)
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);
            registry.Register(new SchemaEntry { Name = "test.PerfJson", Encoding = "jsonschema", Content = "{\"type\":\"object\"}" });
            var transport = new FakePerformanceTransport();
            var session = new FoxgloveSession("perf-json", transport, schemaRegistry: registry);
            var channelIds = new List<uint>();

            for (int t = 0; t < topics; t++)
            {
                uint chId = (uint)(t + 1);
                session.RegisterSchemaChannel(chId, $"/perf/json/{t}", "test.PerfJson", "json");
                channelIds.Add(chId);
            }

            for (int c = 0; c < clients; c++)
            {
                uint clientId = (uint)(c + 1);
                transport.SimulateConnect(clientId);
                for (int t = 0; t < topics; t++)
                    transport.SimulateSubscribe(clientId, (uint)(c * topics + t + 1), channelIds[t]);
            }

            transport.ResetCounters();

            Action warmupFn = () =>
            {
                for (int i = 0; i < warmup; i++)
                {
                    for (int t = 0; t < channelIds.Count; t++)
                        session.Publish(channelIds[t], MakeJsonPayload(t, i));
                }
            };

            var totalMessages = messages * topics;
            return TimedScenario("PublishJsonFanout", warmup * topics, totalMessages, warmupFn, count =>
            {
                int outer = count / topics;
                for (int i = 0; i < outer; i++)
                {
                    for (int t = 0; t < channelIds.Count; t++)
                        session.Publish(channelIds[t], MakeJsonPayload(t, i));
                }
            });
        }

        private static PerformanceScenarioResult RunPublishProtoFanout(int warmup, int topics, int clients, int messages)
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);
            ProtobufSchemasSetup.RegisterSchemas(registry);

            var transport = new FakePerformanceTransport();
            var session = new FoxgloveSession("perf-proto", transport, schemaRegistry: registry);
            session.EnableProtobuf();
            var channelIds = new List<uint>();

            var ts = new Timestamp { Seconds = 1, Nanos = 0 };
            var protoPayload = MakeProtoPayload(ts);

            for (int t = 0; t < topics; t++)
            {
                uint chId = (uint)(t + 1);
                session.RegisterProtobufSchemaChannel(chId, $"/perf/proto/{t}", "foxglove.FrameTransform");
                channelIds.Add(chId);
            }

            for (int c = 0; c < clients; c++)
            {
                uint clientId = (uint)(c + 1);
                transport.SimulateConnect(clientId);
                for (int t = 0; t < topics; t++)
                    transport.SimulateSubscribe(clientId, (uint)(c * topics + t + 1), channelIds[t]);
            }

            transport.ResetCounters();

            Action warmupFn = () =>
            {
                for (int i = 0; i < warmup; i++)
                {
                    foreach (var chId in channelIds)
                        session.Publish(chId, protoPayload);
                }
            };

            var totalMessages = messages * topics;
            return TimedScenario("PublishProtoFanout", warmup * topics, totalMessages, warmupFn, count =>
            {
                int outer = count / topics;
                for (int i = 0; i < outer; i++)
                {
                    foreach (var chId in channelIds)
                        session.Publish(chId, protoPayload);
                }
            });
        }

        private static PerformanceScenarioResult RunMcapRecord(int warmup, int topics, int messages, string compression, string name)
        {
            int warmupCount = warmup;
            long outputBytes = 0;
            long payloadBytes = 0;
            var totalMessages = messages * topics;

            var result = TimedScenario(name, warmup * topics, totalMessages, () =>
            {
                using var ms = new MemoryStream();
                using var recorder = new McapRecorder(ms, null, McapRecorder.DefaultChunkSizeBytes, compression);
                for (int t = 0; t < topics; t++)
                    recorder.AddChannel((uint)(t + 1), $"/perf/mcap/{t}", "json", "test.PerfMcap", "jsonschema", "{\"type\":\"object\"}");
                for (int i = 0; i < warmupCount; i++)
                {
                    for (int t = 0; t < topics; t++)
                        recorder.WriteMessage((uint)(t + 1), (ulong)i * 1000, MakeJsonPayload(t, i));
                }
                recorder.Close();
            }, count =>
            {
                int outer = count / topics;
                var ms = new MemoryStream();
                using var recorder = new McapRecorder(ms, null, McapRecorder.DefaultChunkSizeBytes, compression);
                for (int t = 0; t < topics; t++)
                    recorder.AddChannel((uint)(t + 1), $"/perf/mcap/{t}", "json", "test.PerfMcap", "jsonschema", "{\"type\":\"object\"}");
                for (int i = 0; i < outer; i++)
                {
                    for (int t = 0; t < topics; t++)
                    {
                        var payload = MakeJsonPayload(t, i);
                        payloadBytes += payload.Length;
                        recorder.WriteMessage((uint)(t + 1), (ulong)i * 1000, payload);
                    }
                }
                recorder.Close();
                outputBytes = ms.Length;

                // Verify
                ms.Position = 0;
                var reader = new McapReader(ms);
                var summary = reader.ReadSummary();
                if (summary.Statistics.MessageCount != (ulong)count)
                    throw new Exception($"MCAP message count mismatch: expected {count}, got {summary.Statistics.MessageCount}");
            });

            result.outputBytes = outputBytes;
            if (outputBytes > 0 && payloadBytes > 0)
            {
                var compressionLabel = string.IsNullOrEmpty(compression) ? "none" : compression;
                var ratio = (double)payloadBytes / outputBytes;
                result.notes = $"compression={compressionLabel}; payloadBytes={payloadBytes}; fileBytes={outputBytes}; payloadToFileRatio={ratio:F3}";
            }

            return result;
        }

        private static PerformanceScenarioResult RunMcapRecordNonePrebuiltPayload(int warmup, int topics, int messages)
        {
            var payload = MakeJsonPayload(0, 0);
            int warmupCount = warmup;
            long outputBytes = 0;
            long payloadBytes = 0;

            var totalMessages = messages * topics;
            var result = TimedScenario("McapRecordNonePrebuiltPayload", warmup * topics, totalMessages, () =>
            {
                using var ms = new MemoryStream();
                using var recorder = new McapRecorder(ms, null, McapRecorder.DefaultChunkSizeBytes, "");
                for (int t = 0; t < topics; t++)
                    recorder.AddChannel((uint)(t + 1), $"/perf/prebuilt/{t}", "json", "test.PerfPb", "jsonschema", "{\"type\":\"object\"}");
                for (int i = 0; i < warmupCount; i++)
                {
                    for (int t = 0; t < topics; t++)
                        recorder.WriteMessage((uint)(t + 1), (ulong)i * 1000, payload);
                }
                recorder.Close();
            }, count =>
            {
                int outer = count / topics;
                var ms = new MemoryStream();
                using var recorder = new McapRecorder(ms, null, McapRecorder.DefaultChunkSizeBytes, "");
                for (int t = 0; t < topics; t++)
                    recorder.AddChannel((uint)(t + 1), $"/perf/prebuilt/{t}", "json", "test.PerfPb", "jsonschema", "{\"type\":\"object\"}");
                for (int i = 0; i < outer; i++)
                {
                    for (int t = 0; t < topics; t++)
                        recorder.WriteMessage((uint)(t + 1), (ulong)i * 1000, payload);
                }
                recorder.Close();
                outputBytes = ms.Length;
                payloadBytes = (long)payload.Length * count;

                ms.Position = 0;
                var reader = new McapReader(ms);
                var summary = reader.ReadSummary();
                if (summary.Statistics.MessageCount != (ulong)count)
                    throw new Exception($"MCAP message count mismatch: expected {count}, got {summary.Statistics.MessageCount}");
            });

            result.outputBytes = outputBytes;
            result.notes = $"compression=none; prebuiltPayload=true; payloadBytes={payloadBytes}; fileBytes={outputBytes}; payloadBytesPerMessage={payload.Length}";
            return result;
        }

        private static PerformanceScenarioResult RunMcapReplayTick(int warmup, int topics, int messages, bool isFull)
        {
            // Build a fixture MCAP first
            var fixtureDir = Path.Combine(RepoRoot, "build", "performance", "fixtures");
            Directory.CreateDirectory(fixtureDir);
            var fixturePath = Path.Combine(fixtureDir, "phase35_replay_fixture.mcap");

            using (var fs = new FileStream(fixturePath, FileMode.Create, FileAccess.Write))
            using (var recorder = new McapRecorder(fs))
            {
                for (int t = 0; t < topics; t++)
                    recorder.AddChannel((uint)(t + 1), $"/perf/replay/{t}", "json", "test.PerfReplay", "jsonschema", "{\"type\":\"object\"}");
                for (int i = 0; i < messages; i++)
                {
                    for (int t = 0; t < topics; t++)
                        recorder.WriteMessage((uint)(t + 1), (ulong)i * 1000, MakeJsonPayload(t, i));
                }
                recorder.Close();
            }

            int warmupCount = warmup;
            var totalReplayMessages = messages * topics;
            var result = TimedScenario("McapReplayTick", warmup * topics, totalReplayMessages, () =>
            {
                using var engine = new McapReplayEngine();
                engine.Load(fixturePath);
                engine.MaxMessagesPerTick = Math.Max(engine.MaxMessagesPerTick, topics);
                engine.Seek(0);
                engine.Play();
                for (int i = 0; i < warmupCount; i++)
                {
                    var tickResult = engine.Tick((ulong)i * 1000);
                    tickResult.Clear();
                }
            }, count =>
            {
                int outer = count / topics;
                using var engine = new McapReplayEngine();
                engine.Load(fixturePath);
                engine.MaxMessagesPerTick = Math.Max(engine.MaxMessagesPerTick, topics);
                engine.Seek(0);
                engine.Play();
                int totalMessages = 0;
                for (int i = 0; i < outer; i++)
                {
                    var tickResult = engine.Tick((ulong)i * 1000);
                    totalMessages += tickResult.Count;
                    tickResult.Clear();
                }
                if (totalMessages != count)
                    throw new Exception($"McapReplayTick: expected {count} replay messages, got {totalMessages}");
            });

            result.notes = $"fixturePath={fixturePath}";
            return result;
        }

        private static PerformanceScenarioResult RunMcapRecordAttachmentSummary()
        {
            try
            {
                using var ms = new MemoryStream();
                using (var recorder = new McapRecorder(ms))
                {
                    recorder.AddChannel(1, "/perf/att", "json", "test.PerfAtt", "jsonschema", "{\"type\":\"object\"}");
                    recorder.WriteMessage(1, 1000, MakeJsonPayload(0, 1));
                    recorder.AddAttachment("perf.txt", "text/plain", Encoding.UTF8.GetBytes("phase35 regress"), 2000, 0);
                    recorder.Close();
                }

                ms.Position = 0;
                var reader = new McapReader(ms);
                var summary = reader.ReadSummary();
                if (summary.AttachmentIndexes.Count != 1)
                    throw new Exception("AttachmentIndexes count != 1");
                var attachment = reader.ReadAttachmentAt(summary.AttachmentIndexes[0].Offset);
                if (!attachment.CrcValid)
                    throw new Exception("Attachment CrcValid is false");
                if (summary.Statistics.AttachmentCount != 1)
                    throw new Exception("Statistics.AttachmentCount != 1");

                // Verify Footer summary_crc is non-zero
                var rawBytes = ms.ToArray();
                var footerIdx = rawBytes.Length - 8 - 1 - 8 - 20; // trailing magic + footer record
                var summaryCrcOffset = footerIdx + 1 + 8 + 8 + 8; // opcode + length + sumStart + sumOffStart
                var footerCrc = BitConverter.ToUInt32(rawBytes, summaryCrcOffset);
                if (footerCrc == 0)
                    throw new Exception("Footer summary_crc is zero");

                return new PerformanceScenarioResult
                {
                    name = "McapRecordAttachmentSummary",
                    warmupMessageCount = 0,
                    messageCount = 1,
                    elapsedMs = 0,
                    passed = true,
                    outputBytes = ms.Length,
                    notes = "Phase 34 regression guard: attachment index, CRC, summary_crc verified"
                };
            }
            catch (Exception ex)
            {
                return new PerformanceScenarioResult
                {
                    name = "McapRecordAttachmentSummary",
                    passed = false,
                    notes = ex.Message
                };
            }
        }

        private static PerformanceScenarioResult RunCameraBackpressurePolicyMicro()
        {
            // Measure policy evaluation overhead: run 100K evaluations in a
            // tight loop, while still validating the key state transitions.
            const int iterations = 100_000;
            CameraBackpressureResult r = default;
            long currentDrop = 0;
            long previousDrop = 0;
            double cooldownUntil = 0;
            var pressureCount = 0;
            var blockedCount = 0;
            var allowedCount = 0;
            var invalidState = false;

            PrepareAllocMeasurement(out var gcBeforeTotal, out var gcBeforeThread,
                out var gen0Before, out var gen1Before, out var gen2Before);
            var sw = Stopwatch.StartNew();

            for (var i = 0; i < iterations; i++)
            {
                var currentTime = 10.0 + i * 0.001;
                if (i > 0 && i % 1000 == 0)
                    currentDrop++;

                r = Evaluate(true, currentTime, 0.05, previousDrop, currentDrop, cooldownUntil);
                if (r.PressureObserved)
                    pressureCount++;
                if (r.SkippedByCooldown)
                    blockedCount++;
                if (r.AllowCapture)
                    allowedCount++;
                if (!r.AllowCapture && !r.SkippedByCooldown)
                    invalidState = true;
                if (r.PressureObserved && r.NextDropCount != currentDrop)
                    invalidState = true;

                previousDrop = r.NextDropCount;
                cooldownUntil = r.NextCooldownUntilSec;
            }

            sw.Stop();
            CollectAllocMetrics(gcBeforeTotal, gcBeforeThread, gen0Before, gen1Before, gen2Before,
                iterations, out var allocTotal, out var allocThread, out var allocPerMsg,
                out var gen0, out var gen1, out var gen2, out var allocNotes);

            var passed = !invalidState
                && pressureCount > 0
                && blockedCount > 0
                && allowedCount > 0
                && r.NextDropCount == currentDrop;

            return new PerformanceScenarioResult
            {
                name = "CameraBackpressurePolicyMicro",
                warmupMessageCount = 0,
                messageCount = iterations,
                elapsedMs = sw.ElapsedMilliseconds,
                messagesPerSecond = sw.Elapsed.TotalSeconds > 0 ? iterations / sw.Elapsed.TotalSeconds : 0,
                allocatedBytesTotal = allocTotal,
                allocatedBytesCurrentThread = allocThread,
                allocatedBytesPerMessage = allocPerMsg,
                notes = "policy evaluation loop; 100K iterations; no Unity/GPU; "
                    + $"pressure={pressureCount}, blocked={blockedCount}, allowed={allowedCount}",
                passed = passed
            };
        }

        private static PerformanceScenarioResult RunTransportQueueMicro()
        {
            bool accepted, shouldDisc, dataDropped;

            // Data overflow drops oldest
            var q = new WsSendQueue(maxFrames: 4, maxQueuedBytes: 1024 * 1024);
            for (int i = 0; i < 4; i++)
                q.Enqueue(D(1));
            var er = q.Enqueue(D(1));
            accepted = er.Accepted;
            dataDropped = er.DroppedDataFrames > 0;

            // Control priority over data
            var q2 = new WsSendQueue(maxFrames: 3, maxQueuedBytes: 1024 * 1024);
            q2.Enqueue(D(1)); q2.Enqueue(D(1)); q2.Enqueue(D(1));
            er = q2.Enqueue(C(1));
            shouldDisc = !er.ShouldDisconnect;
            q2.TryDequeue(out var first);
            bool controlFirst = first.Priority == FramePriority.Control;

            // Control-only overflow disconnects
            var q3 = new WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024 * 1024);
            q3.Enqueue(C(1)); q3.Enqueue(C(1));
            er = q3.Enqueue(C(1));
            bool ctrlDisc = !er.Accepted && er.ShouldDisconnect;

            // Drain after complete
            var q4 = new WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024 * 1024);
            q4.Enqueue(D(1));
            q4.Complete();
            bool completed = q4.IsCompleted;
            q4.TryDequeue(out _);
            bool drained = !q4.TryDequeue(out _);

            bool passed = accepted && dataDropped && shouldDisc && controlFirst && ctrlDisc && completed && drained;

            return new PerformanceScenarioResult
            {
                name = "TransportQueueMicro",
                warmupMessageCount = 0,
                messageCount = 0,
                passed = passed,
                notes = passed ? "Queue enqueue/drop/control/complete paths exercised" : "Queue scenario failed"
            };

            static QueuedFrame D(byte b) =>
                new QueuedFrame(WsOpcode.Binary, new[] { b }, FramePriority.Data);
            static QueuedFrame C(byte b) =>
                new QueuedFrame(WsOpcode.Text, new[] { b }, FramePriority.Control);
        }
    }
}
