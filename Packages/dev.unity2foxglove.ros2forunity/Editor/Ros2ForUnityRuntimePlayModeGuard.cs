// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Prevent Play Mode after switching native R2FU runtime packages in the same Editor process.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    [InitializeOnLoad]
    internal static class Ros2ForUnityRuntimePlayModeGuard
    {
        static Ros2ForUnityRuntimePlayModeGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;

            var runtimePackage = Ros2ForUnityRuntimeSelection.GetPendingEditorRestartRuntimePackage();
            if (string.IsNullOrWhiteSpace(runtimePackage))
                return;

            Debug.LogError(
                "Unity2Foxglove ROS2 For Unity runtime was switched to "
                + runtimePackage
                + " in this Unity Editor process. Restart Unity before entering Play Mode so stale native ROS2 runtime DLLs are unloaded.");
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
