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
            TestBoundedEventQueueUsesEnqueuedByteSnapshot();
            TestBoundedEventQueueZeroByteBudgetMeansUnlimitedBytes();
            VerifyFoxgloveManagerUsesBoundedClientEventQueue();
            VerifyClientEventQueueOverflowWarning();
            VerifyReplayEmptyPathRestoresLivePublishers();
            VerifyManagerInspectorAndRuntimeBounds();
            VerifyRos2BridgeWarningThrottling();
            VerifyLoggerSeverityPrefixes();
            VerifyRecordingAndAssetBudgetHardening();
            VerifySerializedSecretsAreNotCommitted();

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
            Check(queue.DroppedCount == 2 && queue.DroppedBytes == 6,
                "134-1A-11: queue exposes cumulative drop counters for monitoring");
        }

        private static void TestBoundedEventQueueUsesEnqueuedByteSnapshot()
        {
            var first = new MutablePayload { Size = 4 };
            var second = new MutablePayload { Size = 4 };
            var queue = new BoundedEventQueue<MutablePayload>(maxFrames: 3, maxBytes: 10, measureBytes: payload => payload.Size);

            Check(queue.TryEnqueue(first, out _), "134-1D-1: mutable payload queue accepts first item");
            Check(queue.TryEnqueue(second, out _), "134-1D-2: mutable payload queue accepts second item");
            first.Size = 100;
            Check(queue.TryDequeue(out _), "134-1D-3: mutable payload queue drains first item");
            Check(queue.QueuedBytes == 4,
                "134-1D-4: dequeue subtracts the byte size captured at enqueue time");
        }

        private static void TestBoundedEventQueueZeroByteBudgetMeansUnlimitedBytes()
        {
            var queue = new BoundedEventQueue<byte[]>(maxFrames: 2, maxBytes: 0, measureBytes: payload => payload?.Length ?? 0);

            Check(queue.TryEnqueue(new byte[1024], out _),
                "134-1E-1: zero maxBytes disables the byte budget for non-empty payloads");
            Check(queue.TryEnqueue(new byte[2048], out _),
                "134-1E-2: zero maxBytes still honors the frame budget only");
            Check(!queue.TryEnqueue(new byte[1], out var overflow)
                  && overflow.QueuedFrames == 2
                  && overflow.RejectedBytes == 1,
                "134-1E-3: zero maxBytes queue still rejects frame-count overflow");
        }

        private static void VerifyFoxgloveManagerUsesBoundedClientEventQueue()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(manager.Contains("MaxQueuedClientEvents", StringComparison.Ordinal)
                  && manager.Contains("MaxQueuedClientEventPayloadBytes", StringComparison.Ordinal)
                  && manager.Contains("MaxQueuedClientLifecycleEvents", StringComparison.Ordinal)
                  && manager.Contains("BoundedEventQueue<ClientEvent>", StringComparison.Ordinal),
                "134-1B-1: FoxgloveManager declares bounded message and lifecycle event queue budgets");
            Check(!manager.Contains("ConcurrentQueue<ClientEvent>", StringComparison.Ordinal),
                "134-1B-2: FoxgloveManager no longer uses an unbounded ConcurrentQueue for client events");
            Check(manager.Contains("EnqueueClientLifecycleEvent(new ClientEvent", StringComparison.Ordinal)
                  && server.Contains("EnqueueClientMessageEvent(new ClientEvent", StringComparison.Ordinal),
                "134-1B-3: connect/disconnect use a lifecycle queue separate from payload message events");
            Check(manager.Contains("evt.IsMessage ? evt.Payload?.Length ?? 0 : 0", StringComparison.Ordinal),
                "134-1B-4: only message payload bytes count against the byte budget");
        }

        private static void VerifyClientEventQueueOverflowWarning()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");

            Check(manager.Contains("WarnClientEventQueueOverflow", StringComparison.Ordinal)
                  && manager.Contains("ClientEventOverflowWarningIntervalTicks", StringComparison.Ordinal)
                  && manager.Contains("Interlocked.CompareExchange", StringComparison.Ordinal),
                "134-1C-1: overflow warning is throttled across transport threads");
            Check(manager.Contains("droppedEvents=", StringComparison.Ordinal)
                  && manager.Contains("droppedPayloadBytes=", StringComparison.Ordinal)
                  && manager.Contains("queuedPayloadBytes=", StringComparison.Ordinal),
                "134-1C-2: overflow warning includes retained and dropped queue diagnostics");
        }

        private static void VerifyReplayEmptyPathRestoresLivePublishers()
        {
            var setup = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var normalized = setup.Replace("\r\n", "\n");

            Check(normalized.Contains("if (string.IsNullOrEmpty(_replayFilePath))", StringComparison.Ordinal)
                  && normalized.Contains("RestoreLivePublishers();\n                return true;", StringComparison.Ordinal),
                "134-1F-1: empty replay path restores publishers disabled during Awake");
        }

        private static void VerifyManagerInspectorAndRuntimeBounds()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var normalizedManager = manager.Replace("\r\n", "\n");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var setup = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");

            Check(normalizedManager.Contains("[Range(1, 65535)]\n        [SerializeField] private int _port", StringComparison.Ordinal)
                  && normalizedManager.Contains("[Range(1, 65535)]\n        [SerializeField] private int _rootCaDistributorPort", StringComparison.Ordinal),
                "134-1G-1: public server and Root CA ports have Inspector range guards");
            Check(manager.Contains("private void OnValidate()", StringComparison.Ordinal)
                  && manager.Contains("_port = Mathf.Clamp(_port, 1, 65535);", StringComparison.Ordinal)
                  && manager.Contains("_rootCaDistributorPort = Mathf.Clamp(_rootCaDistributorPort, 1, 65535);", StringComparison.Ordinal),
                "134-1G-2: manager clamps port fields during Unity validation");
            Check(server.Contains("IsValidTcpPort(_port)", StringComparison.Ordinal)
                  && server.Contains("IsValidTcpPort(_rootCaDistributorPort)", StringComparison.Ordinal),
                "134-1G-3: runtime startup rejects invalid TCP ports before starting transports");
            Check(manager.Contains("[Range(1, MaxRecordingChunkSizeKB)]", StringComparison.Ordinal)
                  && setup.Contains("Mathf.Clamp(_recordingChunkSizeKB, 1, MaxRecordingChunkSizeKB)", StringComparison.Ordinal),
                "134-1G-4: recording chunk size is guarded in Inspector and runtime setup");
        }

        private static void VerifyRos2BridgeWarningThrottling()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var publishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");

            Check(manager.Contains("_lastRos2BridgePublishWarningKey", StringComparison.Ordinal)
                  && manager.Contains("_lastRos2BridgePublishWarningTicks", StringComparison.Ordinal)
                  && manager.Contains("_ros2BridgePublishWarningGate", StringComparison.Ordinal),
                "134-1H-1: manager tracks ROS2 bridge warning throttle state behind a single gate");
            Check(publishing.Contains("WarnRos2BridgePublishSkipped(reason)", StringComparison.Ordinal)
                  && publishing.Contains("WarnRos2BridgePublishSkipped(enqueueReason)", StringComparison.Ordinal)
                  && publishing.Contains("ClientEventOverflowWarningIntervalTicks", StringComparison.Ordinal)
                  && publishing.Contains("lock (_ros2BridgePublishWarningGate)", StringComparison.Ordinal)
                  && !publishing.Contains("Interlocked.Read(ref _lastRos2BridgePublishWarningTicks)", StringComparison.Ordinal),
                "134-1H-2: ROS2 bridge publish failures use an atomic bounded warning path");
        }

        private static void VerifyLoggerSeverityPrefixes()
        {
            var logger = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/IFoxgloveLogger.cs");

            Check(logger.Contains("[Foxglove][Warning]", StringComparison.Ordinal)
                  && logger.Contains("[Foxglove][Error]", StringComparison.Ordinal),
                "134-1I-1: ConsoleLogger distinguishes warning and error severity");
        }

        private static void VerifyRecordingAndAssetBudgetHardening()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var setup = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");

            Check(setup.Contains("DateTime.UtcNow.ToString(RecordingTimestampFormat", StringComparison.Ordinal)
                  && setup.Contains("yyyyMMdd_HHmmss_fffffff'Z'", StringComparison.Ordinal),
                "134-1J-1: generated recording names use high-precision UTC timestamps");
            var changelog = ReadRepoText("CHANGELOG.md");
            Check(changelog.Contains("yyyyMMdd_HHmmss_fffffffZ", StringComparison.Ordinal)
                  && changelog.Contains("yyyyMMdd_HHmmss.mcap", StringComparison.Ordinal)
                  && changelog.Contains("[Foxglove][Warning]", StringComparison.Ordinal),
                "134-1J-2: changelog documents recording timestamp and logger prefix behavior changes");
            Check(manager.Contains("public long MaxBytesOrDefault", StringComparison.Ordinal)
                  && setup.Contains("RegisterAssetRoot(ar.uriPrefix, absRoot, ar.MaxBytesOrDefault)", StringComparison.Ordinal),
                "134-1J-3: asset root byte budgets avoid float-to-long truncation at registration");
            Check(!runtime.Contains("using System.Linq;", StringComparison.Ordinal),
                "134-1J-4: FoxgloveRuntime no longer carries unused System.Linq import");
        }

        private static void VerifySerializedSecretsAreNotCommitted()
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            var serializedExtensions = new[] { ".unity", ".prefab", ".asset" };
            var searchRoots = new[]
            {
                Path.Combine(root, "Assets"),
                Path.Combine(root, "Packages"),
                Path.Combine(root, "ProjectSettings")
            };
            var checkedAny = false;
            foreach (var searchRoot in searchRoots)
            {
                if (!Directory.Exists(searchRoot))
                    continue;

                foreach (var file in Directory.EnumerateFiles(searchRoot, "*.*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    if (Array.IndexOf(serializedExtensions, extension) < 0)
                        continue;

                    checkedAny = true;
                    var text = File.ReadAllText(file);
                    foreach (var field in new[] { "_certificatePassword", "_sharedToken" })
                    {
                        foreach (var line in text.Split('\n'))
                        {
                            var trimmed = line.Trim();
                            if (!trimmed.StartsWith(field + ":", StringComparison.Ordinal))
                                continue;

                            var value = trimmed.Substring(field.Length + 1).Trim();
                            Check(string.IsNullOrEmpty(value) || value == "\"\"",
                                "134-1K-1: serialized scenes, prefabs, and assets do not commit plaintext manager secrets");
                        }
                    }
                }
            }

            Check(checkedAny, "134-1K-2: serialized Unity assets were scanned for committed manager secrets");
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
            public int Size { get; set; }
        }
    }
}
