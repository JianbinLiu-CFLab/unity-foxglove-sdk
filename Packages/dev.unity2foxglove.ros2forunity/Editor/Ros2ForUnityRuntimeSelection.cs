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
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal sealed class Ros2ForUnityRuntimeDescriptor
    {
        public Ros2ForUnityRuntimeDescriptor(
            string displayName,
            string packageName,
            string runtimeId,
            string rosDistro,
            string platform,
            Ros2ForUnityRuntimeCapabilities capabilities,
            string zenohPayloadDiagnostic)
        {
            DisplayName = displayName ?? string.Empty;
            PackageName = packageName ?? string.Empty;
            RuntimeId = runtimeId ?? string.Empty;
            RosDistro = rosDistro ?? string.Empty;
            Platform = platform ?? string.Empty;
            ZenohPayloadDiagnostic = zenohPayloadDiagnostic ?? string.Empty;
            Capabilities = capabilities ?? Ros2ForUnityRuntimeCapabilityParser.Parse(string.Empty);
            CommunicationModes = BuildAvailableCommunicationModes(Capabilities.CommunicationModes, ZenohPayloadDiagnostic);
            DefaultCommunicationMode = SelectDefaultCommunicationMode(CommunicationModes);
            CommunicationModeIds = BuildCommunicationModeIds(CommunicationModes);
            CommunicationModeLabels = BuildCommunicationModeLabels(CommunicationModes);
            SupportsZenoh = ContainsRmw(CommunicationModes, Ros2ForUnityRuntimeSelection.ZenohRmwImplementation);
        }

        public string DisplayName { get; }
        public string PackageName { get; }
        public string RuntimeId { get; }
        public string RosDistro { get; }
        public string Platform { get; }
        public Ros2ForUnityRuntimeCapabilities Capabilities { get; }
        public IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> CommunicationModes { get; }
        public Ros2ForUnityRuntimeCommunicationMode DefaultCommunicationMode { get; }
        public IReadOnlyList<string> CommunicationModeIds { get; }
        public string[] CommunicationModeLabels { get; }
        public bool SupportsZenoh { get; }
        public string ZenohPayloadDiagnostic { get; }

        public Ros2ForUnityRuntimeCommunicationMode FindCommunicationMode(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            for (var i = 0; i < CommunicationModes.Count; i++)
            {
                var mode = CommunicationModes[i];
                if (string.Equals(mode.Id, id, StringComparison.Ordinal))
                    return mode;
            }

            return null;
        }

        private static IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> BuildAvailableCommunicationModes(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> capabilities,
            string zenohPayloadDiagnostic)
        {
            if (capabilities == null || capabilities.Count == 0)
                return Array.Empty<Ros2ForUnityRuntimeCommunicationMode>();

            var suppressZenoh = !string.IsNullOrWhiteSpace(zenohPayloadDiagnostic);
            var available = new List<Ros2ForUnityRuntimeCommunicationMode>(capabilities.Count);
            for (var i = 0; i < capabilities.Count; i++)
            {
                var mode = capabilities[i];
                if (suppressZenoh
                    && string.Equals(
                        mode.RmwImplementation,
                        Ros2ForUnityRuntimeSelection.ZenohRmwImplementation,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                available.Add(mode);
            }

            return available.ToArray();
        }

        private static Ros2ForUnityRuntimeCommunicationMode SelectDefaultCommunicationMode(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes)
        {
            if (modes == null || modes.Count == 0)
                return null;

            for (var i = 0; i < modes.Count; i++)
            {
                if (modes[i].IsDefault)
                    return modes[i];
            }

            return modes[0];
        }

        private static IReadOnlyList<string> BuildCommunicationModeIds(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes)
        {
            var ids = new string[modes?.Count ?? 0];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = modes[i].Id;
            return ids;
        }

        private static string[] BuildCommunicationModeLabels(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes)
        {
            var labels = new string[modes?.Count ?? 0];
            for (var i = 0; i < labels.Length; i++)
                labels[i] = modes[i].DisplayName;
            return labels;
        }

        private static bool ContainsRmw(
            IReadOnlyList<Ros2ForUnityRuntimeCommunicationMode> modes,
            string rmwImplementation)
        {
            for (var i = 0; i < modes.Count; i++)
            {
                if (string.Equals(modes[i].RmwImplementation, rmwImplementation, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
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
        public const string CustomTypesupportCompileSymbol =
            "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES";
        public const string RuntimePackagePrefix = "dev.unity2foxglove.ros2forunity.runtime.";
        public const string CustomTypesupportPackagePrefix =
            Ros2ForUnityCustomTypesupportSelectionTransaction.CustomTypesupportPackagePrefix;
        public const string FastDdsCommunicationMode = Ros2ForUnityRuntimeCapabilityParser.FastDdsCommunicationMode;
        public const string ZenohCommunicationMode = Ros2ForUnityRuntimeCapabilityParser.ZenohCommunicationMode;
        public const string FastDdsRmwImplementation = Ros2ForUnityRuntimeCapabilityParser.FastDdsRmwImplementation;
        public const string ZenohRmwImplementation = Ros2ForUnityRuntimeCapabilityParser.ZenohRmwImplementation;
        private static readonly string[] NoCommunicationModeLabels = Array.Empty<string>();
        private static readonly IReadOnlyList<string> NoCommunicationModeIds = Array.Empty<string>();
        private const string SessionRuntimeKey = "Unity2Foxglove.R2FU.SessionRuntime";
        private const string SessionCommunicationModeKey = "Unity2Foxglove.R2FU.SessionCommunicationMode";
        private const string SessionCustomTypesupportIdentityKey =
            "Unity2Foxglove.R2FU.SessionCustomTypesupportIdentity";
        private const string CommunicationModeEditorUserSettingsKey =
            "Unity2Foxglove.R2FU.CommunicationMode";
        private const string WindowsNativePluginRelativeDirectory =
            "Runtime/Ros2ForUnity/Plugins/Windows/x86_64";
        private static string _cachedCandidatesProjectDirectory;
        private static IReadOnlyList<Ros2ForUnityRuntimeDescriptor> _cachedCandidates;
        private static string _cachedManifestProjectDirectory;
        private static DateTime _cachedManifestWriteTimeUtc;
        private static long _cachedManifestLength = -1;
        private static IReadOnlyList<string> _cachedManifestRuntimePackages;
        private static readonly Dictionary<string, string> ZenohPayloadDiagnostics =
            new Dictionary<string, string>(StringComparer.Ordinal);

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

            var candidates = DiscoverCandidateRuntimes(projectDirectory).ToArray();
            var activePackages = ReadManifestRuntimePackages(projectDirectory).ToArray();
            var activePackage = activePackages.Length == 1 ? activePackages[0] : string.Empty;
            var explicitSelection = candidates.FirstOrDefault(
                runtime => string.Equals(runtime.PackageName, activePackage, StringComparison.Ordinal));

            if (explicitSelection != null)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    activePackage,
                    explicitSelection,
                    string.Empty);
            }

            if (activePackages.Length > 1)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    string.Join(", ", activePackages),
                    null,
                    "Multiple ROS2 For Unity runtime packages are resolved in the Unity manifest. Select one active runtime.");
            }

            if (!string.IsNullOrWhiteSpace(activePackage))
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    activePackage,
                    null,
                    "The active ROS2 For Unity runtime package is not available as a repository candidate: " + activePackage);
            }

            if (candidates.Length > 0)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    string.Empty,
                    null,
                    "No ROS2 For Unity runtime package is resolved in the Unity manifest. Select a candidate to make it active.");
            }

            return new Ros2ForUnityRuntimeSelectionStatus(
                candidates,
                string.Empty,
                null,
                "No ROS2 For Unity runtime candidate package was found under the repository Packages directory.");
        }

        public static void SwitchActiveRuntimePackage(string projectDirectory, string packageName)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new InvalidOperationException("Could not resolve the Unity project directory.");

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException(
                    "Cannot switch ROS2 For Unity runtime while Play Mode, compilation, or package refresh is active.");
            }

            var candidate = DiscoverCandidateRuntimes(projectDirectory)
                .FirstOrDefault(runtime => string.Equals(runtime.PackageName, packageName, StringComparison.Ordinal));
            if (candidate == null)
                throw new InvalidOperationException("Runtime package is not a repository candidate: " + packageName);

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                projectDirectory,
                candidate.PackageName,
                requestedAddOnPackage: null,
                resolve: () => Client.Resolve());
            ThrowIfCustomTypesupportTransactionFailed(result, "switching ROS2 For Unity runtime");
            InvalidateStatusCache();
            ApplyCommunicationModeEnvironment(projectDirectory);
        }

        /// <summary>
        /// Changes only the custom typesupport member of the active runtime/add-on
        /// pair. Callers are intentionally required to use this transaction rather
        /// than editing manifest.json or packages-lock.json directly.
        /// </summary>
        public static void SwitchActiveCustomTypesupportPackage(string projectDirectory, string packageName)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new InvalidOperationException("Could not resolve the Unity project directory.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException(
                    "Cannot switch FoxRun custom ROS2 typesupport while Play Mode, compilation, or package refresh is active.");
            }

            var status = GetStatus(projectDirectory);
            if (status.SelectedRuntime == null)
                throw new InvalidOperationException("Select one valid ROS2 For Unity runtime before selecting custom ROS2 typesupport.");

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                projectDirectory,
                status.SelectedRuntime.PackageName,
                packageName,
                () => Client.Resolve());
            ThrowIfCustomTypesupportTransactionFailed(result, "selecting FoxRun custom ROS2 typesupport");
            InvalidateStatusCache();
        }

        public static string GetSessionRuntimePackage()
            => SessionState.GetString(SessionRuntimeKey, string.Empty);

        public static string GetSessionCommunicationMode()
            => SessionState.GetString(SessionCommunicationModeKey, string.Empty);

        public static string GetSessionCustomTypesupportIdentity()
            => SessionState.GetString(SessionCustomTypesupportIdentityKey, string.Empty);

        public static Ros2ForUnityCustomTypesupportSelectionResult GetActiveCustomTypesupportSelection(
            string projectDirectory)
        {
            var status = GetStatus(projectDirectory);
            return status?.SelectedRuntime == null
                ? new Ros2ForUnityCustomTypesupportSelectionResult(
                    Ros2ForUnityCustomTypesupportSelectionCode.InvalidBaseRuntime,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty)
                : Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                    projectDirectory,
                    status.SelectedRuntime.PackageName);
        }

        public static IReadOnlyList<string> GetCommunicationModeIds(Ros2ForUnityRuntimeDescriptor runtime)
            => runtime?.CommunicationModeIds ?? NoCommunicationModeIds;

        public static string[] GetCommunicationModeLabels(Ros2ForUnityRuntimeDescriptor runtime)
            => runtime?.CommunicationModeLabels ?? NoCommunicationModeLabels;

        public static string GetCommunicationModeDisplayName(
            Ros2ForUnityRuntimeDescriptor runtime,
            string mode)
        {
            var resolved = runtime?.FindCommunicationMode(mode);
            return resolved?.DisplayName ?? mode ?? string.Empty;
        }

        public static string GetRmwImplementationForCommunicationMode(
            Ros2ForUnityRuntimeDescriptor runtime,
            string mode)
        {
            var resolved = runtime?.FindCommunicationMode(mode) ?? runtime?.DefaultCommunicationMode;
            return resolved?.RmwImplementation ?? string.Empty;
        }

        public static string GetCommunicationModeForRuntime(Ros2ForUnityRuntimeDescriptor runtime)
        {
            if (runtime == null)
                return string.Empty;

            var saved = EditorUserSettings.GetConfigValue(GetCommunicationModeSettingsKey(runtime));
            if (string.IsNullOrWhiteSpace(saved))
                saved = EditorUserSettings.GetConfigValue(CommunicationModeEditorUserSettingsKey);
            return runtime.FindCommunicationMode(saved)?.Id
                   ?? runtime.DefaultCommunicationMode?.Id
                   ?? string.Empty;
        }

        public static void SetCommunicationMode(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string mode)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Cannot switch ROS2 For Unity communication mode while Play Mode is active or changing.");

            if (runtime == null)
                throw new InvalidOperationException("Select an active ROS2 For Unity runtime before changing communication mode.");

            if (runtime.FindCommunicationMode(mode) == null)
                throw new InvalidOperationException("Communication mode is not supported by the active runtime: " + mode);

            EditorUserSettings.SetConfigValue(GetCommunicationModeSettingsKey(runtime), mode);
            ApplyCommunicationModeEnvironment(projectDirectory);
        }

        public static void ApplyCommunicationModeEnvironment(string projectDirectory)
        {
            var status = GetStatus(projectDirectory);
            if (status.SelectedRuntime == null)
                return;

            var mode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            ApplySelectedRuntimeEnvironment(status.SelectedRuntime, mode);
        }

        public static void BindActiveRuntimeForPlayMode(string projectDirectory)
        {
            BindActiveRuntimeForPlayMode(GetStatus(projectDirectory));
        }

        public static void BindActiveRuntimeForPlayMode(Ros2ForUnityRuntimeSelectionStatus status)
        {
            if (status == null || status.SelectedRuntime == null)
                return;

            var projectDirectory = ProjectDirectoryFromApplication();
            var customTypesupport = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                projectDirectory,
                status.SelectedRuntime.PackageName);
            ThrowIfCustomTypesupportTransactionFailed(customTypesupport, "binding ROS2 For Unity for Play Mode");

            var communicationMode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            ApplySelectedRuntimeEnvironment(status.SelectedRuntime, communicationMode);

            if (string.IsNullOrWhiteSpace(GetSessionRuntimePackage()))
                SessionState.SetString(SessionRuntimeKey, status.SelectedRuntime.PackageName);

            if (string.IsNullOrWhiteSpace(GetSessionCommunicationMode()))
                SessionState.SetString(SessionCommunicationModeKey, communicationMode);

            if (string.IsNullOrWhiteSpace(GetSessionCustomTypesupportIdentity()))
                SessionState.SetString(SessionCustomTypesupportIdentityKey, BuildCustomTypesupportIdentity(customTypesupport));
        }

        private static void ApplySelectedRuntimeEnvironment(
            Ros2ForUnityRuntimeDescriptor runtime,
            string communicationMode)
        {
            if (runtime == null)
                return;

            // Bind the selected manifest identity before the optional runtime enters
            // its native-ready path, so post-readiness diagnostics report observed
            // process state instead of inferring a distro from the package display name.
            if (!string.IsNullOrWhiteSpace(runtime.RosDistro))
                Environment.SetEnvironmentVariable("ROS_DISTRO", runtime.RosDistro);

            var rmwImplementation = GetRmwImplementationForCommunicationMode(runtime, communicationMode);
            if (!string.IsNullOrWhiteSpace(rmwImplementation))
                Environment.SetEnvironmentVariable("RMW_IMPLEMENTATION", rmwImplementation);
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
               || !string.IsNullOrWhiteSpace(GetCommunicationModeRequiringEditorRestart(status));

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

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Cannot restart Unity while Play Mode is active or changing.");

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

            var startInfo = new ProcessStartInfo
            {
                FileName = editorExecutable,
                Arguments = "-projectPath " + QuoteEditorArgument(projectDirectory),
                WorkingDirectory = projectDirectory,
                UseShellExecute = false,
            };

            startInfo.EnvironmentVariables[NativeLibraryPathVariableName()] =
                BuildCleanRestartPath(projectDirectory, runtime, customTypesupport);
            if (!string.IsNullOrWhiteSpace(runtime.RosDistro))
                startInfo.EnvironmentVariables["ROS_DISTRO"] = runtime.RosDistro;

            var communicationMode = GetCommunicationModeForRuntime(runtime);
            var rmwImplementation = GetRmwImplementationForCommunicationMode(runtime, communicationMode);
            if (!string.IsNullOrWhiteSpace(rmwImplementation))
                startInfo.EnvironmentVariables["RMW_IMPLEMENTATION"] = rmwImplementation;

            try
            {
                if (Process.Start(startInfo) == null)
                    throw new InvalidOperationException("Unity did not start a replacement Editor process.");
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                throw new InvalidOperationException("Could not start a clean Unity Editor replacement process.", ex);
            }

            EditorApplication.Exit(0);
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

        private static string QuoteEditorArgument(string value)
            => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";

        public static IReadOnlyList<Ros2ForUnityRuntimeDescriptor> DiscoverCandidateRuntimes(string projectDirectory)
        {
            if (_cachedCandidates != null
                && string.Equals(_cachedCandidatesProjectDirectory, projectDirectory, StringComparison.Ordinal))
            {
                return _cachedCandidates;
            }

            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            if (string.IsNullOrWhiteSpace(packagesDirectory) || !Directory.Exists(packagesDirectory))
            {
                _cachedCandidatesProjectDirectory = projectDirectory;
                _cachedCandidates = Array.Empty<Ros2ForUnityRuntimeDescriptor>();
                return _cachedCandidates;
            }

            var candidates = Directory
                .GetDirectories(packagesDirectory, RuntimePackagePrefix + "*", SearchOption.TopDirectoryOnly)
                .Where(path => !IsEmbeddedPackage(projectDirectory, path))
                .Select(TryCreateDescriptor)
                .Where(descriptor => descriptor != null)
                .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ToArray();
            _cachedCandidatesProjectDirectory = projectDirectory;
            _cachedCandidates = candidates;
            return candidates;
        }

        public static string RepositoryPackagesDirectory(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return string.Empty;

            var project = new DirectoryInfo(projectDirectory);
            return Path.Combine(project.Parent?.FullName ?? string.Empty, "Packages");
        }

        public static IReadOnlyList<string> ReadManifestRuntimePackages(string projectDirectory)
        {
            var manifestPath = ManifestPath(projectDirectory);
            if (!File.Exists(manifestPath))
            {
                _cachedManifestProjectDirectory = projectDirectory;
                _cachedManifestWriteTimeUtc = DateTime.MinValue;
                _cachedManifestLength = -1;
                _cachedManifestRuntimePackages = Array.Empty<string>();
                return _cachedManifestRuntimePackages;
            }

            var manifestInfo = new FileInfo(manifestPath);
            if (_cachedManifestRuntimePackages != null
                && string.Equals(_cachedManifestProjectDirectory, projectDirectory, StringComparison.Ordinal)
                && _cachedManifestWriteTimeUtc == manifestInfo.LastWriteTimeUtc
                && _cachedManifestLength == manifestInfo.Length)
            {
                return _cachedManifestRuntimePackages;
            }

            var dependencies = ReadManifestDependencies(File.ReadAllText(manifestPath), manifestPath);
            if (dependencies == null)
            {
                _cachedManifestProjectDirectory = projectDirectory;
                _cachedManifestWriteTimeUtc = manifestInfo.LastWriteTimeUtc;
                _cachedManifestLength = manifestInfo.Length;
                _cachedManifestRuntimePackages = Array.Empty<string>();
                return _cachedManifestRuntimePackages;
            }

            var packages = dependencies
                .Properties()
                .Select(property => property.Name)
                .Where(name => name.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            _cachedManifestProjectDirectory = projectDirectory;
            _cachedManifestWriteTimeUtc = manifestInfo.LastWriteTimeUtc;
            _cachedManifestLength = manifestInfo.Length;
            _cachedManifestRuntimePackages = packages;
            return packages;
        }

        public static bool HasManifestRuntimePackage(string projectDirectory)
            => ReadManifestRuntimePackages(projectDirectory).Count > 0;

        public static void InvalidateStatusCache()
        {
            Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            _cachedCandidatesProjectDirectory = null;
            _cachedCandidates = null;
            _cachedManifestProjectDirectory = null;
            _cachedManifestWriteTimeUtc = DateTime.MinValue;
            _cachedManifestLength = -1;
            _cachedManifestRuntimePackages = null;
            ZenohPayloadDiagnostics.Clear();
        }

        private static Ros2ForUnityRuntimeDescriptor TryCreateDescriptor(string packageDirectory)
        {
            var packageName = Path.GetFileName(packageDirectory);
            if (string.IsNullOrWhiteSpace(packageName) || !packageName.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                return null;

            var packageJson = Path.Combine(packageDirectory, "package.json");
            if (!File.Exists(packageJson) || !ContainsPackageName(File.ReadAllText(packageJson), packageName))
                return null;

            var suffix = packageName.Substring(RuntimePackagePrefix.Length);
            var parts = suffix.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return null;

            var packageRosDistro = parts[0];
            var packagePlatform = string.Join(".", parts.Skip(1));
            var capabilities = ReadRuntimeCapabilities(packageDirectory);
            if (!capabilities.IsValid || capabilities.CommunicationModes.Count == 0)
                return null;

            var rosDistro = string.IsNullOrWhiteSpace(capabilities.RosDistro)
                ? packageRosDistro
                : capabilities.RosDistro;
            var platform = string.IsNullOrWhiteSpace(capabilities.Platform)
                ? packagePlatform
                : capabilities.Platform;
            var runtimeId = string.IsNullOrWhiteSpace(capabilities.RuntimeId)
                ? "r2fu-" + rosDistro + "-" + platform.Replace('.', '-')
                : capabilities.RuntimeId;
            var displayName = ToDisplayName(rosDistro) + " " + ToDisplayName(platform.Replace('.', ' '));
            var zenohPayloadDiagnostic = GetZenohPayloadDiagnostic(packageDirectory, capabilities.SupportsZenoh);

            var descriptor = new Ros2ForUnityRuntimeDescriptor(
                displayName,
                packageName,
                runtimeId,
                rosDistro,
                platform,
                capabilities,
                zenohPayloadDiagnostic);
            return descriptor.CommunicationModes.Count == 0 ? null : descriptor;
        }

        private static Ros2ForUnityRuntimeCapabilities ReadRuntimeCapabilities(string packageDirectory)
        {
            var manifestPath = Path.Combine(packageDirectory, "RuntimeSupport", "runtime-manifest.json");
            if (!File.Exists(manifestPath))
                return Ros2ForUnityRuntimeCapabilityParser.Parse(string.Empty);

            try
            {
                return Ros2ForUnityRuntimeCapabilityParser.Parse(File.ReadAllText(manifestPath));
            }
            catch
            {
                return Ros2ForUnityRuntimeCapabilityParser.Parse(string.Empty);
            }
        }

        private static string GetZenohPayloadDiagnostic(string packageDirectory, bool manifestDeclaresZenoh)
        {
            if (!manifestDeclaresZenoh)
                return string.Empty;

            var cacheKey = packageDirectory;
            if (ZenohPayloadDiagnostics.TryGetValue(cacheKey, out var cached))
                return cached;

            var diagnostic = ComputeZenohPayloadDiagnostic(packageDirectory);
            ZenohPayloadDiagnostics[cacheKey] = diagnostic;
            return diagnostic;
        }

        private static string ComputeZenohPayloadDiagnostic(string packageDirectory)
        {
            var pluginsRoot = Path.Combine(packageDirectory, "Runtime", "Ros2ForUnity", "Plugins");
            var streamingAssetsShare = Path.Combine(
                packageDirectory,
                "Runtime",
                "Ros2ForUnity",
                "StreamingAssets",
                "Ros2ForUnity",
                "share",
                "rmw_zenoh_cpp",
                "config");
            if (!Directory.Exists(pluginsRoot))
                return "Zenoh communication mode is unavailable because this runtime package has no native plugin directory.";

            var pluginRoots = Directory
                .EnumerateDirectories(pluginsRoot, "*", SearchOption.AllDirectories)
                .ToArray();
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var pluginRoot in pluginRoots)
            {
                var pluginShare = Path.Combine(pluginRoot, "share", "rmw_zenoh_cpp", "config");
                var required = new[]
                {
                    (Present: HasNativeLibrary(pluginRoot, "rmw_zenoh_cpp"), Name: "rmw_zenoh_cpp native library"),
                    (Present: HasNativeLibrary(pluginRoot, "zenohc"), Name: "zenohc native library"),
                    (Present: File.Exists(Path.Combine(pluginShare, "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5")), Name: "plugin Zenoh session config"),
                    (Present: File.Exists(Path.Combine(pluginShare, "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5")), Name: "plugin Zenoh router config"),
                    (Present: File.Exists(Path.Combine(streamingAssetsShare, "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5")), Name: "StreamingAssets Zenoh session config"),
                    (Present: File.Exists(Path.Combine(streamingAssetsShare, "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5")), Name: "StreamingAssets Zenoh router config"),
                };
                if (required.All(item => item.Present))
                    return string.Empty;

                foreach (var item in required.Where(item => !item.Present))
                    missing.Add(item.Name);
            }

            if (missing.Count == 0)
                missing.Add("complete Zenoh native payload");

            return "Zenoh communication mode is unavailable for this runtime package. Missing: "
                   + string.Join(", ", missing)
                   + ". Rebuild or re-import the runtime ZIP with rmw_zenoh_cpp and config assets.";
        }

        private static bool HasNativeLibrary(string directory, string libraryName)
        {
            return File.Exists(Path.Combine(directory, libraryName + ".dll"))
                   || File.Exists(Path.Combine(directory, "lib" + libraryName + ".so"))
                   || File.Exists(Path.Combine(directory, "lib" + libraryName + ".dylib"));
        }

        private static bool IsEmbeddedPackage(string projectDirectory, string packageDirectory)
        {
            var embeddedRoot = Path.GetFullPath(Path.Combine(projectDirectory, "Packages"));
            var candidate = Path.GetFullPath(packageDirectory);
            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return candidate.StartsWith(embeddedRoot + Path.DirectorySeparatorChar, comparison);
        }

        private static bool ContainsPackageName(string json, string packageName)
        {
            try
            {
                return string.Equals((string)JObject.Parse(json ?? string.Empty)["name"], packageName, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ToDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(
                " ",
                value.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static string ManifestPath(string projectDirectory)
            => Path.Combine(projectDirectory, "Packages", "manifest.json");

        private static JObject ReadManifestJson(string manifest, string manifestPath)
        {
            try
            {
                return JObject.Parse(manifest ?? string.Empty);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unity package manifest is not valid JSON: " + manifestPath, ex);
            }
        }

        private static JObject ReadManifestDependencies(string manifest, string manifestPath)
        {
            return ReadManifestJson(manifest, manifestPath)["dependencies"] as JObject;
        }

        private static void ValidateManifestJson(string manifest, string manifestPath)
        {
            ReadManifestJson(manifest, manifestPath);
        }

        private static string RemoveRuntimePackageDependencies(string manifest)
        {
            var root = ReadManifestJson(manifest, "Unity package manifest");
            var dependencies = root["dependencies"] as JObject
                ?? throw new InvalidOperationException("Unity package manifest is missing a dependencies object.");
            foreach (var property in dependencies.Properties()
                         .Where(property => property.Name.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                         .ToArray())
            {
                property.Remove();
            }

            return SerializeManifest(root, DetectLineEnding(manifest));
        }

        private static string AddRuntimePackageDependency(string manifest, string packageName, string projectDirectory)
        {
            var lineEnding = DetectLineEnding(manifest);
            var root = ReadManifestJson(manifest, "Unity package manifest");
            var dependencies = root["dependencies"] as JObject
                ?? throw new InvalidOperationException("Unity package manifest is missing a dependencies object.");
            var anchor = dependencies.Property("dev.unity2foxglove.ros2forunity")
                         ?? dependencies.Property("dev.unity2foxglove.sdk")
                         ?? dependencies.Properties().FirstOrDefault();
            if (anchor == null)
                throw new InvalidOperationException("Unity package manifest dependencies object is empty; cannot anchor the ROS2 For Unity runtime package.");

            anchor.AddAfterSelf(new JProperty(packageName, BuildRuntimePackageReference(projectDirectory, packageName)));
            return SerializeManifest(root, lineEnding);
        }

        private static string SerializeManifest(JObject root, string lineEnding)
        {
            var serialized = root.ToString(Newtonsoft.Json.Formatting.Indented)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", lineEnding);
            return serialized.TrimEnd() + lineEnding;
        }

        private static string DetectLineEnding(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Environment.NewLine;

            var crlfIndex = text.IndexOf("\r\n", StringComparison.Ordinal);
            var lfIndex = text.IndexOf('\n');
            var crIndex = text.IndexOf('\r');
            if (crlfIndex >= 0 && (lfIndex < 0 || crlfIndex <= lfIndex))
                return "\r\n";
            if (lfIndex >= 0)
                return "\n";
            if (crIndex >= 0)
                return "\r";
            return Environment.NewLine;
        }

        private static string BuildRuntimePackageReference(string projectDirectory, string packageName)
        {
            var projectPackagesDirectory = Path.Combine(projectDirectory, "Packages");
            var runtimePackageDirectory = Path.Combine(RepositoryPackagesDirectory(projectDirectory), packageName);
            var relativePath = GetRelativePath(projectPackagesDirectory, runtimePackageDirectory)
                .Replace('\\', '/')
                .TrimStart('/');
            return "file:" + relativePath;
        }

        private static string GetRelativePath(string fromDirectory, string toPath)
        {
            var fromFull = TrimDirectorySeparators(Path.GetFullPath(fromDirectory));
            var toFull = TrimDirectorySeparators(Path.GetFullPath(toPath));
            var fromRoot = Path.GetPathRoot(fromFull) ?? string.Empty;
            var toRoot = Path.GetPathRoot(toFull) ?? string.Empty;
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!string.Equals(fromRoot, toRoot, comparison))
                return toFull;

            var fromParts = SplitPathParts(fromFull.Substring(fromRoot.Length));
            var toParts = SplitPathParts(toFull.Substring(toRoot.Length));
            var common = 0;
            while (common < fromParts.Length
                   && common < toParts.Length
                   && string.Equals(fromParts[common], toParts[common], comparison))
            {
                common++;
            }

            var parts = new List<string>();
            for (var i = common; i < fromParts.Length; i++)
                parts.Add("..");
            for (var i = common; i < toParts.Length; i++)
                parts.Add(toParts[i]);

            return parts.Count == 0 ? "." : string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Path.DirectorySeparatorChar.ToString();

            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                   || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string TrimDirectorySeparators(string path)
        {
            var root = Path.GetPathRoot(path) ?? string.Empty;
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length < root.Length ? root : trimmed;
        }

        private static string[] SplitPathParts(string path)
            => path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        private static string GetCommunicationModeSettingsKey(Ros2ForUnityRuntimeDescriptor runtime)
            => runtime == null || string.IsNullOrWhiteSpace(runtime.PackageName)
                ? CommunicationModeEditorUserSettingsKey
                : CommunicationModeEditorUserSettingsKey + "." + runtime.PackageName;

        private static void WriteManifestAtomically(string manifestPath, string manifest)
        {
            var directory = Path.GetDirectoryName(manifestPath);
            var tempPath = Path.Combine(directory ?? string.Empty, Path.GetFileName(manifestPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(tempPath, manifest);
            try
            {
                File.Replace(tempPath, manifestPath, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tempPath, manifestPath, overwrite: true);
                File.Delete(tempPath);
            }
            catch (IOException) when (!File.Exists(manifestPath))
            {
                File.Move(tempPath, manifestPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}
#endif
