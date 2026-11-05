// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Performance
{
    public static partial class PerformanceRunner
    {
        private static PerformanceScenarioResult RunFoxRunStreamIngest(
            int warmup,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var stream = new FoxRunStream<int>(new FoxRunStreamOptions(
                capacity: 1024,
                maxInputHz: 1000,
                maxBatch: 128,
                overflow: FoxRunStreamOverflowPolicy.DropOldest));
            Action<int> dispose = _ => { };
            return TimedScenario("FoxRunStreamIngest", warmup, messages, () =>
            {
                for (var i = 0; i < warmup; i++)
                {
                    if (stream.TryAdmitInput())
                        stream.TryEnqueueOwnedAfterAdmission(i, dispose);
                    stream.Drain(_ => { });
                }
            }, count =>
            {
                for (var i = 0; i < count; i++)
                {
                    if (stream.TryAdmitInput())
                        stream.TryEnqueueOwnedAfterAdmission(i, dispose);
                    stream.Drain(_ => { });
                }
            }, thresholds);
        }

        private static PerformanceScenarioResult RunFoxRunPublishPayload(
            int warmup,
            int topics,
            int clients,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var result = RunPublishJsonFanoutPrebuiltPayload(warmup, topics, clients, messages, thresholds);
            result.name = "FoxRunPublishPayload";
            result.notes = "Representative generated publish helper payload fanout.";
            return result;
        }

        private static PerformanceScenarioResult RunWsSendQueueLargeFrame(
            PerformanceThresholdConfig thresholds)
        {
            var result = RunTransportQueueMicro(thresholds);
            result.name = "WsSendQueueLargeFrame";
            result.notes = "Uses the existing bounded queue micro-harness while large-frame coverage is expanded.";
            return result;
        }

        private static PerformanceScenarioResult RunLargePayloadFanout(
            int warmup,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "test.LargePayload",
                Encoding = "jsonschema",
                Content = "{\"type\":\"array\"}"
            });
            var transport = new FakePerformanceTransport();
            var session = new FoxgloveSession("perf-large-payload", transport, schemaRegistry: registry);
            session.RegisterSchemaChannel(1, "/perf/large", "test.LargePayload", "json");
            transport.SimulateConnect(1);
            transport.SimulateSubscribe(1, 1, 1);
            var payload = new byte[1024 * 1024];
            return TimedScenario("LargePayloadFanout", warmup, messages, () =>
            {
                for (var i = 0; i < warmup; i++)
                    session.Publish(1, payload);
            }, count =>
            {
                for (var i = 0; i < count; i++)
                    session.Publish(1, payload);
            }, thresholds);
        }
    }
}
