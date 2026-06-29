// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Optional Foxglove Manager Inspector drawer for active R2FU runtime selection.

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    public static class Ros2ForUnityRuntimeSelectorInspector
    {
        private static string _pendingResolveMessage = string.Empty;

        public static void DrawActiveRuntimeSelector()
        {
            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            var status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            var installed = ToRuntimeArray(status.InstalledRuntimes);
            var installedLabels = BuildRuntimeLabels(installed);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 For Unity Runtime", EditorStyles.boldLabel);

            if (installed.Length == 0)
            {
                EditorGUILayout.HelpBox(status.Diagnostic, MessageType.Warning);
                return;
            }

            DrawRuntimePopup(projectDirectory, status, installed, installedLabels);
            DrawPendingResolveMessage();

            if (!string.IsNullOrWhiteSpace(status.Diagnostic))
                EditorGUILayout.HelpBox(status.Diagnostic, MessageType.Info);

            if (status.SelectedRuntime != null && !string.IsNullOrWhiteSpace(status.SelectedRuntime.ZenohPayloadDiagnostic))
                EditorGUILayout.HelpBox(status.SelectedRuntime.ZenohPayloadDiagnostic, MessageType.Warning);

            if (status.SelectedRuntime != null && status.SelectedRuntime.SupportsZenoh)
                DrawCommunicationModePopup(projectDirectory, status);

            DrawRestartStatus(projectDirectory, status);

            if (status.SelectedRuntime != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Runtime Package", status.SelectedRuntime.PackageName);
                    EditorGUILayout.TextField("ROS Distro", status.SelectedRuntime.RosDistro);
                    EditorGUILayout.TextField("Runtime Id", status.SelectedRuntime.RuntimeId);
                    EditorGUILayout.TextField(
                        "Active RMW",
                        Ros2ForUnityRuntimeSelection.GetRmwImplementationForCommunicationMode(
                            Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(status.SelectedRuntime)));
                }
            }
        }

        private static void DrawRestartStatus(string projectDirectory, Ros2ForUnityRuntimeSelectionStatus status)
        {
            if (status.SelectedRuntime != null)
            {
                var sessionRuntime = Ros2ForUnityRuntimeSelection.GetSessionRuntimePackage();
                var restartPackage = Ros2ForUnityRuntimeSelection.GetRuntimePackageRequiringEditorRestart(status);
                var restartCommunicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeRequiringEditorRestart(status);
                if (!string.IsNullOrWhiteSpace(restartPackage))
                {
                    EditorGUILayout.HelpBox(
                        "Restart Unity before entering Play Mode. This Editor session already loaded "
                        + sessionRuntime
                        + " native ROS2 runtime DLLs, and the active runtime is now "
                        + restartPackage
                        + ". Unity cannot safely unload native ROS2 DLLs mid-session.",
                        MessageType.Error);

                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    {
                        if (GUILayout.Button("Restart Unity"))
                            Ros2ForUnityRuntimeSelection.RestartEditor(projectDirectory);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(restartCommunicationMode))
                {
                    EditorGUILayout.HelpBox(
                        "Restart Unity before entering Play Mode. This Editor session already loaded the ROS2 runtime with communication mode "
                        + Ros2ForUnityRuntimeSelection.GetSessionCommunicationMode()
                        + ", and the active mode is now "
                        + restartCommunicationMode
                        + ". Unity cannot safely unload native ROS2 RMW DLLs mid-session.",
                        MessageType.Error);

                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    {
                        if (GUILayout.Button("Restart Unity"))
                            Ros2ForUnityRuntimeSelection.RestartEditor(projectDirectory);
                    }
                }
                else if (string.IsNullOrWhiteSpace(sessionRuntime))
                {
                    EditorGUILayout.HelpBox(
                        "Switching runtime packages or Lyrical communication mode is safe before this Editor session enters Play Mode. A restart is required only after native ROS2 DLLs have already loaded in this session.",
                        MessageType.Info);
                }
            }
        }

        private static void DrawCommunicationModePopup(
            string projectDirectory,
            Ros2ForUnityRuntimeSelectionStatus status)
        {
            var selectedRuntime = status.SelectedRuntime;
            var modes = Ros2ForUnityRuntimeSelection.GetCommunicationModeIds(selectedRuntime);
            var selectedMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(selectedRuntime);
            var selectedIndex = Math.Max(0, IndexOfMode(modes, selectedMode));
            var labels = Ros2ForUnityRuntimeSelection.GetCommunicationModeLabels(selectedRuntime);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                var changedIndex = EditorGUILayout.Popup("Communication Mode", selectedIndex, labels);
                if (EditorGUI.EndChangeCheck() && changedIndex >= 0 && changedIndex < modes.Count)
                {
                    Ros2ForUnityRuntimeSelection.SetCommunicationMode(
                        projectDirectory,
                        selectedRuntime,
                        modes[changedIndex]);
                }
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Exit Play Mode before switching ROS2 For Unity communication mode.", MessageType.Warning);
        }

        private static void DrawRuntimePopup(
            string projectDirectory,
            Ros2ForUnityRuntimeSelectionStatus status,
            Ros2ForUnityRuntimeDescriptor[] installed,
            string[] installedLabels)
        {
            var selectedIndex = Array.FindIndex(installed, runtime =>
                status.SelectedRuntime != null
                && string.Equals(runtime.PackageName, status.SelectedRuntime.PackageName, StringComparison.Ordinal));
            var popupLabels = installedLabels;
            if (selectedIndex < 0)
            {
                popupLabels = new string[installedLabels.Length + 1];
                popupLabels[0] = "No active runtime";
                Array.Copy(installedLabels, 0, popupLabels, 1, installedLabels.Length);
                selectedIndex = 0;
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUI.BeginChangeCheck();
                var changedIndex = EditorGUILayout.Popup("Active Runtime", selectedIndex, popupLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    var runtimeIndex = popupLabels.Length == installed.Length ? changedIndex : changedIndex - 1;
                    if (runtimeIndex >= 0 && runtimeIndex < installed.Length)
                        SwitchAndResolve(projectDirectory, installed[runtimeIndex]);
                }
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Exit Play Mode before switching ROS2 For Unity runtime packages.", MessageType.Warning);
        }

        private static void SwitchAndResolve(string projectDirectory, Ros2ForUnityRuntimeDescriptor runtime)
        {
            _pendingResolveMessage =
                "Unity is resolving the selected runtime package. Restart Unity only if this Editor session already entered Play Mode with a different ROS2 runtime.";
            Ros2ForUnityRuntimeSelection.SwitchActiveRuntimePackage(projectDirectory, runtime.PackageName);
            Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();
        }

        private static void DrawPendingResolveMessage()
        {
            if (string.IsNullOrWhiteSpace(_pendingResolveMessage))
                return;

            EditorGUILayout.HelpBox(_pendingResolveMessage, MessageType.Info);
        }

        private static Ros2ForUnityRuntimeDescriptor[] ToRuntimeArray(
            System.Collections.Generic.IReadOnlyList<Ros2ForUnityRuntimeDescriptor> runtimes)
        {
            if (runtimes is Ros2ForUnityRuntimeDescriptor[] array)
                return array;

            var result = new Ros2ForUnityRuntimeDescriptor[runtimes.Count];
            for (var i = 0; i < runtimes.Count; i++)
                result[i] = runtimes[i];
            return result;
        }

        private static string[] BuildRuntimeLabels(Ros2ForUnityRuntimeDescriptor[] runtimes)
        {
            var labels = new string[runtimes.Length];
            for (var i = 0; i < runtimes.Length; i++)
                labels[i] = runtimes[i].DisplayName;
            return labels;
        }

        private static int IndexOfMode(System.Collections.Generic.IReadOnlyList<string> modes, string selectedMode)
        {
            for (var i = 0; i < modes.Count; i++)
            {
                if (string.Equals(modes[i], selectedMode, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }
    }
}
#endif
