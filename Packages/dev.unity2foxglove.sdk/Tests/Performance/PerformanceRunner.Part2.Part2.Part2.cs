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

        private static PerformanceScenarioResult RunCameraBackpressurePolicyMicro(PerformanceThresholdConfig thresholds)
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
            var metrics = CollectAllocMetricSample(
                gcBeforeTotal,
                gcBeforeThread,
                gen0Before,
                gen1Before,
                gen2Before,
                iterations);

            var passed = !invalidState
                && pressureCount > 0
                && blockedCount > 0
                && allowedCount > 0
                && r.NextDropCount == currentDrop;

            var result = new PerformanceScenarioResult
            {
                name = "CameraBackpressurePolicyMicro",
                warmupMessageCount = 0,
                messageCount = iterations,
                elapsedMs = sw.ElapsedMilliseconds,
                messagesPerSecond = sw.Elapsed.TotalSeconds > 0 ? iterations / sw.Elapsed.TotalSeconds : 0,
                allocatedBytesTotal = metrics.AllocatedBytesTotal,
                allocatedBytesCurrentThread = metrics.AllocatedBytesCurrentThread,
                allocatedBytesPerMessage = metrics.AllocatedBytesPerMessage,
                gen0Collections = metrics.Gen0Collections,
                gen1Collections = metrics.Gen1Collections,
                gen2Collections = metrics.Gen2Collections,
                allocationNotes = metrics.AllocationNotes,
                notes = "policy evaluation loop; 100K iterations; no Unity/GPU; "
                    + $"pressure={pressureCount}, blocked={blockedCount}, allowed={allowedCount}",
                passed = passed
            };
            ApplyThresholds(result, thresholds);
            return result;
        }

        private static PerformanceScenarioResult RunTransportQueueMicro(PerformanceThresholdConfig thresholds)
        {
            bool accepted, staysConnected, dataDropped;
            const int queueOperationCount = 15;

            PrepareAllocMeasurement(out var gcBeforeTotal, out var gcBeforeThread,
                out var gen0Before, out var gen1Before, out var gen2Before);
            var sw = Stopwatch.StartNew();

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
            staysConnected = !er.ShouldDisconnect;
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

            bool passed = accepted && dataDropped && staysConnected && controlFirst && ctrlDisc && completed && drained;
            sw.Stop();
            var metrics = CollectAllocMetricSample(
                gcBeforeTotal,
                gcBeforeThread,
                gen0Before,
                gen1Before,
                gen2Before,
                queueOperationCount);

            var result = new PerformanceScenarioResult
            {
                name = "TransportQueueMicro",
                warmupMessageCount = 0,
                messageCount = queueOperationCount,
                elapsedMs = sw.ElapsedMilliseconds,
                messagesPerSecond = sw.Elapsed.TotalSeconds > 0 ? queueOperationCount / sw.Elapsed.TotalSeconds : queueOperationCount,
                allocatedBytesTotal = metrics.AllocatedBytesTotal,
                allocatedBytesCurrentThread = metrics.AllocatedBytesCurrentThread,
                allocatedBytesPerMessage = metrics.AllocatedBytesPerMessage,
                gen0Collections = metrics.Gen0Collections,
                gen1Collections = metrics.Gen1Collections,
                gen2Collections = metrics.Gen2Collections,
                allocationNotes = metrics.AllocationNotes,
                passed = passed,
                notes = passed ? "Queue enqueue/drop/control/complete paths exercised" : "Queue scenario failed"
            };
            ApplyThresholds(result, thresholds);
            return result;

            static QueuedFrame D(byte b) =>
                new QueuedFrame(WsOpcode.Binary, new[] { b }, FramePriority.Data);
            static QueuedFrame C(byte b) =>
                new QueuedFrame(WsOpcode.Text, new[] { b }, FramePriority.Control);
        }
    }
}
