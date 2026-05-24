// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Batchmode Phase127 ROS2 For Unity real-project runtime smoke.

using System;
using System.IO;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using ROS2;
#endif

public sealed class Phase127R2FURealProjectSmoke : MonoBehaviour
{
    private const string LogPrefix = "[Phase127R2FURealProjectSmoke]";
    private const string NodeName = "unity2foxglove_phase127";
    private const string OutTopic = "/unity2foxglove/phase127/out";
    private const float PublishIntervalSeconds = 0.5f;
    private const float HoldSeconds = 120f;
    private const float ReadyTimeoutSeconds = 20f;

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
        private readonly float _startedAt;
        private readonly float _deadlineAt;

        private ROS2Node _node;
        private IPublisher<std_msgs.msg.String> _publisher;
        private float _nextPublishAt;
        private int _published;
        private bool _readyLogged;
        private bool _executorStarted;
        private bool _runtimeRootLogged;
        private bool _completed;

        private BatchRunner()
        {
            Application.runInBackground = true;
            _initialPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            _startedAt = Time.realtimeSinceStartup;
            _deadlineAt = _startedAt + ReadyTimeoutSeconds;
            _host = new GameObject("Phase127_R2FU_RuntimeSmoke");
            _host.hideFlags = HideFlags.HideAndDontSave;
            _ros2Unity = _host.AddComponent<ROS2UnityComponent>();
        }

        public static void Start()
        {
            var runner = new BatchRunner();
            Debug.Log(LogPrefix + " START node=" + NodeName + " topic=" + OutTopic);
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
                EnsurePublisher();
                PublishIfDue();

                if (!_readyLogged && _published > 0)
                {
                    _readyLogged = true;
                    Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_SMOKE_READY"
                              + " node=" + NodeName
                              + " topic=" + OutTopic);
                }

                if (Time.realtimeSinceStartup - _startedAt >= HoldSeconds && _published >= 3)
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

        private void EnsurePublisher()
        {
            if (_publisher != null)
                return;

            _node = _ros2Unity.CreateNode(NodeName);
            _publisher = _node.CreatePublisher<std_msgs.msg.String>(OutTopic);
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

        private void Pass()
        {
            Debug.Log(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_SMOKE_GREEN"
                      + " published=" + _published
                      + " heldSeconds=" + (Time.realtimeSinceStartup - _startedAt).ToString("0.0"));
            Complete(0);
        }

        private void Fail(string message)
        {
            Debug.LogError(LogPrefix + " UNITY2FOXGLOVE_R2FU_RUNTIME_SMOKE_FAIL " + message);
            Complete(1);
        }

        private void Complete(int exitCode)
        {
            if (_completed)
                return;

            _completed = true;
            EditorApplication.update -= Tick;
            Cleanup();
            EditorApplication.Exit(exitCode);
        }

        private void Cleanup()
        {
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
    }
#endif
}
