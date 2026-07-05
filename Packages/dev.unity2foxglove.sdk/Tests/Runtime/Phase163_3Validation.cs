// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-3 review regression checks for session protocol and client routing.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_3Validation
    {
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-3: Session Protocol and Client Routing Review ===");

            var root = Phase16Validation.FindRepoRoot();
            var session = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var connection = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Connection.cs");
            var parameters = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Parameters.cs");
            var clientPublish = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            var graph = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            var playback = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionPlaybackHandler.cs");
            var services = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Services.cs");
            var timeBroadcaster = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionTimeBroadcaster.cs");
            var status = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Protocol/Messages/StatusMessages.cs");
            var assets = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Assets/FoxgloveAssetRegistry.cs");
            var phase54 = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase54Validation.cs");
            var registry = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(connection.Contains("if (msg?.SubscriptionIds != null)", StringComparison.Ordinal),
                "163-3A: unsubscribe null bodies do not dereference deserialized messages");
            Check(connection.Contains("if (graphChanged)", StringComparison.Ordinal)
                  && !connection.Contains("_graph.BroadcastUpdate();\r\n            }\r\n            catch", StringComparison.Ordinal),
                "163-3B: subscribe/unsubscribe graph broadcasts require real topology changes");
            Check(clientPublish.Contains("foreach (var chId in msg?.ChannelIds ?? new List<uint>())", StringComparison.Ordinal)
                  && clientPublish.Contains("if (graphChanged)", StringComparison.Ordinal),
                "163-3C: client unadvertise null bodies are safe and avoid empty graph broadcasts");
            Check(session.Contains("private readonly object _paramSubScratchLock = new();", StringComparison.Ordinal)
                  && parameters.Contains("lock (_paramSubScratchLock)", StringComparison.Ordinal)
                  && parameters.Contains("_paramSubScratch.Clear();", StringComparison.Ordinal)
                  && parameters.Contains("return matchingClients;", StringComparison.Ordinal),
                "163-3D: parameter subscriber scratch buffer is locked and returned as a snapshot");
            Check(graph.IndexOf("var recorder = _recorderProvider();", StringComparison.Ordinal)
                  < graph.IndexOf("Interlocked.CompareExchange(ref _dirty, 0, 1)", StringComparison.Ordinal),
                "163-3E: graph metadata dirty flag is not cleared when no recorder is attached");
            Check(playback.Contains("overflowedRequests", StringComparison.Ordinal)
                  && playback.Contains("SendPlaybackState(request.ClientId, request.DisabledFallbackState)", StringComparison.Ordinal)
                  && phase54.Contains("FoxgloveSession.MaxPendingPlaybackControls + 5", StringComparison.Ordinal),
                "163-3F: playback queue overflow reconciles dropped requests with targeted state responses");
            Check(session.Contains("TryDecodeClientMessageData(data, out var chId, out var payload)", StringComparison.Ordinal)
                  && connection.Contains("HandleClientBinaryPublish(uint clientId, uint channelId, byte[] payload)", StringComparison.Ordinal)
                  && clientPublish.Contains("public void RouteBinary(uint clientId, uint chId, byte[] payload)", StringComparison.Ordinal)
                  && !clientPublish.Contains("TryDecodeClientMessageData(data", StringComparison.Ordinal),
                "163-3G: client publish binary frames are decoded once at the session dispatch boundary");
            Check(assets.Contains("Path.GetFullPath(Path.Combine(bestRoot.LocalRoot, relative))", StringComparison.Ordinal)
                  && assets.Contains("Path traversal denied", StringComparison.Ordinal)
                  && assets.Contains("!resolved.StartsWith(rootPrefix", StringComparison.Ordinal),
                "163-3H: asset fetch root-containment is enforced by the registry");
            Check(services.Contains("call.JsonPayload == null", StringComparison.Ordinal)
                  && services.Contains("\"Malformed JSON payload\"", StringComparison.Ordinal)
                  && !services.Contains("ParseJsonPayloadBytes", StringComparison.Ordinal),
                "163-3I: service drain reports missing parsed JSON as malformed payload");
            Check(status.Contains("[JsonProperty(\"id\")]", StringComparison.Ordinal)
                  && status.Contains("empty IDs are intentionally omitted", StringComparison.Ordinal)
                  && status.Contains("ShouldSerializeId() => Id != null && Id.Length > 0", StringComparison.Ordinal),
                "163-3J: status id serialization has one explicit empty-id rule");
            Check(session.Contains("_timeBroadcaster.Reset()", StringComparison.Ordinal)
                  && timeBroadcaster.Contains("Interlocked.Exchange(ref _lastBroadcastTicks, 0)", StringComparison.Ordinal),
                "163-3K: ClearSession resets time broadcast throttle");
            Check(registry.Contains("Ci(\"--phase163-3\", \"Phase 163-3: phase163-3 review regression checks for session protocol and client routing\", Phase163_3Validation.Validate", StringComparison.Ordinal),
                "163-3L: PhaseValidationRegistry wires --phase163-3");

            Console.WriteLine("Phase 163-3: 12 checks passed.");
            Console.WriteLine();
        }

        private static string Read(string root, string relativePath)
            => File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[FAIL] " + message);
            }

            Console.WriteLine("[PASS] " + message);
        }
    }
}
