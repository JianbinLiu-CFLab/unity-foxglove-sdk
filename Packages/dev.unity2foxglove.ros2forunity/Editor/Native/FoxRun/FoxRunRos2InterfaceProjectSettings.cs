// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Project-level initial identity choice for the static ROS2 interface package.

#if UNITY_EDITOR
using System;
using System.IO;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Stores only the first-generation ROS package identity. Once the tracked
    /// source package has a lock, that identity is immutable and the Inspector
    /// deliberately offers an explicit revision workflow instead of a mutable
    /// package-name field.
    /// </summary>
    [FilePath("ProjectSettings/FoxRunRos2InterfacePackageSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FoxRunRos2InterfaceProjectSettings : ScriptableSingleton<FoxRunRos2InterfaceProjectSettings>
    {
        internal const string SettingsPath = "Project/Unity2Foxglove/FoxRun ROS2 Interfaces";

        [SerializeField] private string _initialRosPackageName = FoxRunRos2InterfaceIdentity.DefaultRosPackageName;

        internal static string ResolveRosPackageName(string packageRoot)
        {
            if (TryReadLockedPackageName(packageRoot, out var locked))
                return locked;

            var candidate = instance._initialRosPackageName;
            return FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision(candidate, out var revision)
                   && revision == 1
                ? candidate
                : FoxRunRos2InterfaceIdentity.DefaultRosPackageName;
        }

        private static bool TryReadLockedPackageName(string packageRoot, out string rosPackageName)
        {
            rosPackageName = string.Empty;
            var lockPath = Path.Combine(packageRoot ?? string.Empty, "RuntimeSupport", "foxrun-ros2-interface-lock.json");
            if (!File.Exists(lockPath))
                return false;

            try
            {
                rosPackageName = FoxRunRos2InterfaceLock.Parse(File.ReadAllText(lockPath)).RosPackageName;
                return true;
            }
            catch (Exception exception) when (exception is FormatException || exception is IOException || exception is UnauthorizedAccessException)
            {
                // A malformed existing lock remains a fail-closed command error.
                // The UI merely must not offer changing its identity underneath it.
                return true;
            }
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "FoxRun ROS2 Interfaces",
                guiHandler = _ => DrawSettings()
            };
        }

        private static void DrawSettings()
        {
            var packageRoot = FoxRunRos2InterfacePackageCommand.GetSourcePackageRoot();
            if (TryReadLockedPackageName(packageRoot, out var lockedPackageName))
            {
                EditorGUILayout.LabelField("Locked ROS Package", string.IsNullOrEmpty(lockedPackageName) ? "Invalid lock" : lockedPackageName);
                EditorGUILayout.HelpBox(
                    "The initial static interface package identity is locked. Create the explicit next _vN revision only for a wire-breaking DTO change.",
                    MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();
            var candidate = EditorGUILayout.TextField("Initial ROS Package", instance._initialRosPackageName);
            if (!EditorGUI.EndChangeCheck())
                return;

            if (!FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision(candidate, out var revision) || revision != 1)
            {
                Debug.LogError("FoxRun ROS2 interface package identity must use the initial _v1 revision grammar.");
                return;
            }

            instance._initialRosPackageName = candidate;
            instance.Save(true);
        }
    }
}
#endif
