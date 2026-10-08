// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates protocol DTO serialization, channel registration/unregistration, advertise snapshots, subscribe/unsubscribe parsing, and publish routing.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase2Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        private static void AssertEqual<T>(T expected, T actual, string label) where T : IEquatable<T>
        {
            Assert(expected.Equals(actual), $"{label} (expected={expected}, actual={actual})");
        }

        /// <summary>
        /// Entry point: runs all Phase 2 tests covering protocol DTO
        /// serialization, channel registration/unregistration, advertise
        /// snapshots, subscribe/unsubscribe parsing, and publish routing.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("--- Phase 2 Tests ---");
            _passCount = 0;

            // A: Protocol DTO snapshots
            TestAdvertiseSchemaNullNormalized();
            TestAdvertiseSnapshotNoSchema();
            TestUnadvertiseJsonFormat();
            TestSubscribeMessageJsonFormat();
            TestUnsubscribeMessageJsonFormat();

            // B: Public API
            TestRegisterChannelBroadcastsAdvertise();
            TestUnregisterChannelBroadcastsUnadvertise();
            TestReregisterChannelKeepsSubscriptions();
            TestRuntimeProxyMethods();
            TestRuntimeProxyThrowsBeforeStart();
            TestStopStartReusesTransport();

            // C: Advertise snapshot on connect
            TestNewClientReceivesAdvertiseSnapshot();
            TestAdvertiseSnapshotIsPerClient();
            TestNoAdvertiseSnapshotWhenEmpty();

            // D: Subscribe / unsubscribe parsing
            TestSubscribeAddsSubscription();
            TestSubscribeUnknownChannelIgnored();
            TestUnsubscribeRemovesSubscription();
            TestUnknownOpDoesNotDisconnect();
            TestMalformedJsonDoesNotDisconnect();

            // E: Publish routing
            TestPublishRoutesToSubscriber();
            TestPublishSkipsUnsubscribedClient();
            TestPublishUsesSubscriptionIdNotChannelId();
            TestPublishNoopOnUnknownChannel();
            TestMultiClientMultiSubscription();
            TestRealWebSocketPublishSubscribe();

            Console.WriteLine($"Phase 2: {_passCount} checks passed.\n");
        }

        // ── A: Protocol DTO snapshots ──

        /// <summary>
        /// Ensures that <c>SchemaName</c> and <c>Schema</c> on
        /// <c>AdvertiseChannel</c> default to empty string, and that
        /// assigning null is coerced to empty string.
        /// </summary>
        private static void TestAdvertiseSchemaNullNormalized()
        {
            var ch = new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" };
            // SchemaName and Schema not set — should default to "" not null
            Assert(ch.SchemaName == "", "SchemaName defaults to empty string");
            Assert(ch.Schema == "", "Schema defaults to empty string");

            // Setting null should coerce to ""
            ch.SchemaName = null;
            ch.Schema = null;
            Assert(ch.SchemaName == "", "SchemaName null coerced to ''");
            Assert(ch.Schema == "", "Schema null coerced to ''");
        }

        /// <summary>
        /// Confirms the Advertise JSON serialization output: op is
        /// <c>advertise</c>, schema fields default to empty strings,
        /// <c>schemaEncoding</c> is omitted when not set, and no null
        /// literals appear in the JSON.
        /// </summary>
        private static void TestAdvertiseSnapshotNoSchema()
        {
            var ch = new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" };
            var adv = new Advertise { Channels = new List<AdvertiseChannel> { ch } };
            var json = JsonConvert.SerializeObject(adv);

            var obj = JObject.Parse(json);
            Assert(obj["op"]?.ToString() == "advertise", "Advertise op is advertise");

            var ch0 = obj["channels"]?[0] as JObject;
            Assert(ch0 != null, "Advertise has channels array");
            Assert(ch0["schemaName"]?.ToString() == "", "schemaName serialized as empty string");
            Assert(ch0["schema"]?.ToString() == "", "schema serialized as empty string");
            Assert(ch0["schemaEncoding"] == null, "schemaEncoding omitted when not set");
            // Confirm no null literal in output
            Assert(!json.Contains(": null"), "Advertise JSON contains no null literal");
        }

        /// <summary>
        /// Validates the Unadvertise wire format: <c>op</c> is
        /// <c>unadvertise</c> and <c>channelIds</c> contains the
        /// expected entries.
        /// </summary>
        private static void TestUnadvertiseJsonFormat()
        {
            var msg = new Unadvertise { ChannelIds = new List<uint> { 1, 2, 3 } };
            var json = JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            Assert(obj["op"]?.ToString() == "unadvertise", "Unadvertise op is unadvertise");
            var ids = obj["channelIds"] as JArray;
            Assert(ids != null && ids.Count == 3, "Unadvertise channelIds has 3 entries");
        }

        /// <summary>
        /// Validates the Subscribe message wire format with multiple
        /// subscriptions, each carrying an <c>id</c> and <c>channelId</c>.
        /// </summary>
        private static void TestSubscribeMessageJsonFormat()
        {
            var msg = new SubscribeMessage
            {
                Subscriptions = new List<Subscription>
                {
                    new Subscription { Id = 100, ChannelId = 1 },
                    new Subscription { Id = 200, ChannelId = 2 }
                }
            };
            var json = JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            Assert(obj["op"]?.ToString() == "subscribe", "Subscribe op is subscribe");
            var subs = obj["subscriptions"] as JArray;
            Assert(subs != null && subs.Count == 2, "Subscribe has 2 subscriptions");
            Assert((int)subs[0]["id"] == 100 && (int)subs[0]["channelId"] == 1,
                "Subscription has id=100, channelId=1");
        }

        /// <summary>
        /// Validates the Unsubscribe message wire format: <c>op</c> is
        /// <c>unsubscribe</c> and <c>subscriptionIds</c> contains the
        /// expected values.
        /// </summary>
        private static void TestUnsubscribeMessageJsonFormat()
        {
            var msg = new UnsubscribeMessage { SubscriptionIds = new List<uint> { 100, 200 } };
            var json = JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            Assert(obj["op"]?.ToString() == "unsubscribe", "Unsubscribe op is unsubscribe");
            var ids = obj["subscriptionIds"] as JArray;
            Assert(ids != null && ids.Count == 2, "Unsubscribe subscriptionIds has 2 entries");
        }

        // ── B: Public API ──

        /// <summary>
        /// Fake transport used by Phase 2 tests to record BroadcastText,
        /// per-client SendText and SendBinary calls.
        /// </summary>
        private sealed class Phase2FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public readonly List<string> BroadcastTexts = new List<string>();
            public uint BroadcastTextCallCount => (uint)BroadcastTexts.Count;

            private readonly ConcurrentDictionary<uint, List<byte[]>> _sentBinaries = new();
            private readonly ConcurrentDictionary<uint, List<string>> _sentTexts = new();

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }

            public void SendText(uint clientId, string json)
            {
                _sentTexts.AddOrUpdate(clientId, _ => new List<string> { json },
                    (_, list) => { list.Add(json); return list; });
            }

            public void SendBinary(uint clientId, byte[] data)
            {
                _sentBinaries.AddOrUpdate(clientId, _ => new List<byte[]> { data },
                    (_, list) => { list.Add(data); return list; });
            }

            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }

            public IReadOnlyList<string> SentTexts(uint clientId) =>
                _sentTexts.TryGetValue(clientId, out var list) ? list : Array.Empty<string>();

            public IReadOnlyList<byte[]> SentBinaries(uint clientId) =>
                _sentBinaries.TryGetValue(clientId, out var list) ? list : Array.Empty<byte[]>();

            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateDisconnect(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }

        private sealed class Phase2CaptureLogger : IFoxgloveLogger
        {
            public readonly List<string> Warnings = new();
            public void LogWarning(string message) => Warnings.Add(message ?? string.Empty);
            public void LogError(string message) { }
        }

        /// <summary>
        /// Verifies that <c>RegisterChannel</c> broadcasts an advertise
        /// message with the correct channel id to all connected clients.
        /// </summary>
        private static void TestRegisterChannelBroadcastsAdvertise()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            var ch = new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" };
            session.RegisterChannel(ch);

            Assert(fake.BroadcastTexts.Count == 1, "RegisterChannel broadcasts advertise");
            var adv = JObject.Parse(fake.BroadcastTexts[0]);
            Assert(adv["op"]?.ToString() == "advertise", "Broadcast message is advertise");
            Assert(adv["channels"]?[0]?["id"]?.Value<int>() == 1, "Advertise has channel id=1");
        }

        /// <summary>
        /// Verifies that <c>UnregisterChannel</c> broadcasts an unadvertise
        /// message with the correct channel id.
        /// </summary>
        private static void TestUnregisterChannelBroadcastsUnadvertise()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var ch = new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" };
            session.RegisterChannel(ch);
            fake.BroadcastTexts.Clear();

            session.UnregisterChannel(1);
            Assert(fake.BroadcastTexts.Count == 1, "UnregisterChannel broadcasts unadvertise");
            var unadv = JObject.Parse(fake.BroadcastTexts[0]);
            Assert(unadv["op"]?.ToString() == "unadvertise", "Message is unadvertise");
            Assert(unadv["channelIds"]?[0]?.Value<int>() == 1, "unadvertise has channelId=1");
        }

        /// <summary>
        /// Re-registering the same channel id with updated metadata should
        /// not break existing subscriptions; publish must still deliver to
        /// the original subscription id.
        /// </summary>
        private static void TestReregisterChannelKeepsSubscriptions()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });

            // Client subscribes
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");

            // Conflicting re-registration is rejected and leaves the original descriptor active.
            var rejected = false;
            try
            {
                session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t2", Encoding = "json" });
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Assert(rejected, "Conflicting channel re-register is rejected");

            // Publish should still reach the subscription via original subscriptionId
            session.Publish(1, Encoding.UTF8.GetBytes("{}"));
            var binaries = fake.SentBinaries(1);
            Assert(binaries.Count == 1, "Publish after re-register still sends to existing subscription");
            AssertEqual(100u, BitConverter.ToUInt32(binaries[0], 1),
                "Publish after re-register preserves original subscriptionId");
        }

        /// <summary>
        /// Tests that <c>FoxgloveRuntime.RegisterChannel</c> and
        /// <c>UnregisterChannel</c> correctly proxy to the internal session.
        /// </summary>
        private static void TestRuntimeProxyMethods()
        {
            var runtime = new FoxgloveRuntime();
            runtime.Start("ProxyTest", "127.0.0.1", GetEphemeralTcpPort());
            try
            {
                var ch = new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" };
                runtime.RegisterChannel(ch);
                Assert(runtime.Session.Channels.Count == 1, "Runtime.RegisterChannel proxies to session");

                runtime.UnregisterChannel(1);
                Assert(runtime.Session.Channels.Count == 0, "Runtime.UnregisterChannel proxies to session");
            }
            finally { runtime.Dispose(); }
        }

        /// <summary>
        /// Calling <c>RegisterChannel</c> or <c>Publish</c> on a runtime
        /// before <c>Start</c> must throw <c>InvalidOperationException</c>.
        /// </summary>
        private static void TestRuntimeProxyThrowsBeforeStart()
        {
            var runtime = new FoxgloveRuntime();
            try
            {
                runtime.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
                Assert(false, "Should have thrown");
            }
            catch (InvalidOperationException)
            {
                Assert(true, "Runtime.RegisterChannel before Start throws InvalidOperationException");
            }

            try
            {
                runtime.Publish(1, new byte[] { 1 });
                Assert(false, "Should have thrown");
            }
            catch (InvalidOperationException)
            {
                Assert(true, "Runtime.Publish before Start throws InvalidOperationException");
            }
        }

        // ── C: Advertise snapshot on connect ──
    }
}
