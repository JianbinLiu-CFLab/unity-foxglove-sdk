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
    public sealed partial class Phase184FoxgloveProfileRoute : Phase184AcceptanceRoute
    {
        public const string InheritedTopic = "/foxrun/phase184/profile/default";
        public const string JsonTopic = "/foxrun/phase184/profile/json";
        private const float ClientReadyTimeoutSeconds = 300f;
        private const float ProfileResponseTimeoutSeconds = 60f;

        [FoxRun(InheritedTopic, Mode = FoxRunFlow.PublishAndSubscribe)]
        [SerializeField] private Phase181State _inheritedFoxglove;

        [FoxRun(
            JsonTopic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            SubscribeTransportId = FoxgloveWebSocketTransport.Id,
            PublishTransportIds = new[]
            {
                FoxgloveWebSocketTransport.Id
            },
            Encoding = FoxRunEncoding.JSON,
            Policy = FoxRunPolicy.Change,
            OnlyIf = nameof(AcceptExplicitJson))]
        [SerializeField] private Phase181State _explicitJson;

        [Header("Read-only Profile Evidence")]
        [SerializeField] private bool _acceptExplicitJson = true;
        [SerializeField] private int _inboundApplyStages;
        [SerializeField] private bool _disabledWindowPreservedValue;
        [SerializeField] private bool _sameValueAppliedAfterRecovery;
        [SerializeField] private bool _laterLocalMutation;

        private Phase181State _valueBeforeDisabledWindow;
        private float _gateReopenAt;
        private float _localMutationAt;
        private bool _gateClosed;
        private bool _gateReopened;
        private bool _clientReadyObserved;
        private float _clientReadyDeadline;
        private float _profileResponseDeadline;
        private int _bootstrapSequence;
        private string _lastTargetEvidence = string.Empty;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.FoxgloveProfileCase;

        private bool AcceptExplicitJson() => _acceptExplicitJson;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }

            _acceptExplicitJson = true;
            _inboundApplyStages = 0;
            _gateClosed = false;
            _gateReopened = false;
            _clientReadyObserved = false;
            _disabledWindowPreservedValue = false;
            _sameValueAppliedAfterRecovery = false;
            _laterLocalMutation = false;
            _lastTargetEvidence = string.Empty;
            _bootstrapSequence = 0;
            PulseOutboundBootstrap();
            _clientReadyDeadline = Time.realtimeSinceStartup + ClientReadyTimeoutSeconds;
            Ready("topics=2 encodings=protobuf,json");
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

            EmitTargetStatus();
            if (!_clientReadyObserved)
            {
                if (IsState(_explicitJson, "profile-client-ready"))
                {
                    _clientReadyObserved = true;
                    PulseOutboundBootstrap();
                    _profileResponseDeadline =
                        Time.realtimeSinceStartup + ProfileResponseTimeoutSeconds;
                    Emit(
                        "PHASE184G_PROFILE_CLIENT_READY",
                        "stage=profile-client-ready");
                    return;
                }
                if (Time.realtimeSinceStartup >= _clientReadyDeadline)
                {
                    Fail("Foxglove profile client readiness was not observed.");
                }
                return;
            }

            if (!_gateClosed && IsState(_explicitJson, "profile-a"))
            {
                _inboundApplyStages++;
                _gateClosed = true;
                _acceptExplicitJson = false;
                _valueBeforeDisabledWindow = _explicitJson;
                _gateReopenAt = Time.realtimeSinceStartup + NegativeSeconds;
                Emit("PHASE184G_PROFILE_GATE_CLOSED", "stage=profile-a");
                return;
            }
            if (!_gateClosed
                && Time.realtimeSinceStartup >= _profileResponseDeadline)
            {
                Fail("Foxglove profile response was not observed.");
                return;
            }

            if (_gateClosed && !_gateReopened)
            {
                _disabledWindowPreservedValue =
                    ReferenceEquals(_explicitJson, _valueBeforeDisabledWindow);
                if (Time.realtimeSinceStartup < _gateReopenAt)
                    return;
                if (!_disabledWindowPreservedValue)
                {
                    Fail("OnlyIf-disabled JSON input changed the field.");
                    return;
                }
                _gateReopened = true;
                _acceptExplicitJson = true;
                Emit("PHASE184G_PROFILE_GATE_REOPENED", "stage=profile-b");
                return;
            }

            if (_gateReopened
                && !_sameValueAppliedAfterRecovery
                && IsState(_explicitJson, "profile-b"))
            {
                _inboundApplyStages++;
                _sameValueAppliedAfterRecovery = true;
                _laterLocalMutation = true;
                _explicitJson = State(
                    RunToken,
                    "profile-local-after-remote",
                    18405);
                _localMutationAt = Time.realtimeSinceStartup;
                Emit(
                    "PHASE184G_PROFILE_LOCAL_MUTATED",
                    "stage=profile-local-after-remote");
                return;
            }

            if (_laterLocalMutation
                && Time.realtimeSinceStartup - _localMutationAt >= NegativeSeconds)
            {
                Pass(
                    "applies=" + _inboundApplyStages.ToString(CultureInfo.InvariantCulture)
                    + " disabledPreserved=True recoveryApplied=True laterLocal=True");
            }
        }

        private void PulseOutboundBootstrap()
        {
            var pulse = _bootstrapSequence++;
            _inheritedFoxglove =
                State(RunToken, "profile-outbound", 18401 + pulse);
            _explicitJson =
                State(RunToken, "json-outbound", 18402 + pulse);
        }

        private void EmitTargetStatus()
        {
            var active =
                HasActivePublishTransport(
                    FoxgloveWebSocketTransport.Id);
            var evidence =
                "status=" + (active ? "Active" : "Unavailable")
                + " succeeded="
                + (active
                    ? FoxgloveWebSocketTransport.Id
                    : "None")
                + " failed="
                + (active
                    ? "None"
                    : FoxgloveWebSocketTransport.Id)
                + " topics=2";
            if (string.Equals(evidence, _lastTargetEvidence, StringComparison.Ordinal))
                return;
            _lastTargetEvidence = evidence;
            Emit("PHASE184G_FOXGLOVE_TARGET_STATUS", evidence);
        }
    }

}
