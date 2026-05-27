// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Phase127 ROS2 For Unity real-project runtime smoke.

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase127 R2FU Real Project Smoke")]
public sealed class Phase127R2FURealProjectSmoke : MonoBehaviour
{
    private const string LogPrefix = "[Phase127R2FURealProjectSmoke]";
    private const string NodeName = "unity2foxglove_phase127";
    private const string OutTopic = "/unity2foxglove/phase127/out";
    private const string InTopic = "/unity2foxglove/phase127/in";
    private const float PublishIntervalSeconds = 0.5f;
    private const float HoldSeconds = 120f;
    private const float ReadyTimeoutSeconds = 20f;

    [Header("ROS2")]
    [SerializeField] private string _nodeName = NodeName;
    [SerializeField] private string _outTopic = OutTopic;
    [SerializeField] private string _inTopic = InTopic;

    [Header("Publish")]
    [SerializeField] private bool _enablePublisher = true;
    [SerializeField, Min(0.1f)] private float _publishIntervalSeconds = PublishIntervalSeconds;

    [Header("Subscribe")]
    [SerializeField] private bool _enableSubscription = true;
    [SerializeField, Min(1)] private int _minInboundCount = 1;

    [Header("Runtime Evidence")]
    [SerializeField] private bool _initialPathClean;
    [SerializeField] private bool _runtimeRootIsPackage;
    [SerializeField] private bool _assetRuntimePresent;
    [SerializeField] private string _runtimeRoot = string.Empty;

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Not started.";
    [SerializeField] private int _publishedCount;
    [SerializeField] private int _receivedCount;
    [SerializeField] private string _lastReceived = string.Empty;
    [SerializeField] private string _lastError = string.Empty;

    private readonly object _receiveGate = new object();
    private float _nextPublishAt;
    private bool _warnedMissingDefine;
    private bool _runtimeRootLogged;
    private bool _endpointsLogged;
    private bool _firstPublishLogged;
    private bool _greenLogged;
    private int _loggedReceivedCount;
    private bool _previousRunInBackground;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private ROS2UnityComponent _ros2Unity;
    private ROS2Node _node;
    private IPublisher<std_msgs.msg.String> _publisher;
    private ISubscription<std_msgs.msg.String> _subscription;
    private MethodInfo _startExecutor;
    private bool _ownsRos2UnityComponent;
    private bool _executorStarted;
    private bool _initializationBlocked;
#endif

    private void OnEnable()
    {
        _previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        _nextPublishAt = 0f;
        _publishedCount = 0;
        _receivedCount = 0;
        _loggedReceivedCount = 0;
        _lastReceived = string.Empty;
        _lastError = string.Empty;
        _statusMessage = "Starting Phase127 R2FU real-project smoke.";
        _runtimeRootLogged = false;
        _endpointsLogged = false;
        _firstPublishLogged = false;
        _greenLogged = false;
        _initialPathClean = !ContainsMachineRosPath(Environment.GetEnvironmentVariable("PATH") ?? string.Empty);

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
        DrainReceived();
        PublishIfDue();
        UpdateGreenStatus();
#else
        WarnMissingDefine();
#endif
    }

    private void OnDisable()
    {
        Application.runInBackground = _previousRunInBackground;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        CleanupManualRuntime();
#endif
    }

    private void OnDestroy()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        CleanupManualRuntime();
#endif
    }

    private void OnValidate()
    {
        _publishIntervalSeconds = Mathf.Max(0.1f, _publishIntervalSeconds);
        _minInboundCount = Mathf.Max(1, _minInboundCount);
        if (string.IsNullOrWhiteSpace(_nodeName))
            _nodeName = NodeName;
        if (string.IsNullOrWhiteSpace(_outTopic))
            _outTopic = OutTopic;
        if (string.IsNullOrWhiteSpace(_inTopic))
            _inTopic = InTopic;
    }

    private void WarnMissingDefine()
    {
        _statusMessage = "Import ROS2 For Unity and add UNITY2FOXGLOVE_ROS2_FOR_UNITY before running Phase127.";
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

        if (!_initialPathClean)
        {
            _initializationBlocked = true;
            _lastError = "Unity inherited machine ROS2 PATH entries before runtime initialization.";
            _statusMessage = _lastError;
            Debug.LogError(LogPrefix + " " + _lastError);
            return false;
        }

        if (!_runtimeRootIsPackage)
        {
            _initializationBlocked = true;
            _lastError = "ROS2 For Unity runtime root is not the package runtime path.";
            _statusMessage = _lastError;
            Debug.LogError(LogPrefix + " " + _lastError + " root=" + _runtimeRoot);
            return false;
        }

        try
        {
            EnsureRos2UnityComponent();
            if (_ros2Unity != null && _ros2Unity.Ok())
                return true;

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

    private void LogRuntimeRootOnce()
    {
        if (_runtimeRootLogged)
            return;

        _runtimeRootLogged = true;
        _runtimeRoot = ResolveRuntimeRoot();
        _runtimeRootIsPackage = IsPackageRuntimeRoot(_runtimeRoot);
        _assetRuntimePresent = Directory.Exists(Path.Combine(Application.dataPath, "Ros2ForUnity"));
        Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_MANUAL_RUNTIME_ROOT=" + _runtimeRoot);
        Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_MANUAL_RUNTIME_ROOT_IS_PACKAGE=" + _runtimeRootIsPackage);
        Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_MANUAL_ASSET_RUNTIME_PRESENT=" + _assetRuntimePresent);
        Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_MANUAL_INITIAL_PATH_CLEAN=" + _initialPathClean);
    }

    private void EnsureExecutorStarted()
    {
        if (_executorStarted)
            return;

        _startExecutor?.Invoke(_ros2Unity, null);
        _executorStarted = true;
    }

    private void EnsureEndpoints()
    {
        if ((_publisher != null || !_enablePublisher) && (_subscription != null || !_enableSubscription))
            return;

        if (_node == null)
            _node = _ros2Unity.CreateNode(_nodeName);
        if (_enableSubscription && _subscription == null)
            _subscription = _node.CreateSubscription<std_msgs.msg.String>(_inTopic, QueueReceived);
        if (_enablePublisher && _publisher == null)
            _publisher = _node.CreatePublisher<std_msgs.msg.String>(_outTopic);

        if (!_endpointsLogged)
        {
            _endpointsLogged = true;
            _statusMessage = "Phase127 R2FU real-project smoke is ready.";
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_MANUAL_READY"
                      + " node=" + _nodeName
                      + " outTopic=" + _outTopic
                      + " inTopic=" + _inTopic
                      + " publisher=" + (_publisher != null)
                      + " subscription=" + (_subscription != null));
        }
    }

    private void PublishIfDue()
    {
        if (!_enablePublisher || _publisher == null)
            return;

        if (Time.unscaledTime < _nextPublishAt)
            return;

        _nextPublishAt = Time.unscaledTime + _publishIntervalSeconds;
        _publishedCount++;
        var message = new std_msgs.msg.String
        {
            Data = "phase127 unity smoke " + _publishedCount
        };
        _publisher.Publish(message);
        _statusMessage = "Published " + _publishedCount + " Phase127 message(s).";

        if (!_firstPublishLogged)
        {
            _firstPublishLogged = true;
            Debug.Log(LogPrefix + " first publish: " + message.Data);
        }
    }

    private void QueueReceived(std_msgs.msg.String message)
    {
        lock (_receiveGate)
        {
            _receivedCount++;
            _lastReceived = message.Data;
        }
    }

    private void DrainReceived()
    {
        string received = null;
        int count;
        lock (_receiveGate)
        {
            if (_receivedCount == _loggedReceivedCount)
                return;

            count = _receivedCount;
            received = _lastReceived;
            _loggedReceivedCount = _receivedCount;
        }

        _statusMessage = "Received " + count + " Phase127 message(s).";
        Debug.Log(LogPrefix + " received: " + received);
    }

    private void UpdateGreenStatus()
    {
        var received = SnapshotReceivedCount();
        if (_greenLogged || _publishedCount < 1 || received < _minInboundCount)
            return;

        _greenLogged = true;
        _statusMessage = "GREEN: published=" + _publishedCount + " received=" + received;
        Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_MANUAL_GREEN"
                  + " published=" + _publishedCount
                  + " received=" + received
                  + " minInbound=" + _minInboundCount);
    }

    private int SnapshotReceivedCount()
    {
        lock (_receiveGate)
            return _receivedCount;
    }

    private void CleanupManualRuntime()
    {
        if (_node != null && _subscription != null)
        {
            try
            {
                _node.RemoveSubscription<std_msgs.msg.String>(_subscription);
            }
            catch (Exception)
            {
            }
        }

        if (_node != null && _publisher != null)
        {
            try
            {
                _node.RemovePublisher<std_msgs.msg.String>(_publisher);
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

        _subscription = null;
        _publisher = null;
        _node = null;

        if (_ownsRos2UnityComponent && _ros2Unity != null)
            Destroy(_ros2Unity);

        _ros2Unity = null;
        _ownsRos2UnityComponent = false;
        _executorStarted = false;
        _initializationBlocked = false;
    }

    private static string ResolveRuntimeRoot()
    {
        var type = typeof(ROS2UnityComponent).Assembly.GetType("ROS2.ROS2ForUnity");
        var method = type?.GetMethod(
            "GetRos2ForUnityPath",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as string ?? string.Empty;
    }
#endif

    private static bool IsPackageRuntimeRoot(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("/packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/runtime/ros2forunity")
               && !normalized.Contains("/unity2foxglove/assets/ros2forunity");
    }

    private static bool ContainsMachineRosPath(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.Contains("ros2_jazzy")
               || normalized.Contains("ros2-windows")
               || normalized.Contains("/.pixi/envs/default");
    }

    private static string NormalizePath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
    }

#if UNITY_EDITOR
    public static void RunBatch()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        BatchRunner.Start();
#else
        Debug.LogError(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_SMOKE_FAIL missing UNITY2FOXGLOVE_ROS2_FOR_UNITY");
        EditorApplication.Exit(1);
#endif
    }
#endif

#if UNITY_EDITOR && UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private sealed class BatchRunner
    {
        private readonly GameObject _host;
        private readonly ROS2UnityComponent _ros2Unity;
        private readonly string _initialPath;
        private readonly bool _requireInbound;
        private readonly object _receiveGate = new object();
        private readonly float _holdSeconds;
        private readonly float _inboundTimeoutSeconds;
        private readonly int _minInboundCount;
        private readonly float _startedAt;
        private readonly float _deadlineAt;

        private ROS2Node _node;
        private IPublisher<std_msgs.msg.String> _publisher;
        private ISubscription<std_msgs.msg.String> _subscription;
        private string _lastReceived = string.Empty;
        private float _nextPublishAt;
        private int _published;
        private int _received;
        private int _loggedReceived;
        private bool _readyLogged;
        private bool _endpointsLogged;
        private bool _executorStarted;
        private bool _runtimeRootLogged;
        private bool _completed;
        private float _inboundDeadlineAt;
        private bool _previousRunInBackground;

        private BatchRunner()
        {
            _previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            _initialPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            _requireInbound = string.Equals(
                Environment.GetEnvironmentVariable("UNITY2FOXGLOVE_R2FU_REQUIRE_INBOUND"),
                "1",
                StringComparison.Ordinal);
            _holdSeconds = ReadPositiveFloat("UNITY2FOXGLOVE_R2FU_HOLD_SECONDS", HoldSeconds);
            _inboundTimeoutSeconds = ReadPositiveFloat(
                "UNITY2FOXGLOVE_R2FU_INBOUND_TIMEOUT_SECONDS",
                ReadyTimeoutSeconds);
            _minInboundCount = ReadPositiveInt("UNITY2FOXGLOVE_R2FU_MIN_INBOUND_COUNT", 1);
            _startedAt = Time.realtimeSinceStartup;
            _deadlineAt = _startedAt + ReadyTimeoutSeconds;
            _inboundDeadlineAt = float.PositiveInfinity;
            _host = new GameObject("Phase127_R2FU_RuntimeSmoke");
            _host.hideFlags = HideFlags.HideAndDontSave;
            _ros2Unity = _host.AddComponent<ROS2UnityComponent>();
        }

        public static void Start()
        {
            var runner = new BatchRunner();
            Debug.Log(LogPrefix + " START node=" + NodeName + " outTopic=" + OutTopic + " inTopic=" + InTopic);
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_REQUIRE_INBOUND=" + runner._requireInbound);
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_HOLD_SECONDS=" + runner._holdSeconds.ToString("0.0"));
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_INBOUND_TIMEOUT_SECONDS="
                      + runner._inboundTimeoutSeconds.ToString("0.0"));
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_MIN_INBOUND_COUNT=" + runner._minInboundCount);
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_INITIAL_PATH_CLEAN=" + (!ContainsMachineRosPath(runner._initialPath)));
            if (ContainsMachineRosPath(runner._initialPath))
            {
                runner.Fail("PATH contains machine ROS2 entries before runtime initialization.");
                return;
            }

            EditorApplication.update += runner.Tick;
        }

        private void Tick()
        {
            if (_completed)
                return;

            try
            {
                LogRuntimeRootOnce();

                if (!_ros2Unity.Ok())
                {
                    if (Time.realtimeSinceStartup > _deadlineAt)
                        Fail("ROS2 For Unity did not become ready before timeout.");
                    return;
                }

                EnsureExecutorStarted();
                EnsureEndpoints();
                DrainReceived();
                PublishIfDue();

                if (!_readyLogged && _published > 0 && _publisher != null && _subscription != null)
                {
                    _readyLogged = true;
                    Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_SMOKE_READY"
                              + " node=" + NodeName
                              + " outTopic=" + OutTopic
                              + " inTopic=" + InTopic);
                }

                var received = SnapshotReceivedCount();

                if (_requireInbound && Time.realtimeSinceStartup > _inboundDeadlineAt && received < _minInboundCount)
                    Fail("Inbound ROS2 message was not received before timeout.");

                if (Time.realtimeSinceStartup - _startedAt >= _holdSeconds
                    && _published >= 3
                    && (!_requireInbound || received >= _minInboundCount))
                    Pass();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void LogRuntimeRootOnce()
        {
            if (_runtimeRootLogged)
                return;

            _runtimeRootLogged = true;
            var runtimeRoot = ResolveRuntimeRoot();
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_ROOT=" + runtimeRoot);
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_ROOT_IS_PACKAGE=" + IsPackageRuntimeRoot(runtimeRoot));
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_ASSET_RUNTIME_PRESENT="
                      + Directory.Exists(Path.Combine(Application.dataPath, "Ros2ForUnity")));

            if (!IsPackageRuntimeRoot(runtimeRoot))
                Fail("ROS2 For Unity runtime root is not the package runtime path.");
        }

        private void EnsureEndpoints()
        {
            if (_publisher != null && _subscription != null)
                return;

            if (_node == null)
                _node = _ros2Unity.CreateNode(NodeName);
            if (_subscription == null)
                _subscription = _node.CreateSubscription<std_msgs.msg.String>(InTopic, QueueReceived);
            if (_publisher == null)
                _publisher = _node.CreatePublisher<std_msgs.msg.String>(OutTopic);

            if (!_endpointsLogged && _publisher != null && _subscription != null)
            {
                _endpointsLogged = true;
                if (_requireInbound)
                    _inboundDeadlineAt = Time.realtimeSinceStartup + _inboundTimeoutSeconds;
                Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_ENDPOINTS_READY"
                          + " publisher=True"
                          + " subscription=True"
                          + " subscriptionType=" + _subscription.GetType().FullName
                          + " inboundDeadlineSeconds=" + _inboundTimeoutSeconds.ToString("0.0")
                          + DescribeNativeEntityState());
            }
        }

        private void EnsureExecutorStarted()
        {
            if (_executorStarted)
                return;

            var method = typeof(ROS2UnityComponent).GetMethod(
                "StartExecutor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(_ros2Unity, null);
            _executorStarted = true;
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_EXECUTOR_STARTED=True");
        }

        private void PublishIfDue()
        {
            if (_publisher == null || Time.realtimeSinceStartup < _nextPublishAt)
                return;

            _nextPublishAt = Time.realtimeSinceStartup + PublishIntervalSeconds;
            _published++;
            _publisher.Publish(new std_msgs.msg.String
            {
                Data = "phase127 unity smoke " + _published
            });
        }

        private void QueueReceived(std_msgs.msg.String message)
        {
            lock (_receiveGate)
            {
                _received++;
                _lastReceived = message.Data;
            }
        }

        private void DrainReceived()
        {
            string received = null;
            int count = 0;
            lock (_receiveGate)
            {
                if (_received == 0 || _received == _loggedReceived)
                    return;

                count = _received;
                received = _lastReceived;
                _loggedReceived = _received;
            }

            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_INBOUND_RECEIVED"
                      + " count=" + count
                      + " data=" + received);
        }

        private void Pass()
        {
            var received = SnapshotReceivedCount();
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_SMOKE_GREEN"
                      + " published=" + _published
                      + " received=" + received
                      + " minInbound=" + _minInboundCount
                      + " heldSeconds=" + (Time.realtimeSinceStartup - _startedAt).ToString("0.0"));
            Complete(0);
        }

        private void Fail(string message)
        {
            var received = SnapshotReceivedCount();
            Debug.LogError(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_SMOKE_FAIL "
                           + message
                           + " published=" + _published
                           + " received=" + received
                           + " minInbound=" + _minInboundCount
                           + DescribeNativeEntityState());
            Complete(1);
        }

        private void Complete(int exitCode)
        {
            if (_completed)
                return;

            _completed = true;
            EditorApplication.update -= Tick;
            Application.runInBackground = _previousRunInBackground;
            Cleanup();
            EditorApplication.Exit(exitCode);
        }

        private void Cleanup()
        {
            if (_node != null && _subscription != null)
            {
                try
                {
                    _node.RemoveSubscription<std_msgs.msg.String>(_subscription);
                }
                catch (Exception)
                {
                }
            }

            if (_node != null && _publisher != null)
            {
                try
                {
                    _node.RemovePublisher<std_msgs.msg.String>(_publisher);
                }
                catch (Exception)
                {
                }
            }

            if (_node != null)
            {
                try
                {
                    _ros2Unity.RemoveNode(_node);
                }
                catch (Exception)
                {
                }
            }

            _subscription = null;
            _publisher = null;
            _node = null;

            if (_host != null)
                UnityEngine.Object.DestroyImmediate(_host);
        }

        private static string ResolveRuntimeRoot()
        {
            var type = typeof(ROS2UnityComponent).Assembly.GetType("ROS2.ROS2ForUnity");
            var method = type?.GetMethod(
                "GetRos2ForUnityPath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return method?.Invoke(null, null) as string ?? string.Empty;
        }

        private string DescribeNativeEntityState()
        {
            var ros2NodeField = typeof(ROS2Node).GetField(
                "node",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (ros2NodeField == null)
                return " nativeNodeDiagnostic=<unavailable:ROS2Node.node field missing>"
                       + " publisherCreated=" + (_publisher != null)
                       + " subscriptionCreated=" + (_subscription != null);

            var nativeNode = ros2NodeField?.GetValue(_node);
            return " nativeNodeType=" + (nativeNode?.GetType().FullName ?? "<null>")
                   + " nativeSubscriptions=" + CountCollectionProperty(nativeNode, "Subscriptions")
                   + " publisherCreated=" + (_publisher != null)
                   + " subscriptionCreated=" + (_subscription != null);
        }

        private int SnapshotReceivedCount()
        {
            lock (_receiveGate)
                return _received;
        }

        private static int CountCollectionProperty(object instance, string propertyName)
        {
            if (instance == null)
                return -1;

            var property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var value = property?.GetValue(instance, null);
            if (value is ICollection collection)
                return collection.Count;
            if (value is IEnumerable enumerable)
            {
                var count = 0;
                foreach (var _ in enumerable)
                    count++;
                return count;
            }

            return -1;
        }

        private static bool IsPackageRuntimeRoot(string path)
        {
            var normalized = NormalizePath(path);
            return normalized.Contains("/packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/runtime/ros2forunity")
                   && !normalized.Contains("/unity2foxglove/assets/ros2forunity");
        }

        private static bool ContainsMachineRosPath(string path)
        {
            var normalized = NormalizePath(path);
            return normalized.Contains("ros2_jazzy")
                   || normalized.Contains("ros2-windows")
                   || normalized.Contains("/.pixi/envs/default");
        }

        private static float ReadPositiveFloat(string name, float fallback)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (float.TryParse(raw, out var value) && value > 0f)
                return value;
            return fallback;
        }

        private static int ReadPositiveInt(string name, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (int.TryParse(raw, out var value) && value > 0)
                return value;
            return fallback;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
        }
    }
#endif
}
