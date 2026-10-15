// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: serverInfo capabilities, parameter store/subscriptions, service
//          advertise, binary service codec, and call timeout/sweep
//          (migrated from Phase6Validation; all checks are fake-transport pure logic).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
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
    public partial class ParameterAndServiceTests
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
                        Request = new ServiceSchemaDescriptor { SchemaName = "/req", Encoding = "jsonschema" },
                        Response = new ServiceSchemaDescriptor { SchemaName = "/resp", Encoding = "jsonschema" }
                    }
                }
            };
            var ajson = JsonConvert.SerializeObject(advSvc);
            var aobj = JObject.Parse(ajson);
            Assert.True(aobj["op"]?.ToString() == "advertiseServices", "AdvertiseServices op");
            Assert.True(aobj["services"]?[0]?["request"]?["encoding"]?.ToString() == "jsonschema", "request encoding");
            Assert.True(aobj["services"]?[0]?["response"]?["encoding"]?.ToString() == "jsonschema", "response encoding");

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

            var req = EncodeClientServiceCallRequest(5, 10, "json", payload);

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
        public void ParameterOwnedRegistrationCannotRemoveReplacement()
        {
            var store = new FoxgloveParameterStore();
            var first = store.RegisterOwned("/shared", new JValue(1), "number", true);
            var second = store.RegisterOwned("/shared", new JValue(2), "number", true);

            first.Dispose();

            Assert.Equal(2, store.GetWireParameter("/shared").Value.Value<int>());

            second.Dispose();
            Assert.Null(store.GetWireParameter("/shared"));
        }

        [Fact]
        public void ParameterOwnedReleaseClearsClientUnsetMarker()
        {
            var store = new FoxgloveParameterStore();
            var lease = store.RegisterOwned("/owned", new JValue(1), "number", true);

            Assert.True(store.TrySetFromClientAllowUnset("/owned", JValue.CreateNull()));
            Assert.True(store.WasUnsetByClient("/owned"));
            Assert.True(store.UnregisterOwned(lease));
            Assert.False(store.WasUnsetByClient("/owned"));
        }

        [Fact]
        public void StaleOwnedReleaseDoesNotClearReplacementClientUnsetMarker()
        {
            var store = new FoxgloveParameterStore();
            var first = store.RegisterOwned("/replacement", new JValue(1), "number", true);
            Assert.True(store.TrySetFromClientAllowUnset("/replacement", JValue.CreateNull()));

            var second = store.RegisterOwned("/replacement", new JValue(2), "number", true);
            Assert.True(store.TrySetFromClientAllowUnset("/replacement", JValue.CreateNull()));

            Assert.False(store.UnregisterOwned(first));
            Assert.True(store.WasUnsetByClient("/replacement"));

            Assert.True(store.UnregisterOwned(second));
            Assert.False(store.WasUnsetByClient("/replacement"));
        }

        [Fact]
        public void ParameterRegistrationSnapshotsScalarValues()
        {
            var store = new FoxgloveParameterStore();
            var number = new JValue(1);
            var text = new JValue("before");
            var flag = new JValue(true);

            store.Register("/number", number, "number", true);
            store.Register("/text", text, "string", true);
            store.Register("/flag", flag, "boolean", true);

            number.Value = 9;
            text.Value = "after";
            flag.Value = false;

            Assert.Equal(1, store.GetWireParameter("/number").Value.Value<int>());
            Assert.Equal("before", store.GetWireParameter("/text").Value.Value<string>());
            Assert.True(store.GetWireParameter("/flag").Value.Value<bool>());

            var clientValue = new JValue(4);
            Assert.True(store.TrySetFromClient("/number", clientValue));
            clientValue.Value = 8;
            Assert.Equal(4, store.GetWireParameter("/number").Value.Value<int>());
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
        public void ParameterStoreSupportsExtendedTypesAndUnset()
        {
            var store = new FoxgloveParameterStore();
            store.Register("/bytes", "AQID", "byte_array", true);
            store.Register("/double", 1.25, "float64", true);
            store.Register("/doubles", new JArray(1, 2.5), "float64_array", true);
            store.Register("/flags", new JArray(true, false), "boolean[]", true);
            store.Register("/names", new JArray("left", "right"), "string[]", true);

            Assert.Equal("AQID", store.GetWireParameter("/bytes").Value.Value<string>());
            Assert.Equal(1.25d, store.GetWireParameter("/double").Value.Value<double>());
            Assert.Equal("float64_array", store.GetWireParameter("/doubles").Type);
            Assert.Equal(new JArray(1d, 2.5d), store.GetWireParameter("/doubles").Value);
            Assert.Equal(new JArray(true, false), store.GetWireParameter("/flags").Value);
            Assert.Equal(new JArray("left", "right"), store.GetWireParameter("/names").Value);

            Assert.True(store.TrySetFromClient("/double", new JValue(3.5)));
            Assert.Equal(3.5d, store.GetWireParameter("/double").Value.Value<double>());
            Assert.True(store.TrySetFromClientAllowUnset("/bytes", JValue.CreateNull()));
            Assert.Null(store.GetWireParameter("/bytes"));
            store.Register("/bytes", "AQID", "byte_array", true);
            Assert.False(store.TrySetFromClient("/bytes", JValue.CreateNull()));
            Assert.Equal("AQID", store.GetWireParameter("/bytes").Value.Value<string>());
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
        public void SetParametersNullUnsetsWritableEntry()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Parameters.Register("/unset", 7, "number", true);
            fake.SimulateConnect(1);
            fake.SimulateText(1,
                "{\"op\":\"setParameters\",\"parameters\":[{\"name\":\"/unset\",\"value\":null}],\"id\":\"unset\"}");

            var response = JObject.Parse(fake.SentTexts(1).Last());
            Assert.Equal("unset", response["id"]?.ToString());
            var removed = Assert.IsType<JObject>(Assert.Single((JArray)response["parameters"]));
            Assert.Equal("/unset", removed["name"]?.ToString());
            Assert.False(removed.ContainsKey("value"));
            Assert.Null(session.Parameters.GetWireParameter("/unset"));

            fake.SentTexts(1).Clear();
            fake.SimulateText(1,
                "{\"op\":\"setParameters\",\"parameters\":[{\"name\":\"/unset\",\"value\":null}],\"id\":\"unset-again\"}");

            var repeatedResponse = JObject.Parse(fake.SentTexts(1).Last());
            Assert.Equal("unset-again", repeatedResponse["id"]?.ToString());
            var repeated = Assert.IsType<JObject>(Assert.Single((JArray)repeatedResponse["parameters"]));
            Assert.Equal("/unset", repeated["name"]?.ToString());
            Assert.False(repeated.ContainsKey("value"));
        }

        [Fact]
        public void SetParametersNullBroadcastsNameOnlyRemovalToSubscribers()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Parameters.Register("/unset", 7, "number", true);
            fake.SimulateConnect(1);
            fake.SimulateConnect(2);
            fake.SimulateText(2,
                "{\"op\":\"subscribeParameterUpdates\",\"parameterNames\":[\"/unset\"]}");
            fake.SentTexts(1).Clear();
            fake.SentTexts(2).Clear();

            fake.SimulateText(1,
                "{\"op\":\"setParameters\",\"parameters\":[{\"name\":\"/unset\",\"value\":null}],\"id\":\"unset\"}");

            var response = JObject.Parse(fake.SentTexts(1).Last());
            var responseParameter = Assert.IsType<JObject>(Assert.Single((JArray)response["parameters"]));
            Assert.Equal("/unset", responseParameter["name"]?.ToString());
            Assert.False(responseParameter.ContainsKey("value"));

            var broadcast = JObject.Parse(fake.SentTexts(2).Last());
            var broadcastParameter = Assert.IsType<JObject>(Assert.Single((JArray)broadcast["parameters"]));
            Assert.Equal("/unset", broadcastParameter["name"]?.ToString());
            Assert.False(broadcastParameter.ContainsKey("value"));
        }

        [Fact]
        public void SetParametersDeduplicatesNamesWhileApplyingSequentialUpdates()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.Parameters.Register("/duplicate", 1, "number", true);
            fake.SimulateConnect(1);
            fake.SimulateConnect(2);
            fake.SimulateText(2,
                "{\"op\":\"subscribeParameterUpdates\",\"parameterNames\":[\"/duplicate\"]}");
            fake.SentTexts(1).Clear();
            fake.SentTexts(2).Clear();

            fake.SimulateText(1,
                "{\"op\":\"setParameters\",\"parameters\":[" +
                "{\"name\":\"/duplicate\",\"value\":2}," +
                "{\"name\":\"/duplicate\",\"value\":3}],\"id\":\"duplicate\"}");

            var response = JObject.Parse(fake.SentTexts(1).Last());
            var responseParameters = Assert.IsType<JArray>(response["parameters"]);
            var responseParameter = Assert.Single(responseParameters);
            Assert.Equal(3, responseParameter["value"]?.Value<int>());

            var broadcast = JObject.Parse(fake.SentTexts(2).Last());
            var broadcastParameters = Assert.IsType<JArray>(broadcast["parameters"]);
            var broadcastParameter = Assert.Single(broadcastParameters);
            Assert.Equal(3, broadcastParameter["value"]?.Value<int>());
        }

        [Fact]
        public void RuntimeTrySetParameterNullDoesNotUnsetWritableEntry()
        {
            using var runtime = new FoxgloveRuntime(
                new Phase6FakeTransport(),
                new SystemClock(),
                new DefaultSchemaRegistry());
            runtime.RegisterParameter("/runtime-unset", 7, "number", true);

            Assert.False(runtime.TrySetParameter("/runtime-unset", null));
            Assert.Equal(7, runtime.Parameters.GetWireParameter("/runtime-unset").Value.Value<int>());
            Assert.False(runtime.TrySetParameter("/runtime-unset", JValue.CreateNull()));
            Assert.Equal(7, runtime.Parameters.GetWireParameter("/runtime-unset").Value.Value<int>());
        }

    }
}
