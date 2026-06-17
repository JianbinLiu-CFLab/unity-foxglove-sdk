// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Project-level active runtime selection for optional R2FU runtime packages.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal sealed class Ros2ForUnityRuntimeDescriptor
    {
        public Ros2ForUnityRuntimeDescriptor(
            string displayName,
            string packageName,
            string compileSymbol,
            string runtimeId,
            string rosDistro,
            string platform)
        {
            DisplayName = displayName ?? string.Empty;
            PackageName = packageName ?? string.Empty;
            CompileSymbol = compileSymbol ?? string.Empty;
            RuntimeId = runtimeId ?? string.Empty;
            RosDistro = rosDistro ?? string.Empty;
            Platform = platform ?? string.Empty;
        }

        public string DisplayName { get; }
        public string PackageName { get; }
        public string CompileSymbol { get; }
        public string RuntimeId { get; }
        public string RosDistro { get; }
        public string Platform { get; }
    }

    internal sealed class Ros2ForUnityRuntimeSelectionStatus
    {
        public Ros2ForUnityRuntimeSelectionStatus(
            IReadOnlyList<Ros2ForUnityRuntimeDescriptor> installedRuntimes,
            string activeRuntimePackage,
            Ros2ForUnityRuntimeDescriptor selectedRuntime,
            string diagnostic)
        {
            InstalledRuntimes = installedRuntimes ?? Array.Empty<Ros2ForUnityRuntimeDescriptor>();
            ActiveRuntimePackage = activeRuntimePackage ?? string.Empty;
            SelectedRuntime = selectedRuntime;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public IReadOnlyList<Ros2ForUnityRuntimeDescriptor> InstalledRuntimes { get; }
        public string ActiveRuntimePackage { get; }
        public Ros2ForUnityRuntimeDescriptor SelectedRuntime { get; }
        public string Diagnostic { get; }
        public bool HasSelection => SelectedRuntime != null;
    }

    internal static class Ros2ForUnityRuntimeSelection
    {
        public const string BaseCompileSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        public const string JazzyWin64CompileSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY_JAZZY_WIN64_PACKAGE";
        public const string LyricalWin64CompileSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY_LYRICAL_WIN64_PACKAGE";
        public const string SettingsRelativePath = "ProjectSettings/Unity2FoxgloveRos2ForUnitySettings.json";

        private static readonly Ros2ForUnityRuntimeDescriptor[] KnownRuntimes =
        {
            new Ros2ForUnityRuntimeDescriptor(
                "Jazzy Win64",
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                JazzyWin64CompileSymbol,
                "r2fu-jazzy-win64",
                "jazzy",
                "win64"),
            new Ros2ForUnityRuntimeDescriptor(
                "Lyrical Win64",
                "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
                LyricalWin64CompileSymbol,
                "r2fu-lyrical-win64",
                "lyrical",
                "win64"),
        };

        public static IReadOnlyList<Ros2ForUnityRuntimeDescriptor> KnownRuntimeDescriptors => KnownRuntimes;

        public static string[] RuntimeCompileSymbols => KnownRuntimes
            .Select(runtime => runtime.CompileSymbol)
            .ToArray();

        public static string ProjectDirectoryFromApplication()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent?.FullName ?? string.Empty;
        }

        public static Ros2ForUnityRuntimeSelectionStatus GetStatus()
            => GetStatus(ProjectDirectoryFromApplication());

        public static Ros2ForUnityRuntimeSelectionStatus GetStatus(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    Array.Empty<Ros2ForUnityRuntimeDescriptor>(),
                    string.Empty,
                    null,
                    "Could not resolve the Unity project directory.");
            }

            var installed = KnownRuntimes
                .Where(runtime => IsPackageResolved(projectDirectory, runtime.PackageName))
                .ToArray();
            var activePackage = ReadActiveRuntimePackage(projectDirectory);
            var explicitSelection = installed.FirstOrDefault(
                runtime => string.Equals(runtime.PackageName, activePackage, StringComparison.Ordinal));

            if (explicitSelection != null)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    installed,
                    activePackage,
                    explicitSelection,
                    string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(activePackage))
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    installed,
                    activePackage,
                    null,
                    "The selected ROS2 For Unity runtime package is not installed or not resolved: " + activePackage);
            }

            if (installed.Length > 0)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    installed,
                    string.Empty,
                    installed[0],
                    "Using the first installed ROS2 For Unity runtime. Change the dropdown to select a different active runtime.");
            }

            return new Ros2ForUnityRuntimeSelectionStatus(
                installed,
                string.Empty,
                null,
                "No supported ROS2 For Unity runtime package is installed.");
        }

        public static void SaveActiveRuntimePackage(string projectDirectory, string packageName)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new InvalidOperationException("Could not resolve the Unity project directory.");

            var settingsPath = Path.Combine(projectDirectory, SettingsRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? projectDirectory);
            var settings = new SettingsDto
            {
                schemaVersion = 1,
                activeRuntimePackage = packageName ?? string.Empty,
            };
            File.WriteAllText(settingsPath, JsonUtility.ToJson(settings, prettyPrint: true) + Environment.NewLine);
            AssetDatabase.Refresh();
        }

        private static string ReadActiveRuntimePackage(string projectDirectory)
        {
            var settingsPath = Path.Combine(projectDirectory, SettingsRelativePath);
            if (!File.Exists(settingsPath))
                return string.Empty;

            try
            {
                var settings = JsonUtility.FromJson<SettingsDto>(File.ReadAllText(settingsPath));
                return settings?.activeRuntimePackage?.Trim() ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "Unity2Foxglove could not read " + SettingsRelativePath + ": "
                    + ex.GetType().Name + ": " + ex.Message);
                return string.Empty;
            }
        }

        private static bool IsPackageResolved(string projectDirectory, string packageName)
        {
            var manifestPath = Path.Combine(projectDirectory, "Packages", "manifest.json");
            if (!File.Exists(manifestPath) || !ContainsPackageKey(File.ReadAllText(manifestPath), packageName))
                return false;

            var lockPath = Path.Combine(projectDirectory, "Packages", "packages-lock.json");
            return File.Exists(lockPath) && ContainsPackageKey(File.ReadAllText(lockPath), packageName);
        }

        private static bool ContainsPackageKey(string json, string packageName)
        {
            var dependencyPattern = "\"" + Regex.Escape(packageName) + "\"\\s*:";
            return Regex.IsMatch(json ?? string.Empty, dependencyPattern);
        }

        [Serializable]
        private sealed class SettingsDto
        {
            public int schemaVersion = 1;
            public string activeRuntimePackage = string.Empty;
        }
    }
}
#endif
