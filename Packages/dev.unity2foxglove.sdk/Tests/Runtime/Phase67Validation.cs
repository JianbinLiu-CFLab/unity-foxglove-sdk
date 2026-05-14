// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 67 validation for official Foxglove WebSocket status
// and removeStatus server messages.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates official Foxglove status message wire shape and the
    /// session/runtime/manager APIs that publish status messages.
    /// </summary>
    public static class Phase67Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 67: Official WebSocket Status Messages ===");
            _passed = 0;

            StatusJsonUsesOfficialNumericLevelAndOptionalId();
            RemoveStatusJsonUsesOfficialStatusIdsField();
            SessionStatusBroadcastsToConnectedClients();
            SessionRemoveStatusBroadcastsAndFiltersEmptyIds();
            RuntimeStatusFacadeDelegatesAndGuardsStartup();
            ManagerStatusFacadeSourceShape();
            UnityLoggerDoesNotAutoForwardToStatus();

            Console.WriteLine($"Phase 67: {_passed} checks passed.");
        }

        private static void StatusJsonUsesOfficialNumericLevelAndOptionalId()
        {
            var warning = JObject.Parse(JsonConvert.SerializeObject(new StatusMessage
            {
                Level = FoxgloveStatusLevel.Warning,
                Message = "Oh no",
                Id = "my-id"
            }));

            Check((string)warning["op"] == "status",
                "67A-1: status op matches official wire name");
            Check((int)warning["level"] == 1,
                "67A-2: warning status level serializes as numeric 1");
            Check((string)warning["message"] == "Oh no",
                "67A-3: status message is serialized");
            Check((string)warning["id"] == "my-id",
                "67A-4: status id is serialized when provided");

            var info = JObject.Parse(JsonConvert.SerializeObject(new StatusMessage
            {
                Level = FoxgloveStatusLevel.Info,
                Message = "Ready"
            }));

            Check((int)info["level"] == 0,
                "67A-5: info status level serializes as numeric 0");
            Check(info["id"] == null,
                "67A-6: status id is omitted when absent");

            var error = JObject.Parse(JsonConvert.SerializeObject(new StatusMessage
            {
                Level = FoxgloveStatusLevel.Error,
                Message = "Failed"
            }));

            Check((int)error["level"] == 2,
                "67A-7: error status level serializes as numeric 2");
        }

        private static void RemoveStatusJsonUsesOfficialStatusIdsField()
        {
            var remove = JObject.Parse(JsonConvert.SerializeObject(new RemoveStatusMessage
            {
                StatusIds = new List<string> { "status-1", "status-2" }
            }));

            Check((string)remove["op"] == "removeStatus",
                "67B-1: removeStatus op matches official wire name");
            Check(remove["statusIds"] is JArray,
                "67B-2: removeStatus uses statusIds array");
            Check(remove["status_ids"] == null,
                "67B-3: removeStatus does not use snake_case status_ids");
            Check(remove["statusIds"].Values<string>().SequenceEqual(new[] { "status-1", "status-2" }),
                "67B-4: removeStatus preserves ids in order");
        }

        private static void SessionStatusBroadcastsToConnectedClients()
        {
            var transport = new Phase67FakeTransport();
            using var session = new FoxgloveSession("phase67", transport);

            transport.Connect(7);
            transport.Connect(8);
            transport.ClearText();

            session.PublishStatus(FoxgloveStatusLevel.Warning, "Slow client", "transport/slow-client");

            Check(transport.BroadcastTexts.Count == 1,
                "67C-1: PublishStatus uses one BroadcastText call");
            Check(transport.TextsFor(7).Count == 1,
                "67C-2: first connected client receives status");
            Check(transport.TextsFor(8).Count == 1,
                "67C-3: second connected client receives status");

            var status = JObject.Parse(transport.BroadcastTexts[0]);
            Check((string)status["op"] == "status",
                "67C-4: session broadcasts status op");
            Check((int)status["level"] == 1,
                "67C-5: session broadcasts warning level");
            Check((string)status["message"] == "Slow client",
                "67C-6: session broadcasts status message");
            Check((string)status["id"] == "transport/slow-client",
                "67C-7: session broadcasts status id");

            transport.ClearText();
            session.PublishStatus(FoxgloveStatusLevel.Info, null);
            var nullMessage = JObject.Parse(transport.BroadcastTexts[0]);

            Check((string)nullMessage["message"] == string.Empty,
                "67C-8: null status message is normalized to empty string");
            Check(nullMessage["id"] == null,
                "67C-9: null status id is omitted");
        }

        private static void SessionRemoveStatusBroadcastsAndFiltersEmptyIds()
        {
            var transport = new Phase67FakeTransport();
            using var session = new FoxgloveSession("phase67", transport);

            transport.Connect(7);
            transport.Connect(8);
            transport.ClearText();

            session.RemoveStatus(new[] { "transport/slow-client" });

            Check(transport.BroadcastTexts.Count == 1,
                "67D-1: RemoveStatus broadcasts one text message");
            Check(transport.TextsFor(7).Count == 1 && transport.TextsFor(8).Count == 1,
                "67D-2: RemoveStatus reaches every connected client");

            var remove = JObject.Parse(transport.BroadcastTexts[0]);
            Check((string)remove["op"] == "removeStatus",
                "67D-3: session broadcasts removeStatus op");
            Check(remove["statusIds"].Values<string>().SequenceEqual(new[] { "transport/slow-client" }),
                "67D-4: session broadcasts statusIds");

            transport.ClearText();
            session.RemoveStatus(new[] { "", null });
            Check(transport.BroadcastTexts.Count == 0,
                "67D-5: RemoveStatus with only empty ids is a no-op");
        }

        private static void RuntimeStatusFacadeDelegatesAndGuardsStartup()
        {
            var inactiveTransport = new Phase67FakeTransport();
            using var inactiveRuntime = new FoxgloveRuntime(inactiveTransport, new SystemClock(), new DefaultSchemaRegistry());

            ExpectInvalidOperation(() => inactiveRuntime.PublishStatus(FoxgloveStatusLevel.Info, "before start"),
                "67E-1: PublishStatus before Start throws session-not-started error");
            ExpectInvalidOperation(() => inactiveRuntime.RemoveStatus("before-start"),
                "67E-2: RemoveStatus before Start throws session-not-started error");

            var transport = new Phase67FakeTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase67");
            transport.Connect(7);
            transport.Connect(8);
            transport.ClearText();

            runtime.PublishStatus(FoxgloveStatusLevel.Error, "Runtime error", "runtime/error");
            Check(transport.BroadcastTexts.Count == 1,
                "67E-3: runtime PublishStatus delegates to session");
            Check((string)JObject.Parse(transport.BroadcastTexts[0])["op"] == "status",
                "67E-4: runtime publishes status op");

            transport.ClearText();
            runtime.RemoveStatus();
            Check(transport.BroadcastTexts.Count == 0,
                "67E-5: runtime RemoveStatus with no params is a no-op");

            runtime.RemoveStatus("runtime/error");
            Check(transport.BroadcastTexts.Count == 1,
                "67E-6: runtime RemoveStatus delegates to session");
            Check((string)JObject.Parse(transport.BroadcastTexts[0])["op"] == "removeStatus",
                "67E-7: runtime publishes removeStatus op");
        }

        private static void ManagerStatusFacadeSourceShape()
        {
            var sourcePath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Status.cs";
            Check(File.Exists(sourcePath),
                "67F-1: FoxgloveManager status partial exists in Components/Manager");

            var source = File.ReadAllText(sourcePath);

            Check(source.Contains("Module: Runtime/Components/Manager"),
                "67F-2: manager status partial uses standard module header");
            Check(source.Contains("public void PublishStatus(FoxgloveStatusLevel level, string message, string id = null)"),
                "67F-3: manager exposes generic PublishStatus facade");
            Check(source.Contains("public void PublishInfoStatus(string message, string id = null)"),
                "67F-4: manager exposes info status convenience facade");
            Check(source.Contains("public void PublishWarningStatus(string message, string id = null)"),
                "67F-5: manager exposes warning status convenience facade");
            Check(source.Contains("public void PublishErrorStatus(string message, string id = null)"),
                "67F-6: manager exposes error status convenience facade");
            Check(source.Contains("public void RemoveStatus(params string[] ids)"),
                "67F-7: manager exposes RemoveStatus params facade");
            Check(source.Contains("_runtime.PublishStatus(level, message, id)") && source.Contains("_runtime.RemoveStatus(ids)"),
                "67F-8: manager delegates status calls to runtime");
            Check(source.Contains("PublishStatus called but server is not running")
                    && source.Contains("RemoveStatus called but server is not running")
                    && source.Contains("_warnedNotRunning"),
                "67F-9: manager uses existing not-running warning-once pattern");
        }

        private static void UnityLoggerDoesNotAutoForwardToStatus()
        {
            var loggerSource = File.ReadAllText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveLogger.cs");

            Check(!loggerSource.Contains("PublishStatus") && !loggerSource.Contains("RemoveStatus"),
                "67G-1: UnityLogger does not automatically forward logs to status messages");
        }

        private static void ExpectInvalidOperation(Action action, string label)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Session not started"))
            {
                Check(true, label);
                return;
            }

            throw new Exception("[FAIL] " + label);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        /// <summary>
        /// Fake transport that records broadcast text messages and mirrors
        /// them to each simulated connected client.
        /// </summary>
        private sealed class Phase67FakeTransport : IFoxgloveTransport
        {
            private readonly Dictionary<uint, List<string>> _sentTexts = new();
            private readonly HashSet<uint> _connectedClients = new();

            public readonly List<string> BroadcastTexts = new();
            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();

            public void BroadcastText(string json)
            {
                BroadcastTexts.Add(json);
                foreach (var clientId in _connectedClients)
                    SendText(clientId, json);
            }

            public void BroadcastBinary(byte[] data) { }

            public void SendText(uint clientId, string json)
            {
                if (!_sentTexts.TryGetValue(clientId, out var texts))
                {
                    texts = new List<string>();
                    _sentTexts[clientId] = texts;
                }

                texts.Add(json);
            }

            public void SendBinary(uint clientId, byte[] data) { }

            public void Connect(uint clientId)
            {
                _connectedClients.Add(clientId);
                if (!_sentTexts.ContainsKey(clientId))
                    _sentTexts[clientId] = new List<string>();
                OnClientConnected?.Invoke(clientId);
            }

            public IReadOnlyList<string> TextsFor(uint clientId)
                => _sentTexts.TryGetValue(clientId, out var texts) ? texts : Array.Empty<string>();

            public void ClearText()
            {
                BroadcastTexts.Clear();
                foreach (var texts in _sentTexts.Values)
                    texts.Clear();
            }
        }
    }
}
