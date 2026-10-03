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
    /// acceptance rows. It uses the shared selection lifecycle core and waits
    /// for its Package Manager resolve; it never infers a runtime from the host
    /// environment or initializes ROS2.
    /// </summary>
    public static class Phase181Ros2RuntimeBatchSelection
    {
        private const string DistroArgument = "-phase181Ros2Distro";
        private const string CommunicationModeArgument = "-phase181Ros2CommunicationMode";
        private const string PendingGenerationKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.Generation";
        private const string PendingRuntimePackageKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.RuntimePackage";
        private const string PendingAddOnPackageKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.AddOnPackage";
        private const string PendingCommunicationModeKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.CommunicationMode";
        private const string PendingOriginalManifestKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.OriginalManifest";
        private const string PendingDeadlineUtcTicksKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.DeadlineUtcTicks";
        private const string PendingStatusKey =
            "Unity2Foxglove.Phase181Ros2RuntimeBatchSelection.Status";

        private static bool _resolveRequested;
        private static bool _receivedRegisteredPackages;
        private static string _projectDirectory;
        private static string _runtimePackage;
        private static string _addOnPackage;
        private static string _communicationMode;
        private static Ros2ForUnityInteractiveSelectionState _selectionState;
        private static Ros2ForUnityInteractiveSelectionCoordinatorCore _lifecycle;
        private static Ros2ForUnityRuntimeDescriptor _validatedRuntime;
        private static string _validatedCommunicationMode;
        private static string _validatedRmw;
        private static string _failureOutcome;
        private static int _failureExitCode;

        private sealed class BatchSelectionBackend : IRos2ForUnitySelectionBackend
        {
            public bool IsRegistered(Ros2ForUnityInteractiveSelectionState state)
                => AreSelectedPackagesRegistered();

            public bool IsValid(Ros2ForUnityInteractiveSelectionState state)
                => ValidateSelection();

            public void Commit(Ros2ForUnityInteractiveSelectionState state)
                => CommitSelection();

            public void Resolve()
                => ResolvePackageManager();

            public void Rollback(Ros2ForUnityInteractiveSelectionState state)
                => Ros2ForUnityCustomTypesupportSelectionTransaction.RestoreManifest(
                    state.ProjectDirectory,
                    state.OriginalManifest);
        }

        private static readonly BatchSelectionBackend Backend =
            new BatchSelectionBackend();

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
            var communicationMode = RequireCommunicationModeArgument();
            var projectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication();
            var runtimePackage = Ros2ForUnityRuntimeSelection.RuntimePackagePrefix + distro + ".win64";
            var addOnPackage = Ros2ForUnityCustomTypesupportSelectionTransaction.CustomTypesupportPackagePrefix
                + distro + ".win64";
            var originalManifest = Ros2ForUnityCustomTypesupportSelectionTransaction.ReadManifestText(
                projectDirectory);
            ClearPendingSelection();
            _projectDirectory = projectDirectory;
            _runtimePackage = runtimePackage;
            _addOnPackage = addOnPackage;
            _communicationMode = communicationMode;
            _resolveRequested = false;
            _receivedRegisteredPackages = false;
            _failureOutcome = string.Empty;
            _failureExitCode = 1;
            var generation = SessionState.GetInt(PendingGenerationKey, 0) + 1;
            _selectionState = Ros2ForUnityInteractiveSelectionState.Begin(
                generation,
                _projectDirectory,
                _runtimePackage,
                _addOnPackage,
                originalManifest,
                Ros2ForUnityInteractiveSelectionKind.Runtime,
                DateTime.UtcNow,
                Ros2ForUnitySelectionLifecycleDefaults.ResolveTimeout);
            _lifecycle = new Ros2ForUnityInteractiveSelectionCoordinatorCore();
            _lifecycle.Begin(_selectionState);
            AttachCallbacks();

            var selection = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                _projectDirectory,
                _runtimePackage,
                _addOnPackage,
                resolve: ResolvePackageManager);
            if ((selection.Code != Ros2ForUnityCustomTypesupportSelectionCode.ResolvePending
                 && !selection.IsReady)
                || !string.Equals(
                    selection.ActiveAddOnPackage,
                    _addOnPackage,
                    StringComparison.Ordinal))
            {
                RestoreOriginalManifestBeforeFailure();
                DetachCallbacks();
                ClearPendingSelection();
                throw new InvalidOperationException("Phase181 Batch runtime selection rejected the requested validated pair.");
            }
            if (!_resolveRequested)
            {
                RestoreOriginalManifestBeforeFailure();
                DetachCallbacks();
                ClearPendingSelection();
                throw new InvalidOperationException("Phase181 Batch runtime selection did not start Package Manager resolution.");
            }

            PersistPendingSelection();
            AttachCallbacks();
        }

        private static void CompleteSelectionWhenResolved()
        {
            if (!_resolveRequested
                || _lifecycle == null
                || !_lifecycle.HasOwner)
            {
                return;
            }

            if (_lifecycle.Status == Ros2ForUnitySelectionLifecycleStatus.TimedOut
                || _lifecycle.MarkTimedOut(DateTime.UtcNow))
            {
                FailAndExit("registered-packages-timeout", 2);
                return;
            }

            _failureOutcome = string.Empty;
            _failureExitCode = 1;
            if (!_lifecycle.Tick(
                    DateTime.UtcNow,
                    Backend,
                    HandleLifecycleFailure))
            {
                if (_lifecycle.Status == Ros2ForUnitySelectionLifecycleStatus.Failed)
                    FailAndExit(_failureOutcome, _failureExitCode);
                return;
            }

            DetachCallbacks();
            Debug.Log(
                "PHASE181_BATCH_RUNTIME_SELECTION_READY runtime=" + _runtimePackage
                + " addon=" + _addOnPackage
                + " communicationMode=" + _validatedCommunicationMode
                + " rmw=" + _validatedRmw
                + " registeredEvent=" + _receivedRegisteredPackages);
            ClearPendingSelection();
            EditorApplication.Exit(0);
        }

        private static void ResolvePackageManager()
        {
            _resolveRequested = true;
            PersistPendingSelection();
            Client.Resolve();
        }

        private static bool ValidateSelection()
        {
            Ros2ForUnityRuntimeSelection.InvalidateStatusCache();
            var selection = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                _projectDirectory,
                _runtimePackage);
            if (!selection.IsReady
                || !string.Equals(selection.ActiveAddOnPackage, _addOnPackage, StringComparison.Ordinal))
            {
                _failureOutcome = "post-resolve-validation-failed";
                _failureExitCode = 4;
                return false;
            }

            var status = Ros2ForUnityRuntimeSelection.GetStatus(_projectDirectory);
            var runtime = status.SelectedRuntime;
            if (runtime == null
                || !string.Equals(runtime.PackageName, _runtimePackage, StringComparison.Ordinal)
                || runtime.FindCommunicationMode(_communicationMode) == null)
            {
                _failureOutcome = "communication-mode-unavailable";
                _failureExitCode = 5;
                return false;
            }

            _validatedRuntime = runtime;
            _validatedCommunicationMode = _communicationMode;
            _validatedRmw = Ros2ForUnityRuntimeSelection.GetRmwImplementationForCommunicationMode(
                runtime,
                _communicationMode);
            if (string.IsNullOrWhiteSpace(_validatedRmw))
            {
                _failureOutcome = "communication-mode-unavailable";
                _failureExitCode = 5;
                return false;
            }

            return true;
        }

        private static void CommitSelection()
        {
            Ros2ForUnityRuntimeSelection.SetCommunicationMode(
                _projectDirectory,
                _validatedRuntime,
                _communicationMode);
            var selectedCommunicationMode = Ros2ForUnityRuntimeSelection.GetCommunicationModeForRuntime(
                _validatedRuntime);
            var selectedRmw = Ros2ForUnityRuntimeSelection.GetRmwImplementationForCommunicationMode(
                _validatedRuntime,
                selectedCommunicationMode);
            if (!string.Equals(selectedCommunicationMode, _communicationMode, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(selectedRmw))
            {
                _failureOutcome = "communication-mode-binding-failed";
                _failureExitCode = 6;
                throw new InvalidOperationException("Phase181 communication mode binding failed.");
            }

            _validatedCommunicationMode = selectedCommunicationMode;
            _validatedRmw = selectedRmw;
            Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();
        }

        private static void HandleLifecycleFailure(Exception exception)
        {
            if (string.IsNullOrWhiteSpace(_failureOutcome))
            {
                _failureOutcome = "selection-lifecycle-failed";
                _failureExitCode = 7;
            }
            Debug.LogError(
                "PHASE181_BATCH_RUNTIME_SELECTION_FAIL reason="
                + exception.GetType().Name);
        }

        private static void RestoreOriginalManifestBeforeFailure()
        {
            if (_selectionState == null || string.IsNullOrWhiteSpace(_projectDirectory))
                return;

            try
            {
                Ros2ForUnityCustomTypesupportSelectionTransaction.RestoreManifest(
                    _projectDirectory,
                    _selectionState.OriginalManifest);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "PHASE181_BATCH_RUNTIME_SELECTION_RESTORE_FAILED type="
                    + exception.GetType().Name);
            }
        }

        private static void FailAndExit(string outcome, int exitCode)
        {
            RestoreOriginalManifestBeforeFailure();
            _lifecycle?.Fail();
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
            if (_selectionState == null || _lifecycle == null)
                return;

            SessionState.SetInt(PendingGenerationKey, checked((int)_selectionState.Generation));
            SessionState.SetString(PendingRuntimePackageKey, _runtimePackage);
            SessionState.SetString(PendingAddOnPackageKey, _addOnPackage);
            SessionState.SetString(PendingCommunicationModeKey, _communicationMode);
            SessionState.SetString(PendingOriginalManifestKey, _selectionState.OriginalManifest);
            SessionState.SetString(
                PendingDeadlineUtcTicksKey,
                _selectionState.DeadlineUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            SessionState.SetString(PendingStatusKey, _lifecycle.Status.ToString());
        }

        private static bool TryRestorePendingSelection()
        {
            var runtimePackage = SessionState.GetString(PendingRuntimePackageKey, string.Empty);
            var addOnPackage = SessionState.GetString(PendingAddOnPackageKey, string.Empty);
            var communicationMode = SessionState.GetString(PendingCommunicationModeKey, string.Empty);
            var originalManifest = SessionState.GetString(PendingOriginalManifestKey, string.Empty);
            var statusText = SessionState.GetString(
                PendingStatusKey,
                Ros2ForUnitySelectionLifecycleStatus.Pending.ToString());
            var deadlineTicksText = SessionState.GetString(PendingDeadlineUtcTicksKey, string.Empty);
            var generation = SessionState.GetInt(PendingGenerationKey, 0);
            if (generation <= 0
                || string.IsNullOrWhiteSpace(runtimePackage)
                || string.IsNullOrWhiteSpace(addOnPackage)
                || string.IsNullOrWhiteSpace(communicationMode)
                || string.IsNullOrWhiteSpace(originalManifest)
                || !Enum.TryParse(statusText, out Ros2ForUnitySelectionLifecycleStatus status)
                || !Enum.IsDefined(typeof(Ros2ForUnitySelectionLifecycleStatus), status)
                || status == Ros2ForUnitySelectionLifecycleStatus.Completed
                || status == Ros2ForUnitySelectionLifecycleStatus.Cancelled
                || !long.TryParse(
                    deadlineTicksText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var deadlineTicks))
            {
                ClearPendingSelection();
                return false;
            }

            try
            {
                var snapshot = new Ros2ForUnityInteractiveSelectionState.Snapshot
                {
                    Generation = generation,
                    ProjectDirectory = Ros2ForUnityRuntimeSelection.ProjectDirectoryFromApplication(),
                    RuntimePackage = runtimePackage,
                    AddOnPackage = addOnPackage,
                    OriginalManifest = originalManifest,
                    Kind = Ros2ForUnityInteractiveSelectionKind.Runtime,
                    DeadlineUtc = new DateTime(deadlineTicks, DateTimeKind.Utc)
                };
                if (!Ros2ForUnityInteractiveSelectionState.TryRestore(
                        snapshot,
                        DateTime.UtcNow,
                        out _selectionState))
                {
                    ClearPendingSelection();
                    return false;
                }

                _projectDirectory = snapshot.ProjectDirectory;
                _runtimePackage = runtimePackage;
                _addOnPackage = addOnPackage;
                _communicationMode = communicationMode;
                _lifecycle = new Ros2ForUnityInteractiveSelectionCoordinatorCore();
                _lifecycle.Restore(_selectionState, DateTime.UtcNow, status);
                return true;
            }
            catch (Exception)
            {
                ClearPendingSelection();
                return false;
            }
        }

        private static void ClearPendingSelection()
        {
            SessionState.SetInt(PendingGenerationKey, 0);
            SessionState.SetString(PendingRuntimePackageKey, string.Empty);
            SessionState.SetString(PendingAddOnPackageKey, string.Empty);
            SessionState.SetString(PendingCommunicationModeKey, string.Empty);
            SessionState.SetString(PendingOriginalManifestKey, string.Empty);
            SessionState.SetString(PendingDeadlineUtcTicksKey, string.Empty);
            SessionState.SetString(PendingStatusKey, string.Empty);
            _selectionState = null;
            _lifecycle = null;
            _projectDirectory = null;
            _runtimePackage = null;
            _addOnPackage = null;
            _communicationMode = null;
            _validatedRuntime = null;
            _validatedCommunicationMode = null;
            _validatedRmw = null;
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
