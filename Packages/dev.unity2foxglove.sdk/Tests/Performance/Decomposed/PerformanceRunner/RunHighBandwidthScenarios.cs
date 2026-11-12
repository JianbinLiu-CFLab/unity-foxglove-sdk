// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Foxglove;
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
        private static PerformanceComponentEvidence Component(PerformanceScenarioResult result)
        {
            return new PerformanceComponentEvidence
            {
                name = result.name,
                messageCount = result.messageCount,
                elapsedMs = result.elapsedMs,
                elapsedSeconds = result.elapsedSeconds,
                messagesPerSecond = result.messagesPerSecond,
                bytesPerSecond = result.bytesPerSecond,
                publishBytesPerSecond = result.publishBytesPerSecond,
                egressBytes = result.egressBytes,
                allocatedBytesTotal = result.allocatedBytesTotal,
                allocatedBytesCurrentThread = result.allocatedBytesCurrentThread,
                allocatedBytesPerMessage = result.allocatedBytesPerMessage,
                gen0Collections = result.gen0Collections,
                gen1Collections = result.gen1Collections,
                gen2Collections = result.gen2Collections,
                allocationNotes = result.allocationNotes,
                outputBytes = result.outputBytes,
                acceptedBytes = result.acceptedBytes,
                droppedBytes = result.droppedBytes,
                clientCount = result.clientCount,
                thresholdsEvaluated = result.thresholdsEvaluated,
                thresholdNotes = result.thresholdNotes,
                passed = result.passed,
                dropRatio = result.dropRatio,
                queuedBytesHighWater = result.queuedBytesHighWater,
                notes = result.notes,
                helperProvenance = result.helperProvenance
            };
        }

        private static double RateBytes(PerformanceScenarioResult result, int bytesPerMessage)
        {
            return result.elapsedSeconds > 0
                ? result.messageCount * (double)bytesPerMessage / result.elapsedSeconds
                : 0;
        }

        internal static PerformanceScenarioResult RunFoxRunStreamIngest(
            int warmup,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            RunStreamVariant(1000, 10, 1024, 128);
            var defaultRun = RunStreamVariant(1000, messages <= 2000 ? 50 : 100, 1024, 128);
            var unboundedRun = RunStreamVariant(1_000_000_000, messages <= 2000 ? 15 : 30, 4096, 256);
            var defaultComponent = StreamComponentResult("FoxRunStreamIngest.Default", defaultRun);
            var unboundedComponent = StreamComponentResult("FoxRunStreamIngest.Unbounded", unboundedRun);
            PerformanceRunner.ApplyThresholds(defaultComponent, thresholds);
            PerformanceRunner.ApplyThresholds(unboundedComponent, thresholds);
            var result = CombineScenarioResults(
                "FoxRunStreamIngest",
                warmup,
                defaultRun,
                unboundedRun,
                "Default and Unbounded producer-thread admission/drain variants; "
                    + $"default={defaultRun.Notes}; unbounded={unboundedRun.Notes}");
            result.components = new List<PerformanceComponentEvidence>
            {
                Component(defaultComponent),
                Component(unboundedComponent)
            };
            result.passed &= defaultComponent.passed && unboundedComponent.passed;
            PerformanceRunner.ApplyThresholds(result, thresholds);
            return result;
        }

        internal static PerformanceScenarioResult RunFoxRunPublishPayload(
            int warmup,
            int topics,
            int messages,
            PerformanceThresholdConfig thresholds)
        {
            var jsonOne = RunJsonWriterShapeFanout(warmup, topics, 1, messages, thresholds);
            var jsonFour = RunJsonWriterShapeFanout(warmup, topics, 4, messages, thresholds);
            var protoOne = RunFrameTransformFanout(warmup, topics, 1, messages, thresholds);
            var protoFour = RunFrameTransformFanout(warmup, topics, 4, messages, thresholds);
            var messagePackOne = RunMessagePackFanout(warmup, topics, 1, messages, thresholds);
            var messagePackFour = RunMessagePackFanout(warmup, topics, 4, messages, thresholds);
            var result = CombineScenarioResults(
                "FoxRunPublishPayload",
                warmup,
                jsonOne,
                jsonFour,
                "Option A' JSON writer-shape and FrameTransform Protobuf building blocks one/four-client fanout; "
                    + $"MessagePack one/four-client: {messagePackOne.notes}; {messagePackFour.notes}",
                protoOne,
                protoFour,
                messagePackOne,
                messagePackFour);
            result.components = new List<PerformanceComponentEvidence>
            {
                Component(jsonOne),
                Component(jsonFour),
                Component(protoOne),
                Component(protoFour),
                Component(messagePackOne),
                Component(messagePackFour)
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
            result.passed &= componentFailures.Count == 0;
            var componentNotes = result.thresholdNotes;
            PerformanceRunner.ApplyThresholds(result, thresholds);
            if (!string.IsNullOrEmpty(componentNotes))
            {
                result.thresholdNotes = string.IsNullOrEmpty(result.thresholdNotes)
                    ? componentNotes
                    : componentNotes + "; aggregate=" + result.thresholdNotes;
            }
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
                droppedForSize += control.DroppedDataFrames;
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
                    droppedDataFramesSnapshot = queue.DroppedDataFramesSnapshot,
                    queuedBytes = queue.QueuedBytes,
                    controlDequeuedFirst = controlDequeuedFirst,
                    acceptedBytes = acceptedForFrame,
                    droppedBytes = droppedForFrame,
                    queuedBytesHighWater = highWaterForSize,
                    dropRatio = acceptedForSize + droppedForSize > 0
                        ? (double)droppedForSize / (acceptedForSize + droppedForSize)
                        : 0
                });
                passed &= droppedForSize == queue.DroppedDataFramesSnapshot;
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
                elapsedMs = Math.Max(1L, sw.ElapsedMilliseconds),
                elapsedSeconds = sw.Elapsed.TotalSeconds,
                messagesPerSecond = sw.Elapsed.TotalSeconds > 0 ? operations / sw.Elapsed.TotalSeconds : operations,
                bytesPerSecond = sw.Elapsed.TotalSeconds > 0 ? acceptedBytes / sw.Elapsed.TotalSeconds : acceptedBytes,
                clientCount = 1,
                helperProvenance = "WsSendQueue.Enqueue/TryDequeue",
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
                Component(oneClient),
                Component(fourClients),
                Component(mcap)
            };
            PerformanceRunner.ApplyThresholds(result, thresholds);
            return result;
        }

        private static PerformanceScenarioResult StreamComponentResult(string name, StreamRun run)
        {
            var elapsedSeconds = run.ElapsedSeconds;
            return new PerformanceScenarioResult
            {
                name = name,
                messageCount = run.MessageCount,
                elapsedMs = run.ElapsedMs,
                elapsedSeconds = elapsedSeconds,
                messagesPerSecond = elapsedSeconds > 0 ? run.MessageCount / elapsedSeconds : run.MessageCount,
                allocatedBytesTotal = run.Metrics.AllocatedBytesTotal,
                allocatedBytesCurrentThread = run.Metrics.AllocatedBytesCurrentThread,
                allocatedBytesPerMessage = run.Metrics.AllocatedBytesPerMessage,
                gen0Collections = run.Metrics.Gen0Collections,
                gen1Collections = run.Metrics.Gen1Collections,
                gen2Collections = run.Metrics.Gen2Collections,
                allocationNotes = run.Metrics.AllocationNotes,
                helperProvenance = "FoxRunStream.TryAdmitInput + TryEnqueueOwnedAfterAdmission + Drain",
                passed = run.Passed,
                notes = run.Notes
            };
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
                ElapsedMs = Math.Max(1L, stopwatch.ElapsedMilliseconds),
                ElapsedSeconds = elapsedSeconds,
                MessageCount = checked((int)Math.Min(int.MaxValue, stats.Admitted)),
                Passed = passed,
                Metrics = metrics,
                Notes = $"admitted={stats.Admitted};received={stats.Received};rateDropped={stats.RateDropped};"
                    + $"droppedOldest={stats.DroppedOldest};drained={stats.Drained};ceiling={ceiling}"
            };
        }

        private static PerformanceScenarioResult RunJsonWriterShapeFanout(
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
                () =>
                {
                    PublishJsonWriterShape(session, channelIds, warmup);
                    transport.ResetCounters();
                },
                count => PublishJsonWriterShape(session, channelIds, count / topics),
                thresholds);
            result.helperProvenance = "FoxRunJsonStringBuilderShape;source=Editor/Shared/FoxgloveSourceEmitter/Decomposed/PublishDispatchEmitter/PublishDispatchEmitter.Json.cs:163-180;scope=option-A'-building-block";
            result.notes = "benchmarkPath=session.Publish pre-serialized payload;generated-member-read=not-run;manager-helper=not-run;"
                + $"clients={clients};topics={topics}";
            result.clientCount = clients;
            result.egressBytes = transport.DataBinaryBytes;
            result.bytesPerSecond = result.elapsedSeconds > 0 ? result.egressBytes / result.elapsedSeconds : 0;
            result.publishBytesPerSecond = RateBytes(result, BuildJsonWriterShapePayload(0, 0).Length);
            return result;
        }

        private static PerformanceScenarioResult RunFrameTransformFanout(
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
                () =>
                {
                    PublishFrameTransform(session, channelIds, warmup);
                    transport.ResetCounters();
                },
                count => PublishFrameTransform(session, channelIds, count / topics),
                thresholds);
            result.helperProvenance = "FoxgloveFrameTransformProtobufShape;source=Editor/Shared/FoxgloveSourceEmitter/ProtobufPublishDispatchEmitter.cs:264-269;scope=option-A'-building-block";
            result.notes = "benchmarkPath=session.Publish pre-serialized payload;generated-member-read=not-run;manager-helper=not-run;"
                + $"clients={clients};topics={topics}";
            result.clientCount = clients;
            result.egressBytes = transport.DataBinaryBytes;
            result.bytesPerSecond = result.elapsedSeconds > 0 ? result.egressBytes / result.elapsedSeconds : 0;
            result.publishBytesPerSecond = RateBytes(result, BuildFrameTransformPayload(0, 0).Length);
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

        private static void PublishJsonWriterShape(FoxgloveSession session, List<uint> channelIds, int messages)
        {
            for (var message = 0; message < messages; message++)
                for (var topic = 0; topic < channelIds.Count; topic++)
                    session.Publish(channelIds[topic], BuildJsonWriterShapePayload(topic, message));
        }

        private static void PublishFrameTransform(FoxgloveSession session, List<uint> channelIds, int messages)
        {
            for (var message = 0; message < messages; message++)
                for (var topic = 0; topic < channelIds.Count; topic++)
                    session.Publish(channelIds[topic], BuildFrameTransformPayload(topic, message));
        }

        private static byte[] BuildJsonWriterShapePayload(int topic, int message)
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

        private static byte[] BuildFrameTransformPayload(int topic, int message)
        {
            var payload = new List<byte>(64);
            var timestamp = new Timestamp { Seconds = 1, Nanos = message };
            var translation = new Vector3 { X = topic, Y = 1, Z = 2 };
            var rotation = new Quaternion { X = 0, Y = 0, Z = 0, W = 1 };
            FoxRunProtobufWire.WriteBytes(payload, 1, timestamp.ToByteArray());
            FoxRunProtobufWire.WriteString(payload, 2, $"parent-{topic}");
            FoxRunProtobufWire.WriteString(payload, 3, "child");
            FoxRunProtobufWire.WriteBytes(payload, 4, EncodeVector3(translation));
            FoxRunProtobufWire.WriteBytes(payload, 5, EncodeQuaternion(rotation));
            return payload.ToArray();
        }

        private static byte[] EncodeVector3(Vector3 value)
        {
            var payload = new List<byte>(16);
            FoxRunProtobufWire.WriteDouble(payload, 1, value.X);
            FoxRunProtobufWire.WriteDouble(payload, 2, value.Y);
            FoxRunProtobufWire.WriteDouble(payload, 3, value.Z);
            return payload.ToArray();
        }

        private static byte[] EncodeQuaternion(Quaternion value)
        {
            var payload = new List<byte>(20);
            FoxRunProtobufWire.WriteDouble(payload, 1, value.X);
            FoxRunProtobufWire.WriteDouble(payload, 2, value.Y);
            FoxRunProtobufWire.WriteDouble(payload, 3, value.Z);
            FoxRunProtobufWire.WriteDouble(payload, 4, value.W);
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
                    transport.ResetCounters();
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
            result.clientCount = clients;
            result.egressBytes = transport.DataBinaryBytes;
            result.bytesPerSecond = result.elapsedSeconds > 0 ? result.egressBytes / result.elapsedSeconds : 0;
            result.publishBytesPerSecond = RateBytes(result, payload.Length);
            result.helperProvenance = "FoxgloveMsgPackWriter;FoxgloveManager.PublishFoxRunMessagePackBytes source shape";
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
                transport.ResetCounters();
            }, count =>
            {
                for (var i = 0; i < count; i++)
                    session.Publish(1, payload);
            }, thresholds);
            result.notes = $"sink=websocket-fake;clients={clients};payloadBytes={payload.Length};"
                + $"dataFrames={transport.DataBinaryCount}";
            result.clientCount = clients;
            result.helperProvenance = "FoxgloveSession.Publish with FakePerformanceTransport";
            result.egressBytes = transport.DataBinaryBytes;
            result.bytesPerSecond = result.elapsedSeconds > 0 ? result.egressBytes / result.elapsedSeconds : 0;
            result.publishBytesPerSecond = RateBytes(result, payload.Length);
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
            }, thresholds);
            result.outputBytes = outputBytes;
            result.clientCount = 1;
            result.helperProvenance = "McapRecorder.WriteMessage compression=none";
            result.bytesPerSecond = result.elapsedSeconds > 0
                ? outputBytes / result.elapsedSeconds
                : 0;
            result.publishBytesPerSecond = result.bytesPerSecond;
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
            var elapsedSeconds = first.ElapsedSeconds + second.ElapsedSeconds;
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
                elapsedSeconds += result.elapsedSeconds;
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
                elapsedSeconds = elapsedSeconds,
                messagesPerSecond = elapsedSeconds > 0 ? count / elapsedSeconds : count,
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
            var elapsedSeconds = 0d;
            var count = 0;
            var allocatedTotal = 0L;
            var allocatedThread = 0L;
            var gen0 = 0;
            var gen1 = 0;
            var gen2 = 0;
            var outputBytes = 0L;
            var egressBytes = 0L;
            var acceptedBytes = 0L;
            var droppedBytes = 0L;
            var queuedBytesHighWater = 0L;
            var publishBytes = 0d;
            var passed = true;
            foreach (var result in results)
            {
                elapsed = checked(elapsed + result.elapsedMs);
                elapsedSeconds += result.elapsedSeconds;
                count = checked(count + result.messageCount);
                allocatedTotal = checked(allocatedTotal + result.allocatedBytesTotal);
                allocatedThread = checked(allocatedThread + result.allocatedBytesCurrentThread);
                gen0 += result.gen0Collections;
                gen1 += result.gen1Collections;
                gen2 += result.gen2Collections;
                outputBytes = checked(outputBytes + result.outputBytes);
                egressBytes = checked(egressBytes + result.egressBytes);
                acceptedBytes = checked(acceptedBytes + result.acceptedBytes);
                droppedBytes = checked(droppedBytes + result.droppedBytes);
                queuedBytesHighWater = Math.Max(queuedBytesHighWater, result.queuedBytesHighWater);
                publishBytes += result.publishBytesPerSecond * result.elapsedSeconds;
                passed &= result.passed;
            }

            return new PerformanceScenarioResult
            {
                name = name,
                warmupMessageCount = warmup,
                messageCount = count,
                elapsedMs = elapsed,
                elapsedSeconds = elapsedSeconds,
                messagesPerSecond = elapsedSeconds > 0 ? count / elapsedSeconds : count,
                bytesPerSecond = elapsedSeconds > 0 ? egressBytes / elapsedSeconds : egressBytes,
                publishBytesPerSecond = elapsedSeconds > 0 ? publishBytes / elapsedSeconds : publishBytes,
                egressBytes = egressBytes,
                allocatedBytesTotal = allocatedTotal,
                allocatedBytesCurrentThread = allocatedThread,
                allocatedBytesPerMessage = count > 0 ? (double)allocatedTotal / count : 0,
                gen0Collections = gen0,
                gen1Collections = gen1,
                gen2Collections = gen2,
                outputBytes = outputBytes,
                acceptedBytes = acceptedBytes,
                droppedBytes = droppedBytes,
                queuedBytesHighWater = queuedBytesHighWater,
                dropRatio = acceptedBytes + droppedBytes > 0
                    ? (double)droppedBytes / (acceptedBytes + droppedBytes)
                    : 0,
                passed = passed,
                notes = notes
            };
        }

        private sealed class StreamRun
        {
            public long ElapsedMs;
            public double ElapsedSeconds;
            public int MessageCount;
            public bool Passed;
            public PerformanceRunner.AllocationMetricSample Metrics;
            public string Notes;
        }
    }
}
