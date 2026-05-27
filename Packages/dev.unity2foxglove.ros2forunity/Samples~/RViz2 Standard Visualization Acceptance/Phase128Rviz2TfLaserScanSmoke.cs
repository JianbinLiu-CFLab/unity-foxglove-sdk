// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Publishes Phase128 RViz2 TF and LaserScan acceptance topics.

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/RViz2 TF LaserScan Smoke")]
public sealed class Phase128Rviz2TfLaserScanSmoke : MonoBehaviour
{
    private const string LogPrefix = "[Phase128Rviz2TfLaserScanSmoke]";
    private const string NodeName = "unity2foxglove_phase128_rviz2";
    private const string TfTopic = "/tf";
    private const string ScanTopic = "/scan";
    private const string FrameMap = "map";
    private const string FrameBaseLink = "base_link";
    private const string FrameLaser = "laser";
    private const float DefaultPublishIntervalSeconds = 0.5f;
    private const int RangeCount = 181;

    [Header("Publish")]
    [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = DefaultPublishIntervalSeconds;

    [Header("Runtime Evidence")]
    [SerializeField] private string _runtimeRoot = string.Empty;
    [SerializeField] private bool _runtimeRootIsPackage;
    [SerializeField] private bool _assetRuntimePresent;

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Not started.";
    [SerializeField] private int _publishedTfCount;
    [SerializeField] private int _publishedScanCount;
    [SerializeField] private string _lastError = string.Empty;

    private float _nextPublishAt;
    private bool _warnedMissingDefine;
    private bool _runtimeRootLogged;
    private bool _endpointsLogged;
    private bool _firstPublishLogged;
    private bool _initializationBlocked;
    private float _nextRetryTime;
    private bool _warnedMissingStartExecutor;
    private double _realtimeStartSeconds;
    private long _unixStartSeconds;
    private double _lastStampSeconds;
    private float[] _ranges = new float[0];
    private float[] _intensities = new float[0];

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _node;
    private IPublisher<tf2_msgs.msg.TFMessage> _tfPublisher;
    private IPublisher<sensor_msgs.msg.LaserScan> _scanPublisher;
    private MethodInfo _startExecutor;
    private bool _ownsRos2UnityComponent;
    private bool _executorStarted;
#endif

    private void OnEnable()
    {
        // Acceptance samples keep Unity active while RViz2 and ROS2 CLI windows have focus.
        Application.runInBackground = true;
        _nextPublishAt = 0f;
        _publishedTfCount = 0;
        _publishedScanCount = 0;
        _lastError = string.Empty;
        _statusMessage = "Starting Phase128 RViz2 TF/LaserScan smoke.";
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
        InitializeScanBuffers();

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

        if (!TryEnsurePostReadySetup())
            return;
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

    private void InitializeScanBuffers()
    {
        if (_ranges.Length == RangeCount)
            return;

        _ranges = new float[RangeCount];
        _intensities = new float[0];
        for (var i = 0; i < _ranges.Length; i++)
        {
            var wave = Mathf.Sin(i * 0.11f);
            var value = 3.0f + (0.25f * wave);
            _ranges[i] = IsFiniteRange(value) ? value : 3.0f;
        }
    }

    private static bool IsFiniteRange(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
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

    private bool TryEnsurePostReadySetup()
    {
        if (_initializationBlocked && Time.time < _nextRetryTime)
            return false;

        _initializationBlocked = false;
        try
        {
            EnsureExecutorStarted();
            EnsureEndpoints();
            return true;
        }
        catch (Exception ex)
        {
            _initializationBlocked = true;
            _nextRetryTime = Time.time + 5f;
            _lastError = ex.GetType().Name + ": " + ex.Message;
            _statusMessage = _lastError;
            Debug.LogWarning(LogPrefix + " ROS2 For Unity endpoint setup failed: " + _lastError);
            return false;
        }
    }

    private bool TryEnsureReady()
    {
        if (_initializationBlocked && Time.time < _nextRetryTime)
            return false;

        _initializationBlocked = false;
        try
        {
            EnsureRos2UnityComponent();
            if (_ros2Unity != null && _ros2Unity.Ok())
            {
                _statusMessage = "ROS2 For Unity is ready for Phase128 RViz2 acceptance.";
                _lastError = string.Empty;
                return true;
            }

            _statusMessage = "Waiting for ROS2 For Unity runtime.";
            return false;
        }
        catch (Exception ex)
        {
            _initializationBlocked = true;
            _nextRetryTime = Time.time + 5f;
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
                // R2FU has historically kept StartExecutor private; FixedUpdate may still start it.
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
        if (_tfPublisher != null && _scanPublisher != null)
            return;

        if (_node == null)
            _node = _ros2Unity.CreateNode(NodeName);
        if (_tfPublisher == null)
            _tfPublisher = _node.CreatePublisher<tf2_msgs.msg.TFMessage>(TfTopic);
        if (_scanPublisher == null)
            _scanPublisher = _node.CreatePublisher<sensor_msgs.msg.LaserScan>(ScanTopic);

        if (!_endpointsLogged && _tfPublisher != null && _scanPublisher != null)
        {
            _endpointsLogged = true;
            _statusMessage = "Phase128 RViz2 endpoints ready.";
            Debug.Log(LogPrefix + " READY node=" + NodeName + " tf=" + TfTopic + " scan=" + ScanTopic);
        }
    }

    private void PublishIfDue()
    {
        if (_tfPublisher == null || _scanPublisher == null)
            return;

        if (Time.unscaledTime < _nextPublishAt)
            return;

        _nextPublishAt = Time.unscaledTime + _publishIntervalSeconds;
        CreateStamp(out var sec, out var nanosec);

        try
        {
            _tfPublisher.Publish(CreateTfMessage(sec, nanosec));
            _scanPublisher.Publish(CreateLaserScan(sec, nanosec));
        }
        catch (Exception ex)
        {
            _lastError = ex.GetType().Name + ": " + ex.Message;
            _statusMessage = _lastError;
            Debug.LogWarning(LogPrefix + " publish failed: " + _lastError);
            return;
        }

        _publishedTfCount++;
        _publishedScanCount++;
        _lastError = string.Empty;
        _statusMessage = "Published TF=" + _publishedTfCount + " scan=" + _publishedScanCount + ".";

        if (!_firstPublishLogged)
        {
            _firstPublishLogged = true;
            Debug.Log(LogPrefix + " first publish: frames=" + FrameMap + "," + FrameBaseLink + "," + FrameLaser);
        }
    }

    private tf2_msgs.msg.TFMessage CreateTfMessage(int sec, uint nanosec)
    {
        var phase = Mathf.Sin(Time.unscaledTime * 0.25f) * 0.25f;
        return new tf2_msgs.msg.TFMessage
        {
            Transforms = new[]
            {
                new geometry_msgs.msg.TransformStamped
                {
                    Header = CreateHeader(FrameMap, sec, nanosec),
                    Child_frame_id = FrameBaseLink,
                    Transform = new geometry_msgs.msg.Transform
                    {
                        Translation = new geometry_msgs.msg.Vector3
                        {
                            X = phase,
                            Y = 0.0,
                            Z = 0.0
                        },
                        Rotation = IdentityRotation()
                    }
                },
                new geometry_msgs.msg.TransformStamped
                {
                    Header = CreateHeader(FrameBaseLink, sec, nanosec),
                    Child_frame_id = FrameLaser,
                    Transform = new geometry_msgs.msg.Transform
                    {
                        Translation = new geometry_msgs.msg.Vector3
                        {
                            X = 0.25,
                            Y = 0.0,
                            Z = 0.15
                        },
                        Rotation = IdentityRotation()
                    }
                }
            }
        };
    }

    private sensor_msgs.msg.LaserScan CreateLaserScan(int sec, uint nanosec)
    {
        InitializeScanBuffers();
        return new sensor_msgs.msg.LaserScan
        {
            Header = CreateHeader(FrameLaser, sec, nanosec),
            Angle_min = -Mathf.PI * 0.5f,
            Angle_max = Mathf.PI * 0.5f,
            Angle_increment = Mathf.PI / (RangeCount - 1),
            Time_increment = 0f,
            Scan_time = _publishIntervalSeconds,
            Range_min = 0.1f,
            Range_max = 10.0f,
            Ranges = _ranges,
            Intensities = _intensities
        };
    }

    private static std_msgs.msg.Header CreateHeader(string frameId, int sec, uint nanosec)
    {
        var stamp = new builtin_interfaces.msg.Time
        {
            Sec = sec,
            Nanosec = nanosec
        };
        return new std_msgs.msg.Header
        {
            Stamp = stamp,
            Frame_id = frameId
        };
    }

    private static geometry_msgs.msg.Quaternion IdentityRotation()
    {
        return new geometry_msgs.msg.Quaternion
        {
            X = 0.0,
            Y = 0.0,
            Z = 0.0,
            W = 1.0
        };
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
        if (_node != null && _tfPublisher != null)
        {
            try
            {
                _node.RemovePublisher<tf2_msgs.msg.TFMessage>(_tfPublisher);
            }
            catch (Exception)
            {
            }
        }

        if (_node != null && _scanPublisher != null)
        {
            try
            {
                _node.RemovePublisher<sensor_msgs.msg.LaserScan>(_scanPublisher);
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

        _tfPublisher = null;
        _scanPublisher = null;
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
        return normalized.Contains("/packages/dev.unity2foxglove.ros2forunity.runtime.")
               && normalized.Contains("/runtime/ros2forunity")
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
