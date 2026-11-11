// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Performance
{
    internal static class HighBandwidthScenarioRunner
    {
        private static PerformanceComponentEvidence Component(
            PerformanceScenarioResult result,
            double bytesPerSecond = 0)
        {
            return new PerformanceComponentEvidence
            {
                name = result.name,
                messageCount = result.messageCount,
                elapsedMs = result.elapsedMs,
                messagesPerSecond = result.messagesPerSecond,
                bytesPerSecond = bytesPerSecond,
                allocatedBytesTotal = result.allocatedBytesTotal,
                allocatedBytesPerMessage = result.allocatedBytesPerMessage,
                dropRatio = result.dropRatio,
                queuedBytesHighWater = result.queuedBytesHighWater,
                notes = result.notes
            };
        }

        internal static PerformanceScenarioResult RunFoxRunStreamIngest(
            int warmup,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            RunStreamVariant(1000, 10, 1024, 128);
            var defaultRun = RunStreamVariant(1000, messages <= 2000 ? 50 : 100, 1024, 128);
            var unboundedRun = RunStreamVariant(1_000_000_000, messages <= 2000 ? 15 : 30, 4096, 256);
            var result = CombineScenarioResults(
                "FoxRunStreamIngest",
                warmup,
                defaultRun,
                unboundedRun,
                "Default and Unbounded producer-thread admission/drain variants; "
                    + $"default={defaultRun.Notes}; unbounded={unboundedRun.Notes}");
            result.components = new List<PerformanceComponentEvidence>
            {
                StreamComponent("FoxRunStreamIngest.Default", defaultRun),
                StreamComponent("FoxRunStreamIngest.Unbounded", unboundedRun)
            };
            PerformanceRunner.ApplyThresholds(result, thresholds);
            return result;
        }

        internal static PerformanceScenarioResult RunFoxRunPublishPayload(
            int warmup,
            int topics,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var jsonOne = RunGeneratedJsonFanout(warmup, topics, 1, messages, thresholds);
            var jsonFour = RunGeneratedJsonFanout(warmup, topics, 4, messages, thresholds);
            var protoOne = RunGeneratedProtoFanout(warmup, topics, 1, messages, thresholds);
            var protoFour = RunGeneratedProtoFanout(warmup, topics, 4, messages, thresholds);
            var messagePackOne = RunMessagePackFanout(warmup, topics, 1, messages, thresholds);
            var messagePackFour = RunMessagePackFanout(warmup, topics, 4, messages, thresholds);
            var result = CombineScenarioResults(
                "FoxRunPublishPayload",
                warmup,
                jsonOne,
                jsonFour,
                "Generated StringBuilder JSON and FoxRunProtobufWire Protobuf one/four-client fanout; "
                    + $"MessagePack one/four-client: {messagePackOne.notes}; {messagePackFour.notes}",
                protoOne,
                protoFour,
                messagePackOne,
                messagePackFour);
            result.components = new List<PerformanceComponentEvidence>
            {
                Component(jsonOne, RateBytes(jsonOne, BuildGeneratedJsonPayload(0, 0).Length)),
                Component(jsonFour, RateBytes(jsonFour, BuildGeneratedJsonPayload(0, 0).Length)),
                Component(protoOne, RateBytes(protoOne, BuildGeneratedProtoPayload(0, 0).Length)),
                Component(protoFour, RateBytes(protoFour, BuildGeneratedProtoPayload(0, 0).Length)),
                Component(messagePackOne, RateBytes(messagePackOne, MakeMessagePackPayload().Length)),
                Component(messagePackFour, RateBytes(messagePackFour, MakeMessagePackPayload().Length))
            };
            var components = new[] { jsonOne, jsonFour, protoOne, protoFour, messagePackOne, messagePackFour };
            result.thresholdsEvaluated = false;
            var componentFailures = new List<string>();
            foreach (var component in components)
            {
                result.thresholdsEvaluated |= component.thresholdsEvaluated;
                if (!component.passed)
                    componentFailures.Add($"{component.name}: {component.thresholdNotes ?? "scenario failed"}");
            }
            result.thresholdNotes = componentFailures.Count == 0
                ? (result.thresholdsEvaluated ? "component thresholds passed" : null)
                : string.Join("; ", componentFailures);
            return result;
        }

        internal static PerformanceScenarioResult RunWsSendQueueLargeFrame(
            PerformanceThresholdConfig thresholds)
        {
            var sizes = new[] { 64 * 1024, 1024 * 1024, 4 * 1024 * 1024 };
            PerformanceRunner.PrepareAllocMeasurement(out var gcBeforeTotal, out var gcBeforeThread,
                out var gen0Before, out var gen1Before, out var gen2Before);
            var sw = Stopwatch.StartNew();
            var passed = true;
            var details = new List<string>();
            var frameEvidence = new List<PerformanceFrameEvidence>();
            var operations = 0;
            long acceptedBytes = 0;
            long droppedBytes = 0;
            long queuedBytesHighWater = 0;
            foreach (var size in sizes)
            {
                var queue = new WsSendQueue(maxFrames: 4, maxQueuedBytes: size * 2);
                var payload = new byte[size];
                long acceptedForSize = 0;
                long droppedForSize = 0;
                long highWaterForSize = 0;
                for (var i = 0; i < 8; i++)
                {
                    var enqueue = queue.Enqueue(new QueuedFrame(2, payload, FramePriority.Data));
                    passed &= enqueue.Accepted || enqueue.DroppedDataFrames > 0;
                    if (enqueue.Accepted)
                        acceptedForSize++;
                    droppedForSize += enqueue.DroppedDataFrames;
                    highWaterForSize = Math.Max(highWaterForSize, queue.QueuedBytes);
                    operations++;
                    if (i % 3 == 2)
                    {
                        queue.TryDequeue(out _);
                        highWaterForSize = Math.Max(highWaterForSize, queue.QueuedBytes);
                        operations++;
                    }
                }

                var control = queue.Enqueue(new QueuedFrame(1, new byte[] { 1 }, FramePriority.Control));
                passed &= control.Accepted && !control.ShouldDisconnect;
                passed &= queue.DroppedDataFramesSnapshot > 0;
                passed &= queue.GetSnapshot().QueuedBytes <= size * 2;
                operations++;
                passed &= queue.TryDequeue(out var first) && first.Priority == FramePriority.Control;
                highWaterForSize = Math.Max(highWaterForSize, queue.QueuedBytes);
                operations++;
                var controlDequeuedFirst = first.Priority == FramePriority.Control;
                var acceptedForFrame = acceptedForSize * (long)size;
                var droppedForFrame = droppedForSize * (long)size;
                acceptedBytes += acceptedForFrame;
                droppedBytes += droppedForFrame;
                queuedBytesHighWater = Math.Max(queuedBytesHighWater, highWaterForSize);
                frameEvidence.Add(new PerformanceFrameEvidence
                {
                    sizeBytes = size,
                    droppedDataFrames = droppedForSize,
                    queuedBytes = queue.QueuedBytes,
                    controlDequeuedFirst = controlDequeuedFirst,
                    acceptedBytes = acceptedForFrame,
                    droppedBytes = droppedForFrame,
                    queuedBytesHighWater = highWaterForSize,
                    dropRatio = acceptedForSize + droppedForSize > 0
                        ? (double)droppedForSize / (acceptedForSize + droppedForSize)
                        : 0
                });
                details.Add($"{size}B:acceptedBytes={acceptedForFrame};droppedBytes={droppedForFrame};"
                    + $"dropRatio={frameEvidence[frameEvidence.Count - 1].dropRatio:F3};queuedHighWater={highWaterForSize}");
            }
            sw.Stop();
            var metrics = PerformanceRunner.CollectAllocMetricSample(
                gcBeforeTotal,
                gcBeforeThread,
                gen0Before,
                gen1Before,
                gen2Before,
                operations);
            var result = new PerformanceScenarioResult
            {
                name = "WsSendQueueLargeFrame",
                warmupMessageCount = 0,
                messageCount = operations,
                elapsedMs = sw.ElapsedMilliseconds,
                messagesPerSecond = sw.Elapsed.TotalSeconds > 0 ? operations / sw.Elapsed.TotalSeconds : operations,
                bytesPerSecond = sw.Elapsed.TotalSeconds > 0 ? acceptedBytes / sw.Elapsed.TotalSeconds : acceptedBytes,
                allocatedBytesTotal = metrics.AllocatedBytesTotal,
                allocatedBytesCurrentThread = metrics.AllocatedBytesCurrentThread,
                allocatedBytesPerMessage = metrics.AllocatedBytesPerMessage,
                gen0Collections = metrics.Gen0Collections,
                gen1Collections = metrics.Gen1Collections,
                gen2Collections = metrics.Gen2Collections,
                allocationNotes = metrics.AllocationNotes,
                passed = passed,
                frameEvidence = frameEvidence,
                acceptedBytes = acceptedBytes,
                droppedBytes = droppedBytes,
                queuedBytesHighWater = queuedBytesHighWater,
                dropRatio = acceptedBytes + droppedBytes > 0
                    ? (double)droppedBytes / (acceptedBytes + droppedBytes)
                    : 0,
                notes = string.Join("; ", details)
            };
            PerformanceRunner.ApplyThresholds(result, thresholds);
            return result;
        }

        internal static PerformanceScenarioResult RunLargePayloadFanout(
            int warmup,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var oneClient = RunLargePayloadWebSocket(warmup, messages, 1, thresholds);
            var fourClients = RunLargePayloadWebSocket(warmup, messages, 4, thresholds);
            var mcap = RunLargePayloadMcap(Math.Min(messages, 64), thresholds);
            var result = CombineScenarioResults(
                "LargePayloadFanout",
                warmup,
                oneClient,
                fourClients,
                $"WebSocket one/four-client and MCAP none-compression sinks; mcap={mcap.notes}",
                mcap);
            result.components = new List<PerformanceComponentEvidence>
            {
                Component(oneClient, oneClient.elapsedMs > 0 ? oneClient.messageCount * 1024d * 1024d / (oneClient.elapsedMs / 1000d) : 0),
                Component(fourClients, fourClients.elapsedMs > 0 ? fourClients.messageCount * 1024d * 1024d / (fourClients.elapsedMs / 1000d) : 0),
                Component(mcap, mcap.elapsedMs > 0 ? mcap.outputBytes / (mcap.elapsedMs / 1000d) : 0)
            };
            PerformanceRunner.ApplyThresholds(result, thresholds);
            return result;
        }

        private static PerformanceComponentEvidence StreamComponent(string name, StreamRun run)
        {
            var elapsedSeconds = run.ElapsedMs / 1000d;
            return new PerformanceComponentEvidence
            {
                name = name,
                messageCount = run.MessageCount,
                elapsedMs = run.ElapsedMs,
                messagesPerSecond = elapsedSeconds > 0 ? run.MessageCount / elapsedSeconds : run.MessageCount,
                allocatedBytesTotal = run.Metrics.AllocatedBytesTotal,
                allocatedBytesPerMessage = run.Metrics.AllocatedBytesPerMessage,
                notes = run.Notes
            };
        }

        private static double RateBytes(PerformanceScenarioResult result, int bytesPerMessage)
        {
            return result.elapsedMs > 0
                ? result.messageCount * (double)bytesPerMessage / (result.elapsedMs / 1000d)
                : 0;
        }

        private static StreamRun RunStreamVariant(double maxInputHz, int durationMs, int capacity, int maxBatch)
        {
            var stream = new FoxRunStream<int>(new FoxRunStreamOptions(
                capacity, maxInputHz, maxBatch, FoxRunStreamOverflowPolicy.DropOldest));
            Action<int> dispose = _ => { };
            PerformanceRunner.PrepareAllocMeasurement(out var gcBeforeTotal, out var gcBeforeThread,
                out var gen0Before, out var gen1Before, out var gen2Before);
            var stop = new ManualResetEventSlim(false);
            Exception producerError = null;
            var producer = new Thread(() =>
            {
                try
                {
                    var value = 0;
                    while (!stop.IsSet)
                    {
                        if (stream.TryAdmitInput())
                            stream.TryEnqueueOwnedAfterAdmission(value++, dispose);
                    }
                }
                catch (Exception exception)
                {
                    producerError = exception;
                }
            });
            var stopwatch = Stopwatch.StartNew();
            producer.Start();
            while (stopwatch.ElapsedMilliseconds < durationMs)
            {
                stream.Drain(_ => { });
                Thread.Yield();
            }
            stop.Set();
            producer.Join();
            while (stream.Drain(_ => { }) > 0) { }
            stopwatch.Stop();
            var stats = stream.Stats;
            var queued = stream.Count;
            stream.Dispose();
            stop.Dispose();
            var metrics = PerformanceRunner.CollectAllocMetricSample(
                gcBeforeTotal,
                gcBeforeThread,
                gen0Before,
                gen1Before,
                gen2Before,
                checked((int)Math.Min(int.MaxValue, Math.Max(1L, stats.Admitted))));
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            var ceiling = (long)Math.Ceiling(maxInputHz * elapsedSeconds) + 2;
            var accounting = stats.Received == stats.Admitted + stats.RateDropped
                && stats.Admitted == stats.Drained + stats.Taken + stats.DroppedOldest
                    + stats.DroppedNewest + stats.Cleared + queued;
            var passed = producerError == null
                && stats.Admitted <= ceiling
                && accounting
                && stats.DisposalFailures == 0;
            return new StreamRun
            {
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                MessageCount = checked((int)Math.Min(int.MaxValue, stats.Admitted)),
                Passed = passed,
                Metrics = metrics,
                Notes = $"admitted={stats.Admitted};received={stats.Received};rateDropped={stats.RateDropped};"
                    + $"droppedOldest={stats.DroppedOldest};drained={stats.Drained};ceiling={ceiling}"
            };
        }

        private static PerformanceScenarioResult RunGeneratedJsonFanout(
            int warmup,
            int topics,
            int clients,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry
            {
                Name = "test.FoxRunGeneratedJson",
                Encoding = "jsonschema",
                Content = "{\"type\":\"object\"}"
            });
            var transport = new FakePerformanceTransport();
            var session = new FoxgloveSession("perf-foxrun-json", transport, schemaRegistry: registry);
            var channelIds = RegisterFanoutChannels(session, transport, topics, clients, "json", "test.FoxRunGeneratedJson", "foxrun-json");
            var result = PerformanceRunner.TimedScenario(
                $"FoxRunPublishPayload.Json.{clients}",
                warmup * topics,
                messages * topics,
                () => PublishGeneratedJson(session, channelIds, warmup),
                count => PublishGeneratedJson(session, channelIds, count / topics),
                thresholds);
            result.notes = "generatedJsonWriter=__WriteFoxRunJson_*;publishHelper=PublishFoxRunJsonBytes;"
                + "source=Editor/Shared/FoxgloveSourceEmitter/Decomposed/PublishDispatchEmitter/PublishDispatchEmitter.Json.cs:163-180;"
                + $"clients={clients};topics={topics}";
            return result;
        }

        private static PerformanceScenarioResult RunGeneratedProtoFanout(
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
            var session = new FoxgloveSession("perf-foxrun-proto", transport, schemaRegistry: registry);
            session.EnableProtobuf();
            var channelIds = RegisterFanoutChannels(session, transport, topics, clients, "protobuf", "foxglove.FrameTransform", "foxrun-proto");
            var result = PerformanceRunner.TimedScenario(
                $"FoxRunPublishPayload.Protobuf.{clients}",
                warmup * topics,
                messages * topics,
                () => PublishGeneratedProto(session, channelIds, warmup),
                count => PublishGeneratedProto(session, channelIds, count / topics),
                thresholds);
            result.notes = "generatedProtoWriter=FoxRunProtobufWire;publishHelper=PublishProto;"
                + "source=Editor/Shared/FoxgloveSourceEmitter/ProtobufPublishDispatchEmitter.cs:264-269;"
                + $"clients={clients};topics={topics}";
            return result;
        }

        private static List<uint> RegisterFanoutChannels(
            FoxgloveSession session,
            FakePerformanceTransport transport,
            int topics,
            int clients,
            string encoding,
            string schemaName,
            string topicPrefix)
        {
            var channelIds = new List<uint>();
            for (var topic = 0; topic < topics; topic++)
            {
                var channelId = (uint)(topic + 1);
                if (encoding == "protobuf")
                    session.RegisterProtobufSchemaChannel(channelId, $"/{topicPrefix}/{topic}", schemaName);
                else
                    session.RegisterSchemaChannel(channelId, $"/{topicPrefix}/{topic}", schemaName, encoding);
                channelIds.Add(channelId);
            }
            for (var client = 0; client < clients; client++)
            {
                var clientId = (uint)(client + 1);
                transport.SimulateConnect(clientId);
                for (var topic = 0; topic < topics; topic++)
                    transport.SimulateSubscribe(clientId, (uint)(client * topics + topic + 1), channelIds[topic]);
            }
            return channelIds;
        }

        private static void PublishGeneratedJson(FoxgloveSession session, List<uint> channelIds, int messages)
        {
            for (var message = 0; message < messages; message++)
                for (var topic = 0; topic < channelIds.Count; topic++)
                    session.Publish(channelIds[topic], BuildGeneratedJsonPayload(topic, message));
        }

        private static void PublishGeneratedProto(FoxgloveSession session, List<uint> channelIds, int messages)
        {
            for (var message = 0; message < messages; message++)
                for (var topic = 0; topic < channelIds.Count; topic++)
                    session.Publish(channelIds[topic], BuildGeneratedProtoPayload(topic, message));
        }

        private static byte[] BuildGeneratedJsonPayload(int topic, int message)
        {
            var json = new StringBuilder(128);
            json.Append('{');
            json.Append("\"sequence\":").Append(message);
            json.Append(",\"topic\":").Append(topic);
            json.Append(",\"message\":");
            AppendGeneratedJsonString(json, "perf foxrun payload");
            json.Append('}');
            return Encoding.UTF8.GetBytes(json.ToString());
        }

        private static void AppendGeneratedJsonString(StringBuilder json, string value)
        {
            if (value == null)
            {
                json.Append("null");
                return;
            }
            json.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default: json.Append(character); break;
                }
            }
            json.Append('"');
        }

        private static byte[] BuildGeneratedProtoPayload(int topic, int message)
        {
            var payload = new List<byte>(64);
            FoxRunProtobufWire.WriteInt32(payload, 1, message);
            FoxRunProtobufWire.WriteInt32(payload, 2, topic);
            FoxRunProtobufWire.WriteString(payload, 3, "perf foxrun payload");
            FoxRunProtobufWire.WriteDouble(payload, 4, 1.25);
            return payload.ToArray();
        }

        private static PerformanceScenarioResult RunMessagePackFanout(
            int warmup,
            int topics,
            int clients,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var registry = new DefaultSchemaRegistry();
            registry.Register(new SchemaEntry { Name = "test.PerfMsgPack", Encoding = "msgpack", Content = string.Empty });
            var transport = new FakePerformanceTransport();
            var session = new FoxgloveSession("perf-msgpack", transport, schemaRegistry: registry);
            var channelIds = new List<uint>();
            for (var t = 0; t < topics; t++)
            {
                var channelId = (uint)(t + 1);
                session.RegisterSchemaChannel(channelId, $"/perf/msgpack/{t}", "test.PerfMsgPack", "msgpack", "msgpack");
                channelIds.Add(channelId);
            }
            for (var c = 0; c < clients; c++)
            {
                var clientId = (uint)(c + 1);
                transport.SimulateConnect(clientId);
                for (var t = 0; t < channelIds.Count; t++)
                    transport.SimulateSubscribe(clientId, (uint)(c * topics + t + 1), channelIds[t]);
            }
            var payload = MakeMessagePackPayload();
            var result = PerformanceRunner.TimedScenario(
                $"FoxRunPublishPayload.MessagePack.{clients}",
                warmup * topics,
                messages * topics,
                () =>
                {
                    for (var i = 0; i < warmup; i++)
                    {
                        for (var t = 0; t < channelIds.Count; t++)
                            session.Publish(channelIds[t], payload);
                    }
                },
                count =>
                {
                    var outer = count / topics;
                    for (var i = 0; i < outer; i++)
                    {
                        for (var t = 0; t < channelIds.Count; t++)
                            session.Publish(channelIds[t], payload);
                    }
                },
                thresholds);
            result.notes = $"clients={clients};topics={topics};payloadBytes={payload.Length};generatedMessagePackShape=map";
            return result;
        }

        private static PerformanceScenarioResult RunLargePayloadWebSocket(
            int warmup,
            int messages,
            int clients,
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
            var session = new FoxgloveSession($"perf-large-payload-{clients}", transport, schemaRegistry: registry);
            session.RegisterSchemaChannel(1, "/perf/large", "test.LargePayload", "json");
            for (var c = 0; c < clients; c++)
            {
                var clientId = (uint)(c + 1);
                transport.SimulateConnect(clientId);
                transport.SimulateSubscribe(clientId, clientId, 1);
            }
            var payload = new byte[1024 * 1024];
            var result = PerformanceRunner.TimedScenario($"LargePayloadFanout.WebSocket.{clients}", warmup, messages, () =>
            {
                for (var i = 0; i < warmup; i++)
                    session.Publish(1, payload);
            }, count =>
            {
                for (var i = 0; i < count; i++)
                    session.Publish(1, payload);
            }, null);
            result.notes = $"sink=websocket-fake;clients={clients};payloadBytes={payload.Length};"
                + $"dataFrames={transport.DataBinaryCount}";
            result.bytesPerSecond = result.elapsedMs > 0
                ? result.messageCount * (double)payload.Length / (result.elapsedMs / 1000d)
                : 0;
            return result;
        }

        private static PerformanceScenarioResult RunLargePayloadMcap(
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var payload = new byte[1024 * 1024];
            long outputBytes = 0;
            var result = PerformanceRunner.TimedScenario("LargePayloadFanout.Mcap", messages, messages, () =>
            {
                using var warmupStream = new MemoryStream();
                using var warmupRecorder = new McapRecorder(warmupStream);
                warmupRecorder.AddChannel(1, "/perf/large", "json", "test.LargePayload", "jsonschema", "{\"type\":\"array\"}");
                for (var i = 0; i < messages; i++)
                    warmupRecorder.WriteMessage(1, (ulong)i, payload);
                warmupRecorder.Close();
            }, count =>
            {
                using var output = new MemoryStream();
                using var recorder = new McapRecorder(output);
                recorder.AddChannel(1, "/perf/large", "json", "test.LargePayload", "jsonschema", "{\"type\":\"array\"}");
                for (var i = 0; i < count; i++)
                    recorder.WriteMessage(1, (ulong)i, payload);
                recorder.Close();
                outputBytes = output.Length;
                if (outputBytes <= payload.Length)
                    throw new Exception("Large payload MCAP output did not contain the payload data.");
            }, null);
            result.outputBytes = outputBytes;
            result.bytesPerSecond = result.elapsedMs > 0
                ? outputBytes / (result.elapsedMs / 1000d)
                : 0;
            result.notes = $"sink=mcap;compression=none;payloadBytes={payload.Length};fileBytes={outputBytes}";
            return result;
        }

        private static byte[] MakeMessagePackPayload()
        {
            using var writer = new FoxgloveMsgPackWriter(64);
            writer.WriteMapHeader(3);
            writer.WriteString("sequence");
            writer.WriteInt32(1);
            writer.WriteString("topic");
            writer.WriteString("perf");
            writer.WriteString("value");
            writer.WriteDouble(1.25);
            return writer.ToArray();
        }

        private static PerformanceScenarioResult CombineScenarioResults(
            string name,
            int warmup,
            StreamRun first,
            StreamRun second,
            string notes,
            params PerformanceScenarioResult[] additional)
        {
            var elapsed = first.ElapsedMs + second.ElapsedMs;
            var count = first.MessageCount + second.MessageCount;
            var passed = first.Passed && second.Passed;
            var allocatedTotal = 0L;
            var allocatedThread = 0L;
            var gen0 = first.Metrics.Gen0Collections + second.Metrics.Gen0Collections;
            var gen1 = first.Metrics.Gen1Collections + second.Metrics.Gen1Collections;
            var gen2 = first.Metrics.Gen2Collections + second.Metrics.Gen2Collections;
            allocatedTotal = checked(first.Metrics.AllocatedBytesTotal + second.Metrics.AllocatedBytesTotal);
            allocatedThread = checked(first.Metrics.AllocatedBytesCurrentThread + second.Metrics.AllocatedBytesCurrentThread);
            foreach (var result in additional ?? Array.Empty<PerformanceScenarioResult>())
            {
                elapsed += result.elapsedMs;
                count = checked(count + result.messageCount);
                passed &= result.passed;
                allocatedTotal = checked(allocatedTotal + result.allocatedBytesTotal);
                allocatedThread = checked(allocatedThread + result.allocatedBytesCurrentThread);
                gen0 += result.gen0Collections;
                gen1 += result.gen1Collections;
                gen2 += result.gen2Collections;
            }
            return new PerformanceScenarioResult
            {
                name = name,
                warmupMessageCount = warmup,
                messageCount = count,
                elapsedMs = elapsed,
                messagesPerSecond = elapsed > 0 ? count / (elapsed / 1000d) : count,
                allocatedBytesTotal = allocatedTotal,
                allocatedBytesCurrentThread = allocatedThread,
                allocatedBytesPerMessage = count > 0 ? (double)allocatedTotal / count : 0,
                gen0Collections = gen0,
                gen1Collections = gen1,
                gen2Collections = gen2,
                passed = passed,
                notes = notes
            };
        }

        private static PerformanceScenarioResult CombineScenarioResults(
            string name,
            int warmup,
            PerformanceScenarioResult first,
            PerformanceScenarioResult second,
            string notes,
            params PerformanceScenarioResult[] additional)
        {
            var results = new List<PerformanceScenarioResult> { first, second };
            if (additional != null)
                results.AddRange(additional);

            var elapsed = 0L;
            var count = 0;
            var allocatedTotal = 0L;
            var allocatedThread = 0L;
            var gen0 = 0;
            var gen1 = 0;
            var gen2 = 0;
            var outputBytes = 0L;
            var passed = true;
            foreach (var result in results)
            {
                elapsed = checked(elapsed + result.elapsedMs);
                count = checked(count + result.messageCount);
                allocatedTotal = checked(allocatedTotal + result.allocatedBytesTotal);
                allocatedThread = checked(allocatedThread + result.allocatedBytesCurrentThread);
                gen0 += result.gen0Collections;
                gen1 += result.gen1Collections;
                gen2 += result.gen2Collections;
                outputBytes = checked(outputBytes + result.outputBytes);
                passed &= result.passed;
            }

            return new PerformanceScenarioResult
            {
                name = name,
                warmupMessageCount = warmup,
                messageCount = count,
                elapsedMs = elapsed,
                messagesPerSecond = elapsed > 0 ? count / (elapsed / 1000d) : count,
                allocatedBytesTotal = allocatedTotal,
                allocatedBytesCurrentThread = allocatedThread,
                allocatedBytesPerMessage = count > 0 ? (double)allocatedTotal / count : 0,
                gen0Collections = gen0,
                gen1Collections = gen1,
                gen2Collections = gen2,
                outputBytes = outputBytes,
                passed = passed,
                notes = notes
            };
        }

        private sealed class StreamRun
        {
            public long ElapsedMs;
            public int MessageCount;
            public bool Passed;
            public PerformanceRunner.AllocationMetricSample Metrics;
            public string Notes;
        }
    }
}
