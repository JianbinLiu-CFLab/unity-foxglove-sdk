using System;
using System.Collections.Generic;
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
    public static class Phase8Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        public static void Validate()
        {
            Console.WriteLine("--- Phase 8 Tests ---");

            TestServerInfoIncludesConnectionGraph();
            TestConnectionGraphSubscribeUnsubscribe();
            TestConnectionGraphUpdateFormat();
            TestClientAdvertiseRegistersChannel();
            TestClientMessageData();
            TestTwoClientsDifferentChannelIds();
            TestClientDisconnectCleansUp();
            TestConnectionGraphLinkedToClientPublish();

            Console.WriteLine($"Phase 8: {_passCount} checks passed.\n");
        }

        private static void TestServerInfoIncludesConnectionGraph()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert(json.Contains("connectionGraph"), "capabilities includes connectionGraph");
            Assert(json.Contains("clientPublish"), "capabilities includes clientPublish");
        }

        private static void TestConnectionGraphSubscribeUnsubscribe()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            // Subscribe to graph
            fake.SimulateText(1, "{\"op\":\"subscribeConnectionGraph\"}");
            // Graph should be subscribed now
            // Simulate a channel publish to trigger graph update
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t1", Encoding = "json" });
            // Graph update should have been sent to subscriber
            var hasUpdate = fake.SentTexts[1].Any(t => t.Contains("connectionGraphUpdate"));
            Assert(hasUpdate, "Graph subscriber receives update after register");

            // Unsubscribe
            fake.SimulateText(1, "{\"op\":\"unsubscribeConnectionGraph\"}");
            Assert(true, "UnsubscribeConnectionGraph does not throw");
        }

        private static void TestConnectionGraphUpdateFormat()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"subscribeConnectionGraph\"}");

            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t1", Encoding = "json" });

            var updates = fake.SentTexts[1].Where(t => t.Contains("connectionGraphUpdate")).ToList();
            Assert(updates.Count > 0, "Graph update sent");
            var update = JObject.Parse(updates.Last());
            Assert(update["op"]?.ToString() == "connectionGraphUpdate", "op is connectionGraphUpdate");
            Assert(update["publishedTopics"] is JArray, "publishedTopics is array");
            var pt = update["publishedTopics"]?[0] as JObject;
            Assert(pt?["name"]?.ToString() == "/t1", "published topic name");
            Assert(pt["publisherIds"] is JArray, "publisherIds is array");
        }

        private static void TestClientAdvertiseRegistersChannel()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            var advJson = JsonConvert.SerializeObject(new Advertise
            {
                Channels = new List<AdvertiseChannel>
                {
                    new() { Id = 10, Topic = "/client/t1", Encoding = "json" }
                }
            });
            fake.SimulateText(1, advJson);

            // Send client MessageData
            var payload = Encoding.UTF8.GetBytes("hello");
            var frame = new byte[1 + 4 + payload.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, 10);
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);

            var received = false;
            session.OnClientMessage += (clientId, chId, topic, pl) =>
            {
                received = clientId == 1 && chId == 10 && topic == "/client/t1";
            };
            fake.SimulateBinary(1, frame);
            Assert(received, "Client MessageData received via OnClientMessage");
        }

        private static void TestClientMessageData()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            // Advertise client channel
            fake.SimulateText(1, "{\"op\":\"advertise\",\"channels\":[{\"id\":5,\"topic\":\"/c/t\",\"encoding\":\"json\"}]}");

            var payload = Encoding.UTF8.GetBytes("data");
            var frame = new byte[1 + 4 + payload.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, 5);
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);

            var gotMessage = false;
            session.OnClientMessage += (cid, ch, topic, pl) =>
                gotMessage = cid == 1 && ch == 5 && Encoding.UTF8.GetString(pl) == "data";
            fake.SimulateBinary(1, frame);
            Assert(gotMessage, "Client binary MessageData dispatched");
        }

        private static void TestTwoClientsDifferentChannelIds()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            fake.SimulateConnect(2);

            // Both advertise channelId=1 — must not collide
            fake.SimulateText(1, "{\"op\":\"advertise\",\"channels\":[{\"id\":1,\"topic\":\"/c/t1\",\"encoding\":\"json\"}]}");
            fake.SimulateText(2, "{\"op\":\"advertise\",\"channels\":[{\"id\":1,\"topic\":\"/c/t2\",\"encoding\":\"json\"}]}");

            var received = new List<string>();
            session.OnClientMessage += (cid, ch, topic, pl) => received.Add($"{cid}:{topic}");

            var payload = Encoding.UTF8.GetBytes("x");
            var frame1 = new byte[1 + 4 + 1]; frame1[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame1, 1, 1); frame1[5] = 120;
            fake.SimulateBinary(1, frame1);

            fake.SimulateBinary(2, frame1);

            Assert(received.Contains("1:/c/t1"), "Client 1 channel 1 routes to /c/t1");
            Assert(received.Contains("2:/c/t2"), "Client 2 channel 1 routes to /c/t2");
        }

        private static void TestClientDisconnectCleansUp()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"advertise\",\"channels\":[{\"id\":1,\"topic\":\"/c/t\",\"encoding\":\"json\"}]}");
            fake.SimulateDisconnect(1);

            // Client channel should be cleaned
            var gotMsg = false;
            session.OnClientMessage += (cid, ch, topic, pl) => gotMsg = true;
            var frame = new byte[1 + 4 + 1]; frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, 1); frame[5] = 1;
            fake.SimulateBinary(1, frame);
            Assert(!gotMsg, "Client disconnect cleans channel registry");
        }

        private static void TestConnectionGraphLinkedToClientPublish()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(2);
            fake.SimulateText(2, "{\"op\":\"subscribeConnectionGraph\"}");

            // Client 1 advertises → graph should be updated for subscriber client 2
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"advertise\",\"channels\":[{\"id\":1,\"topic\":\"/c/t\",\"encoding\":\"json\"}]}");

            var updates = fake.SentTexts[2].Where(t => t.Contains("connectionGraphUpdate")).ToList();
            Assert(updates.Count > 0, "Client advertise triggers graph update");
        }

        private sealed class Phase8FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public Dictionary<uint, List<string>> SentTexts = new();
            public List<string> BroadcastTexts = new();

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint clientId, string json)
            {
                if (!SentTexts.ContainsKey(clientId)) SentTexts[clientId] = new();
                SentTexts[clientId].Add(json);
            }
            public void SendBinary(uint clientId, byte[] data)
            {
                if (!SentBinaries.ContainsKey(clientId)) SentBinaries[clientId] = new();
                SentBinaries[clientId].Add(data);
            }
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public Dictionary<uint, List<byte[]>> SentBinaries = new();
            public void SimulateConnect(uint id) => OnClientConnected?.Invoke(id);
            public void SimulateDisconnect(uint id) => OnClientDisconnected?.Invoke(id);
            public void SimulateText(uint id, string json) => OnTextReceived?.Invoke(id, json);
            public void SimulateBinary(uint id, byte[] data) => OnBinaryReceived?.Invoke(id, data);
        }
    }
}
