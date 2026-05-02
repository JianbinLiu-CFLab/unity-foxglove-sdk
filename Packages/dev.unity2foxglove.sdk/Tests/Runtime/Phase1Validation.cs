using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase1Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
            }
            else
            {
                throw new Exception($"[FAIL] {label}");
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string label) where T : IEquatable<T>
        {
            Assert(expected.Equals(actual), $"{label} (expected={expected}, actual={actual})");
        }

        public static void Validate()
        {
            Console.WriteLine("--- Phase 1 Tests ---");

            TestFakeTransportServerInfo();
            TestServerInfoJsonContent();
            TestSessionIdStable();
            TestRealWebSocketConnect();
            TestBadSubprotocolRejected();

            Console.WriteLine($"Phase 1: {_passCount} checks passed.\n");
        }

        // ── G.1 Fake transport: serverInfo is per-client SendText ──

        private static void TestFakeTransportServerInfo()
        {
            var fake = new FakeTransport();
            var session = new FoxgloveSession("FakeTest", fake);

            // Simulate two clients connecting
            fake.SimulateConnect(1);
            fake.SimulateConnect(2);

            // Both should have received SendText, NOT BroadcastText
            AssertEqual(1u, fake.SentTextCount(1), "Client 1 received one SendText");
            AssertEqual(1u, fake.SentTextCount(2), "Client 2 received one SendText");
            AssertEqual(0u, fake.BroadcastTextCallCount, "No BroadcastText calls");

            // Each serverInfo should contain the session name
            var json1 = fake.LastSentText(1);
            var json2 = fake.LastSentText(2);
            Assert(json1.Contains("\"FakeTest\""), "Client 1 serverInfo has correct name");
            Assert(json2.Contains("\"FakeTest\""), "Client 2 serverInfo has correct name");
        }

        // ── G.2 serverInfo JSON content assertions ──

        private static void TestServerInfoJsonContent()
        {
            var fake = new FakeTransport();
            var session = new FoxgloveSession("ContentTest", fake);
            fake.SimulateConnect(1);

            var json = fake.LastSentText(1);
            var obj = JObject.Parse(json);

            Assert(obj["op"]?.ToString() == "serverInfo", "op is serverInfo");
            Assert(obj["name"]?.ToString() == "ContentTest", "name matches session name");
            Assert(obj["sessionId"]?.ToString()?.Length > 0, "sessionId is non-empty");

            // Phase 6: capabilities include parameters, services (2 items)
            var caps = obj["capabilities"] as JArray;
            Assert(caps != null && caps.Count >= 2, "capabilities is non-empty (Phase 6)");

            // Phase 6: supportedEncodings includes json
            var encs = obj["supportedEncodings"] as JArray;
            Assert(encs != null && encs.ToString().Contains("json"), "supportedEncodings includes json");

            // metadata must NOT be present
            Assert(obj["metadata"] == null, "metadata omitted when empty");

            // Phase 8: clientPublish is now declared
            // Phase 7: time capability is now declared
        }

        // ── G.3 SessionId is stable across clients ──

        private static void TestSessionIdStable()
        {
            var fake = new FakeTransport();
            var session = new FoxgloveSession("IdTest", fake);

            fake.SimulateConnect(1);
            fake.SimulateConnect(2);

            var sid1 = (string)JObject.Parse(fake.LastSentText(1))["sessionId"];
            var sid2 = (string)JObject.Parse(fake.LastSentText(2))["sessionId"];

            AssertEqual(sid1, sid2, "SessionId same for all clients in same session");

            // New session = new id
            var session2 = new FoxgloveSession("IdTest2", new FakeTransport());
            Assert(!session2.SessionId.Equals(session.SessionId),
                "New FoxgloveSession generates new SessionId");
        }

        // ── G.4 Real ClientWebSocket integration test ──

        private static void TestRealWebSocketConnect()
        {
            using var runtime = new FoxgloveRuntime();
            runtime.Start("IntegrationTest", "127.0.0.1", 18765);

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(Subprotocol.SdkV1);

                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri("ws://127.0.0.1:18765/"), cts.Token).GetAwaiter().GetResult();

                Assert(ws.State == WebSocketState.Open, "WebSocket connected");
                Assert(ws.SubProtocol == Subprotocol.SdkV1, "Server echoed subprotocol foxglove.sdk.v1");

                // Read the first message (should be serverInfo)
                var buffer = new byte[4096];
                Task.Delay(100).Wait(); // let server finish sending serverInfo
                var result = ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).GetAwaiter().GetResult();

                Assert(result.MessageType == WebSocketMessageType.Text, "First message is text (serverInfo)");

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var obj = JObject.Parse(json);

                Assert(obj["op"]?.ToString() == "serverInfo", "Integration: op is serverInfo");
                Assert(obj["name"]?.ToString() == "IntegrationTest", "Integration: name matches");

                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                runtime.Dispose();
            }
        }

        // ── G.5 Negative: wrong subprotocol rejected ─

        private static void TestBadSubprotocolRejected()
        {
            using var runtime = new FoxgloveRuntime();
            runtime.Start("BadSubprotocolTest", "127.0.0.1", 18766);

            try
            {
                var ws = new ClientWebSocket();
                // Deliberately use wrong subprotocol
                ws.Options.AddSubProtocol("wrong.protocol.v1");

                var cts = new CancellationTokenSource(3000);
                try
                {
                    ws.ConnectAsync(new Uri("ws://127.0.0.1:18766/"), cts.Token).GetAwaiter().GetResult();

                    // Connection may succeed at HTTP level but server closes immediately.
                    // ReceiveAsync will return a Close frame or throw.
                    try
                    {
                        var buffer = new byte[256];
                        var result = ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).GetAwaiter().GetResult();
                        Assert(result.MessageType == WebSocketMessageType.Close,
                            "Wrong subprotocol: server sent close frame");
                    }
                    catch (WebSocketException)
                    {
                        Assert(true, "Wrong subprotocol: connection rejected at receive");
                    }
                }
                catch (WebSocketException)
                {
                    Assert(true, "Wrong subprotocol: connection rejected at connect");
                }
            }
            finally
            {
                runtime.Dispose();
            }
        }
    }

    // ── G.1 helper: FakeTransport tracks SendText vs BroadcastText ──

    internal class FakeTransport : IFoxgloveTransport
    {
        private readonly ConcurrentDictionary<uint, List<string>> _sentTexts
            = new ConcurrentDictionary<uint, List<string>>();

        public uint BroadcastTextCallCount { get; private set; }

        public bool IsRunning => true;

        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;
        public event Action<uint, string> OnTextReceived;
        public event Action<uint, byte[]> OnBinaryReceived;

        public void Start(string host, int port) { }
        public void Stop() { }
        public void Dispose() { }

        public void SendText(uint clientId, string json)
        {
            _sentTexts.AddOrUpdate(clientId,
                _ => new List<string> { json },
                (_, list) => { list.Add(json); return list; });
        }

        public void SendBinary(uint clientId, byte[] data) { }

        public void BroadcastText(string json)
        {
            BroadcastTextCallCount++;
        }

        public void BroadcastBinary(byte[] data) { }

        public uint SentTextCount(uint clientId)
        {
            _sentTexts.TryGetValue(clientId, out var list);
            return (uint)(list?.Count ?? 0);
        }

        public string LastSentText(uint clientId)
        {
            _sentTexts.TryGetValue(clientId, out var list);
            return list?.LastOrDefault();
        }

        public void SimulateConnect(uint clientId)
        {
            OnClientConnected?.Invoke(clientId);
        }
    }
}
