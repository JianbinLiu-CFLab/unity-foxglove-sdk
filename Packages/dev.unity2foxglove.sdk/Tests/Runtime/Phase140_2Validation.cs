// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-2 session protocol and registry review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for session protocol and registry defects found in Phase 140-2.
    /// </summary>
    public static class Phase140_2Validation
    {
        private const uint ClientId = 1402;
        private static int _passed;

        /// <summary>
        /// Runs all Phase 140-2 session protocol and registry review checks.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-2: Session protocol and registry review fixes ===");
            _passed = 0;

            VerifySubscriptionBudgetWarningIsLockedAndRuntimeReadIsVolatile();
            VerifyBroadcastTimeUsesInterlockedThrottle();
            VerifyPlaybackDrainRespondsWhenPlaybackDisablesAfterEnqueue();
            VerifyClientPublishRemovalDoesNotMutateGraphUnderClientLock();
            VerifyConnectionGraphSubscribeUsesAtomicSnapshot();
            VerifyDeadSnapshotBroadcastHelperWasRemoved();
            VerifyParameterSubscribeAllBehaviorIsDocumented();
            VerifyOpt10OnClientTextOpExtractionEquivalence();

            Console.WriteLine($"Phase 140-2: {_passed} checks passed.");
        }

        private static void VerifySubscriptionBudgetWarningIsLockedAndRuntimeReadIsVolatile()
        {
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var connection = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Connection.cs");

            Check(session.Contains("private readonly object _subscriptionBudgetWarnedClientsLock = new();", StringComparison.Ordinal),
                "140-2A-1: subscription budget warning set has a dedicated lock");
            Check(connection.Contains("lock (_subscriptionBudgetWarnedClientsLock)", StringComparison.Ordinal)
                  && session.Contains("lock (_subscriptionBudgetWarnedClientsLock)", StringComparison.Ordinal),
                "140-2A-2: subscription budget warning add/remove/clear paths are synchronized");
            Check(connection.Contains("Volatile.Read(ref _runtime)?.RequestReplaySubscriberBackfill()", StringComparison.Ordinal)
                  && !connection.Contains("_runtime?.RequestReplaySubscriberBackfill()", StringComparison.Ordinal),
                "140-2A-3: subscribe backfill request uses the volatile runtime publication contract");
        }

        private static void VerifyBroadcastTimeUsesInterlockedThrottle()
        {
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionTimeBroadcaster.cs");
            var method = ExtractMethodBody(source, "internal bool TryReserveBroadcast(long nowTicks, float rateHz)");

            Check(session.Contains("_timeBroadcaster.TryReserveBroadcast(DateTime.UtcNow.Ticks, rateHz)", StringComparison.Ordinal)
                  && method.Contains("Interlocked.Read(ref _lastBroadcastTicks)", StringComparison.Ordinal)
                  && method.Contains("Interlocked.CompareExchange(ref _lastBroadcastTicks", StringComparison.Ordinal),
                "140-2B-1: BroadcastTime protects its throttle timestamp with interlocked operations");
            Check(!method.Contains("_lastBroadcastTicks = nowTicks;", StringComparison.Ordinal),
                "140-2B-2: BroadcastTime no longer writes the throttle timestamp as a plain long field");
        }

        private static void VerifyPlaybackDrainRespondsWhenPlaybackDisablesAfterEnqueue()
        {
            var transport = new Phase140_2FakeTransport();
            using var session = new FoxgloveSession("phase140-2", transport);
            var runtime = new Phase140_2RuntimeContext();
            runtime.EnablePlayback();
            session.SetRuntimeContext(runtime);

            transport.Connect(ClientId);
            transport.ClearBinary();

            transport.Binary(ClientId, BuildPlaybackControlFrame("phase140-2-request", hasSeek: true));
            runtime.DisablePlayback();
            session.DrainPlaybackControls();

            var frames = transport.BinariesFor(ClientId);
            Check(runtime.AppliedPlaybackControls == 0,
                "140-2C-1: disabled playback after enqueue is not applied to the runtime");
            Check(frames.Count == 1 && frames[0][0] == ServerOpcode.PlaybackState,
                "140-2C-2: disabled playback after enqueue still returns a targeted PlaybackState");
            Check(DecodePlaybackStateRequestId(frames[0]) == "phase140-2-request",
                "140-2C-3: fallback PlaybackState preserves the requestId");
            Check(frames[0][14] == 0,
                "140-2C-4: fallback PlaybackState does not claim the seek was applied");
        }

        private static void VerifyClientPublishRemovalDoesNotMutateGraphUnderClientLock()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            var method = ExtractMethodBody(source, "public void RemoveClient(uint clientId)");
            var lockIndex = method.IndexOf("lock (_clientChannelsLock)", StringComparison.Ordinal);
            var graphIndex = method.IndexOf("_graph.RemoveClientPublishedTopic", StringComparison.Ordinal);

            Check(method.Contains("removedGraphTopics", StringComparison.Ordinal),
                "140-2D-1: RemoveClient snapshots graph removals while holding the client-channel lock");
            Check(lockIndex >= 0 && graphIndex > method.IndexOf("}", lockIndex, StringComparison.Ordinal),
                "140-2D-2: RemoveClient applies graph removals after releasing the client-channel lock");
        }

        private static void VerifyConnectionGraphSubscribeUsesAtomicSnapshot()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ConnectionGraphRegistry.cs");
            var graph = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");

            Check(registry.Contains("public ConnectionGraphUpdate SubscribeAndGetSnapshot(uint clientId)", StringComparison.Ordinal)
                  && registry.Contains("return BuildSnapshotLocked();", StringComparison.Ordinal),
                "140-2E-1: connection graph registry can subscribe and snapshot from one lock epoch");
            Check(graph.Contains("_graph.SubscribeAndGetSnapshot(clientId)", StringComparison.Ordinal),
                "140-2E-2: graph subscribe handler uses the atomic subscribe-and-snapshot path");
        }

        private static void VerifyDeadSnapshotBroadcastHelperWasRemoved()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            Check(!source.Contains("BroadcastSessionSnapshot", StringComparison.Ordinal),
                "140-2F-1: unused BroadcastSessionSnapshot helper was removed");
        }

        private static void VerifyParameterSubscribeAllBehaviorIsDocumented()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ParameterSubscriptionRegistry.cs");
            Check(source.Contains("cannot narrow the subscription", StringComparison.Ordinal)
                  && source.Contains("unsubscribe first", StringComparison.Ordinal),
                "140-2G-1: subscribe-all parameter behavior is documented for callers");
        }

        private static byte[] BuildPlaybackControlFrame(string requestId, bool hasSeek)
        {
            var id = Encoding.UTF8.GetBytes(requestId ?? string.Empty);
            var frame = new byte[19 + id.Length];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            frame[1] = 1;
            BinaryEncoding.WriteF32LE(frame, 2, 1f);
            frame[6] = hasSeek ? (byte)1 : (byte)0;
            BinaryEncoding.WriteU64LE(frame, 7, 1_000_000UL);
            BinaryEncoding.WriteU32LE(frame, 15, (uint)id.Length);
            Buffer.BlockCopy(id, 0, frame, 19, id.Length);
            return frame;
        }

        private static string DecodePlaybackStateRequestId(byte[] frame)
        {
            if (frame == null || frame.Length < 19 || frame[0] != ServerOpcode.PlaybackState)
                throw new InvalidOperationException("Not a PlaybackState frame.");

            var idLength = BinaryEncoding.ReadU32LE(frame, 15);
            if (idLength > int.MaxValue || idLength > frame.Length - 19)
                throw new InvalidOperationException("Malformed PlaybackState requestId length.");

            return idLength == 0
                ? string.Empty
                : Encoding.UTF8.GetString(frame, 19, (int)idLength);
        }

        /// <summary>
        /// OPT-10: Validate that JsonTextReader-based "op" field extraction produces
        /// identical results to JObject.Parse(json)["op"]?.ToString() for all
        /// supported control-message shapes, including edge cases.
        /// This test MUST pass before replacing the JObject.Parse call in OnClientText.
        /// </summary>
        private static void VerifyOpt10OnClientTextOpExtractionEquivalence()
        {
            var testCases = new (string json, string expectedOp)[]
            {
                // Normal messages
                (@"{""op"":""subscribe"",""id"":1}", "subscribe"),
                (@"{""id"":1,""op"":""unsubscribe""}", "unsubscribe"), // op not first
                (@"{""op"":""advertise"",""channels"":[{""id"":1,""topic"":""/t""}]}", "advertise"),
                // No op field
                (@"{""id"":1,""channels"":[]}", null),
                (@"{}", null),
                // Null op value
                (@"{""op"":null}", null),
                // Numeric op (shouldn't happen but must match JObject behavior)
                (@"{""op"":123}", "123"),
                // Unicode in op value
                (@"{""op"":""\u4e2d\u6587""}", "\u4e2d\u6587"),
                // Whitespace
                ("  { \"op\" : \"subscribe\" , \"id\" : 1 }  ", "subscribe"),
                // Nested "op" should NOT shadow top-level
                (@"{""data"":{""op"":""nested""},""op"":""top""}", "top"),
                // Duplicate top-level "op": JObject default DuplicatePropertyNameHandling
                // is Replace (last wins), so the extractor must return the LAST "op".
                (@"{""op"":""subscribe"",""op"":""advertise""}", "advertise"),
            };

            foreach (var (json, expected) in testCases)
            {
                string oldResult = null;
                try { oldResult = JObject.Parse(json)["op"]?.ToString(); }
                catch { oldResult = null; }

                // Drive the REAL production method (internal, same assembly), not a copy,
                // mirroring OnClientText's catch -> treat-as-missing semantics.
                string newResult;
                try { newResult = FoxgloveSession.TryReadOpField(json); }
                catch { newResult = null; }

                var label = $"OPT-10 op extraction [{json.Substring(0, Math.Min(40, json.Length))}...]";
                Check(oldResult == newResult, label);
                if (expected != null)
                    Check(newResult == expected, $"OPT-10 expected op={expected} for {label.Split('[')[1].Split(']')[0]}");
            }
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

        private sealed class Phase140_2RuntimeContext : IRuntimeContext
        {
            public bool PlaybackEnabled { get; private set; }
            public int AppliedPlaybackControls { get; private set; }
            public FoxgloveAssetRegistry Assets { get; } = new();

            public void EnablePlayback() => PlaybackEnabled = true;
            public void DisablePlayback() => PlaybackEnabled = false;
            public ulong GetPlaybackStartNs() => 0;
            public ulong GetPlaybackEndNs() => 10_000_000;
            public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs) { }
            public void ReplaySeek(ulong timeNs) { }
            public void ReplayPlay() { }
            public void ReplayPause() { }
            public void RequestReplaySubscriberBackfill() { }

            public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            {
                return new PlaybackClock.PlaybackStateSnapshot
                {
                    Status = 1,
                    CurrentTimeNs = 123_456_789UL,
                    Speed = 1f,
                    DidSeek = didSeek,
                    RequestId = requestId
                };
            }

            public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
                byte cmd,
                float speed,
                bool hasSeek,
                ulong seekNs,
                string requestId)
            {
                AppliedPlaybackControls++;
                return GetPlaybackState(hasSeek, requestId);
            }
        }

        private sealed class Phase140_2FakeTransport : IFoxgloveTransport
        {
            private readonly Dictionary<uint, List<byte[]>> _sentBinaries = new();
            private readonly HashSet<uint> _clients = new();

            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data)
            {
                foreach (var clientId in _clients)
                    SendBinary(clientId, data);
            }

            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data)
            {
                if (!_sentBinaries.TryGetValue(clientId, out var frames))
                {
                    frames = new List<byte[]>();
                    _sentBinaries[clientId] = frames;
                }

                frames.Add(data);
            }

            public void Connect(uint clientId)
            {
                _clients.Add(clientId);
                if (!_sentBinaries.ContainsKey(clientId))
                    _sentBinaries[clientId] = new List<byte[]>();
                OnClientConnected?.Invoke(clientId);
            }

            public void Disconnect(uint clientId)
            {
                _clients.Remove(clientId);
                OnClientDisconnected?.Invoke(clientId);
            }

            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);

            public void Binary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);

            public IReadOnlyList<byte[]> BinariesFor(uint clientId)
                => _sentBinaries.TryGetValue(clientId, out var frames) ? frames : Array.Empty<byte[]>();

            public void ClearBinary()
            {
                foreach (var frames in _sentBinaries.Values)
                    frames.Clear();
            }
        }
    }
}
