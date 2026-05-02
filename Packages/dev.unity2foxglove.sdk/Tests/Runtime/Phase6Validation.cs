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
    public static class Phase6Validation
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
            Console.WriteLine("--- Phase 6 Tests ---");

            TestServerInfoCapabilities();
            TestDtoFieldNames();
            TestBinaryServiceCodec();
            TestParameterStoreRegisterGet();
            TestParameterSetFromClient();
            TestParameterSubscribeUnsubscribe();
            TestServiceAdvertiseBeforeConnect();
            TestServiceAdvertiseAfterConnect();
            TestServiceCallFailureUnknown();
            TestServiceCallFailureEncoding();
            TestServiceCallEnqueueComplete();
            TestServiceCallTimeout();

            Console.WriteLine($"Phase 6: {_passCount} checks passed.\n");
        }

        // ── Capabilities ──

        private static void TestServerInfoCapabilities()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts(1)[0];
            var info = JObject.Parse(json);
            var caps = info["capabilities"] as JArray;
            Assert(caps != null, "capabilities present");
            Assert(caps.ToString().Contains("parameters"), "capabilities includes parameters");
            Assert(caps.ToString().Contains("services"), "capabilities includes services");
            Assert(caps.ToString().Contains("parametersSubscribe"), "capabilities includes parametersSubscribe (Phase 7)");
            Assert(!caps.ToString().Contains("assets"), "capabilities excludes assets");
            Assert(!caps.ToString().Contains("playbackControl"), "capabilities excludes playbackControl");
            Assert(info["supportedEncodings"]?[0]?.ToString() == "json", "supportedEncodings includes json");
        }

        // ── DTO ──

        private static void TestDtoFieldNames()
        {
            var setParams = new SetParameters
            {
                Parameters = new List<Parameter> { new Parameter { Name = "p1", Value = 42 } },
                Id = "req1"
            };
            var json = JsonConvert.SerializeObject(setParams);
            var obj = JObject.Parse(json);
            Assert(obj["op"]?.ToString() == "setParameters", "SetParameters op");
            Assert(obj["parameters"]?[0]?["name"]?.ToString() == "p1", "param name");
            Assert((int)obj["parameters"][0]["value"] == 42, "param value");

            var unsub = new UnsubscribeParameterUpdates
            { ParameterNames = new List<string> { "p1", "p2" } };
            var ujson = JsonConvert.SerializeObject(unsub);
            Assert(JObject.Parse(ujson)["op"]?.ToString() == "unsubscribeParameterUpdates", "UnsubParamUpdates op");

            var advSvc = new AdvertiseServices
            {
                Services = new List<ServiceDescriptor>
                {
                    new ServiceDescriptor
                    {
                        Name = "/svc", Type = "/svc",
                        Request = new ServiceSchemaDescriptor { SchemaName = "/req", Encoding = "jsonschema" },
                        Response = new ServiceSchemaDescriptor { SchemaName = "/resp", Encoding = "jsonschema" }
                    }
                }
            };
            var ajson = JsonConvert.SerializeObject(advSvc);
            var aobj = JObject.Parse(ajson);
            Assert(aobj["op"]?.ToString() == "advertiseServices", "AdvertiseServices op");

            var fail = new ServiceCallFailure { ServiceId = 1, CallId = 2, Message = "err" };
            var fjson = JsonConvert.SerializeObject(fail);
            var fobj = JObject.Parse(fjson);
            Assert(fobj["serviceId"]?.Value<int>() == 1, "serviceCallFailure serviceId");
        }

        // ── Binary codec ──

        private static void TestBinaryServiceCodec()
        {
            // Roundtrip: encode server response, then verify a client request with same structure
            var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
            var resp = BinaryEncoding.EncodeServerServiceCallResponse(5, 10, "json", payload);
            Assert(resp[0] == ServerOpcode.ServiceCallResponse, "Response opcode correct");

            // Build a client request frame manually: opcode(2) + serviceId + callId + encodingLen + encoding + payload
            var enc = Encoding.UTF8.GetBytes("json");
            var req = new byte[1 + 4 + 4 + 4 + enc.Length + payload.Length];
            req[0] = ClientOpcode.ServiceCallRequest;
            BinaryEncoding.WriteU32LE(req, 1, 5);
            BinaryEncoding.WriteU32LE(req, 5, 10);
            BinaryEncoding.WriteU32LE(req, 9, (uint)enc.Length);
            Buffer.BlockCopy(enc, 0, req, 13, enc.Length);
            Buffer.BlockCopy(payload, 0, req, 13 + enc.Length, payload.Length);

            var decoded = BinaryEncoding.TryDecodeClientServiceCallRequest(req,
                out var sid, out var cid, out var decEnc, out var pl);
            Assert(decoded, "Client decode succeeds");
            AssertEqual(5u, sid, "serviceId roundtrip");
            AssertEqual(10u, cid, "callId roundtrip");
            Assert(decEnc == "json", "encoding roundtrip");
            Assert(Encoding.UTF8.GetString(pl) == "{\"x\":1}", "payload roundtrip");
        }

        // ── Parameters ──

        private static void TestParameterStoreRegisterGet()
        {
            var store = new FoxgloveParameterStore();
            store.Register("/speed", 100, "number", true);
            var p = store.GetWireParameter("/speed");
            Assert(p != null, "Get registered param");
            Assert((int)p.Value == 100, "value correct");

            var all = store.GetAllWireParameters();
            Assert(all.Count == 1, "GetAll returns one param");
        }

        private static void TestParameterSetFromClient()
        {
            var store = new FoxgloveParameterStore();
            store.Register("/speed", 100, "number", true);
            store.Register("/readonly", 5, "number", false);

            Assert(store.TrySetFromClient("/speed", 200), "writable param changed");
            Assert(!store.TrySetFromClient("/readonly", 10), "readonly param rejected");
            Assert(!store.TrySetFromClient("/unknown", 1), "unknown param rejected");

            Assert((int)store.GetWireParameter("/speed").Value == 200, "value updated");
            Assert((int)store.GetWireParameter("/readonly").Value == 5, "readonly unchanged");
        }

        // ── Parameter subscriptions ──

        private sealed class Phase6FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            private readonly Dictionary<uint, List<string>> _sentTexts = new();
            private readonly Dictionary<uint, List<byte[]>> _sentBinaries = new();
            public readonly List<string> BroadcastTexts = new();

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint clientId, string json)
            {
                if (!_sentTexts.ContainsKey(clientId)) _sentTexts[clientId] = new();
                _sentTexts[clientId].Add(json);
            }
            public void SendBinary(uint clientId, byte[] data)
            {
                if (!_sentBinaries.ContainsKey(clientId)) _sentBinaries[clientId] = new();
                _sentBinaries[clientId].Add(data);
            }
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public List<string> SentTexts(uint clientId) => _sentTexts.TryGetValue(clientId, out var l) ? l : new();
            public List<byte[]> SentBinaries(uint clientId) => _sentBinaries.TryGetValue(clientId, out var l) ? l : new();
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void SimulateBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }

        private static void TestParameterSubscribeUnsubscribe()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Parameters.Register("/speed", 100, "number", true);
            fake.SimulateConnect(1);

            // Subscribe to all
            fake.SimulateText(1, "{\"op\":\"subscribeParameterUpdates\",\"parameterNames\":[]}");
            Assert(session.Parameters.TrySetFromClient("/speed", 200), "param change successful");
            // getParameters should return updated value
            fake.SentTexts(1).Clear();
            fake.SimulateText(1, "{\"op\":\"getParameters\",\"parameterNames\":[],\"id\":\"r1\"}");
            var response = fake.SentTexts(1).Last();
            var obj = JObject.Parse(response);
            Assert(obj["id"]?.ToString() == "r1", "getParameters id roundtrip");
            Assert(obj["parameters"]?[0]?["value"]?.Value<int>() == 200, "getParameters returns current value");

            // Unsubscribe
            fake.SimulateText(1, "{\"op\":\"unsubscribeParameterUpdates\",\"parameterNames\":[]}");
            Assert(true, "unsubscribeParameterUpdates does not throw");
        }

        // ── Services ──

        private static void TestServiceAdvertiseBeforeConnect()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            // Register via the safe API (RegisterService), then connect
            session.RegisterService(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            fake.SimulateConnect(1);
            var texts = fake.SentTexts(1);
            var hasAdv = false;
            foreach (var t in texts)
                if (JObject.Parse(t)["op"]?.ToString() == "advertiseServices") hasAdv = true;
            Assert(hasAdv, "New client receives service advertise snapshot");
        }

        private static void TestServiceAdvertiseAfterConnect()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            // Clear broadcast history
            fake.BroadcastTexts.Clear();

            // Register AFTER connect — must broadcast to already-connected client
            session.RegisterService(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            var hasAdv = false;
            foreach (var t in fake.BroadcastTexts)
                if (JObject.Parse(t)["op"]?.ToString() == "advertiseServices") hasAdv = true;
            Assert(hasAdv, "RegisterService broadcasts advertiseServices to already-connected client");
        }

        private static void TestServiceCallFailureUnknown()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            var request = BinaryEncoding.EncodeServerServiceCallResponse(999, 1, "json",
                Encoding.UTF8.GetBytes("{}"));
            request[0] = ClientOpcode.ServiceCallRequest;

            fake.SimulateBinary(1, request);
            var sent = fake.SentTexts(1);
            var failure = JObject.Parse(sent.Last());
            Assert(failure["op"]?.ToString() == "serviceCallFailure", "Unknown service → failure");
        }

        private static void TestServiceCallFailureEncoding()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            fake.SimulateConnect(1);

            var encBytes = Encoding.UTF8.GetBytes("protobuf");
            var frame = new byte[1 + 4 + 4 + 4 + encBytes.Length];
            frame[0] = ClientOpcode.ServiceCallRequest;
            frame[13 + 4 + 4 + 4 - 13 - 1] = 0; // Hmm, let me just use EncodeServerServiceCallResponse and change opcode
            // Actually simpler: use the binary encoder but with wrong encoding
            var req = BinaryEncoding.EncodeServerServiceCallResponse(1, 1, "protobuf", new byte[] { 1 });
            req[0] = ClientOpcode.ServiceCallRequest;
            fake.SimulateBinary(1, req);
            var sent = fake.SentTexts(1);
            Assert(sent.Last().Contains("Unsupported encoding"), "Wrong encoding → failure");
        }

        private static void TestServiceCallEnqueueComplete()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            fake.SimulateConnect(1);

            var payload = Encoding.UTF8.GetBytes("{}");
            var frame = BinaryEncoding.EncodeServerServiceCallResponse(1, 1, "json", payload);
            frame[0] = ClientOpcode.ServiceCallRequest;
            fake.SimulateBinary(1, frame);

            // Complete the pending call
            session.Services.CompleteResponse(1, 1, "json", Encoding.UTF8.GetBytes("{\"ok\":true}"));

            // Drain should send response
            session.DrainServiceCalls();
            var binaries = fake.SentBinaries(1);
            Assert(binaries.Count > 0, "Service response sent as binary after drain");
        }

        private static void TestServiceCallTimeout()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            fake.SimulateConnect(1);

            var payload = Encoding.UTF8.GetBytes("{}");
            var frame = BinaryEncoding.EncodeServerServiceCallResponse(1, 1, "json", payload);
            frame[0] = ClientOpcode.ServiceCallRequest;
            fake.SimulateBinary(1, frame);

            // Manually set CreatedAt to simulate timeout
            foreach (var call in session.Services.DrainCompleted()) { } // drain nothing
            // Hack: register a call that's already timed out by setting completion externally
            // For this test, we verify the timeout code path exists and doesn't crash
            session.Services.SweepTimeouts(TimeSpan.Zero); // Zero timeout → all pending timed out
            session.DrainServiceCalls();
            var texts = fake.SentTexts(1);
            Assert(texts.Any(t => t.Contains("serviceCallFailure")), "Timeout produces serviceCallFailure");
        }
    }
}
