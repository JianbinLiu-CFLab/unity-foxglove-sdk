// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor/Phase181
// Purpose: Select one explicit Phase181 runtime/add-on pair in Unity Batch Mode.

#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    /// <summary>
    /// Explicit Batch-only runtime selector for the isolated Phase181 Windows
    /// acceptance rows. It uses the normal atomic selection transaction and
    /// waits for its Package Manager resolve; it never infers a runtime from
    /// the host environment or initializes ROS2.
    /// </summary>
    public static class Phase181Ros2RuntimeBatchSelection
    {
        private const string DistroArgument = "-phase181Ros2Distro";
        private const string CommunicationModeArgument = "-phase181Ros2CommunicationMode";
        private const double ResolveTimeoutSeconds = 300.0;
        private const string PendingRuntimePackageKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.RuntimePackage";
        private const string PendingAddOnPackageKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.AddOnPackage";
        private const string PendingCommunicationModeKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.CommunicationMode";
        private const string PendingDeadlineUtcTicksKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.DeadlineUtcTicks";

        private static bool _resolveRequested;
        private static bool _receivedRegisteredPackages;
        private static string _projectDirectory;
        private static string _runtimePackage;
        private static string _addOnPackage;
        private static string _communicationMode;
        private static DateTime _deadlineUtc;

        /// <summary>
        /// Package resolution can reload the Editor domain and remove ordinary
        /// static delegates. Resume the same bounded batch selection from its
        /// SessionState hand-off rather than leaving the process alive after a
        /// successful package switch.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ResumePendingSelectionAfterDomainReload()
        {
            if (!Application.isBatchMode || !TryRestorePendingSelection())
                return;

            _resolveRequested = true;
            _receivedRegisteredPackages = false;
            AttachCallbacks();
            EditorApplication.delayCall += CompleteSelectionWhenResolved;
        }

        /// <summary>
        /// Batch entry point. Invoke with <c>-phase181Ros2Distro</c> set to
        /// humble, jazzy, or lyrical and <c>-phase181Ros2CommunicationMode</c>
        /// set to fastdds or zenoh. The command owns the selection transaction
        /// and communication-mode binding, then exits after Package Manager
        /// resolution completes.
        /// </summary>
        public static void SelectFromCommandLine()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Phase181Ros2RuntimeBatchSelection requires Unity Batch Mode.");

            var distro = RequireDistroArgument();
            _communicationMode = RequireCommunicationModeArgument();
            _projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            _runtimePackage = Ros2ForUnityRuntimeSelection.RuntimePackagePrefix + distro + ".win64";
            _addOnPackage = Ros2ForUnityCustomTypesupportSelectionTransaction.CustomTypesupportPackagePrefix
                + distro + ".win64";
            ClearPendingSelection();
            _resolveRequested = false;
            _receivedRegisteredPackages = false;
            _deadlineUtc = DateTime.UtcNow.AddSeconds(ResolveTimeoutSeconds);
            AttachCallbacks();

            var selection = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                _projectDirectory,
                _runtimePackage,
                _addOnPackage,
                () =>
                {
                    _resolveRequested = true;
                    PersistPendingSelection();
                    Client.Resolve();
                });
            if (!selection.IsReady
                || !string.Equals(selection.ActiveAddOnPackage, _addOnPackage, StringComparison.Ordinal))
            {
                DetachCallbacks();
                ClearPendingSelection();
                throw new InvalidOperationException("Phase181 Batch runtime selection rejected the requested validated pair.");
            }
            if (!_resolveRequested)
            {
                DetachCallbacks();
                ClearPendingSelection();
                throw new InvalidOperationException("Phase181 Batch runtime selection did not start Package Manager resolution.");
            }

            AttachCallbacks();
        }

        private static void CompleteSelectionWhenResolved()
        {
            if (!_resolveRequested || !AreSelectedPackagesRegistered())
            {
                if (DateTime.UtcNow > _deadlineUtc)
                    FailAndExit("registered-packages-timeout", 2);
                return;
            }

            Ros2ForUnityRuntimeSelection.InvalidateStatusCache();
            var selection = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                _projectDirectory,
                _runtimePackage);
            if (!selection.IsReady
                || !string.Equals(selection.ActiveAddOnPackage, _addOnPackage, StringComparison.Ordinal))
            {
                FailAndExit("post-resolve-validation-failed", 4);
                return;
            }

            var status = Ros2ForUnityRuntimeSelection.GetStatus(_projectDirectory);
            var runtime = status.SelectedRuntime;
            if (runtime == null
                || !string.Equals(runtime.PackageName, _runtimePackage, StringComparison.Ordinal)
                || runtime.FindCommunicationMode(_communicationMode) == null)
            {
                FailAndExit("communication-mode-unavailable", 5);
                return;
            }

            Ros2ForUnityRuntimeSelection.SetCommunicationMode(
                _projectDirectory,
                runtime,
                _communicationMode);
            var selectedCommunicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(runtime);
            var selectedRmw = Ros2ForUnityRuntimeSelection.GetRmwImplementationForCommunicationMode(
                runtime,
                selectedCommunicationMode);
            if (!string.Equals(selectedCommunicationMode, _communicationMode, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(selectedRmw))
            {
                FailAndExit("communication-mode-binding-failed", 6);
                return;
            }

            Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();
            DetachCallbacks();
            ClearPendingSelection();
            Debug.Log(
                "PHASE181_BATCH_RUNTIME_SELECTION_READY runtime=" + _runtimePackage
                + " addon=" + _addOnPackage
                + " communicationMode=" + selectedCommunicationMode
                + " rmw=" + selectedRmw
                + " registeredEvent=" + _receivedRegisteredPackages);
            EditorApplication.Exit(0);
        }

        private static void FailAndExit(string outcome, int exitCode)
        {
            DetachCallbacks();
            ClearPendingSelection();
            Debug.LogError("PHASE181_BATCH_RUNTIME_SELECTION_FAIL outcome=" + outcome + " exitCode=" + exitCode);
            EditorApplication.Exit(exitCode);
        }

        private static void OnPackagesRegistered(PackageRegistrationEventArgs _)
        {
            _receivedRegisteredPackages = true;
        }

        private static bool AreSelectedPackagesRegistered()
        {
            var runtime = PackageManagerPackageInfo.FindForPackageName(_runtimePackage);
            var addOn = PackageManagerPackageInfo.FindForPackageName(_addOnPackage);
            return runtime != null
                   && runtime.isDirectDependency
                   && addOn != null
                   && addOn.isDirectDependency;
        }

        private static void PersistPendingSelection()
        {
            SessionState.SetString(PendingRuntimePackageKey, _runtimePackage);
            SessionState.SetString(PendingAddOnPackageKey, _addOnPackage);
            SessionState.SetString(PendingCommunicationModeKey, _communicationMode);
            SessionState.SetString(
                PendingDeadlineUtcTicksKey,
                _deadlineUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryRestorePendingSelection()
        {
            var runtimePackage = SessionState.GetString(PendingRuntimePackageKey, string.Empty);
            var addOnPackage = SessionState.GetString(PendingAddOnPackageKey, string.Empty);
            var communicationMode = SessionState.GetString(PendingCommunicationModeKey, string.Empty);
            var deadlineTicksText = SessionState.GetString(PendingDeadlineUtcTicksKey, string.Empty);
            if (string.IsNullOrWhiteSpace(runtimePackage)
                || string.IsNullOrWhiteSpace(addOnPackage)
                || string.IsNullOrWhiteSpace(communicationMode)
                || !long.TryParse(
                    deadlineTicksText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var deadlineTicks))
            {
                return false;
            }

            try
            {
                _deadlineUtc = new DateTime(deadlineTicks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                ClearPendingSelection();
                return false;
            }

            _projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            _runtimePackage = runtimePackage;
            _addOnPackage = addOnPackage;
            _communicationMode = communicationMode;
            return true;
        }

        private static void ClearPendingSelection()
        {
            SessionState.SetString(PendingRuntimePackageKey, string.Empty);
            SessionState.SetString(PendingAddOnPackageKey, string.Empty);
            SessionState.SetString(PendingCommunicationModeKey, string.Empty);
            SessionState.SetString(PendingDeadlineUtcTicksKey, string.Empty);
        }

        private static void AttachCallbacks()
        {
            Events.registeredPackages -= OnPackagesRegistered;
            Events.registeredPackages += OnPackagesRegistered;
            EditorApplication.update -= CompleteSelectionWhenResolved;
            EditorApplication.update += CompleteSelectionWhenResolved;
        }

        private static void DetachCallbacks()
        {
            EditorApplication.update -= CompleteSelectionWhenResolved;
            Events.registeredPackages -= OnPackagesRegistered;
        }

        private static string RequireDistroArgument()
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, DistroArgument);
            if (index < 0 || index + 1 >= arguments.Length)
                throw new InvalidOperationException("Phase181 Batch runtime selection requires -phase181Ros2Distro.");

            var distro = arguments[index + 1];
            if (!string.Equals(distro, "humble", StringComparison.Ordinal)
                && !string.Equals(distro, "jazzy", StringComparison.Ordinal)
                && !string.Equals(distro, "lyrical", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Phase181 Batch runtime selection received an unsupported ROS2 distribution.");
            }

            return distro;
        }

        private static string RequireCommunicationModeArgument()
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, CommunicationModeArgument);
            if (index < 0 || index + 1 >= arguments.Length)
            {
                throw new InvalidOperationException(
                    "Phase181Ros2RuntimeBatchSelection requires -phase181Ros2CommunicationMode.");
            }

            var mode = arguments[index + 1];
            if (!string.Equals(mode, Ros2ForUnityRuntimeSelection.FastDdsCommunicationMode, StringComparison.Ordinal)
                && !string.Equals(mode, Ros2ForUnityRuntimeSelection.ZenohCommunicationMode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Phase181 Batch runtime selection received an unsupported communication mode.");
            }

            return mode;
        }
    }
}
#endif
