// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 54 regression coverage for session backpressure,
// malformed protocol input, connection graph cleanup, and docs drift.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates hardening fixes from the runtime/session protocol review.
    /// </summary>
    public static class Phase54Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 54: Runtime Session Protocol Hardening ===");
            _passed = 0;

            VerifyServicePendingCallsAreBounded();
            VerifyPlaybackControlQueueIsBounded();
            VerifyFetchAssetMissingUriReturnsError();
            VerifyUnregisterChannelRemovesGraphSubscriptions();
            VerifyDisconnectBroadcastsGraphCleanup();
            VerifyRuntimeRegisterServiceUpdatesGraph();
            VerifyBroadcastTimeAcceptsSubHzRate();
            VerifyMalformedClientAdvertiseDoesNotRegisterChannel();
            VerifyChineseArchitectureDocsMatchCurrentCapabilities();

            Console.WriteLine($"Phase 54: {_passed} checks passed.");
        }

        private static void VerifyServicePendingCallsAreBounded()
        {
            var transport = new Phase54FakeTransport();
            var services = new FoxgloveServiceRegistry();
            var serviceId = services.Register(new ServiceDescriptor { Name = "/phase54/service", Type = "phase54" });
            var session = new FoxgloveSession("phase54", transport, new Phase54FixedClock(1),
                new DefaultSchemaRegistry(), null, new FoxgloveParameterStore(), services);

            transport.Connect(1);
            for (uint i = 1; i <= FoxgloveServiceRegistry.MaxPendingCallsPerClient + 1; i++)
                transport.Binary(1, BuildServiceCallFrame(serviceId, i, "{}"));

            var failures = transport.TextsFor(1)
                .Select(TryParseJson)
                .Where(j => j?["op"]?.ToString() == "serviceCallFailure")
                .ToList();

            Check(failures.Count == 1, "54A-1: service pending overflow returns one failure");
            Check(failures[0]?["message"]?.ToString().Contains("pending", StringComparison.OrdinalIgnoreCase) == true,
                "54A-1b: overflow failure explains pending-call limit");
            Check(services.GetPendingCalls().Count == FoxgloveServiceRegistry.MaxPendingCallsPerClient,
                "54A-1c: pending service call map stays at per-client cap");
        }

        private static void VerifyPlaybackControlQueueIsBounded()
        {
            var transport = new Phase54FakeTransport();
            var session = new FoxgloveSession("phase54", transport);
            var runtime = new Phase54RuntimeContext();
            runtime.EnablePlayback();
            session.SetRuntimeContext(runtime);

            transport.Connect(1);
            for (var i = 0; i < FoxgloveSession.MaxPendingPlaybackControls + 5; i++)
                transport.Binary(1, BuildPlaybackControlFrame($"req-{i}"));

            session.DrainPlaybackControls();

            Check(runtime.AppliedPlaybackControls == FoxgloveSession.MaxPendingPlaybackControls,
                "54A-2: playback control drain is capped");
            Check(transport.BroadcastBinaries.Count == FoxgloveSession.MaxPendingPlaybackControls,
                "54A-2b: playback state broadcasts stay bounded");
        }

        private static void VerifyFetchAssetMissingUriReturnsError()
        {
            var root = Path.Combine(Path.GetTempPath(), "foxglove_phase54_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var transport = new Phase54FakeTransport();
                var session = new FoxgloveSession("phase54", transport);
                var runtime = new Phase54RuntimeContext();
                runtime.Assets.RegisterRoot("asset://phase54/", root);
                session.SetRuntimeContext(runtime);

                transport.Connect(1);
                Check(!Throws(() => transport.Text(1, "{\"op\":\"fetchAsset\",\"requestId\":7}")),
                    "54A-3: fetchAsset without uri does not throw");

                var frame = transport.BinariesFor(1).LastOrDefault();
                Check(IsFetchAssetError(frame, 7), "54A-3b: missing uri returns fetchAssetResponse error");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static void VerifyUnregisterChannelRemovesGraphSubscriptions()
        {
            var transport = new Phase54FakeTransport();
            var session = new FoxgloveSession("phase54", transport);

            transport.Connect(99);
            transport.Text(99, "{\"op\":\"subscribeConnectionGraph\"}");
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/phase54/topic", Encoding = "json" });

            transport.Connect(1);
            transport.Text(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":10,\"channelId\":1}]}");
            transport.ClearTexts(99);

            session.UnregisterChannel(1);

            var graph = LastGraphUpdate(transport, 99);
            Check(graph != null, "54B-1: unregister channel broadcasts graph update");
            Check(!GraphHasSubscribedTopic(graph, "/phase54/topic"),
                "54B-1b: unregister channel removes subscribed-topic graph edge");
        }

        private static void VerifyDisconnectBroadcastsGraphCleanup()
        {
            var transport = new Phase54FakeTransport();
            var session = new FoxgloveSession("phase54", transport);

            transport.Connect(99);
            transport.Text(99, "{\"op\":\"subscribeConnectionGraph\"}");
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/phase54/live", Encoding = "json" });

            transport.Connect(1);
            transport.Text(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":77,\"channelId\":1}]}");
            transport.Text(1, "{\"op\":\"advertise\",\"channels\":[{\"id\":9,\"topic\":\"/phase54/client\",\"encoding\":\"json\"}]}");
            transport.ClearTexts(99);

            transport.Disconnect(1);

            var graph = LastGraphUpdate(transport, 99);
            Check(graph != null, "54B-2: disconnect broadcasts graph cleanup");
            Check(!GraphHasSubscribedTopic(graph, "/phase54/live"),
                "54B-2b: disconnect removes subscribed-topic graph edge");
            Check(!GraphHasPublishedTopic(graph, "/phase54/client"),
                "54B-2c: disconnect removes client-published graph edge");
        }

        private static void VerifyRuntimeRegisterServiceUpdatesGraph()
        {
            var transport = new Phase54FakeTransport();
            using var runtime = new FoxgloveRuntime(transport, new Phase54FixedClock(1), new DefaultSchemaRegistry());
            runtime.Start("phase54", "127.0.0.1", 0);

            transport.Connect(99);
            transport.Text(99, "{\"op\":\"subscribeConnectionGraph\"}");
            transport.ClearTexts(99);

            runtime.RegisterService(new ServiceDescriptor { Name = "/phase54/dynamic", Type = "phase54.Dynamic" });

            var graph = LastGraphUpdate(transport, 99);
            Check(graph != null, "54B-3: runtime service registration broadcasts graph update");
            Check(GraphHasAdvertisedService(graph, "/phase54/dynamic"),
                "54B-3b: runtime service registration appears in graph");
        }

        private static void VerifyBroadcastTimeAcceptsSubHzRate()
        {
            var session = new FoxgloveSession("phase54", new Phase54FakeTransport(), new Phase54FixedClock(123));
            Check(!Throws(() => session.BroadcastTime(0.5f)),
                "54C-1: BroadcastTime accepts sub-Hz positive rates");
        }

        private static void VerifyMalformedClientAdvertiseDoesNotRegisterChannel()
        {
            var transport = new Phase54FakeTransport();
            var session = new FoxgloveSession("phase54", transport);
            transport.Connect(1);

            transport.Text(1, "{\"op\":\"advertise\",\"channels\":[{\"id\":5,\"encoding\":\"json\"}]}");

            var received = false;
            session.OnClientMessage += (_, _, _, _) => received = true;
            transport.Binary(1, BuildClientMessageFrame(5, "bad"));

            Check(!received, "54C-2: malformed client advertise does not leave registered channel");
        }

        private static void VerifyChineseArchitectureDocsMatchCurrentCapabilities()
        {
            var doc = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/zh/10_架构说明.md");

            Check(!doc.Contains("Push broadcast deferred to Phase 7"),
                "54D-1: zh architecture doc no longer says parameter push is deferred");
            Check(!doc.Contains("No `time` capability declared"),
                "54D-1b: zh architecture doc no longer denies time capability");
            Check(doc.Contains("parametersSubscribe") && doc.Contains("connectionGraph") && doc.Contains("clientPublish"),
                "54D-1c: zh architecture doc lists current runtime capabilities");
        }

        private static byte[] BuildServiceCallFrame(uint serviceId, uint callId, string jsonPayload)
        {
            var encoding = Encoding.UTF8.GetBytes("json");
            var payload = Encoding.UTF8.GetBytes(jsonPayload);
            var frame = new byte[13 + encoding.Length + payload.Length];
            frame[0] = ClientOpcode.ServiceCallRequest;
            BinaryEncoding.WriteU32LE(frame, 1, serviceId);
            BinaryEncoding.WriteU32LE(frame, 5, callId);
            BinaryEncoding.WriteU32LE(frame, 9, (uint)encoding.Length);
            Buffer.BlockCopy(encoding, 0, frame, 13, encoding.Length);
            Buffer.BlockCopy(payload, 0, frame, 13 + encoding.Length, payload.Length);
            return frame;
        }

        private static byte[] BuildPlaybackControlFrame(string requestId)
        {
            var id = Encoding.UTF8.GetBytes(requestId);
            var frame = new byte[19 + id.Length];
            frame[0] = ClientOpcode.PlaybackControlRequest;
            frame[1] = 1;
            BinaryEncoding.WriteF32LE(frame, 2, 1f);
            frame[6] = 0;
            BinaryEncoding.WriteU64LE(frame, 7, 0);
            BinaryEncoding.WriteU32LE(frame, 15, (uint)id.Length);
            Buffer.BlockCopy(id, 0, frame, 19, id.Length);
            return frame;
        }

        private static byte[] BuildClientMessageFrame(uint channelId, string payload)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            var frame = new byte[5 + bytes.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, channelId);
            Buffer.BlockCopy(bytes, 0, frame, 5, bytes.Length);
            return frame;
        }

        private static JObject LastGraphUpdate(Phase54FakeTransport transport, uint clientId)
        {
            return transport.TextsFor(clientId)
                .Select(TryParseJson)
                .LastOrDefault(j => j?["op"]?.ToString() == "connectionGraphUpdate");
        }

        private static bool GraphHasSubscribedTopic(JObject graph, string topic)
        {
            return graph["subscribedTopics"] is JArray topics
                   && topics.OfType<JObject>().Any(t => t["name"]?.ToString() == topic);
        }

        private static bool GraphHasPublishedTopic(JObject graph, string topic)
        {
            return graph["publishedTopics"] is JArray topics
                   && topics.OfType<JObject>().Any(t => t["name"]?.ToString() == topic);
        }

        private static bool GraphHasAdvertisedService(JObject graph, string name)
        {
            return graph["advertisedServices"] is JArray services
                   && services.OfType<JObject>().Any(s => s["name"]?.ToString() == name);
        }

        private static bool IsFetchAssetError(byte[] frame, uint requestId)
        {
            return frame != null
                   && frame.Length >= 10
                   && frame[0] == ServerOpcode.FetchAssetResponse
                   && BinaryEncoding.ReadU32LE(frame, 1) == requestId
                   && frame[5] == 1;
        }

        private static JObject TryParseJson(string text)
        {
            try { return JObject.Parse(text); }
            catch { return null; }
        }

        private static bool Throws(Action action)
        {
            try { action(); return false; }
            catch { return true; }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception($"[FAIL] {label}");
            _passed++;
            Console.WriteLine($"[PASS] {label}");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }

        private sealed class Phase54RuntimeContext : IRuntimeContext
        {
            private bool _playbackEnabled;

            public bool PlaybackEnabled => _playbackEnabled;
            public FoxgloveAssetRegistry Assets { get; } = new();
            public int AppliedPlaybackControls { get; private set; }

            public void EnablePlayback() => _playbackEnabled = true;
            public ulong GetPlaybackStartNs() => 0;
            public ulong GetPlaybackEndNs() => 10_000_000_000;
            public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs) { }

            public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            {
                return new PlaybackClock.PlaybackStateSnapshot
                {
                    Status = 1,
                    CurrentTimeNs = 0,
                    Speed = 1f,
                    DidSeek = didSeek,
                    RequestId = requestId
                };
            }

            public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
                byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId)
            {
                AppliedPlaybackControls++;
                return GetPlaybackState(hasSeek, requestId);
            }

            public void ReplaySeek(ulong timeNs) { }
            public void ReplayPlay() { }
            public void ReplayPause() { }
        }

        private sealed class Phase54FixedClock : IFoxgloveClock
        {
            public Phase54FixedClock(ulong nowNs)
            {
                NowNs = nowNs;
            }

            public ulong NowNs { get; }
        }

        private sealed class Phase54FakeTransport : IFoxgloveTransport
        {
            private readonly HashSet<uint> _clients = new();
            private readonly Dictionary<uint, List<string>> _texts = new();
            private readonly Dictionary<uint, List<byte[]>> _binaries = new();

            public bool IsRunning { get; private set; }
            public List<byte[]> BroadcastBinaries { get; } = new();
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }

            public void BroadcastText(string json)
            {
                foreach (var clientId in _clients)
                    SendText(clientId, json);
            }

            public void BroadcastBinary(byte[] data)
            {
                BroadcastBinaries.Add(data);
                foreach (var clientId in _clients)
                    SendBinary(clientId, data);
            }

            public void SendText(uint clientId, string json)
            {
                if (!_texts.TryGetValue(clientId, out var list))
                    _texts[clientId] = list = new List<string>();
                list.Add(json);
            }

            public void SendBinary(uint clientId, byte[] data)
            {
                if (!_binaries.TryGetValue(clientId, out var list))
                    _binaries[clientId] = list = new List<byte[]>();
                list.Add(data);
            }

            public IReadOnlyList<string> TextsFor(uint clientId) =>
                _texts.TryGetValue(clientId, out var list) ? list : Array.Empty<string>();

            public IReadOnlyList<byte[]> BinariesFor(uint clientId) =>
                _binaries.TryGetValue(clientId, out var list) ? list : Array.Empty<byte[]>();

            public void ClearTexts(uint clientId)
            {
                if (_texts.TryGetValue(clientId, out var list))
                    list.Clear();
            }

            public void Connect(uint clientId)
            {
                _clients.Add(clientId);
                OnClientConnected?.Invoke(clientId);
            }

            public void Disconnect(uint clientId)
            {
                _clients.Remove(clientId);
                OnClientDisconnected?.Invoke(clientId);
            }

            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void Binary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
