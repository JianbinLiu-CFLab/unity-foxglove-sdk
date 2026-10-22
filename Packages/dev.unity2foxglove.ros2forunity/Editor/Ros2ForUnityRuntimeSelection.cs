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
    /// <remarks>Phase192 source sections: SessionAndCommunication.cs, RestartAndEnvironment.cs, Discovery.cs, Manifest.cs, EnvironmentLease.cs</remarks>
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
    internal static partial class Ros2ForUnityRuntimeSelection
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
        private const string SessionZenohRouterEndpointKey = "Unity2Foxglove.R2FU.SessionZenohRouterEndpoint";
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
        private static DateTime _cachedLockWriteTimeUtc;
        private static long _cachedLockLength = -1;
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

            var originalManifest = Ros2ForUnityCustomTypesupportSelectionTransaction.ReadManifestText(projectDirectory);
            Ros2ForUnityInteractiveSelectionCoordinator.Begin(
                projectDirectory,
                candidate.PackageName,
                string.Empty,
                Ros2ForUnityInteractiveSelectionKind.Runtime,
                originalManifest);
            try
            {
                var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                    projectDirectory,
                    candidate.PackageName,
                    requestedAddOnPackage: null,
                    resolve: () => Client.Resolve());
                if (result.Code == Ros2ForUnityCustomTypesupportSelectionCode.ResolvePending)
                    return;
                ThrowIfCustomTypesupportTransactionFailed(result, "switching ROS2 For Unity runtime");
                CompleteInteractiveSelection(projectDirectory, Ros2ForUnityInteractiveSelectionKind.Runtime);
                Ros2ForUnityInteractiveSelectionCoordinator.Complete();
            }
            catch
            {
                Ros2ForUnityInteractiveSelectionCoordinator.Cancel();
                throw;
            }
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

            var originalManifest = Ros2ForUnityCustomTypesupportSelectionTransaction.ReadManifestText(projectDirectory);
            Ros2ForUnityInteractiveSelectionCoordinator.Begin(
                projectDirectory,
                status.SelectedRuntime.PackageName,
                packageName,
                Ros2ForUnityInteractiveSelectionKind.CustomTypesupport,
                originalManifest);
            try
            {
                var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                    projectDirectory,
                    status.SelectedRuntime.PackageName,
                    packageName,
                    resolve: () => Client.Resolve());
                if (result.Code == Ros2ForUnityCustomTypesupportSelectionCode.ResolvePending)
                    return;
                ThrowIfCustomTypesupportTransactionFailed(result, "selecting FoxRun custom ROS2 typesupport");
                CompleteInteractiveSelection(projectDirectory, Ros2ForUnityInteractiveSelectionKind.CustomTypesupport);
                Ros2ForUnityInteractiveSelectionCoordinator.Complete();
            }
            catch
            {
                Ros2ForUnityInteractiveSelectionCoordinator.Cancel();
                throw;
            }
        }

        internal static void CompleteInteractiveSelection(
            string projectDirectory,
            Ros2ForUnityInteractiveSelectionKind kind)
        {
            InvalidateStatusCache();
            if (kind == Ros2ForUnityInteractiveSelectionKind.Runtime)
                ApplyCommunicationModeEnvironment(projectDirectory);
            Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForInteractiveSelection();
            if (kind == Ros2ForUnityInteractiveSelectionKind.CustomTypesupport)
                Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
        }
    }
}
#endif
