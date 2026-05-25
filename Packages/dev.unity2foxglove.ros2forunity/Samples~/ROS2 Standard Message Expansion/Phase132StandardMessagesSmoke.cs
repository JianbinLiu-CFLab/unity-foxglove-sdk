// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Coordinates Phase132 standard ROS2 message sample publishers.

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Standard Messages Smoke")]
public sealed class Phase132StandardMessagesSmoke : MonoBehaviour
{
    private const string LogPrefix = "[Phase132StandardMessagesSmoke]";
    private const string NodeName = "unity2foxglove_phase132_standard_messages";
    private const float DefaultPublishIntervalSeconds = 0.5f;

    [Header("Topics")]
    [SerializeField] private string _cameraInfoTopic = "/camera/camera_info";
    [SerializeField] private string _imageTopic = "/camera/image_raw";
    [SerializeField] private string _imuTopic = "/imu/data";
    [SerializeField] private string _odometryTopic = "/odom";
    [SerializeField] private string _poseTopic = "/pose";
    [SerializeField] private string _navSatFixTopic = "/fix";

    [Header("Sources")]
    [SerializeField] private bool _publishCamera = true;
    [SerializeField] private bool _publishImu = true;
    [SerializeField] private bool _publishOdometry = true;
    [SerializeField] private bool _publishPose = true;
    [SerializeField] private bool _publishNavSatFix = true;
    [SerializeField] private Phase132StandardCameraSource _cameraSource;
    [SerializeField] private Phase132StandardImuSource _imuSource;
    [SerializeField] private Phase132StandardOdometrySource _odometrySource;
    [SerializeField] private Phase132StandardPoseSource _poseSource;
    [SerializeField] private Phase132StandardNavSatFixSource _navSatFixSource;

    [Header("Publish")]
    [SerializeField, Min(0.2f)] private float _publishIntervalSeconds = DefaultPublishIntervalSeconds;

    [Header("Runtime Evidence")]
    [SerializeField] private string _runtimeRoot = string.Empty;
    [SerializeField] private bool _runtimeRootIsPackage;
    [SerializeField] private bool _assetRuntimePresent;

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Not started.";
    [SerializeField] private int _enabledSourceCount;
    [SerializeField] private int _publishedCameraInfoCount;
    [SerializeField] private int _publishedImageCount;
    [SerializeField] private int _publishedImuCount;
    [SerializeField] private int _publishedOdometryCount;
    [SerializeField] private int _publishedPoseCount;
    [SerializeField] private int _publishedNavSatFixCount;
    [SerializeField] private string _lastError = string.Empty;

    private float _nextPublishAt;
    private bool _warnedMissingDefine;
    private bool _runtimeRootLogged;
    private bool _endpointsLogged;
    private bool _firstPublishLogged;
    private bool _initializationBlocked;
    private bool _warnedMissingStartExecutor;
    private int _frameIndex;
    private double _realtimeStartSeconds;
    private long _unixStartSeconds;
    private double _lastStampSeconds;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _node;
    private IPublisher<sensor_msgs.msg.CameraInfo> _cameraInfoPublisher;
    private IPublisher<sensor_msgs.msg.Image> _imagePublisher;
    private IPublisher<sensor_msgs.msg.Imu> _imuPublisher;
    private IPublisher<nav_msgs.msg.Odometry> _odometryPublisher;
    private IPublisher<geometry_msgs.msg.PoseStamped> _posePublisher;
    private IPublisher<sensor_msgs.msg.NavSatFix> _navSatFixPublisher;
    private MethodInfo _startExecutor;
    private bool _ownsRos2UnityComponent;
    private bool _executorStarted;
#endif

    private void OnEnable()
    {
        Application.runInBackground = true;
        _nextPublishAt = 0f;
        _enabledSourceCount = 0;
        _publishedCameraInfoCount = 0;
        _publishedImageCount = 0;
        _publishedImuCount = 0;
        _publishedOdometryCount = 0;
        _publishedPoseCount = 0;
        _publishedNavSatFixCount = 0;
        _lastError = string.Empty;
        _statusMessage = "Starting Phase132 standard message smoke.";
        _runtimeRoot = string.Empty;
        _runtimeRootIsPackage = false;
        _assetRuntimePresent = false;
        _runtimeRootLogged = false;
        _endpointsLogged = false;
        _firstPublishLogged = false;
        _initializationBlocked = false;
        _warnedMissingStartExecutor = false;
        _frameIndex = 0;
        _unixStartSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _realtimeStartSeconds = RealtimeSeconds();
        _lastStampSeconds = 0d;

        EnsureSources();
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
        EnsureSources();
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
        _publishIntervalSeconds = Mathf.Max(0.2f, _publishIntervalSeconds);
        _cameraInfoTopic = CleanTopic(_cameraInfoTopic, "/camera/camera_info");
        _imageTopic = CleanTopic(_imageTopic, "/camera/image_raw");
        _imuTopic = CleanTopic(_imuTopic, "/imu/data");
        _odometryTopic = CleanTopic(_odometryTopic, "/odom");
        _poseTopic = CleanTopic(_poseTopic, "/pose");
        _navSatFixTopic = CleanTopic(_navSatFixTopic, "/fix");
        _enabledSourceCount = CountEnabledSources();
    }

    private void EnsureSources()
    {
        if (_publishCamera && _cameraSource == null)
            _cameraSource = GetOrAddSource<Phase132StandardCameraSource>();
        if (_publishImu && _imuSource == null)
            _imuSource = GetOrAddSource<Phase132StandardImuSource>();
        if (_publishOdometry && _odometrySource == null)
            _odometrySource = GetOrAddSource<Phase132StandardOdometrySource>();
        if (_publishPose && _poseSource == null)
            _poseSource = GetOrAddSource<Phase132StandardPoseSource>();
        if (_publishNavSatFix && _navSatFixSource == null)
            _navSatFixSource = GetOrAddSource<Phase132StandardNavSatFixSource>();

        _enabledSourceCount = CountEnabledSources();
    }

    private T GetOrAddSource<T>() where T : Component
    {
        var component = GetComponent<T>();
        return component != null ? component : gameObject.AddComponent<T>();
    }

    private int CountEnabledSources()
    {
        var count = 0;
        if (_publishCamera && _cameraSource != null)
            count++;
        if (_publishImu && _imuSource != null)
            count++;
        if (_publishOdometry && _odometrySource != null)
            count++;
        if (_publishPose && _poseSource != null)
            count++;
        if (_publishNavSatFix && _navSatFixSource != null)
            count++;
        return count;
    }

    private static string CleanTopic(string value, string fallback)
    {
        var topic = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return topic.StartsWith("/", StringComparison.Ordinal) ? topic : "/" + topic;
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
                _statusMessage = "ROS2 For Unity is ready for Phase132 standard messages.";
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
        if (_node == null)
            _node = _ros2Unity.CreateNode(NodeName);

        if (_publishCamera)
        {
            if (_cameraInfoPublisher == null)
                _cameraInfoPublisher = _node.CreatePublisher<sensor_msgs.msg.CameraInfo>(_cameraInfoTopic);
            if (_imagePublisher == null)
                _imagePublisher = _node.CreatePublisher<sensor_msgs.msg.Image>(_imageTopic);
        }

        if (_publishImu && _imuPublisher == null)
            _imuPublisher = _node.CreatePublisher<sensor_msgs.msg.Imu>(_imuTopic);
        if (_publishOdometry && _odometryPublisher == null)
            _odometryPublisher = _node.CreatePublisher<nav_msgs.msg.Odometry>(_odometryTopic);
        if (_publishPose && _posePublisher == null)
            _posePublisher = _node.CreatePublisher<geometry_msgs.msg.PoseStamped>(_poseTopic);
        if (_publishNavSatFix && _navSatFixPublisher == null)
            _navSatFixPublisher = _node.CreatePublisher<sensor_msgs.msg.NavSatFix>(_navSatFixTopic);

        if (!_endpointsLogged)
        {
            _endpointsLogged = true;
            _statusMessage = "Phase132 standard message endpoints ready.";
            Debug.Log(LogPrefix + " READY node=" + NodeName
                + " camera_info=" + _cameraInfoTopic
                + " image=" + _imageTopic
                + " imu=" + _imuTopic
                + " odom=" + _odometryTopic
                + " pose=" + _poseTopic
                + " fix=" + _navSatFixTopic);
        }
    }

    private void PublishIfDue()
    {
        if (Time.unscaledTime < _nextPublishAt)
            return;

        _nextPublishAt = Time.unscaledTime + _publishIntervalSeconds;
        CreateStamp(out var sec, out var nanosec);

        try
        {
            if (_publishCamera && _cameraSource != null && _cameraInfoPublisher != null && _imagePublisher != null)
            {
                _cameraInfoPublisher.Publish(_cameraSource.CreateCameraInfo(sec, nanosec));
                _cameraSource.RecordCameraInfoPublished();
                _publishedCameraInfoCount++;

                _imagePublisher.Publish(_cameraSource.CreateImage(sec, nanosec, _frameIndex));
                _cameraSource.RecordImagePublished();
                _publishedImageCount++;
            }

            if (_publishImu && _imuSource != null && _imuPublisher != null)
            {
                _imuPublisher.Publish(_imuSource.CreateImu(sec, nanosec));
                _imuSource.RecordPublished();
                _publishedImuCount++;
            }

            if (_publishOdometry && _odometrySource != null && _odometryPublisher != null)
            {
                _odometryPublisher.Publish(_odometrySource.CreateOdometry(sec, nanosec));
                _odometrySource.RecordPublished();
                _publishedOdometryCount++;
            }

            if (_publishPose && _poseSource != null && _posePublisher != null)
            {
                _posePublisher.Publish(_poseSource.CreatePoseStamped(sec, nanosec));
                _poseSource.RecordPublished();
                _publishedPoseCount++;
            }

            if (_publishNavSatFix && _navSatFixSource != null && _navSatFixPublisher != null)
            {
                _navSatFixPublisher.Publish(_navSatFixSource.CreateNavSatFix(sec, nanosec));
                _navSatFixSource.RecordPublished();
                _publishedNavSatFixCount++;
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.GetType().Name + ": " + ex.Message;
            _statusMessage = _lastError;
            Debug.LogWarning(LogPrefix + " publish failed: " + _lastError);
            return;
        }

        _frameIndex++;
        _lastError = string.Empty;
        _statusMessage = "Published standard messages set=" + _frameIndex + " sources=" + _enabledSourceCount + ".";

        if (!_firstPublishLogged)
        {
            _firstPublishLogged = true;
            Debug.Log(LogPrefix + " first publish: camera_info=" + _cameraInfoTopic
                + " image=" + _imageTopic
                + " imu=" + _imuTopic
                + " odom=" + _odometryTopic
                + " pose=" + _poseTopic
                + " fix=" + _navSatFixTopic);
        }
    }

    private void CreateStamp(out int sec, out uint nanosec)
    {
        var elapsed = Math.Max(0d, RealtimeSeconds() - _realtimeStartSeconds);
        var totalSeconds = _unixStartSeconds + elapsed;
        // Keep sample stamps monotonic even if the editor clock jitters while entering or leaving Play Mode.
        if (totalSeconds <= _lastStampSeconds)
            totalSeconds = _lastStampSeconds + 0.000001d;
        _lastStampSeconds = totalSeconds;

        var wholeSeconds = Math.Floor(totalSeconds);
        var fractional = totalSeconds - wholeSeconds;

        // ROS2 builtin_interfaces/Time.sec is int32; this smoke follows that ROS2/Y2038 wire limit.
        sec = checked((int)wholeSeconds);
        nanosec = (uint)Math.Min(999999999d, Math.Max(0d, Math.Round(fractional * 1000000000d)));
        if (nanosec == 1000000000u)
        {
            sec = checked(sec + 1);
            nanosec = 0u;
        }
    }

    private static double RealtimeSeconds()
    {
        return Time.realtimeSinceStartupAsDouble;
    }

    private void CleanupRuntime()
    {
        RemovePublisherIfPresent(_cameraInfoPublisher);
        RemovePublisherIfPresent(_imagePublisher);
        RemovePublisherIfPresent(_imuPublisher);
        RemovePublisherIfPresent(_odometryPublisher);
        RemovePublisherIfPresent(_posePublisher);
        RemovePublisherIfPresent(_navSatFixPublisher);

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

        _cameraInfoPublisher = null;
        _imagePublisher = null;
        _imuPublisher = null;
        _odometryPublisher = null;
        _posePublisher = null;
        _navSatFixPublisher = null;
        _node = null;
        _executorStarted = false;

        if (_ownsRos2UnityComponent && _ros2Unity != null)
            Destroy(_ros2Unity);
        _ros2Unity = null;
        _ownsRos2UnityComponent = false;
    }

    private void RemovePublisherIfPresent<T>(IPublisher<T> publisher)
    {
        if (_node == null || publisher == null)
            return;

        try
        {
            _node.RemovePublisher<T>(publisher);
        }
        catch (Exception)
        {
        }
    }
#endif

    private void LogRuntimeRootOnce()
    {
        if (_runtimeRootLogged)
            return;

        _runtimeRootLogged = true;
        var root = FindRuntimeRoot();
        _runtimeRoot = root ?? string.Empty;
        _runtimeRootIsPackage = !string.IsNullOrEmpty(root)
            && root.Replace('\\', '/').Contains("Packages/dev.unity2foxglove.ros2forunity.runtime.", StringComparison.OrdinalIgnoreCase);
        _assetRuntimePresent = Directory.Exists(Path.Combine(Application.dataPath, "Ros2ForUnity"));

        Debug.Log(LogPrefix + " RUNTIME_ROOT=" + _runtimeRoot);
        Debug.Log(LogPrefix + " RUNTIME_ROOT_IS_PACKAGE=" + _runtimeRootIsPackage);
        Debug.Log(LogPrefix + " ASSET_RUNTIME_PRESENT=" + _assetRuntimePresent);
    }

    private static string FindRuntimeRoot()
    {
        var projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null)
            return string.Empty;

        var packagesRoot = Path.Combine(projectRoot.FullName, "Packages");
        if (!Directory.Exists(packagesRoot))
            return string.Empty;

        var candidates = Directory.GetDirectories(packagesRoot, "dev.unity2foxglove.ros2forunity.runtime.*");
        if (candidates.Length == 0)
            return string.Empty;

        Array.Sort(candidates, StringComparer.OrdinalIgnoreCase);
        return candidates[candidates.Length - 1];
    }
}

internal static class Phase132StandardMessagesCommon
{
    public static string CleanFrameId(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public static void ValidateFixedArrayLength(Array values, int expectedLength, string label)
    {
        if (values == null || values.Length != expectedLength)
            throw new InvalidOperationException(label + " must contain exactly " + expectedLength + " values.");
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    public static std_msgs.msg.Header CreateHeader(string frameId, int sec, uint nanosec)
    {
        return new std_msgs.msg.Header
        {
            Stamp = new builtin_interfaces.msg.Time
            {
                Sec = sec,
                Nanosec = nanosec
            },
            Frame_id = frameId
        };
    }

    public static geometry_msgs.msg.Point CreatePoint(Vector3 position)
    {
        return new geometry_msgs.msg.Point
        {
            X = position.x,
            Y = position.y,
            Z = position.z
        };
    }

    public static geometry_msgs.msg.Vector3 CreateVector3(Vector3 vector)
    {
        return new geometry_msgs.msg.Vector3
        {
            X = vector.x,
            Y = vector.y,
            Z = vector.z
        };
    }

    public static geometry_msgs.msg.Quaternion IdentityRotation()
    {
        return new geometry_msgs.msg.Quaternion
        {
            X = 0.0,
            Y = 0.0,
            Z = 0.0,
            W = 1.0
        };
    }

    public static geometry_msgs.msg.Pose CreatePose(Vector3 position)
    {
        return new geometry_msgs.msg.Pose
        {
            Position = CreatePoint(position),
            Orientation = IdentityRotation()
        };
    }
#endif
}
