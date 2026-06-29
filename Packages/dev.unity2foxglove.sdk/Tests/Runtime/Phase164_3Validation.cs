// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase164-3 optimization regression coverage for session routing hot paths.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase164_3Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-3 Tests ---");
            _passed = 0;

            VerifyBroadcastTextEncodesOnceAndAvoidsClientSnapshots();
            VerifySubscriptionRegistryAvoidsSingleAndBatchCopies();
            VerifyWebSocketMaskLoopAvoidsModulo();
            VerifyClientPublishDisconnectAvoidsLinqAllocation();
            VerifySingleChannelBroadcastsReuseLists();
            VerifyRegistryAndCompileEntry();

            Console.WriteLine("Phase 164-3: " + _passed + " checks passed.\n");
        }

        private static void VerifyBroadcastTextEncodesOnceAndAvoidsClientSnapshots()
        {
            var backend = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs");
            var connection = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsConnection.cs");
            var broadcastText = SourceMethod(backend, "public void BroadcastText(string json)");
            var broadcastBinary = SourceMethod(backend, "public void BroadcastBinary(byte[] data)");
            var broadcastData = SourceMethod(backend, "public void BroadcastDataBinary(byte[] data)");
            var clearData = SourceMethod(backend, "public void ClearDataQueues()");

            Check(connection.Contains("internal EnqueueResult SendTextEncoded(byte[] utf8Json, FramePriority priority)", StringComparison.Ordinal)
                  && broadcastText.Contains("Encoding.UTF8.GetBytes(json ?? string.Empty)", StringComparison.Ordinal)
                  && broadcastText.Contains("conn.SendTextEncoded", StringComparison.Ordinal),
                "164-3A-1: BroadcastText encodes JSON once and sends pre-encoded text frames");
            Check(!broadcastText.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && !broadcastBinary.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && !broadcastData.Contains("_clients.ToArray()", StringComparison.Ordinal)
                  && !clearData.Contains("_clients.ToArray()", StringComparison.Ordinal),
                "164-3A-2: broadcast and clear-data paths iterate the concurrent client dictionary without array snapshots");
        }

        private static void VerifySubscriptionRegistryAvoidsSingleAndBatchCopies()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            var trySingle = SourceMethod(source, "public bool TryAddSubscription(uint clientId, uint subscriptionId, uint channelId, out string error)");
            var addSingle = SourceMethod(source, "public void AddSubscription(uint clientId, uint subscriptionId, uint channelId)");
            var tryBatch = SourceMethod(source, "public bool TryAddSubscriptions(");

            Check(!trySingle.Contains("new[]", StringComparison.Ordinal)
                  && !addSingle.Contains("new[]", StringComparison.Ordinal)
                  && source.Contains("TryAddSubscriptionLocked", StringComparison.Ordinal),
                "164-3B-1: single-subscription path avoids wrapper array allocation");
            Check(!tryBatch.Contains("new Dictionary<uint, uint>(subs)", StringComparison.Ordinal)
                  && tryBatch.Contains("newUniqueCount", StringComparison.Ordinal)
                  && tryBatch.Contains("currentClientCount + newUniqueCount", StringComparison.Ordinal),
                "164-3B-2: batch subscription budget check avoids copying the existing client map");
        }

        private static void VerifyWebSocketMaskLoopAvoidsModulo()
        {
            var codec = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsFrameCodec.cs");
            var method = SourceMethod(codec, "internal static bool TryReadFrame(Stream stream, out WsFrame frame)");

            Check(!method.Contains("i % 4", StringComparison.Ordinal)
                  && method.Contains("maskIndex", StringComparison.Ordinal)
                  && method.Contains("if (maskIndex == 4)", StringComparison.Ordinal),
                "164-3C: WebSocket frame unmasking uses a rotating mask index instead of modulo per byte");
        }

        private static void VerifyClientPublishDisconnectAvoidsLinqAllocation()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            var method = SourceMethod(source, "public void RemoveClient(uint clientId)");

            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal)
                  && !method.Contains(".Where(", StringComparison.Ordinal)
                  && !method.Contains(".ToList()", StringComparison.Ordinal)
                  && method.Contains("_clientChannelRemovalScratch", StringComparison.Ordinal),
                "164-3D: client-publish disconnect path avoids LINQ and reuses a removal scratch list");
        }

        private static void VerifySingleChannelBroadcastsReuseLists()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");

            Check(source.Contains("private readonly List<AdvertiseChannel> _singleAdvertiseChannels", StringComparison.Ordinal)
                  && source.Contains("private readonly List<uint> _singleUnadvertiseChannelIds", StringComparison.Ordinal)
                  && source.Contains("_singleAdvertiseChannels.Add(channel)", StringComparison.Ordinal)
                  && source.Contains("_singleUnadvertiseChannelIds.Add(channelId)", StringComparison.Ordinal)
                  && !source.Contains("new List<AdvertiseChannel> { channel }", StringComparison.Ordinal)
                  && !source.Contains("new List<uint> { channelId }", StringComparison.Ordinal),
                "164-3E: single channel advertise/unadvertise broadcasts reuse session-owned one-element lists");
        }

        private static void VerifyRegistryAndCompileEntry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-3\", \"Phase 164-3\", Phase164_3Validation.Validate", StringComparison.Ordinal),
                "164-3F-1: validation registry exposes Phase164-3");
            Check(project.Contains("<Compile Include=\"Phase164_3Validation.cs\" />", StringComparison.Ordinal),
                "164-3F-2: runtime validation project compiles Phase164-3");
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Could not slice method: " + signature);
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not locate repository root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
