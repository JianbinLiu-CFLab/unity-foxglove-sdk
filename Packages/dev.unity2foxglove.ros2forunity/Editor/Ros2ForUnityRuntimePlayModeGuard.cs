// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Prevent Play Mode after switching native R2FU runtime packages in the same Editor process.

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Process = System.Diagnostics.Process;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    [InitializeOnLoad]
    internal static class Ros2ForUnityRuntimePlayModeGuard
    {
        static Ros2ForUnityRuntimePlayModeGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private const string CompilationStartedWhileR2fuPlayModeKey =
            "Unity2Foxglove.R2FU.CompilationStartedWhilePlayMode";

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;

            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            var status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            var runtimePackage = Ros2ForUnityRuntimeSelection.GetRuntimePackageRequiringEditorRestart(status);
            var communicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeRequiringEditorRestart(status);
            if (string.IsNullOrWhiteSpace(runtimePackage) && string.IsNullOrWhiteSpace(communicationMode))
            {
                if (TryGetMissingZenohRouterDiagnostic(status, out var zenohRouterDiagnostic))
                {
                    Debug.LogError(zenohRouterDiagnostic);
                    if (!Application.isBatchMode)
                    {
                        EditorUtility.DisplayDialog(
                            "ROS2 For Unity Zenoh router required",
                            zenohRouterDiagnostic,
                            "OK");
                    }

                    EditorApplication.isPlaying = false;
                    return;
                }

                Ros2ForUnityRuntimeSelection.BindActiveRuntimeForPlayMode(status);
                return;
            }

            if (!string.IsNullOrWhiteSpace(runtimePackage))
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity runtime was switched to "
                    + runtimePackage
                    + " in this Unity Editor process. Restart Unity before entering Play Mode so stale native ROS2 runtime DLLs are unloaded.");
            }
            else
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity communication mode was switched to "
                    + communicationMode
                    + " in this Unity Editor process. Restart Unity before entering Play Mode so stale native ROS2 RMW DLLs are unloaded.");
            }

            EditorApplication.isPlaying = false;
        }

        private static bool TryGetMissingZenohRouterDiagnostic(
            Ros2ForUnityRuntimeSelectionStatus status,
            out string diagnostic)
        {
            diagnostic = string.Empty;
            if (status == null || status.SelectedRuntime == null || !status.SelectedRuntime.SupportsZenoh)
                return false;

            var communicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(status.SelectedRuntime);
            if (!string.Equals(
                    communicationMode,
                    Ros2ForUnityRuntimeSelection.ZenohCommunicationMode,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (IsZenohRouterProcessRunning())
                return false;

            diagnostic =
                "Unity2Foxglove ROS2 For Unity is configured for Zenoh (rmw_zenoh_cpp), but no local Zenoh router process was detected. "
                + "Start rmw_zenohd before entering Play Mode, or switch the ROS2 For Unity Communication Mode back to FastDDS. "
                + "The Phase162 smoke helper only keeps its auto-started router alive while that helper is running; RViz left open after the helper exits no longer has a router.";
            return true;
        }

        private static bool IsZenohRouterProcessRunning()
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception)
            {
                return false;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    if (IsZenohRouterProcess(process))
                        return true;
                }
            }

            return false;
        }

        private static bool IsZenohRouterProcess(Process process)
        {
            try
            {
                if (LooksLikeZenohRouterProcess(process.ProcessName))
                    return true;
            }
            catch (Exception)
            {
                // Process metadata can disappear while Unity is enumerating processes.
            }

            try
            {
                if (LooksLikeZenohRouterProcess(process.MainModule?.FileName))
                    return true;
            }
            catch (Exception)
            {
                // Access to other-user/system process modules can be denied.
            }

            return false;
        }

        private static bool LooksLikeZenohRouterProcess(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && (value.IndexOf("rmw_zenohd", StringComparison.OrdinalIgnoreCase) >= 0
                       || value.IndexOf("zenohd", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void OnCompilationStarted(object context)
        {
            Ros2ForUnityRuntimeSelection.InvalidateStatusCache();
            if (StopPlayModeBeforeNativeReload("script compilation"))
                SessionState.SetBool(CompilationStartedWhileR2fuPlayModeKey, true);
        }

        private static void OnCompilationFinished(object context)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                SessionState.SetBool(CompilationStartedWhileR2fuPlayModeKey, false);
        }

        private static void OnBeforeAssemblyReload()
        {
            var compilationStartedWhilePlaying = SessionState.GetBool(CompilationStartedWhileR2fuPlayModeKey, false);
            if (!compilationStartedWhilePlaying && !EditorApplication.isPlaying)
                return;

            SessionState.SetBool(CompilationStartedWhileR2fuPlayModeKey, false);
            StopPlayModeBeforeNativeReload(
                compilationStartedWhilePlaying
                    ? "script compilation assembly reload"
                    : "assembly reload");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(
                    "Unity2Foxglove ROS2 For Unity assembly reload is continuing before Play Mode fully exited. Native ROS2/RMW DLLs may still be loaded; restart Unity before entering Play Mode again.");
            }
        }

        private static bool StopPlayModeBeforeNativeReload(string reason)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            if (!Ros2ForUnityRuntimeSelection.HasManifestRuntimePackage(projectDirectory))
                return false;

            Debug.LogError(
                "Unity2Foxglove ROS2 For Unity is active while Unity is starting "
                + reason
                + ". Exit Play Mode before changing scripts or packages; native ROS2/RMW DLLs cannot be safely unloaded during Play Mode.");

            EditorApplication.isPlaying = false;
            return true;
        }
    }
}
#endif
