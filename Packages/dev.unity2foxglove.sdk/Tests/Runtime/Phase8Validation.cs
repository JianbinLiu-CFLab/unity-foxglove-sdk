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

        /// <summary>
        /// Entry point: runs all Phase 8 tests covering connection graph
        /// capabilities, graph subscribe/unsubscribe, client publish,
        /// per-client channel isolation, and disconnect cleanup.
        /// </summary>
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
            TestDisconnectCleansGraphSubscribedTopics();

            Console.WriteLine($"Phase 8: {_passCount} checks passed.\n");
        }

        /// <summary>
        /// ServerInfo capabilities must include connectionGraph and
        /// clientPublish.
        /// </summary>
        private static void TestServerInfoIncludesConnectionGraph()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert(json.Contains("connectionGraph"), "capabilities includes connectionGraph");
            Assert(json.Contains("clientPublish"), "capabilities includes clientPublish");
        }

        /// <summary>
        /// Subscribing to the connection graph triggers graph updates on
        /// channel registration; unsubscribing must not throw.
        /// </summary>
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

        /// <summary>
        /// Validates the connectionGraphUpdate JSON structure: op field,
        /// <c>publishedTopics</c> array with correct topic name and
        /// publisherIds.
        /// </summary>
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

        /// <summary>
        /// A client advertise message must register a client channel and
        /// subsequent client MessageData binary frames must dispatch
        /// through <c>OnClientMessage</c> with the correct topic.
        /// </summary>
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

        /// <summary>
        /// A client binary MessageData frame must be decoded and
        /// dispatched with the correct channel id, topic, and payload.
        /// </summary>
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

        /// <summary>
        /// Two clients advertising the same channel id but different
        /// topics must each route correctly to their own topic without
        /// cross-contamination.
        /// </summary>
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

        /// <summary>
        /// When a client disconnects, its advertised client channels must
        /// be cleaned up so that subsequent binary frames no longer
        /// dispatch.
        /// </summary>
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

        /// <summary>
        /// When a client advertises a topic, graph subscribers on other
        /// clients must receive a connectionGraphUpdate.
        /// </summary>
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

        /// <summary>
        /// When a client subscribed to server channels disconnects, its
        /// subscribed-topic entries must be removed from the connection graph.
        /// </summary>
        private static void TestDisconnectCleansGraphSubscribedTopics()
        {
            var fake = new Phase8FakeTransport();
            var session = new FoxgloveSession("Test", fake);

            // Client 1 subscribes to channels and connection graph
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"subscribeConnectionGraph\"}");

            // Register a server channel
            session.RegisterChannel(new AdvertiseChannel { Id = 10, Topic = "/server/t1", Encoding = "json" });

            // Client subscribes to the server channel
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":10}]}");

            // Client also advertises a client-published channel
            fake.SimulateText(1, "{\"op\":\"advertise\",\"channels\":[{\"id\":5,\"topic\":\"/client/t1\",\"encoding\":\"json\"}]}");

            // Capture the latest graph update sent to client 1 (via SendText, not BroadcastText)
            var sentTo1 = fake.SentTexts[1];
            var snapshots = sentTo1.Where(t => t.Contains("connectionGraphUpdate")).ToList();
            var beforeDisconnect = JObject.Parse(snapshots.Last());
            var subsBefore = (JArray)beforeDisconnect["subscribedTopics"];
            var pubsBefore = (JArray)beforeDisconnect["publishedTopics"];
            Assert(subsBefore != null && subsBefore.Count > 0, "Graph disconnect: subscribed topics exist before disconnect");
            Assert(pubsBefore != null && pubsBefore.Count > 0, "Graph disconnect: published topics exist before disconnect");

            // Disconnect client 1
            fake.SimulateDisconnect(1);

            // After disconnect, connect a new client who subscribes to graph.
            // The snapshot must NOT contain the disconnected client's subscriptions.
            fake.SimulateConnect(2);
            fake.SimulateText(2, "{\"op\":\"subscribeConnectionGraph\"}");

            var sentTo2 = fake.SentTexts[2];
            var graphUpdates = sentTo2.Where(t => t.Contains("connectionGraphUpdate")).ToList();
            Assert(graphUpdates.Count > 0, "Graph disconnect: graph update sent to new client");
            var afterDisconnect = JObject.Parse(graphUpdates.Last());
            var subsAfter = (JArray)afterDisconnect["subscribedTopics"];
            var pubsAfter = (JArray)afterDisconnect["publishedTopics"];

            var hasClientSub = subsAfter.Any(s =>
            {
                var ids = (JArray)((JObject)s)["subscriberIds"];
                return ids?.Any(id => id?.ToString().StartsWith("client:1:") == true) == true;
            });
            Assert(!hasClientSub, "Graph disconnect: no subscribed topics from disconnected client");

            var hasClientPub = pubsAfter.Any(p =>
            {
                var ids = (JArray)((JObject)p)["publisherIds"];
                return ids?.Any(id => id?.ToString().StartsWith("client:1:") == true) == true;
            });
            Assert(!hasClientPub, "Graph disconnect: no published topics from disconnected client");
        }

        /// <summary>
        /// Fake transport for Phase 8 recording per-client SendText,
        /// SendBinary, BroadcastText, plus simulators for connect,
        /// disconnect, text, and binary events.
        /// </summary>
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
