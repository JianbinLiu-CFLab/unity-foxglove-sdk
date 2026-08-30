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
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
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
        public void ServiceSnapshotPrecedesTopicSnapshotForPanelReadiness()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/unity/status",
                Encoding = "json",
                SchemaName = "foxglove.Log"
            });
            session.RegisterService(new ServiceDescriptor
            {
                Name = "/foxrun/subscription-contracts",
                Type = "/foxrun/subscription-contracts",
                Request = new ServiceSchemaDescriptor { SchemaName = "FoxRunCatalogRequest" },
                Response = new ServiceSchemaDescriptor { SchemaName = "FoxRunCatalogResponse" }
            });

            fake.SimulateConnect(1);
            var operations = fake.SentTexts(1)
                .Select(text => JObject.Parse(text)["op"]?.ToString())
                .ToList();

            Assert.True(
                operations.IndexOf("advertiseServices") < operations.IndexOf("advertise"),
                "Service snapshot must precede topics so a panel topics render is a service-readiness barrier.");
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

            var request = EncodeClientServiceCallRequest(999, 1, "json", Encoding.UTF8.GetBytes("{}"));

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

            var req = EncodeClientServiceCallRequest(1, 1, "protobuf", new byte[] { 1 });
            fake.SimulateBinary(1, req);
            var sent = fake.SentTexts(1);
            Assert.True(sent.Last().Contains("Unsupported encoding"), "Wrong encoding → failure");
        }

        [Fact]
        public void InvalidUtf8ServicePayloadIsRejectedBeforeHandlerDispatch()
        {
            var fake = new Phase6FakeTransport();
            var calls = 0;
            var session = new FoxgloveSession("Test", fake);
            var serviceId = session.Services.Register(
                new ServiceDescriptor
                {
                    Name = "/strict", Type = "/strict",
                    Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                    Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
                },
                _ =>
                {
                    calls++;
                    return new JObject { ["ok"] = true };
                });
            fake.SimulateConnect(1);

            // A malformed UTF-8 byte inside a JSON string becomes U+FFFD under
            // replacement decoding and would otherwise be dispatched.
            var invalidUtf8Json = new byte[] { 0x22, 0xC3, 0x22 };
            fake.SimulateBinary(1, EncodeClientServiceCallRequest(serviceId, 7, "json", invalidUtf8Json));
            session.DrainServiceCalls();

            Assert.Equal(0, calls);
            Assert.Contains("Malformed JSON payload", fake.SentTexts(1).Last(), StringComparison.Ordinal);
        }

        [Fact]
        public void ServicePublicationFailureLeavesRegistryRetryable()
        {
            var fake = new Phase6FakeTransport();
            var session = new FoxgloveSession("Test", fake);
            var descriptor = new ServiceDescriptor
            {
                Name = "/atomic", Type = "/atomic",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            };

            fake.ThrowBroadcastCount = 1;
            Assert.Throws<InvalidOperationException>(() => session.RegisterService(descriptor));
            Assert.Empty(session.Services.GetAll());

            var serviceId = session.RegisterService(descriptor);
            Assert.NotNull(session.Services.GetById(serviceId));

            fake.ThrowBroadcastCount = 1;
            Assert.Throws<InvalidOperationException>(() => session.UnregisterService(serviceId));
            Assert.NotNull(session.Services.GetById(serviceId));

            Assert.True(session.UnregisterService(serviceId));
            Assert.Null(session.Services.GetById(serviceId));
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
            var frame = EncodeClientServiceCallRequest(1, 1, "json", payload);
            fake.SimulateBinary(1, frame);

            session.Services.CompleteResponse(1, 1, "json", Encoding.UTF8.GetBytes("{\"ok\":true}"));

            session.DrainServiceCalls();
            var binaries = fake.SentBinaries(1);
            Assert.True(binaries.Count > 0, "Service response sent as binary after drain");
        }

        [Fact]
        public void ServiceCallResponseSendFailureDoesNotDropLaterResponses()
        {
            var fake = new Phase6FakeTransport { ThrowBinaryForClientId = 1 };
            var session = new FoxgloveSession("Test", fake);
            var serviceId = session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });

            EnqueueCompletedCall(session, serviceId, clientId: 1, callId: 10);
            EnqueueCompletedCall(session, serviceId, clientId: 2, callId: 20);

            session.DrainServiceCalls();

            var response = Assert.Single(fake.SentBinaries(2));
            Assert.Equal(20u, BinaryEncoding.ReadU32LE(response, 5));
        }

        [Fact]
        public void ServiceCallRecordingFailureDoesNotDropLaterResponses()
        {
            var fake = new Phase6FakeTransport();
            var clock = new ThrowOnceClock();
            var session = new FoxgloveSession("Test", fake, clock);
            var serviceId = session.Services.Register(new ServiceDescriptor
            {
                Name = "/test", Type = "/test",
                Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
            });
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            session.SetRecorder(recorder);

            EnqueueCompletedCall(session, serviceId, clientId: 1, callId: 10);
            EnqueueCompletedCall(session, serviceId, clientId: 2, callId: 20);
            clock.ThrowOnNextRead = true;

            session.DrainServiceCalls();

            Assert.Single(fake.SentBinaries(1));
            Assert.Single(fake.SentBinaries(2));
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
            var frame = EncodeClientServiceCallRequest(1, 1, "json", payload);
            fake.SimulateBinary(1, frame);

            foreach (var call in session.Services.DrainCompleted()) { } // drain nothing
            session.Services.SweepTimeouts(TimeSpan.Zero); // Zero timeout → all pending timed out
            session.DrainServiceCalls();
            var texts = fake.SentTexts(1);
            Assert.True(texts.Any(t => t.Contains("serviceCallFailure")), "Timeout produces serviceCallFailure");
        }

        [Fact]
        public void ServiceHandlerExceptionIsDetailedLocallyAndRedactedRemotely()
        {
            var fake = new Phase6FakeTransport();
            var logger = new CaptureLogger();
            var services = new FoxgloveServiceRegistry();
            var serviceId = services.Register(
                new ServiceDescriptor
                {
                    Name = "/phase187/failure", Type = "/phase187/failure",
                    Request = new ServiceSchemaDescriptor { SchemaName = "/req" },
                    Response = new ServiceSchemaDescriptor { SchemaName = "/resp" }
                },
                _ => throw new InvalidOperationException("phase187-sensitive-detail"));
            using var session = new FoxgloveSession(
                "phase187-service",
                fake,
                logger: logger,
                serviceRegistry: services);
            fake.SimulateConnect(187);

            fake.SimulateBinary(
                187,
                EncodeClientServiceCallRequest(serviceId, 1, "json", Encoding.UTF8.GetBytes("{}")));
            session.DrainServiceCalls();

            var failure = fake.SentTexts(187).Last(text => text.Contains("serviceCallFailure"));
            Assert.Contains("Service handler failed", failure, StringComparison.Ordinal);
            Assert.DoesNotContain("phase187-sensitive-detail", failure, StringComparison.Ordinal);
            Assert.Contains(logger.Errors, entry => entry.Contains("phase187-sensitive-detail", StringComparison.Ordinal));
        }

        [Fact]
        public void MalformedPlaybackControlIsConsumedAndDiagnosed()
        {
            var fake = new Phase6FakeTransport();
            var logger = new CaptureLogger();
            using var session = new FoxgloveSession(
                "phase187-playback",
                fake,
                logger: logger);
            fake.SimulateConnect(187);

            fake.SimulateBinary(
                187,
                new[] { ClientOpcode.PlaybackControlRequest });

            Assert.Contains(
                logger.Warnings,
                entry => entry.Contains("PlaybackControl", StringComparison.Ordinal)
                         && entry.Contains("187", StringComparison.Ordinal));
            Assert.Empty(fake.SentBinaries(187));
        }

        private static byte[] EncodeClientServiceCallRequest(
            uint serviceId,
            uint callId,
            string encoding,
            byte[] payload)
        {
            var encodingBytes = Encoding.UTF8.GetBytes(encoding ?? "");
            payload ??= Array.Empty<byte>();
            var frame = new byte[1 + 4 + 4 + 4 + encodingBytes.Length + payload.Length];
            frame[0] = ClientOpcode.ServiceCallRequest;
            BinaryEncoding.WriteU32LE(frame, 1, serviceId);
            BinaryEncoding.WriteU32LE(frame, 5, callId);
            BinaryEncoding.WriteU32LE(frame, 9, (uint)encodingBytes.Length);
            Buffer.BlockCopy(encodingBytes, 0, frame, 13, encodingBytes.Length);
            Buffer.BlockCopy(payload, 0, frame, 13 + encodingBytes.Length, payload.Length);
            return frame;
        }

        private static void EnqueueCompletedCall(
            FoxgloveSession session,
            uint serviceId,
            uint clientId,
            uint callId)
        {
            session.Services.Enqueue(serviceId, callId, clientId, "json", Encoding.UTF8.GetBytes("{}"));
            session.Services.CompleteResponse(clientId, callId, "json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
        }

        private sealed class ThrowOnceClock : IFoxgloveClock
        {
            public bool ThrowOnNextRead { get; set; }

            public ulong NowNs
            {
                get
                {
                    if (ThrowOnNextRead)
                    {
                        ThrowOnNextRead = false;
                        throw new InvalidOperationException("Injected clock failure.");
                    }

                    return 1;
                }
            }
        }

        /// <summary>Captures detailed local service diagnostics for assertions.</summary>
        private sealed class CaptureLogger : IFoxgloveLogger
        {
            internal readonly List<string> Errors = new List<string>();
            internal readonly List<string> Warnings = new List<string>();
            public void LogWarning(string message) => Warnings.Add(message);
            public void LogError(string message) => Errors.Add(message);
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
            public uint? ThrowBinaryForClientId { get; set; }
            public int ThrowBroadcastCount { get; set; }

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
                if (ThrowBinaryForClientId == clientId)
                    throw new InvalidOperationException("Injected binary send failure.");
                if (!_sentBinaries.ContainsKey(clientId)) _sentBinaries[clientId] = new();
                _sentBinaries[clientId].Add(data);
            }
            public void BroadcastText(string json)
            {
                if (ThrowBroadcastCount > 0)
                {
                    ThrowBroadcastCount--;
                    throw new InvalidOperationException("Injected broadcast failure.");
                }
                BroadcastTexts.Add(json);
            }
            public void BroadcastBinary(byte[] data) { }
            public List<string> SentTexts(uint clientId) => _sentTexts.TryGetValue(clientId, out var l) ? l : new();
            public List<byte[]> SentBinaries(uint clientId) => _sentBinaries.TryGetValue(clientId, out var l) ? l : new();
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void SimulateBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
