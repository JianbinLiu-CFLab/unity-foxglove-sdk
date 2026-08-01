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
        private bool _fanoutFailedProviderObserved;
        private long _fanoutSentFramesBeforeFailure;

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

        private void OnEnable()
        {
            _enableCount = SaturatingIncrement(_enableCount);
            _generatedEvidence = default;
            _generatedIdentity = default;
            _ready = false;
            _terminal = false;
            _readyMarkerEmitted = false;
            _externalGateReady = false;
            _externalGateFailureLogged = false;
            _exerciseGateReady = false;
            _exerciseGateFailureLogged = false;
            _fanoutFailureInjected = false;
            _fanoutFailedProviderObserved = false;
            _fanoutSentFramesBeforeFailure = 0;
            _nextExternalGateCheckAt = 0f;
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
                    "PHASE186_ACCEPTANCE_CONTEXT_FAIL reason="
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

            Phase186Generated_Tick(ref _generatedEvidence);
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

            if (!_manual
                && _generatedEvidence.Applied > 0
                && _generatedEvidence.LocalMutations == 0
                && !_localMutationRequested)
            {
                PublishLocalMutation();
            }
            InjectFanoutFailureIfRequested();
            if (!_manual && CanComplete)
                CompleteAutomaticAcceptance();
        }

        /// <summary>Publishes one causally distinct local B after remote A.</summary>
        public void PublishLocalMutation()
        {
            if (!_contextValid || _terminal)
                return;
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
            if (!_manual || !CanComplete || _terminal)
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
                    || !_fanoutFailedProviderObserved
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
                    if (_fanoutFailureInjected
                        && !string.Equals(
                            status.Publish.State.ToString(),
                            "Ready",
                            StringComparison.Ordinal))
                    {
                        _fanoutFailedProviderObserved = true;
                    }
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

        private bool TryValidateContext(out string reason)
        {
            if (_manager == null || _provider == null)
            {
                reason = "scene Manager or Bridge Provider is absent";
                return false;
            }
            if (!_generatedIdentity.Present)
            {
                reason = "token-specific generated partial is absent";
                return false;
            }
            if (!string.Equals(_runId, _generatedIdentity.RunId, StringComparison.Ordinal)
                || !string.Equals(_caseId, _generatedIdentity.CaseId, StringComparison.Ordinal)
                || !string.Equals(_tokenHash, _generatedIdentity.TokenHash, StringComparison.Ordinal)
                || !string.Equals(_featureHead, _generatedIdentity.Head, StringComparison.Ordinal)
                || !string.Equals(
                    InterfaceDigest,
                    _generatedIdentity.InterfaceDigest,
                    StringComparison.Ordinal))
            {
                reason = "serialized run identity differs from generated binding";
                return false;
            }
            if (!SameTopics(_topics, _generatedIdentity.Topics))
            {
                reason = "serialized topics differ from generated binding";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private string FormatIdentityMarker(string prefix, string verdict)
            => prefix
               + " run=" + _runId
               + " case=" + _caseId
               + " tokenHash=" + _tokenHash
               + " head=" + _featureHead
               + " verdict=" + verdict;

        private void OnGUI()
        {
            const float left = 12f;
            const float top = 44f;
            const float width = 820f;
            GUI.Box(new Rect(left, top, width, 250f), "Phase186 ROS-free Bridge acceptance");
            GUI.Label(new Rect(left + 12f, top + 26f, width - 24f, 22f),
                "Run: " + Phase186Bound(_runId) + " / " + Phase186Bound(_caseId));
            GUI.Label(new Rect(left + 12f, top + 48f, width - 24f, 22f),
                "Status: " + Phase186Bound(_status));
            GUI.Label(new Rect(left + 12f, top + 70f, width - 24f, 22f),
                "Provider: connected=" + _connected
                + " publish=" + _publishState
                + " subscribe=" + _subscribeState
                + " generation=" + _sessionGeneration);
            GUI.Label(new Rect(left + 12f, top + 92f, width - 24f, 22f),
                "Inbound estimate: received=" + _generatedEvidence.Received
                + " applied=" + _generatedEvidence.Applied
                + " replaced=" + _generatedEvidence.Replaced);
            GUI.Label(new Rect(left + 12f, top + 114f, width - 24f, 22f),
                "Outbound: accepted=" + _acceptedFrames
                + " sent=" + _sentFrames
                + " failed=" + _failedFrames
                + " queued=" + _queuedFrames);
            GUI.Label(new Rect(left + 12f, top + 136f, width - 24f, 22f),
                "Latest standard: "
                + Phase186Bound(_generatedEvidence.LastStandardMessage));
            GUI.Label(new Rect(left + 12f, top + 158f, width - 24f, 22f),
                "Latest Phase181: "
                + Phase186Bound(_generatedEvidence.LastCustomMessage));

            GUI.enabled = _contextValid && !_terminal;
            if (GUI.Button(
                    new Rect(left + 12f, top + 190f, 280f, 32f),
                    "Publish distinct local mutation B"))
            {
                PublishLocalMutation();
            }
            GUI.enabled = _manual && CanComplete && !_terminal;
            if (GUI.Button(
                    new Rect(left + 310f, top + 190f, 280f, 32f),
                    "Complete current manual run"))
            {
                CompleteManualAcceptance();
            }
            GUI.enabled = true;
        }

        internal static void Phase186RecordSequence(
            long sequence,
            ref long previous,
            ref GeneratedEvidence evidence)
        {
            if (sequence < 0 || sequence == previous)
                return;
            if (previous >= 0 && sequence > previous)
            {
                var delta = sequence - previous;
                evidence.Received = SaturatingAdd(evidence.Received, delta);
                evidence.Replaced = SaturatingAdd(
                    evidence.Replaced,
                    Math.Max(0L, delta - 1L));
            }
            else
            {
                evidence.Received = SaturatingIncrement(evidence.Received);
            }
            evidence.Applied = SaturatingIncrement(evidence.Applied);
            previous = sequence;
        }

        internal static bool Phase186TryReadSequence(
            string message,
            out long sequence)
        {
            sequence = 0;
            if (string.IsNullOrEmpty(message))
                return false;
            var first = message.IndexOf(':');
            var second = first < 0 ? -1 : message.IndexOf(':', first + 1);
            var third = second < 0 ? -1 : message.IndexOf(':', second + 1);
            return first > 0
                   && second > first + 1
                   && third > second + 1
                   && long.TryParse(
                       message.Substring(second + 1, third - second - 1),
                       NumberStyles.None,
                       CultureInfo.InvariantCulture,
                       out sequence);
        }

        internal static string Phase186Bound(string value)
        {
            value ??= string.Empty;
            return value.Length <= MaximumTextLength
                ? value
                : value.Substring(0, MaximumTextLength);
        }

        private static void ValidateTopics(string[] topics)
        {
            if (topics == null || topics.Length == 0 || topics.Length > 3)
                throw new ArgumentException("Topic set is absent or unbounded.", nameof(topics));
            for (var i = 0; i < topics.Length; i++)
            {
                var topic = topics[i];
                if (string.IsNullOrWhiteSpace(topic)
                    || !topic.StartsWith("/foxrun/phase186/p186h_", StringComparison.Ordinal)
                    || topic.IndexOf("phase181", StringComparison.OrdinalIgnoreCase) >= 0
                    || topic.IndexOf("phase184", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new ArgumentException("Topic is outside Phase186 run authority.", nameof(topics));
                }
                for (var previous = 0; previous < i; previous++)
                {
                    if (string.Equals(topic, topics[previous], StringComparison.Ordinal))
                        throw new ArgumentException("Topic set contains a duplicate.", nameof(topics));
                }
            }
        }

        private static bool SameTopics(string[] left, string[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static bool IsRunId(string value)
            => !string.IsNullOrEmpty(value)
               && value.Length >= 12
               && value.Length <= 80
               && value.StartsWith("phase186h-", StringComparison.Ordinal);

        private static bool IsLowerHex(string value, int length)
        {
            if (value == null || value.Length != length)
                return false;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static long SaturatingIncrement(long value)
            => value == long.MaxValue ? long.MaxValue : value + 1L;

        private static long SaturatingAdd(long value, long addition)
        {
            if (addition <= 0)
                return value;
            return value > long.MaxValue - addition
                ? long.MaxValue
                : value + addition;
        }
    }
}
