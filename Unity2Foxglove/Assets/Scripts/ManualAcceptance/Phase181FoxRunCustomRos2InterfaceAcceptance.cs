// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase181
// Purpose: Bounded Unity evidence surface for custom FoxRun DTO ROS2 interop.

using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    /// <summary>
    /// Main-thread-only manual acceptance view for the Phase181 custom DTO
    /// contracts. Generated bindings own native message graphs; this component
    /// exposes only bounded managed copies and counters to the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    [AddComponentMenu("Foxglove/Manual Acceptance/Phase181 Custom ROS2 Interface")]
    public sealed partial class Phase181FoxRunCustomRos2InterfaceAcceptance : MonoBehaviour
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES
        private enum BidirectionalEvidenceStage
        {
            AwaitingInitialRemote,
            AwaitingSameOriginDrop,
            AwaitingNullEmptyRemote,
            Complete,
        }
#endif

        public const string NativePublishTopic = "/foxrun/phase181/custom/publish";
        public const string NativeSubscribeTopic = "/foxrun/phase181/custom/subscribe";
        public const string NativeBidirectionalTopic = "/foxrun/phase181/custom/bidirectional";

        private const int MaximumMarkerCount = 32;
        private const int MaximumInspectorTextLength = 256;
        private const int MaximumTokenLength = 96;
        private const float DefaultPlayerTimeoutSeconds = 120f;

        [Header("Manager Under Test")]
        [Tooltip("The Manager that owns the custom native ROS2 session.")]
        [SerializeField] private FoxgloveManager _manager;

        [FoxRun(
            NativePublishTopic,
            Mode = FoxRunFlow.Publish,
            PublishTransportIds = new[]
            {
                FoxRunRos2TransportProvider.IdValue
            })]
        [SerializeField] private Phase181State _nativePublish;

        [FoxRun(
            NativeSubscribeTopic,
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId =
                FoxRunRos2TransportProvider.IdValue)]
        [SerializeField] private Phase181State _inputPort;

        // The peer protocol explicitly owns the native inbound/output-loop evidence.
        [FoxRun(
            NativeBidirectionalTopic,
            Mode = FoxRunFlow.PublishAndSubscribe,
            SubscribeTransportId =
                FoxRunRos2TransportProvider.IdValue,
            PublishTransportIds = new[]
            {
                FoxRunRos2TransportProvider.IdValue
            })]
        [SerializeField] private Phase181State _nativeInputWebSocketOutput;

        // Keep the declarations source-generator-visible before an add-on is
        // selected. Generated native bindings remain conditionally compiled,
        // while this public read-only surface prevents bootstrap builds from
        // treating the locked sample values as unused fields.
        public Phase181State NativePublishValue => _nativePublish;
        public Phase181State InputPort => _inputPort;
        public Phase181State NativeInputWebSocketOutput => _nativeInputWebSocketOutput;

#if !(UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES)
        [Header("Custom Interface Availability")]
        [TextArea(2, 3)]
        [SerializeField] private string _unavailableReason =
            "Phase181 custom ROS2 typesupport is unavailable. Install the matching static interface package and exactly one matching distro add-on.";
#endif

        [Header("Safe Decoded Inspector Copies")]
        [SerializeField] private string _status = "Waiting for custom native ROS2 registration.";
        [SerializeField] private int _emittedMarkerCount;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES
        [SerializeField] private string _runtime = string.Empty;
        [SerializeField] private string _rmwImplementation = string.Empty;
        [SerializeField] private string _interfaceDigestPrefix = "120864853239";
        [SerializeField] private string _subscribeMessage = string.Empty;
        [SerializeField] private string _subscribeNestedLabel = string.Empty;
        [SerializeField] private int _subscribeCount;
        [SerializeField] private int _subscribeByteCount;
        [SerializeField] private int _subscribeSequenceCount;
        [SerializeField] private bool _subscribeHasNested;
        [SerializeField] private bool _subscribeOptionalCountPresent;
        [SerializeField] private bool _subscribeOptionalTextPresent;
        [SerializeField] private bool _subscribeMessageWasNull;
        [SerializeField] private bool _subscribeMessageWasEmpty;
        [SerializeField] private bool _subscribeSequenceWasNull;
        [SerializeField] private bool _subscribeSequenceWasEmpty;

        [Header("Bounded Binding Counters")]
        [SerializeField] private long _subscribeReceived;
        [SerializeField] private long _subscribeApplied;
        [SerializeField] private long _subscribeReplaced;
        [SerializeField] private long _bidirectionalApplied;
        [SerializeField] private long _localOriginDrops;
        [SerializeField] private long _sessionGeneration;
        [SerializeField] private string _lastErrorCode = string.Empty;
#endif

        [Header("Player Auto-Quit")]
        [Tooltip("Only Player command-line arguments activate auto-quit. Editor Play Mode never quits automatically.")]
        [SerializeField] private bool _playerAutoQuitRequested;
        [SerializeField] private string _playerToken = string.Empty;
        [SerializeField] private float _playerAutoQuitTimeoutSeconds = DefaultPlayerTimeoutSeconds;

        private readonly HashSet<string> _emittedMarkers = new HashSet<string>(StringComparer.Ordinal);
        private string _runToken = string.Empty;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES
        private long _observedSubscribeSession = long.MinValue;
        private long _observedSubscribeApplied = -1;
        private long _observedBidirectionalSession = long.MinValue;
        private long _observedBidirectionalApplied = -1;
        private long _observedOriginDrops = -1;
        private bool _runtimeReady;
        private bool _interfaceReady;
        private bool _publishSourceMarked;
        private bool _correlatedSubscribeApplied;
        private bool _correlatedBidirectionalInitialApplied;
        private bool _correlatedBidirectionalFinalApplied;
        private BidirectionalEvidenceStage _bidirectionalEvidenceStage;
#endif
        private bool _completed;
        private float _autoQuitDeadline;

        private void OnEnable()
        {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();

            _emittedMarkers.Clear();
            _emittedMarkerCount = 0;
            _observedSubscribeSession = long.MinValue;
            _observedSubscribeApplied = -1;
            _observedBidirectionalSession = long.MinValue;
            _observedBidirectionalApplied = -1;
            _observedOriginDrops = -1;
            _runtimeReady = false;
            _interfaceReady = false;
            _publishSourceMarked = false;
            _correlatedSubscribeApplied = false;
            _correlatedBidirectionalInitialApplied = false;
            _correlatedBidirectionalFinalApplied = false;
            _bidirectionalEvidenceStage = BidirectionalEvidenceStage.AwaitingInitialRemote;
#else
            _emittedMarkers.Clear();
#endif
            _completed = false;
            _playerAutoQuitRequested = HasCommandLineFlag("--phase181-custom-ros2-player-auto-quit");
            var requestedPlayerToken = (ReadCommandLineValue("--phase181-custom-ros2-token") ?? string.Empty).Trim();
            _runToken = IsSafeToken(requestedPlayerToken) ? requestedPlayerToken : GenerateRunToken();
            _playerToken = _runToken;
            _playerAutoQuitTimeoutSeconds = Mathf.Clamp(
                ReadPlayerTimeoutSeconds(),
                10f,
                600f);
            _autoQuitDeadline = Time.realtimeSinceStartup + _playerAutoQuitTimeoutSeconds;

            if (_playerAutoQuitRequested && !IsSafeToken(requestedPlayerToken))
            {
                _status = "Player auto-quit requires a bounded safe custom ROS2 token.";
                CompletePlayer(3, "invalid-token");
                return;
            }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES
            _nativePublish = CreateState("unity-publish", 181, false);
            _nativeInputWebSocketOutput = CreateState(
                "unity-bidirectional",
                RunTokenProbeCount(_runToken),
                true);
            _status = _manager == null
                ? "Assign a FoxgloveManager with custom native ROS2 enabled."
                : "Waiting for custom native ROS2 registration.";
#else
            _status = _unavailableReason;
            EmitMarker("PHASE181_CUSTOM_ROS2_UNAVAILABLE", "reason=custom-typesupport-unavailable");
            if (_playerAutoQuitRequested)
                CompletePlayer(4, "runtime-unavailable");
#endif
        }

        private void Update()
        {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES
            ObserveRuntimeAndInterface();
            ObserveSubscribe();
            ObserveBidirectional();
            EvaluatePlayerAutoQuit();
#endif
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES
        private void ObserveRuntimeAndInterface()
        {
            var snapshots = FoxRunRos2SubscriptionRuntimeDiagnostics.GetSnapshots();
            for (var i = 0; i < snapshots.Length; i++)
            {
                var snapshot = snapshots[i];
                if (!string.Equals(snapshot.Topic, NativeSubscribeTopic, StringComparison.Ordinal))
                    continue;

                _lastErrorCode = CopySafeText(snapshot.LastErrorCode, MaximumInspectorTextLength);
                if (!_runtimeReady
                    && IsReady(snapshot.State)
                    && !string.IsNullOrWhiteSpace(snapshot.RosDistro)
                    && !string.IsNullOrWhiteSpace(snapshot.RmwImplementation))
                {
                    _runtimeReady = true;
                    _runtime = CopySafeText(snapshot.RosDistro, MaximumInspectorTextLength);
                    _rmwImplementation = CopySafeText(snapshot.RmwImplementation, MaximumInspectorTextLength);
                    _status = "Custom native ROS2 runtime ready: " + _runtime + " / " + _rmwImplementation + ".";
                    EmitMarker(
                        "PHASE181_CUSTOM_ROS2_READY",
                        "runtime=" + SanitizeToken(_runtime) + " rmw=" + SanitizeToken(_rmwImplementation));
                }

                if (!_interfaceReady
                    && IsReady(snapshot.State)
                    && string.Equals(
                        snapshot.CanonicalRosType,
                        "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                        StringComparison.Ordinal))
                {
                    _interfaceReady = true;
                    EmitMarker(
                        "PHASE181_CUSTOM_INTERFACE_READY",
                        "interface=v1 digest=" + _interfaceDigestPrefix);
                }
            }

            if (_runtimeReady && _interfaceReady && !_publishSourceMarked)
            {
                _publishSourceMarked = true;
                EmitMarker(
                    "PHASE181_CUSTOM_ROS2_PUBLISHED",
                    "topic=" + NativePublishTopic + " source=armed");
            }
        }

        private void ObserveSubscribe()
        {
            if (!TryGetNewlyApplied(
                    NativeSubscribeTopic,
                    ref _observedSubscribeSession,
                    ref _observedSubscribeApplied,
                    out var snapshot)
            || _inputPort == null)
            {
                return;
            }

            CopySafeState(_inputPort);
            _subscribeReceived = snapshot.Received;
            _subscribeApplied = snapshot.Applied;
            _subscribeReplaced = snapshot.Replaced;
            _sessionGeneration = snapshot.SessionGeneration;
            if (!IsCorrelatedInitialPayload(_inputPort))
            {
                _status = "Ignored a custom native Subscribe DTO that did not match this acceptance run.";
                return;
            }

            _correlatedSubscribeApplied = true;
            _status = "Applied custom native ROS2 Subscribe DTO on Unity's main thread.";
            EmitMarker(
                "PHASE181_CUSTOM_ROS2_APPLIED",
                "topic=" + NativeSubscribeTopic
                + " session=" + snapshot.SessionGeneration.ToString(CultureInfo.InvariantCulture)
                + " applied=" + snapshot.Applied.ToString(CultureInfo.InvariantCulture));
        }

        private void ObserveBidirectional()
        {
            if (!FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(
                    this,
                    NativeBidirectionalTopic,
                    out var snapshot))
            {
                return;
            }

            _localOriginDrops = snapshot.SameOriginDrops;
            var sameOriginDropObserved = _observedOriginDrops >= 0
                                         && snapshot.SameOriginDrops > _observedOriginDrops;

            if (TryGetNewlyApplied(
                    NativeBidirectionalTopic,
                    ref _observedBidirectionalSession,
                    ref _observedBidirectionalApplied,
                    out snapshot)
                && _nativeInputWebSocketOutput != null)
            {
                _bidirectionalApplied = snapshot.Applied;
                _sessionGeneration = snapshot.SessionGeneration;
                if (_bidirectionalEvidenceStage == BidirectionalEvidenceStage.AwaitingInitialRemote
                    && IsCorrelatedInitialPayload(_nativeInputWebSocketOutput))
                {
                    _correlatedBidirectionalInitialApplied = true;
                    _bidirectionalEvidenceStage = BidirectionalEvidenceStage.AwaitingSameOriginDrop;
                    EmitMarker(
                        "PHASE181_CUSTOM_ROS2_APPLIED",
                        "topic=" + NativeBidirectionalTopic
                        + " session=" + snapshot.SessionGeneration.ToString(CultureInfo.InvariantCulture)
                        + " applied=" + snapshot.Applied.ToString(CultureInfo.InvariantCulture));
                }
                else if (_bidirectionalEvidenceStage == BidirectionalEvidenceStage.AwaitingNullEmptyRemote
                         && IsNullEmptyRemotePayload(_nativeInputWebSocketOutput))
                {
                    _correlatedBidirectionalFinalApplied = true;
                    _bidirectionalEvidenceStage = BidirectionalEvidenceStage.Complete;
                    EmitMarker(
                        "PHASE181_CUSTOM_ROS2_APPLIED",
                        "topic=" + NativeBidirectionalTopic
                        + " session=" + snapshot.SessionGeneration.ToString(CultureInfo.InvariantCulture)
                        + " applied=" + snapshot.Applied.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (sameOriginDropObserved
                && _bidirectionalEvidenceStage == BidirectionalEvidenceStage.AwaitingSameOriginDrop)
            {
                _bidirectionalEvidenceStage = BidirectionalEvidenceStage.AwaitingNullEmptyRemote;
                EmitMarker(
                    "PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED",
                    "topic=" + NativeBidirectionalTopic
                    + " drops=" + snapshot.SameOriginDrops.ToString(CultureInfo.InvariantCulture));
            }
            _observedOriginDrops = snapshot.SameOriginDrops;
        }

        private bool TryGetNewlyApplied(
            string topic,
            ref long observedSession,
            ref long observedApplied,
            out FoxRunRos2SubscriptionAcceptanceSnapshot snapshot)
        {
            if (!FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(this, topic, out snapshot))
                return false;

            if (snapshot.SessionGeneration != observedSession)
            {
                observedSession = snapshot.SessionGeneration;
                observedApplied = -1;
            }

            if (snapshot.Applied <= observedApplied)
                return false;

            observedApplied = snapshot.Applied;
            return true;
        }

        private void EvaluatePlayerAutoQuit()
        {
            if (!_playerAutoQuitRequested || _completed || Application.isEditor)
                return;

            if (_runtimeReady
                && _interfaceReady
                && _correlatedSubscribeApplied
                && _correlatedBidirectionalInitialApplied
                && _correlatedBidirectionalFinalApplied)
            {
                _status = "Custom native ROS2 peer proof completed.";
                CompletePlayer(0, "success");
                return;
            }

            if (Time.realtimeSinceStartup < _autoQuitDeadline)
                return;

            _status = "Timed out waiting for custom native ROS2 peer proof.";
            CompletePlayer(2, "timeout");
        }

        private bool IsCorrelatedInitialPayload(Phase181State state)
        {
            if (state == null
                || state.Count != 181
                || state.Kind != Phase181StateKind.Active
                || !string.Equals(state.Message, _runToken, StringComparison.Ordinal)
                || state.Bytes == null
                || state.Bytes.Length != 3
                || state.Bytes[0] != 0x18
                || state.Bytes[1] != 0x01
                || state.Bytes[2] != 0x81
                || state.Values == null
                || state.Values.Count != 3
                || state.Values[0] != 181L
                || state.Values[1] != 182L
                || state.Values[2] != 183L
                || state.Nested == null
                || !state.Nested.Enabled
                || !string.Equals(state.Nested.Label, _runToken, StringComparison.Ordinal)
                || state.OptionalCount != 181
                || !string.Equals(state.OptionalText, _runToken, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private bool IsNullEmptyRemotePayload(Phase181State state)
            => state != null
               && state.Count == RunTokenProbeCount(_runToken)
               && state.Kind == Phase181StateKind.Active
               && string.Equals(state.Message, string.Empty, StringComparison.Ordinal)
               && state.Bytes != null
               && state.Bytes.Length == 0
               && state.Values != null
               && state.Values.Count == 0
               && state.Nested == null
               && !state.OptionalCount.HasValue
               && state.OptionalText == null;

        private void CopySafeState(Phase181State source)
        {
            _subscribeCount = source.Count;
            _subscribeMessageWasNull = source.Message == null;
            _subscribeMessageWasEmpty = string.Equals(source.Message, string.Empty, StringComparison.Ordinal);
            _subscribeMessage = CopySafeText(source.Message, MaximumInspectorTextLength);
            _subscribeByteCount = Math.Min(source.Bytes == null ? 0 : source.Bytes.Length, MaximumInspectorTextLength);
            _subscribeSequenceWasNull = source.Values == null;
            _subscribeSequenceWasEmpty = source.Values != null && source.Values.Count == 0;
            _subscribeSequenceCount = Math.Min(source.Values == null ? 0 : source.Values.Count, MaximumInspectorTextLength);
            _subscribeHasNested = source.Nested != null;
            _subscribeNestedLabel = CopySafeText(source.Nested == null ? null : source.Nested.Label, MaximumInspectorTextLength);
            _subscribeOptionalCountPresent = source.OptionalCount.HasValue;
            _subscribeOptionalTextPresent = source.OptionalText != null;
        }

        private static Phase181State CreateState(string label, int count, bool emptyValues)
            => new Phase181State
            {
                Count = count,
                Kind = Phase181StateKind.Active,
                Message = emptyValues ? string.Empty : label,
                Bytes = emptyValues ? Array.Empty<byte>() : new byte[] { 0x18, 0x1, 0x81 },
                Values = emptyValues ? new List<long>() : new List<long> { count, count + 1L, count + 2L },
                Nested = emptyValues ? null : new Phase181NestedState { Enabled = true, Label = label },
                OptionalCount = emptyValues ? null : count,
                OptionalText = emptyValues ? null : label,
            };

        private static int RunTokenProbeCount(string token)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(token ?? string.Empty);
            var digest = sha.ComputeHash(bytes);
            var value =
                ((digest[0] & 0x7f) << 24)
                | (digest[1] << 16)
                | (digest[2] << 8)
                | digest[3];
            return value == 0 ? 1 : value;
        }

        private static bool IsReady(FoxRunRos2SubscriptionBindingState state)
            => state == FoxRunRos2SubscriptionBindingState.Ready
               || state == FoxRunRos2SubscriptionBindingState.Receiving;
#endif

        private float ReadPlayerTimeoutSeconds()
        {
            var text = ReadCommandLineValue("--phase181-custom-ros2-timeout-seconds");
            if (string.IsNullOrWhiteSpace(text)
                || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || float.IsNaN(parsed)
                || float.IsInfinity(parsed))
            {
                return _playerAutoQuitTimeoutSeconds;
            }
            return parsed;
        }

        private void CompletePlayer(int exitCode, string outcome)
        {
            if (_completed)
                return;

            _completed = true;
            EmitMarker(
                exitCode == 0 ? "PHASE181_CUSTOM_ROS2_PASS" : "PHASE181_CUSTOM_ROS2_FAIL",
                "outcome=" + SanitizeToken(outcome));
            if (!Application.isEditor)
                Application.Quit(exitCode);
        }

        private void EmitMarker(string marker, string fields)
        {
            if (_emittedMarkerCount >= MaximumMarkerCount || string.IsNullOrEmpty(marker))
                return;

            var boundedFields = CopySafeText(fields, MaximumInspectorTextLength);
            var markerFields = boundedFields + " token=" + SanitizeToken(_runToken);
            var key = marker + "|" + markerFields;
            if (!_emittedMarkers.Add(key))
                return;

            _emittedMarkerCount++;
            Debug.Log(marker + " " + markerFields, this);
        }

        private static bool HasCommandLineFlag(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string ReadCommandLineValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                    return args[i + 1];
            }
            return null;
        }

        private static bool IsSafeToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumTokenLength)
                return false;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (!((character >= 'a' && character <= 'z')
                      || (character >= 'A' && character <= 'Z')
                      || (character >= '0' && character <= '9')
                      || character == '-' || character == '_' || character == '.'))
                {
                    return false;
                }
            }
            return true;
        }

        private static string GenerateRunToken()
            => "phase181-" + Guid.NewGuid().ToString("N");

        private static string CopySafeText(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        private static string SanitizeToken(string value)
            => IsSafeToken(value) ? value : "none";
    }
}
