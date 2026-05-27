// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates capabilities, logger injection, service call encapsulation, lifecycle survival, handler delegates, and time frame binary encoding.

using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase7Validation
    {
        private static int _passCount;

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        /// <summary>
        /// Entry point: runs all Phase 7 tests covering capabilities,
        /// logger injection, service call encapsulation, lifecycle
        /// survival, handler delegates, and time frame binary encoding.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("--- Phase 7 Tests ---");
            _passCount = 0;

            TestServerInfoIncludesParametersSubscribe();
            TestLoggerInjectedIntoSession();
            TestRemoveClientCallsDirect();
            TestEmptyParamNamesMeansAll();
            TestServiceCallCompleteFailEncapsulation();
            TestStopStartPreservesParameters();
            TestHandlerDelegateSuccessAndFailure();
            TestTimeFrameFormat();

            Console.WriteLine($"Phase 7: {_passCount} checks passed.\n");
        }

        /// <summary>
        /// Verifies serverInfo capabilities include parametersSubscribe
        /// and time.
        /// </summary>
        private static void TestServerInfoIncludesParametersSubscribe()
        {
            var fake = new Phase7FakeTransport();
            var s = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert(json.Contains("parametersSubscribe"), "capabilities includes parametersSubscribe");
            Assert(json.Contains("time"), "capabilities includes time");
        }

        /// <summary>
        /// A custom logger injected into FoxgloveSession must receive
        /// warning messages when the session is exercised.
        /// </summary>
        private static void TestLoggerInjectedIntoSession()
        {
            var testLogger = new TestLogger();
            var transport = new Phase7FakeTransport();
            var session = new FoxgloveSession("Test", transport, logger: testLogger);
            session.ForceLoggerTest();
            Assert(testLogger.WarningCount > 0, "Injected logger received warning messages");
        }

        /// <summary>
        /// <c>RemoveClientCalls</c> must remove only the specified
        /// client's pending calls while leaving other clients' calls
        /// intact.
        /// </summary>
        private static void TestRemoveClientCallsDirect()
        {
            var reg = new FoxgloveServiceRegistry();
            reg.Register(new ServiceDescriptor
            {
                Name = "/t", Type = "/t",
                Request = new ServiceSchemaDescriptor { SchemaName = "/r" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/s" }
            });
            reg.Enqueue(1, 1, 1, "json", new byte[] { 1 });
            reg.Enqueue(1, 2, 2, "json", new byte[] { 1 });
            reg.RemoveClientCalls(1);
            var pending = reg.GetPendingCalls();
            Assert(!pending.Any(c => c.ClientId == 1), "Client 1 calls removed directly from pending");
            Assert(pending.Any(c => c.ClientId == 2), "Client 2 calls still pending");
        }

        /// <summary>
        /// Subscribing with an empty parameterNames array must match any
        /// parameter; unsubscribing with empty array must clear all.
        /// </summary>
        private static void TestEmptyParamNamesMeansAll()
        {
            var reg = new ParameterSubscriptionRegistry();
            reg.Subscribe(1, new string[0]);
            Assert(reg.IsSubscribed(1, "any"), "Empty subscribe → subscribed to any param");
            reg.Unsubscribe(1, new string[0]);
            Assert(!reg.IsSubscribed(1, "any"), "Empty unsubscribe → cleared all");
        }

        /// <summary>
        /// <c>Complete</c> on a <c>FoxgloveServiceCall</c> sets
        /// <c>IsCompleted</c> and payload; <c>Fail</c> sets
        /// <c>IsCompleted</c> and failure message.
        /// </summary>
        private static void TestServiceCallCompleteFailEncapsulation()
        {
            var call = new FoxgloveServiceCall();
            call.Complete("json", new byte[] { 1, 2, 3 });
            Assert(call.IsCompleted, "Complete → IsCompleted");
            Assert(call.ResponsePayload.Length == 3, "Complete → payload set");

            var call2 = new FoxgloveServiceCall();
            call2.Fail("boom");
            Assert(call2.IsCompleted, "Fail → IsCompleted");
            Assert(call2.FailureMessage == "boom", "Fail → message set");
        }

        // ── Batch 2 lifecycle survival ──

        /// <summary>
        /// Parameters and services registered before <c>Start</c> must
        /// survive a Stop/Start cycle with their values intact.
        /// </summary>
        private static void TestStopStartPreservesParameters()
        {
            var rt = new FoxgloveRuntime();
            rt.RegisterParameter("/p1", JToken.FromObject(42), "number", true);
            rt.RegisterService(new ServiceDescriptor
            {
                Name = "/svc", Type = "/svc",
                Request = new ServiceSchemaDescriptor { SchemaName = "/r" },
                Response = new ServiceSchemaDescriptor { SchemaName = "/s" }
            });
            rt.Start("Test", "127.0.0.1", 18795);
            rt.Stop();
            var p = rt.Parameters.GetWireParameter("/p1");
            Assert(p != null, "Parameter survives Stop/Start");
            Assert((int)p.Value == 42, "Parameter value survives");
            var svcs = rt.GetServicesSnapshot();
            Assert(svcs.Count == 1, "Service survives Stop/Start");
            rt.Dispose();
        }

        // ── Batch 3 handler delegate ──

        /// <summary>
        /// Service handler delegates: a successful handler must return a
        /// JSON response, while a failing handler (throwing exception)
        /// must not crash and must remove the pending call.
        /// </summary>
        private static void TestHandlerDelegateSuccessAndFailure()
        {
            var rt = new FoxgloveRuntime();
            rt.Start("Test", "127.0.0.1", 18796);
            try
            {
                rt.RegisterService(new ServiceDescriptor
                {
                    Name = "/ok", Type = "/ok",
                    Request = new ServiceSchemaDescriptor { SchemaName = "/r" },
                    Response = new ServiceSchemaDescriptor { SchemaName = "/s" }
                }, req => JToken.Parse("{\"status\":\"ok\"}"));

                rt.RegisterService(new ServiceDescriptor
                {
                    Name = "/boom", Type = "/boom",
                    Request = new ServiceSchemaDescriptor { SchemaName = "/r" },
                    Response = new ServiceSchemaDescriptor { SchemaName = "/s" }
                }, req => throw new Exception("handler error"));

                var session = rt.Session;
                session.Services.Enqueue(1, 1, 1, "json", Encoding.UTF8.GetBytes("{}"));
                session.DrainServiceCalls();
                var pending = session.Services.GetPendingCalls();
                Assert(pending.Count == 0, "Handler delegate processed pending call");

                session.Services.Enqueue(2, 1, 2, "json", Encoding.UTF8.GetBytes("{}"));
                session.DrainServiceCalls();
                var pending2 = session.Services.GetPendingCalls();
                Assert(pending2.Count == 0, "Failing handler delegate removes pending call");
            }
            finally { rt.Dispose(); }
        }

        // ── Batch 4 Time frame ──

        /// <summary>
        /// <c>EncodeTime</c> produces a 9-byte frame with opcode 2 and
        /// the timestamp roundtrips through <c>BitConverter</c>.
        /// </summary>
        private static void TestTimeFrameFormat()
        {
            var frame = BinaryEncoding.EncodeTime(12345678901234567890UL);
            Assert(frame[0] == 2, "Time frame opcode is 2");
            Assert(frame.Length == 9, "Time frame is 9 bytes (opcode + 8 byte timestamp)");
            var decoded = BitConverter.ToUInt64(frame, 1);
            Assert(decoded == 12345678901234567890UL, "Time frame timestamp roundtrips");
        }

        /// <summary>
        /// Fake transport for Phase 7 recording per-client SendText
        /// and providing a connect simulator.
        /// </summary>
        private sealed class Phase7FakeTransport : Transport.IFoxgloveTransport
        {
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<string>> SentTexts = new();
            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint clientId, string json)
            {
                if (!SentTexts.ContainsKey(clientId)) SentTexts[clientId] = new();
                SentTexts[clientId].Add(json);
            }
            public void SendBinary(uint clientId, byte[] data) { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SimulateConnect(uint id) => OnClientConnected?.Invoke(id);
        }

        /// <summary>
        /// Simple logger that counts warning calls to verify logger
        /// injection into FoxgloveSession.
        /// </summary>
        private class TestLogger : IFoxgloveLogger
        {
            public int WarningCount;
            public void LogWarning(string message) { WarningCount++; }
            public void LogError(string message) { }
        }
    }
}
