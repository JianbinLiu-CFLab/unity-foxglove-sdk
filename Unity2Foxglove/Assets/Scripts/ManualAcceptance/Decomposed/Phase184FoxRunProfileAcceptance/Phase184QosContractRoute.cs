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
    public sealed partial class Phase184QosContractRoute : Phase184AcceptanceRoute
    {
        public const string SystemDefaultTopic =
            "/foxrun/phase184/qos/system_default";
        public const string KeepAllTopic =
            "/foxrun/phase184/qos/keep_all";
        public const string KeepLastDepthTopic =
            "/foxrun/phase184/qos/keep_last_depth";

        [FoxRun(
            SystemDefaultTopic,
            Mode = FoxRunFlow.Publish,
            PublishTransportIds = new[]
            {
                Phase184FoxRunProfileAcceptance.NativeTransportId,
                Ros2BridgeTransportProvider.ProviderId
            },
            Reliability = FoxRunDeliveryReliability.SystemDefault,
            Durability = FoxRunDeliveryDurability.SystemDefault,
            History = FoxRunDeliveryHistory.SystemDefault)]
        [SerializeField] private Phase181State _qosSystemDefault;

        [FoxRun(
            KeepAllTopic,
            Mode = FoxRunFlow.Publish,
            PublishTransportIds = new[]
            {
                Phase184FoxRunProfileAcceptance.NativeTransportId,
                Ros2BridgeTransportProvider.ProviderId
            },
            History = FoxRunDeliveryHistory.KeepAll)]
        [SerializeField] private Phase181State _qosKeepAll;

        [FoxRun(
            KeepLastDepthTopic,
            Mode = FoxRunFlow.Publish,
            PublishTransportIds = new[]
            {
                Phase184FoxRunProfileAcceptance.NativeTransportId,
                Ros2BridgeTransportProvider.ProviderId
            },
            Reliability = FoxRunDeliveryReliability.BestEffort,
            Durability = FoxRunDeliveryDurability.TransientLocal,
            History = FoxRunDeliveryHistory.KeepLast,
            Depth = 7)]
        [SerializeField] private Phase181State _qosKeepLastDepth;

        [Header("Read-only QoS Evidence")]
        [SerializeField] private int _readyContracts;
        [SerializeField] private bool _nativeReadyForBridge;
        private string _lastSystemDefaultEvidence = string.Empty;
        private string _lastKeepAllEvidence = string.Empty;
        private string _lastKeepLastDepthEvidence = string.Empty;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.QosContractCase;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }
            _qosSystemDefault = State(RunToken, "qos-system-default", 18431);
            _qosKeepAll = State(RunToken, "qos-keep-all", 18432);
            _qosKeepLastDepth = State(RunToken, "qos-keep-last-depth", 18433);
            _lastSystemDefaultEvidence = string.Empty;
            _lastKeepAllEvidence = string.Empty;
            _lastKeepLastDepthEvidence = string.Empty;
            Ready("topics=3 targets=native,bridge");
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

            if (!_nativeReadyForBridge
                && HasActivePublishTransport(
                    Phase184FoxRunProfileAcceptance.NativeTransportId))
            {
                _nativeReadyForBridge = true;
                Emit(
                    "PHASE184G_NATIVE_READY_FOR_BRIDGE",
                    "topic=" + SystemDefaultTopic + " target="
                    + Phase184FoxRunProfileAcceptance.NativeTransportId);
            }

            _readyContracts = 0;
            CountReady(SystemDefaultTopic, ref _lastSystemDefaultEvidence);
            CountReady(KeepAllTopic, ref _lastKeepAllEvidence);
            CountReady(KeepLastDepthTopic, ref _lastKeepLastDepthEvidence);
            if (_readyContracts == 3)
                Pass("readyContracts=3 requestedPolicies=system-default,keep-all,keep-last-depth-7");
        }

        private void CountReady(string topic, ref string lastEvidence)
        {
            var nativeActive =
                HasActivePublishTransport(
                    Phase184FoxRunProfileAcceptance.NativeTransportId);
            var bridgeActive =
                HasActivePublishTransport(
                    Ros2BridgeTransportProvider.ProviderId);
            var evidence =
                "topic=" + Phase184AcceptanceText.SafeMarker(topic)
                + " status="
                + (nativeActive && bridgeActive
                    ? "Active"
                    : "Waiting")
                + " succeeded="
                + Phase184AcceptanceText.FormatTransportIds(
                    ActiveProviderIds(nativeActive, bridgeActive))
                + " failed="
                + Phase184AcceptanceText.FormatTransportIds(
                    InactiveProviderIds(nativeActive, bridgeActive));
            if (!string.Equals(evidence, lastEvidence, StringComparison.Ordinal))
            {
                lastEvidence = evidence;
                Emit("PHASE184G_QOS_TARGET_STATUS", evidence);
            }

            if (nativeActive && bridgeActive)
                _readyContracts++;
        }

        private static IEnumerable<string> ActiveProviderIds(
            bool nativeActive,
            bool bridgeActive)
        {
            if (nativeActive)
                yield return Phase184FoxRunProfileAcceptance.NativeTransportId;
            if (bridgeActive)
                yield return Ros2BridgeTransportProvider.ProviderId;
        }

        private static IEnumerable<string> InactiveProviderIds(
            bool nativeActive,
            bool bridgeActive)
        {
            if (!nativeActive)
                yield return Phase184FoxRunProfileAcceptance.NativeTransportId;
            if (!bridgeActive)
                yield return Ros2BridgeTransportProvider.ProviderId;
        }
    }

}
