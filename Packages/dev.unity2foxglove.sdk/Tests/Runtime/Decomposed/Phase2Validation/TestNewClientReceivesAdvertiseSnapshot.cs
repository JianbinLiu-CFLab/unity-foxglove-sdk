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
            AssertClientCanStillSubscribeAndReceive(session, fake, "Unknown op leaves client usable");
        }

        /// <summary>
        /// Receiving malformed JSON must not disconnect the client;
        /// the server must handle parse errors gracefully.
        /// </summary>
        private static void TestMalformedJsonDoesNotDisconnect()
        {
            var fake = new Phase2FakeTransport();
            var logger = new Phase2CaptureLogger();
            var session = new FoxgloveSession("Test", fake, logger: logger);
            fake.SimulateConnect(1);

            fake.SimulateText(1, "not json at all");
            Assert(logger.Warnings.Any(w => w.Contains("Malformed JSON from client 1", StringComparison.Ordinal)
                                            && w.Contains("JsonReaderException", StringComparison.Ordinal)),
                "Malformed JSON warning includes client id and parser exception type");
            AssertClientCanStillSubscribeAndReceive(session, fake, "Malformed JSON leaves client usable");
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
            var port = GetEphemeralTcpPort();
            runtime.Start("R1", "127.0.0.1", port);
            runtime.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t1", Encoding = "json" });
            var sid1 = runtime.Session.SessionId;
            runtime.Stop();

            // Restart with same transport on a fresh port to avoid OS-level
            // listener release races while still proving session state resets.
            var restartPort = GetEphemeralTcpPort();
            runtime.Start("R2", "127.0.0.1", restartPort);
            runtime.RegisterChannel(new AdvertiseChannel { Id = 2, Topic = "/t2", Encoding = "json" });
            Assert(runtime.Session.SessionId != sid1, "Restarted session has different SessionId");
            Assert(runtime.Session.Channels.Count == 1, "Restarted session has clean channel state");

            // Real connect: should receive only new session's serverInfo + one channel
            var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol(Subprotocol.SdkV1);
            var cts = new CancellationTokenSource(5000);
            ws.ConnectAsync(new Uri($"ws://127.0.0.1:{restartPort}/"), cts.Token).GetAwaiter().GetResult();
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

            CloseClientWebSocketForCleanup(ws);
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
            var port = GetEphemeralTcpPort();
            runtime.Start("RealPublishTest", "127.0.0.1", port);
            runtime.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(Subprotocol.SdkV1);
                var cts = new CancellationTokenSource(10000);
                ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token).GetAwaiter().GetResult();
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

                Assert(SpinUntil(() => runtime.Session.HasChannelDemand(1), 2000),
                    "Integration: subscribe processed before publish");

                // Publish payload
                var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
                runtime.Publish(1, payload);

                // Receive binary MessageData frame
                var r3 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                Assert(r3.MessageType == WebSocketMessageType.Binary, "Integration: received binary MessageData");

                // Decode frame: opcode(1) + subscriptionId(4 LE) + logTime(8 LE) + payload
                var subId = BitConverter.ToUInt32(buf, 1);
                AssertEqual(100u, subId, "Integration: subscriptionId=100 in binary frame");
                var payloadText = Encoding.UTF8.GetString(buf, 13, r3.Count - 13);
                Assert(payloadText == "{\"x\":1}", "Integration: payload roundtrips");

                // Send unsubscribe
                var unsubJson = "{\"op\":\"unsubscribe\",\"subscriptionIds\":[100]}";
                var unsubBytes = Encoding.UTF8.GetBytes(unsubJson);
                ws.SendAsync(new ArraySegment<byte>(unsubBytes), WebSocketMessageType.Text, true, cts.Token)
                    .GetAwaiter().GetResult();
                Assert(SpinUntil(() => !runtime.Session.HasChannelDemand(1), 2000),
                    "Integration: unsubscribe processed before negative publish");

                // Publish again — should not receive
                runtime.Publish(1, payload);

                // Use a cancellable receive so the negative assertion does not
                // leave a pending ReceiveAsync that later aborts cleanup.
                using var noMessageCts = new CancellationTokenSource();
                var recvTask = ws.ReceiveAsync(seg, noMessageCts.Token);
                var timeout = Task.Delay(2000);
                var winner = Task.WhenAny(recvTask, timeout).GetAwaiter().GetResult();
                Assert(winner == timeout, "Integration: no message after unsubscribe");
                if (winner == timeout)
                    CancelAndObserveReceive(noMessageCts, recvTask);

                CloseClientWebSocketForCleanup(ws);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        private static void CloseClientWebSocketForCleanup(ClientWebSocket ws)
        {
            if (ws == null)
                return;

            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (WebSocketException)
            {
                // CI can observe the TCP stream closing before the WebSocket
                // close handshake completes. Protocol assertions have already
                // run, so this is cleanup rather than test behavior.
            }
            catch (OperationCanceledException)
            {
                // Cancellation during cleanup should not fail completed protocol assertions.
            }
            catch (ObjectDisposedException)
            {
                // Already disposed is cleanup.
            }
            catch (InvalidOperationException)
            {
                // Already closing/closed is also cleanup.
            }
            finally
            {
                ws.Dispose();
            }
        }

        private static void AssertClientCanStillSubscribeAndReceive(
            FoxgloveSession session,
            Phase2FakeTransport fake,
            string label)
        {
            session.RegisterChannel(new AdvertiseChannel { Id = 77, Topic = "/stability", Encoding = "json" });
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":991,\"channelId\":77}]}");
            session.Publish(77, Encoding.UTF8.GetBytes("{\"ok\":true}"));
            Assert(fake.SentBinaries(1).Count == 1, label);
        }

        private static int GetEphemeralTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static bool SpinUntil(Func<bool> condition, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }

            return condition();
        }

        private static void CancelAndObserveReceive(
            CancellationTokenSource cts,
            Task<WebSocketReceiveResult> recvTask)
        {
            try { cts.Cancel(); } catch { }
            try { recvTask.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
