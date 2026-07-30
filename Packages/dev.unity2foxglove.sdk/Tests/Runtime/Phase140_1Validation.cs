// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-1 runtime facade and lifecycle review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for runtime facade and lifecycle defects found in Phase 140-1.
    /// </summary>
    public static class Phase140_1Validation
    {
        private static int _passed;

        /// <summary>
        /// Runs all Phase 140-1 lifecycle review checks.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-1: Runtime facade and lifecycle review fixes ===");
            _passed = 0;

            VerifyManagerStartupSetupFailuresUseUnifiedCleanup();
            VerifyRuntimeDisposeIsIdempotent();
            VerifyOptionalProtobufRegistrationAvoidsMethodInfoInvoke();
            VerifyUnityThreadContractsAreExplicit();
            VerifyOpt1PublishJsonByteEquivalence();
            VerifyOpt5DrainToExceptionSafety();
            VerifyOpt3NamespaceCacheNoRuntimeSetter();
            VerifyOpt2QosAfterEarlyExitGuards();

            Console.WriteLine($"Phase 140-1: {_passed} checks passed.");
        }

        private static void VerifyManagerStartupSetupFailuresUseUnifiedCleanup()
        {
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var startServer = ExtractMethodBody(server, "public void StartServer()");
            var tryIndex = startServer.IndexOf("try", StringComparison.Ordinal);
            var setupRecordingIndex = startServer.IndexOf("SetupRecording()", StringComparison.Ordinal);
            var setupReplayIndex = startServer.IndexOf("SetupReplay()", StringComparison.Ordinal);

            Check(tryIndex >= 0
                  && setupRecordingIndex > tryIndex
                  && setupReplayIndex > tryIndex,
                "140-1A-1: recording and replay setup run inside the unified StartServer cleanup boundary");
            Check(startServer.Contains("CleanupStartupAfterFailure();", StringComparison.Ordinal),
                "140-1A-2: StartServer catch routes every startup failure through one cleanup helper");
            Check(server.Contains("private void CleanupStartupAfterFailure()", StringComparison.Ordinal)
                  && server.Contains("CleanupPendingRecordingSidecar();", StringComparison.Ordinal)
                  && server.Contains("_runtime?.DisableRecording()", StringComparison.Ordinal)
                  && server.Contains("_runtime?.DisableReplay()", StringComparison.Ordinal)
                  && server.Contains("RestoreLivePublishers();", StringComparison.Ordinal),
                "140-1A-3: startup cleanup clears staged sidecars, recording, replay, endpoints, and disabled publishers");
        }

        private static void VerifyRuntimeDisposeIsIdempotent()
        {
            var transport = new CountingTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());

            runtime.Dispose();
            runtime.Dispose();

            Check(transport.DisposeCalls == 1,
                "140-1B-1: FoxgloveRuntime.Dispose disposes transport exactly once across repeated calls");
        }

        private static void VerifyOptionalProtobufRegistrationAvoidsMethodInfoInvoke()
        {
            var runtime = PhaseValidationSourceHelpers.ReadFoxgloveRuntimeSources();
            var packageLink = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/link.xml");

            Check(!runtime.Contains("method.Invoke", StringComparison.Ordinal)
                  && runtime.Contains("CreateDelegate", StringComparison.Ordinal),
                "140-1C-1: optional protobuf schema registration avoids MethodInfo.Invoke");
            Check(packageLink.Contains("Unity.FoxgloveSDK.Proto", StringComparison.Ordinal)
                  && packageLink.Contains("Google.Protobuf", StringComparison.Ordinal),
                "140-1C-2: package link.xml preserves optional protobuf registration dependencies");
        }

        private static void VerifyUnityThreadContractsAreExplicit()
        {
            var ros2Policy = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2NativeOutputPolicy.cs");
            var sharedClock = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveSharedSensorClock.cs");

            Check(ros2Policy.Contains("Unity main thread", StringComparison.Ordinal)
                  && ros2Policy.Contains("Refresh", StringComparison.Ordinal),
                "140-1D-1: Ros2NativeOutputPolicy documents its Unity-main-thread manager lookup contract");
            Check(sharedClock.Contains("main-thread-only", StringComparison.Ordinal)
                  && sharedClock.Contains("not synchronized", StringComparison.Ordinal),
                "140-1D-2: shared sensor clock documents its single-threaded Unity owner contract");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static string ExtractMethodBody(string source, string signature)
        {
            var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
            if (signatureIndex < 0)
                return string.Empty;
            var braceIndex = source.IndexOf('{', signatureIndex);
            if (braceIndex < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceIndex; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(braceIndex, i - braceIndex + 1);
                }
            }

            return string.Empty;
        }

        private static void VerifyOpt1PublishJsonByteEquivalence()
        {
            var messages = new object[]
            {
                new { x = 1, y = "hello" },
                new { name = "test", values = new[] { 1, 2, 3 } },
                new Dictionary<string, object> { ["key"] = "val", ["num"] = 42 },
                new { unicode = "\u4e2d\u6587\u6d4b\u8bd5" },
                new { },
            };

            foreach (var message in messages)
            {
                var oldBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message));
                byte[] newBytes;
                using (var stream = new MemoryStream())
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
                    using (var jsonWriter = new JsonTextWriter(writer))
                        JsonSerializer.CreateDefault().Serialize(jsonWriter, message);
                    newBytes = stream.ToArray();
                }

                var label = "OPT-1 byte-equivalence [" + JsonConvert.SerializeObject(message).Substring(0, Math.Min(40, JsonConvert.SerializeObject(message).Length)) + "...]";
                Check(oldBytes.SequenceEqual(newBytes), label);
            }

            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
                using (var jsonWriter = new JsonTextWriter(writer))
                    JsonSerializer.CreateDefault().Serialize(jsonWriter, new { x = 1 });
                var bytes = stream.ToArray();
                Check(bytes.Length == 0 || bytes[0] != 0xEF, "OPT-1: no UTF-8 BOM preamble (0xEF) at byte 0");
            }
        }

        private static void VerifyOpt5DrainToExceptionSafety()
        {
            var queue = new BoundedEventQueue<int>(maxFrames: 10, maxBytes: 0, measureBytes: _ => 0);
            for (var i = 0; i < 5; i++)
                queue.TryEnqueue(i, out _);
            var drained = new List<int>();
            queue.DrainTo(drained);
            Check(drained.Count == 5 && drained[0] == 0 && drained[4] == 4, "OPT-5: DrainTo copies all items in FIFO order");
            Check(queue.Count == 0, "OPT-5: queue is empty after DrainTo");

            var queue2 = new BoundedEventQueue<int>(maxFrames: 10, maxBytes: 0, measureBytes: _ => 0);
            for (var i = 0; i < 3; i++)
                queue2.TryEnqueue(i, out _);
            var scratch = new List<int>();
            queue2.DrainTo(scratch);
            var threw = false;
            try { foreach (var item in scratch) if (item == 1) throw new Exception("simulated callback failure"); }
            catch { threw = true; }
            finally { scratch.Clear(); }
            Check(threw, "OPT-5: simulated exception was thrown");

            for (var i = 10; i < 13; i++)
                queue2.TryEnqueue(i, out _);
            var drained2 = new List<int>();
            queue2.DrainTo(drained2);
            Check(drained2.SequenceEqual(new[] { 10, 11, 12 }), "OPT-5: no stale items re-delivered after exception+clear");
        }

        private static void VerifyOpt3NamespaceCacheNoRuntimeSetter()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var lines = source.Split('\n');
            var assignments = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!ContainsExactIdentifier(trimmed, "_ros2BridgeNamespace")) continue;
                if (trimmed.Contains("= ")) assignments++;
            }

            Check(assignments <= 1, "OPT-3: _ros2BridgeNamespace has no runtime setter (only [SerializeField] init)");
            Check(source.Contains("InvalidateRos2BridgeNamespaceCache()", StringComparison.Ordinal), "OPT-3: InvalidateRos2BridgeNamespaceCache exists");
            Check(CountOccurrences(source, "InvalidateRos2BridgeNamespaceCache()") >= 2, "OPT-3: cache invalidated from at least 2 call sites (OnValidate + InitializeOutputModeWatchers)");
        }

        private static void VerifyOpt2QosAfterEarlyExitGuards()
        {
            var source = PhaseValidationSourceHelpers.ReadFoxgloveManagerPublishingSources();
            var body = ExtractMethodBody(source, "public bool TryPrepareRos2BridgePublish(");
            Check(!string.IsNullOrEmpty(body), "OPT-2: TryPrepareRos2BridgePublish method body found");

            var qosDef = body.IndexOf("qos = default", StringComparison.Ordinal);
            var qosRes = body.IndexOf("qos = ResolveRos2BridgeQos", StringComparison.Ordinal);
            var early1 = body.IndexOf("SuppressLivePublishersForReplay", StringComparison.Ordinal);
            var early2 = body.IndexOf("!_ros2BridgeEnabled", StringComparison.Ordinal);

            Check(qosDef >= 0, "OPT-2: qos = default present");
            Check(qosRes > qosDef, "OPT-2: ResolveRos2BridgeQos() called after qos=default");
            Check(qosRes > early1 && qosRes > early2, "OPT-2: ResolveRos2BridgeQos() called after both early-exit guards");
        }

        private static bool ContainsExactIdentifier(string text, string identifier)
        {
            var index = 0;
            while ((index = text.IndexOf(identifier, index, StringComparison.Ordinal)) >= 0)
            {
                var afterEnd = index + identifier.Length;
                if (afterEnd >= text.Length || !(char.IsLetterOrDigit(text[afterEnd]) || text[afterEnd] == '_'))
                    return true;
                index = afterEnd;
            }

            return false;
        }

        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
            { count++; index += pattern.Length; }

            return count;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class CountingTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public int DisposeCalls { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => DisposeCalls++;
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
        }
    }
}
