// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase179
// Purpose: Four-type Linux-to-Unity native ROS2 subscription acceptance surface.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

/// <summary>
/// A bounded, main-thread-only acceptance surface for the Phase179 native
/// FoxRun subscription contract. The generated binding owns the typed message
/// fields below; this component only copies the small managed values needed for
/// Inspector and log evidence. It never retains a callback-owned ROS2 object.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase179 ROS2 Native Subscribe")]
public sealed partial class Phase179FoxRunRos2NativeSubscribeAcceptance : MonoBehaviour
{
    public const string StringTopic = "/foxrun/phase179/string";
    public const string TwistTopic = "/foxrun/phase179/twist";
    public const string JoyTopic = "/foxrun/phase179/joy";
    public const string ImuTopic = "/foxrun/phase179/imu";

    private const int MaximumMarkerCount = 48;
    private const int MaximumTokenLength = 96;
    private const int MaximumMarkerTextLength = 256;
    private const int MaximumInspectorStringLength = 256;
    private const int MaximumInspectorArrayLength = 8;
    private const int MaximumMarkerArrayLength = 3;
    private const float AutoQuitTimeoutSeconds = 60f;
    private const double EqualityTolerance = 0.000001d;

    // Marker output is an acceptance aid, not a general message logger. Keep
    // obvious credential-bearing strings out of both Inspector copies and logs.
    private static readonly string[] SensitiveMarkerFragments =
    {
        "password",
        "secret",
        "credential",
        "authorization",
        "bearer",
        "api_key",
        "apikey",
        "access_key",
        "accesskey",
        "private_key",
        "privatekey",
        "zenohrouterpath",
        "zenohconfig"
    };

    [Header("Manager Under Test")]
    [Tooltip("The Manager that owns the native FoxRun subscription session.")]
    [SerializeField] private FoxgloveManager _manager;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    // These fields are the generated binding targets. Their lifetime is owned by
    // the generated host; Inspector evidence below is copied from them on Unity's
    // main thread and no callback-owned reference is stored separately.
    [FoxRun(
        StringTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
        Ros2Qos = FoxRunRos2QosPreset.Reliable)]
    private std_msgs.msg.String _inputString;

    [FoxRun(
        TwistTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
        Ros2Qos = FoxRunRos2QosPreset.Reliable)]
    private geometry_msgs.msg.Twist _inputTwist;

    [FoxRun(
        JoyTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
        Ros2Qos = FoxRunRos2QosPreset.SensorData)]
    private sensor_msgs.msg.Joy _inputJoy;

    [FoxRun(
        ImuTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
        Ros2Qos = FoxRunRos2QosPreset.SensorData)]
    private sensor_msgs.msg.Imu _inputImu;
#else
    [Header("Native Runtime Availability")]
    [TextArea(2, 3)]
    [SerializeField] private string _nativeRuntimeAvailability =
        "ROS2 native subscription support is unavailable. Install exactly one ROS2 For Unity runtime package and enable UNITY2FOXGLOVE_ROS2_FOR_UNITY.";
#endif

    [Header("Safe Inspector Copies")]
    [SerializeField] private string _status = "Waiting for native ROS2 subscription registration.";
    [SerializeField] private string _stringData = string.Empty;
    [SerializeField] private double _twistLinearX;
    [SerializeField] private double _twistLinearY;
    [SerializeField] private double _twistAngularZ;
    [SerializeField] private string _joyFrameId = string.Empty;
    [SerializeField] private float[] _joyAxes = Array.Empty<float>();
    [SerializeField] private int[] _joyButtons = Array.Empty<int>();
    [SerializeField] private string _imuFrameId = string.Empty;
    [SerializeField] private double _imuOrientationX;
    [SerializeField] private double _imuOrientationY;
    [SerializeField] private double _imuOrientationZ;
    [SerializeField] private double _imuOrientationW;
    [SerializeField] private double _imuAngularVelocityX;
    [SerializeField] private double _imuAngularVelocityY;
    [SerializeField] private double _imuAngularVelocityZ;
    [SerializeField] private double _imuLinearAccelerationX;
    [SerializeField] private double _imuLinearAccelerationY;
    [SerializeField] private double _imuLinearAccelerationZ;

    [Header("Bounded Application Counters")]
    [SerializeField] private int _stringValuesApplied;
    [SerializeField] private int _twistValuesApplied;
    [SerializeField] private int _joyValuesApplied;
    [SerializeField] private int _imuValuesApplied;
    [SerializeField] private long _lastReceived;
    [SerializeField] private long _lastApplied;
    [SerializeField] private long _lastReplaced;
    [SerializeField] private long _lastSessionGeneration;
    [SerializeField] private int _emittedMarkerCount;
    [TextArea(2, 4)]
    [SerializeField] private string _borrowedLifetimeNote =
        "The framework borrows ROS2 callback messages. The generated binding deep-copies them before main-thread application; this component exposes only bounded managed copies and counters.";

    /// <summary>Gets the fixed Inspector note that describes borrowed-message ownership.</summary>
    public string BorrowedLifetimeNote => _borrowedLifetimeNote;

    [Header("Player Auto-Quit")]
    [Tooltip("Only Player command-line arguments activate auto-quit. Editor Play Mode never quits automatically.")]
    [SerializeField] private bool _playerAutoQuitRequested;
    [SerializeField] private string _playerToken = string.Empty;
    [SerializeField] private float _playerAutoQuitTimeoutSeconds = AutoQuitTimeoutSeconds;
    [SerializeField] private int _playerBurstFinalSequence = -1;
    [SerializeField] private bool _playerStringMatched;
    [SerializeField] private bool _playerTwistMatched;
    [SerializeField] private bool _playerJoyMatched;
    [SerializeField] private bool _playerImuMatched;
    [SerializeField] private bool _playerBurstMatched;

    private readonly HashSet<string> _emittedMarkerKeys = new HashSet<string>(StringComparer.Ordinal);
    private string _activeCorrelationToken = string.Empty;
    // Unity's Editor.log can reuse storage below its apparent EOF. A fresh,
    // non-secret token lets the local helper distinguish this activation's
    // READY evidence from an older matching runtime line.
    private string _readyRunToken = string.Empty;
    private bool _readyMarkerEmitted;
    private bool _completionMarkerEmitted;
    private float _autoQuitDeadline;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private long _stringObservedSession = long.MinValue;
    private long _twistObservedSession = long.MinValue;
    private long _joyObservedSession = long.MinValue;
    private long _imuObservedSession = long.MinValue;
    private long _stringObservedApplied = -1;
    private long _twistObservedApplied = -1;
    private long _joyObservedApplied = -1;
    private long _imuObservedApplied = -1;
#else
    private bool _unavailableWarningEmitted;
#endif

    private void OnEnable()
    {
        if (_manager == null)
            _manager = FindFirstObjectByType<FoxgloveManager>();

        _emittedMarkerKeys.Clear();
        _emittedMarkerCount = 0;
        _activeCorrelationToken = string.Empty;
        _readyMarkerEmitted = false;
        _readyRunToken = Guid.NewGuid().ToString("N");
        _completionMarkerEmitted = false;
        _playerAutoQuitRequested = HasCommandLineFlag("--phase179-player-auto-quit");
        _playerToken = ReadCommandLineValue("--phase179-token") ?? string.Empty;
        _playerToken = _playerToken.Trim();
        _playerBurstFinalSequence = -1;
        var burstFinalSequenceText = ReadCommandLineValue("--phase179-player-burst-final-sequence");
        var hasBurstFinalSequence = HasCommandLineFlag("--phase179-player-burst-final-sequence");
        if (hasBurstFinalSequence
            && (string.IsNullOrEmpty(burstFinalSequenceText)
                || !TryParseNonNegativeInt(
                burstFinalSequenceText,
                0,
                burstFinalSequenceText.Length,
                out _playerBurstFinalSequence)))
        {
            _status = "Player burst final sequence must be a non-negative decimal integer.";
            CompletePlayer(3, "invalid-burst-final-sequence");
            return;
        }
        if (_playerBurstFinalSequence == int.MaxValue)
        {
            _status = "Player burst final sequence is outside the supported bounded range.";
            CompletePlayer(3, "invalid-burst-final-sequence");
            return;
        }
        _playerAutoQuitTimeoutSeconds = Mathf.Clamp(_playerAutoQuitTimeoutSeconds, 10f, 300f);
        _autoQuitDeadline = Time.realtimeSinceStartup + _playerAutoQuitTimeoutSeconds;
        _playerStringMatched = false;
        _playerTwistMatched = false;
        _playerJoyMatched = false;
        _playerImuMatched = false;
        _playerBurstMatched = false;

        if (_playerAutoQuitRequested && !IsSafeCorrelationToken(_playerToken))
        {
            _status = "Player auto-quit requires --phase179-token to start with an ASCII letter or digit and use at most 96 safe token characters.";
            CompletePlayer(3, "invalid-token");
            return;
        }

        _status = _manager == null
            ? "Assign a FoxgloveManager with native subscriptions enabled."
            : "Waiting for native ROS2 subscription registration.";
    }

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        ObserveString();
        ObserveTwist();
        ObserveJoy();
        ObserveImu();
        EmitReadyMarkerWhenRuntimeIsObserved();
        EvaluatePlayerAutoQuit();
#else
        WarnUnavailableOnce();
        if (_playerAutoQuitRequested)
            CompletePlayer(4, "runtime-unavailable");
#endif
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void ObserveString()
    {
        if (!TryGetNewlyAppliedSnapshot(
                StringTopic,
                ref _stringObservedSession,
                ref _stringObservedApplied,
                out var snapshot)
            || _inputString == null)
            return;

        var data = _inputString.Data ?? string.Empty;
        _stringData = CopySafeText(data, MaximumInspectorStringLength);
        CaptureCorrelationToken(ExtractCorrelationToken(data));
        _stringValuesApplied++;
        _playerStringMatched = _playerAutoQuitRequested && MatchesPlayerString(data);
        if (_playerAutoQuitRequested && _playerBurstFinalSequence >= 0)
            _playerBurstMatched = MatchesExpectedPlayerBurst(data);

        // A fast burst can apply more values than the bounded evidence budget.
        // Suppress intermediate sequence markers so the terminal latest value is
        // always eligible for correlation without turning callbacks into logs.
        var isBurst = TryParseBurstValue(data, out _, out var sequence, out var total);
        if (!isBurst || sequence == total - 1)
        {
            EmitAppliedMarker(
                StringTopic,
                ExtractCorrelationToken(data),
                BuildStringValue(data),
                snapshot);
        }
    }

    private void ObserveTwist()
    {
        if (!TryGetNewlyAppliedSnapshot(
                TwistTopic,
                ref _twistObservedSession,
                ref _twistObservedApplied,
                out var snapshot)
            || _inputTwist == null)
            return;

        var linear = _inputTwist.Linear;
        var angular = _inputTwist.Angular;
        _twistLinearX = linear == null ? 0d : linear.X;
        _twistLinearY = linear == null ? 0d : linear.Y;
        _twistAngularZ = angular == null ? 0d : angular.Z;
        _twistValuesApplied++;
        _playerTwistMatched = _playerAutoQuitRequested
                              && NearlyEqual(_twistLinearX, 1.25d)
                              && NearlyEqual(_twistLinearY, -0.25d)
                              && NearlyEqual(_twistAngularZ, -0.5d);
        EmitAppliedMarker(
            TwistTopic,
            PlayerOrFallbackToken("twist"),
            BuildTwistValue(_twistLinearX, _twistLinearY, _twistAngularZ),
            snapshot);
    }

    private void ObserveJoy()
    {
        if (!TryGetNewlyAppliedSnapshot(
                JoyTopic,
                ref _joyObservedSession,
                ref _joyObservedApplied,
                out var snapshot)
            || _inputJoy == null)
            return;

        var header = _inputJoy.Header;
        var frameId = header == null ? string.Empty : header.Frame_id ?? string.Empty;
        _joyFrameId = CopySafeText(frameId, MaximumInspectorStringLength);
        CaptureCorrelationToken(frameId);
        _joyAxes = CopyBounded(_inputJoy.Axes, MaximumInspectorArrayLength);
        _joyButtons = CopyBounded(_inputJoy.Buttons, MaximumInspectorArrayLength);
        _joyValuesApplied++;
        _playerJoyMatched = _playerAutoQuitRequested
                            && string.Equals(frameId, _playerToken, StringComparison.Ordinal)
                            && Matches(_inputJoy.Axes, 0.125f, -0.5f, 1f)
                            && Matches(_inputJoy.Buttons, 1, 0, 1);
        EmitAppliedMarker(
            JoyTopic,
            frameId,
            BuildJoyValue(frameId, _inputJoy.Axes, _inputJoy.Buttons),
            snapshot);
    }

    private void ObserveImu()
    {
        if (!TryGetNewlyAppliedSnapshot(
                ImuTopic,
                ref _imuObservedSession,
                ref _imuObservedApplied,
                out var snapshot)
            || _inputImu == null)
            return;

        var header = _inputImu.Header;
        var frameId = header == null ? string.Empty : header.Frame_id ?? string.Empty;
        var orientation = _inputImu.Orientation;
        var angularVelocity = _inputImu.Angular_velocity;
        var linearAcceleration = _inputImu.Linear_acceleration;
        _imuFrameId = CopySafeText(frameId, MaximumInspectorStringLength);
        CaptureCorrelationToken(frameId);
        _imuOrientationX = orientation == null ? 0d : orientation.X;
        _imuOrientationY = orientation == null ? 0d : orientation.Y;
        _imuOrientationZ = orientation == null ? 0d : orientation.Z;
        _imuOrientationW = orientation == null ? 0d : orientation.W;
        _imuAngularVelocityX = angularVelocity == null ? 0d : angularVelocity.X;
        _imuAngularVelocityY = angularVelocity == null ? 0d : angularVelocity.Y;
        _imuAngularVelocityZ = angularVelocity == null ? 0d : angularVelocity.Z;
        _imuLinearAccelerationX = linearAcceleration == null ? 0d : linearAcceleration.X;
        _imuLinearAccelerationY = linearAcceleration == null ? 0d : linearAcceleration.Y;
        _imuLinearAccelerationZ = linearAcceleration == null ? 0d : linearAcceleration.Z;
        _imuValuesApplied++;
        _playerImuMatched = _playerAutoQuitRequested
                            && string.Equals(frameId, _playerToken, StringComparison.Ordinal)
                            && NearlyEqual(_imuOrientationX, 0.1d)
                            && NearlyEqual(_imuOrientationY, -0.2d)
                            && NearlyEqual(_imuOrientationZ, 0.3d)
                            && NearlyEqual(_imuOrientationW, 0.9d)
                            && NearlyEqual(_imuAngularVelocityX, 0.4d)
                            && NearlyEqual(_imuAngularVelocityY, -0.5d)
                            && NearlyEqual(_imuAngularVelocityZ, 0.6d)
                            && NearlyEqual(_imuLinearAccelerationX, 1.1d)
                            && NearlyEqual(_imuLinearAccelerationY, 1.2d)
                            && NearlyEqual(_imuLinearAccelerationZ, 1.3d);
        EmitAppliedMarker(
            ImuTopic,
            frameId,
            BuildImuValue(),
            snapshot);
    }

    private bool TryGetNewlyAppliedSnapshot(
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
        _lastReceived = snapshot.Received;
        _lastApplied = snapshot.Applied;
        _lastReplaced = snapshot.Replaced;
        _lastSessionGeneration = snapshot.SessionGeneration;
        return true;
    }

    private void EmitReadyMarkerWhenRuntimeIsObserved()
    {
        if (_readyMarkerEmitted)
            return;

        var snapshots = FoxRunRos2SubscriptionRuntimeDiagnostics.GetSnapshots();
        for (var i = 0; i < snapshots.Length; i++)
        {
            var snapshot = snapshots[i];
            if (!string.Equals(snapshot.Topic, StringTopic, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(snapshot.RosDistro)
                || string.IsNullOrWhiteSpace(snapshot.RmwImplementation)
                || (snapshot.State != FoxRunRos2SubscriptionBindingState.Ready
                    && snapshot.State != FoxRunRos2SubscriptionBindingState.Receiving))
                continue;

            if (_readyMarkerEmitted)
                return;

            _readyMarkerEmitted = true;
            _status = "Native ROS2 String subscription registered: "
                      + snapshot.RosDistro + " / " + snapshot.RmwImplementation + ".";
            Debug.Log(
                "PHASE179_ROS2_INBOUND_READY runtime=" + SanitizeToken(snapshot.RosDistro)
                + " rmw=" + SanitizeToken(snapshot.RmwImplementation)
                + " token=" + SanitizeToken(PlayerOrFallbackToken(_readyRunToken)),
                this);
            return;
        }

        if (_readyMarkerEmitted)
        {
            _readyMarkerEmitted = false;
            _status = "Waiting for native ROS2 String subscription registration.";
        }
    }
#endif

    private void EvaluatePlayerAutoQuit()
    {
        if (!_playerAutoQuitRequested || _completionMarkerEmitted || Application.isEditor)
            return;

        if (_playerStringMatched
            && _playerTwistMatched
            && _playerJoyMatched
            && (_playerBurstFinalSequence < 0 || _playerBurstMatched))
        {
            _status = _playerBurstFinalSequence < 0
                ? "Required String, Twist, and Joy values matched the Player token."
                : "Required values and the final latest-wins String burst matched the Player token.";
            CompletePlayer(0, "success");
            return;
        }

        if (Time.realtimeSinceStartup >= _autoQuitDeadline)
        {
            _status = "Timed out waiting for String, Twist, and Joy values that match the Player token.";
            CompletePlayer(2, "timeout");
        }
    }

    private void CompletePlayer(int exitCode, string outcome)
    {
        if (_completionMarkerEmitted)
            return;

        _completionMarkerEmitted = true;
        Debug.Log(
            "PHASE179_ROS2_INBOUND_COMPLETE token=" + SanitizeToken(PlayerOrFallbackToken("none"))
            + " outcome=" + SanitizeToken(outcome)
            + " exitCode=" + exitCode.ToString(CultureInfo.InvariantCulture),
            this);
        if (!Application.isEditor)
            Application.Quit(exitCode);
    }

#if !UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void WarnUnavailableOnce()
    {
        if (_unavailableWarningEmitted)
            return;

        _unavailableWarningEmitted = true;
        _status = _nativeRuntimeAvailability;
        Debug.LogWarning("[Phase179] " + _nativeRuntimeAvailability, this);
    }
#endif

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void EmitAppliedMarker(
        string topic,
        string valueToken,
        string valueJson,
        FoxRunRos2SubscriptionAcceptanceSnapshot snapshot)
    {
        var markerToken = SanitizeToken(PlayerTokenOrValue(valueToken));
        var key = snapshot.SessionGeneration.ToString(CultureInfo.InvariantCulture)
                  + "|" + topic + "|" + markerToken + "|" + valueJson;
        if (_emittedMarkerCount >= MaximumMarkerCount || !_emittedMarkerKeys.Add(key))
            return;

        _emittedMarkerCount++;
        Debug.Log(
            "PHASE179_ROS2_INBOUND_APPLIED session="
            + snapshot.SessionGeneration.ToString(CultureInfo.InvariantCulture)
            + " topic=" + topic
            + " token=" + markerToken
            + " received=" + snapshot.Received.ToString(CultureInfo.InvariantCulture)
            + " applied=" + snapshot.Applied.ToString(CultureInfo.InvariantCulture)
            + " replaced=" + snapshot.Replaced.ToString(CultureInfo.InvariantCulture)
            + " value=" + valueJson,
            this);
    }

    private string PlayerTokenOrValue(string valueToken)
    {
        if (_playerAutoQuitRequested)
            return _playerToken;
        return IsSafeCorrelationToken(_activeCorrelationToken)
            ? _activeCorrelationToken
            : valueToken;
    }

    private void CaptureCorrelationToken(string value)
    {
        // Twist has no semantic token field. The helper sends a String first,
        // so a current safe token can still correlate the deterministic Twist
        // marker without changing its ROS2 message contract.
        if (IsSafeCorrelationToken(value))
            _activeCorrelationToken = value;
    }

    private string PlayerOrFallbackToken(string fallback)
        => !string.IsNullOrWhiteSpace(_playerToken) ? _playerToken : fallback;

    private string BuildStringValue(string data)
        => "{\"type\":\"String\",\"data\":\""
           + EscapeJson(CopySafeText(data, MaximumMarkerTextLength)) + "\"}";

    private static string BuildTwistValue(double linearX, double linearY, double angularZ)
        => "{\"type\":\"Twist\",\"linear\":{\"x\":" + FormatNumber(linearX)
           + ",\"y\":" + FormatNumber(linearY)
           + "},\"angular\":{\"z\":" + FormatNumber(angularZ) + "}}";

    private static string BuildJoyValue(string frameId, float[] axes, int[] buttons)
        => "{\"type\":\"Joy\",\"frameId\":\"" + EscapeJson(CopySafeText(frameId, MaximumMarkerTextLength))
           + "\",\"axes\":" + FormatArray(axes)
           + ",\"buttons\":" + FormatArray(buttons) + "}";

    private string BuildImuValue()
        => "{\"type\":\"Imu\",\"frameId\":\"" + EscapeJson(CopySafeText(_imuFrameId, MaximumMarkerTextLength))
           + "\",\"orientation\":{\"x\":" + FormatNumber(_imuOrientationX)
           + ",\"y\":" + FormatNumber(_imuOrientationY)
           + ",\"z\":" + FormatNumber(_imuOrientationZ)
           + ",\"w\":" + FormatNumber(_imuOrientationW)
           + "},\"angularVelocity\":{\"x\":" + FormatNumber(_imuAngularVelocityX)
           + ",\"y\":" + FormatNumber(_imuAngularVelocityY)
           + ",\"z\":" + FormatNumber(_imuAngularVelocityZ)
           + "},\"linearAcceleration\":{\"x\":" + FormatNumber(_imuLinearAccelerationX)
           + ",\"y\":" + FormatNumber(_imuLinearAccelerationY)
           + ",\"z\":" + FormatNumber(_imuLinearAccelerationZ) + "}}";
#endif

    private static bool Matches(float[] values, float first, float second, float third)
        => values != null
           && values.Length == 3
           && NearlyEqual(values[0], first)
           && NearlyEqual(values[1], second)
           && NearlyEqual(values[2], third);

    private static bool Matches(int[] values, int first, int second, int third)
        => values != null
           && values.Length == 3
           && values[0] == first
           && values[1] == second
           && values[2] == third;

    private bool MatchesPlayerString(string value)
    {
        if (string.Equals(value, _playerToken, StringComparison.Ordinal))
            return true;

        return TryParseBurstValue(value, out var correlationToken, out var sequence, out var total)
               && string.Equals(correlationToken, _playerToken, StringComparison.Ordinal)
               && sequence == total - 1;
    }

    private bool MatchesExpectedPlayerBurst(string value)
    {
        if (_playerBurstFinalSequence < 0
            || !TryParseBurstValue(value, out var correlationToken, out var sequence, out var total))
        {
            return false;
        }

        return string.Equals(correlationToken, _playerToken, StringComparison.Ordinal)
               && sequence == _playerBurstFinalSequence
               && total == _playerBurstFinalSequence + 1;
    }

    private static string ExtractCorrelationToken(string value)
        => TryParseBurstValue(value, out var correlationToken, out _, out _)
            ? correlationToken
            : value ?? string.Empty;

    private static bool TryParseBurstValue(
        string value,
        out string correlationToken,
        out int sequence,
        out int total)
    {
        correlationToken = string.Empty;
        sequence = -1;
        total = -1;
        if (string.IsNullOrEmpty(value))
            return false;

        const string sequencePrefix = "|seq=";
        const string totalPrefix = "|total=";
        var sequenceIndex = value.LastIndexOf(sequencePrefix, StringComparison.Ordinal);
        var totalIndex = value.LastIndexOf(totalPrefix, StringComparison.Ordinal);
        if (sequenceIndex <= 0 || totalIndex <= sequenceIndex + sequencePrefix.Length)
            return false;

        if (!TryParseNonNegativeInt(value, sequenceIndex + sequencePrefix.Length, totalIndex, out sequence)
            || !TryParseNonNegativeInt(value, totalIndex + totalPrefix.Length, value.Length, out total)
            || total <= 0
            || sequence < 0
            || sequence >= total)
        {
            sequence = -1;
            total = -1;
            return false;
        }

        correlationToken = value.Substring(0, sequenceIndex);
        return IsSafeCorrelationToken(correlationToken);
    }

    private static bool TryParseNonNegativeInt(string value, int start, int end, out int parsed)
    {
        parsed = 0;
        if (start < 0 || end <= start || end > value.Length)
            return false;
        for (var i = start; i < end; i++)
        {
            var digit = value[i] - '0';
            if (digit < 0 || digit > 9 || parsed > (int.MaxValue - digit) / 10)
                return false;
            parsed = parsed * 10 + digit;
        }
        return true;
    }

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) <= EqualityTolerance;

    private static bool NearlyEqual(float left, float right)
        => Math.Abs(left - right) <= (float)EqualityTolerance;

    private static string FormatNumber(double value)
        => value.ToString("0.0###############", CultureInfo.InvariantCulture);

    private static string FormatArray(float[] values)
    {
        var count = Math.Min(values?.Length ?? 0, MaximumMarkerArrayLength);
        var builder = new StringBuilder(count * 8 + 2);
        builder.Append('[');
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(FormatNumber(values[i]));
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static string FormatArray(int[] values)
    {
        var count = Math.Min(values?.Length ?? 0, MaximumMarkerArrayLength);
        var builder = new StringBuilder(count * 4 + 2);
        builder.Append('[');
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static string EscapeJson(string value)
    {
        value = value ?? string.Empty;
        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '\"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < ' ')
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(character);
                    break;
            }
        }
        return builder.ToString();
    }

    private static string CopyBounded(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
    }

    private static string CopySafeText(string value, int maximumLength)
    {
        var bounded = CopyBounded(value, maximumLength);
        return ContainsSensitiveMarkerText(bounded) ? "redacted" : bounded;
    }

    private static bool ContainsSensitiveMarkerText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        for (var i = 0; i < SensitiveMarkerFragments.Length; i++)
        {
            if (value.IndexOf(SensitiveMarkerFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static float[] CopyBounded(float[] values, int maximumLength)
    {
        var count = Math.Min(values?.Length ?? 0, maximumLength);
        if (count == 0)
            return Array.Empty<float>();
        var copy = new float[count];
        Array.Copy(values, copy, count);
        return copy;
    }

    private static int[] CopyBounded(int[] values, int maximumLength)
    {
        var count = Math.Min(values?.Length ?? 0, maximumLength);
        if (count == 0)
            return Array.Empty<int>();
        var copy = new int[count];
        Array.Copy(values, copy, count);
        return copy;
    }

    private static bool HasCommandLineFlag(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string ReadCommandLineValue(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var i = 0; i < arguments.Length - 1; i++)
        {
            if (!string.Equals(arguments[i], name, StringComparison.Ordinal))
                continue;
            var value = arguments[i + 1];
            return string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal)
                ? null
                : value;
        }
        return null;
    }

    private static bool IsSafeCorrelationToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > MaximumTokenLength
            || !IsAsciiLetterOrDigit(token[0])
            || ContainsSensitiveMarkerText(token))
            return false;
        for (var i = 0; i < token.Length; i++)
        {
            var character = token[i];
            if (!IsAsciiLetterOrDigit(character)
                && character != '.'
                && character != '_'
                && character != ':'
                && character != '-')
                return false;
        }
        return true;
    }

    private static bool IsAsciiLetterOrDigit(char character)
        => (character >= 'a' && character <= 'z')
           || (character >= 'A' && character <= 'Z')
           || (character >= '0' && character <= '9');

    private static string SanitizeToken(string value)
    {
        value = CopyBounded(value, MaximumTokenLength);
        if (value.Length == 0)
            return "none";
        if (ContainsSensitiveMarkerText(value))
            return "redacted";
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            builder.Append(IsAsciiLetterOrDigit(character)
                           || character == '.'
                           || character == '_'
                           || character == ':'
                           || character == '-'
                ? character
                : '_');
        }
        return builder.ToString();
    }
}
