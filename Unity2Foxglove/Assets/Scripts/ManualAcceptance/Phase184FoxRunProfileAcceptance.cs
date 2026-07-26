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
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Debug = UnityEngine.Debug;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    /// <summary>
    /// Selects exactly one Phase184 route component before ordinary Unity
    /// lifecycle methods register generated FoxRun contracts. Keeping the
    /// other route GameObjects inactive prevents the Foxglove-only case from
    /// acquiring Native or Bridge demand.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32000)]
    [AddComponentMenu("Foxglove/Manual Acceptance/Phase184 Profile Acceptance")]
    public sealed class Phase184FoxRunProfileAcceptance : MonoBehaviour
    {
        public const string FoxgloveProfileCase = "foxglove-profile";
        public const string MultiTargetCase = "multi-target";
        public const string DegradedTargetCase = "degraded-target";
        public const string QosContractCase = "qos-contract";
        public const string StreamCase = "stream-640hz";

        private const int MaximumStatusCharacters = 512;
        private const int MaximumConfigBytes = 1024 * 1024;
        private const string BatchConfigArgument = "-phase184RunConfig";
        private const string ManualPointerRelativePath =
            "build/phase184/acceptance/manual-active.json";

        [Header("Manager Under Test")]
        [SerializeField] private FoxgloveManager _manager;

        [Header("Case-isolated Routes")]
        [SerializeField] private Phase184FoxgloveProfileRoute _foxgloveProfile;
        [SerializeField] private Phase184MultiTargetRoute _multiTarget;
        [SerializeField] private Phase184DegradedTargetRoute _degradedTarget;
        [SerializeField] private Phase184QosContractRoute _qosContract;
        [SerializeField] private Phase184StreamRoute _stream;

        [Header("Read-only Run Evidence")]
        [SerializeField] private string _selectedCase = string.Empty;
        [SerializeField] private string _status = "Waiting for a Phase184-G run context.";
        [SerializeField] private string _requestedPublishProfile = string.Empty;
        [SerializeField] private string _requestedSubscribeProfile = string.Empty;
        [SerializeField] private string _effectivePublishProfile = string.Empty;
        [SerializeField] private string _effectiveSubscribeProfile = string.Empty;
        [SerializeField] private string _tokenDigestPrefix = string.Empty;
        [SerializeField] private bool _contextValidated;

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
            _status = "Validated " + context.CaseId + "; activating its isolated route.";
            CaptureProfiles();
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

            _effectivePublishProfile =
                _manager.ActiveFoxRunPublishTargets + " / "
                + _manager.ActiveFoxRunPublishEncoding;
            _effectiveSubscribeProfile =
                _manager.ActiveFoxRunSubscriptionSource + " / "
                + _manager.ActiveFoxRunSubscriptionEncoding;
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
            _manager.EnableFoxRunInbound = true;
            _manager.DefaultFoxRunPublishEncoding = FoxRunEncoding.Protobuf;
            _manager.DefaultFoxRunSubscriptionEncoding = FoxRunEncoding.Protobuf;
            _manager.DefaultFoxRunPublishTargets = FoxRunEndpoint.Foxglove;
            _manager.DefaultFoxRunSubscriptionSource = FoxRunEndpoint.Foxglove;

            if (caseId == MultiTargetCase
                || caseId == QosContractCase
                || caseId == StreamCase)
            {
                _manager.DefaultFoxRunSubscriptionSource = FoxRunEndpoint.Ros2Native;
            }
        }

        private void CaptureProfiles()
        {
            _requestedPublishProfile =
                _manager.DefaultFoxRunPublishTargets + " / "
                + _manager.DefaultFoxRunPublishEncoding;
            _requestedSubscribeProfile =
                _manager.DefaultFoxRunSubscriptionSource + " / "
                + _manager.DefaultFoxRunSubscriptionEncoding;
            _effectivePublishProfile = _requestedPublishProfile;
            _effectiveSubscribeProfile = _requestedSubscribeProfile;
        }

        private void FailContext(string reason)
        {
            _contextValidated = false;
            _status = Phase184AcceptanceText.Bound(reason, MaximumStatusCharacters);
            Debug.LogError(
                "PHASE184G_CONTEXT_FAIL reason="
                + Phase184AcceptanceText.SafeMarker(_status),
                this);
        }

        private static bool TryLoadContext(
            out Phase184AcceptanceRunContext context,
            out string error)
        {
            context = default;
            error = string.Empty;
            try
            {
                var configPath = ReadCommandLineValue(BatchConfigArgument);
                var isBatchContext = !string.IsNullOrWhiteSpace(configPath);
                var pointerToken = string.Empty;
                if (string.IsNullOrWhiteSpace(configPath))
                {
                    var repository = Directory.GetParent(
                        Directory.GetParent(Application.dataPath)?.FullName
                        ?? string.Empty)?.FullName;
                    if (string.IsNullOrWhiteSpace(repository))
                    {
                        error = "Could not resolve the repository root for manual-active.json.";
                        return false;
                    }

                    var pointerPath = Path.Combine(
                        repository,
                        ManualPointerRelativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!TryResolveManualPointer(
                            pointerPath,
                            repository,
                            out configPath,
                            out pointerToken,
                            out error))
                        return false;
                }

                var fullPath = Path.GetFullPath(configPath);
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumConfigBytes)
                {
                    error = "The Phase184 run config is missing, empty, or oversized.";
                    return false;
                }

                var json = JObject.Parse(File.ReadAllText(fullPath));
                var caseId = (string)json["case"] ?? string.Empty;
                var token = (string)json["token"] ?? string.Empty;
                var executionMode = (string)json["executionMode"] ?? string.Empty;
                var windows = json["observationWindows"] as JObject;
                var negativeSeconds = (int?)windows?["negativeSeconds"] ?? 3;
                if (!IsKnownCase(caseId)
                    || !Phase184AcceptanceText.IsSafeToken(token)
                    || (!string.IsNullOrEmpty(pointerToken)
                        && !string.Equals(token, pointerToken, StringComparison.Ordinal))
                    || (isBatchContext && executionMode != "batch")
                    || (!isBatchContext && executionMode != "manual")
                    || negativeSeconds < 1
                    || negativeSeconds > 30)
                {
                    error =
                        "The Phase184 run config has an invalid case, token, mode, or window.";
                    return false;
                }

                context = new Phase184AcceptanceRunContext(
                    caseId,
                    token,
                    Math.Max(1, negativeSeconds));
                return true;
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is Newtonsoft.Json.JsonException
                || exception is ArgumentException
                || exception is InvalidOperationException)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static bool TryResolveManualPointer(
            string pointerPath,
            string repository,
            out string configPath,
            out string pointerToken,
            out string error)
        {
            configPath = string.Empty;
            pointerToken = string.Empty;
            error = string.Empty;
            var info = new FileInfo(pointerPath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumConfigBytes)
            {
                error = "No valid Phase184 manual-active pointer is present.";
                return false;
            }

            var pointer = JObject.Parse(File.ReadAllText(pointerPath));
            configPath = (string)pointer["runConfig"] ?? string.Empty;
            pointerToken = (string)pointer["token"] ?? string.Empty;
            var helperPid = (int?)pointer["helperPid"] ?? 0;
            var helperCreated = (double?)pointer["helperCreationUnixSeconds"] ?? 0d;
            var expires = (string)pointer["expiresUtc"] ?? string.Empty;
            if (!Phase184AcceptanceText.IsSafeToken(pointerToken)
                || helperPid <= 0
                || helperCreated <= 0d
                || !DateTimeOffset.TryParse(
                    expires,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var expiry)
                || expiry <= DateTimeOffset.UtcNow)
            {
                error = "The Phase184 manual-active pointer is stale or malformed.";
                return false;
            }

            try
            {
                using var helper = Process.GetProcessById(helperPid);
                var actualCreated =
                    new DateTimeOffset(helper.StartTime.ToUniversalTime())
                        .ToUnixTimeMilliseconds() / 1000d;
                if (Math.Abs(actualCreated - helperCreated) > 2d)
                {
                    error = "The Phase184 helper identity no longer matches the pointer.";
                    return false;
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidOperationException
                || exception is System.ComponentModel.Win32Exception)
            {
                error = "The Phase184 helper process is no longer alive.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(configPath))
                return false;
            var fullConfigPath = Path.GetFullPath(configPath);
            var acceptanceRoot = Path.GetFullPath(
                Path.Combine(repository, "build", "phase184", "acceptance"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullConfigPath.StartsWith(
                    acceptanceRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The Phase184 run config escaped the owned acceptance root.";
                return false;
            }
            configPath = fullConfigPath;
            return true;
        }

        private static bool IsKnownCase(string value)
            => value == FoxgloveProfileCase
               || value == MultiTargetCase
               || value == DegradedTargetCase
               || value == QosContractCase
               || value == StreamCase;

        private static string ReadCommandLineValue(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                    return arguments[index + 1];
            }
            return string.Empty;
        }

        private void Emit(string marker, string fields)
            => Debug.Log(marker + " " + fields, this);
    }

    internal readonly struct Phase184AcceptanceRunContext
    {
        internal Phase184AcceptanceRunContext(
            string caseId,
            string token,
            int negativeSeconds)
        {
            CaseId = caseId;
            Token = token;
            NegativeSeconds = negativeSeconds;
            TokenDigestPrefix = Phase184AcceptanceText.TokenDigestPrefix(token);
        }

        internal string CaseId { get; }
        internal string Token { get; }
        internal int NegativeSeconds { get; }
        internal string TokenDigestPrefix { get; }
    }

    public abstract class Phase184AcceptanceRoute : MonoBehaviour
    {
        private const int MaximumMarkerCount = 64;

        [Header("Read-only Route Evidence")]
        [SerializeField] private string _caseId = string.Empty;
        [SerializeField] private string _status = "Inactive";
        [SerializeField] private int _emittedMarkers;
        [SerializeField] private bool _terminal;
        [SerializeField] private bool _passed;

        private readonly HashSet<string> _markerKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private Phase184AcceptanceRunContext _context;
        private bool _armed;

        protected string RunToken => _context.Token;
        protected int NegativeSeconds => _context.NegativeSeconds;
        protected bool IsArmed => _armed;
        protected bool IsTerminal => _terminal;
        protected abstract string RouteCaseId { get; }

        internal void Arm(Phase184AcceptanceRunContext context)
        {
            _context = context;
            _caseId = context.CaseId;
            _status = "Armed";
            _terminal = false;
            _passed = false;
            _emittedMarkers = 0;
            _markerKeys.Clear();
            _armed = true;
        }

        protected void Ready(string fields)
        {
            _status = "Ready";
            Emit("PHASE184G_ROUTE_READY", fields);
        }

        protected void Pass(string fields)
        {
            if (_terminal)
                return;
            _terminal = true;
            _passed = true;
            _status = "PASS";
            Emit("PHASE184G_CASE_PASS", fields);
        }

        protected void Fail(string reason)
        {
            if (_terminal)
                return;
            _terminal = true;
            _passed = false;
            _status = Phase184AcceptanceText.Bound(reason, 512);
            Emit(
                "PHASE184G_CASE_FAIL",
                "reason=" + Phase184AcceptanceText.SafeMarker(_status));
        }

        protected void Emit(string marker, string fields)
        {
            if (!_armed || _emittedMarkers >= MaximumMarkerCount)
                return;

            var bounded = Phase184AcceptanceText.Bound(fields, 512);
            var line = marker
                       + " case=" + RouteCaseId
                       + " token=" + Phase184AcceptanceText.SafeMarker(RunToken)
                       + (string.IsNullOrEmpty(bounded) ? string.Empty : " " + bounded);
            if (!_markerKeys.Add(line))
                return;
            _emittedMarkers++;
            Debug.Log(line, this);
        }

        protected static Phase181State State(string token, string stage, int count)
        {
            var label = token + "-" + stage;
            return new Phase181State
            {
                Count = count,
                Kind = Phase181StateKind.Active,
                Message = label,
                Bytes = new byte[] { 0x18, 0x04, (byte)(count & 0xff) },
                Values = new List<long> { count, count + 1L, count + 2L },
                Nested = new Phase181NestedState { Enabled = true, Label = label },
                OptionalCount = count,
                OptionalText = label,
            };
        }

        protected bool IsState(Phase181State value, string stage)
            => value != null
               && value.Kind == Phase181StateKind.Active
               && string.Equals(
                   value.Message,
                   RunToken + "-" + stage,
                   StringComparison.Ordinal);

        protected bool TryGetTargetStatus(
            string topic,
            out FoxRunPublishDispatchResult result)
        {
            result = default;
            var source = this as IFoxgloveLogSource;
            if (source == null)
                return false;
            var hub = FindFirstObjectByType<FoxgloveLogHub>();
            if (hub == null)
                return false;

            for (var index = 0; index < source.FoxgloveLog_TopicCount; index++)
            {
                if (!string.Equals(
                        source.FoxgloveLog_GetTopic(index).Topic,
                        topic,
                        StringComparison.Ordinal))
                {
                    continue;
                }
                return hub.TryGetPublishTargetStatus(source, index, out result);
            }
            return false;
        }
    }

    [DisallowMultipleComponent]
    public sealed partial class Phase184FoxgloveProfileRoute : Phase184AcceptanceRoute
    {
        public const string InheritedTopic = "/foxrun/phase184/profile/default";
        public const string JsonTopic = "/foxrun/phase184/profile/json";

        [FoxRun(InheritedTopic, Mode = FoxRunFlow.PublishAndSubscribe)]
        [SerializeField] private Phase181State _inheritedFoxglove;

        [FoxRun(
            JsonTopic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            Source = FoxRunEndpoint.Foxglove,
            Targets = FoxRunEndpoint.Foxglove,
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
            _disabledWindowPreservedValue = false;
            _sameValueAppliedAfterRecovery = false;
            _laterLocalMutation = false;
            _inheritedFoxglove = State(RunToken, "profile-outbound", 18401);
            _explicitJson = State(RunToken, "json-outbound", 18402);
            Ready("topics=2 encodings=protobuf,json");
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

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
    }

    [DisallowMultipleComponent]
    public sealed partial class Phase184MultiTargetRoute : Phase184AcceptanceRoute
    {
        public const string Topic = "/foxrun/phase184/multi/state";

        [FoxRun(
            Topic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            Source = FoxRunEndpoint.Ros2Native,
            Targets = FoxRunEndpoint.Foxglove
                      | FoxRunEndpoint.Ros2Native
                      | FoxRunEndpoint.Ros2Bridge,
            Encoding = FoxRunEncoding.Protobuf,
            QoS = FoxRunQosProfile.Default,
            Policy = FoxRunPolicy.Change)]
        [SerializeField] private Phase181State _multiTarget;

        [Header("Read-only Fanout/Origin Evidence")]
        [SerializeField] private string _targetStatus = "Waiting";
        [SerializeField] private long _sameOriginDrops;
        [SerializeField] private bool _remoteApplied;
        [SerializeField] private bool _laterLocalMutation;

        private float _remoteObservedAt;
        private bool _initialArmed;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.MultiTargetCase;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }

            _multiTarget = State(RunToken, "multi-warmup", 18410);
            Ready("topic=" + Topic + " targets=foxglove,native,bridge");
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

            if (TryGetTargetStatus(Topic, out var status))
            {
                _targetStatus = status.Status.ToString();
                if (!_initialArmed
                    && status.Status == FoxRunPublishTargetStatus.Ready
                    && status.SucceededTargets
                       == (FoxRunEndpoint.Foxglove
                           | FoxRunEndpoint.Ros2Native
                           | FoxRunEndpoint.Ros2Bridge))
                {
                    _initialArmed = true;
                    _multiTarget = State(RunToken, "multi-local-1", 18411);
                    Emit("PHASE184G_MULTI_LOCAL_ARMED", "stage=1");
                }
            }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
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

            if (_laterLocalMutation
                && TryGetTargetStatus(Topic, out var finalStatus)
                && finalStatus.Status == FoxRunPublishTargetStatus.Ready)
            {
                Pass(
                    "remoteApplied=True sameOriginDrops="
                    + _sameOriginDrops.ToString(CultureInfo.InvariantCulture)
                    + " laterLocal=True");
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed partial class Phase184DegradedTargetRoute : Phase184AcceptanceRoute
    {
        public const string Topic = "/foxrun/phase184/degraded/state";

        [FoxRun(
            Topic,
            Mode = FoxRunFlow.Publish,
            Targets = FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge,
            Encoding = FoxRunEncoding.Protobuf,
            Policy = FoxRunPolicy.Change)]
        [SerializeField] private Phase181State _degradedTarget;

        [Header("Read-only Degraded Evidence")]
        [SerializeField] private string _targetStatus = "Waiting";
        [SerializeField] private string _succeededTargets = string.Empty;
        [SerializeField] private string _failedTargets = string.Empty;
        [SerializeField] private float _degradedObservedAt = -1f;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.DegradedTargetCase;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }
            _degradedTarget = State(RunToken, "degraded-local", 18420);
            Ready("topic=" + Topic + " bridge=deliberately-absent");
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;
            if (!TryGetTargetStatus(Topic, out var status))
                return;

            _targetStatus = status.Status.ToString();
            _succeededTargets = status.SucceededTargets.ToString();
            _failedTargets = status.FailedTargets.ToString();
            if (status.Status == FoxRunPublishTargetStatus.Degraded
                && status.SucceededTargets == FoxRunEndpoint.Foxglove
                && status.FailedTargets == FoxRunEndpoint.Ros2Bridge)
            {
                if (_degradedObservedAt < 0f)
                {
                    _degradedObservedAt = Time.realtimeSinceStartup;
                    Emit(
                        "PHASE184G_DEGRADED_WINDOW_STARTED",
                        "healthy=foxglove failed=bridge");
                    return;
                }
                if (Time.realtimeSinceStartup - _degradedObservedAt < NegativeSeconds)
                    return;
                Pass("healthy=foxglove failed=bridge status=Degraded");
                return;
            }
            _degradedObservedAt = -1f;
        }
    }

    [DisallowMultipleComponent]
    public sealed partial class Phase184QosContractRoute : Phase184AcceptanceRoute
    {
        public const string SystemDefaultTopic =
            "/foxrun/phase184/qos/system-default";
        public const string KeepAllTopic =
            "/foxrun/phase184/qos/keep-all";
        public const string KeepLastDepthTopic =
            "/foxrun/phase184/qos/keep-last-depth";

        [FoxRun(
            SystemDefaultTopic,
            Mode = FoxRunFlow.Publish,
            Targets = FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
            QoS = FoxRunQosProfile.SystemDefault)]
        [SerializeField] private Phase181State _qosSystemDefault;

        [FoxRun(
            KeepAllTopic,
            Mode = FoxRunFlow.Publish,
            Targets = FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
            QoS = FoxRunQosProfile.Default,
            History = FoxRunQosHistory.KeepAll)]
        [SerializeField] private Phase181State _qosKeepAll;

        [FoxRun(
            KeepLastDepthTopic,
            Mode = FoxRunFlow.Publish,
            Targets = FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
            QoS = FoxRunQosProfile.Default,
            Reliability = FoxRunQosReliability.BestEffort,
            Durability = FoxRunQosDurability.TransientLocal,
            History = FoxRunQosHistory.KeepLast,
            Depth = 7)]
        [SerializeField] private Phase181State _qosKeepLastDepth;

        [Header("Read-only QoS Evidence")]
        [SerializeField] private int _readyContracts;

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
            Ready("topics=3 targets=native,bridge");
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

            _readyContracts = 0;
            CountReady(SystemDefaultTopic);
            CountReady(KeepAllTopic);
            CountReady(KeepLastDepthTopic);
            if (_readyContracts == 3)
                Pass("readyContracts=3 requestedPolicies=system-default,keep-all,keep-last-depth-7");
        }

        private void CountReady(string topic)
        {
            if (TryGetTargetStatus(topic, out var status)
                && status.Status == FoxRunPublishTargetStatus.Ready
                && status.SucceededTargets
                   == (FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge))
            {
                _readyContracts++;
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed partial class Phase184StreamRoute : Phase184AcceptanceRoute
    {
        public const string StreamTopic = "/foxrun/phase184/stream/state";
        public const string OriginTopic = "/foxrun/phase184/zenoh/origin";
        private const int ExpectedOfferedSamples = 1280;
        private const float InitialDrainDelaySeconds = 0.5f;

        [FoxRun(
            StreamTopic,
            Mode = FoxRunFlow.Subscribe,
            Source = FoxRunEndpoint.Ros2Native,
            QoS = FoxRunQosProfile.SensorData)]
        private FoxRunStream<Phase181State> _streamInputPort =
            new FoxRunStream<Phase181State>(
                new FoxRunStreamOptions(
                    32,
                    1000d,
                    16,
                    FoxRunStreamOverflowPolicy.DropOldest));

        [FoxRun(
            OriginTopic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            Source = FoxRunEndpoint.Ros2Native,
            Targets = FoxRunEndpoint.Ros2Native,
            QoS = FoxRunQosProfile.SensorData,
            Policy = FoxRunPolicy.Change)]
        [SerializeField] private Phase181State _zenohOrigin;

        [Header("Read-only Stream Evidence")]
        [SerializeField] private long _offered;
        [SerializeField] private long _accepted;
        [SerializeField] private long _drained;
        [SerializeField] private long _replaced;
        [SerializeField] private long _rateDropped;
        [SerializeField] private long _maximumQueueDepth;
        [SerializeField] private long _disposalFailures;
        [SerializeField] private int _lastRetainedSequence = -1;
        [SerializeField] private bool _retainedOrdered = true;
        [SerializeField] private bool _ownershipBalanced;
        [SerializeField] private bool _remoteOriginApplied;
        [SerializeField] private long _sameOriginDrops;
        [SerializeField] private bool _laterLocalOrigin;

        private float _enabledAt;

        protected override string RouteCaseId =>
            Phase184FoxRunProfileAcceptance.StreamCase;

        private void OnEnable()
        {
            if (!IsArmed)
            {
                enabled = false;
                return;
            }
            _enabledAt = Time.realtimeSinceStartup;
            _zenohOrigin = State(RunToken, "origin-warmup", 18440);
            Ready("streamCapacity=32 maxInputHz=1000 maxBatch=16 overflow=DropOldest");
        }

        private void OnDisable()
        {
            _streamInputPort?.Clear();
        }

        private void Update()
        {
            if (!IsArmed || IsTerminal)
                return;

            if (Time.realtimeSinceStartup - _enabledAt >= InitialDrainDelaySeconds)
                _streamInputPort.Drain(ObserveRetainedSample);

            var stats = _streamInputPort.Stats;
            _offered = stats.Received;
            _accepted = stats.Admitted;
            _drained = stats.Drained;
            _replaced = stats.DroppedOldest + stats.DroppedNewest;
            _rateDropped = stats.RateDropped;
            _maximumQueueDepth = stats.HighWater;
            _disposalFailures = stats.DisposalFailures;
            _ownershipBalanced =
                stats.Admitted
                == stats.Drained
                   + stats.DroppedOldest
                   + stats.DroppedNewest
                   + stats.Cleared
                   + _streamInputPort.Count;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
            if (FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(
                    this,
                    OriginTopic,
                    out var originSnapshot))
            {
                _sameOriginDrops = originSnapshot.SameOriginDrops;
            }
#endif

            if (!_remoteOriginApplied && IsState(_zenohOrigin, "origin-remote"))
            {
                _remoteOriginApplied = true;
                Emit("PHASE184G_STREAM_REMOTE_ORIGIN_APPLIED", "stage=remote");
            }
            if (_remoteOriginApplied && !_laterLocalOrigin && _sameOriginDrops > 0)
            {
                _laterLocalOrigin = true;
                _zenohOrigin = State(RunToken, "origin-local", 18442);
                Emit("PHASE184G_STREAM_LOCAL_ORIGIN_MUTATED", "stage=local");
            }

            if (_offered >= ExpectedOfferedSamples
                && _streamInputPort.Count == 0
                && _replaced > 0
                && _retainedOrdered
                && _ownershipBalanced
                && _remoteOriginApplied
                && _sameOriginDrops > 0
                && _laterLocalOrigin)
            {
                Pass(
                    "offered=" + _offered.ToString(CultureInfo.InvariantCulture)
                    + " accepted=" + _accepted.ToString(CultureInfo.InvariantCulture)
                    + " drained=" + _drained.ToString(CultureInfo.InvariantCulture)
                    + " replaced=" + _replaced.ToString(CultureInfo.InvariantCulture)
                    + " rateDropped=" + _rateDropped.ToString(CultureInfo.InvariantCulture)
                    + " highWater=" + _maximumQueueDepth.ToString(CultureInfo.InvariantCulture)
                    + " disposalFailures=" + _disposalFailures.ToString(CultureInfo.InvariantCulture)
                    + " lastSequence=" + _lastRetainedSequence.ToString(CultureInfo.InvariantCulture)
                    + " ordered=True ownershipBalanced=True");
            }
        }

        private void ObserveRetainedSample(Phase181State sample)
        {
            if (sample == null)
            {
                _retainedOrdered = false;
                return;
            }
            if (_lastRetainedSequence >= 0 && sample.Count <= _lastRetainedSequence)
                _retainedOrdered = false;
            _lastRetainedSequence = sample.Count;
        }
    }

    internal static class Phase184AcceptanceText
    {
        internal static bool IsSafeToken(string token)
        {
            if (string.IsNullOrEmpty(token)
                || token.Length < 18
                || token.Length > 70
                || !token.StartsWith("p184g_", StringComparison.Ordinal))
            {
                return false;
            }
            for (var index = 6; index < token.Length; index++)
            {
                var character = token[index];
                if (!(character >= 'a' && character <= 'z')
                    && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9'))
                {
                    return false;
                }
            }
            return true;
        }

        internal static string SafeMarker(string value)
        {
            value = Bound(value, 512);
            var characters = value.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                var character = characters[index];
                if (character == ' '
                    || character == '\r'
                    || character == '\n'
                    || character == '\t'
                    || character == '=')
                {
                    characters[index] = '_';
                }
            }
            return new string(characters);
        }

        internal static string Bound(string value, int maximum)
        {
            value ??= string.Empty;
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        internal static string TokenDigestPrefix(string token)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty);
            var digest = sha.ComputeHash(bytes);
            var text = BitConverter.ToString(digest).Replace("-", string.Empty);
            return text.Substring(0, 12).ToLowerInvariant();
        }
    }
}
