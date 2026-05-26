// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-1 runtime facade and lifecycle hardening.

using System;
using System.IO;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_1Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-1: Runtime facade and lifecycle hardening ===");
            _passed = 0;

            TestBoundedEventQueueRejectsOverflow();
            VerifyFoxgloveManagerUsesBoundedClientEventQueue();
            VerifyClientEventQueueOverflowWarning();

            Console.WriteLine($"Phase 134-1: {_passed} checks passed.");
        }

        private static void TestBoundedEventQueueRejectsOverflow()
        {
            var queue = new BoundedEventQueue<byte[]>(maxFrames: 3, maxBytes: 10, measureBytes: payload => payload?.Length ?? 0);

            Check(queue.TryEnqueue(new byte[3], out _), "134-1A-1: queue accepts first payload");
            Check(queue.TryEnqueue(new byte[3], out _), "134-1A-2: queue accepts second payload");
            Check(queue.TryEnqueue(new byte[3], out _), "134-1A-3: queue accepts third payload within frame and byte budgets");
            Check(queue.Count == 3 && queue.QueuedBytes == 9, "134-1A-4: queue tracks frame count and payload bytes");
            Check(!queue.TryEnqueue(new byte[1], out var frameOverflow)
                  && frameOverflow.QueuedFrames == 3
                  && frameOverflow.QueuedBytes == 9
                  && frameOverflow.RejectedBytes == 1,
                "134-1A-5: queue rejects frame-count overflow without retaining payload");
            Check(queue.TryDequeue(out var first) && first.Length == 3, "134-1A-6: queue drains FIFO items");
            Check(queue.Count == 2 && queue.QueuedBytes == 6, "134-1A-7: queue subtracts payload bytes after drain");
            Check(!queue.TryEnqueue(new byte[5], out var byteOverflow)
                  && byteOverflow.QueuedFrames == 2
                  && byteOverflow.QueuedBytes == 6
                  && byteOverflow.RejectedBytes == 5,
                "134-1A-8: queue rejects byte-budget overflow");
            Check(queue.TryEnqueue(new byte[4], out _), "134-1A-9: queue still accepts payloads that exactly fill byte budget");
            queue.Clear();
            Check(queue.Count == 0 && queue.QueuedBytes == 0, "134-1A-10: queue clear releases retained payload accounting");

            var unlimitedBytes = new BoundedEventQueue<byte[]>(maxFrames: 1, maxBytes: 0, measureBytes: payload => payload?.Length ?? 0);
            Check(unlimitedBytes.TryEnqueue(new byte[1024], out _),
                "134-1A-11: zero byte budget disables byte-budget rejection while retaining frame cap");

            var mutable = new MutablePayload { Size = 4 };
            var measuredOnce = new BoundedEventQueue<MutablePayload>(maxFrames: 2, maxBytes: 10, measureBytes: payload => payload?.Size ?? 0);
            Check(measuredOnce.TryEnqueue(mutable, out _) && measuredOnce.QueuedBytes == 4,
                "134-1A-12: queue records byte size at enqueue time");
            mutable.Size = 100;
            Check(measuredOnce.TryDequeue(out _) && measuredOnce.QueuedBytes == 0,
                "134-1A-13: post-enqueue mutation does not skew queued byte accounting");
            Check(measuredOnce.DroppedCount == 0 && measuredOnce.DroppedBytes == 0,
                "134-1A-14: queue exposes cumulative drop counters");
        }

        private static void VerifyFoxgloveManagerUsesBoundedClientEventQueue()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(manager.Contains("MaxQueuedClientLifecycleEvents", StringComparison.Ordinal)
                  && manager.Contains("MaxQueuedClientEvents", StringComparison.Ordinal)
                  && manager.Contains("MaxQueuedClientEventPayloadBytes", StringComparison.Ordinal)
                  && manager.Contains("_clientLifecycleEvents", StringComparison.Ordinal)
                  && manager.Contains("_clientMessageEvents", StringComparison.Ordinal)
                  && manager.Contains("BoundedEventQueue<ClientEvent>", StringComparison.Ordinal),
                "134-1B-1: FoxgloveManager declares separate bounded lifecycle and message event queue budgets");
            Check(!manager.Contains("ConcurrentQueue<ClientEvent>", StringComparison.Ordinal),
                "134-1B-2: FoxgloveManager no longer uses an unbounded ConcurrentQueue for client events");
            Check(manager.Contains("EnqueueClientLifecycleEvent(new ClientEvent", StringComparison.Ordinal)
                  && manager.Contains("EnqueueClientMessageEvent(ClientEvent evt)", StringComparison.Ordinal)
                  && server.Contains("EnqueueClientMessageEvent(new ClientEvent", StringComparison.Ordinal),
                "134-1B-3: connect/disconnect lifecycle events are bounded separately from message events");
            Check(manager.Contains("evt.IsMessage ? evt.Payload?.Length ?? 0 : 0", StringComparison.Ordinal),
                "134-1B-4: only message payload bytes count against the byte budget");
        }

        private static void VerifyClientEventQueueOverflowWarning()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs");

            Check(manager.Contains("WarnClientEventQueueOverflow", StringComparison.Ordinal)
                  && manager.Contains("ClientEventOverflowWarningIntervalTicks", StringComparison.Ordinal)
                  && manager.Contains("Interlocked.CompareExchange", StringComparison.Ordinal),
                "134-1C-1: overflow warning is throttled across transport threads");
            Check(manager.Contains("droppedEvents=", StringComparison.Ordinal)
                  && manager.Contains("droppedPayloadBytes=", StringComparison.Ordinal)
                  && manager.Contains("queuedPayloadBytes=", StringComparison.Ordinal),
                "134-1C-2: overflow warning includes retained and dropped queue diagnostics");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private sealed class MutablePayload
        {
            public int Size;
        }
    }
}
