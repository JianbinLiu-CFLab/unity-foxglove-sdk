// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Data Transport presentation for R2FU-projected FoxRun custom ROS2 typesupport.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    /// <summary>
    /// Draws readiness evidence for generated custom FoxRun ROS2 interfaces.
    /// This is intentionally a presentation layer: package selection delegates
    /// to the 181-C transaction and source actions invoke the 181-B command.
    /// It never resolves packages, edits manifests, or loads native DLLs.
    /// </summary>
    public static class FoxRunRos2CustomTypesupportInspector
    {
        private const string GenerateSourceMenuItem = "Foxglove/FoxRun/Generate ROS2 Interface Source Package";
        private const string ValidateSourceMenuItem = "Foxglove/FoxRun/Validate ROS2 Interface Source Package";
        private const string OpenSourceMenuItem = "Foxglove/FoxRun/Open ROS2 Interface Source Package";
        private static string _zenohRouterSettingsError;

        /// <summary>
        /// Draws an R2FU-local projection over one validated neutral
        /// declaration snapshot. No ROS-specific shape is retained by the
        /// core SDK.
        /// </summary>
        public static void DrawCustomTypesupportPreflight(
            IReadOnlyList<
                Ros2ForUnityCustomTypesupportContract> contracts)
        {
            contracts ??=
                Array.Empty<
                    Ros2ForUnityCustomTypesupportContract>();
            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            var runtimeStatus = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);
            var runtime = runtimeStatus?.SelectedRuntime;
            var activeAddOns = Ros2ForUnityCustomTypesupportSelectionTransaction.GetActiveAddOnPackageIds(projectDirectory);
            var activeAddOn = activeAddOns.Count == 1 ? activeAddOns[0] : string.Empty;
            var source = Ros2ForUnityCustomTypesupportDiscovery
                .Discover(projectDirectory, activeAddOn)
                .Source;
            contracts = QualifyContracts(
                contracts,
                source.RosPackageName);
            var selection = runtime == null
                ? null
                : Ros2ForUnityRuntimeSelection.GetActiveCustomTypesupportSelection(projectDirectory);
            var result = Ros2ForUnityCustomTypesupportPreflight.Evaluate(
                new Ros2ForUnityCustomTypesupportPreflightInput(
                    projectDirectory,
                    hasCustomNativeContract: contracts.Count > 0,
                    runtime?.PackageName ?? string.Empty,
                    runtime?.RosDistro ?? string.Empty,
                    runtime == null
                        ? string.Empty
                        : Ros2ForUnityRuntimeSelection.GetRmwImplementationForCommunicationMode(
                            runtime,
                            Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(runtime)),
                    editorReloadSettled: !EditorApplication.isCompiling && !EditorApplication.isUpdating,
                    customCompileSymbolDefined: HasCustomTypesupportCompileSymbol(),
                    selection,
                    activeAddOns,
                    Ros2ForUnityCustomTypesupportSelectionTransaction.DiscoverCandidatePackageIds(projectDirectory),
                    contracts));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Custom FoxRun ROS 2 Interface", EditorStyles.boldLabel);
            DrawReadOnlyIdentity(result);
            EditorGUILayout.HelpBox(
                result.Diagnostic + "\n" + result.Action,
                result.IsReady ? MessageType.Info : MessageType.Warning);
            DrawZenohRouterSettings(projectDirectory, runtime);
            DrawContracts(result.Contracts);
            DrawSourceActions();
            DrawAddOnSelection(projectDirectory, runtime, result);
        }

        private static IReadOnlyList<
            Ros2ForUnityCustomTypesupportContract>
            QualifyContracts(
                IReadOnlyList<
                    Ros2ForUnityCustomTypesupportContract> contracts,
                string rosPackageName)
        {
            return contracts
                .Where(contract => contract != null)
                .Select(contract =>
                {
                    var envelope =
                        contract.CanonicalEnvelopeType;
                    if (!string.IsNullOrWhiteSpace(
                            rosPackageName)
                        && !envelope.Contains(
                            "/",
                            StringComparison.Ordinal))
                    {
                        envelope =
                            rosPackageName
                            + "/msg/"
                            + envelope;
                    }

                    return new
                        Ros2ForUnityCustomTypesupportContract(
                            envelope,
                            contract.DirectionalPolicy);
                })
                .GroupBy(
                    contract =>
                        contract.CanonicalEnvelopeType
                        + "\u001f"
                        + contract.DirectionalPolicy,
                    StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(
                    contract =>
                        contract.CanonicalEnvelopeType,
                    StringComparer.Ordinal)
                .ThenBy(
                    contract =>
                        contract.DirectionalPolicy,
                    StringComparer.Ordinal)
                .ToArray();
        }

        private static void DrawReadOnlyIdentity(Ros2ForUnityCustomTypesupportPreflightResult result)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Static Interface Package", EmptyAsNone(result.StaticPackageId));
                EditorGUILayout.TextField("ROS Package", EmptyAsNone(result.RosPackageName));
                EditorGUILayout.TextField("Interface Revision", result.InterfaceRevision.ToString());
                EditorGUILayout.TextField("Interface Digest", EmptyAsNone(result.ShortInterfaceDigest));
                EditorGUILayout.TextField("Resolved Typesupport Add-On", EmptyAsNone(result.ActiveAddOnPackage));
                EditorGUILayout.TextField("Selected RMW", EmptyAsNone(result.RmwImplementation));
            }
        }

        private static void DrawContracts(IReadOnlyList<Ros2ForUnityCustomTypesupportContract> contracts)
        {
            if (contracts == null || contracts.Count == 0)
                return;

            EditorGUILayout.LabelField("Generated Contracts", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var contract in contracts)
                {
                    EditorGUILayout.LabelField("Canonical Envelope", contract.CanonicalEnvelopeType);
                    EditorGUILayout.LabelField("Directional Policy", contract.DirectionalPolicy);
                }
            }
        }

        private static void DrawZenohRouterSettings(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime)
        {
            if (runtime == null)
                return;

            var communicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(runtime);
            var rmwImplementation = Ros2ForUnityRuntimeSelection.GetRmwImplementationForCommunicationMode(
                runtime,
                communicationMode);
            if (!string.Equals(
                    rmwImplementation,
                    Ros2ForUnityRuntimeSelection.ZenohRmwImplementation,
                    StringComparison.Ordinal))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Zenoh Router", EditorStyles.boldLabel);
            var endpoint = Ros2ForUnityZenohRouterSettings.Get(runtime);
            var address = endpoint.Address;
            var port = endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var settingsChangeBlocked = EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(settingsChangeBlocked))
            {
                EditorGUI.BeginChangeCheck();
                address = EditorGUILayout.DelayedTextField("Router Address", address);
                port = EditorGUILayout.DelayedTextField("Router Port", port);
                if (EditorGUI.EndChangeCheck())
                {
                    if (Ros2ForUnityZenohRouterSettings.TrySet(
                            projectDirectory,
                            runtime,
                            address,
                            port,
                            out var error))
                    {
                        _zenohRouterSettingsError = string.Empty;
                        endpoint = Ros2ForUnityZenohRouterSettings.Get(runtime);
                    }
                    else
                    {
                        _zenohRouterSettingsError = error;
                    }
                }
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Effective Endpoint", endpoint.Endpoint);

            if (settingsChangeBlocked)
            {
                EditorGUILayout.HelpBox(
                    "Exit Play Mode before changing the shared Zenoh Router Address or Router Port.",
                    MessageType.Info);
            }
            else if (!string.IsNullOrWhiteSpace(_zenohRouterSettingsError))
            {
                EditorGUILayout.HelpBox(_zenohRouterSettingsError, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "This endpoint is shared by every Zenoh R2FU session in this Unity project. Restart Unity after changing it once native ROS2 has loaded.",
                    MessageType.Info);
            }
        }

        private static void DrawSourceActions()
        {
            EditorGUILayout.LabelField("Static Interface Source", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("Generate Source Package"))
                        ExecuteSourceMenuItem(GenerateSourceMenuItem);
                    if (GUILayout.Button("Validate Source Package"))
                        ExecuteSourceMenuItem(ValidateSourceMenuItem);
                }

                if (GUILayout.Button("Open Source Package"))
                    ExecuteSourceMenuItem(OpenSourceMenuItem);
            }
        }

        private static void DrawAddOnSelection(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            Ros2ForUnityCustomTypesupportPreflightResult result)
        {
            var candidates = result.CandidateAddOnPackages ?? Array.Empty<string>();
            if (candidates.Count == 0)
                return;

            EditorGUILayout.LabelField("Typesupport Add-On", EditorStyles.boldLabel);
            var matchingCandidates = FilterCandidatesForRuntime(candidates, runtime);
            if (matchingCandidates.Count != 1)
            {
                EditorGUILayout.HelpBox(
                    "No unique typesupport add-on matches the active ROS2 For Unity runtime. "
                    + "Keep exactly one validated add-on for its distribution and platform in the repository Packages directory.",
                    MessageType.Warning);
                return;
            }

            var matchingAddOn = matchingCandidates[0];
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Matching Add-On", matchingAddOn);
            EditorGUILayout.HelpBox(
                "Install and Select writes this matching add-on into Unity's package manifest and then reloads packages. "
                + "It is required before custom native ROS2 contracts can enter Play Mode.",
                MessageType.Info);

            var selectionChangeBlocked = EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating;
            if (selectionChangeBlocked)
            {
                EditorGUILayout.HelpBox(
                    "Exit Play Mode and wait for Unity compilation and package refresh to finish before changing custom typesupport.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(selectionChangeBlocked))
            {
                if (GUILayout.Button("Install and Select Matching Typesupport Add-On"))
                    SelectMatchingAddOn(projectDirectory, runtime, matchingAddOn);
            }
        }

        private static IReadOnlyList<string> FilterCandidatesForRuntime(
            IReadOnlyList<string> candidates,
            Ros2ForUnityRuntimeDescriptor runtime)
        {
            if (runtime == null
                || string.IsNullOrWhiteSpace(runtime.RosDistro)
                || string.IsNullOrWhiteSpace(runtime.Platform))
            {
                return Array.Empty<string>();
            }

            var packageSuffix = "." + runtime.RosDistro + "." + runtime.Platform;
            return candidates
                .Where(candidate => candidate.EndsWith(packageSuffix, StringComparison.Ordinal))
                .ToArray();
        }

        private static void ExecuteSourceMenuItem(string menuItem)
        {
            if (!EditorApplication.ExecuteMenuItem(menuItem))
            {
                Debug.LogError("Unity2Foxglove could not run the requested FoxRun ROS 2 source command.");
                return;
            }

            Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
        }

        private static void SelectMatchingAddOn(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string packageName)
        {
            try
            {
                Ros2ForUnityRuntimeSelection.SwitchActiveCustomTypesupportPackage(
                    projectDirectory,
                    packageName);
                Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();
                Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError(
                    "Unity2Foxglove could not select the requested custom ROS2 typesupport add-on: "
                    + exception.Message);
            }
            catch (Exception)
            {
                Debug.LogError(
                    "Unity2Foxglove could not select the requested custom ROS2 typesupport add-on. "
                    + "Inspect the bounded readiness status above and choose a matching verified add-on.");
            }
        }

        private static bool HasCustomTypesupportCompileSymbol()
        {
            var symbols = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            return symbols
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(symbol => string.Equals(
                    symbol.Trim(),
                    Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol,
                    StringComparison.Ordinal));
        }

        private static string EmptyAsNone(string value)
            => string.IsNullOrWhiteSpace(value) ? "None" : value;
    }
}
#endif
