// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Optional Foxglove Manager Inspector drawer for active R2FU runtime selection.

#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    public static class Ros2ForUnityRuntimeSelectorInspector
    {
        public static void DrawActiveRuntimeSelector()
        {
            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            var status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            var installed = status.InstalledRuntimes.ToArray();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 For Unity Runtime", EditorStyles.boldLabel);

            if (installed.Length == 0)
            {
                EditorGUILayout.HelpBox(status.Diagnostic, MessageType.Warning);
                return;
            }

            DrawRuntimePopup(projectDirectory, status, installed);

            if (!string.IsNullOrWhiteSpace(status.Diagnostic))
                EditorGUILayout.HelpBox(status.Diagnostic, MessageType.Info);

            var pendingRestartPackage = Ros2ForUnityRuntimeSelection.GetPendingEditorRestartRuntimePackage();
            if (!string.IsNullOrWhiteSpace(pendingRestartPackage))
            {
                EditorGUILayout.HelpBox(
                    "Restart Unity before entering Play Mode. The active runtime was switched to "
                    + pendingRestartPackage
                    + ", but native ROS2 runtime DLLs loaded earlier in this Editor process cannot be unloaded safely.",
                    MessageType.Error);
            }

            if (status.SelectedRuntime != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Runtime Package", status.SelectedRuntime.PackageName);
                    EditorGUILayout.TextField("ROS Distro", status.SelectedRuntime.RosDistro);
                    EditorGUILayout.TextField("Runtime Id", status.SelectedRuntime.RuntimeId);
                }
            }
        }

        private static void DrawRuntimePopup(
            string projectDirectory,
            Ros2ForUnityRuntimeSelectionStatus status,
            Ros2ForUnityRuntimeDescriptor[] installed)
        {
            var selectedIndex = Math.Max(0, Array.FindIndex(installed, runtime =>
                status.SelectedRuntime != null
                && string.Equals(runtime.PackageName, status.SelectedRuntime.PackageName, StringComparison.Ordinal)));
            var installedLabels = installed.Select(runtime => runtime.DisplayName).ToArray();

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                var changedIndex = EditorGUILayout.Popup("Active Runtime", selectedIndex, installedLabels);
                if (EditorGUI.EndChangeCheck() && changedIndex >= 0 && changedIndex < installed.Length)
                    SwitchAndResolve(projectDirectory, installed[changedIndex]);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Exit Play Mode before switching ROS2 For Unity runtime packages.", MessageType.Warning);
        }

        private static void SwitchAndResolve(string projectDirectory, Ros2ForUnityRuntimeDescriptor runtime)
        {
            Ros2ForUnityRuntimeSelection.SwitchActiveRuntimePackage(projectDirectory, runtime.PackageName);
            Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();
            EditorGUILayout.HelpBox("Unity is resolving the selected runtime package. Restart Unity after package reimport and script compilation finish.", MessageType.Info);
        }
    }
}
#endif
