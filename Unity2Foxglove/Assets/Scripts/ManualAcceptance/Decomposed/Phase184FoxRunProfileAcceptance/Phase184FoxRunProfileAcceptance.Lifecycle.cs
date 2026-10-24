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

    public sealed partial class Phase184FoxRunProfileAcceptance
    {
        private void Awake()
        {
            DisableEveryRoute();
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();

            if (!TryLoadContext(out var context, out var error))
            {
                FailContext(error);
                return;
            }

            if (_manager == null)
            {
                FailContext("The Phase184 acceptance scene has no FoxgloveManager.");
                return;
            }

            ConfigureDirectionalDefaults(context.CaseId);
            var route = SelectRoute(context.CaseId);
            if (route == null)
            {
                FailContext("The selected Phase184 route is absent from the scene.");
                return;
            }

            _selectedCase = context.CaseId;
            _tokenDigestPrefix = context.TokenDigestPrefix;
            _contextValidated = true;
            ResetTransportClientEvidence(context.Token);
            _status = "Validated " + context.CaseId + "; activating its isolated route.";
            CaptureProfiles();
            _activeRoute = route;
            _profileEvidenceEmitted = false;
            route.Arm(context);
            route.gameObject.SetActive(true);
            Emit(
                "PHASE184G_CONTEXT_READY",
                "case=" + context.CaseId
                + " token=" + Phase184AcceptanceText.SafeMarker(context.Token)
                + " tokenDigest=" + context.TokenDigestPrefix);
        }

        private void Update()
        {
            if (!_contextValidated || _manager == null)
                return;

            CaptureRuntimeProfileEvidence();
            if (!_contextValidated)
                return;

            CaptureTransportClientEvidence();
            _effectivePublishProfile =
                Phase184AcceptanceText.FormatTransportIds(
                    _manager.ActiveFoxRunPublishSessionPolicy
                        .PublishTransportIds)
                + " / "
                + _manager.ActiveFoxRunPublishEncoding;
            _effectiveSubscribeProfile =
                _manager.ActiveFoxRunSubscriptionSessionPolicy
                    .DefaultProvider.Value
                + " / "
                + _manager.ActiveFoxRunSubscriptionEncoding;
        }

        private void CaptureRuntimeProfileEvidence()
        {
            if (_profileEvidenceEmitted || _activeRoute == null)
                return;

            var subscribeTransportIds =
                new HashSet<string>(StringComparer.Ordinal);
            var publishTransportIds =
                new HashSet<string>(StringComparer.Ordinal);
            var hasPublish = false;
            var hasSubscribe = false;
            var publishProtobuf = false;
            var publishJson = false;
            var subscribeProtobuf = false;
            var subscribeJson = false;
            var declarationCount = 0;
            foreach (var field in _activeRoute.GetType().GetFields(
                         BindingFlags.Instance
                         | BindingFlags.Public
                         | BindingFlags.NonPublic))
            {
                foreach (FoxRunAttribute declaration in field.GetCustomAttributes(
                             typeof(FoxRunAttribute),
                             false))
                {
                    declarationCount++;
                    if (declaration.Mode == FoxRunFlow.Publish
                        || declaration.Mode == FoxRunFlow.PublishAndSubscribe)
                    {
                        hasPublish = true;
                        if (declaration.PublishTransportIds != null
                            && declaration.PublishTransportIds.Length > 0)
                        {
                            AddTransportIds(
                                publishTransportIds,
                                declaration.PublishTransportIds);
                        }
                        else
                        {
                            foreach (var id in
                                     _manager.ConfiguredFoxRunPublishTransportIds)
                            {
                                publishTransportIds.Add(id.Value);
                            }
                        }
                        AddEncoding(
                            declaration.Encoding != 0
                                ? declaration.Encoding
                                : _manager.ActiveFoxRunPublishEncoding,
                            ref publishProtobuf,
                            ref publishJson);
                    }
                    if (declaration.Mode == FoxRunFlow.Subscribe
                        || declaration.Mode == FoxRunFlow.PublishAndSubscribe)
                    {
                        hasSubscribe = true;
                        subscribeTransportIds.Add(
                            string.IsNullOrWhiteSpace(
                                declaration.SubscribeTransportId)
                                ? _manager
                                    .ConfiguredFoxRunSubscribeTransportId
                                    .Value
                                : declaration.SubscribeTransportId);
                        AddEncoding(
                            declaration.Encoding != 0
                                ? declaration.Encoding
                                : _manager.ActiveFoxRunSubscriptionEncoding,
                            ref subscribeProtobuf,
                            ref subscribeJson);
                    }
                }
            }

            if (declarationCount == 0)
            {
                FailContext("The selected Phase184 route has no FoxRun declaration.");
                return;
            }

            Emit(
                "PHASE184G_PROFILE_EVIDENCE",
                "case=" + _selectedCase
                + " token=" + Phase184AcceptanceText.SafeMarker(_runToken)
                + " source="
                + (hasSubscribe
                    ? Phase184AcceptanceText.FormatTransportIds(
                        subscribeTransportIds)
                    : "None")
                + " targets="
                + (hasPublish
                    ? Phase184AcceptanceText.FormatTransportIds(
                        publishTransportIds)
                    : "None")
                + " publishEncoding="
                + FormatEncodings(hasPublish, publishProtobuf, publishJson)
                + " subscribeEncoding="
                + FormatEncodings(hasSubscribe, subscribeProtobuf, subscribeJson));
            _profileEvidenceEmitted = true;
        }

        private static void AddTransportIds(
            ISet<string> destination,
            IEnumerable<string> source)
        {
            foreach (var id in source)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    destination.Add(id);
            }
        }

        private static void AddEncoding(
            FoxRunEncoding encoding,
            ref bool protobuf,
            ref bool json)
        {
            protobuf |= encoding == FoxRunEncoding.Protobuf;
            json |= encoding == FoxRunEncoding.JSON;
        }

        private static string FormatEncodings(
            bool applicable,
            bool protobuf,
            bool json)
        {
            if (!applicable)
                return "not_applicable";
            if (protobuf && json)
                return "protobuf,json";
            if (protobuf)
                return "protobuf";
            if (json)
                return "json";
            return "None";
        }

        private void DisableEveryRoute()
        {
            SetRouteActive(_foxgloveProfile, false);
            SetRouteActive(_multiTarget, false);
            SetRouteActive(_degradedTarget, false);
            SetRouteActive(_qosContract, false);
            SetRouteActive(_stream, false);
        }

        private static void SetRouteActive(Phase184AcceptanceRoute route, bool active)
        {
            if (route != null && route.gameObject.activeSelf != active)
                route.gameObject.SetActive(active);
        }

        private Phase184AcceptanceRoute SelectRoute(string caseId)
        {
            switch (caseId)
            {
                case FoxgloveProfileCase:
                    return _foxgloveProfile;
                case MultiTargetCase:
                    return _multiTarget;
                case DegradedTargetCase:
                    return _degradedTarget;
                case QosContractCase:
                    return _qosContract;
                case StreamCase:
                    return _stream;
                default:
                    return null;
            }
        }

        private void ConfigureDirectionalDefaults(string caseId)
        {
            _manager.DefaultFoxRunPublishEncoding = FoxRunEncoding.Protobuf;
            _manager.DefaultFoxRunSubscriptionEncoding = FoxRunEncoding.Protobuf;
            var publishTransportIds = new[]
            {
                FoxgloveWebSocketTransport.Id
            };
            var subscribeTransportId = FoxgloveWebSocketTransport.Id;

            switch (caseId)
            {
                case MultiTargetCase:
                    publishTransportIds = new[]
                    {
                        FoxgloveWebSocketTransport.Id,
                        NativeTransportId,
                        Ros2BridgeTransportProvider.ProviderId
                    };
                    subscribeTransportId =
                        NativeTransportId;
                    break;
                case QosContractCase:
                    publishTransportIds = new[]
                    {
                        NativeTransportId,
                        Ros2BridgeTransportProvider.ProviderId
                    };
                    subscribeTransportId =
                        NativeTransportId;
                    break;
                case StreamCase:
                    publishTransportIds = new[]
                    {
                        NativeTransportId
                    };
                    subscribeTransportId =
                        NativeTransportId;
                    break;
            }

            _manager.ConfigureFoxRunTransports(
                publishTransportIds,
                subscriptionsEnabled: true,
                subscribeTransportId);
        }

        private void CaptureProfiles()
        {
            _requestedPublishProfile =
                Phase184AcceptanceText.FormatTransportIds(
                    _manager.ConfiguredFoxRunPublishTransportIds)
                + " / "
                + _manager.DefaultFoxRunPublishEncoding;
            _requestedSubscribeProfile =
                _manager.ConfiguredFoxRunSubscribeTransportId.Value
                + " / "
                + _manager.DefaultFoxRunSubscriptionEncoding;
            _effectivePublishProfile = _requestedPublishProfile;
            _effectiveSubscribeProfile = _requestedSubscribeProfile;
        }

        private void ResetTransportClientEvidence(string runToken)
        {
            _runToken = runToken;
            _transportClientMarkerState.Reset();
            _nextTransportClientSampleAt = 0f;
        }

        private void CaptureTransportClientEvidence()
        {
            if (_transportClientMarkerState.IsOverflowed)
                return;

            var now = Time.unscaledTime;
            if (now < _nextTransportClientSampleAt)
                return;
            _nextTransportClientSampleAt =
                now + TransportClientSampleIntervalSeconds;

            var stats = _manager.GetTransportStatsSnapshot();
            if (stats == null || !stats.Supported)
            {
                _transportClientMarkerState.ResetPending();
                return;
            }

            var active = stats.ActiveClientCount;
            var accepted = stats.TotalAcceptedClients;
            var decision =
                _transportClientMarkerState.Observe(active, accepted);
            switch (decision.Kind)
            {
                case Phase184TransportClientMarkerKind.Normal:
                    EmitTransportClientEvidence(
                        "PHASE184H_TRANSPORT_CLIENTS",
                        decision.ActiveClientCount,
                        decision.TotalAcceptedClients);
                    break;
                case Phase184TransportClientMarkerKind.Overflow:
                    EmitTransportClientEvidence(
                        "PHASE184H_TRANSPORT_CLIENTS_OVERFLOW",
                        decision.ActiveClientCount,
                        decision.TotalAcceptedClients);
                    break;
            }
        }

        private void EmitTransportClientEvidence(
            string marker,
            int active,
            long accepted)
        {
            Emit(
                marker,
                "case=" + _selectedCase
                + " token=" + Phase184AcceptanceText.SafeMarker(_runToken)
                + " active=" + active
                + " accepted=" + accepted);
        }

    }
}
