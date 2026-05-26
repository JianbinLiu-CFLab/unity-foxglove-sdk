// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-2 session protocol and registry hardening.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_2Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-2: Session protocol and registry hardening ===");
            _passed = 0;

            VerifyClientAdvertiseBudgetRejectsOversizedBatch();
            VerifyClientSubscribeBudgetRejectsOversizedBatch();
            VerifyNamedParameterUnsubscribeRemovesEmptyClient();
            VerifyReregisterChannelRemovesStaleGraphTopic();
            VerifyAssetReadsFailClosedAndStayBounded();
            VerifySessionRegistryConcurrencyGuards();
            VerifySourceDeclaresBudgetPolicies();
            VerifyAtomicBudgetRejectionIsDocumented();

            Console.WriteLine($"Phase 134-2: {_passed} checks passed.");
        }

        private static void VerifyClientAdvertiseBudgetRejectsOversizedBatch()
        {
            var transport = new Phase134_2FakeTransport();
            var session = new FoxgloveSession("phase134-2", transport);
            var received = new List<uint>();
            session.OnClientMessage += (_, channelId, _, _) => received.Add(channelId);
            transport.Connect(7);

            transport.Text(7, JsonConvert.SerializeObject(new Advertise
            {
                Channels = new List<AdvertiseChannel>
                {
                    new AdvertiseChannel { Id = 1, Topic = "/client/valid", Encoding = "json" }
                }
            }));
            transport.Binary(7, BuildClientMessageFrame(1, "ok"));
            Check(received.SequenceEqual(new[] { 1u }),
                "134-2A-1: valid client advertise remains routable before overflow probe");
            received.Clear();

            var oversized = new List<AdvertiseChannel>();
            for (uint i = 2; i <= 301; i++)
                oversized.Add(new AdvertiseChannel { Id = i, Topic = "/client/over/" + i, Encoding = "json" });

            transport.Text(7, JsonConvert.SerializeObject(new Advertise { Channels = oversized }));
            transport.Binary(7, BuildClientMessageFrame(301, "must-not-route"));
            Check(received.Count == 0,
                "134-2A-2: oversized client advertise batch is rejected without partial channel registration");

            transport.Binary(7, BuildClientMessageFrame(1, "still-ok"));
            Check(received.SequenceEqual(new[] { 1u }),
                "134-2A-3: rejecting an oversized client advertise preserves existing valid channels");

            var largeSchema = new string('x', 1024 * 1024 + 1);
            transport.Text(7, JsonConvert.SerializeObject(new Advertise
            {
                Channels = new List<AdvertiseChannel>
                {
                    new AdvertiseChannel
                    {
                        Id = 500,
                        Topic = "/client/huge_schema",
                        Encoding = "json",
                        SchemaName = "Huge",
                        SchemaEncoding = "jsonschema",
                        Schema = largeSchema
                    }
                }
            }));
            received.Clear();
            transport.Binary(7, BuildClientMessageFrame(500, "must-not-route"));
            Check(received.Count == 0,
                "134-2A-4: client-published schema payload budget rejects oversized schemas");
        }

        private static void VerifyClientSubscribeBudgetRejectsOversizedBatch()
        {
            var transport = new Phase134_2FakeTransport();
            var session = new FoxgloveSession("phase134-2", transport);
            transport.Connect(9);

            for (uint i = 1; i <= 1101; i++)
                session.RegisterChannel(new AdvertiseChannel { Id = i, Topic = "/server/" + i, Encoding = "json" });

            transport.Text(9, JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription> { new Subscription { Id = 1, ChannelId = 1 } }
            }));
            session.Publish(1, Encoding.UTF8.GetBytes("{}"), 10);
            Check(transport.BinariesFor(9).Count == 1,
                "134-2B-1: valid client subscription remains routable before overflow probe");
            transport.ClearBinaries(9);

            var oversized = new List<Subscription>();
            for (uint i = 2; i <= 1101; i++)
                oversized.Add(new Subscription { Id = i, ChannelId = i });

            transport.Text(9, JsonConvert.SerializeObject(new SubscribeMessage { Subscriptions = oversized }));
            session.Publish(1101, Encoding.UTF8.GetBytes("{}"), 11);
            Check(transport.BinariesFor(9).Count == 0,
                "134-2B-2: oversized subscribe batch is rejected without partial subscription registration");

            session.Publish(1, Encoding.UTF8.GetBytes("{}"), 12);
            Check(transport.BinariesFor(9).Count == 1,
                "134-2B-3: rejecting an oversized subscribe batch preserves existing subscriptions");
        }

        private static void VerifyNamedParameterUnsubscribeRemovesEmptyClient()
        {
            var subs = new ParameterSubscriptionRegistry();
            subs.Subscribe(42, new[] { "/phase134/name" });
            Check(subs.GetSubscribedClientIds().Contains(42),
                "134-2C-1: named parameter subscribe records the client");
            subs.Unsubscribe(42, new[] { "/phase134/name" });
            Check(!subs.GetSubscribedClientIds().Contains(42),
                "134-2C-2: named parameter unsubscribe removes the client when the set becomes empty");
        }

        private static void VerifyReregisterChannelRemovesStaleGraphTopic()
        {
            var transport = new Phase134_2FakeTransport();
            var session = new FoxgloveSession("phase134-2", transport);
            transport.Connect(3);
            transport.Text(3, "{\"op\":\"subscribeConnectionGraph\"}");
            transport.ClearTexts(3);

            session.RegisterChannel(new AdvertiseChannel { Id = 77, Topic = "/phase134/old", Encoding = "json" });
            transport.ClearTexts(3);
            session.RegisterChannel(new AdvertiseChannel { Id = 77, Topic = "/phase134/new", Encoding = "json" });

            var graph = LastGraphUpdate(transport, 3);
            Check(graph != null, "134-2D-1: channel re-register broadcasts a graph update");
            Check(!GraphHasPublishedTopic(graph, "/phase134/old"),
                "134-2D-2: channel id re-register removes the stale published graph topic");
            Check(GraphHasPublishedTopic(graph, "/phase134/new"),
                "134-2D-3: channel id re-register advertises the replacement graph topic");
        }

        private static void VerifyAssetReadsFailClosedAndStayBounded()
        {
            var root = Path.Combine(Path.GetTempPath(), "u2f_phase134_2_assets_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "ok.txt"), "phase134-2");
                File.WriteAllBytes(Path.Combine(root, "too-large.bin"), new byte[16]);

                var assets = new FoxgloveAssetRegistry();
                assets.RegisterRoot("package://phase134/", root, maxBytes: 12);

                Check(assets.TryRead("package://phase134/ok.txt", out var bytes, out var error)
                      && Encoding.UTF8.GetString(bytes) == "phase134-2"
                      && string.IsNullOrEmpty(error),
                    "134-2E-1: fetchAsset reads valid files under the registered root");
                Check(!assets.TryRead("package://phase134/too-large.bin", out bytes, out error)
                      && bytes == null
                      && error.Contains("size limit", StringComparison.OrdinalIgnoreCase),
                    "134-2E-2: fetchAsset rejects oversized files without throwing");
                Check(!assets.TryRead("package://phase134/../outside.txt", out bytes, out error)
                      && bytes == null
                      && error.Contains("Path traversal", StringComparison.OrdinalIgnoreCase),
                    "134-2E-3: fetchAsset fails closed on path traversal attempts");

                var invalidRootRejected = false;
                try { assets.RegisterRoot("package://bad/", "   "); }
                catch (ArgumentException) { invalidRootRejected = true; }
                Check(invalidRootRejected,
                    "134-2E-4: asset root registration rejects empty local roots with a clear exception");
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        private static void VerifySessionRegistryConcurrencyGuards()
        {
            var assetRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Assets/FoxgloveAssetRegistry.cs");
            var graphHandler = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            var serviceRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceRegistry.cs");
            var parameterStore = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/FoxgloveParameterStore.cs");
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");

            Check(assetRegistry.Contains("TryReadResolvedFile", StringComparison.Ordinal)
                  && assetRegistry.Contains("FileShare.ReadWrite | FileShare.Delete", StringComparison.Ordinal)
                  && !assetRegistry.Contains("return File.ReadAllBytes(path);", StringComparison.Ordinal),
                "134-2F-1: fetchAsset uses bounded read-time enforcement instead of naked File.ReadAllBytes");
            Check(graphHandler.Contains("Interlocked.CompareExchange(ref _dirty, 0, 1)", StringComparison.Ordinal)
                  && graphHandler.Contains("Volatile.Write(ref _dirty, 1)", StringComparison.Ordinal)
                  && graphHandler.Contains("will retry after the next graph broadcast", StringComparison.Ordinal),
                "134-2F-2: connection graph metadata dirty state is only cleared for a write attempt and is restored on write failure");
            Check(!serviceRegistry.Contains("var id = Register(descriptor);", StringComparison.Ordinal)
                  && serviceRegistry.Contains("if (handler != null)", StringComparison.Ordinal),
                "134-2F-3: service descriptor and handler registration share a single critical section");
            Check(parameterStore.IndexOf("foreach (var name in names)", StringComparison.Ordinal)
                  < parameterStore.IndexOf("lock (_lock)", parameterStore.IndexOf("GetWireParameters", StringComparison.Ordinal), StringComparison.Ordinal),
                "134-2F-4: parameter name enumeration is materialized before taking the parameter store lock");
            Check(session.Contains("SendSessionSnapshot(clientId, chs, svcs)", StringComparison.Ordinal),
                "134-2F-5: client connect sends session snapshot and seeds graph from one channel/service snapshot");
        }

        private static void VerifySourceDeclaresBudgetPolicies()
        {
            var clientPublish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            var subscriptions = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            var sessionConnection = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Connection.cs");

            Check(clientPublish.Contains("MaxClientPublishedChannelsPerClient", StringComparison.Ordinal)
                  && clientPublish.Contains("MaxTotalClientPublishedChannels", StringComparison.Ordinal)
                  && clientPublish.Contains("MaxClientPublishedSchemaBytes", StringComparison.Ordinal),
                "134-2G-1: client publish handler declares per-client, total, and schema budgets");
            Check(subscriptions.Contains("MaxSubscriptionsPerClient", StringComparison.Ordinal)
                  && subscriptions.Contains("MaxTotalSubscriptions", StringComparison.Ordinal)
                  && subscriptions.Contains("TryAddSubscriptions", StringComparison.Ordinal),
                "134-2G-2: subscription registry declares bounded batch subscription API");
            Check(sessionConnection.Contains("WarnSubscriptionBudgetRejected", StringComparison.Ordinal),
                "134-2G-3: subscribe overflow path emits a bounded warning instead of partial state");
        }

        private static void VerifyAtomicBudgetRejectionIsDocumented()
        {
            var architecture = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/10_Architecture.md");

            Check(architecture.Contains("all-or-nothing", StringComparison.OrdinalIgnoreCase)
                  && architecture.Contains("subscribe", StringComparison.OrdinalIgnoreCase)
                  && architecture.Contains("client advertise", StringComparison.OrdinalIgnoreCase)
                  && architecture.Contains("budget", StringComparison.OrdinalIgnoreCase),
                "134-2H-1: architecture docs surface all-or-nothing subscribe and client advertise budget rejection");
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

        private static JObject LastGraphUpdate(Phase134_2FakeTransport transport, uint clientId)
        {
            return transport.TextsFor(clientId)
                .Select(TryParseJson)
                .LastOrDefault(j => j?["op"]?.ToString() == "connectionGraphUpdate");
        }

        private static bool GraphHasPublishedTopic(JObject graph, string topic)
        {
            return graph["publishedTopics"] is JArray topics
                   && topics.OfType<JObject>().Any(t => t["name"]?.ToString() == topic);
        }

        private static JObject TryParseJson(string text)
        {
            try { return JObject.Parse(text); }
            catch { return null; }
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

        private sealed class Phase134_2FakeTransport : IFoxgloveTransport
        {
            private readonly HashSet<uint> _clients = new();
            private readonly Dictionary<uint, List<string>> _texts = new();
            private readonly Dictionary<uint, List<byte[]>> _binaries = new();

            public bool IsRunning { get; private set; }
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

            public void ClearBinaries(uint clientId)
            {
                if (_binaries.TryGetValue(clientId, out var list))
                    list.Clear();
            }

            public void Connect(uint clientId)
            {
                _clients.Add(clientId);
                OnClientConnected?.Invoke(clientId);
            }

            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void Binary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
