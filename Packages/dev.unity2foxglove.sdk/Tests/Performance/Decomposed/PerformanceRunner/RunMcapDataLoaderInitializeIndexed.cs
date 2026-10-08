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

        private static PerformanceScenarioResult RunPublishJsonFanoutPrebuiltPayload(
            int warmup,
            int topics,
            int clients,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);
            registry.Register(new SchemaEntry { Name = "test.PerfJson", Encoding = "jsonschema", Content = "{\"type\":\"object\"}" });
            var transport = new FakePerformanceTransport();
            var session = new FoxgloveSession("perf-json-prebuilt", transport, schemaRegistry: registry);
            var channelIds = new List<uint>();
            var payloads = new byte[topics][];

            for (int t = 0; t < topics; t++)
            {
                uint chId = (uint)(t + 1);
                session.RegisterSchemaChannel(chId, $"/perf/json/prebuilt/{t}", "test.PerfJson", "json");
                channelIds.Add(chId);
                payloads[t] = MakeJsonPayload(t, 0);
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
                        session.Publish(channelIds[t], payloads[t]);
                }
            };

            var totalMessages = messages * topics;
            var result = TimedScenario("PublishJsonFanoutPrebuiltPayload", warmup * topics, totalMessages, warmupFn, count =>
            {
                int outer = count / topics;
                for (int i = 0; i < outer; i++)
                {
                    for (int t = 0; t < channelIds.Count; t++)
                        session.Publish(channelIds[t], payloads[t]);
                }
            }, thresholds);
            result.notes = "prebuiltPayload=true; excludes JSON serialization allocation";
            return result;
        }

        private static PerformanceScenarioResult RunPublishProtoFanout(
            int warmup,
            int topics,
            int clients,
            int messages,
            PerformanceThresholdConfig thresholds)
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
            }, thresholds);
        }
    }
}
