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


public partial class ParameterAndServiceTests
    {
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
            public readonly List<string> OrderedTexts = new();
            public uint? ThrowBinaryForClientId { get; set; }
            public int ThrowBroadcastCount { get; set; }
            public bool RecordBroadcastBeforeThrow { get; set; }
            public int ThrowSendTextCount { get; set; }

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }
            public void SendText(uint clientId, string json)
            {
                if (ThrowSendTextCount > 0)
                {
                    ThrowSendTextCount--;
                    throw new InvalidOperationException("Injected text send failure.");
                }
                if (!_sentTexts.ContainsKey(clientId)) _sentTexts[clientId] = new();
                _sentTexts[clientId].Add(json);
                OrderedTexts.Add(json);
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
                    if (RecordBroadcastBeforeThrow)
                        BroadcastTexts.Add(json);
                    throw new InvalidOperationException("Injected broadcast failure.");
                }
                BroadcastTexts.Add(json);
                OrderedTexts.Add(json);
            }
            public void BroadcastBinary(byte[] data) { }
            public List<string> SentTexts(uint clientId) => _sentTexts.TryGetValue(clientId, out var l) ? l : new();
            public List<byte[]> SentBinaries(uint clientId) => _sentBinaries.TryGetValue(clientId, out var l) ? l : new();
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void SimulateBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }

        private sealed class ConnectRaceTransport : IFoxgloveTransport
        {
            private readonly object _gate = new();
            private readonly HashSet<uint> _connected = new();
            private readonly Dictionary<uint, List<string>> _sentTexts = new();
            private int _serverInfoBlockEntered;

            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public readonly ManualResetEventSlim ServerInfoEntered = new(false);
            public readonly ManualResetEventSlim ReleaseServerInfo = new(false);
            public int ThrowSendTextCount { get; set; }
            public bool RecordBeforeThrow { get; set; }

            public void Start(string host, int port) { }
            public void Stop() { }
            public void Dispose() { }

            public void SendText(uint clientId, string json)
            {
                if (ThrowSendTextCount > 0)
                {
                    if (RecordBeforeThrow)
                        RecordText(clientId, json);
                    ThrowSendTextCount--;
                    throw new InvalidOperationException("Injected text send failure.");
                }

                RecordText(clientId, json);

                if (clientId == 1
                    && JObject.Parse(json)["op"]?.ToString() == "serverInfo"
                    && Interlocked.Exchange(ref _serverInfoBlockEntered, 1) == 0)
                {
                    ServerInfoEntered.Set();
                    ReleaseServerInfo.Wait();
                }
            }

            private void RecordText(uint clientId, string json)
            {
                lock (_gate)
                {
                    if (!_sentTexts.TryGetValue(clientId, out var messages))
                    {
                        messages = new List<string>();
                        _sentTexts[clientId] = messages;
                    }
                    messages.Add(json);
                }
            }

            public void SendBinary(uint clientId, byte[] data) { }

            public void BroadcastText(string json)
            {
                uint[] clients;
                lock (_gate)
                    clients = _connected.ToArray();
                foreach (var clientId in clients)
                    SendText(clientId, json);
            }

            public void BroadcastBinary(byte[] data) { }

            public List<string> MessagesFor(uint clientId)
            {
                lock (_gate)
                    return _sentTexts.TryGetValue(clientId, out var messages)
                        ? new List<string>(messages)
                        : new List<string>();
            }

            public void ClearMessages()
            {
                lock (_gate)
                    _sentTexts.Clear();
            }

            public void SimulateConnect(uint clientId)
            {
                lock (_gate)
                    _connected.Add(clientId);
                OnClientConnected?.Invoke(clientId);
            }

            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
