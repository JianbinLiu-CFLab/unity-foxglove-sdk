// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Project-level active runtime selection for optional R2FU runtime packages.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static partial class Ros2ForUnityRuntimeSelection
    {
        private static void ApplySelectedRuntimeEnvironment(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string communicationMode)
        {
            if (runtime == null)
                return;

            try
            {
                // Bind the selected manifest identity before the optional runtime enters
                // its native-ready path, so post-readiness diagnostics report observed
                // process state instead of inferring a distro from the package display name.
                Ros2ForUnityEditorEnvironmentLease.Set(
                    "ROS_DISTRO",
                    string.IsNullOrWhiteSpace(runtime.RosDistro) ? null : runtime.RosDistro);

                var rmwImplementation = GetRmwImplementationForCommunicationMode(runtime, communicationMode);
                Ros2ForUnityEditorEnvironmentLease.Set(
                    "RMW_IMPLEMENTATION",
                    string.IsNullOrWhiteSpace(rmwImplementation) ? null : rmwImplementation);

                Ros2ForUnityZenohRouterSettings.ApplyToCurrentProcess(
                    projectDirectory,
                    runtime,
                    rmwImplementation);
            }
            catch
            {
                Ros2ForUnityEditorEnvironmentLease.Restore();
                throw;
            }
        }

        public static string GetRuntimePackageRequiringEditorRestart(string projectDirectory)
            => GetRuntimePackageRequiringEditorRestart(GetStatus(projectDirectory));

        public static string GetRuntimePackageRequiringEditorRestart(Ros2ForUnityRuntimeSelectionStatus status)
        {
            var sessionRuntime = GetSessionRuntimePackage();
            if (string.IsNullOrWhiteSpace(sessionRuntime))
                return string.Empty;

            if (status == null || status.SelectedRuntime == null)
                return string.Empty;

            return string.Equals(status.SelectedRuntime.PackageName, sessionRuntime, StringComparison.Ordinal)
                ? string.Empty
                : status.SelectedRuntime.PackageName;
        }

        public static bool IsEditorRestartRequired(string projectDirectory)
            => IsEditorRestartRequired(GetStatus(projectDirectory));

        public static bool IsEditorRestartRequired(Ros2ForUnityRuntimeSelectionStatus status)
            => !string.IsNullOrWhiteSpace(GetRuntimePackageRequiringEditorRestart(status))
               || !string.IsNullOrWhiteSpace(GetCommunicationModeRequiringEditorRestart(status))
               || !string.IsNullOrWhiteSpace(GetZenohRouterEndpointRequiringEditorRestart(status));

        public static string GetCommunicationModeRequiringEditorRestart(string projectDirectory)
            => GetCommunicationModeRequiringEditorRestart(GetStatus(projectDirectory));

        public static string GetCommunicationModeRequiringEditorRestart(Ros2ForUnityRuntimeSelectionStatus status)
        {
            var sessionMode = GetSessionCommunicationMode();
            if (string.IsNullOrWhiteSpace(sessionMode))
                return string.Empty;

            if (status == null || status.SelectedRuntime == null)
                return string.Empty;

            var communicationMode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            return string.Equals(communicationMode, sessionMode, StringComparison.Ordinal)
                ? string.Empty
                : communicationMode;
        }

        public static string GetZenohRouterEndpointRequiringEditorRestart(string projectDirectory)
            => GetZenohRouterEndpointRequiringEditorRestart(GetStatus(projectDirectory));

        public static string GetZenohRouterEndpointRequiringEditorRestart(Ros2ForUnityRuntimeSelectionStatus status)
        {
            var sessionEndpoint = GetSessionZenohRouterEndpoint();
            if (string.IsNullOrWhiteSpace(sessionEndpoint) || status?.SelectedRuntime == null)
                return string.Empty;

            var communicationMode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            if (!string.Equals(
                    GetRmwImplementationForCommunicationMode(status.SelectedRuntime, communicationMode),
                    ZenohRmwImplementation,
                    StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var selectedEndpoint = Ros2ForUnityZenohRouterSettings.Get(status.SelectedRuntime).Endpoint;
            return string.Equals(selectedEndpoint, sessionEndpoint, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : selectedEndpoint;
        }

        public static string GetCustomTypesupportRequiringEditorRestart(Ros2ForUnityRuntimeSelectionStatus status)
        {
            if (string.IsNullOrWhiteSpace(GetSessionRuntimePackage()) || status?.SelectedRuntime == null)
                return string.Empty;

            var selection = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                ProjectDirectoryFromApplication(),
                status.SelectedRuntime.PackageName);
            if (!selection.IsReady && selection.Code != Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly)
                return "invalid custom typesupport selection";

            return string.Equals(
                GetSessionCustomTypesupportIdentity(),
                BuildCustomTypesupportIdentity(selection),
                StringComparison.Ordinal)
                ? string.Empty
                : (string.IsNullOrWhiteSpace(selection.ActiveAddOnPackage)
                    ? "base-only"
                    : selection.ActiveAddOnPackage);
        }

        public static void RestartEditor(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new InvalidOperationException("Could not resolve the Unity project directory.");

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException(
                    "Cannot restart Unity while Play Mode, compilation, or package refresh is active.");
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var status = GetStatus(projectDirectory);
            if (status.SelectedRuntime == null)
                throw new InvalidOperationException("Cannot restart Unity without one selected ROS2 For Unity runtime package.");

            var customTypesupport = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                projectDirectory,
                status.SelectedRuntime.PackageName);
            ThrowIfCustomTypesupportTransactionFailed(customTypesupport, "restarting Unity with custom ROS2 typesupport");
            RestartEditorInCleanProcess(projectDirectory, status.SelectedRuntime, customTypesupport);
        }

        private static void RestartEditorInCleanProcess(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            Ros2ForUnityCustomTypesupportSelectionResult customTypesupport)
        {
            var editorExecutable = EditorApplication.applicationPath;
            if (string.IsNullOrWhiteSpace(editorExecutable) || !File.Exists(editorExecutable))
                throw new InvalidOperationException("Could not resolve the current Unity Editor executable for a clean restart.");

            var replacementEnvironment = new ProcessStartInfo
            {
                UseShellExecute = false,
            };

            replacementEnvironment.EnvironmentVariables[NativeLibraryPathVariableName()] =
                BuildCleanRestartPath(projectDirectory, runtime, customTypesupport);
            if (!string.IsNullOrWhiteSpace(runtime.RosDistro))
                replacementEnvironment.EnvironmentVariables["ROS_DISTRO"] = runtime.RosDistro;

            var communicationMode = GetCommunicationModeForRuntime(runtime);
            var rmwImplementation = GetRmwImplementationForCommunicationMode(runtime, communicationMode);
            if (!string.IsNullOrWhiteSpace(rmwImplementation))
                replacementEnvironment.EnvironmentVariables["RMW_IMPLEMENTATION"] = rmwImplementation;

            Ros2ForUnityZenohRouterSettings.ApplyToRestartProcess(
                projectDirectory,
                runtime,
                rmwImplementation,
                replacementEnvironment);

            var restartRelayStartInfo = Ros2ForUnityEditorRestartRelay.CreateStartInfo(
                Application.platform == RuntimePlatform.WindowsEditor,
                ResolveRestartRelayExecutable(),
                Process.GetCurrentProcess().Id,
                editorExecutable,
                projectDirectory,
                replacementEnvironment);

            try
            {
                if (Process.Start(restartRelayStartInfo) == null)
                    throw new InvalidOperationException("Unity did not start a replacement Editor process.");
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException("Could not start a clean Unity Editor replacement process.", ex);
            }

            EditorApplication.Exit(0);
        }

        private static string ResolveRestartRelayExecutable()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var powershell = Path.Combine(
                    systemDirectory,
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe");
                if (File.Exists(powershell))
                    return powershell;

                throw new InvalidOperationException(
                    "Could not resolve the Windows PowerShell executable required for a clean Unity restart.");
            }

            const string posixShell = "/bin/sh";
            if (File.Exists(posixShell))
                return posixShell;

            throw new InvalidOperationException(
                "Could not resolve the POSIX shell required for a clean Unity restart.");
        }

        private static string BuildCleanRestartPath(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor selectedRuntime,
            Ros2ForUnityCustomTypesupportSelectionResult selectedCustomTypesupport)
        {
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var blockedRuntimePluginPaths = new HashSet<string>(comparison);
            foreach (var runtime in DiscoverCandidateRuntimes(projectDirectory))
                blockedRuntimePluginPaths.Add(NormalizeRestartPath(RuntimePluginDirectory(projectDirectory, runtime)));
            foreach (var customPackage in Ros2ForUnityCustomTypesupportSelectionTransaction.DiscoverCandidatePackageIds(projectDirectory))
            {
                blockedRuntimePluginPaths.Add(NormalizeRestartPath(
                    Ros2ForUnityCustomTypesupportSelectionTransaction.PluginDirectory(projectDirectory, customPackage)));
            }

            var cleanEntries = new List<string>();
            var seen = new HashSet<string>(comparison);
            AddRestartPathEntry(
                cleanEntries,
                seen,
                RuntimePluginDirectory(projectDirectory, selectedRuntime));
            if (selectedCustomTypesupport?.IsReady == true)
            {
                AddRestartPathEntry(
                    cleanEntries,
                    seen,
                    selectedCustomTypesupport.NativePluginDirectory);
            }

            var inheritedPath = Environment.GetEnvironmentVariable(NativeLibraryPathVariableName());
            if (!string.IsNullOrWhiteSpace(inheritedPath))
            {
                foreach (var rawEntry in inheritedPath.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var entry = rawEntry.Trim();
                    if (blockedRuntimePluginPaths.Contains(NormalizeRestartPath(entry)))
                        continue;

                    AddRestartPathEntry(cleanEntries, seen, entry);
                }
            }

            return string.Join(Path.PathSeparator.ToString(), cleanEntries);
        }

        private static void AddRestartPathEntry(
            ICollection<string> entries,
            ISet<string> seen,
            string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
                return;

            var normalized = NormalizeRestartPath(entry);
            if (!seen.Add(normalized))
                return;

            entries.Add(entry.Trim());
        }

        private static string RuntimePluginDirectory(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime)
        {
            return Path.Combine(
                RepositoryPackagesDirectory(projectDirectory),
                runtime?.PackageName ?? string.Empty,
                WindowsNativePluginRelativeDirectory);
        }

        private static string BuildCustomTypesupportIdentity(
            Ros2ForUnityCustomTypesupportSelectionResult selection)
        {
            if (selection == null || selection.Code == Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly)
                return string.Empty;

            return selection.ActiveAddOnPackage
                   + "|" + selection.InterfaceDigest
                   + "|" + selection.BaseRuntimeAbiDigest
                   + "|" + NormalizeRestartPath(selection.NativePluginDirectory);
        }

        private static void ThrowIfCustomTypesupportTransactionFailed(
            Ros2ForUnityCustomTypesupportSelectionResult result,
            string operation)
        {
            if (result != null
                && (result.Code == Ros2ForUnityCustomTypesupportSelectionCode.Ready
                    || result.Code == Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly))
            {
                return;
            }

            var code = result == null ? "Unknown" : result.Code.ToString();
            var candidateValidation = result == null
                || result.CandidateValidationCode == Ros2ForUnityCustomTypesupportCandidateValidationCode.None
                ? string.Empty
                : " / " + result.CandidateValidationCode;
            throw new InvalidOperationException(
                "FoxRun custom ROS2 typesupport preflight failed while " + operation + ": " + code + candidateValidation + ".");
        }

        private static string NormalizeRestartPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return TrimDirectorySeparators(Path.GetFullPath(path.Trim()));
            }
            catch (Exception)
            {
                return TrimDirectorySeparators(path.Trim());
            }
        }

        private static string NativeLibraryPathVariableName()
            => Application.platform == RuntimePlatform.WindowsEditor ? "PATH" : "LD_LIBRARY_PATH";

    }
}
#endif
