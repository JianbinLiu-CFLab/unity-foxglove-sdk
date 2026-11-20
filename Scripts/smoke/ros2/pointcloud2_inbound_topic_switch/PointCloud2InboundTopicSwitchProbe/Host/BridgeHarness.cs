// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Host the real PointCloud2 inbound bridge outside Unity and record the frames it delivers.

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity2Foxglove.Ros2ForUnity.Native;
using Unity.FoxgloveSDK.Schemas.PointCloud;

/// <summary>Drives the bridge's Unity lifecycle methods from the probe thread acting as the Unity main thread.</summary>
internal sealed class BridgeHarness
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.NonPublic;

    private readonly List<(string Topic, string FrameId)> _delivered = new List<(string Topic, string FrameId)>();
    private bool _enabled;

    internal BridgeHarness(ROS2.ROS2UnityComponent unity, string initialTopic)
    {
        OpenStablePlayModeGate();
        Bridge = new Ros2ForUnityPointCloud2InboundBridge { gameObject = new UnityEngine.GameObject("pointcloud2-probe") };
        Bridge.Topic = initialTopic;
        typeof(Ros2ForUnityPointCloud2InboundBridge).GetField("_ros2Unity", InstanceMembers).SetValue(Bridge, unity);
        Bridge.FrameReceived += Record;
    }

    internal Ros2ForUnityPointCloud2InboundBridge Bridge { get; }

    internal IReadOnlyList<(string Topic, string FrameId)> Delivered => _delivered;

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

    private void Record(PackedPointCloudFrame frame) => _delivered.Add((frame.Topic, frame.FrameId));

    private void Invoke(string method)
    {
        try
        {
            typeof(Ros2ForUnityPointCloud2InboundBridge).GetMethod(method, InstanceMembers).Invoke(Bridge, null);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            throw new InvalidOperationException("Bridge " + method + " failed.", error.InnerException);
        }
    }

    /// The probe has no Unity scene loop, so it marks the lifecycle gate's cached scene state stable.
    private static void OpenStablePlayModeGate()
    {
        var gate = typeof(Ros2ForUnityPointCloud2InboundBridge).Assembly
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
