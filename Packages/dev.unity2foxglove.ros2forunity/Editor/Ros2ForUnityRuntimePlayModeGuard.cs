// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Prevent Play Mode after switching native R2FU runtime packages in the same Editor process.

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

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

        private static void OnCompilationStarted(object context)
        {
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
            var status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            if (status.SelectedRuntime == null)
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
