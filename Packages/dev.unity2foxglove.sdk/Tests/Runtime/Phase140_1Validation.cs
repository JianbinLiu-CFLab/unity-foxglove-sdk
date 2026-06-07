// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-1 runtime facade and lifecycle review fixes.

using System;
using System.IO;
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
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var packageLink = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/link.xml");
            var activeLink = ReadRepoText("Unity2Foxglove/Assets/link.xml");

            Check(!runtime.Contains("method.Invoke", StringComparison.Ordinal)
                  && runtime.Contains("CreateDelegate", StringComparison.Ordinal),
                "140-1C-1: optional protobuf schema registration avoids MethodInfo.Invoke");
            Check(packageLink.Contains("Unity.FoxgloveSDK.Proto", StringComparison.Ordinal)
                  && activeLink.Contains("Unity.FoxgloveSDK.Proto", StringComparison.Ordinal)
                  && activeLink.Contains("Google.Protobuf", StringComparison.Ordinal),
                "140-1C-2: active Unity link.xml preserves optional protobuf registration dependencies");
        }

        private static void VerifyUnityThreadContractsAreExplicit()
        {
            var ros2Policy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/Ros2NativeOutputPolicy.cs");
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
