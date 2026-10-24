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
        private void OnEnable()
        {
            _enableCount = SaturatingIncrement(_enableCount);
            _generatedEvidence = default;
            _generatedIdentity = default;
            _manualInteraction.ResetForRun();
            _ready = false;
            _terminal = false;
            _readyMarkerEmitted = false;
            _externalGateReady = false;
            _externalGateFailureLogged = false;
            _exerciseGateReady = false;
            _exerciseGateFailureLogged = false;
            _fanoutFailureInjected = false;
            _fanoutFailureObservation.ResetForRun();
            _fanoutSentFramesBeforeFailure = 0;
            _progressFingerprint = string.Empty;
            _nextExternalGateCheckAt = 0f;
            _publisherWarmupRequested = false;
            _localMutationRequested = false;
            _hasObservedConnection = false;
            _lastConnected = false;
            _hasObservedGeneration = false;
            _lastSessionGeneration = 0;

            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();
            if (_provider == null && _manager != null)
                _provider = _manager.GetComponent<Ros2BridgeTransportProvider>();

            Phase186Generated_Describe(ref _generatedIdentity);
            Phase186Generated_Initialize();
            _contextValid = TryValidateContext(out var reason);
            _status = _contextValid
                ? "Waiting for current-run Bridge readiness and live data."
                : "Fail closed: " + reason;
            if (!_contextValid)
                Debug.LogError(
                    "PHASE186_ACCEPTANCE_CONTEXT_FAIL run="
                    + _generatedIdentity.RunId
                    + " case=" + _generatedIdentity.CaseId
                    + " tokenHash=" + _generatedIdentity.TokenHash
                    + " head=" + _generatedIdentity.Head
                    + " reason="
                    + Phase186Bound(reason));
        }

        private void OnDisable()
        {
            _disableCount = SaturatingIncrement(_disableCount);
        }

        private void Update()
        {
            _updateCount = SaturatingIncrement(_updateCount);
            if (!_contextValid || _terminal)
                return;

            var appliedBeforeTick = _generatedEvidence.Applied;
            Phase186Generated_Tick(ref _generatedEvidence);
            _manualInteraction.ObserveGeneratedTick(
                _manual,
                appliedBeforeTick,
                _generatedEvidence.Applied,
                HasObservedExternalInput());
            CaptureProviderEvidence();
            RefreshExerciseGate();
            RefreshExternalGate();
            if (_generatedEvidence.SlowMainThread
                && _slowMainThreadDelayMs > 0)
            {
                Thread.Sleep(_slowMainThreadDelayMs);
            }

            _ready = _generatedEvidence.Generated
                     && ProviderDirectionsReady();
            if (_ready && !_readyMarkerEmitted)
            {
                _readyMarkerEmitted = true;
                _status = "Current-run Bridge is ready.";
                Debug.Log(FormatIdentityMarker("PHASE186_ACCEPTANCE_READY", "READY"));
            }

            if (_ready && !_publisherWarmupRequested)
                WarmPublishersForDiscovery();

#if UNITY_EDITOR
            DriveBatchManualAcceptance();
            if (_terminal)
                return;
#endif
            if (!_manual
                && _generatedEvidence.Applied > 0
                && HasObservedExternalInput()
                && _generatedEvidence.LocalMutations == 0
                && !_localMutationRequested)
            {
                PublishLocalMutation();
            }
            InjectFanoutFailureIfRequested();
            EmitProgressIfChanged();
            if (!_manual && CanComplete)
                CompleteAutomaticAcceptance();
        }

#if UNITY_EDITOR
        private void DriveBatchManualAcceptance()
        {
            if (!_manual || !Application.isBatchMode)
                return;
            var interaction = EvaluateManualInteraction();
            if (interaction.CanRequestLocalB)
            {
                PublishLocalMutation();
                return;
            }
            if (interaction.CanRequestComplete)
                CompleteManualAcceptance();
        }
#endif

        private void WarmPublishersForDiscovery()
        {
            _publisherWarmupRequested = true;
            var published = false;
            Phase186Generated_WarmPublishers(ref published);
            Debug.Log(
                "PHASE186_PUBLISHER_WARMUP run=" + _runId
                + " case=" + _caseId
                + " tokenHash=" + _tokenHash
                + " published=" + (published ? "true" : "false"));
        }

        private bool HasObservedExternalInput()
            => Phase186ManualInteractionState.IsExpectedExternalInput(
                   _generatedEvidence.LastStandardMessage,
                   _tokenHash)
               || Phase186ManualInteractionState.IsExpectedExternalInput(
                   _generatedEvidence.LastCustomMessage,
                   _tokenHash);

        private void EmitProgressIfChanged()
        {
            var caseSpecific = HasCaseSpecificEvidence();
            var fingerprint = _manual
                ? ManualProgressFingerprint(caseSpecific)
                : ProgressFingerprint(caseSpecific);
            if (string.Equals(
                    fingerprint,
                    _progressFingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }
            _progressFingerprint = fingerprint;
            if (_manual)
                EmitManualProgress(caseSpecific);
            else
                EmitAutomaticProgress(caseSpecific);
        }

        private void EmitAutomaticProgress(bool caseSpecific)
        {
            Debug.Log(
                "PHASE186_ACCEPTANCE_PROGRESS run=" + _runId
                + " case=" + _caseId
                + " ready=" + (_ready ? "true" : "false")
                + " external=" + (_externalGateReady ? "true" : "false")
                + " generated=" + (_generatedEvidence.CanComplete ? "true" : "false")
                + " caseSpecific=" + (caseSpecific ? "true" : "false")
                + " connected=" + (_connected ? "true" : "false")
                + " publish=" + _publishState
                + " subscribe=" + _subscribeState
                + " received=" + _generatedEvidence.Received.ToString(
                    CultureInfo.InvariantCulture)
                + " applied=" + _generatedEvidence.Applied.ToString(
                    CultureInfo.InvariantCulture)
                + " diagnostic=" + Phase186Bound(_lastDiagnostic));
        }

        private string ProgressFingerprint(bool caseSpecific)
            => (_ready ? "1" : "0")
               + (_externalGateReady ? "1" : "0")
               + (_generatedEvidence.CanComplete ? "1" : "0")
               + (caseSpecific ? "1" : "0")
               + (_connected ? "1" : "0")
               + ":" + _publishState
               + ":" + _subscribeState
               + ":" + Phase186Bound(_lastDiagnostic);

        private string ManualProgressFingerprint(bool caseSpecific)
            => ProgressFingerprint(caseSpecific)
               + (_manualInteraction.ExternalAObserved ? "1" : "0");

        private void EmitManualProgress(bool caseSpecific)
        {
            Debug.Log(
                "PHASE186_ACCEPTANCE_PROGRESS run=" + _runId
                    + " case=" + _caseId
                    + " tokenHash=" + _tokenHash
                    + " head=" + _featureHead
                    + " ready=" + (_ready ? "true" : "false")
                    + " external=" + (_externalGateReady ? "true" : "false")
                    + " generated=" + (_generatedEvidence.CanComplete ? "true" : "false")
                    + " caseSpecific=" + (caseSpecific ? "true" : "false")
                    + " connected=" + (_connected ? "true" : "false")
                    + " publish=" + _publishState
                    + " subscribe=" + _subscribeState
                    + " externalA=" + (_manualInteraction.ExternalAObserved ? "true" : "false")
                    + " received=" + _generatedEvidence.Received.ToString(
                        CultureInfo.InvariantCulture)
                    + " applied=" + _generatedEvidence.Applied.ToString(
                        CultureInfo.InvariantCulture)
                    + " diagnostic=" + Phase186Bound(_lastDiagnostic));
        }

        /// <summary>Publishes one causally distinct local B after remote A.</summary>
        public void PublishLocalMutation()
        {
            if (_manual)
            {
                var interaction = EvaluateManualInteraction();
                if (!_manualInteraction.TryRequestLocalB(interaction))
                    return;
            }
            else
            {
                if (!_contextValid || _terminal)
                    return;
            }
            _localMutationRequested = true;
            var published = false;
            Phase186Generated_PublishLocalMutation(
                ref _generatedEvidence,
                ref published);
            _status = published
                ? "Published a distinct local B through generated FoxRun binding."
                : "Generated run has no publish-capable contract.";
            Debug.Log(
                "PHASE186_LOCAL_MUTATION run=" + _runId
                + " case=" + _caseId
                + " tokenHash=" + _tokenHash
                + " published=" + (published ? "true" : "false"));
        }

        /// <summary>Emits the exact blocking manual marker after live evidence.</summary>
        public void CompleteManualAcceptance()
        {
            var interaction = EvaluateManualInteraction();
            if (!_manualInteraction.TryRequestComplete(interaction))
                return;
            _terminal = true;
            _status = "Manual acceptance completed for the current run.";
            Debug.Log(
                "PHASE186_MANUAL_COMPLETE case=" + _caseId
                + " run=" + _runId
                + " tokenHash=" + _tokenHash
                + " head=" + _featureHead
                + " verdict=PASS");
            EmitEvidenceMarker();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        private void CompleteAutomaticAcceptance()
        {
            _terminal = true;
            _status = "Automatic Unity evidence completed for the current run.";
            Debug.Log(FormatIdentityMarker("PHASE186_ACCEPTANCE_PASS", "PASS"));
            EmitEvidenceMarker();
        }

        private void EmitEvidenceMarker()
        {
            Debug.Log(
                "PHASE186_ACCEPTANCE_EVIDENCE run=" + _runId
                + " case=" + _caseId
                + " tokenHash=" + _tokenHash
                + " generation=" + _sessionGeneration.ToString(CultureInfo.InvariantCulture)
                + " received=" + _generatedEvidence.Received.ToString(CultureInfo.InvariantCulture)
                + " applied=" + _generatedEvidence.Applied.ToString(CultureInfo.InvariantCulture)
                + " replaced=" + _generatedEvidence.Replaced.ToString(CultureInfo.InvariantCulture)
                + " localMutations=" + _generatedEvidence.LocalMutations.ToString(CultureInfo.InvariantCulture)
                + " accepted=" + _acceptedFrames.ToString(CultureInfo.InvariantCulture)
                + " sent=" + _sentFrames.ToString(CultureInfo.InvariantCulture)
                + " failed=" + _failedFrames.ToString(CultureInfo.InvariantCulture)
                + " connectTransitions=" + _connectTransitions.ToString(CultureInfo.InvariantCulture)
                + " disconnectTransitions=" + _disconnectTransitions.ToString(CultureInfo.InvariantCulture)
                + " generationChanges=" + _sessionGenerationChanges.ToString(CultureInfo.InvariantCulture)
                + " dropped=" + _droppedFrames.ToString(CultureInfo.InvariantCulture)
                + " providerReplaced=" + _replacedFrames.ToString(CultureInfo.InvariantCulture));
        }

    }
}
