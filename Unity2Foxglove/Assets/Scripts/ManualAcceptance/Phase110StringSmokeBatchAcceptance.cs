// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Batchmode acceptance driver for the real Phase106Acceptance String Smoke component.

using System;
using System.IO;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

[DisallowMultipleComponent]
public sealed class Phase110StringSmokeBatchAcceptance : MonoBehaviour
{
    private const string LogPrefix = "[Phase110StringSmokeBatchAcceptance]";
    private const string ScenePath = "Assets/Scenes/Phase106Acceptance.unity";
    private const string NodeName = "unity2foxglove_phase110";
    private const string OutTopic = "/unity2foxglove/ros2forunity/string/out";
    private const string InTopic = "/unity2foxglove/ros2forunity/string/in";
    private const float ReadyTimeoutSeconds = 30f;
    private const float HoldSeconds = 60f;
    private const float InboundTimeoutSeconds = 90f;
    private const int MinInboundCount = 3;
    private const string DirectModeTrueToken = "DIRECT_MODE=True";
    private const string DirectModeFalseToken = "DIRECT_MODE=False";

#if UNITY_EDITOR
    public static void RunBatch()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        BatchRunner.Start();
#else
        Debug.LogError(LogPrefix + " UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_FAIL missing UNITY2FOXGLOVE_ROS2_FOR_UNITY");
        EditorApplication.Exit(1);
#endif
    }
#endif

#if UNITY_EDITOR && UNITY2FOXGLOVE_ROS2_FOR_UNITY
    private sealed class BatchRunner
    {
        private readonly bool _directMode;
        private readonly bool _requireInbound;
        private readonly int _minInboundCount;
        private readonly float _holdSeconds;
        private readonly float _readyDeadlineAt;
        private readonly float _startedAt;
        private readonly string _initialPath;

        private Phase110Ros2ForUnityStringSmoke _smoke;
        private MethodInfo _startExecutor;
        private bool _readyLogged;
        private bool _completed;
        private bool _runtimeLogged;
        private bool _inboundDeadlineArmed;
        private float _inboundDeadlineAt = float.PositiveInfinity;
        private bool _previousRunInBackground;
        private bool _executorsStarted;
        private bool _previousEnterPlayModeOptionsEnabled;
        private EnterPlayModeOptions _previousEnterPlayModeOptions;

        private BatchRunner()
        {
            _previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            _directMode = ReadBool("UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_DIRECT", false);
            _requireInbound = ReadBool("UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_REQUIRE_INBOUND", true);
            _minInboundCount = ReadPositiveInt(
                "UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_MIN_INBOUND_COUNT",
                MinInboundCount);
            _holdSeconds = ReadPositiveFloat(
                "UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_HOLD_SECONDS",
                HoldSeconds);
            _initialPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            _startedAt = Time.realtimeSinceStartup;
            _readyDeadlineAt = _startedAt + ReadyTimeoutSeconds;
        }

        public static void Start()
        {
            var runner = new BatchRunner();
            var modeToken = runner._directMode ? DirectModeTrueToken : DirectModeFalseToken;
            Debug.Log(LogPrefix + " START " + modeToken
                      + " scene=" + ScenePath
                      + " node=" + NodeName
                      + " outTopic=" + OutTopic
                      + " inTopic=" + InTopic);
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_INITIAL_PATH_CLEAN="
                      + (!ContainsMachineRosPath(runner._initialPath)));
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_REQUIRE_INBOUND="
                      + runner._requireInbound);
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_MIN_INBOUND_COUNT="
                      + runner._minInboundCount);

            if (ContainsMachineRosPath(runner._initialPath))
            {
                runner.Fail("Unity inherited machine ROS2 PATH entries before runtime initialization.");
                return;
            }

            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                runner.ConfigureSmoke();
                EditorApplication.update += runner.Tick;
                runner.EnterPlayMode();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                runner.Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ConfigureSmoke()
        {
            _smoke = UnityEngine.Object.FindFirstObjectByType<Phase110Ros2ForUnityStringSmoke>();
            if (_smoke == null)
                throw new InvalidOperationException("Could not find Phase110Ros2ForUnityStringSmoke in " + ScenePath);

            _smoke.ConfigureForBatch(
                _directMode,
                NodeName,
                OutTopic,
                InTopic,
                enablePublisher: true,
                enableSubscription: true,
                publishIntervalSeconds: 0.5f);

            _startExecutor = typeof(ROS2UnityComponent).GetMethod(
                "StartExecutor",
                BindingFlags.Instance | BindingFlags.NonPublic);

            _smoke.enabled = true;
        }

        private void EnterPlayMode()
        {
            _previousEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _previousEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions =
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorApplication.EnterPlaymode();
        }

        private void Tick()
        {
            if (_completed)
                return;

            try
            {
                if (!EditorApplication.isPlaying)
                    return;

                LogRuntimeRootOnce();
                EnsureExecutorsStarted();

                var published = _smoke.PublishedCount;
                var received = _smoke.ReceivedCount;
                var status = _smoke.StatusMessage;
                var lastError = _smoke.LastError;

                if (!_readyLogged && published > 0)
                {
                    _readyLogged = true;
                    if (_requireInbound)
                    {
                        _inboundDeadlineArmed = true;
                        _inboundDeadlineAt = Time.realtimeSinceStartup
                                             + ReadPositiveFloat(
                                                 "UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_INBOUND_TIMEOUT_SECONDS",
                                                 InboundTimeoutSeconds);
                    }

                    Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_READY"
                              + " mode=" + (_directMode ? "direct" : "facade")
                              + " node=" + NodeName
                              + " outTopic=" + OutTopic
                              + " inTopic=" + InTopic
                              + " status=" + status);
                }

                if (Time.realtimeSinceStartup > _readyDeadlineAt && !_readyLogged)
                    Fail("String Smoke did not publish before timeout. status=" + status + " lastError=" + lastError);

                if (_requireInbound
                    && _inboundDeadlineArmed
                    && Time.realtimeSinceStartup > _inboundDeadlineAt
                    && received < _minInboundCount)
                {
                    Fail("Inbound String Smoke messages were not received before timeout. status="
                         + status + " lastError=" + lastError);
                }

                if (Time.realtimeSinceStartup - _startedAt >= _holdSeconds
                    && published >= 3
                    && (!_requireInbound || received >= _minInboundCount))
                {
                    Pass(published, received, status);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void LogRuntimeRootOnce()
        {
            if (_runtimeLogged)
                return;

            _runtimeLogged = true;
            var runtimeRoot = ResolveRuntimeRoot();
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_RUNTIME_ROOT=" + runtimeRoot);
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_RUNTIME_ROOT_IS_PACKAGE="
                      + IsPackageRuntimeRoot(runtimeRoot));
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_ASSET_RUNTIME_PRESENT="
                      + Directory.Exists(Path.Combine(Application.dataPath, "Ros2ForUnity")));

            if (!IsPackageRuntimeRoot(runtimeRoot))
                Fail("ROS2 For Unity runtime root is not the package runtime path.");
        }

        private void EnsureExecutorsStarted()
        {
            if (_executorsStarted)
                return;

            if (_startExecutor == null)
            {
                _executorsStarted = true;
                return;
            }

            var components = UnityEngine.Object.FindObjectsByType<ROS2UnityComponent>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < components.Length; i++)
                _startExecutor.Invoke(components[i], null);
            _executorsStarted = true;
        }

        private void Pass(int published, int received, string status)
        {
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_GREEN"
                      + " mode=" + (_directMode ? "direct" : "facade")
                      + " published=" + published
                      + " received=" + received
                      + " minInbound=" + _minInboundCount
                      + " status=" + status);
            Complete(0);
        }

        private void Fail(string message)
        {
            Debug.LogError(LogPrefix + " UNITY2FOXGLOVE_PHASE110_STRING_SMOKE_FAIL "
                           + message
                           + " mode=" + (_directMode ? "direct" : "facade")
                           + " published=" + (_smoke == null ? -1 : _smoke.PublishedCount)
                           + " received=" + (_smoke == null ? -1 : _smoke.ReceivedCount)
                           + " status=" + (_smoke == null ? "<unavailable>" : _smoke.StatusMessage)
                           + " lastError=" + (_smoke == null ? "<unavailable>" : _smoke.LastError));
            Complete(1);
        }

        private void Complete(int exitCode)
        {
            if (_completed)
                return;

            _completed = true;
            EditorApplication.update -= Tick;
            Application.runInBackground = _previousRunInBackground;
            EditorSettings.enterPlayModeOptionsEnabled = _previousEnterPlayModeOptionsEnabled;
            EditorSettings.enterPlayModeOptions = _previousEnterPlayModeOptions;
            try
            {
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + " cleanup warning: " + ex.Message);
            }

            EditorApplication.Exit(exitCode);
        }

        private static string ResolveRuntimeRoot()
        {
            var type = typeof(ROS2UnityComponent).Assembly.GetType("ROS2.ROS2ForUnity");
            var method = type?.GetMethod(
                "GetRos2ForUnityPath",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return method?.Invoke(null, null) as string ?? string.Empty;
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

        private static bool ReadBool(string name, bool fallback)
        {
            var raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;
            return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
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
