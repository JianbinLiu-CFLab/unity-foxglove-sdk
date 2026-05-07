// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates protocol DTO serialization, channel registration/unregistration, advertise snapshots, subscribe/unsubscribe parsing, and publish routing.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public static class Phase2Validation
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

            public List<string> SentTexts(uint clientId) =>
                _sentTexts.TryGetValue(clientId, out var list) ? list : new List<string>();

            public List<byte[]> SentBinaries(uint clientId) =>
                _sentBinaries.TryGetValue(clientId, out var list) ? list : new List<byte[]>();

            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateDisconnect(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
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

            // Re-register same channel (e.g. updated schema)
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t2", Encoding = "json" });

            // Publish should still reach the subscription via original subscriptionId
            session.Publish(1, Encoding.UTF8.GetBytes("{}"));
            var binaries = fake.SentBinaries(1);
            Assert(binaries.Count == 1, "Publish after re-register still sends to existing subscription");
        }

        /// <summary>
        /// Tests that <c>FoxgloveRuntime.RegisterChannel</c> and
        /// <c>UnregisterChannel</c> correctly proxy to the internal session.
        /// </summary>
        private static void TestRuntimeProxyMethods()
        {
            var runtime = new FoxgloveRuntime();
            runtime.Start("ProxyTest", "127.0.0.1", 18770);
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

        /// <summary>
        /// When a client connects after channels have been registered, it
        /// must receive serverInfo followed by an advertise snapshot
        /// containing all currently registered channels.
        /// </summary>
        private static void TestNewClientReceivesAdvertiseSnapshot()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);

            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t1", Encoding = "json" });
            session.RegisterChannel(new AdvertiseChannel { Id = 2, Topic = "/t2", Encoding = "json" });

            // Simulate late-connecting client
            fake.SimulateConnect(1);

            var texts = fake.SentTexts(1);
            Assert(texts.Count >= 2, "Client receives serverInfo + advertise snapshot");

            var second = JObject.Parse(texts[1]);
            Assert(second["op"]?.ToString() == "advertise", "Second message is advertise");
            Assert(second["channels"] is JArray arr && arr.Count == 2,
                "Advertise snapshot includes both channels");
        }

        /// <summary>
        /// Each newly-connected client receives its own advertise snapshot
        /// via <c>SendText</c>, not a shared broadcast.
        /// </summary>
        private static void TestAdvertiseSnapshotIsPerClient()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t1", Encoding = "json" });

            fake.SimulateConnect(1);
            fake.SimulateConnect(2);

            // Each client gets their own SendText
            Assert(fake.SentTexts(1).Count >= 2, "Client 1 got snapshot");
            Assert(fake.SentTexts(2).Count >= 2, "Client 2 got snapshot");
        }

        /// <summary>
        /// When no channels are registered, connecting clients receive only
        /// serverInfo, with no advertise snapshot.
        /// </summary>
        private static void TestNoAdvertiseSnapshotWhenEmpty()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);

            fake.SimulateConnect(1);
            var texts = fake.SentTexts(1);
            // Only serverInfo, no advertise
            Assert(texts.Count == 1, "Empty channels: only serverInfo, no advertise snapshot");
        }

        // ── D: Subscribe / unsubscribe parsing ──

        /// <summary>
        /// A client subscribe message must create a subscription entry so
        /// that subsequent <c>Publish</c> calls deliver binary frames.
        /// </summary>
        private static void TestSubscribeAddsSubscription()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            fake.SimulateConnect(1);

            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");

            byte[] payload = Encoding.UTF8.GetBytes("{\"x\":1}");
            session.Publish(1, payload);
            Assert(fake.SentBinaries(1).Count == 1, "Subscribe enables Publish delivery");
        }

        /// <summary>
        /// A subscribe to a non-existent channel id must be silently
        /// ignored and must not cause phantom message deliveries.
        /// </summary>
        private static void TestSubscribeUnknownChannelIgnored()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            // Subscribe to non-existent channel
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":999}]}");

            // Publish on existing channel should not produce phantom deliveries
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            session.Publish(1, Encoding.UTF8.GetBytes("{}"));
            Assert(fake.SentBinaries(1).Count == 0, "Unknown channel subscription produces no messages");
        }

        /// <summary>
        /// An unsubscribe message must remove the subscription entry,
        /// stopping further message delivery for that subscription id.
        /// </summary>
        private static void TestUnsubscribeRemovesSubscription()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            fake.SimulateConnect(1);

            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");
            fake.SimulateText(1, "{\"op\":\"unsubscribe\",\"subscriptionIds\":[100]}");

            session.Publish(1, Encoding.UTF8.GetBytes("{}"));
            Assert(fake.SentBinaries(1).Count == 0, "Unsubscribe stops delivery");
        }

        /// <summary>
        /// Receiving a JSON message with an unknown <c>op</c> value must
        /// not disconnect or throw, preserving client connection stability.
        /// </summary>
        private static void TestUnknownOpDoesNotDisconnect()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            // Should not throw or disconnect
            fake.SimulateText(1, "{\"op\":\"unknownOp\",\"data\":{}}");
            Assert(true, "Unknown op does not disconnect client");
        }

        /// <summary>
        /// Receiving malformed JSON must not disconnect the client;
        /// the server must handle parse errors gracefully.
        /// </summary>
        private static void TestMalformedJsonDoesNotDisconnect()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            fake.SimulateText(1, "not json at all");
            Assert(true, "Malformed JSON does not disconnect client");
        }

        // ── E: Publish routing ──

        /// <summary>
        /// A <c>Publish</c> on a registered channel must send exactly one
        /// binary frame to the subscribed client.
        /// </summary>
        private static void TestPublishRoutesToSubscriber()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");

            var payload = Encoding.UTF8.GetBytes("hello");
            session.Publish(1, payload);
            Assert(fake.SentBinaries(1).Count == 1, "Publish sends one binary frame");
        }

        /// <summary>
        /// Publish must deliver to the subscribed client only; an
        /// unsubscribed client on the same channel must receive nothing.
        /// </summary>
        private static void TestPublishSkipsUnsubscribedClient()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            fake.SimulateConnect(1);
            fake.SimulateConnect(2);
            // Only client 1 subscribes
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");

            session.Publish(1, Encoding.UTF8.GetBytes("{}"));
            Assert(fake.SentBinaries(1).Count == 1, "Subscribed client 1 receives");
            Assert(fake.SentBinaries(2).Count == 0, "Unsubscribed client 2 gets nothing");
        }

        /// <summary>
        /// The binary MessageData frame must use the client's
        /// <c>subscriptionId</c> rather than the server-side
        /// <c>channelId</c>.
        /// </summary>
        private static void TestPublishUsesSubscriptionIdNotChannelId()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");

            session.Publish(1, Encoding.UTF8.GetBytes("x"));
            var frame = fake.SentBinaries(1)[0];

            // BinaryEncoding.EncodeServerMessageData format: opcode(1) + subId(4 LE) + logTime(8 LE) + payload
            var subId = BitConverter.ToUInt32(frame, 1);
            AssertEqual(100u, subId, "Binary frame uses subscriptionId (100), not channelId (1)");
        }

        /// <summary>
        /// Publishing on an unregistered channel must be a no-op (no
        /// throw, no messages sent).
        /// </summary>
        private static void TestPublishNoopOnUnknownChannel()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            // Publish on unregistered channel — should not throw
            session.Publish(999, Encoding.UTF8.GetBytes("{}"));
            Assert(fake.SentBinaries(1).Count == 0, "Publish on unknown channel is no-op");
        }

        /// <summary>
        /// With two clients subscribing with different subscription ids
        /// on the same channel, each published frame must carry the
        /// correct per-client subscription id.
        /// </summary>
        private static void TestMultiClientMultiSubscription()
        {
            var fake = new Phase2FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            fake.SimulateConnect(1);
            fake.SimulateConnect(2);

            // Both subscribe with different subscriptionIds
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":11,\"channelId\":1}]}");
            fake.SimulateText(2, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":22,\"channelId\":1}]}");

            session.Publish(1, Encoding.UTF8.GetBytes("x"));

            // Each client's frame has its own subscriptionId
            var f1 = fake.SentBinaries(1)[0];
            var f2 = fake.SentBinaries(2)[0];
            AssertEqual(11u, BitConverter.ToUInt32(f1, 1), "Client 1 frame uses subId=11");
            AssertEqual(22u, BitConverter.ToUInt32(f2, 1), "Client 2 frame uses subId=22");
        }

        // ── P1: Stop/Start restart with same transport ──

        /// <summary>
        /// Stop/Start cycle must reuse the same transport instance without
        /// leaking old session event handlers, produce a fresh SessionId,
        /// and serve only the newly registered channels.
        /// </summary>
        private static void TestStopStartReusesTransport()
        {
            var transport = new ManagedWsBackend();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("R1", "127.0.0.1", 18780);
            runtime.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t1", Encoding = "json" });
            var sid1 = runtime.Session.SessionId;
            runtime.Stop();

            // Restart with same transport — should not leak old session event handlers
            runtime.Start("R2", "127.0.0.1", 18780);
            runtime.RegisterChannel(new AdvertiseChannel { Id = 2, Topic = "/t2", Encoding = "json" });
            Assert(runtime.Session.SessionId != sid1, "Restarted session has different SessionId");
            Assert(runtime.Session.Channels.Count == 1, "Restarted session has clean channel state");

            // Real connect: should receive only new session's serverInfo + one channel
            var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol(Subprotocol.SdkV1);
            var cts = new CancellationTokenSource(5000);
            ws.ConnectAsync(new Uri("ws://127.0.0.1:18780/"), cts.Token).GetAwaiter().GetResult();
            Assert(ws.State == WebSocketState.Open, "Restart: WebSocket connected");

            var buf = new byte[4096];
            var seg = new ArraySegment<byte>(buf);

            // serverInfo — should belong to new session
            var r1 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
            var info = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r1.Count));
            Assert(info["sessionId"]?.ToString() == runtime.Session.SessionId,
                "Restart: serverInfo has new sessionId");

            // advertise — should have exactly one channel (the new one)
            var r2 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
            var adv = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r2.Count));
            var channels = adv["channels"] as JArray;
            Assert(channels?.Count == 1, "Restart: advertise has exactly 1 channel");
            Assert(channels[0]["topic"]?.ToString() == "/t2", "Restart: channel is /t2 (not old /t1)");

            ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            runtime.Dispose();
        }

        // ── P2: Real ClientWebSocket integration: subscribe → Publish → binary MessageData ──

        /// <summary>
        /// Full integration test over a real WebSocket: subscribe, publish,
        /// receive binary MessageData with correct subscription id and
        /// payload, then unsubscribe and verify no further messages arrive.
        /// </summary>
        private static void TestRealWebSocketPublishSubscribe()
        {
            using var runtime = new FoxgloveRuntime();
            runtime.Start("RealPublishTest", "127.0.0.1", 18781);
            runtime.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(Subprotocol.SdkV1);
                var cts = new CancellationTokenSource(10000);
                ws.ConnectAsync(new Uri("ws://127.0.0.1:18781/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "Integration: WebSocket connected");

                // Read serverInfo
                var buf = new byte[4096];
                var seg = new ArraySegment<byte>(buf);
                var r1 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                var info = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r1.Count));
                Assert(info["op"]?.ToString() == "serverInfo", "Integration: got serverInfo");

                // Read advertise
                var r2 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                var adv = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r2.Count));
                Assert(adv["op"]?.ToString() == "advertise", "Integration: got advertise");
                Assert(adv["channels"]?[0]?["topic"]?.ToString() == "/t", "Integration: channel topic matches");

                // Send subscribe
                var subJson = "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}";
                var subBytes = Encoding.UTF8.GetBytes(subJson);
                ws.SendAsync(new ArraySegment<byte>(subBytes), WebSocketMessageType.Text, true, cts.Token)
                    .GetAwaiter().GetResult();

                // Give server a moment to process subscribe
                Task.Delay(100).Wait();

                // Publish payload
                var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
                runtime.Publish(1, payload);

                // Receive binary MessageData frame
                var r3 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                Assert(r3.MessageType == WebSocketMessageType.Binary, "Integration: received binary MessageData");

                // Decode frame: opcode(1) + subscriptionId(4 LE) + logTime(8 LE) + payload
                var frame = new byte[r3.Count];
                Array.Copy(buf, 0, frame, 0, r3.Count);
                var subId = BitConverter.ToUInt32(frame, 1);
                AssertEqual(100u, subId, "Integration: subscriptionId=100 in binary frame");
                var payloadText = Encoding.UTF8.GetString(frame, 13, frame.Length - 13);
                Assert(payloadText == "{\"x\":1}", "Integration: payload roundtrips");

                // Send unsubscribe
                var unsubJson = "{\"op\":\"unsubscribe\",\"subscriptionIds\":[100]}";
                var unsubBytes = Encoding.UTF8.GetBytes(unsubJson);
                ws.SendAsync(new ArraySegment<byte>(unsubBytes), WebSocketMessageType.Text, true, cts.Token)
                    .GetAwaiter().GetResult();
                Task.Delay(100).Wait();

                // Publish again — should not receive
                runtime.Publish(1, payload);
                Task.Delay(200).Wait();

                // Use Task.WhenAny to check no message arrives without aborting the WebSocket
                var recvTask = ws.ReceiveAsync(seg, CancellationToken.None);
                var timeout = Task.Delay(800);
                var winner = Task.WhenAny(recvTask, timeout).GetAwaiter().GetResult();
                Assert(winner == timeout, "Integration: no message after unsubscribe");

                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                runtime.Dispose();
            }
        }
    }
}
