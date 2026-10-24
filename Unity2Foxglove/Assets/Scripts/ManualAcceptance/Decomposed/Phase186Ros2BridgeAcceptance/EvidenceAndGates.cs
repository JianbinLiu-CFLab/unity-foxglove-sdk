// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase186
// Purpose: Controlled main-thread evidence surface for the ROS-free Bridge.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;

namespace Unity2Foxglove.ManualAcceptance
{
    public sealed partial class Phase186Ros2BridgeAcceptance
    {
        private bool HasCaseSpecificEvidence()
        {
            var kinds = _generatedIdentity.ContractKinds ?? Array.Empty<string>();
            var hasPublish = kinds.Any(kind =>
                kind.EndsWith("publish", StringComparison.Ordinal)
                || kind.EndsWith("duplex", StringComparison.Ordinal));
            if (hasPublish && _sentFrames <= 0)
                return false;
            if (string.Equals(
                    _caseId,
                    "slow-main-thread-640hz",
                    StringComparison.Ordinal)
                && _generatedEvidence.Replaced <= 0)
            {
                return false;
            }
            if ((string.Equals(
                     _caseId,
                     "reconnect-degraded-recovery",
                     StringComparison.Ordinal)
                 || string.Equals(_caseId, "lifecycle", StringComparison.Ordinal))
                && (_disconnectTransitions <= 0 || _connectTransitions < 2))
            {
                return false;
            }
            if (string.Equals(
                    _caseId,
                    "fanout-fairness-health",
                    StringComparison.Ordinal)
                && (!_fanoutFailureInjected
                    || !_fanoutFailureObservation
                        .FailureObservedAfterInjection
                    || _sentFrames <= _fanoutSentFramesBeforeFailure))
            {
                return false;
            }
            return true;
        }

        private void InjectFanoutFailureIfRequested()
        {
            if (_fanoutFailureInjected
                || !_exerciseGateReady
                || !string.Equals(
                    _caseId,
                    "fanout-fairness-health",
                    StringComparison.Ordinal))
            {
                return;
            }

            var type = Type.GetType(
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2TransportProvider, "
                + "Unity2Foxglove.Ros2ForUnity.Native",
                throwOnError: false);
            var component = type == null || _manager == null
                ? null
                : _manager.GetComponent(type) as Behaviour;
            if (component == null || !component.enabled)
            {
                _status = "Fail closed: fanout R2FU Provider is absent before injection.";
                return;
            }
            if (!_fanoutFailureObservation.ReadyBeforeInjection)
            {
                _status =
                    "Waiting for fanout R2FU Provider readiness before injection.";
                return;
            }
            _fanoutSentFramesBeforeFailure = _sentFrames;
            component.enabled = false;
            _fanoutFailureInjected = true;
            PublishLocalMutation();
            Debug.Log(
                "PHASE186_FANOUT_FAILURE_INJECTED run=" + _runId
                + " tokenHash=" + _tokenHash
                + " provider=unity2foxglove.r2fu");
        }

        private void RefreshExerciseGate()
        {
            if (_exerciseGateReady
                || !string.Equals(
                    _caseId,
                    "fanout-fairness-health",
                    StringComparison.Ordinal)
                || string.IsNullOrEmpty(_exerciseGate)
                || !File.Exists(_exerciseGate))
            {
                return;
            }
            try
            {
                ValidateGate(_exerciseGate, "exercise");
                _exerciseGateReady = true;
                Debug.Log(
                    "PHASE186_EXERCISE_GATE_READY run=" + _runId
                    + " case=" + _caseId
                    + " tokenHash=" + _tokenHash);
            }
            catch (Exception exception)
            {
                if (_exerciseGateFailureLogged)
                    return;
                _exerciseGateFailureLogged = true;
                Debug.LogError(
                    "PHASE186_EXERCISE_GATE_FAIL run=" + _runId
                    + " reason=" + Phase186Bound(exception.GetType().Name));
            }
        }

        private bool ProviderDirectionsReady()
        {
            var kinds = _generatedIdentity.ContractKinds ?? Array.Empty<string>();
            var needsPublish = kinds.Any(kind =>
                kind.EndsWith("publish", StringComparison.Ordinal)
                || kind.EndsWith("duplex", StringComparison.Ordinal));
            var needsSubscribe = kinds.Any(kind =>
                kind.EndsWith("subscribe", StringComparison.Ordinal)
                || kind.EndsWith("duplex", StringComparison.Ordinal));
            return _connected
                   && (!needsPublish
                       || string.Equals(
                           _publishState,
                           "Ready",
                           StringComparison.Ordinal))
                   && (!needsSubscribe
                        || string.Equals(
                            _subscribeState,
                            "Ready",
                            StringComparison.Ordinal));
        }

        private Phase186ManualInteraction EvaluateManualInteraction()
            => _manualInteraction.Evaluate(
                _manual,
                _contextValid,
                _terminal,
                string.Equals(
                    _publishState,
                    "Ready",
                    StringComparison.Ordinal),
                string.Equals(
                    _subscribeState,
                    "Ready",
                    StringComparison.Ordinal),
                CanComplete);

        private void RefreshExternalGate()
        {
            if (_externalGateReady || Time.realtimeSinceStartup < _nextExternalGateCheckAt)
                return;
            _nextExternalGateCheckAt = Time.realtimeSinceStartup + 0.25f;
            if (string.IsNullOrEmpty(_externalGate) || !File.Exists(_externalGate))
                return;
            try
            {
                var json = JObject.Parse(File.ReadAllText(_externalGate));
                var expectedKeys = new[]
                {
                    "schemaVersion", "runId", "caseId", "tokenHash", "head", "ready"
                };
                var actualKeys = json.Properties()
                    .Select(property => property.Name)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (!actualKeys.SequenceEqual(
                        expectedKeys.OrderBy(value => value, StringComparer.Ordinal),
                        StringComparer.Ordinal)
                    || (int?)json["schemaVersion"] != 1
                    || (bool?)json["ready"] != true
                    || !string.Equals((string)json["runId"], _runId, StringComparison.Ordinal)
                    || !string.Equals((string)json["caseId"], _caseId, StringComparison.Ordinal)
                    || !string.Equals((string)json["tokenHash"], _tokenHash, StringComparison.Ordinal)
                    || !string.Equals((string)json["head"], _featureHead, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "External live evidence gate differs from current run authority.");
                }
                _externalGateReady = true;
                _status = "External live actors passed; waiting for final Unity evidence.";
                Debug.Log(
                    "PHASE186_EXTERNAL_GATE_READY run=" + _runId
                    + " case=" + _caseId
                    + " tokenHash=" + _tokenHash
                    + " head=" + _featureHead);
            }
            catch (Exception exception)
            {
                if (_externalGateFailureLogged)
                    return;
                _externalGateFailureLogged = true;
                _status = "Fail closed: external live evidence gate is invalid.";
                Debug.LogError(
                    "PHASE186_EXTERNAL_GATE_FAIL run=" + _runId
                    + " reason=" + Phase186Bound(exception.GetType().Name));
            }
        }

        private void ValidateGate(string path, string stage)
        {
            var json = JObject.Parse(File.ReadAllText(path));
            var expectedKeys = new[]
            {
                "schemaVersion", "runId", "caseId", "tokenHash", "head", "ready"
            };
            var actualKeys = json.Properties()
                .Select(property => property.Name)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (!actualKeys.SequenceEqual(
                    expectedKeys.OrderBy(value => value, StringComparer.Ordinal),
                    StringComparer.Ordinal)
                || (int?)json["schemaVersion"] != 1
                || (bool?)json["ready"] != true
                || !string.Equals((string)json["runId"], _runId, StringComparison.Ordinal)
                || !string.Equals((string)json["caseId"], _caseId, StringComparison.Ordinal)
                || !string.Equals((string)json["tokenHash"], _tokenHash, StringComparison.Ordinal)
                || !string.Equals((string)json["head"], _featureHead, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Unity " + stage + " gate differs from current run authority.");
            }
        }

        private void CaptureProviderEvidence()
        {
            if (_provider != null)
            {
                var stats = _provider.GetStatsSnapshot();
                if (!_hasObservedConnection && stats.Connected)
                {
                    _connectTransitions = SaturatingIncrement(
                        _connectTransitions);
                }
                else if (_hasObservedConnection
                         && stats.Connected != _lastConnected)
                {
                    if (stats.Connected)
                    {
                        _connectTransitions = SaturatingIncrement(
                            _connectTransitions);
                    }
                    else
                    {
                        _disconnectTransitions = SaturatingIncrement(
                            _disconnectTransitions);
                    }
                }
                _hasObservedConnection = true;
                _lastConnected = stats.Connected;
                _connected = stats.Connected;
                _queuedFrames = stats.QueuedFrames;
                _acceptedFrames = stats.AcceptedFrames;
                _sentFrames = stats.SentFrames;
                _replacedFrames = stats.ReplacedFrames;
                _droppedFrames = stats.DroppedFrames;
                _failedFrames = stats.FailedFrames;
                _oversizeFrames = stats.OversizeFrames;
                _disposalFailures = stats.DisposalFailures;
                _lastDiagnostic = Phase186Bound(stats.LastError);
            }

            var statuses = _manager?.CaptureFoxRunTransportStatuses();
            if (statuses == null)
                return;
            for (var i = 0; i < statuses.Count; i++)
            {
                var status = statuses[i];
                if (string.Equals(
                        status.ProviderId.Value,
                        "unity2foxglove.r2fu",
                        StringComparison.Ordinal))
                {
                    _fanoutFailureObservation.Observe(
                        _fanoutFailureInjected,
                        status.Publish.State == FoxRunTransportObservedState.Ready);
                    continue;
                }
                if (!string.Equals(
                        status.ProviderId.Value,
                        Ros2BridgeTransportProvider.ProviderId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (_hasObservedGeneration
                    && status.Generation != _lastSessionGeneration)
                {
                    _sessionGenerationChanges = SaturatingIncrement(
                        _sessionGenerationChanges);
                }
                _hasObservedGeneration = true;
                _lastSessionGeneration = status.Generation;
                _sessionGeneration = status.Generation;
                _publishState = status.Publish.State.ToString();
                _subscribeState = status.Subscribe.State.ToString();
                if (status.Diagnostics.Count > 0)
                {
                    var diagnostic = status.Diagnostics[status.Diagnostics.Count - 1];
                    _lastDiagnostic = Phase186Bound(
                        diagnostic.Code + ":" + diagnostic.Message);
                }
            }
        }

    }
}
