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
    public sealed partial class Phase184MultiTargetRoute : Phase184AcceptanceRoute
    {
        public const string Topic = "/foxrun/phase184/multi/state";
        private const float WarmupPulseIntervalSeconds = 0.25f;
        private const float WarmupTimeoutSeconds = 300f;

        [FoxRun(
            Topic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            SubscribeTransportId =
                Phase184FoxRunProfileAcceptance.NativeTransportId,
            PublishTransportIds = new[]
            {
                FoxgloveWebSocketTransport.Id,
                Phase184FoxRunProfileAcceptance.NativeTransportId,
                Ros2BridgeTransportProvider.ProviderId
            },
            Encoding = FoxRunEncoding.Protobuf,
            Policy = FoxRunPolicy.Change,
            Hz = 4f)]
        [SerializeField] private Phase181State _multiTarget;

        [Header("Read-only Fanout/Origin Evidence")]
        [SerializeField] private string _targetStatus = "Waiting";
        [SerializeField] private long _sameOriginDrops;
        [SerializeField] private bool _remoteApplied;
        [SerializeField] private bool _laterLocalMutation;

        private float _remoteObservedAt;
        private float _nextWarmupPulseAt;
        private float _warmupDeadline;
        private int _warmupPulses;
        private bool _nativeReadyForBridge;
        private bool _initialArmed;
        private FoxgloveManager _manager;
        private string _lastBridgeRuntimeError = string.Empty;
        private string _lastTargetEvidence = string.Empty;
        private int _bridgeRuntimeFailures;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.MultiTargetCase;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }

            _manager = FindFirstObjectByType<FoxgloveManager>();
            _lastBridgeRuntimeError = string.Empty;
            _lastTargetEvidence = string.Empty;
            _bridgeRuntimeFailures = 0;
            _warmupPulses = 0;
            _warmupDeadline =
                Time.realtimeSinceStartup + WarmupTimeoutSeconds;
            PulseWarmupUntilTargetsReady();
            Ready("topic=" + Topic + " targets=foxglove,native,bridge");
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

            EmitBridgeRuntimeFailure();
            var allProvidersActive = HasActivePublishTransports(
                FoxgloveWebSocketTransport.Id,
                Phase184FoxRunProfileAcceptance.NativeTransportId,
                Ros2BridgeTransportProvider.ProviderId);
            EmitTargetStatus(allProvidersActive);
            _targetStatus =
                allProvidersActive ? "Active" : "Waiting";
            if (!_nativeReadyForBridge
                && HasActivePublishTransport(
                    Phase184FoxRunProfileAcceptance.NativeTransportId))
            {
                _nativeReadyForBridge = true;
                Emit(
                    "PHASE184G_NATIVE_READY_FOR_BRIDGE",
                    "topic=" + Topic + " target="
                    + Phase184FoxRunProfileAcceptance.NativeTransportId);
            }
            if (!_initialArmed && allProvidersActive)
            {
                _initialArmed = true;
                _multiTarget = State(RunToken, "multi-local-1", 18411);
                Emit("PHASE184G_MULTI_LOCAL_ARMED", "stage=1");
            }
            if (!_initialArmed)
            {
                if (Time.realtimeSinceStartup >= _warmupDeadline)
                {
                    Fail("Multi-target readiness was not observed.");
                    return;
                }
                if (Time.realtimeSinceStartup >= _nextWarmupPulseAt)
                    PulseWarmupUntilTargetsReady();
            }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
            if (FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(
                    this,
                    Topic,
                    out var snapshot))
            {
                _sameOriginDrops = snapshot.SameOriginDrops;
            }
#endif

            if (!_remoteApplied && IsState(_multiTarget, "multi-remote-2"))
            {
                _remoteApplied = true;
                _remoteObservedAt = Time.realtimeSinceStartup;
                Emit("PHASE184G_MULTI_REMOTE_APPLIED", "stage=2");
            }

            if (_remoteApplied
                && !_laterLocalMutation
                && _sameOriginDrops > 0
                && Time.realtimeSinceStartup - _remoteObservedAt >= NegativeSeconds)
            {
                _laterLocalMutation = true;
                _multiTarget = State(RunToken, "multi-local-3", 18413);
                Emit("PHASE184G_MULTI_LOCAL_MUTATED", "stage=3");
            }

            if (_laterLocalMutation && allProvidersActive)
            {
                Pass(
                    "remoteApplied=True sameOriginDrops="
                    + _sameOriginDrops.ToString(CultureInfo.InvariantCulture)
                    + " laterLocal=True");
            }
        }

        private void PulseWarmupUntilTargetsReady()
        {
            _multiTarget =
                State(RunToken, "multi-warmup", 18410 + _warmupPulses++);
            _nextWarmupPulseAt =
                Time.realtimeSinceStartup + WarmupPulseIntervalSeconds;
        }

        private void EmitBridgeRuntimeFailure()
        {
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();
            if (_manager == null)
                return;

            var provider =
                _manager.GetComponent<Ros2BridgeTransportProvider>();
            var stats = provider == null
                ? Ros2BridgeStatsSnapshot.Disabled
                : provider.GetStatsSnapshot();
            var error = stats.LastError ?? string.Empty;
            if (string.IsNullOrWhiteSpace(error)
                || string.Equals(
                    error,
                    _lastBridgeRuntimeError,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastBridgeRuntimeError = error;
            _bridgeRuntimeFailures++;
            Emit(
                "PHASE184G_BRIDGE_RUNTIME_FAILURE",
                "connected=" + stats.Connected
                + " connecting=" + stats.Connecting
                + " lastError="
                + Phase184AcceptanceText.SafeMarker(error));
        }

        private void EmitTargetStatus(bool allProvidersActive)
        {
            var evidence =
                "status="
                + (allProvidersActive ? "Active" : "Waiting")
                + " succeeded="
                + Phase184AcceptanceText.FormatTransportIds(
                    ActiveProviderIds(
                        FoxgloveWebSocketTransport.Id,
                        Phase184FoxRunProfileAcceptance.NativeTransportId,
                        Ros2BridgeTransportProvider.ProviderId))
                + " failed="
                + Phase184AcceptanceText.FormatTransportIds(
                    InactiveProviderIds(
                        FoxgloveWebSocketTransport.Id,
                        Phase184FoxRunProfileAcceptance.NativeTransportId,
                        Ros2BridgeTransportProvider.ProviderId))
                + " bridgeRuntimeFailures="
                + _bridgeRuntimeFailures.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(
                    evidence,
                    _lastTargetEvidence,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastTargetEvidence = evidence;
            Emit("PHASE184G_MULTI_TARGET_STATUS", evidence);
        }

        private IEnumerable<string> ActiveProviderIds(
            params string[] providerIds)
        {
            foreach (var id in providerIds)
            {
                if (HasActivePublishTransport(id))
                    yield return id;
            }
        }

        private IEnumerable<string> InactiveProviderIds(
            params string[] providerIds)
        {
            foreach (var id in providerIds)
            {
                if (!HasActivePublishTransport(id))
                    yield return id;
            }
        }
    }

}
