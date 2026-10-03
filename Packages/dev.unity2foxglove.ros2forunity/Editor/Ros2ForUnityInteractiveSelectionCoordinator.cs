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
        private const double ResolveTimeoutSeconds = 300.0;
        private const string PendingGenerationKey = "Unity2Foxglove.R2FU.InteractiveSelection.Generation";
        private const string PendingProjectKey = "Unity2Foxglove.R2FU.InteractiveSelection.Project";
        private const string PendingRuntimeKey = "Unity2Foxglove.R2FU.InteractiveSelection.Runtime";
        private const string PendingAddOnKey = "Unity2Foxglove.R2FU.InteractiveSelection.AddOn";
        private const string PendingKindKey = "Unity2Foxglove.R2FU.InteractiveSelection.Kind";
        private const string PendingOriginalManifestKey = "Unity2Foxglove.R2FU.InteractiveSelection.OriginalManifest";
        private const string PendingDeadlineKey = "Unity2Foxglove.R2FU.InteractiveSelection.DeadlineUtcTicks";

        private static Ros2ForUnityInteractiveSelectionState _pending;

        internal static bool HasPending => _pending != null;

        internal static string PendingMessage
            => _pending == null
                ? string.Empty
                : "Unity is resolving the selected ROS2 For Unity package. The selection is applied only after Package Manager registration completes.";

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!TryRestorePending())
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
            if (_pending != null)
                throw new InvalidOperationException("Another ROS2 For Unity package selection is already resolving.");
            if (string.IsNullOrWhiteSpace(projectDirectory)
                || string.IsNullOrWhiteSpace(runtimePackage)
                || string.IsNullOrEmpty(originalManifest))
            {
                throw new InvalidOperationException("The interactive ROS2 For Unity selection transaction is incomplete.");
            }

            var generation = SessionState.GetInt(PendingGenerationKey, 0) + 1;
            _pending = Ros2ForUnityInteractiveSelectionState.Begin(
                generation,
                projectDirectory,
                runtimePackage,
                addOnPackage,
                originalManifest,
                kind,
                DateTime.UtcNow,
                TimeSpan.FromSeconds(ResolveTimeoutSeconds));
            PersistPending();
            AttachCallbacks();
        }

        internal static void Cancel()
        {
            DetachCallbacks();
            _pending = null;
            ClearPending();
        }

        private static void Update()
        {
            var pending = _pending;
            if (pending == null)
                return;

            if (pending.IsExpired(DateTime.UtcNow))
            {
                Fail(
                    pending,
                    "Package Manager registration timed out; the selected manifest was left in place for the Editor to finish resolving.",
                    rollbackManifest: false);
                return;
            }

            if (!AreSelectedPackagesRegistered(pending))
                return;

            try
            {
                Ros2ForUnityRuntimeSelection.InvalidateStatusCache();
                var selection = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                    pending.ProjectDirectory,
                    pending.RuntimePackage);
                if (selection.Code != Ros2ForUnityCustomTypesupportSelectionCode.Ready
                    && selection.Code != Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly)
                {
                    Fail(pending, "Post-resolve validation failed: " + selection.Code + ".");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(pending.AddOnPackage)
                    && !string.Equals(
                        selection.ActiveAddOnPackage,
                        pending.AddOnPackage,
                        StringComparison.Ordinal))
                {
                    Fail(pending, "Post-resolve validation selected a different custom typesupport add-on.");
                    return;
                }

                var generation = pending.Generation;
                Ros2ForUnityRuntimeSelection.CompleteInteractiveSelection(
                    pending.ProjectDirectory,
                    pending.Kind);
                if (_pending != pending || !pending.TryComplete(generation))
                    return;

                DetachCallbacks();
                _pending = null;
                ClearPending();
            }
            catch (Exception exception)
            {
                Fail(pending, "Post-resolve completion failed: " + exception.GetType().Name + ".");
            }
        }

        private static bool AreSelectedPackagesRegistered(Ros2ForUnityInteractiveSelectionState pending)
        {
            var runtime = PackageManagerPackageInfo.FindForPackageName(pending.RuntimePackage);
            if (runtime == null || !runtime.isDirectDependency)
                return false;

            if (string.IsNullOrWhiteSpace(pending.AddOnPackage))
                return true;

            var addOn = PackageManagerPackageInfo.FindForPackageName(pending.AddOnPackage);
            return addOn != null && addOn.isDirectDependency;
        }

        private static void Fail(
            Ros2ForUnityInteractiveSelectionState pending,
            string reason,
            bool rollbackManifest = true)
        {
            if (_pending != pending || !pending.TryFail(pending.Generation))
                return;

            DetachCallbacks();
            _pending = null;
            ClearPending();
            if (rollbackManifest)
            {
                try
                {
                    Ros2ForUnityCustomTypesupportSelectionTransaction.RestoreManifest(
                        pending.ProjectDirectory,
                        pending.OriginalManifest);
                    Client.Resolve();
                }
                catch (Exception restoreException)
                {
                    Debug.LogError(
                        "ROS2 For Unity package selection failed and manifest rollback also failed: "
                        + restoreException.GetType().Name + ": " + restoreException.Message);
                }
            }

            Debug.LogError("ROS2 For Unity package selection failed: " + reason);
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
            SessionState.SetInt(PendingGenerationKey, checked((int)pending.Generation));
            SessionState.SetString(PendingProjectKey, pending.ProjectDirectory);
            SessionState.SetString(PendingRuntimeKey, pending.RuntimePackage);
            SessionState.SetString(PendingAddOnKey, pending.AddOnPackage);
            SessionState.SetString(PendingKindKey, pending.Kind.ToString());
            SessionState.SetString(PendingOriginalManifestKey, pending.OriginalManifest);
            SessionState.SetString(
                PendingDeadlineKey,
                pending.DeadlineUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryRestorePending()
        {
            var project = SessionState.GetString(PendingProjectKey, string.Empty);
            var runtime = SessionState.GetString(PendingRuntimeKey, string.Empty);
            var addOn = SessionState.GetString(PendingAddOnKey, string.Empty);
            var original = SessionState.GetString(PendingOriginalManifestKey, string.Empty);
            var kindText = SessionState.GetString(PendingKindKey, string.Empty);
            var deadlineText = SessionState.GetString(PendingDeadlineKey, string.Empty);
            var generation = SessionState.GetInt(PendingGenerationKey, 0);
            if (generation <= 0
                || string.IsNullOrWhiteSpace(project)
                || string.IsNullOrWhiteSpace(runtime)
                || string.IsNullOrEmpty(original)
                || !Enum.TryParse(kindText, out Ros2ForUnityInteractiveSelectionKind kind)
                || !long.TryParse(deadlineText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
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

                _pending = restored;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                _pending = null;
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
        }
    }
}
#endif
