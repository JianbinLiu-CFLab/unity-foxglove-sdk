// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Publishes Phase130 RViz2 MarkerArray acceptance topics.

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/RViz2 MarkerArray Smoke")]
public sealed class Phase130Rviz2MarkerArraySmoke : MonoBehaviour
{
    private const string LogPrefix = "[Phase130Rviz2MarkerArraySmoke]";
    private const string NodeName = "unity2foxglove_phase130_markerarray";
    private const string MarkersTopic = "/markers";
    private const string MarkerStableName = "phase130_cube";
    private const float DefaultPublishIntervalSeconds = 0.5f;
    private const int DeleteCycleLength = 24;
    private const int DeleteFrame = 20;
    private const int DeleteAllFrame = 21;

    [Header("Publish")]
    [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = DefaultPublishIntervalSeconds;

    [Header("Runtime Evidence")]
    [SerializeField] private string _runtimeRoot = string.Empty;
    [SerializeField] private bool _runtimeRootIsPackage;
    [SerializeField] private bool _assetRuntimePresent;

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Not started.";
    [SerializeField] private int _publishedMarkerArrayCount;
    [SerializeField] private int _activeMarkerCount;
    [SerializeField] private int _lastMarkerId;
    [SerializeField] private string _lastAction = string.Empty;
    [SerializeField] private string _lastError = string.Empty;

    private float _nextPublishAt;
    private bool _warnedMissingDefine;
    private bool _runtimeRootLogged;
    private bool _endpointsLogged;
    private bool _firstPublishLogged;
    private bool _initializationBlocked;
    private bool _warnedMissingStartExecutor;
    private double _realtimeStartSeconds;
    private long _unixStartSeconds;
    private double _lastStampSeconds;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _node;
    private IPublisher<visualization_msgs.msg.MarkerArray> _markerPublisher;
    private MethodInfo _startExecutor;
    private bool _ownsRos2UnityComponent;
    private bool _executorStarted;
#endif

    private void OnEnable()
    {
        Application.runInBackground = true;
        _nextPublishAt = 0f;
        _publishedMarkerArrayCount = 0;
        _activeMarkerCount = 0;
        _lastMarkerId = Phase130MarkerArrayMessageBuilder.CreateDeterministicId(MarkerStableName);
        _lastAction = string.Empty;
        _lastError = string.Empty;
        _statusMessage = "Starting Phase130 RViz2 MarkerArray smoke.";
        _runtimeRoot = string.Empty;
        _runtimeRootIsPackage = false;
        _assetRuntimePresent = false;
        _runtimeRootLogged = false;
        _endpointsLogged = false;
        _firstPublishLogged = false;
        _initializationBlocked = false;
        _warnedMissingStartExecutor = false;
        _unixStartSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _realtimeStartSeconds = RealtimeSeconds();
        _lastStampSeconds = 0d;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        EnsureRos2UnityComponent();
#else
        WarnMissingDefine();
#endif
    }

    private void Update()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        LogRuntimeRootOnce();
        if (!TryEnsureReady())
            return;

        EnsureExecutorStarted();
        EnsureEndpoints();
        PublishIfDue();
#else
        WarnMissingDefine();
#endif
    }

    private void OnDisable()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        CleanupRuntime();
#endif
    }

    private void OnDestroy()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        CleanupRuntime();
#endif
    }

    private void OnValidate()
    {
        _publishIntervalSeconds = Mathf.Max(0.1f, _publishIntervalSeconds);
    }

    private void WarnMissingDefine()
    {
        _lastError = "Import ROS2 For Unity and add UNITY2FOXGLOVE_ROS2_FOR_UNITY before running this sample.";
        _statusMessage = _lastError;
        if (_warnedMissingDefine)
            return;

        _warnedMissingDefine = true;
        Debug.LogWarning(LogPrefix + " " + _statusMessage);
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private void EnsureRos2UnityComponent()
    {
        if (_ros2Unity != null)
            return;

        _ros2Unity = GetComponent<ROS2UnityComponent>();
        if (_ros2Unity == null)
        {
            _ros2Unity = gameObject.AddComponent<ROS2UnityComponent>();
            _ownsRos2UnityComponent = true;
        }

        _startExecutor = typeof(ROS2UnityComponent).GetMethod(
            "StartExecutor",
            BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private bool TryEnsureReady()
    {
        if (_initializationBlocked)
            return false;

        try
        {
            EnsureRos2UnityComponent();
            if (_ros2Unity != null && _ros2Unity.Ok())
            {
                _statusMessage = "ROS2 For Unity is ready for Phase130 MarkerArray acceptance.";
                _lastError = string.Empty;
                return true;
            }

            _statusMessage = "Waiting for ROS2 For Unity runtime.";
            return false;
        }
        catch (Exception ex)
        {
            _initializationBlocked = true;
            _lastError = ex.GetType().Name + ": " + ex.Message;
            _statusMessage = _lastError;
            Debug.LogWarning(LogPrefix + " ROS2 For Unity initialization failed: " + _lastError);
            return false;
        }
    }

    private void EnsureExecutorStarted()
    {
        if (_executorStarted)
            return;

        if (_startExecutor == null)
        {
            if (!_warnedMissingStartExecutor)
            {
                _warnedMissingStartExecutor = true;
                Debug.LogWarning(LogPrefix + " StartExecutor reflection hook was not found; continuing without explicit executor start.");
            }

            _executorStarted = true;
            return;
        }

        _startExecutor.Invoke(_ros2Unity, null);
        _executorStarted = true;
    }

    private void EnsureEndpoints()
    {
        if (_markerPublisher != null)
            return;

        if (_node == null)
            _node = _ros2Unity.CreateNode(NodeName);
        _markerPublisher = _node.CreatePublisher<visualization_msgs.msg.MarkerArray>(MarkersTopic);

        if (!_endpointsLogged && _markerPublisher != null)
        {
            _endpointsLogged = true;
            _statusMessage = "Phase130 RViz2 MarkerArray endpoints ready.";
            Debug.Log(LogPrefix + " READY node=" + NodeName + " markers=" + MarkersTopic);
        }
    }

    private void PublishIfDue()
    {
        if (_markerPublisher == null)
            return;

        if (Time.unscaledTime < _nextPublishAt)
            return;

        _nextPublishAt = Time.unscaledTime + _publishIntervalSeconds;
        CreateStamp(out var sec, out var nanosec);

        try
        {
            var markerArray = CreateMarkerArray(sec, nanosec);
            _markerPublisher.Publish(markerArray);
        }
        catch (Exception ex)
        {
            _lastError = ex.GetType().Name + ": " + ex.Message;
            _statusMessage = _lastError;
            Debug.LogWarning(LogPrefix + " publish failed: " + _lastError);
            return;
        }

        _publishedMarkerArrayCount++;
        _lastError = string.Empty;
        _statusMessage = "Published markers=" + _publishedMarkerArrayCount
            + " active=" + _activeMarkerCount
            + " action=" + _lastAction + ".";

        if (!_firstPublishLogged)
        {
            _firstPublishLogged = true;
            Debug.Log(LogPrefix + " first publish: frame=map topic=" + MarkersTopic + " ns=" + Phase130MarkerArrayMessageBuilder.DefaultNamespace);
        }
    }

    private visualization_msgs.msg.MarkerArray CreateMarkerArray(int sec, uint nanosec)
    {
        var cycle = _publishedMarkerArrayCount % DeleteCycleLength;
        if (cycle == DeleteFrame)
        {
            _activeMarkerCount = 0;
            _lastAction = "DELETE";
            return Phase130MarkerArrayMessageBuilder.BuildDelete(MarkerStableName, sec, nanosec);
        }

        if (cycle == DeleteAllFrame)
        {
            _activeMarkerCount = 0;
            _lastAction = "DELETEALL";
            return Phase130MarkerArrayMessageBuilder.BuildDeleteAll(sec, nanosec);
        }

        var phase = Time.unscaledTime;
        var x = Mathf.Sin(phase * 0.7f) * 1.2f;
        var y = Mathf.Cos(phase * 0.5f) * 0.8f;
        var z = 0.5f + Mathf.Sin(phase * 1.3f) * 0.25f;
        var size = 0.35f + (Mathf.Sin(phase * 0.9f) + 1f) * 0.08f;
        var color = Color.HSVToRGB(Mathf.Repeat(phase * 0.08f, 1f), 0.85f, 1f);
        color.a = 0.9f;

        _activeMarkerCount = 1;
        _lastAction = "ADD";
        return Phase130MarkerArrayMessageBuilder.BuildAddOrModify(
            MarkerStableName,
            new Vector3(x, y, z),
            new Vector3(size, size, size),
            color,
            sec,
            nanosec);
    }

    private void CreateStamp(out int sec, out uint nanosec)
    {
        var elapsed = Math.Max(0d, RealtimeSeconds() - _realtimeStartSeconds);
        var totalSeconds = _unixStartSeconds + elapsed;
        if (totalSeconds <= _lastStampSeconds)
            totalSeconds = _lastStampSeconds + 0.000001d;
        _lastStampSeconds = totalSeconds;

        // ROS2 Time.sec is int32, so wall-clock stamps inherit the protocol Y2038 limit.
        sec = (int)Math.Floor(totalSeconds);
        var fractional = totalSeconds - sec;
        var nanos = (uint)Math.Round(fractional * 1000000000d);
        if (nanos >= 1000000000u)
        {
            sec++;
            nanos -= 1000000000u;
        }

        nanosec = nanos;
    }

    private void LogRuntimeRootOnce()
    {
        if (_runtimeRootLogged)
            return;

        _runtimeRootLogged = true;
        _runtimeRoot = ResolveRuntimeRoot();
        _runtimeRootIsPackage = IsPackageRuntimeRoot(_runtimeRoot);
        _assetRuntimePresent = Directory.Exists(Path.Combine(Application.dataPath, "Ros2ForUnity"));
        Debug.Log(LogPrefix + " RUNTIME_ROOT=" + _runtimeRoot);
        Debug.Log(LogPrefix + " RUNTIME_ROOT_IS_PACKAGE=" + _runtimeRootIsPackage);
        Debug.Log(LogPrefix + " ASSET_RUNTIME_PRESENT=" + _assetRuntimePresent);
    }

    private static string ResolveRuntimeRoot()
    {
        var type = typeof(ROS2UnityComponent).Assembly.GetType("ROS2.ROS2ForUnity");
        var method = type?.GetMethod(
            "GetRos2ForUnityPath",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as string ?? string.Empty;
    }

    private void CleanupRuntime()
    {
        if (_node != null && _markerPublisher != null)
        {
            try
            {
                _node.RemovePublisher<visualization_msgs.msg.MarkerArray>(_markerPublisher);
            }
            catch (Exception)
            {
            }
        }

        if (_ros2Unity != null && _node != null)
        {
            try
            {
                _ros2Unity.RemoveNode(_node);
            }
            catch (Exception)
            {
            }
        }

        _markerPublisher = null;
        _node = null;
        if (_ownsRos2UnityComponent && _ros2Unity != null)
            Destroy(_ros2Unity);
        _ros2Unity = null;
        _ownsRos2UnityComponent = false;
        _executorStarted = false;
        _initializationBlocked = false;
        _warnedMissingStartExecutor = false;
    }
#endif

    private static bool IsPackageRuntimeRoot(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("/packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/runtime/ros2forunity")
               && !normalized.Contains("/unity2foxglove/assets/ros2forunity");
    }

    private static string NormalizePath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
    }

    private static double RealtimeSeconds()
    {
        return Time.realtimeSinceStartupAsDouble;
    }
}
