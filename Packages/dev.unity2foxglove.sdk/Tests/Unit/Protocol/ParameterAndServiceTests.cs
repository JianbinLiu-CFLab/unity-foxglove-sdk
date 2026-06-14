// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: serverInfo capabilities, parameter store/subscriptions, service
//          advertise, binary service codec, and call timeout/sweep
//          (migrated from Phase6Validation; all checks are fake-transport pure logic).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// Server capabilities, parameter store and subscriptions, service advertise,
    /// binary service-call codec, and call timeout/sweep. Ported from Phase6Validation.
    /// </summary>
    [Trait("Phase", "6")]
    [Trait("Domain", "Protocol")]
    public class ParameterAndServiceTests
    {
        private static void AssertEqual<T>(T expected, T actual, string label) where T : IEquatable<T>
        {
            Assert.True(expected.Equals(actual), $"{label} (expected={expected}, actual={actual})");
        }

        [Fact]
        public void ServerInfoCapabilities()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts(1)[0];
            var info = JObject.Parse(json);
            var caps = info["capabilities"] as JArray;
            Assert.True(caps != null, "capabilities present");
            Assert.True(caps.ToString().Contains("parameters"), "capabilities includes parameters");
            Assert.True(caps.ToString().Contains("services"), "capabilities includes services");
            Assert.True(caps.ToString().Contains("parametersSubscribe"), "capabilities includes parametersSubscribe (Phase 7)");
            Assert.True(!caps.ToString().Contains("assets"), "capabilities excludes assets");
            Assert.True(!caps.ToString().Contains("playbackControl"), "capabilities excludes playbackControl");
            Assert.True(info["supportedEncodings"]?[0]?.ToString() == "json", "supportedEncodings includes json");
        }

        [Fact]
        public void DtoFieldNames()
        {
            var setParams = new SetParameters
            {
                Parameters = new List<Parameter> { new Parameter { Name = "p1", Value = 42 } },
                Id = "req1"
            };
            var json = JsonConvert.SerializeObject(setParams);
            var obj = JObject.Parse(json);
            Assert.True(obj["op"]?.ToString() == "setParameters", "SetParameters op");
            Assert.True(obj["parameters"]?[0]?["name"]?.ToString() == "p1", "param name");
            Assert.True((int)obj["parameters"][0]["value"] == 42, "param value");

            var unsub = new UnsubscribeParameterUpdates
            { ParameterNames = new List<string> { "p1", "p2" } };
            var ujson = JsonConvert.SerializeObject(unsub);
            Assert.True(JObject.Parse(ujson)["op"]?.ToString() == "unsubscribeParameterUpdates", "UnsubParamUpdates op");

            var advSvc = new AdvertiseServices
            {
                Services = new List<ServiceDescriptor>
                {
                    new ServiceDescriptor
                    {
                        Name = "/svc", Type = "/svc",
                        Request = new ServiceSchemaDescriptor { SchemaName = "/req", Encoding = "json" },
                        Response = new ServiceSchemaDescriptor { SchemaName = "/resp", Encoding = "json" }
                    }
                }
            };
            var ajson = JsonConvert.SerializeObject(advSvc);
            var aobj = JObject.Parse(ajson);
            Assert.True(aobj["op"]?.ToString() == "advertiseServices", "AdvertiseServices op");

            var fail = new ServiceCallFailure { ServiceId = 1, CallId = 2, Message = "err" };
            var fjson = JsonConvert.SerializeObject(fail);
            var fobj = JObject.Parse(fjson);
            Assert.True(fobj["serviceId"]?.Value<int>() == 1, "serviceCallFailure serviceId");
        }

        [Fact]
        public void BinaryServiceCodec()
        {
            var payload = Encoding.UTF8.GetBytes("{\"x\":1}");
            var resp = BinaryEncoding.EncodeServerServiceCallResponse(5, 10, "json", payload);
            Assert.True(resp[0] == ServerOpcode.ServiceCallResponse, "Response opcode correct");

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
            Assert.True(decoded, "Client decode succeeds");
            AssertEqual(5u, sid, "serviceId roundtrip");
            AssertEqual(10u, cid, "callId roundtrip");
            Assert.True(decEnc == "json", "encoding roundtrip");
            Assert.True(Encoding.UTF8.GetString(pl) == "{\"x\":1}", "payload roundtrip");
        }

        [Fact]
        public void ParameterStoreRegisterGet()
        {
            var store = new FoxgloveParameterStore();
            store.Register("/speed", 100, "number", true);
            var p = store.GetWireParameter("/speed");
            Assert.True(p != null, "Get registered param");
            Assert.True((int)p.Value == 100, "value correct");

            var all = store.GetAllWireParameters();
            Assert.True(all.Count == 1, "GetAll returns one param");
        }

        [Fact]
        public void ParameterSetFromClient()
        {
            var store = new FoxgloveParameterStore();
            store.Register("/speed", 100, "number", true);
            store.Register("/readonly", 5, "number", false);

            Assert.True(store.TrySetFromClient("/speed", 200), "writable param changed");
            Assert.True(!store.TrySetFromClient("/readonly", 10), "readonly param rejected");
            Assert.True(!store.TrySetFromClient("/unknown", 1), "unknown param rejected");

            Assert.True((int)store.GetWireParameter("/speed").Value == 200, "value updated");
            Assert.True((int)store.GetWireParameter("/readonly").Value == 5, "readonly unchanged");
        }

        [Fact]
        public void ParameterSubscribeUnsubscribe()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Parameters.Register("/speed", 100, "number", true);
            fake.SimulateConnect(1);

            fake.SimulateText(1, "{\"op\":\"subscribeParameterUpdates\",\"parameterNames\":[]}");
            Assert.True(session.Parameters.TrySetFromClient("/speed", 200), "param change successful");
            fake.SentTexts(1).Clear();
            fake.SimulateText(1, "{\"op\":\"getParameters\",\"parameterNames\":[],\"id\":\"r1\"}");
            var response = fake.SentTexts(1).Last();
            var obj = JObject.Parse(response);
            Assert.True(obj["id"]?.ToString() == "r1", "getParameters id roundtrip");
            Assert.True(obj["parameters"]?[0]?["value"]?.Value<int>() == 200, "getParameters returns current value");

            fake.SimulateText(1, "{\"op\":\"unsubscribeParameterUpdates\",\"parameterNames\":[]}");
            Assert.True(true, "unsubscribeParameterUpdates does not throw");
        }

        [Fact]
        public void ServiceAdvertiseBeforeConnect()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
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
            Assert.True(hasAdv, "New client receives service advertise snapshot");
        }

        [Fact]
        public void ServiceAdvertiseAfterConnect()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);

            fake.BroadcastTexts.Clear();

            session.RegisterService(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            var hasAdv = false;
            foreach (var t in fake.BroadcastTexts)
                if (JObject.Parse(t)["op"]?.ToString() == "advertiseServices") hasAdv = true;
            Assert.True(hasAdv, "RegisterService broadcasts advertiseServices to already-connected client");
        }

        [Fact]
        public void ServiceCallFailureUnknown()
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
            Assert.True(failure["op"]?.ToString() == "serviceCallFailure", "Unknown service → failure");
        }

        [Fact]
        public void ServiceCallFailureEncoding()
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

            var req = BinaryEncoding.EncodeServerServiceCallResponse(1, 1, "protobuf", new byte[] { 1 });
            req[0] = ClientOpcode.ServiceCallRequest;
            fake.SimulateBinary(1, req);
            var sent = fake.SentTexts(1);
            Assert.True(sent.Last().Contains("Unsupported encoding"), "Wrong encoding → failure");
        }

        [Fact]
        public void ServiceCallEnqueueComplete()
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

            session.Services.CompleteResponse(1, 1, "json", Encoding.UTF8.GetBytes("{\"ok\":true}"));

            session.DrainServiceCalls();
            var binaries = fake.SentBinaries(1);
            Assert.True(binaries.Count > 0, "Service response sent as binary after drain");
        }

        [Fact]
        public void ServiceCallTimeout()
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

            foreach (var call in session.Services.DrainCompleted()) { } // drain nothing
            session.Services.SweepTimeouts(TimeSpan.Zero); // Zero timeout → all pending timed out
            session.DrainServiceCalls();
            var texts = fake.SentTexts(1);
            Assert.True(texts.Any(t => t.Contains("serviceCallFailure")), "Timeout produces serviceCallFailure");
        }

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
    }
}
