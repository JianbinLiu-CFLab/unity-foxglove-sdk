// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase179
// Purpose: Batch-mode lifecycle probe for the native FoxRun ROS2 subscription scene.

#if UNITY_EDITOR
using System;
using Process = System.Diagnostics.Process;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

/// <summary>
/// Runs the tracked Phase179 scene in a disposable batch-mode Editor process.
/// It has no normal Editor or Player behavior: it activates only when the
/// command line names <see cref="Run"/> as its execute method.
/// </summary>
public static class Phase179BatchModeNativeLifecycleProbe
{
    private const string ExecuteMethodName =
        "Phase179BatchModeNativeLifecycleProbe.Run";
    private const string AcceptanceScenePath =
        "Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity";
    private const string StringTopic = "/foxrun/phase179/string";
    private const double RegistrationTimeoutSeconds = 90.0;
    private const double ReadyDwellSeconds = 10.0;

    private static readonly string SessionPrefix =
        "Unity2Foxglove.Phase179BatchModeNativeLifecycleProbe."
        + Process.GetCurrentProcess().Id + ".";

    private static bool _handlersAttached;
    private static double _playStartedAt;
    private static double _readyObservedAt;

    [InitializeOnLoadMethod]
    private static void RegisterFromCommandLine()
    {
        if (!IsRequestedBatchRun())
            return;

        AttachHandlers();
        if (!SessionState.GetBool(SessionKey("requested"), false))
            Run();
    }

    /// <summary>
    /// Batch entry point:
    /// <c>-executeMethod Phase179BatchModeNativeLifecycleProbe.Run</c>.
    /// The caller owns the process environment, including ROS_DISTRO,
    /// RMW_IMPLEMENTATION, PATH, and any Zenoh router topology.
    /// </summary>
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            throw new InvalidOperationException(
                "Phase179BatchModeNativeLifecycleProbe only runs in a Unity batch-mode process.");
        }

        AttachHandlers();
        if (SessionState.GetBool(SessionKey("requested"), false))
            return;

        SessionState.SetBool(SessionKey("requested"), true);
        EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
    }

    private static void AttachHandlers()
    {
        if (_handlersAttached)
            return;

        _handlersAttached = true;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OpenSceneAndEnterPlayMode()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += OpenSceneAndEnterPlayMode;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EditorSceneManager.OpenScene(AcceptanceScenePath);
        Debug.Log("PHASE179_BATCH_NATIVE_PROBE_SCENE_OPENED scene=" + AcceptanceScenePath);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _playStartedAt = EditorApplication.timeSinceStartup;
            _readyObservedAt = 0.0;
            Debug.Log("PHASE179_BATCH_NATIVE_PROBE_PLAY_ENTERED");
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode
            || !SessionState.GetBool(SessionKey("exit-requested"), false))
            return;

        var exitCode = SessionState.GetInt(SessionKey("exit-code"), 3);
        EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }

    private static void OnEditorUpdate()
    {
        if (!IsRequestedBatchRun() || !EditorApplication.isPlaying)
            return;

        if (HasReadyStringSubscription())
        {
            if (_readyObservedAt <= 0.0)
            {
                _readyObservedAt = EditorApplication.timeSinceStartup;
                Debug.Log("PHASE179_BATCH_NATIVE_PROBE_READY topic=" + StringTopic);
            }
            else if (EditorApplication.timeSinceStartup - _readyObservedAt >= ReadyDwellSeconds)
            {
                Debug.Log("PHASE179_BATCH_NATIVE_PROBE_STABLE dwellSeconds=" + ReadyDwellSeconds);
                RequestExit(0, "ready-stable");
            }
            return;
        }

        if (_playStartedAt <= 0.0
            || EditorApplication.timeSinceStartup - _playStartedAt < RegistrationTimeoutSeconds)
            return;

        Debug.LogError(
            "PHASE179_BATCH_NATIVE_PROBE_TIMEOUT topic=" + StringTopic
            + " diagnostics=" + DescribeDiagnostics());
        RequestExit(2, "registration-timeout");
    }

    private static bool HasReadyStringSubscription()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        var snapshots = FoxRunRos2SubscriptionRuntimeDiagnostics.GetSnapshots();
        for (var index = 0; index < snapshots.Length; index++)
        {
            var snapshot = snapshots[index];
            if (!string.Equals(snapshot.Topic, StringTopic, StringComparison.Ordinal))
                continue;

            if (snapshot.State == FoxRunRos2SubscriptionBindingState.Ready
                || snapshot.State == FoxRunRos2SubscriptionBindingState.Receiving)
                return true;
        }
#endif
        return false;
    }

    private static string DescribeDiagnostics()
    {
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        var snapshots = FoxRunRos2SubscriptionRuntimeDiagnostics.GetSnapshots();
        if (snapshots.Length == 0)
            return "none";

        var limit = Math.Min(snapshots.Length, 8);
        var parts = new string[limit];
        for (var index = 0; index < limit; index++)
        {
            var snapshot = snapshots[index];
            parts[index] = snapshot.Topic + ":" + snapshot.State
                           + ":" + snapshot.LastErrorCode;
        }
        return string.Join("|", parts);
#else
        return "native-runtime-compile-symbol-unavailable";
#endif
    }

    private static void RequestExit(int exitCode, string outcome)
    {
        if (SessionState.GetBool(SessionKey("exit-requested"), false))
            return;

        SessionState.SetBool(SessionKey("exit-requested"), true);
        SessionState.SetInt(SessionKey("exit-code"), exitCode);
        Debug.Log(
            "PHASE179_BATCH_NATIVE_PROBE_EXIT outcome=" + outcome
            + " exitCode=" + exitCode);
        EditorApplication.ExitPlaymode();
    }

    private static bool IsRequestedBatchRun()
    {
        if (!Application.isBatchMode)
            return false;

        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], ExecuteMethodName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string SessionKey(string suffix)
        => SessionPrefix + suffix;
}
#endif
