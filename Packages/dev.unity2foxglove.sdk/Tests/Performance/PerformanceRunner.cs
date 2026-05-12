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

        // Scenarios

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
            var q = new ManagedWsBackend.WsSendQueue(maxFrames: 4, maxQueuedBytes: 1024 * 1024);
            for (int i = 0; i < 4; i++)
                q.Enqueue(D(1));
            var er = q.Enqueue(D(1));
            accepted = er.Accepted;
            dataDropped = er.DroppedDataFrames > 0;

            // Control priority over data
            var q2 = new ManagedWsBackend.WsSendQueue(maxFrames: 3, maxQueuedBytes: 1024 * 1024);
            q2.Enqueue(D(1)); q2.Enqueue(D(1)); q2.Enqueue(D(1));
            er = q2.Enqueue(C(1));
            shouldDisc = !er.ShouldDisconnect;
            q2.TryDequeue(out var first);
            bool controlFirst = first.Priority == ManagedWsBackend.FramePriority.Control;

            // Control-only overflow disconnects
            var q3 = new ManagedWsBackend.WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024 * 1024);
            q3.Enqueue(C(1)); q3.Enqueue(C(1));
            er = q3.Enqueue(C(1));
            bool ctrlDisc = !er.Accepted && er.ShouldDisconnect;

            // Drain after complete
            var q4 = new ManagedWsBackend.WsSendQueue(maxFrames: 2, maxQueuedBytes: 1024 * 1024);
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

            static ManagedWsBackend.QueuedFrame D(byte b) =>
                new ManagedWsBackend.QueuedFrame(WsOpcode.Binary, new[] { b }, ManagedWsBackend.FramePriority.Data);
            static ManagedWsBackend.QueuedFrame C(byte b) =>
                new ManagedWsBackend.QueuedFrame(WsOpcode.Text, new[] { b }, ManagedWsBackend.FramePriority.Control);
        }
    }
}
