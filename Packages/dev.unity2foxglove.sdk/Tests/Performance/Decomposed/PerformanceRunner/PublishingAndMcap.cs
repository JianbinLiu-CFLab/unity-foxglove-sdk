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

        private static PerformanceScenarioResult RunPublishJsonFanout(
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
            }, thresholds);
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

        private static PerformanceScenarioResult RunMcapRecord(
            int warmup,
            int topics,
            int messages,
            string compression,
            string name,
            PerformanceThresholdConfig thresholds)
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
                using var ms = new MemoryStream(EstimateMcapRecordCapacity(topics, outer));
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
            }, thresholds);

            result.outputBytes = outputBytes;
            if (outputBytes > 0 && payloadBytes > 0)
            {
                var compressionLabel = string.IsNullOrEmpty(compression) ? "none" : compression;
                var ratio = (double)payloadBytes / outputBytes;
                result.notes = $"compression={compressionLabel}; payloadBytes={payloadBytes}; fileBytes={outputBytes}; payloadToFileRatio={ratio:F3}";
            }

            return result;
        }

        private static int EstimateMcapRecordCapacity(int topics, int messages)
        {
            var estimate = 4096L + (long)Math.Max(1, topics) * 512L
                + (long)Math.Max(1, topics) * Math.Max(1, messages) * 128L;
            return estimate > int.MaxValue ? int.MaxValue : (int)estimate;
        }

        private static PerformanceScenarioResult RunMcapRecordNonePrebuiltPayload(
            int warmup,
            int topics,
            int messages,
            PerformanceThresholdConfig thresholds)
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
                using var ms = new MemoryStream();
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
            }, thresholds);

            result.outputBytes = outputBytes;
            result.notes = $"compression=none; prebuiltPayload=true; payloadBytes={payloadBytes}; fileBytes={outputBytes}; payloadBytesPerMessage={payload.Length}";
            return result;
        }

        private static PerformanceScenarioResult RunMcapReplayTick(
            int warmup,
            int topics,
            int messages,
            bool isFull,
            PerformanceThresholdConfig thresholds)
        {
            var fixtureDir = Path.Combine(RepoRoot, "build", "performance", "fixtures");
            Directory.CreateDirectory(fixtureDir);
            var fixturePath = Path.Combine(
                fixtureDir,
                $"phase35_replay_fixture_v1_{(isFull ? "full" : "quick")}_{topics}_{messages}.mcap");

            if (!File.Exists(fixturePath) || new FileInfo(fixturePath).Length == 0)
            {
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
            }, thresholds);

            result.notes = $"fixturePath={fixturePath}";
            return result;
        }

        private static PerformanceScenarioResult RunMcapRecordAttachmentSummary(PerformanceThresholdConfig thresholds)
        {
            try
            {
                PrepareAllocMeasurement(out var gcBeforeTotal, out var gcBeforeThread,
                    out var gen0Before, out var gen1Before, out var gen2Before);
                var sw = Stopwatch.StartNew();
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

                // Verify Footer summary_crc is non-zero using the MCAP footer layout constants.
                var rawBytes = ms.ToArray();
                var footerIdx = rawBytes.Length
                                - McapWriter.MagicLength
                                - McapWriter.RecordHeaderLength
                                - McapWriter.FooterContentLength;
                var summaryCrcOffset = footerIdx
                                       + McapWriter.RecordHeaderLength
                                       + McapWriter.FooterContentLength
                                       - McapWriter.Crc32SizeBytes;
                var footerCrc = BitConverter.ToUInt32(rawBytes, summaryCrcOffset);
                if (footerCrc == 0)
                    throw new Exception("Footer summary_crc is zero");

                sw.Stop();
                var elapsedMs = ResolveElapsedMilliseconds(sw.ElapsedMilliseconds);
                var metrics = CollectAllocMetricSample(
                    gcBeforeTotal,
                    gcBeforeThread,
                    gen0Before,
                    gen1Before,
                    gen2Before,
                    1);
                var elapsedSeconds = elapsedMs / 1000.0;
                var result = new PerformanceScenarioResult
                {
                    name = "McapRecordAttachmentSummary",
                    warmupMessageCount = 0,
                    messageCount = 1,
                    elapsedMs = elapsedMs,
                    messagesPerSecond = elapsedSeconds > 0 ? 1 / elapsedSeconds : 0,
                    allocatedBytesTotal = metrics.AllocatedBytesTotal,
                    allocatedBytesCurrentThread = metrics.AllocatedBytesCurrentThread,
                    allocatedBytesPerMessage = metrics.AllocatedBytesPerMessage,
                    gen0Collections = metrics.Gen0Collections,
                    gen1Collections = metrics.Gen1Collections,
                    gen2Collections = metrics.Gen2Collections,
                    allocationNotes = metrics.AllocationNotes,
                    passed = true,
                    outputBytes = ms.Length,
                    notes = "Phase 34 regression guard: attachment index, CRC, summary_crc verified"
                };
                ApplyThresholds(result, thresholds);
                return result;
            }
            catch (Exception ex)
            {
                var result = new PerformanceScenarioResult
                {
                    name = "McapRecordAttachmentSummary",
                    passed = false,
                    notes = ex.Message
                };
                ApplyThresholds(result, thresholds);
                return result;
            }
        }

    }
}
