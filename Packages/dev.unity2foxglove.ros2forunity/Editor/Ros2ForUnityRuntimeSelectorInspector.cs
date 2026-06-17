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

            if (installed.Length > 0
                && string.IsNullOrWhiteSpace(status.ActiveRuntimePackage)
                && status.SelectedRuntime != null)
            {
                SaveAndReconcile(projectDirectory, status.SelectedRuntime);
                status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            }

            DrawRuntimePopup(projectDirectory, status, installed);

            if (!string.IsNullOrWhiteSpace(status.Diagnostic))
                EditorGUILayout.HelpBox(status.Diagnostic, MessageType.Info);

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
            var selectedIndex = Math.Max(
                0,
                Array.FindIndex(installed, runtime =>
                    string.Equals(runtime.PackageName, status.SelectedRuntime.PackageName, StringComparison.Ordinal)));
            var installedLabels = installed.Select(runtime => runtime.DisplayName).ToArray();

            EditorGUI.BeginChangeCheck();
            var changedIndex = EditorGUILayout.Popup("Active Runtime", selectedIndex, installedLabels);
            if (EditorGUI.EndChangeCheck() && changedIndex >= 0 && changedIndex < installed.Length)
                SaveAndReconcile(projectDirectory, installed[changedIndex]);
        }

        private static void SaveAndReconcile(string projectDirectory, Ros2ForUnityRuntimeDescriptor runtime)
        {
            Ros2ForUnityRuntimeSelection.SaveActiveRuntimePackage(projectDirectory, runtime.PackageName);
            Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();
        }
    }
}
#endif
