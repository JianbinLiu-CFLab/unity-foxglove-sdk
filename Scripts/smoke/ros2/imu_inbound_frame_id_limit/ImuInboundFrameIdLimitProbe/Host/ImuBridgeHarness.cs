// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Host the real IMU inbound bridge outside Unity and record the frame ids it delivers.

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity2Foxglove.Ros2ForUnity.Native;
using Unity.FoxgloveSDK.Schemas.Imu;

/// <summary>Drives the bridge's Unity lifecycle methods from the probe thread acting as the Unity main thread.</summary>
internal sealed class ImuBridgeHarness
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic;

    private readonly List<string> _delivered = new List<string>();
    private bool _enabled;

    internal ImuBridgeHarness(ROS2.ROS2UnityComponent unity, string topic)
    {
        OpenStablePlayModeGate();
        Bridge = new Ros2ForUnityImuInboundBridge { gameObject = new UnityEngine.GameObject("imu-probe") };
        Bridge.Topic = topic;
        typeof(Ros2ForUnityImuInboundBridge).GetField("_ros2Unity", InstanceMembers).SetValue(Bridge, unity);
        Bridge.FrameReceived += Record;
    }

    internal Ros2ForUnityImuInboundBridge Bridge { get; }

    internal IReadOnlyList<string> Delivered => _delivered;

    internal void Enable()
    {
        Invoke("OnEnable");
        _enabled = true;
    }

    internal void Update() => Invoke("Update");

    internal void Disable()
    {
        if (!_enabled)
            return;
        _enabled = false;
        Invoke("OnDisable");
    }

    private void Record(ImuNativeFrame frame) => _delivered.Add(frame.FrameId);

    private void Invoke(string method)
    {
        try
        {
            typeof(Ros2ForUnityImuInboundBridge).GetMethod(method, InstanceMembers).Invoke(Bridge, null);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            throw new InvalidOperationException("Bridge " + method + " failed.", error.InnerException);
        }
    }

    /// The probe has no Unity scene loop, so it marks the lifecycle gate's cached scene state stable.
    private static void OpenStablePlayModeGate()
    {
        var gate = typeof(Ros2ForUnityImuInboundBridge).Assembly
            .GetType("Unity2Foxglove.Ros2ForUnity.Native.Ros2ForUnityNativeBridgeLifecycleGate", throwOnError: true);
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        gate.GetField("_lastRefreshedActiveSceneHandle", StaticMembers).SetValue(null, active.handle);
        gate.GetField("_lastRefreshedSceneCount", StaticMembers).SetValue(null, UnityEngine.SceneManagement.SceneManager.sceneCount);
        gate.GetField("_isStablePlayModeScene", StaticMembers).SetValue(null, true);
        gate.GetField("_nativeReloadWindow", StaticMembers).SetValue(null, false);
        gate.GetField("_sceneStateDirty", StaticMembers).SetValue(null, false);
        UnityEngine.Application.isPlaying = true;
    }
}
