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
    /// <summary>
    /// Hosts one token-scoped Phase186-H run. The ignored generated partial
    /// contributes the actual FoxRun declarations; this tracked half owns only
    /// identity validation, main-thread observation, UI, and terminal markers.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("Foxglove/Manual Acceptance/Phase186 ROS2 Bridge")]
    /// <remarks>Phase192 source sections: LifecycleAndProgress.cs, EvidenceAndGates.cs, ValidationAndUi.cs</remarks>
    public sealed partial class Phase186Ros2BridgeAcceptance : MonoBehaviour
    {
        public const string InterfaceDigest =
            "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd";

        private const int MaximumTextLength = 256;
        private const int MaximumSlowMainThreadDelayMs = 25;

        [Serializable]
        public struct GeneratedRunIdentity
        {
            public bool Present;
            public string RunId;
            public string CaseId;
            public string TokenHash;
            public string Head;
            public string InterfaceDigest;
            public string[] Topics;
            public string[] ContractKinds;
        }

        [Serializable]
        public struct GeneratedEvidence
        {
            public bool Generated;
            public bool SlowMainThread;
            public bool CanComplete;
            public long Received;
            public long Applied;
            public long Replaced;
            public long LocalMutations;
            public string LastStandardMessage;
            public string LastCustomMessage;
            public string LastTopic;
        }

        [Header("Scene-owned references")]
        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private Ros2BridgeTransportProvider _provider;

        [Header("Current transient run")]
        [SerializeField] private string _runId = string.Empty;
        [SerializeField] private string _caseId = string.Empty;
        [SerializeField] private string _tokenHash = string.Empty;
        [SerializeField] private string _featureHead = string.Empty;
        [SerializeField] private string[] _topics = Array.Empty<string>();
        [SerializeField] private bool _manual;
        [SerializeField] private string _outputRoot = string.Empty;
        [SerializeField] private string _externalGate = string.Empty;
        [SerializeField] private string _exerciseGate = string.Empty;
        [SerializeField, Range(0, MaximumSlowMainThreadDelayMs)]
        private int _slowMainThreadDelayMs = 12;

        [Header("Main-thread generated evidence")]
        [SerializeField] private GeneratedEvidence _generatedEvidence;
        [SerializeField] private string _status = "Run is not configured.";
        [SerializeField] private bool _contextValid;
        [SerializeField] private bool _ready;
        [SerializeField] private bool _externalGateReady;
        [SerializeField] private bool _terminal;

        [Header("Provider/session evidence")]
        [SerializeField] private bool _connected;
        [SerializeField] private string _publishState = "Stopped";
        [SerializeField] private string _subscribeState = "Stopped";
        [SerializeField] private ulong _sessionGeneration;
        [SerializeField] private int _queuedFrames;
        [SerializeField] private long _acceptedFrames;
        [SerializeField] private long _sentFrames;
        [SerializeField] private long _replacedFrames;
        [SerializeField] private long _droppedFrames;
        [SerializeField] private long _failedFrames;
        [SerializeField] private long _oversizeFrames;
        [SerializeField] private long _disposalFailures;
        [SerializeField] private string _lastDiagnostic = string.Empty;

        [Header("Reconnect and bounded-pressure observations")]
        [SerializeField] private long _connectTransitions;
        [SerializeField] private long _disconnectTransitions;
        [SerializeField] private long _sessionGenerationChanges;

        [Header("Lifecycle")]
        [SerializeField] private long _enableCount;
        [SerializeField] private long _disableCount;
        [SerializeField] private long _updateCount;

        private GeneratedRunIdentity _generatedIdentity;
        private bool _readyMarkerEmitted;
        private bool _publisherWarmupRequested;
        private bool _localMutationRequested;
        private bool _hasObservedConnection;
        private bool _lastConnected;
        private bool _hasObservedGeneration;
        private ulong _lastSessionGeneration;
        private float _nextExternalGateCheckAt;
        private bool _externalGateFailureLogged;
        private bool _exerciseGateReady;
        private bool _exerciseGateFailureLogged;
        private bool _fanoutFailureInjected;
        private long _fanoutSentFramesBeforeFailure;
        private string _progressFingerprint = string.Empty;
        private readonly Phase186ManualInteractionState _manualInteraction =
            new Phase186ManualInteractionState();
        private readonly Phase186FanoutFailureObservation
            _fanoutFailureObservation =
                new Phase186FanoutFailureObservation();

        public string RunId => _runId;
        public string CaseId => _caseId;
        public string TokenHash => _tokenHash;
        public string FeatureHead => _featureHead;
        public bool ContextValid => _contextValid;
        public bool Ready => _ready;
        public bool CanComplete =>
            _contextValid
            && _ready
            && _externalGateReady
            && _generatedEvidence.CanComplete
            && HasCaseSpecificEvidence();

        partial void Phase186Generated_Describe(
            ref GeneratedRunIdentity identity);

        partial void Phase186Generated_Initialize();

        partial void Phase186Generated_Tick(
            ref GeneratedEvidence evidence);

        partial void Phase186Generated_WarmPublishers(ref bool published);

        partial void Phase186Generated_PublishLocalMutation(
            ref GeneratedEvidence evidence,
            ref bool published);

        /// <summary>Assigns the only Manager and Provider allowed in the scene.</summary>
        public void ConfigureSceneReferences(
            FoxgloveManager manager,
            Ros2BridgeTransportProvider provider)
        {
            _manager = manager != null
                ? manager
                : throw new ArgumentNullException(nameof(manager));
            _provider = provider != null
                ? provider
                : throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// Installs current-run identity in memory before entering Play Mode.
        /// The builder never persists a token into the tracked scene.
        /// </summary>
        public void ConfigureForRun(
            string runId,
            string caseId,
            string tokenHash,
            string featureHead,
            string[] topics,
            bool manual,
            int slowMainThreadDelayMs,
            string outputRoot,
            string externalGate,
            string exerciseGate)
        {
            _manualInteraction.ResetForRun();
            _fanoutFailureObservation.ResetForRun();
            if (!IsRunId(runId))
                throw new ArgumentException("Run ID is malformed.", nameof(runId));
            if (string.IsNullOrWhiteSpace(caseId) || caseId.Length > 80)
                throw new ArgumentException("Case ID is malformed.", nameof(caseId));
            if (!IsLowerHex(tokenHash, 64))
                throw new ArgumentException("Token hash is malformed.", nameof(tokenHash));
            if (!IsLowerHex(featureHead, 40))
                throw new ArgumentException("Feature SHA is malformed.", nameof(featureHead));
            ValidateTopics(topics);
            var normalizedOutput = Path.GetFullPath(outputRoot ?? string.Empty);
            var normalizedGate = Path.GetFullPath(externalGate ?? string.Empty);
            var normalizedExerciseGate = Path.GetFullPath(
                exerciseGate ?? string.Empty);
            if (!Path.IsPathRooted(normalizedOutput)
                || !string.Equals(
                    normalizedGate,
                    Path.Combine(normalizedOutput, "unity-external-gate.json"),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "External gate is outside the current run output.",
                    nameof(externalGate));
            }
            if (!string.Equals(
                    normalizedExerciseGate,
                    Path.Combine(normalizedOutput, "unity-exercise-gate.json"),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Exercise gate is outside the current run output.",
                    nameof(exerciseGate));
            }

            _runId = runId;
            _caseId = caseId;
            _tokenHash = tokenHash;
            _featureHead = featureHead;
            _topics = (string[])topics.Clone();
            _manual = manual;
            _outputRoot = normalizedOutput;
            _externalGate = normalizedGate;
            _exerciseGate = normalizedExerciseGate;
            _slowMainThreadDelayMs = Mathf.Clamp(
                slowMainThreadDelayMs,
                0,
                MaximumSlowMainThreadDelayMs);
        }

    }
}
