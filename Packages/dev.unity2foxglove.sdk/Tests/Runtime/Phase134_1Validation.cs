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
    /// <summary>
    /// Validation type for Phase134_1Validation.
    /// </summary>
    public static class Phase134_1Validation
    {
        private static readonly string[] SerializedSecretFields =
            { "_certificatePassword", "_sharedToken", "_replayCursorBridgeToken" };
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-1: Runtime facade and lifecycle hardening ===");
            _passed = 0;

            TestBoundedEventQueueRejectsOverflow();
            TestBoundedEventQueueUsesEnqueuedByteSnapshot();
            TestBoundedEventQueueZeroByteBudgetMeansUnlimitedBytes();
            VerifyFoxgloveManagerUsesBoundedClientEventQueue();
            VerifyProviderPublishFailureIsolation();
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
            Check(queue.DroppedCount == 0 && queue.DroppedBytes == 0,
                "134-1A-11: queue clear resets session drop counters for fresh diagnostics");
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
            Check(!manager.Contains("ConcurrentQueue<ClientEvent>", StringComparison.Ordinal),
                "134-1B-2: FoxgloveManager no longer uses an unbounded ConcurrentQueue for client events");
        }

        private static void VerifyProviderPublishFailureIsolation()
        {
            var warningState = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/WarningDebounceState.cs");
            Check(!warningState.Contains("Ros2Bridge", StringComparison.Ordinal)
                  && !warningState.Contains("ROS2", StringComparison.Ordinal),
                "134-1H-1: core manager warning state remains transport-provider neutral");
        }

        private static void VerifyRecordingAndAssetBudgetHardening()
        {
            var setup = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var changelog = ReadRepoText("CHANGELOG.md");
            Check(changelog.Contains("yyyyMMdd_HHmmss_fffffffZ", StringComparison.Ordinal)
                  && changelog.Contains("yyyyMMdd_HHmmss.mcap", StringComparison.Ordinal)
                  && changelog.Contains("[Foxglove][Warning]", StringComparison.Ordinal),
                "134-1J-2: changelog documents recording timestamp and logger prefix behavior changes");
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
            var foundSearchRoot = false;
            foreach (var searchRoot in searchRoots)
            {
                if (!Directory.Exists(searchRoot))
                    continue;

                foundSearchRoot = true;
                foreach (var file in Directory.EnumerateFiles(searchRoot, "*.*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    if (Array.IndexOf(serializedExtensions, extension) < 0)
                        continue;

                    checkedAny = true;
                    var text = File.ReadAllText(file);
                    using var reader = new StringReader(text);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var trimmed = line.Trim();
                        foreach (var field in SerializedSecretFields)
                        {
                            if (!trimmed.StartsWith(field + ":", StringComparison.Ordinal))
                                continue;

                            var value = trimmed.Substring(field.Length + 1).Trim();
                            Check(string.IsNullOrEmpty(value) || value == "\"\"",
                                "134-1K-1: serialized scenes, prefabs, and assets do not commit plaintext manager secrets");
                        }
                    }
                }
            }

            Check(foundSearchRoot, "134-1K-0: serialized Unity asset search roots exist for committed manager secret scan");
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
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source is missing.", path);

            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private sealed class MutablePayload
        {
            public int Size { get; set; }
        }
    }
}
