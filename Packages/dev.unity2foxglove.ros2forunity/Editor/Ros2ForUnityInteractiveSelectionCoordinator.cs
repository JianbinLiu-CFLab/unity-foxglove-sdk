// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Complete interactive package selection only after Package Manager registration.

#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static class Ros2ForUnityInteractiveSelectionCoordinator
    {
        private const string PendingGenerationKey = "Unity2Foxglove.R2FU.InteractiveSelection.Generation";
        private const string PendingProjectKey = "Unity2Foxglove.R2FU.InteractiveSelection.Project";
        private const string PendingRuntimeKey = "Unity2Foxglove.R2FU.InteractiveSelection.Runtime";
        private const string PendingAddOnKey = "Unity2Foxglove.R2FU.InteractiveSelection.AddOn";
        private const string PendingKindKey = "Unity2Foxglove.R2FU.InteractiveSelection.Kind";
        private const string PendingOriginalManifestKey = "Unity2Foxglove.R2FU.InteractiveSelection.OriginalManifest";
        private const string PendingDeadlineKey = "Unity2Foxglove.R2FU.InteractiveSelection.DeadlineUtcTicks";
        private const string PendingStatusKey = "Unity2Foxglove.R2FU.InteractiveSelection.Status";

        private static Ros2ForUnityInteractiveSelectionState _pending;
        private static Ros2ForUnityInteractiveSelectionCoordinatorCore _lifecycle;

        internal static bool HasPending => _lifecycle != null && _lifecycle.HasOwner;
        internal static bool CanRetry
            => HasPending
                && (_lifecycle.Status == Ros2ForUnitySelectionLifecycleStatus.TimedOut
                    || _lifecycle.Status == Ros2ForUnitySelectionLifecycleStatus.Failed);
        internal static bool CanCancel => HasPending;
        internal static Ros2ForUnitySelectionLifecycleStatus Status
            => _lifecycle == null
                ? Ros2ForUnitySelectionLifecycleStatus.Completed
                : _lifecycle.Status;

        internal static string PendingMessage
        {
            get
            {
                switch (Status)
                {
                    case Ros2ForUnitySelectionLifecycleStatus.TimedOut:
                        return "Package Manager resolution is taking longer than expected. The selected manifest remains owned by this transaction. Retry when Package Manager is ready, or cancel to restore the previous manifest.";
                    case Ros2ForUnitySelectionLifecycleStatus.Failed:
                        return "ROS2 For Unity package selection is waiting for recovery. Retry the resolve, or cancel to restore the previous manifest.";
                    default:
                        return HasPending
                            ? "Unity is resolving the selected ROS2 For Unity package. The selection is applied only after Package Manager registration completes."
                            : string.Empty;
                }
            }
        }

        private sealed class UnitySelectionBackend : IRos2ForUnitySelectionBackend
        {
            public bool IsRegistered(Ros2ForUnityInteractiveSelectionState state)
                => AreSelectedPackagesRegistered(state);

            public bool IsValid(Ros2ForUnityInteractiveSelectionState state)
                => ValidateSelectedPackages(state);

            public void Commit(Ros2ForUnityInteractiveSelectionState state)
                => Ros2ForUnityRuntimeSelection.CompleteInteractiveSelection(
                    state.ProjectDirectory,
                    state.Kind);

            public void Resolve()
            {
                if (_lifecycle != null && _lifecycle.HasOwner)
                {
                    _pending = _lifecycle.State;
                    PersistPending();
                }
                Client.Resolve();
            }

            public void Rollback(Ros2ForUnityInteractiveSelectionState state)
                => Ros2ForUnityCustomTypesupportSelectionTransaction.RestoreManifest(
                    state.ProjectDirectory,
                    state.OriginalManifest);
        }

        private static readonly UnitySelectionBackend Backend =
            new UnitySelectionBackend();

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (Application.isBatchMode || !TryRestorePending())
                return;

            AttachCallbacks();
        }

        internal static void Begin(
            string projectDirectory,
            string runtimePackage,
            string addOnPackage,
            Ros2ForUnityInteractiveSelectionKind kind,
            string originalManifest)
        {
            if (Application.isBatchMode)
                throw new InvalidOperationException("Interactive package selection is unavailable in batch mode.");
            if (HasPending)
                throw new InvalidOperationException("Another ROS2 For Unity package selection is already resolving.");
            if (string.IsNullOrWhiteSpace(projectDirectory)
                || string.IsNullOrWhiteSpace(runtimePackage)
                || string.IsNullOrEmpty(originalManifest))
            {
                throw new InvalidOperationException("The interactive ROS2 For Unity selection transaction is incomplete.");
            }

            var generation = SessionState.GetInt(PendingGenerationKey, 0) + 1;
            var state = Ros2ForUnityInteractiveSelectionState.Begin(
                generation,
                projectDirectory,
                runtimePackage,
                addOnPackage,
                originalManifest,
                kind,
                DateTime.UtcNow,
                Ros2ForUnitySelectionLifecycleDefaults.ResolveTimeout);
            _lifecycle = new Ros2ForUnityInteractiveSelectionCoordinatorCore();
            _lifecycle.Begin(state);
            _pending = state;
            PersistPending();
            AttachCallbacks();
        }

        internal static void Retry()
        {
            if (!CanRetry)
                return;

            DetachCallbacks();
            var retried = _lifecycle.Retry(
                DateTime.UtcNow,
                Ros2ForUnitySelectionLifecycleDefaults.ResolveTimeout,
                Backend,
                HandleLifecycleFailure);
            _pending = _lifecycle.State;
            PersistPending();
            AttachCallbacks();
            if (!retried)
                return;
        }

        internal static void Complete()
        {
            if (!HasPending)
                return;

            if (!_lifecycle.Complete())
                return;
            DetachCallbacks();
            _pending = null;
            _lifecycle = null;
            ClearPending();
        }

        internal static void Cancel()
        {
            if (!HasPending)
                return;

            DetachCallbacks();
            var cancelled = _lifecycle.Cancel(Backend, HandleLifecycleFailure);
            if (!cancelled)
            {
                _pending = _lifecycle.State;
                PersistPending();
                AttachCallbacks();
                return;
            }

            _pending = _lifecycle.State;
            PersistPending();
            try
            {
                Backend.Resolve();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "ROS2 For Unity package-selection rollback completed, but Package Manager refresh failed: "
                    + exception.GetType().Name + ".");
            }
            finally
            {
                _pending = null;
                _lifecycle = null;
                ClearPending();
            }
        }

        private static void Update()
        {
            if (!HasPending || _pending == null)
                return;

            if (_lifecycle.Status == Ros2ForUnitySelectionLifecycleStatus.Failed)
                return;

            if (_lifecycle.MarkTimedOut(DateTime.UtcNow))
            {
                PersistPending();
                Debug.LogWarning(PendingMessage);
                return;
            }

            if (!_lifecycle.Tick(
                    DateTime.UtcNow,
                    Backend,
                    HandleLifecycleFailure))
            {
                if (_lifecycle.Status == Ros2ForUnitySelectionLifecycleStatus.Failed)
                {
                    _pending = _lifecycle.State;
                    PersistPending();
                }
                return;
            }

            DetachCallbacks();
            _pending = null;
            _lifecycle = null;
            ClearPending();
        }

        private static bool AreSelectedPackagesRegistered(
            Ros2ForUnityInteractiveSelectionState pending)
        {
            var runtime = PackageManagerPackageInfo.FindForPackageName(pending.RuntimePackage);
            if (runtime == null || !runtime.isDirectDependency)
                return false;

            if (string.IsNullOrWhiteSpace(pending.AddOnPackage))
                return true;

            var addOn = PackageManagerPackageInfo.FindForPackageName(pending.AddOnPackage);
            return addOn != null && addOn.isDirectDependency;
        }

        private static bool ValidateSelectedPackages(
            Ros2ForUnityInteractiveSelectionState pending)
        {
            Ros2ForUnityRuntimeSelection.InvalidateStatusCache();
            var selection = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                pending.ProjectDirectory,
                pending.RuntimePackage);
            if (selection.Code != Ros2ForUnityCustomTypesupportSelectionCode.Ready
                && selection.Code != Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly)
            {
                throw new InvalidOperationException(
                    "Post-resolve validation failed: " + selection.Code + ".");
            }

            if (!string.IsNullOrWhiteSpace(pending.AddOnPackage)
                && !string.Equals(
                    selection.ActiveAddOnPackage,
                    pending.AddOnPackage,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Post-resolve validation selected a different custom typesupport add-on.");
            }

            return true;
        }

        private static void HandleLifecycleFailure(Exception exception)
        {
            if (_lifecycle == null || !_lifecycle.HasOwner)
                return;

            _pending = _lifecycle.State;
            PersistPending();
            Debug.LogError(
                "ROS2 For Unity package selection failed: "
                + exception.GetType().Name + ".");
        }

        internal static void DrawPendingResolveControls()
        {
            if (!HasPending)
                return;

            var type = Status == Ros2ForUnitySelectionLifecycleStatus.Pending
                ? MessageType.Info
                : MessageType.Warning;
            EditorGUILayout.HelpBox(PendingMessage, type);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!CanRetry))
                {
                    if (GUILayout.Button("Retry package resolution"))
                        Retry();
                }

                using (new EditorGUI.DisabledScope(!CanCancel))
                {
                    if (GUILayout.Button("Cancel and restore previous selection"))
                        Cancel();
                }
            }
        }

        private static void OnPackagesRegistered(PackageRegistrationEventArgs _)
        {
            EditorApplication.delayCall -= Update;
            EditorApplication.delayCall += Update;
        }

        private static void AttachCallbacks()
        {
            Events.registeredPackages -= OnPackagesRegistered;
            Events.registeredPackages += OnPackagesRegistered;
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        private static void DetachCallbacks()
        {
            EditorApplication.update -= Update;
            Events.registeredPackages -= OnPackagesRegistered;
        }

        private static void PersistPending()
        {
            var pending = _pending;
            if (pending == null || _lifecycle == null)
                return;

            SessionState.SetInt(PendingGenerationKey, checked((int)pending.Generation));
            SessionState.SetString(PendingProjectKey, pending.ProjectDirectory);
            SessionState.SetString(PendingRuntimeKey, pending.RuntimePackage);
            SessionState.SetString(PendingAddOnKey, pending.AddOnPackage);
            SessionState.SetString(PendingKindKey, pending.Kind.ToString());
            SessionState.SetString(PendingOriginalManifestKey, pending.OriginalManifest);
            SessionState.SetString(
                PendingDeadlineKey,
                pending.DeadlineUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            SessionState.SetString(PendingStatusKey, _lifecycle.Status.ToString());
        }

        private static bool TryRestorePending()
        {
            var project = SessionState.GetString(PendingProjectKey, string.Empty);
            var runtime = SessionState.GetString(PendingRuntimeKey, string.Empty);
            var addOn = SessionState.GetString(PendingAddOnKey, string.Empty);
            var original = SessionState.GetString(PendingOriginalManifestKey, string.Empty);
            var kindText = SessionState.GetString(PendingKindKey, string.Empty);
            var statusText = SessionState.GetString(
                PendingStatusKey,
                Ros2ForUnitySelectionLifecycleStatus.Pending.ToString());
            var deadlineText = SessionState.GetString(PendingDeadlineKey, string.Empty);
            var generation = SessionState.GetInt(PendingGenerationKey, 0);
            if (generation <= 0
                || string.IsNullOrWhiteSpace(project)
                || string.IsNullOrWhiteSpace(runtime)
                || string.IsNullOrEmpty(original)
                || !Enum.TryParse(kindText, out Ros2ForUnityInteractiveSelectionKind kind)
                || !Enum.IsDefined(typeof(Ros2ForUnityInteractiveSelectionKind), kind)
                || !Enum.TryParse(
                    statusText,
                    out Ros2ForUnitySelectionLifecycleStatus status)
                || !Enum.IsDefined(typeof(Ros2ForUnitySelectionLifecycleStatus), status)
                || status == Ros2ForUnitySelectionLifecycleStatus.Completed
                || status == Ros2ForUnitySelectionLifecycleStatus.Cancelled
                || !long.TryParse(
                    deadlineText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ticks))
            {
                ClearPending();
                return false;
            }

            try
            {
                var snapshot = new Ros2ForUnityInteractiveSelectionState.Snapshot
                {
                    Generation = generation,
                    ProjectDirectory = project,
                    RuntimePackage = runtime,
                    AddOnPackage = addOn,
                    OriginalManifest = original,
                    Kind = kind,
                    DeadlineUtc = new DateTime(ticks, DateTimeKind.Utc)
                };
                if (!Ros2ForUnityInteractiveSelectionState.TryRestore(
                        snapshot,
                        DateTime.UtcNow,
                        out var restored))
                {
                    ClearPending();
                    return false;
                }

                _lifecycle = new Ros2ForUnityInteractiveSelectionCoordinatorCore();
                _lifecycle.Restore(restored, DateTime.UtcNow, status);
                _pending = restored;
                PersistPending();
                return true;
            }
            catch (Exception)
            {
                _pending = null;
                _lifecycle = null;
                ClearPending();
                return false;
            }
        }

        private static void ClearPending()
        {
            SessionState.SetString(PendingProjectKey, string.Empty);
            SessionState.SetString(PendingRuntimeKey, string.Empty);
            SessionState.SetString(PendingAddOnKey, string.Empty);
            SessionState.SetString(PendingKindKey, string.Empty);
            SessionState.SetString(PendingOriginalManifestKey, string.Empty);
            SessionState.SetString(PendingDeadlineKey, string.Empty);
            SessionState.SetString(PendingStatusKey, string.Empty);
        }
    }
}
#endif
