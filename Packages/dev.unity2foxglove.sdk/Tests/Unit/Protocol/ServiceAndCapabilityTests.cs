// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Capabilities, logger injection, service registry/call encapsulation,
//          and time-frame encoding (pure-logic checks migrated from Phase7Validation).
//          The two real-server checks (TestStopStartPreservesParameters,
//          TestHandlerDelegateSuccessAndFailure) stay in the console runner.

using System;
using System.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// Server capabilities, logger injection, service registry/call encapsulation,
    /// parameter subscription matching, and time-frame binary encoding.
    /// Ported from Phase7Validation (pure-logic subset).
    /// </summary>
    [Trait("Phase", "7")]
    [Trait("Domain", "Protocol")]
    public class ServiceAndCapabilityTests
    {
        [Fact]
        public void ServerInfoIncludesParametersSubscribe()
        {
            var fake = new Phase7FakeTransport();
            var s = new FoxgloveSession("Test", fake);
            fake.SimulateConnect(1);
            var json = fake.SentTexts[1][0];
            Assert.True(json.Contains("parametersSubscribe"), "capabilities includes parametersSubscribe");
            Assert.True(json.Contains("time"), "capabilities includes time");
        }

        [Fact]
        public void LoggerInjectedIntoSession()
        {
            var testLogger = new TestLogger();
            var transport = new Phase7FakeTransport();
            var session = new FoxgloveSession("Test", transport, logger: testLogger);
            session.ForceLoggerTest();
            Assert.True(testLogger.WarningCount > 0, "Injected logger received warning messages");
        }

        [Fact]
        public void RemoveClientCallsDirect()
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
            Assert.True(!pending.Any(c => c.ClientId == 1), "Client 1 calls removed directly from pending");
            Assert.True(pending.Any(c => c.ClientId == 2), "Client 2 calls still pending");
        }

        [Fact]
        public void EmptyParamNamesMeansAll()
        {
            var reg = new ParameterSubscriptionRegistry();
            reg.Subscribe(1, new string[0]);
            Assert.True(reg.IsSubscribed(1, "any"), "Empty subscribe → subscribed to any param");
            reg.Unsubscribe(1, new string[0]);
            Assert.True(!reg.IsSubscribed(1, "any"), "Empty unsubscribe → cleared all");
        }

        [Fact]
        public void ServiceCallCompleteFailEncapsulation()
        {
            var call = new FoxgloveServiceCall();
            call.Complete("json", new byte[] { 1, 2, 3 });
            Assert.True(call.IsCompleted, "Complete → IsCompleted");
            Assert.True(call.ResponsePayload.Length == 3, "Complete → payload set");

            var call2 = new FoxgloveServiceCall();
            call2.Fail("boom");
            Assert.True(call2.IsCompleted, "Fail → IsCompleted");
            Assert.True(call2.FailureMessage == "boom", "Fail → message set");
        }

        [Fact]
        public void TimeFrameFormat()
        {
            var frame = BinaryEncoding.EncodeTime(12345678901234567890UL);
            Assert.True(frame[0] == 2, "Time frame opcode is 2");
            Assert.True(frame.Length == 9, "Time frame is 9 bytes (opcode + 8 byte timestamp)");
            var decoded = BitConverter.ToUInt64(frame, 1);
            Assert.True(decoded == 12345678901234567890UL, "Time frame timestamp roundtrips");
        }

        private sealed class Phase7FakeTransport : IFoxgloveTransport
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

        private class TestLogger : IFoxgloveLogger
        {
            public int WarningCount;
            public void LogWarning(string message) { WarningCount++; }
            public void LogError(string message) { }
        }
    }
}
