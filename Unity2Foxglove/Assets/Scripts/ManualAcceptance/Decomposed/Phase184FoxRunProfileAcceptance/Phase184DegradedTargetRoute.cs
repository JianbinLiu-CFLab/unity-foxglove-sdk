// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Case-isolated Unity evidence surface for Phase184 profile acceptance.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;
using Debug = UnityEngine.Debug;

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    [DisallowMultipleComponent]
    public sealed partial class Phase184DegradedTargetRoute : Phase184AcceptanceRoute
    {
        public const string Topic = "/foxrun/phase184/degraded/state";
        public const string DegradedClientReadyTopic =
            "/foxrun/phase184/degraded/client_ready";

        private const float DegradedClientReadyTimeoutSeconds = 180f;
        private const float DegradedDeliveryPulseIntervalSeconds = 0.25f;
        private const uint DegradedClientReadyChannelId = 184902;
        private const int DegradedClientReadyCount = 18419;
        private const int MaximumDegradedClientReadyPayloadBytes = 4096;

        [FoxRun(
            Topic,
            Mode = FoxRunFlow.Publish,
            PublishTransportIds = new[]
            {
                FoxgloveWebSocketTransport.Id,
                Ros2BridgeTransportProvider.ProviderId
            },
            Encoding = FoxRunEncoding.Protobuf,
            Policy = FoxRunPolicy.Change)]
        [SerializeField] private Phase181State _degradedTarget;

        [Header("Read-only Degraded Evidence")]
        [SerializeField] private string _targetStatus = "Waiting";
        [SerializeField] private string _succeededTargets = string.Empty;
        [SerializeField] private string _failedTargets = string.Empty;
        [SerializeField] private float _degradedObservedAt = -1f;
        [SerializeField] private int _bridgeDiagnosticCount;

        private FoxgloveManager _manager;
        private bool _degradedClientReadyObserved;
        private float _degradedClientReadyDeadline;
        private float _nextDegradedDeliveryPulseAt;
        private int _degradedDeliveryPulses;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.DegradedTargetCase;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }
            _manager = FindFirstObjectByType<FoxgloveManager>();
            if (_manager == null)
            {
                Fail("Foxglove Manager is unavailable.");
                return;
            }
            _manager.OnClientMessageWithEncoding -= OnDegradedClientMessage;
            _manager.OnClientMessageWithEncoding += OnDegradedClientMessage;
            _targetStatus = "Waiting";
            _succeededTargets = string.Empty;
            _failedTargets = string.Empty;
            _degradedObservedAt = -1f;
            _bridgeDiagnosticCount = 0;
            _degradedClientReadyObserved = false;
            _degradedClientReadyDeadline =
                Time.realtimeSinceStartup
                + DegradedClientReadyTimeoutSeconds;
            _degradedDeliveryPulses = 0;
            PulseDegradedDelivery();
            Ready("topic=" + Topic + " bridge=deliberately-absent");
        }

        private void OnDisable()
        {
            if (_manager != null)
                _manager.OnClientMessageWithEncoding -= OnDegradedClientMessage;
            _manager = null;
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;
            if (!_degradedClientReadyObserved)
            {
                if (Time.realtimeSinceStartup
                    >= _degradedClientReadyDeadline)
                {
                    Fail("Foxglove degraded client readiness was not observed.");
                }
                return;
            }
            var webSocketActive =
                HasActivePublishTransport(
                    FoxgloveWebSocketTransport.Id);
            var bridgeActive =
                HasActivePublishTransport(
                    Ros2BridgeTransportProvider.ProviderId);
            _targetStatus =
                webSocketActive && !bridgeActive
                    ? "Degraded"
                    : "Waiting";
            _succeededTargets =
                webSocketActive
                    ? FoxgloveWebSocketTransport.Id
                    : "None";
            _failedTargets =
                bridgeActive
                    ? "None"
                    : Ros2BridgeTransportProvider.ProviderId;
            if (webSocketActive && !bridgeActive)
            {
                if (Time.realtimeSinceStartup >= _nextDegradedDeliveryPulseAt)
                    PulseDegradedDelivery();
                if (_degradedObservedAt < 0f)
                {
                    _degradedObservedAt = Time.realtimeSinceStartup;
                    _bridgeDiagnosticCount++;
                    Emit(
                        "PHASE184G_DEGRADED_WINDOW_STARTED",
                        "healthy=foxglove failed=bridge bridgeDiagnostics="
                        + _bridgeDiagnosticCount.ToString(CultureInfo.InvariantCulture));
                    return;
                }
                if (Time.realtimeSinceStartup - _degradedObservedAt < NegativeSeconds)
                    return;
                Pass(
                    "status=" + _targetStatus
                    + " succeeded=" + _succeededTargets
                    + " failed=" + _failedTargets
                    + " foxgloveState=Ready ros2BridgeState=Unavailable bridgeDiagnostics="
                    + _bridgeDiagnosticCount.ToString(CultureInfo.InvariantCulture));
                return;
            }
            _degradedObservedAt = -1f;
        }

        private void OnDegradedClientMessage(
            uint clientId,
            uint channelId,
            string topic,
            string encoding,
            byte[] payload)
        {
            if (!IsArmed
                || IsTerminal
                || _degradedClientReadyObserved
                || !string.Equals(
                    topic,
                    DegradedClientReadyTopic,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (channelId != DegradedClientReadyChannelId
                || !string.Equals(encoding, "json", StringComparison.Ordinal)
                || payload == null
                || payload.Length == 0
                || payload.Length > MaximumDegradedClientReadyPayloadBytes
                || !FoxRunInboundJson.TryReadObject<Phase181State>(
                    payload,
                    "clientReady",
                    out var ready,
                    out _)
                || !IsState(ready, "degraded-client-ready")
                || ready.Count != DegradedClientReadyCount)
            {
                Fail("Foxglove degraded client readiness was invalid.");
                return;
            }

            _degradedClientReadyObserved = true;
            PulseDegradedDelivery();
            Emit(
                "PHASE184G_DEGRADED_CLIENT_READY",
                "stage=degraded-client-ready");
        }

        private void PulseDegradedDelivery()
        {
            var pulse = _degradedDeliveryPulses++;
            _degradedTarget =
                State(
                    RunToken,
                    "degraded-local",
                    18420 + pulse);
            _nextDegradedDeliveryPulseAt =
                Time.realtimeSinceStartup
                + DegradedDeliveryPulseIntervalSeconds;
        }
    }

}
