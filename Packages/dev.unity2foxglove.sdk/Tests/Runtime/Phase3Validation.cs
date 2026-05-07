using System;
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
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase3Validation
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

        public static void Validate()
        {
            Console.WriteLine("--- Phase 3 Tests ---");

            TestCoreSchemasRegistered();
            TestLogSchemaRegistered();
            TestRegisterSchemaChannelAdvertise();
            TestRegisterSchemaChannelUnknownThrows();
            TestFrameTransformFieldNames();
            TestSceneUpdateRequiredArrays();
            TestCubeEntitySerialization();
            TestSceneEntityDeletionTypeIsInteger();
            TestPublishJsonBinaryFrame();
            TestRealWebSocketTypedAdvertise();
            TestRealWebSocketPublishJsonSceneUpdate();

            Console.WriteLine($"Phase 3: {_passCount} checks passed.\n");
        }

        // ── Core schema registration ──

        private static void TestCoreSchemasRegistered()
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);

            Assert(registry.TryGetSchema("foxglove.FrameTransform", out var ft), "Registry has FrameTransform");
            Assert(registry.TryGetSchema("foxglove.SceneUpdate", out var su), "Registry has SceneUpdate");

            Assert(ft.Encoding == "jsonschema", "FrameTransform encoding is jsonschema");
            Assert(su.Encoding == "jsonschema", "SceneUpdate encoding is jsonschema");

            // Both should be parsable JSON
            JObject.Parse(ft.Content);
            Assert(true, "FrameTransform schema is valid JSON");
            JObject.Parse(su.Content);
            Assert(true, "SceneUpdate schema is valid JSON");

            Assert(ft.Content.Contains("foxglove.FrameTransform"), "FrameTransform schema has correct title");
            Assert(su.Content.Contains("foxglove.SceneUpdate"), "SceneUpdate schema has correct title");
        }

        private static void TestLogSchemaRegistered()
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);

            Assert(registry.TryGetSchema(FoxgloveSchemaDefinitions.LogSchemaName, out var log), "Registry has foxglove.Log");
            Assert(log.Encoding == "jsonschema", "Log encoding is jsonschema");

            JObject.Parse(log.Content);
            Assert(true, "Log schema is valid JSON");
            Assert(log.Content.Contains("foxglove.Log"), "Log schema has correct title");
        }

        // ── RegisterSchemaChannel ──

        private sealed class Phase3FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public readonly List<string> BroadcastTexts = new();
            private readonly Dictionary<uint, List<string>> _sentTexts = new();
            private readonly Dictionary<uint, List<byte[]>> _sentBinaries = new();

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint clientId, string json) { if (!_sentTexts.ContainsKey(clientId)) _sentTexts[clientId] = new(); _sentTexts[clientId].Add(json); }
            public void SendBinary(uint clientId, byte[] data) { if (!_sentBinaries.ContainsKey(clientId)) _sentBinaries[clientId] = new(); _sentBinaries[clientId].Add(data); }
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public List<string> SentTexts(uint clientId) => _sentTexts.TryGetValue(clientId, out var l) ? l : new();
            public List<byte[]> SentBinaries(uint clientId) => _sentBinaries.TryGetValue(clientId, out var l) ? l : new();
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }

        private static void TestRegisterSchemaChannelAdvertise()
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);
            var fake = new Phase3FakeTransport();
            var session = new FoxgloveSession("Test", fake, schemaRegistry: registry);
            fake.SimulateConnect(1);

            session.RegisterSchemaChannel(10, "/tf", "foxglove.FrameTransform");

            var advJson = fake.BroadcastTexts[0];
            var adv = JObject.Parse(advJson);
            Assert(adv["op"]?.ToString() == "advertise", "advertise op correct");

            var ch = adv["channels"]?[0] as JObject;
            Assert(ch["id"]?.Value<int>() == 10, "channel id=10");
            Assert(ch["topic"]?.ToString() == "/tf", "channel topic=/tf");
            Assert(ch["encoding"]?.ToString() == "json", "encoding=json");
            Assert(ch["schemaName"]?.ToString() == "foxglove.FrameTransform", "schemaName correct");
            Assert(ch["schemaEncoding"]?.ToString() == "jsonschema", "schemaEncoding=jsonschema");
            Assert(ch["schema"]?.ToString().Contains("foxglove.FrameTransform") == true, "schema contains title");
        }

        private static void TestRegisterSchemaChannelUnknownThrows()
        {
            var registry = new DefaultSchemaRegistry();
            var fake = new Phase3FakeTransport();
            var session = new FoxgloveSession("Test", fake, schemaRegistry: registry);

            try
            {
                session.RegisterSchemaChannel(1, "/t", "nonexistent.Schema");
                Assert(false, "Should have thrown");
            }
            catch (InvalidOperationException ex)
            {
                Assert(ex.Message.Contains("nonexistent.Schema"), "Exception message contains schema name");
            }
        }

        // ── DTO field names ──

        private static void TestFrameTransformFieldNames()
        {
            var msg = new FrameTransformMessage
            {
                Timestamp = new FoxgloveTime { Sec = 1, Nsec = 2 },
                ParentFrameId = "world",
                ChildFrameId = "child",
                Translation = new FoxgloveVector3 { X = 1, Y = 2, Z = 3 },
                Rotation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
            };
            var json = JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            Assert(obj["parent_frame_id"]?.ToString() == "world", "parent_frame_id field name");
            Assert(obj["child_frame_id"]?.ToString() == "child", "child_frame_id field name");
            Assert(obj["translation"]?["x"]?.Value<double>() == 1, "translation.x present");
            Assert(obj["rotation"]?["w"]?.Value<double>() == 1, "rotation.w present");
        }

        private static void TestSceneUpdateRequiredArrays()
        {
            var msg = new SceneUpdateMessage
            {
                Entities = new List<SceneEntity>
                {
                    new SceneEntity
                    {
                        Id = "e1",
                        FrameId = "unity_world",
                        Timestamp = new FoxgloveTime { Sec = 1 },
                        Lifetime = new FoxgloveDuration(),
                        Cubes = new List<CubePrimitive>
                        {
                            new CubePrimitive
                            {
                                Pose = new FoxglovePose
                                {
                                    Position = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                                    Orientation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
                                },
                                Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 },
                                Color = new FoxgloveColor { R = 0, G = 1, B = 0, A = 1 }
                            }
                        }
                    }
                }
            };
            var json = JsonConvert.SerializeObject(msg);
            var obj = JObject.Parse(json);
            var entity = obj["entities"]?[0] as JObject;
            Assert(entity != null, "entities[0] exists");
            Assert(entity["deletions"] == null, "deletions not present at entity level");
            Assert(entity["arrows"] is JArray, "arrows present as array");
            Assert(entity["cubes"] is JArray, "cubes present as array");
            Assert(entity["spheres"] is JArray, "spheres present as array");
            Assert(entity["cylinders"] is JArray, "cylinders present as array");
            Assert(entity["lines"] is JArray, "lines present as array");
            Assert(entity["triangles"] is JArray, "triangles present as array");
            Assert(entity["texts"] is JArray, "texts present as array");
            Assert(entity["models"] is JArray, "models present as array");
            Assert(entity["metadata"] is JArray, "metadata present as array");
        }

        private static void TestCubeEntitySerialization()
        {
            var entity = new SceneEntity
            {
                Id = "phase3_cube",
                FrameId = "unity_world",
                Timestamp = new FoxgloveTime { Sec = 1 },
                Lifetime = new FoxgloveDuration(),
                Cubes = new List<CubePrimitive>
                {
                    new CubePrimitive
                    {
                        Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 },
                        Pose = new FoxglovePose
                        {
                            Position = new FoxgloveVector3(),
                            Orientation = new FoxgloveQuaternion { W = 1 }
                        },
                        Color = new FoxgloveColor { R = 0, G = 1, B = 0, A = 1 }
                    }
                }
            };
            var json = JsonConvert.SerializeObject(entity);
            var obj = JObject.Parse(json);
            Assert(obj["id"]?.ToString() == "phase3_cube", "entity id");
            Assert(obj["frame_id"]?.ToString() == "unity_world", "entity frame_id");
            Assert((double)obj["cubes"][0]["size"]["x"] == 1.0, "cube size.x == 1");
        }

        private static void TestSceneEntityDeletionTypeIsInteger()
        {
            var deletion = new SceneEntityDeletion
            {
                Type = SceneEntityDeletionType.All,
                Id = "test",
                Timestamp = new FoxgloveTime { Sec = 1 }
            };
            var json = JsonConvert.SerializeObject(deletion);
            var obj = JObject.Parse(json);
            Assert(obj["type"]?.Value<int>() == 1, "deletion type is integer 1 (ALL)");
        }

        // ── PublishJson ──

        private static void TestPublishJsonBinaryFrame()
        {
            var registry = new DefaultSchemaRegistry();
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(registry);
            var fake = new Phase3FakeTransport();
            var session = new FoxgloveSession("Test", fake, schemaRegistry: registry);
            session.RegisterChannel(new AdvertiseChannel { Id = 1, Topic = "/t", Encoding = "json" });
            fake.SimulateConnect(1);
            fake.SimulateText(1, "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}");

            var cube = new CubePrimitive
            {
                Size = new FoxgloveVector3 { X = 1, Y = 2, Z = 3 },
                Pose = new FoxglovePose
                {
                    Position = new FoxgloveVector3(),
                    Orientation = new FoxgloveQuaternion { W = 1 }
                },
                Color = new FoxgloveColor { R = 1, G = 0, B = 0, A = 1 }
            };
            session.PublishJson(1, cube);

            var frames = fake.SentBinaries(1);
            Assert(frames.Count == 1, "PublishJson sent one binary frame");
            var payloadJson = Encoding.UTF8.GetString(frames[0], 13, frames[0].Length - 13);
            var payload = JObject.Parse(payloadJson);
            Assert((double)payload["size"]["x"] == 1.0, "Payload roundtrip: size.x=1");
        }

        // ── Real WebSocket integration ──

        private static void TestRealWebSocketTypedAdvertise()
        {
            using var runtime = new FoxgloveRuntime();
            runtime.Start("SchemaTest", "127.0.0.1", 18782);
            runtime.RegisterSchemaChannel(1, "/tf", "foxglove.FrameTransform");

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(Subprotocol.SdkV1);
                var cts = new CancellationTokenSource(5000);
                ws.ConnectAsync(new Uri("ws://127.0.0.1:18782/"), cts.Token).GetAwaiter().GetResult();
                Assert(ws.State == WebSocketState.Open, "Integration: connected");

                var buf = new byte[8192];
                var seg = new ArraySegment<byte>(buf);

                // serverInfo
                var r1 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                var info = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r1.Count));
                Assert(info["op"]?.ToString() == "serverInfo", "Got serverInfo");

                // typed advertise
                var r2 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                var adv = JObject.Parse(Encoding.UTF8.GetString(buf, 0, r2.Count));
                Assert(adv["op"]?.ToString() == "advertise", "Got advertise");
                var ch = adv["channels"]?[0] as JObject;
                Assert(ch["schemaName"]?.ToString() == "foxglove.FrameTransform", "schemaName in advertise");
                Assert(ch["schemaEncoding"]?.ToString() == "jsonschema", "schemaEncoding in advertise");
                Assert(ch["schema"]?.ToString().Length > 0, "schema content non-empty");

                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            }
            finally { runtime.Dispose(); }
        }

        private static void TestRealWebSocketPublishJsonSceneUpdate()
        {
            using var runtime = new FoxgloveRuntime();
            runtime.Start("SceneTest", "127.0.0.1", 18783);
            runtime.RegisterSchemaChannel(1, "/scene", "foxglove.SceneUpdate");

            try
            {
                var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(Subprotocol.SdkV1);
                var cts = new CancellationTokenSource(10000);
                ws.ConnectAsync(new Uri("ws://127.0.0.1:18783/"), cts.Token).GetAwaiter().GetResult();

                var buf = new byte[131072];
                var seg = new ArraySegment<byte>(buf);
                // Skip serverInfo
                ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                // Skip advertise (may be large due to embedded SceneUpdate schema)
                ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();

                // Subscribe
                var sub = "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":100,\"channelId\":1}]}";
                ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(sub)), WebSocketMessageType.Text, true, cts.Token).GetAwaiter().GetResult();
                Task.Delay(100).Wait();

                // Publish SceneUpdate via PublishJson
                var msg = new SceneUpdateMessage
                {
                    Entities = new List<SceneEntity>
                    {
                        new SceneEntity
                        {
                            Id = "phase3_cube",
                            FrameId = "unity_world",
                            Timestamp = new FoxgloveTime { Sec = 1 },
                            Lifetime = new FoxgloveDuration(),
                            Cubes = new List<CubePrimitive>
                            {
                                new CubePrimitive
                                {
                                    Pose = new FoxglovePose
                                    {
                                        Position = new FoxgloveVector3(),
                                        Orientation = new FoxgloveQuaternion { W = 1 }
                                    },
                                    Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 },
                                    Color = new FoxgloveColor { R = 0, G = 1, B = 0, A = 1 }
                                }
                            }
                        }
                    }
                };
                runtime.PublishJson(1, msg);

                // Receive binary frame
                var r3 = ws.ReceiveAsync(seg, cts.Token).GetAwaiter().GetResult();
                Assert(r3.MessageType == WebSocketMessageType.Binary, "Received binary MessageData");
                var frame = new byte[r3.Count];
                Array.Copy(buf, 0, frame, 0, r3.Count);
                var subId = BitConverter.ToUInt32(frame, 1);
                AssertEqual(100u, subId, "subscriptionId=100");
                var payloadJson = Encoding.UTF8.GetString(frame, 13, frame.Length - 13);
                var payload = JObject.Parse(payloadJson);
                Assert(payload["entities"]?[0]?["id"]?.ToString() == "phase3_cube", "entity id in payload");
                Assert((double)payload["entities"][0]["cubes"][0]["size"]["x"] == 1.0, "cube size.x=1 in payload");

                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult();
            }
            finally { runtime.Dispose(); }
        }
    }
}
