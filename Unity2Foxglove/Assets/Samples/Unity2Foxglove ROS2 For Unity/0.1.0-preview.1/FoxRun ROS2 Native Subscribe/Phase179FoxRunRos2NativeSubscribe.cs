// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Source-only four-type FoxRun native ROS2 Subscribe sample.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

/// <summary>
/// Imports four existing ROS2 message subscriptions into a Unity scene without
/// adding a ROS2 dependency to the core SDK. The generated binding owns its
/// typed message fields; this sample stores only bounded managed evidence for
/// Inspector display and never keeps a borrowed callback message reference.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/FoxRun Native Subscribe")]
public sealed partial class Phase179FoxRunRos2NativeSubscribe : MonoBehaviour
{
    public const string StringTopic = "/foxrun/phase179/string";
    public const string TwistTopic = "/foxrun/phase179/twist";
    public const string JoyTopic = "/foxrun/phase179/joy";
    public const string ImuTopic = "/foxrun/phase179/imu";

    private const int MaximumStringLength = 256;
    private const int MaximumArrayLength = 8;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    [FoxRun(
        StringTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscribeTransportId =
            FoxRunRos2TransportProvider.IdValue)]
    private std_msgs.msg.String _inputString;

    [FoxRun(
        TwistTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscribeTransportId =
            FoxRunRos2TransportProvider.IdValue)]
    private geometry_msgs.msg.Twist _inputTwist;

    [FoxRun(
        JoyTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscribeTransportId =
            FoxRunRos2TransportProvider.IdValue)]
    private sensor_msgs.msg.Joy _inputJoy;

    [FoxRun(
        ImuTopic,
        Mode = FoxRunFlow.Subscribe,
        SubscribeTransportId =
            FoxRunRos2TransportProvider.IdValue)]
    private sensor_msgs.msg.Imu _inputImu;
#else
    [Header("Native Runtime Availability")]
    [TextArea(2, 3)]
    [SerializeField] private string _nativeRuntimeAvailability =
        "No active ROS2 For Unity runtime. Select exactly one runtime package, let Unity compile UNITY2FOXGLOVE_ROS2_FOR_UNITY, then restart the Editor when required.";
#endif

    [Header("Safe Managed Inspector Copies")]
    [SerializeField] private string _status = "Waiting for native ROS2 inputs.";
    [SerializeField] private string _stringData = string.Empty;
    [SerializeField] private double _twistLinearX;
    [SerializeField] private double _twistLinearY;
    [SerializeField] private double _twistAngularZ;
    [SerializeField] private string _joyFrameId = string.Empty;
    [SerializeField] private float[] _joyAxes = Array.Empty<float>();
    [SerializeField] private int[] _joyButtons = Array.Empty<int>();
    [SerializeField] private string _imuFrameId = string.Empty;
    [SerializeField] private double _imuOrientationW;
    [SerializeField] private double _imuAngularVelocityZ;
    [SerializeField] private double _imuLinearAccelerationZ;

    /// <summary>Gets the most recent bounded Inspector status text.</summary>
    public string Status => _status;

    [Header("Generated Binding Counters")]
    [SerializeField] private long _received;
    [SerializeField] private long _applied;
    [SerializeField] private long _replaced;
    [SerializeField] private long _sessionGeneration;

    private readonly Dictionary<string, ObservedCounters> _observed =
        new Dictionary<string, ObservedCounters>(StringComparer.Ordinal);

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        ObserveString();
        ObserveTwist();
        ObserveJoy();
        ObserveImu();
#else
        _status = _nativeRuntimeAvailability;
#endif
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void ObserveString()
    {
        if (!TryObserve(StringTopic, out var snapshot) || _inputString == null)
            return;
        _stringData = CopyBounded(_inputString.Data, MaximumStringLength);
        _status = "Applied std_msgs/msg/String on Unity's main thread.";
        CopyCounters(snapshot);
    }

    private void ObserveTwist()
    {
        if (!TryObserve(TwistTopic, out var snapshot) || _inputTwist == null)
            return;
        var linear = _inputTwist.Linear;
        var angular = _inputTwist.Angular;
        _twistLinearX = linear == null ? 0d : linear.X;
        _twistLinearY = linear == null ? 0d : linear.Y;
        _twistAngularZ = angular == null ? 0d : angular.Z;
        _status = "Applied geometry_msgs/msg/Twist on Unity's main thread.";
        CopyCounters(snapshot);
    }

    private void ObserveJoy()
    {
        if (!TryObserve(JoyTopic, out var snapshot) || _inputJoy == null)
            return;
        _joyFrameId = CopyBounded(_inputJoy.Header == null ? string.Empty : _inputJoy.Header.Frame_id, MaximumStringLength);
        _joyAxes = CopyBounded(_inputJoy.Axes, MaximumArrayLength);
        _joyButtons = CopyBounded(_inputJoy.Buttons, MaximumArrayLength);
        _status = "Applied sensor_msgs/msg/Joy on Unity's main thread.";
        CopyCounters(snapshot);
    }

    private void ObserveImu()
    {
        if (!TryObserve(ImuTopic, out var snapshot) || _inputImu == null)
            return;
        _imuFrameId = CopyBounded(_inputImu.Header == null ? string.Empty : _inputImu.Header.Frame_id, MaximumStringLength);
        _imuOrientationW = _inputImu.Orientation == null ? 0d : _inputImu.Orientation.W;
        _imuAngularVelocityZ = _inputImu.Angular_velocity == null ? 0d : _inputImu.Angular_velocity.Z;
        _imuLinearAccelerationZ = _inputImu.Linear_acceleration == null ? 0d : _inputImu.Linear_acceleration.Z;
        _status = "Applied sensor_msgs/msg/Imu on Unity's main thread.";
        CopyCounters(snapshot);
    }

    private bool TryObserve(string topic, out FoxRunRos2SubscriptionAcceptanceSnapshot snapshot)
    {
        if (!FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet(this, topic, out snapshot))
            return false;

        if (!_observed.TryGetValue(topic, out var observed)
            || observed.SessionGeneration != snapshot.SessionGeneration)
        {
            observed = new ObservedCounters(snapshot.SessionGeneration, -1);
        }

        if (snapshot.Applied <= observed.Applied)
            return false;

        _observed[topic] = new ObservedCounters(snapshot.SessionGeneration, snapshot.Applied);
        return true;
    }

    private void CopyCounters(FoxRunRos2SubscriptionAcceptanceSnapshot snapshot)
    {
        _received = snapshot.Received;
        _applied = snapshot.Applied;
        _replaced = snapshot.Replaced;
        _sessionGeneration = snapshot.SessionGeneration;
    }
#endif

    private static string CopyBounded(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
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

    private readonly struct ObservedCounters
    {
        public ObservedCounters(long sessionGeneration, long applied)
        {
            SessionGeneration = sessionGeneration;
            Applied = applied;
        }

        public long SessionGeneration { get; }
        public long Applied { get; }
    }
}
