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
